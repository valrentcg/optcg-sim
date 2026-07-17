using System;
using System.Collections.Generic;
using System.Linq;

namespace OnePieceTcg.Engine.Bot.Search
{
    /// <summary>
    /// General, MECHANICAL "resolve this [Trigger] now vs. take the card into hand" evaluator for the
    /// battle Trigger step. This replaces the noisy full-game rollout that both advanced paths used to make
    /// the use/pass call — a rollout washes out the marginal value of a defensive or timed Trigger, so
    /// beneficial Triggers (a +power buff that stops an attack, a free blocker, a KO, a draw) were being
    /// declined and the Life card thrown away, or worthless ones fired for nothing.
    ///
    /// The comparison the rules actually pose is:  value of RESOLVING the Trigger now  vs.  value of the
    /// revealed card entering hand (its [Counter] plus generic card advantage). We estimate the USE side by
    /// SIMULATING it — clone, apply useTrigger, drive only the Trigger's own resolution to a stable point —
    /// then reading the board/tempo/resource DELTA the Trigger produced. Everything is printed-text/mechanical
    /// (removal, body, power, Life, DON, draw, paid cost); there are NO card-id special cases, so the same
    /// judgement applies to every deck that exposes the mechanic.
    ///
    /// OBSERVATION BOUNDARY: every input is information the DEFENDING seat legitimately has. The revealed
    /// Life card is our own; board power, opposing bodies, rested/active status, remaining attackers, and the
    /// current attack's power are all public; a draw is only COUNTED, never read; and a search ([DeckLook])
    /// is not driven at all (we would have to select from the true deck order) — it is credited generically.
    /// So this policy never consults the opponent's hand, our face-down Life, or deck order.
    /// </summary>
    public static class TriggerUtilityPolicy
    {
        // Utility units: ~1.0 == one card in hand == 1000 power == a small tempo swing. Constants are
        // deliberately interpretable rather than evolved; they were calibrated against the mechanical
        // fixtures and a mixed-starter batch, not fitted to individual cards.
        private const double Card = 0.9;          // generic value of one extra card in hand
        private const double CounterPerK = 0.5;   // value of 1000 printed [Counter] on the held card
        private const double RemovalPerInvest = 0.7;  // per (cost + power/1000) of an opposing body removed
        private const double RestPerAttacker = 0.45;  // per opposing attacker merely disabled (rested), not removed
        private const double BodyPerInvest = 0.5;     // per (cost + power/1000) of a friendly body created
        private const double BlockerBonus = 0.4;      // a fresh Blocker while attacks remain
        private const double LeaderHitMitig = 1.6;    // stopping an attack that would deal Life damage (one Life)
        private const double CharHitMitigPerInvest = 0.55; // saving one of my Characters from the current attack
        private const double DefPowerPerK = 0.2;      // residual +power (per 1000) that shrinks future attacks
        private const double CarryPerK = 0.45;        // +power (per 1000) that persists into my next turn
        private const double LifePerCard = 1.4;       // one Life gained
        private const double DonPer = 0.22;           // one extra ACTIVE DON!! (counter mana / activation fuel)
        private const double DrawPerCard = 0.85;      // one card drawn
        private const double SearchBonus = 1.0;       // a look/search resolved (targeted card selection)
        private const double CostPerHand = 0.9;       // one card paid from hand
        private const double CostPerLife = 1.5;       // one card paid from Life
        private const double Hysteresis = 0.15;       // don't burn a Life card for a negligible net gain
        private const int MaxResolveSteps = 12;       // cap on driving the Trigger's own follow-up resolution

        /// <summary>True to fire the Trigger (useTrigger), false to take the card into hand (passTrigger).
        /// Only meaningful at the battle Trigger step for the defending seat; returns false otherwise.</summary>
        public static bool ShouldUse(GameState state, string seat)
        {
            var battle = state?.Battle;
            if (battle == null || battle.Step != "trigger" || battle.TargetSeat != seat) return false;
            var revealed = battle.RevealedLife;
            var def = revealed == null ? null : GameEngine.GetCard(revealed);
            if (def == null || string.IsNullOrEmpty(def.Trigger)) return false;

            // Provable-zero-value cases (no legal opposing target, full board for "play this card",
            // owner-agnostic bounce with only friendly targets) are already handled — and regression
            // tested — by the shared guard. Keep it as a cheap NECESSARY pre-filter; it never forces a
            // USE, only a fast decline, so the value comparison below decides everything it lets through.
            if (!IntermediateBot.ShouldUseTrigger(state, seat)) return false;

            double passUtility = Card + Math.Max(0, GameEngine.GetCounterPower(revealed)) / 1000.0 * CounterPerK;
            double useUtility = ScoreUseLine(state, seat, out bool preventsLethal);

            if (preventsLethal) return true;                 // Life is the win condition; never decline survival.
            return useUtility > passUtility + Hysteresis;
        }

