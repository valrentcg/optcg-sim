// One Piece TCG - Engine: rule-based "intermediate" bot.
// Pure C#, no UnityEngine dependency — sits directly on top of GameEngine's public API,
// the same surface the human UI (GameManager.cs) drives.
//
// Turn-sequencing heuristics below (deploy -> attach DON!! -> attack -> end turn, dumping
// leftover active DON!! onto the lead attacker right before declaring) were validated against
// ~20 real high-ranked-ladder OPTCGSim replays (OCR-extracted action logs), which consistently
// show that exact ordering. This is a first pass: single-ply, no lookahead, no opponent-hand
// modeling. It is meant to be a competent, non-random practice opponent — not a solved player.
//
// Robustness note: GameEngine is ~8800 lines of card-specific rules text-matching, and its
// target-legality checks aren't all centralized in IsValidEffectTarget — some (e.g. Jinbe
// ST01-005's "other than this card" self-exclusion) live only inside the resolver and reject
// a command without consuming the pending effect. A naive bot that always re-picks its
// highest-scoring candidate can loop on that forever. Every decision point here is therefore
// guarded by a per-call blacklist of command "signatures" checked against a precise per-command-
// type before/after success test (SnapshotFor/ChangedFor below) — NOT "did EventLog grow",
// since rejections still Log() a message, which would make every retry look like progress.
// A rejected pick is excluded and the bot falls through to its next-best option (ultimately:
// pass/end turn) instead of retrying indefinitely. This caught two real bugs during testing:
// the Jinbe self-target loop, and DecideDeckLook sending deckLookConfirmOrder for a "scry"-step
// effect (Bartholomew Kuma ST03-010) instead of deckLookScryConfirm — found via a 435-game
// all-starter-decks batch run, not the original st01/st02-only testing.
//
// Usage:
//   IntermediateBot.TakeAllAvailableActions(state, "south");   // acts for one seat until it
//                                                               // must yield (opponent's turn,
//                                                               // waiting on a defense decision).
//   IntermediateBot.PlayFullMatch(state);                      // drives BOTH seats until the
//                                                               // match finishes (self-play).
//   IntermediateBot.DecideOneCommand(...) / Snapshot / Succeeded / Signature  // single-step
//                                                               // hooks for a host that wants
//                                                               // to apply each command itself
//                                                               // (e.g. GameManager.Dispatch,
//                                                               // for UI/animation/network sync)
//                                                               // one "think tick" at a time —
//                                                               // see GameManager.IntermediateAiTick.

using System;
using System.Collections.Generic;
using System.Linq;

namespace OnePieceTcg.Engine.Bot
{
    public static class IntermediateBot
    {
        // ---- Public entry points -------------------------------------------------

        /// <summary>Applies commands for <paramref name="seat"/> until it has no further
        /// decision to make right now (opponent's turn, or waiting on the opponent's
        /// block/counter/trigger). Returns the number of commands applied.</summary>
        public static int TakeAllAvailableActions(GameState state, string seat, int maxCommands = 400)
        {
            int applied = 0;
            var blacklist = new HashSet<string>();
            for (int i = 0; i < maxCommands; i++)
            {
                var cmd = DecideNextCommand(state, seat, blacklist);
                if (cmd == null) break;
                object before = SnapshotFor(state, cmd);
                GameEngine.ApplyCommand(state, cmd);
                applied++;
                if (!ChangedFor(state, cmd, before))
                    blacklist.Add(Sig(cmd));
            }
            return applied;
        }

        // Rejections inside GameEngine still call Log(...), so "did EventLog grow" is NOT a
        // reliable success signal (that's exactly what caused the Jinbe self-target infinite
        // loop during testing — the rejection message itself grew the log every retry). Each
        // candidate-selection command type gets a precise before/after check instead.
        //
        // Public so a host driving one command at a time (GameManager.IntermediateAiTick) can
        // reuse the exact same success check around its own Dispatch() call instead of
        // reimplementing it: snapshot = SnapshotFor(...); Dispatch(cmd); if (!Succeeded(...)) blacklist.Add(Signature(cmd)).
        public static object SnapshotFor(GameState state, GameCommand cmd)
        {
            switch (cmd.Type)
            {
                case "attachDon":
                    return GameEngine.ActiveDonCount(state.Players[cmd.Seat]);
                case "activateMain":
                {
                    // Composite state fingerprint — an activation that no-ops (already used, not
                    // actually activatable) leaves all of these unchanged, so it gets blacklisted.
                    var pp = state.Players[cmd.Seat];
                    long f = state.PendingEffects.Count * 1_000_000L
                             + pp.AbilityUsedThisTurn.Count * 10_000L
                             + GameEngine.ActiveDonCount(pp) * 100L + pp.Hand.Count;
                    return f;
                }
                case "deckLookSelect":
                    return state.DeckLook?.Cards.Count ?? -1;
                case "deckLookConfirmOrder":
                case "deckLookScryConfirm":
                    // Reference, not a count: a rejected confirm leaves DeckLook as the exact
                    // same object (ResolveDeckLookConfirmOrder/ScryConfirm return early without
                    // touching it — e.g. wrong Step, as happened when this bot used to send
                    // deckLookConfirmOrder for a "scry"-step effect and silently stalled).
                    return state.DeckLook;
                default:
                    return null;
            }
        }

