using System;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim.Planning
{
    /// <summary>
    /// Reads a deck list into a <see cref="DeckContext"/> (blueprint §5.1 fingerprint, §4.1 win
    /// conditions) so the value function can judge a position for THIS deck — most importantly which
    /// win route it's playing and whether it *wants* low life (Yellow). Both deck lists are known
    /// pre-match (§8.1), so this is computed once per side at game start.
    /// </summary>
    public static class DeckFingerprint
    {
        public static DeckContext Analyze(DeckDef deck)
        {
            var ctx = new DeckContext();
            int n = 0; long costSum = 0;
            int lowLife = 0, immediateWin = 0, selfDeckout = 0, blockerWin = 0;
            var bandCounts = new System.Collections.Generic.Dictionary<int, int>(); // threshold → copies referencing it

            void Scan(string cardId, int qty)
            {
                var d = CardData.GetCard(cardId);
                if (d == null) return;
                string t = ((d.Effect ?? "") + " " + (d.Trigger ?? "")).ToLowerInvariant();
                if (d.Type == "event") ctx.Events += qty;
                if (t.Contains("activate: main")) ctx.MainActivations += qty;
                if (cardId == deck.Leader && t.Contains("activate: main")) ctx.RequiresMainActivation = true;
                if (System.Text.RegularExpressions.Regex.IsMatch(t, @"don!!\s*[−-]\s*\d"))
                    ctx.DonMinusEffects += qty;
                // Archetype-level DON!! recovery (leader OR characters), keyed off the shared detector.
                if (OnePieceTcg.Engine.Bot.Search.DonOpportunityModel.IsDonRecovery(d.Effect)
                    || OnePieceTcg.Engine.Bot.Search.DonOpportunityModel.IsDonRecovery(d.Trigger))
                    ctx.HasDonRecovery = true;
                // Trash-recursion: cards that act "from your trash" (play/add a body back).
                if (t.Contains("from your trash")) ctx.TrashRecurCards += qty;
                // WS-1: a leader that shrinks the DON!! deck ("your DON!! deck consists of N cards").
                var capM = System.Text.RegularExpressions.Regex.Match(t, @"don!!\s*deck consists of (\d+) cards");
                if (capM.Success) ctx.DonDeckCap = int.Parse(capM.Groups[1].Value);
                // WS-1: cards that key off a low-DON band ("N or less DON!! cards on your field").
                foreach (System.Text.RegularExpressions.Match m in
                         System.Text.RegularExpressions.Regex.Matches(t, @"(\d+) or less don!! cards on your field"))
                {
                    int thr = int.Parse(m.Groups[1].Value);
                    bandCounts[thr] = (bandCounts.TryGetValue(thr, out var c) ? c : 0) + qty;
                }
                if (d.Type != "event") { n += qty; costSum += (long)d.Cost * qty; }
                if (d.Keywords?.Contains("Blocker") ?? false) ctx.Blockers += qty;
                if (d.Counter > 0 || (d.Keywords?.Contains("Counter") ?? false)) ctx.Counters += qty;
                if (t.Contains("k.o.") || t.Contains("trash") || t.Contains("rest ") || t.Contains("return")) ctx.Removal += qty;
                if (t.Contains("top of your deck") || t.Contains("look at") || t.Contains("search your deck") || t.Contains("reveal")) ctx.Searchers += qty;

                // life-context: cards that reward being at (or push toward) low life
                if (t.Contains("or less life") || t.Contains("life is") || t.Contains("if you have") && t.Contains("life")) lowLife += qty;
                // win-condition wording
                bool win = t.Contains("win the game");
                if (win && (t.Contains("instead of losing") || t.Contains("do not lose"))) selfDeckout += qty;
                else if (win && (t.Contains("blocker"))) blockerWin += qty;
                else if (win) immediateWin += qty;
            }

            Scan(deck.Leader, 1);
            foreach (var (cardId, qty) in deck.List) if (cardId != deck.Leader) Scan(cardId, qty);

            ctx.AvgCost = n == 0 ? 0 : (double)costSum / n;
            // The band the deck cares about most = the threshold the most copies reference.
            if (bandCounts.Count > 0)
            {
                var dominant = bandCounts.OrderByDescending(kv => kv.Value).First();
                ctx.DonBandThreshold = dominant.Key;
                ctx.DonBandCards = dominant.Value;
            }
            ctx.HasLowLifePayoff = lowLife >= 3;   // a few payoff cards ⇒ this deck weaponizes low life
            ctx.AltWin =
                selfDeckout >= 1 ? "self-deckout" :
                blockerWin >= 1 ? "blocker-trigger" :
                immediateWin >= 1 ? "immediate" : "";
            ctx.Archetype = Classify(ctx);
            return ctx;
        }

        /// <summary>Label how the deck wants to win. Thresholds are read off the ACTUAL spread over the 41
        /// imported meta decks (avgCost 2.1-5.8, a real and roughly continuous range) rather than guessed:
        /// the cut points sit in the gaps and split the field ~7 aggro / ~22 midrange / ~8 control / 4 combo.
        /// A deck with a genuine alternate win route is playing a different game entirely, so that wins over
        /// curve. See <see cref="DeckContext.Archetype"/> for why Counters/Removal are not consulted.</summary>
        public static string Classify(DeckContext c) =>
            c.AltWin != "" ? "combo" :
            c.AvgCost < 3.5 ? "aggro" :
            c.AvgCost < 4.7 ? "midrange" : "control";

        public static string Describe(DeckContext c) =>
            $"[{c.Archetype,-8}] avgCost={c.AvgCost:0.0} blockers={c.Blockers} counters={c.Counters} removal={c.Removal} " +
            $"searchers={c.Searchers} events={c.Events} mainActs={c.MainActivations} donMinus={c.DonMinusEffects} " +
            $"leaderEngine={c.RequiresMainActivation} lowLifePayoff={c.HasLowLifePayoff} altWin='{c.AltWin}'";
    }
}