        /// <summary>Simulate the useTrigger line on a clone, resolve only the Trigger's own follow-ups, and
        /// score the resulting board/tempo/resource delta from <paramref name="seat"/>'s view.</summary>
        private static double ScoreUseLine(GameState state, string seat, out bool preventsLethal)
        {
            preventsLethal = false;
            string opp = GameEngine.OtherSeat(seat);
            var battle = state.Battle;

            // The current, live attack — used for immediate-mitigation and lethal detection. Read the
            // attacker's power off the card (Battle.AttackPower is not populated this early) so this works
            // whether or not the engine has computed it yet.
            var attackerBefore = FindOnBoard(state, opp, battle.AttackerId);
            var targetBefore = FindOnBoard(state, seat, battle.TargetId);
            int attackPow = attackerBefore == null ? 0 : GameEngine.GetPower(state, attackerBefore);
            bool targetIsLeader = targetBefore != null && state.Players[seat].Leader != null
                && targetBefore.InstanceId == state.Players[seat].Leader.InstanceId;
            bool connectsBefore = targetBefore != null && attackPow >= GameEngine.GetPower(state, targetBefore);

            var clone = GameClone.Clone(state);
            GameEngine.ApplyCommand(clone, new GameCommand { Type = "useTrigger", Seat = seat });
            // If the engine ever no-ops the use (it shouldn't — the pre-filter already rejected provable
            // no-ops), the deltas below stay ~0 and the caller safely falls back to taking the card.
            bool searched = DriveTriggerResolution(clone, seat);

            var meB = state.Players[seat]; var meA = clone.Players[seat];
            var opB = state.Players[opp]; var opA = clone.Players[opp];

            double u = 0;

            // --- Removal of the opponent's board (KO, bounce) ---
            // Count only bodies that actually LEFT the board, valued at their real investment. A temporary
            // power debuff on a body that survives is NOT removal (that value, if any, shows up as
            // mitigation below when it stops an attack) — so a "-5000 during this turn" on a spent Character
            // must score zero here, not its 5000 power drop.
            int removedInvestment = 0, removedBodies = 0;
            foreach (var c in opB.CharacterArea)
                if (c != null && FindOnBoard(clone, opp, c.InstanceId) == null)
                {
                    var d = GameEngine.GetCard(c);
                    if (d != null) { removedInvestment += d.Cost * 1000 + GameEngine.GetPower(state, c); removedBodies++; }
                }
            u += RemovalPerInvest * removedInvestment / 1000.0;
            int disabled = Math.Max(0, ActiveAttackers(state, opB) - ActiveAttackers(clone, opA));
            // Don't double-count bodies that were removed outright; only extra rests beyond removed count.
            u += RestPerAttacker * Math.Max(0, disabled - removedBodies);

            // --- Friendly body created ("Play this card") ---
            var newBodies = meA.CharacterArea.Where(c => c != null
                && meB.CharacterArea.All(o => o?.InstanceId != c.InstanceId)).ToList();
            int remainingAttacks = ActiveAttackers(state, opB);
            foreach (var b in newBodies)
            {
                var bd = GameEngine.GetCard(b);
                if (bd == null) continue;
                u += BodyPerInvest * (bd.Cost + GameEngine.GetPower(clone, b) / 1000.0);
                if (remainingAttacks > 0 && GameEngine.HasBlocker(clone, b)) u += BlockerBonus;
            }

            // --- Power change on cards that exist in BOTH states (timed / instant buffs) ---
            double powerGainK = 0;
            foreach (var c in meB.CharacterArea.Where(c => c != null)
                         .Concat(meB.Leader == null ? Enumerable.Empty<CardInstance>() : new[] { meB.Leader }))
            {
                var after = FindOnBoard(clone, seat, c.InstanceId);
                if (after == null) continue;
                int d = GameEngine.GetPower(clone, after) - GameEngine.GetPower(state, c);
                if (d > 0) powerGainK += d / 1000.0;
            }

            // Carry: a bonus that survives to my next turn is offence too (Kong Gatling), independent of any
            // defence it also provides now — so credit it on the FULL power gain before the mitigation split.
            if (CrossTurnBuffAdded(state, clone, seat) && powerGainK > 0) u += CarryPerK * powerGainK;

            // Immediate mitigation: did the buff (or removing the attacker) stop THIS attack connecting?
            bool attackerGone = FindOnBoard(clone, opp, battle.AttackerId) == null;
            var targetAfter = targetBefore == null ? null : FindOnBoard(clone, seat, targetBefore.InstanceId);
            bool connectsAfter = !attackerGone && targetAfter != null
                && attackPow >= GameEngine.GetPower(clone, targetAfter);
            bool lifeSaver = connectsBefore && !connectsAfter;
            if (lifeSaver)
            {
                if (targetIsLeader)
                {
                    int lifeHit = attackerBefore != null && GameEngine.HasDoubleAttack(state, attackerBefore) ? 2 : 1;
                    u += LeaderHitMitig * lifeHit;
                    if (meB.Life.Count <= lifeHit) preventsLethal = true;
                    powerGainK = Math.Max(0, powerGainK - 1.0); // the ~1000 that flipped the attack is booked here
                }
                else if (targetBefore != null)
                {
                    var td = GameEngine.GetCard(targetBefore);
                    if (td != null) u += CharHitMitigPerInvest * (td.Cost + GameEngine.GetPower(state, targetBefore) / 1000.0);
                }
            }

            // Residual power: still useful against the NEXT attacker, capped by how many attacks remain.
            u += DefPowerPerK * powerGainK * Math.Min(3, Math.Max(remainingAttacks, connectsBefore ? 1 : 0));

            // --- Life / DON / cards, minus paid costs ---
            u += LifePerCard * (meA.Life.Count - meB.Life.Count);
            u += DonPer * (GameEngine.ActiveDonCount(meA) - GameEngine.ActiveDonCount(meB));
            if (meB.Life.Count > meA.Life.Count && meB.Life.Count <= 1) preventsLethal = false; // paying Life is not survival

            int handDelta = meA.Hand.Count - meB.Hand.Count;   // draws add, discard costs subtract
            if (handDelta > 0) u += DrawPerCard * handDelta;
            else if (handDelta < 0) u += CostPerHand * handDelta; // handDelta<0 → subtracts
            if (searched) u += SearchBonus;

            // Life paid as a cost (top-of-Life to hand/trash effects) — count only losses beyond the
            // revealed Trigger card itself, which always leaves Life whichever line we pick.
            int lifePaid = (meB.Life.Count - meA.Life.Count) - 1;
            if (lifePaid > 0) u -= CostPerLife * lifePaid;

            // Life GAINED case handled above; if we neither gained nor paid, no adjustment.
            return u;
        }