        public static bool Succeeded(GameState state, GameCommand cmd, object before) => ChangedFor(state, cmd, before);

        private static bool ChangedFor(GameState state, GameCommand cmd, object before)
        {
            switch (cmd.Type)
            {
                case "resolveEffect":
                    return !state.PendingEffects.Any(e => e.EffectId == cmd.EffectId);
                case "playCard":
                case "counterWithCard":
                    return !state.Players[cmd.Seat].Hand.Any(c => c.InstanceId == cmd.InstanceId);
                case "attachDon":
                    return GameEngine.ActiveDonCount(state.Players[cmd.Seat]) != (int)before;
                case "activateMain":
                {
                    var pp = state.Players[cmd.Seat];
                    long f = state.PendingEffects.Count * 1_000_000L
                             + pp.AbilityUsedThisTurn.Count * 10_000L
                             + GameEngine.ActiveDonCount(pp) * 100L + pp.Hand.Count;
                    return f != (long)before;
                }
                case "declareAttack":
                    return state.Battle != null && state.Battle.AttackerId == cmd.Attacker;
                case "blockAttack":
                    return state.Battle != null && state.Battle.TargetId == cmd.Blocker;
                case "deckLookSelect":
                    return state.DeckLook == null || state.DeckLook.Cards.Count != (int)before;
                case "deckLookConfirmOrder":
                case "deckLookScryConfirm":
                    return !ReferenceEquals(state.DeckLook, before);
                case "endTurn":
                    // Success = it's genuinely no longer this seat's active main phase — covers
                    // both the ordinary case (ActiveSeat flips to the opponent) and the rarer
                    // one where ending the turn immediately finishes the match.
                    return state.Status == "finished" || state.ActiveSeat != cmd.Seat || state.Phase != "main";
                default:
                    return true; // remaining system/progression commands (passX, resolveAttack, useTrigger, ...) — not candidate loops, and not reachable while blocked the way endTurn was.
            }
        }

        /// <summary>Self-play driver: alternates both seats until the match finishes.
        /// Intended for bot-vs-bot practice generation. Guards against engine deadlocks
        /// with a total-command safety cap.</summary>
        public static void PlayFullMatch(GameState state, int maxTotalCommands = 20000)
        {
            int total = 0;
            while (state.Status != "finished" && total < maxTotalCommands)
            {
                int south = TakeAllAvailableActions(state, "south", maxTotalCommands - total);
                total += south;
                int north = TakeAllAvailableActions(state, "north", maxTotalCommands - total);
                total += north;
                if (south == 0 && north == 0)
                {
                    // Neither seat found a legal action — either the match ended between
                    // checks or both bots are stuck waiting on each other. Bail out rather
                    // than spin forever.
                    break;
                }
            }
        }

        private static string Sig(GameCommand c) =>
            string.Join("|", c.Type, c.Seat, c.InstanceId, c.Target, c.Attacker, c.Blocker, c.EffectId, c.Amount);

        /// <summary>Public alias of the same signature <see cref="TakeAllAvailableActions"/> uses
        /// internally to key its blacklist — expose it so a host-owned blacklist (e.g.
        /// GameManager's per-turn <c>aiTriedThisTurn</c> set) stays in the exact same key space.</summary>
        public static string Signature(GameCommand cmd) => Sig(cmd);

        // ---- Top-level decision dispatch ------------------------------------------

        /// <summary>Decides the single next command for <paramref name="seat"/> without applying
        /// it — for a host (GameManager) that wants to drive its own Dispatch() per command for
        /// UI/animation/network sync, one "think tick" at a time, instead of the batch
        /// <see cref="TakeAllAvailableActions"/>. Returns null when there's nothing to do right
        /// now (opponent's turn, or waiting on their defense decision). <paramref name="blacklist"/>
        /// should persist across ticks for as long as the same decision context is live (a
        /// per-turn set, cleared on turn change, mirrors what the batch method does per-call).</summary>
        public static GameCommand DecideOneCommand(GameState state, string seat, HashSet<string> blacklist) =>
            DecideNextCommand(state, seat, blacklist);

