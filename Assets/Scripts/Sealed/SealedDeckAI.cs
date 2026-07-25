// One Piece TCG — Sealed / Pre-Release: the A.I. builds a sealed deck from its own pool.
//
// Pure C#, no UnityEngine. The bot opens its OWN packs from a derived seed and constructs a 40-card
// deck the way a limited player does — draft to a CURVE, take the removal and the Blockers, and keep
// enough Counter cards to survive — rather than sorting by cost and taking the top 40, which is how a
// naive sealed bot ends up with nine 8-drops and no early plays.
//
// NOTE: sealed does NOT apply the constructed colour rule (see SealedProduct), so there is no colour
// filter here. Every card in the pool is playable under any Leader; building "on colour" would only
// shrink the card pool for no rules reason, and a 6-pack pool rarely holds 40 cards of one colour.
//
// The weights encode limited-format priorities, not constructed ones: in sealed, cheap interaction
// and a functional curve beat raw card power, because nobody's deck is consistent enough to punish
// a slightly weaker card that actually gets cast.

using System;
using System.Collections.Generic;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sealed
{
    public static class SealedDeckAI
    {
        /// <summary>Target shape for a 40-card sealed deck. These are limited-format norms: enough
        /// cheap plays to curve out, a real Counter count, and a couple of Blockers.</summary>
        private const int TargetCharacters = 26;
        private const int MinCounterCards = 12;

        /// <summary>Open a pool for the A.I. and build its deck. The seed is DERIVED from the player's
        /// so an event is reproducible end-to-end, but the bot never shares the player's cards.</summary>
        public static SealedPool BuildOpponent(SealedProduct product, string playerSeed, int opponentIndex)
        {
            var pool = SealedPool.Generate(product, $"{playerSeed}|ai{opponentIndex}");
            Build(pool);
            return pool;
        }

        /// <summary>Fill <paramref name="pool"/>'s deck in place: choose a Leader, draft the best
        /// playables in the pool to a curve quota, then enforce the Counter floor and the curve.</summary>
        public static void Build(SealedPool pool)
        {
            if (pool == null) return;
            pool.ClearDeck();

            var counts = pool.PoolCounts();
            var mode = pool.LeaderMode;

            // Rainbow Luffy: no choice to make, and nothing in the pool can be off-colour under it.
            if (mode == SealedLeaderMode.RainbowLuffy)
            {
                SealedLeaderRules.EnsureRegistered();
                pool.LeaderId = SealedLeaderRules.RainbowLuffyId;
            }
            var leaders = mode == SealedLeaderMode.RainbowLuffy
                ? new List<string> { SealedLeaderRules.RainbowLuffyId }
                : SealedLeaderRules.LegalLeaders(mode, pool);
            if (leaders.Count == 0) return;

            // 1. Leader. Sealed does NOT apply the colour rule, so a Leader is not a colour commitment
            //    the way it is in constructed — every card in the pool is playable under any Leader.
            //    Pick on the Leader's own merits (stats, Life, and an ability that does something) and
            //    give a mild nudge toward the colour you happen to be deepest in, since colour-
            //    referencing Leader abilities still function normally.
            string bestLeader = pool.LeaderId;
            double bestLeaderScore = double.NegativeInfinity;
            if (mode != SealedLeaderMode.RainbowLuffy)
            foreach (var leaderId in leaders)
            {
                var lDef = CardData.GetCard(leaderId);
                if (lDef == null) continue;
                var lColors = SealedPool.SplitColors(lDef.Color).ToHashSet(StringComparer.OrdinalIgnoreCase);

                double score = lDef.Power / 1000.0 + (lDef.Life ?? 0) * 0.8;
                if (!string.IsNullOrWhiteSpace(lDef.Effect)) score += 1.5;

                double onColourDepth = 0;
                foreach (var kv in counts)
                {
                    var d = CardData.GetCard(kv.Key);
                    if (d == null || d.Type == "leader") continue;
                    if (SealedPool.SplitColors(d.Color).Any(c => lColors.Contains(c)))
                        onColourDepth += kv.Value;
                }
                score += onColourDepth * 0.05;   // a nudge, not a constraint

                if (score > bestLeaderScore) { bestLeaderScore = score; bestLeader = leaderId; }
            }
            pool.LeaderId = bestLeader;

            // 2. Candidate playables: EVERY non-Leader card in the pool, best first. No colour filter —
            //    see SealedProduct for the official rule. Repeated per copy owned, since sealed has no
            //    copy limit beyond what was opened.
            var candidates = new List<(string id, CardDef def, double score)>();
            foreach (var kv in counts)
            {
                var def = CardData.GetCard(kv.Key);
                if (def == null || def.Type == "leader") continue;
                for (int i = 0; i < kv.Value; i++) candidates.Add((kv.Key, def, CardScore(def)));
            }
            candidates = candidates.OrderByDescending(c => c.score).ToList();

            int target = pool.Product()?.DeckSize ?? 40;

            // 3. Draft to a CURVE QUOTA, not to raw card quality. Taking the best-scoring cards outright
            //    drifts expensive — the strongest cards in any pool are the big ones — and a 40-card
            //    limited deck that cannot act before turn 4 loses to any curve. Real limited players
            //    build to a shape, so the fill caps how many cards may come from each cost band and
            //    takes the best available within each. Quotas sum to just over 40 so the deck fills
            //    even when a pool is thin in one band.
            var quota = new Dictionary<int, int> { [0] = 4, [1] = 6, [2] = 8, [3] = 9, [4] = 7, [5] = 4, [6] = 3 };
            var taken = new Dictionary<int, int>();
            int Band(CardDef d) => Math.Min(6, Math.Max(0, d.Cost));

            int characters = 0;
            foreach (var c in candidates)
            {
                if (pool.DeckCount() >= target) break;
                bool isChar = c.def.Type == "character";
                if (isChar && characters >= TargetCharacters) continue;
                if (!isChar && pool.DeckCount() - characters >= target - TargetCharacters) continue;

                int band = Band(c.def);
                int used = taken.TryGetValue(band, out var u) ? u : 0;
                if (used >= quota[band]) continue;

                if (pool.Add(c.id) > 0)
                {
                    taken[band] = used + 1;
                    if (isChar) characters++;
                }
            }

            // Relax the quotas if the pool was too thin to reach 40 within them.
            if (pool.DeckCount() < target)
                foreach (var c in candidates)
                {
                    if (pool.DeckCount() >= target) break;
                    pool.Add(c.id);
                }

            // 4. Counter floor: limited games are decided by surviving early attacks. If the deck is
            //    short on Counter cards, swap the weakest non-Counter bodies for Counter ones.
            EnsureCounters(pool, candidates, MinCounterCards);

            // 5. Curve pass. Card quality alone drifts expensive — the best cards in a pool tend to be
            //    the big ones, and a 40-card limited deck that cannot act before turn 4 loses to any
            //    curve. Trade the priciest cards for the best cheap ones until the average lands in
            //    limited's healthy band.
            FixCurve(pool, candidates, targetAverage: 3.4);
        }

        /// <summary>Pull the deck's average cost down toward <paramref name="targetAverage"/> by
        /// swapping its most expensive cards for the best affordable ones still in the pool.</summary>
        private static void FixCurve(SealedPool pool, List<(string id, CardDef def, double score)> candidates, double targetAverage)
        {
            var cheapPool = candidates
                .Where(c => c.def.Cost <= 3 && c.def.Type != "leader")
                .OrderByDescending(c => c.score)
                .ToList();

            for (int guard = 0; guard < 40; guard++)
            {
                var stats = pool.DeckStats();
                if (stats.AverageCost <= targetAverage) return;

                // Most expensive card currently in the deck.
                var dearest = pool.Deck.Keys
                    .Select(id => (id, def: CardData.GetCard(id)))
                    .Where(x => x.def != null)
                    .OrderByDescending(x => x.def.Cost)
                    .FirstOrDefault();
                if (dearest.def == null || dearest.def.Cost <= 4) return;   // nothing left worth cutting

                // Best cheap card with copies to spare.
                var swapIn = cheapPool.FirstOrDefault(c => pool.Remaining(c.id) > 0);
                if (swapIn.id == null) return;

                pool.Remove(dearest.id);
                if (pool.Add(swapIn.id) == 0) { pool.Add(dearest.id); return; }   // could not improve; stop
            }
        }

        /// <summary>Raise the deck's Counter count to <paramref name="floor"/> by trading out its
        /// lowest-value no-Counter cards for the best unused Counter cards.</summary>
        private static void EnsureCounters(SealedPool pool, List<(string id, CardDef def, double score)> candidates, int floor)
        {
            int Counters() => pool.Deck.Sum(kv => (CardData.GetCard(kv.Key)?.Counter ?? 0) > 0 ? kv.Value : 0);
            if (Counters() >= floor) return;

            var counterCandidates = candidates
                .Where(c => c.def.Counter > 0)
                .OrderByDescending(c => c.score)
                .ToList();

            foreach (var c in counterCandidates)
            {
                if (Counters() >= floor) break;
                if (pool.Remaining(c.id) <= 0) continue;

                // Drop the worst no-Counter card currently in the deck to make room.
                var worst = pool.Deck.Keys
                    .Select(id => (id, def: CardData.GetCard(id)))
                    .Where(x => x.def != null && x.def.Counter <= 0)
                    .OrderBy(x => CardScore(x.def))
                    .FirstOrDefault();
                if (worst.def == null) break;
                pool.Remove(worst.id);
                pool.Add(c.id);
            }
        }

        /// <summary>Limited-format card value. Deliberately different from the constructed evaluator:
        /// cheap interaction and Blockers are worth more here, and huge finishers are worth less,
        /// because sealed games are decided by curve and board presence rather than combo payoff.</summary>
        private static double CardScore(CardDef def)
        {
            if (def == null) return 0;
            double score = 0;

            // Power per cost — the core of limited card quality.
            if (def.Type == "character")
            {
                score += def.Power / 1000.0;
                score += Math.Max(0, 6 - def.Cost) * 0.35;      // cheap bodies curve out
            }
            else if (def.Type == "event") score += 2.0;
            else if (def.Type == "stage") score += 1.0;

            // Keywords that dominate limited board stalls.
            if (SealedPool.HasKeyword(def, "Blocker")) score += 2.6;
            if (SealedPool.HasKeyword(def, "Rush")) score += 1.6;
            if (SealedPool.HasKeyword(def, "Double Attack")) score += 1.4;
            if (SealedPool.HasKeyword(def, "Banish")) score += 0.8;

            // Interaction: removal is the scarcest resource in a sealed pool.
            string effect = (def.Effect ?? "").ToLowerInvariant();
            if (effect.Contains("k.o.")) score += 2.8;
            if (effect.Contains("return") && effect.Contains("hand")) score += 1.8;
            if (effect.Contains("rest")) score += 0.9;
            if (effect.Contains("draw")) score += 1.1;
            if (effect.Contains("power -") || effect.Contains("power −")) score += 1.2;

            // Counter value and Triggers are real defensive equity in limited.
            score += def.Counter / 1500.0;
            if (!string.IsNullOrWhiteSpace(def.Trigger)) score += 0.7;

            // A card you cannot reliably cast is worth less no matter how strong.
            if (def.Cost >= 8) score -= 1.5;
            else if (def.Cost == 7) score -= 0.6;

            return score;
        }
    }
}
