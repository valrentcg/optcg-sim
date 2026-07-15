using System;
using System.Collections.Generic;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim.Heuristics
{
    /// <summary>A flat bag of named numeric features observed at a decision point — the raw material
    /// the distiller buckets over to find conditional heuristics (§12.2 feature attribution).</summary>
    public sealed class FeatureBag : Dictionary<string, double>
    {
        public void Set(string k, double v) => this[k] = v;
        public void Set(string k, bool v) => this[k] = v ? 1 : 0;
    }

    /// <summary>Extracts cheap, PUBLIC-to-the-owner features. Everything here is legally observable by
    /// the seat making the decision (its own hand, the deck lists both players registered) — never the
    /// opponent's hidden hand (§13). Used for start-of-game decisions (mulligan, turn order).</summary>
    public static class Features
    {
        // ── opening-hand features for the mulligan decision (§5.4) ──────────────
        public static FeatureBag OpeningHand(GameState state, string seat)
        {
            var p = state.Players[seat];
            string leaderColor = CardData.GetCard(p.Leader.CardId)?.Color ?? "";
            var defs = p.Hand.Select(c => CardData.GetCard(c.CardId)).Where(d => d != null).ToList();

            int chars = defs.Count(d => d.Type == "character");
            int events = defs.Count(d => d.Type == "event");
            int stages = defs.Count(d => d.Type == "stage");
            int counters = defs.Count(d => d.Counter > 0 || (d.Keywords?.Contains("Counter") ?? false));
            int searchers = defs.Count(d => IsSearch(d.Effect) || IsSearch(d.Trigger));
            int cost01 = defs.Count(d => d.Type != "event" && d.Cost <= 1);
            int cost2 = defs.Count(d => d.Type != "event" && d.Cost == 2);
            int cost3 = defs.Count(d => d.Type != "event" && d.Cost == 3);
            int cost4plus = defs.Count(d => d.Type != "event" && d.Cost >= 4);
            int onColor = defs.Count(d => string.Equals(d.Color, leaderColor, StringComparison.OrdinalIgnoreCase));
            int maxDup = p.Hand.GroupBy(c => c.CardId).Select(g => g.Count()).DefaultIfEmpty(0).Max();

            // Turn 1 has 1 DON (going first, no draw); turn 2 has 2. Rough early-playability.
            int playT1 = defs.Count(d => d.Type == "character" && d.Cost <= 1);
            int playT2 = defs.Count(d => d.Type == "character" && d.Cost <= 2);

            var f = new FeatureBag();
            f.Set("hand_chars", chars);
            f.Set("hand_events", events);
            f.Set("hand_stages", stages);
            f.Set("hand_counters", counters);
            f.Set("hand_searchers", searchers);
            f.Set("hand_cost01", cost01);
            f.Set("hand_cost2", cost2);
            f.Set("hand_cost3", cost3);
            f.Set("hand_cost4plus", cost4plus);
            f.Set("hand_oncolor", onColor);
            f.Set("hand_maxdup", maxDup);
            f.Set("hand_playable_t1", playT1);
            f.Set("hand_playable_t2", playT2);
            f.Set("hand_has_searcher", searchers >= 1);
            f.Set("hand_has_t1_play", playT1 >= 1);
            f.Set("hand_curve_ok", cost01 >= 1 && cost2 >= 1); // a minimal early curve
            f.Set("hand_flooded", cost4plus >= 3);             // too top-heavy
            return f;
        }

        // ── matchup features for matchup-conditioned decisions (§5.1 fingerprint, §6.4) ──
        public static FeatureBag Matchup(DeckDef mine, DeckDef opp)
        {
            var f = new FeatureBag();
            var (myAvg, myLow) = CurveProfile(mine);
            var (opAvg, opLow) = CurveProfile(opp);
            f.Set("my_avg_cost", Math.Round(myAvg, 2));
            f.Set("opp_avg_cost", Math.Round(opAvg, 2));
            f.Set("my_low_curve", myLow);      // fraction of deck at cost <= 2
            f.Set("opp_low_curve", opLow);
            f.Set("my_leader_life", CardData.GetCard(mine.Leader)?.Life ?? 0);
            f.Set("opp_leader_life", CardData.GetCard(opp.Leader)?.Life ?? 0);
            f.Set("i_am_faster", myAvg < opAvg);

            // Deck fingerprint (§5.1): the interaction shape the advanced bot compares across decks.
            var mf = Fingerprint(mine);
            var of = Fingerprint(opp);
            f.Set("my_blockers", mf.blockers);
            f.Set("opp_blockers", of.blockers);
            f.Set("my_counter_density", mf.counterDensity);
            f.Set("opp_counter_density", of.counterDensity);
            f.Set("my_events", mf.events);
            f.Set("opp_events", of.events);
            f.Set("i_have_more_counters", mf.counterDensity > of.counterDensity);
            f.Set("opp_many_blockers", of.blockers >= 6);
            return f;
        }

        private struct DeckFingerprint { public int blockers; public double counterDensity; public int events; }

        private static DeckFingerprint Fingerprint(DeckDef d)
        {
            int blockers = 0, events = 0, counters = 0, n = 0;
            foreach (var (cardId, qty) in d.List)
            {
                var def = CardData.GetCard(cardId);
                if (def == null) continue;
                n += qty;
                if (def.Type == "event") events += qty;
                if (def.Counter > 0 || (def.Keywords?.Contains("Counter") ?? false)) counters += qty;
                if (def.Keywords?.Contains("Blocker") ?? false) blockers += qty;
            }
            return new DeckFingerprint
            {
                blockers = blockers,
                events = events,
                counterDensity = n == 0 ? 0 : (double)counters / n,
            };
        }

        private static (double avg, double lowFrac) CurveProfile(DeckDef d)
        {
            int n = 0; long sum = 0; int low = 0;
            foreach (var (cardId, qty) in d.List)
            {
                var def = CardData.GetCard(cardId);
                if (def == null || def.Type == "event") continue; // curve = characters/stages
                n += qty; sum += (long)def.Cost * qty;
                if (def.Cost <= 2) low += qty;
            }
            return n == 0 ? (0, 0) : ((double)sum / n, (double)low / n);
        }

        private static bool IsSearch(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            text = text.ToLowerInvariant();
            return text.Contains("top of your deck") || text.Contains("look at") || text.Contains("reveal")
                || text.Contains("search your deck");
        }
    }
}
