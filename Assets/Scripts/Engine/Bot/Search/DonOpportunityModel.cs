using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace OnePieceTcg.Engine.Bot.Search
{
    /// <summary>
    /// WS-1 decision-quality: the DON resource profile and the opportunity-cost model for spending a
    /// DON-manipulation card's Main effect vs. holding it. Frequency was never the Enel problem — the bot
    /// already plays ~6.6 DON-minus events per game — so this addresses QUALITY: is playing THIS card's Main
    /// now worth more than keeping it for [Counter] defence, net of the DON it returns?
    ///
    /// Codex's model:  useMain iff  effect_gained  >  counter_value_of_card  +  active_DON_opportunity_cost,
    /// where the opportunity cost is small when the deck recovers DON quickly (a re-ramp leader like Enel, or
    /// simply next turn's natural DON), and the counter value is weighted by how much defence the seat needs.
    /// Nothing is card-id specific: the profile and the value estimate are read from printed text and board.
    /// </summary>
    public readonly struct DonResourceProfile
    {
        public readonly int Capacity;        // DON!! deck size cap (10 default, detected lower e.g. Enel 6)
        public readonly int DeckRemaining;   // DON!! still in the DON!! deck to ramp from
        public readonly int FieldDon;        // DON!! on the field = cost area + attached
        public readonly int Active;          // active (unrested, unattached) DON!! in the cost area
        public readonly int Rested;          // rested DON!! in the cost area
        public readonly int Attached;        // DON!! given to Leader/Characters
        public readonly bool FastRecovery;   // a re-ramp leader can pull DON back this game (Enel) → returning DON is cheap

        public DonResourceProfile(int capacity, int deckRemaining, int fieldDon, int active, int rested, int attached, bool fastRecovery)
        { Capacity = capacity; DeckRemaining = deckRemaining; FieldDon = fieldDon; Active = active; Rested = rested; Attached = attached; FastRecovery = fastRecovery; }

        public static DonResourceProfile Build(GameState state, string seat)
        {
            var p = state.Players[seat];
            int active = p.CostArea.Count(d => !d.Rested);
            int rested = p.CostArea.Count(d => d.Rested);
            int attached = (p.Leader?.AttachedDonIds?.Count ?? 0)
                + p.CharacterArea.Where(c => c != null).Sum(c => c.AttachedDonIds?.Count ?? 0);
            // Recovery is not leader-only: purple runs CHARACTERS that re-ramp DON!! too (Senor Pink sets a
            // DON!! active, Gamma Knife pulls one from the DON!! deck, …). Treat DON!! as cheap to return when
            // any live source — leader, a Character on board, or a card in hand — can reclaim it. Text-driven,
            // so it works for whatever deck is bot-piloted.
            bool fast = DonOpportunityModel.IsDonRecovery(GameEngine.GetCard(p.Leader)?.Effect)
                || p.CharacterArea.Any(c => c != null && DonOpportunityModel.IsDonRecovery(GameEngine.GetCard(c)?.Effect))
                || p.Hand.Any(c => c != null && DonOpportunityModel.IsDonRecovery(GameEngine.GetCard(c)?.Effect));
            int capacity = p.DonDeck + p.CostArea.Count + attached; // total DON!! that ever exists for this player
            return new DonResourceProfile(capacity, p.DonDeck, p.CostArea.Count + attached, active, rested, attached, fast);
        }
    }

    public static class DonOpportunityModel
    {
        // Value units follow the rest of the bot: 1.0 ≈ one card ≈ 1000 power ≈ a small tempo swing.
        private const double DrawValue = 0.9;
        private const double RemovalPerInvest = 0.7;   // per (cost + power/1000) of an opposing body removed
        private const double SelfBuffPerK = 0.2;       // this-turn +power on our own board
        private const double CounterPerK = 1.0;        // per 1000 printed [Counter], before the need weighting
        private const double DonReturnFast = 0.1;      // opportunity cost per DON!! returned, re-ramp deck
        private const double DonReturnSlow = 0.25;     // per DON!! returned, ordinary recovery
        private const double DonReturnHeavy = 1.0;     // per DON!! returned with NO recovery (blue Crocodile): real tempo
        private const double Margin = 0.2;             // don't spend a Counter card for a marginal Main gain
        // Disruption realised NOW (a valid target exists this turn).
        private const double RestValue = 0.5;          // rest an active opposing attacker (deny its swing)
        private const double FreezeValue = 0.6;        // keep a rested body from refreshing (deny next-turn attacker)
        private const double PowerDownValue = 0.3;     // a bare −power shrink with a target (minor unless it combos)
        // Option value of HOLDING a targeted disruption whose target is absent now — the card is worth more on a
        // later turn when a target appears, so a draw-plus-disruption card should not be burned for the draw alone.
        private const double KoOption = 0.6;
        private const double RestOption = 0.5;
        private const double FreezeOption = 0.8;
        private const double PowerDownOption = 0.2;

        /// <summary>True to play this DON-manipulation card's Main effect now; false to hold it for [Counter].
        /// Only meaningful for a card in hand whose text pays a DON!! −N cost and/or carries a [Counter] mode.</summary>
        public static bool ShouldUseMain(GameState state, string seat, CardInstance card)
        {
            var def = card == null ? null : GameEngine.GetCard(card);
            if (def == null) return true;
            var profile = DonResourceProfile.Build(state, seat);

            double mainValue = MainEffectValue(state, seat, def.Effect);
            // Hold value = defensive [Counter] value + the option value of a targeted disruption whose target
            // is absent now (worth keeping for a turn when a target appears). The second term is what lets a
            // no-Counter card like Lightning Dragon be HELD when its freeze hits nothing.
            double holdValue = CounterHoldValue(state, seat, card) + OptionValueOfHolding(state, seat, def.Effect);
            int donReturned = DonMinusCost(def.Effect);
            double donCost = donReturned * (profile.FastRecovery ? DonReturnFast : DonReturnSlow);

            return mainValue > holdValue + donCost + Margin;
        }

        /// <summary>Estimate the value of resolving a Main effect NOW, from text + board (deterministic, no
        /// simulation). Dominant terms only: draw, a K.O. that has a legal target, and a self power buff.</summary>
        public static double MainEffectValue(GameState state, string seat, string effect)
        {
            if (string.IsNullOrEmpty(effect)) return 0;
            string e = effect.ToLowerInvariant();
            double v = 0;

            var draw = Regex.Match(e, @"draw (\d+) card");
            if (draw.Success) v += DrawValue * int.Parse(draw.Groups[1].Value);
            else if (e.Contains("draw a card") || e.Contains("draw 1 card")) v += DrawValue;

            // A K.O. clause is only worth anything if a legal target exists on the opponent's board.
            if (e.Contains("k.o.")) v += RemovalPerInvest * BestKoTargetInvestment(state, seat, e);
            // A bounce (return a Character to the owner's hand) is removal-to-hand — worth its best opposing target.
            if (RemovalModel.Classify(effect) == RemovalKind.Bounce)
                v += RemovalPerInvest * BestKoTargetInvestment(state, seat, e);

            // Rest an active opposing attacker — value only if an unrested body in range exists.
            if (Regex.IsMatch(e, @"rest up to \d+ of your opponent") && HasOpposingTarget(state, seat, e, TargetMode.Unrested))
                v += RestValue;
            // Freeze ("will not become active") — value only if a rested body in range exists to deny.
            if (e.Contains("will not become active") && HasOpposingTarget(state, seat, e, TargetMode.Rested))
                v += FreezeValue;
            // A bare −power debuff aimed at the opponent — minor, and only if there is a body to shrink.
            if (Regex.IsMatch(e, @"[-−–‑‒—]\s?\d{3,5} power") && e.Contains("opponent")
                && HasOpposingTarget(state, seat, e, TargetMode.Any))
                v += PowerDownValue;

            // "gains +N power" on our own side, this turn only — minor tempo.
            var buff = Regex.Match(e, @"gains? \+(\d{3,5}) power");
            if (buff.Success && !e.Contains("opponent")) v += SelfBuffPerK * (int.Parse(buff.Groups[1].Value) / 1000.0);

            return v;
        }

        /// <summary>Value of KEEPING a card because a targeted disruption in its Main has NO target right now —
        /// it converts to value on a later turn when a target appears. This is the option-value term that lets
        /// a draw-plus-disruption card (Lightning Dragon's freeze, El Thor's K.O.) be held rather than burned
        /// for the draw alone. Only the disruption classes are counted, and only when their target is absent.</summary>
        public static double OptionValueOfHolding(GameState state, string seat, string effect)
        {
            if (string.IsNullOrEmpty(effect)) return 0;
            string e = effect.ToLowerInvariant();
            double v = 0;
            if (e.Contains("k.o.") && BestKoTargetInvestment(state, seat, e) <= 0) v += KoOption;
            if (Regex.IsMatch(e, @"rest up to \d+ of your opponent") && !HasOpposingTarget(state, seat, e, TargetMode.Unrested)) v += RestOption;
            if (e.Contains("will not become active") && !HasOpposingTarget(state, seat, e, TargetMode.Rested)) v += FreezeOption;
            if (Regex.IsMatch(e, @"[-−–‑‒—]\s?\d{3,5} power") && e.Contains("opponent") && !HasOpposingTarget(state, seat, e, TargetMode.Any)) v += PowerDownOption;
            return v;
        }

        private enum TargetMode { Any, Unrested, Rested }

        // Does the opponent have a Character that a clause could act on, honouring its power/cost threshold and
        // the rested/active requirement of the effect class (rest needs an active body; freeze needs a rested one)?
        private static bool HasOpposingTarget(GameState state, string seat, string lowerEffect, TargetMode mode)
        {
            var opp = state.Players[GameEngine.OtherSeat(seat)];
            int powCap = ParseThreshold(lowerEffect, @"(\d{3,5}) power or less");
            int costCap = ParseThreshold(lowerEffect, @"cost of (\d+) or less");
            foreach (var c in opp.CharacterArea)
            {
                if (c == null) continue;
                if (mode == TargetMode.Unrested && c.Rested) continue;
                if (mode == TargetMode.Rested && !c.Rested) continue;
                if (powCap >= 0 && GameEngine.GetPower(state, c) > powCap) continue;
                if (costCap >= 0 && GameEngine.GetCost(state, c) > costCap) continue;
                return true;
            }
            return false;
        }

        /// <summary>The value of KEEPING this card for [Counter], = its counter power weighted by how much
        /// defence the seat needs (low life / a threatening board make a held Counter worth more).</summary>
        public static double CounterHoldValue(GameState state, string seat, CardInstance card)
        {
            int cp = GameEngine.GetCounterPower(card);
            if (cp <= 0) return 0;
            var me = state.Players[seat];
            var opp = state.Players[GameEngine.OtherSeat(seat)];
            int life = me.Life.Count;
            double need = life <= 1 ? 1.0 : life <= 2 ? 0.7 : life <= 3 ? 0.45 : life <= 4 ? 0.3 : 0.2;
            if (opp.CharacterArea.Any(c => c != null && !c.Rested)) need = Math.Min(1.0, need + 0.15);
            return CounterPerK * (cp / 1000.0) * need;
        }

        /// <summary>Best (cost + power/1000) among opponent bodies a K.O. clause could legally hit, by parsing
        /// the clause's power/cost threshold. 0 when nothing is in range — so a K.O. with no target is worthless.</summary>
        private static double BestKoTargetInvestment(GameState state, string seat, string lowerEffect)
        {
            var opp = state.Players[GameEngine.OtherSeat(seat)];
            int powCap = ParseThreshold(lowerEffect, @"(\d{3,5}) power or less");
            int costCap = ParseThreshold(lowerEffect, @"cost of (\d+) or less");
            double best = 0;
            foreach (var c in opp.CharacterArea)
            {
                if (c == null) continue;
                var d = GameEngine.GetCard(c);
                if (d == null) continue;
                if (powCap >= 0 && GameEngine.GetPower(state, c) > powCap) continue;
                if (costCap >= 0 && GameEngine.GetCost(state, c) > costCap) continue;
                if (powCap < 0 && costCap < 0) { /* untargeted K.O. wording — treat every body as in range */ }
                best = Math.Max(best, d.Cost + GameEngine.GetPower(state, c) / 1000.0);
            }
            return best;
        }

        /// <summary>Is an activation that pays a DON!! −N cost worth it — does the effect it buys exceed the
        /// tempo of the DON!! returned? For a re-ramp deck (Enel) the returned DON!! comes back, so the cost is
        /// near-zero; for a deck with NO recovery (blue Crocodile's DON!! −4 leader bounce) each returned DON!!
        /// is real, unrecoverable tempo, so the bounce must hit a target worth ~4 DON!! before it fires. Keyed
        /// off the detected resource profile, not a card id. Non-DON!!-minus activations are not gated here.</summary>
        public static bool ActivationClearsDonCost(GameState state, string seat, string effect)
        {
            int donN = DonMinusCost(effect);
            if (donN <= 0) return true;
            double perDon = DonResourceProfile.Build(state, seat).FastRecovery ? DonReturnFast : DonReturnHeavy;
            return MainEffectValue(state, seat, effect) > donN * perDon;
        }

        /// <summary>Does this card's text recover DON!! — set a rested DON!! active, or add one from the DON!!
        /// deck? (Distinct from a DON!! −N COST, which RETURNS DON!! "to your DON!! deck".) Used to detect a
        /// deck's DON!!-recovery capability from the leader OR characters OR hand, not just the leader.</summary>
        public static bool IsDonRecovery(string effect)
        {
            if (string.IsNullOrEmpty(effect)) return false;
            string e = effect.ToLowerInvariant();
            return Regex.IsMatch(e, @"don!! cards? as active")             // "set … DON!! cards as active"
                || Regex.IsMatch(e, @"don!! cards? from your don!! deck"); // "add … DON!! card from your DON!! deck …"
        }

        /// <summary>The DON!! −N cost a Main clause pays (return N DON!! to the deck), or 0.</summary>
        public static int DonMinusCost(string effect)
        {
            if (string.IsNullOrEmpty(effect)) return 0;
            var m = Regex.Match(effect.ToLowerInvariant(), @"don!!\s*[-−–‑‒—]\s?(\d+)");
            return m.Success ? int.Parse(m.Groups[1].Value) : 0;
        }

        private static int ParseThreshold(string text, string pattern)
        {
            var m = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
            return m.Success ? int.Parse(m.Groups[1].Value) : -1;
        }
    }
}