        /// <summary>Drive ONLY the Trigger's own resolution (its pending effect / A-B choice) on the clone,
        /// using the incumbent's target logic, so the board delta reflects the actual outcome. Stops the
        /// instant control would leave the Trigger (the battle would advance) and refuses to resolve a
        /// [DeckLook] — selecting from the true deck order is a peek. Returns true if a look/search appeared.</summary>
        private static bool DriveTriggerResolution(GameState clone, string seat)
        {
            var bl = new HashSet<string>();
            for (int i = 0; i < MaxResolveSteps; i++)
            {
                if (clone.DeckLook != null && clone.DeckLook.Seat == seat) return true; // don't peek; credit generically
                bool mine = (clone.ActiveChoice != null && clone.ActiveChoice.Seat == seat)
                    || clone.PendingEffects.Any(e => e.Seat == seat);
                if (!mine) break;
                var cmd = IntermediateBot.DecideOneCommand(clone, seat, bl);
                if (cmd == null || cmd.Type == "passTrigger" || cmd.Type == "useTrigger"
                    || cmd.Type == "declareAttack" || cmd.Type == "resolveAttack") break;
                object snap = IntermediateBot.SnapshotFor(clone, cmd);
                GameEngine.ApplyCommand(clone, cmd);
                if (!IntermediateBot.Succeeded(clone, cmd, snap)) bl.Add(IntermediateBot.Signature(cmd));
            }
            return false;
        }

        // A newly-added power source (timed bonus or base-power override) whose duration reaches the
        // deciding seat's own next turn — i.e. offence, not just this-turn defence.
        private static bool CrossTurnBuffAdded(GameState before, GameState after, string seat)
        {
            int beforeCount = before.TimedPowerBonuses.Count(b => b.OwnerSeat == seat && Persists(b.Duration));
            int afterCount = after.TimedPowerBonuses.Count(b => b.OwnerSeat == seat && Persists(b.Duration));
            return afterCount > beforeCount;
        }
        private static bool Persists(string duration) => string.IsNullOrEmpty(duration)
            || duration.IndexOf("endOfNextTurn", StringComparison.OrdinalIgnoreCase) >= 0
            || duration.IndexOf("permanent", StringComparison.OrdinalIgnoreCase) >= 0;

        // --- Public-board measurements (observation-safe) ---
        private static int ActiveAttackers(GameState s, PlayerState p)
        {
            int n = p.CharacterArea.Count(c => c != null && !c.Rested && !GameEngine.IsSummoningSick(s, c));
            if (p.Leader != null && !p.Leader.Rested) n++;
            return n;
        }
        private static CardInstance FindOnBoard(GameState s, string seat, string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId) || !s.Players.ContainsKey(seat)) return null;
            var p = s.Players[seat];
            if (p.Leader?.InstanceId == instanceId) return p.Leader;
            var c = p.CharacterArea.FirstOrDefault(x => x != null && x.InstanceId == instanceId);
            if (c != null) return c;
            if (p.Stage?.InstanceId == instanceId) return p.Stage;
            return null;
        }
    }
}