        private static GameCommand DecideNextCommand(GameState state, string seat, HashSet<string> blacklist)
        {
            if (state == null || !state.Players.ContainsKey(seat)) return null;

            if (state.Status == "coinflip")
            {
                if (state.CoinFlipWinner == seat)
                    return Try(blacklist, new GameCommand { Type = "chooseTurnOrder", Seat = seat, GoingFirst = true });
                return null;
            }

            if (state.Status == "mulligan")
            {
                var p = state.Players[seat];
                if (p.MulliganDecided) return null;
                return Try(blacklist, new GameCommand { Type = "mulliganDecision", Seat = seat, Mulligan = ShouldMulligan(p) });
            }

            if (state.Status != "active") return null;

            // Bug found via a 435-game all-starter-decks batch run (game "st07 vs st01"): the
            // engine's IsTurnPlayerInMain gate requires PendingEffects.Count == 0 AND
            // DeckLook == null GLOBALLY — not just "no effect/look belonging to me". Falling
            // through to DecideMainPhase while the OTHER seat has an unresolved effect meant
            // every main-phase command (including the endTurn fallback, which bypasses Try()/
            // the blacklist entirely) silently no-opped forever: declareAttack/attachDon got
            // blacklisted after one try each, but endTurn kept getting re-issued and rejected
            // every single loop iteration, burning the whole command budget without ever
            // yielding back to the seat that actually owns the blocking effect.
            if (state.ActiveChoice != null && state.ActiveChoice.Seat != seat) return null;

            if (state.ActiveChoice != null && state.ActiveChoice.Seat == seat)
            {
                var a = Try(blacklist, new GameCommand { Type = "resolveChoice", Seat = seat, Target = "A" });
                if (a != null) return a;
                return Try(blacklist, new GameCommand { Type = "resolveChoice", Seat = seat, Target = "B" });
            }

            if (state.DeckLook != null && state.DeckLook.Seat != seat) return null;
            if (state.DeckLook != null && state.DeckLook.Seat == seat)
                return DecideDeckLook(state, seat, blacklist);

            var myEffect = state.PendingEffects.FirstOrDefault(e => e.Seat == seat);
            if (myEffect != null)
                return DecideEffect(state, seat, myEffect, blacklist);
            if (state.PendingEffects.Count > 0) return null; // some other seat's effect is blocking everyone.

            if (state.Battle != null)
            {
                if (state.Battle.TargetSeat == seat)
                    return DecideDefense(state, seat, blacklist);
                return null; // I'm the attacker; waiting on the defender.
            }

            if (state.ActiveSeat == seat && state.Phase == "main")
                return DecideMainPhase(state, seat, blacklist);

            return null;
        }

        /// <summary>Returns <paramref name="cmd"/> unless its signature is already known to be
        /// a no-op, in which case null (letting the caller fall through to the next option).</summary>
        private static GameCommand Try(HashSet<string> blacklist, GameCommand cmd) =>
            blacklist.Contains(Sig(cmd)) ? null : cmd;

        // ---- Setup ------------------------------------------------------------

        private static bool ShouldMulligan(PlayerState p)
        {
            if (p.Hand.Count == 0) return false;
            int cheapPlays = p.Hand.Count(c => (CardData.GetCard(c.CardId)?.Cost ?? 99) <= 2);
            double avgCost = p.Hand.Average(c => CardData.GetCard(c.CardId)?.Cost ?? 5);
            // No early plays at all, or a hand that's generally too expensive to develop from.
            return cheapPlays == 0 || avgCost > 4.5;
        }

        // ---- Pending effect / choice resolution --------------------------------

        private static GameCommand DecideEffect(GameState state, string seat, PendingEffect effect, HashSet<string> blacklist)
        {
            var candidates = CandidatesForZone(state, seat, effect.TargetZone)
                .Where(c => GameEngine.IsValidEffectTarget(state, effect, c))
                .ToList();

            // "Return ... to the owner's hand" can legally select either board, but it is still a
            // removal effect. The old scorer treated it as beneficial and therefore selected the
            // highest-value friendly card (often a Boa Hancock blocker). Prefer an opposing target;
            // when none exists, decline instead of damaging our own board.
            if (IsOwnerAgnosticBounce(effect.Text))
            {
                candidates = candidates.Where(c => c.Owner != seat).ToList();
                if (candidates.Count == 0)
                    return new GameCommand { Type = "passEffect", Seat = seat, EffectId = effect.EffectId };
            }

            bool negative = IsNegativeEffectText(effect.Text);
            var ranked = candidates
                .Select(c =>
                {
                    bool ownedBySelf = c.Owner == seat;
                    bool wantMax = (ownedBySelf && !negative) || (!ownedBySelf && negative);
                    double v = Value(state, c);
                    return (card: c, score: wantMax ? v : -v);
                })
                .OrderByDescending(x => x.score);

            foreach (var (card, _) in ranked)
            {
                var cmd = Try(blacklist, new GameCommand { Type = "resolveEffect", Seat = seat, EffectId = effect.EffectId, Target = card.InstanceId });
                if (cmd != null) return cmd;
            }
            return new GameCommand { Type = "passEffect", Seat = seat, EffectId = effect.EffectId };
        }

        private static bool IsNegativeEffectText(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            string t = text.ToLowerInvariant();
            return t.Contains("k.o.") || t.Contains("trash") || t.Contains("rest ") ||
                   t.Contains("cannot") || t.Contains("-1000") || t.Contains("−1000") ||
                   t.Contains("discard") || System.Text.RegularExpressions.Regex.IsMatch(t, @"[-−]\d");
        }

        private static bool IsOwnerAgnosticBounce(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            return text.IndexOf("return", StringComparison.OrdinalIgnoreCase) >= 0
                && text.IndexOf("owner's hand", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static IEnumerable<CardInstance> CandidatesForZone(GameState state, string seat, EffectTargetZone zone)
        {
            var mine = state.Players[seat];
            var theirs = state.Players[GameEngine.OtherSeat(seat)];
            IEnumerable<CardInstance> Board(PlayerState p)
            {
                if (p.Leader != null) yield return p.Leader;
                foreach (var c in p.CharacterArea) if (c != null) yield return c;
                if (p.Stage != null) yield return p.Stage;
            }
            switch (zone)
            {
                case EffectTargetZone.Hand:
                    return mine.Hand;
                case EffectTargetZone.Trash:
                    return mine.Trash;
                case EffectTargetZone.Any:
                    return Board(mine).Concat(Board(theirs)).Concat(mine.Hand).Concat(mine.Trash);
                case EffectTargetZone.Play:
                default:
                    return Board(mine).Concat(Board(theirs));
            }
        }

        private static double Value(GameState state, CardInstance card)
        {
            var def = GameEngine.GetCard(card);
            if (def == null) return 0;
            // Cost dominates (bigger investment = more important card); power breaks ties.
            double v = def.Cost * 1000 + GameEngine.GetPower(state, card);
            // Keyword-aware: a Blocker walls attacks, Double Attack racers hit twice, Rush is tempo —
            // so they're worth more to keep (and worth more to remove from the opponent).
            if (GameEngine.HasBlocker(state, card)) v += 1500;
            if (GameEngine.HasDoubleAttack(state, card)) v += 1500;
            if (GameEngine.HasRush(state, card)) v += 500;
            return v;
        }

        // DON!! needed for `atk` (at its CURRENT power, which already includes attached DON!! on your
        // turn) to reach `targetPower` — i.e. to win the battle (attacker power >= defender power).
        private static int DonToReach(GameState s, CardInstance atk, int targetPower)
        {
            int cur = GameEngine.GetPower(s, atk);
            return cur >= targetPower ? 0 : (targetPower - cur + 999) / 1000;
        }

        // Cheapest PROFITABLE attack for `atk` within `budget` extra DON!!: (targetId, donNeeded).
        // A profitable attack is one that CONNECTS — never a 3000 attacker swinging into a 5000 leader
        // for nothing. Prefers K.O.'ing a rested enemy Character (removes a threat) unless `race` is on
        // (then it hits the Leader to push lethal). Returns null when nothing connects within budget.
        private static (string targetId, int don)? PlanAttack(GameState s, string seat, CardInstance atk, int budget, bool race)
        {
            var opp = s.Players[GameEngine.OtherSeat(seat)];
            (string id, int don, double val)? koPick = null;
            foreach (var t in opp.CharacterArea)
            {
                if (t == null || !t.Rested) continue;                 // can only attack rested Characters
                int need = DonToReach(s, atk, GameEngine.GetPower(s, t));
                if (need > budget) continue;
                double val = Value(s, t) - need * 250;                 // favour high value, low DON!! spend
                if (koPick == null || val > koPick.Value.val) koPick = (t.InstanceId, need, val);
            }
            (string id, int don)? leaderPick = null;
            if (opp.Leader != null)
            {
                int need = DonToReach(s, atk, GameEngine.GetPower(s, opp.Leader));
                if (need <= budget) leaderPick = (opp.Leader.InstanceId, need);
            }
            if (race && leaderPick != null) return (leaderPick.Value.id, leaderPick.Value.don);
            if (koPick != null) return (koPick.Value.id, koPick.Value.don);
            if (leaderPick != null) return (leaderPick.Value.id, leaderPick.Value.don);
            return null;
        }

        // ---- Deck-look resolution (simplified: pick the single best matching card) --

        private static GameCommand DecideDeckLook(GameState state, string seat, HashSet<string> blacklist)
        {
            var dl = state.DeckLook;
            if (dl.Step == "select")
            {
                var eligible = dl.Cards.Where(c => DeckLookCardEligible(state, dl, c))
                    .OrderByDescending(c => Value(state, c));
                foreach (var c in eligible)
                {
                    var cmd = Try(blacklist, new GameCommand { Type = "deckLookSelect", Seat = seat, Target = c.InstanceId });
                    if (cmd != null) return cmd;
                }
                return new GameCommand { Type = "deckLookSelect", Seat = seat, Target = "" };
            }
            if (dl.Step == "scry")
            {
                // "Look at N, place each at top or bottom": OrderedInstanceIds here is the set
                // kept on TOP (in order) — everything else in dl.Cards goes to the bottom. Bare
                // "keep nothing on top" (empty list) is always a legal, terminating choice, so
                // default to that rather than risk misjudging which cards are worth keeping.
                return new GameCommand { Type = "deckLookScryConfirm", Seat = seat, OrderedInstanceIds = new List<string>() };
            }
            // "rearrange" (and any other step): keep remaining cards in their current order —
            // a no-op ordering that completes the effect without a deep bottom-of-deck strategy.
            return new GameCommand
            {
                Type = "deckLookConfirmOrder",
                Seat = seat,
                OrderedInstanceIds = dl.Cards.Select(c => c.InstanceId).ToList(),
            };
        }

        private static bool DeckLookCardEligible(GameState state, DeckLookState dl, CardInstance card)
        {
            var def = GameEngine.GetCard(card);
            if (def == null) return false;
            if (!string.IsNullOrEmpty(dl.CardTypeFilter) && !string.IsNullOrEmpty(dl.NamedCardFilter))
            {
                bool nameMatch = GameEngine.NameMatches(state, card, dl.NamedCardFilter);
                bool typeMatch = def.Type.Equals(dl.CardTypeFilter, StringComparison.OrdinalIgnoreCase);
                if (!nameMatch && !typeMatch) return false;
            }
            else if (!string.IsNullOrEmpty(dl.NamedCardFilter) && !GameEngine.NameMatches(state, card, dl.NamedCardFilter))
                return false;
            else if (!string.IsNullOrEmpty(dl.CardTypeFilter) && !def.Type.Equals(dl.CardTypeFilter, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.IsNullOrEmpty(dl.FeatureFilter) && !def.HasFeature(dl.FeatureFilter)) return false;
            if (dl.MaxCost >= 0 && def.Cost > dl.MaxCost) return false;
            if (dl.MaxPower >= 0 && def.Power > dl.MaxPower) return false;
            if (dl.RequireTrigger && string.IsNullOrEmpty(def.Trigger)) return false;
            // "Play mode" search effects reject a Character pick outright when the board is
            // full (the engine re-inserts it and no-ops rather than advancing) — the blacklist
            // in TakeAllAvailableActions already prevents looping on this, but excluding it
            // here means the bot doesn't waste a turn even trying it first.
            if (dl.PlayMode && def.Type == "character" && state.Players.TryGetValue(dl.Seat, out var dlP)
                && !dlP.CharacterArea.Exists(c => c == null))
                return false;
            return true;
        }

        // ---- Defense (block / counter / trigger / resolve) ------------------------

        private static GameCommand DecideDefense(GameState state, string seat, HashSet<string> blacklist)
        {
            var battle = state.Battle;
            var me = state.Players[seat];
            switch (battle.Step)
            {
                case "block":
                {
                    var attacker = FindCard(state, battle.AttackerSeat, battle.AttackerId);
                    int attackPower = attacker != null ? GameEngine.GetPower(state, attacker) : 0;
                    var blockers = me.CharacterArea
                        .Where(c => c != null && !c.Rested
                            && (GameEngine.GetCard(c)?.Keywords?.Contains("Blocker") ?? false))
                        .ToList();
                    if (blockers.Count == 0)
                        return new GameCommand { Type = "passBlock", Seat = seat };

                    bool leaderLethalRisk = battle.TargetId == me.Leader?.InstanceId && me.Life.Count == 0;
                    // A blocker survives only if its power EXCEEDS the attack (a tie loses — attacker
                    // wins on >=). Block with the smallest survivor to keep the bigger blockers back.
                    var surviving = blockers.Where(b => GameEngine.GetPower(state, b) > attackPower)
                        .OrderBy(b => GameEngine.GetPower(state, b));
                    foreach (var b in surviving)
                    {
                        var cmd = Try(blacklist, new GameCommand { Type = "blockAttack", Seat = seat, Blocker = b.InstanceId });
                        if (cmd != null) return cmd;
                    }
                    if (leaderLethalRisk || me.Life.Count <= 1)
                    {
                        foreach (var b in blockers.OrderBy(b => GameEngine.GetPower(state, b)))
                        {
                            var cmd = Try(blacklist, new GameCommand { Type = "blockAttack", Seat = seat, Blocker = b.InstanceId });
                            if (cmd != null) return cmd;
                        }
                    }
                    return new GameCommand { Type = "passBlock", Seat = seat };
                }
                case "counter":
                {
                    // To SURVIVE, the defender's power must EXCEED the attacker's — a TIE loses the
                    // battle (attacker wins on >=). So the boost we need is attack - defense + 1, not
                    // attack - defense (that was the "counter 6k into 6k and still take damage" bug).
                    int need = battle.AttackPower - battle.DefensePower + 1;
                    if (need <= 0) return new GameCommand { Type = "passCounter", Seat = seat };

                    bool targetIsLeader = battle.TargetId == me.Leader?.InstanceId;
                    var target = FindCard(state, seat, battle.TargetId);
                    // Worth defending? Protect the Leader when Life is getting low (or it's lethal);
                    // for a Character, only save a genuinely valuable one — otherwise let it die and
                    // keep the counters for the Leader.
                    bool worthDefending = targetIsLeader
                        ? (me.Life.Count <= 3 || me.Life.Count == 0)
                        : (target != null && (GameEngine.GetCard(target)?.Cost ?? 0) >= 5);
                    if (!worthDefending) return new GameCommand { Type = "passCounter", Seat = seat };

                    // AWARENESS: only commit counters if the AI can AFFORD to reach `need`. A
                    // [Counter] event costs DON!! to play, so summing every eligible counter over-
                    // counts — the AI would start countering, run out of DON!!, and stop partway
                    // (wasting the cards while still taking the hit). Use the DON!!-constrained max.
                    var usable = me.Hand.Where(c => GameEngine.CanCounterFromHand(state, seat, c))
                        .Select(c => (card: c, cp: GameEngine.GetCounterPower(c)))
                        .Where(x => x.cp > 0)
                        .OrderByDescending(x => x.cp)
                        .ToList();
                    if (MaxAffordableCounter(state, seat) < need) return new GameCommand { Type = "passCounter", Seat = seat };

                    // If one counter alone suffices, use the SMALLEST such (don't overspend a big
                    // counter on a small need); otherwise stack largest-first to reach `need`.
                    var single = usable.Where(x => x.cp >= need).OrderBy(x => x.cp).ToList();
                    var plan = single.Count > 0 ? new List<(CardInstance card, int cp)> { single[0] } : usable;
                    foreach (var x in plan)
                    {
                        var cmd = Try(blacklist, new GameCommand { Type = "counterWithCard", Seat = seat, InstanceId = x.card.InstanceId });
                        if (cmd != null) return cmd;
                    }
                    return new GameCommand { Type = "passCounter", Seat = seat };
                }
                case "trigger":
                    return new GameCommand { Type = ShouldUseTrigger(state, seat) ? "useTrigger" : "passTrigger", Seat = seat };
                case "damage":
                default:
                    return new GameCommand { Type = "resolveAttack", Seat = seat };
            }
        }

        // Declining a Trigger adds the Life card to hand. If that Trigger merely activates an
        // owner-agnostic bounce Main effect (Sables) and the opponent has no legal Character target,
        // firing it can only bounce our own board. Hold the Event instead.
        internal static bool ShouldUseTrigger(GameState state, string seat)
        {
            var revealed = state.Battle?.RevealedLife;
            var def = revealed == null ? null : GameEngine.GetCard(revealed);
            if (def == null || string.IsNullOrEmpty(def.Trigger)) return false;
            bool activatesMain = def.Trigger.IndexOf("Activate this card's [Main] effect", StringComparison.OrdinalIgnoreCase) >= 0;
            string payload = activatesMain ? def.Effect : def.Trigger;

            // A Trigger that plays its own Character is optional. With all five slots occupied it
            // cannot create a body, so taking the card into hand is strictly better than revealing
            // and attempting the no-op.
            if (def.Type == "character"
                && def.Trigger.IndexOf("Play this card", StringComparison.OrdinalIgnoreCase) >= 0
                && !state.Players[seat].CharacterArea.Any(c => c == null))
                return false;

            // Optional target-dependent removal/debuff Triggers must have a legal opposing target.
            // This covers direct Trigger text and "activate this card's Main" Events such as
            // ST21-017 Gum-Gum Mole Pistol. Without the gate the bot trashed the Life card even
            // when the entire effect resolved for zero value.
            if (NeedsOpponentTarget(payload)
                && !HasUsefulOpponentTarget(state, seat, revealed, def, payload))
                return false;

            if (!activatesMain || !IsOwnerAgnosticBounce(def.Effect)) return true;

            return HasUsefulOpponentTarget(state, seat, revealed, def, def.Effect);
        }

        private static bool NeedsOpponentTarget(string text)
        {
            if (string.IsNullOrEmpty(text)
                || text.IndexOf("opponent", StringComparison.OrdinalIgnoreCase) < 0) return false;
            string t = text.ToLowerInvariant();
            return t.Contains("k.o.") || t.Contains("return") || t.Contains("rest up to")
                || t.Contains("trash up to") || t.Contains("cannot attack")
                || System.Text.RegularExpressions.Regex.IsMatch(t, @"[-\u2212\u2013\u2011\u2012\u2014]\d{3,5}\s+power");
        }

        private static bool HasUsefulOpponentTarget(GameState state, string seat, CardInstance source,
            CardDef def, string text)
        {
            var probe = new PendingEffect
            {
                Seat = seat, SourceCardId = def.Id, SourceInstanceId = source.InstanceId,
                Text = text, TargetZone = EffectTargetZone.Play,
            };
            var opp = state.Players[GameEngine.OtherSeat(seat)];
            if (opp.Leader != null && GameEngine.IsValidEffectTarget(state, probe, opp.Leader)) return true;
            return opp.CharacterArea.Any(c => c != null && GameEngine.IsValidEffectTarget(state, probe, c));
        }

        // Maximum total counter power the defender can actually AFFORD this Counter step. Character
        // [Counter] cards trash from hand for free; [Counter] events cost their printed cost in DON!!,
        // so we can only play the subset that fits the active-DON!! budget (greedy, highest power
        // first). Under-counts vs. a perfect knapsack, which is the SAFE side — the bot never starts
        // a counter chain it can't finish.
        private static int MaxAffordableCounter(GameState state, string seat)
        {
            var me = state.Players[seat];
            int don = GameEngine.ActiveDonCount(me);
            int total = 0;
            var events = new List<(int cp, int cost)>();
            foreach (var c in me.Hand)
            {
                if (!GameEngine.CanCounterFromHand(state, seat, c)) continue;
                int cp = GameEngine.GetCounterPower(c);
                if (cp <= 0) continue;
                var def = GameEngine.GetCard(c);
                if (def != null && def.Type == "event") events.Add((cp, def.Cost));
                else total += cp;               // character [Counter] — free to play
            }
            foreach (var e in events.OrderByDescending(x => x.cp))
                if (e.cost <= don) { don -= e.cost; total += e.cp; }
            return total;
        }

        private static CardInstance FindCard(GameState state, string seat, string instanceId)
        {
            if (string.IsNullOrEmpty(seat) || string.IsNullOrEmpty(instanceId)) return null;
            var p = state.Players[seat];
            if (p.Leader?.InstanceId == instanceId) return p.Leader;
            var c = p.CharacterArea.FirstOrDefault(x => x != null && x.InstanceId == instanceId);
            if (c != null) return c;
            if (p.Stage?.InstanceId == instanceId) return p.Stage;
            return null;
        }

        private static IEnumerable<CardInstance> BoardCards(PlayerState p)
        {
            if (p.Leader != null) yield return p.Leader;
            foreach (var c in p.CharacterArea) if (c != null) yield return c;
            if (p.Stage != null) yield return p.Stage;
        }

        // Should the bot proactively fire this card's [Activate: Main] ability? Conservative: only
        // clearly-beneficial ones (card advantage / removal / tempo), and never ones whose printed
        // cost spends our own hand or Life (so it doesn't pay a cost just to trigger something).
        private static bool BeneficialActivateMain(string effect)
        {
            if (string.IsNullOrEmpty(effect)
                || effect.IndexOf("[Activate: Main]", StringComparison.OrdinalIgnoreCase) < 0) return false;
            string e = effect.ToLowerInvariant();
            if (e.Contains("you may trash") && e.Contains("from your hand")) return false;
            if (e.Contains("trash") && e.Contains("from the top of your life")) return false;
            if (e.Contains("trash this character")) return false;
            return e.Contains("draw ") || e.Contains("k.o.") || e.Contains("set up to") || e.Contains("rest up to")
                || e.Contains("play up to") || e.Contains("add up to") || e.Contains("return up to")
                || e.Contains("gains +") || e.Contains("look at")
                || (e.Contains("give") && e.Contains("rested don!!"));
        }

        // ---- Main phase: deploy -> attach DON!! -> attack -> end turn -------------

        private static GameCommand DecideMainPhase(GameState state, string seat, HashSet<string> blacklist)
        {
            var p = state.Players[seat];

            // 1) Deploy the highest-value affordable card first (spends DON!! on board
            //    presence before combat math — matches the deploy-then-attach-then-attack
            //    ordering seen across the mined ranked-ladder replays).
            var playable = p.Hand.Where(c => GameEngine.CanPlayFromHand(state, seat, c))
                .OrderByDescending(c => MainPlayValue(state, seat, c));
            foreach (var c in playable)
            {
                var cmd = Try(blacklist, new GameCommand { Type = "playCard", Seat = seat, InstanceId = c.InstanceId });
                if (cmd != null) return cmd;
            }

            // 2) [Activate: Main] usage is DISABLED for now — a repeatable (non-Once-Per-Turn)
            //    activation whose success-check saw state change every tick could re-fire forever
            //    and stall the game. Re-enable with a per-turn "already activated" guard on the card.

            // 3) Attack planning. No attacks on your very first turn (summoning sickness — Rush is
            //    handled per-attacker below).
            if (p.TurnsStarted <= 1) return Try(blacklist, new GameCommand { Type = "endTurn", Seat = seat });

            var opponent = state.Players[GameEngine.OtherSeat(seat)];
            var attackers = new List<CardInstance>();
            if (p.Leader != null && !p.Leader.Rested) attackers.Add(p.Leader);
            attackers.AddRange(p.CharacterArea.Where(c => c != null && !c.Rested));
            attackers = attackers
                .Where(a => !GameEngine.HasModifier(state, a, "cannotAttack"))
                .Where(a => a.PlayedOnTurn != state.TurnNumber || GameEngine.HasRush(state, a))
                .OrderByDescending(a => GameEngine.GetPower(state, a))
                .ToList();

            int activeDon = GameEngine.ActiveDonCount(p);
            int counterReserve = CounterEventReserve(p, activeDon);

            // Lethal/race read: if enough of my attackers can reach the opponent's Leader to cover
            // their Life (plus their active blockers), push face instead of trading. Approximate —
            // it doesn't perfectly account for shared DON!!, just biases target selection.
            int oppLeaderPow = opponent.Leader != null ? GameEngine.GetPower(state, opponent.Leader) : 999;
            int leaderReachers = attackers.Count(a => DonToReach(state, a, oppLeaderPow) <= activeDon);
            int oppBlockers = opponent.CharacterArea.Count(c => c != null && !c.Rested && GameEngine.HasBlocker(state, c));
            bool race = opponent.Leader != null && opponent.Life.Count > 0
                        && leaderReachers >= opponent.Life.Count + oppBlockers;

            // Commit every DON!! that has no concrete defensive job BEFORE the first attack.
            // The previous sequence immediately swung every attacker that already connected, so
            // once they were rested it could end the turn with 5+ active DON!! and no Counter Event.
            // One-at-a-time, least-attached distribution avoids a single giant overcommit while
            // guaranteeing that otherwise-stranded DON!! becomes pressure. A real [Counter] Event
            // in hand is the only reason this baseline deliberately preserves active DON!!.
            int spendableDon = Math.Max(0, activeDon - counterReserve);
            if (spendableDon > 0)
            {
                var pressureTarget = attackers
                    .Where(a => PlanAttack(state, seat, a, spendableDon, race) != null)
                    .OrderBy(a => a.AttachedDonIds.Count)
                    .ThenByDescending(a => GameEngine.GetPower(state, a))
                    .FirstOrDefault();
                if (pressureTarget != null)
                {
                    var commit = Try(blacklist, new GameCommand
                    {
                        Type = "attachDon", Seat = seat, Target = pressureTarget.InstanceId, Amount = 1,
                    });
                    if (commit != null) return commit;
                }
            }

            // Step A — declare any attacker that ALREADY connects (needs 0 more DON!!). This spends
            //          our cheapest hits first and leaves DON!! for the rest.
            foreach (var atk in attackers)
            {
                var plan = PlanAttack(state, seat, atk, 0, race);
                if (plan == null) continue;
                var cmd = Try(blacklist, new GameCommand { Type = "declareAttack", Seat = seat, Attacker = atk.InstanceId, Target = plan.Value.targetId });
                if (cmd != null) return cmd;
            }

            // Step B — SPREAD DON!!: give the CHEAPEST-to-ready attacker exactly enough DON!! to
            //          connect (never dump the whole pool on one card), maximizing the number of
            //          connecting hits. Next tick, Step A declares that now-ready attacker.
            CardInstance bestAtk = null; int bestDon = int.MaxValue; string bestTarget = null;
            foreach (var atk in attackers)
            {
                var plan = PlanAttack(state, seat, atk, Math.Max(0, activeDon - counterReserve), race);
                if (plan == null || plan.Value.don < 1) continue;      // 0-DON ones handled in Step A
                if (plan.Value.don < bestDon) { bestDon = plan.Value.don; bestAtk = atk; bestTarget = plan.Value.targetId; }
            }
            if (bestAtk != null)
            {
                var attach = Try(blacklist, new GameCommand { Type = "attachDon", Seat = seat, Target = bestAtk.InstanceId, Amount = bestDon });
                if (attach != null) return attach;
            }

            // No attacker can profitably connect (even with DON!!) — don't throw away swings; end turn.
            return Try(blacklist, new GameCommand { Type = "endTurn", Seat = seat });
        }

        private static int CounterEventReserve(PlayerState p, int activeDon)
        {
            // Preserve enough for the most expensive currently-payable [Counter] Event. This is
            // deliberately evidence-based reserve: no such Event in hand means reserve zero.
            return p.Hand
                .Select(GameEngine.GetCard)
                .Where(d => d != null && d.Type == "event" && d.Cost <= activeDon
                    && (d.Effect ?? "").IndexOf("[Counter]", StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(d => d.Cost)
                .DefaultIfEmpty(0)
                .Max();
        }

        // Compare a removal Event by the tempo it creates, not only its printed cost. Previously a
        // four-cost Sables always ranked below a three-cost Boa blocker (body + Blocker bonus), so the
        // bot deployed Boa and stranded the bounce Event with an expensive enemy Character in play.
        private static double MainPlayValue(GameState state, string seat, CardInstance card)
        {
            double score = Value(state, card);
            var def = GameEngine.GetCard(card);
            if (def == null || def.Type != "event" || !IsOwnerAgnosticBounce(def.Effect)) return score;

            var probe = new PendingEffect
            {
                Seat = seat, SourceCardId = def.Id, SourceInstanceId = card.InstanceId,
                Text = def.Effect, TargetZone = EffectTargetZone.Play,
            };
            var targets = state.Players[GameEngine.OtherSeat(seat)].CharacterArea
                .Where(c => c != null && GameEngine.IsValidEffectTarget(state, probe, c))
                .ToList();
            if (targets.Count == 0) return -1_000_000;
            return score + targets.Max(c => Value(state, c)) + 2500;
        }
    }
}
