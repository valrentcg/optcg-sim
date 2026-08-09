// One Piece TCG — Sealed / Pre-Release: instant, tiered A.I. deck construction.
//
// Every opponent opens its OWN six-pack pool from a derived deterministic seed. Construction is
// immediate: the player never watches the bot click through a deck builder. The difficulty selected
// for the match controls both how well the pool is built and which gameplay policy pilots it.
//
// The Advanced profile follows recurring competitive sealed advice: keep all practical 2K counters
// and Blockers, favour efficient 4–6 DON!! bodies and scarce interaction, maintain enough Characters
// to pressure the board, and only count conditional effects when the selected Leader/pool can support
// them. Intermediate and Beginner use the same legal pipeline with progressively noisier evaluation
// and looser curve/counter discipline, so they make believable deck-building mistakes rather than
// illegal decks or arbitrary omissions.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sealed
{
    public static class SealedDeckAI
    {
        private sealed class TierProfile
        {
            public string Name;
            public int MinCharacters;
            public int MinCounterCards;
            public double TargetAverageCost;
            public double SynergyWeight;
            public double Noise;
            public double PriorityKeepChance;
            public Dictionary<int, int> CurveQuota;
        }

        private sealed class Candidate
        {
            public string Id;
            public CardDef Def;
            public int CopyIndex;
            public double Score;
        }

        private static readonly Regex LeaderFeatureGate = new Regex(
            @"(?:your\s+Leader\s+has\s+the|your\s+Leader(?:'s)?\s+type\s+includes)\s*\{([^}]+)\}",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex LeaderQuotedFeatureGate = new Regex(
            "(?:your\\s+Leader(?:'s)?\\s+type\\s+includes)\\s*[\\x22\\u201c]([^\\x22\\u201d]+)[\\x22\\u201d]",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex LeaderNameGate = new Regex(
            @"(?:your\s+Leader\s+is|your\s+Leader's\s+name\s+is)\s*\[([^\]]+)\]",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex LeaderColorGate = new Regex(
            @"your\s+Leader(?:'s)?\s+colors?\s+include\s+(red|green|blue|purple|black|yellow)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex FeatureReference = new Regex(@"\{([^}]+)\}",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Open exactly the product's pack count for an opponent, then build immediately.
        /// The selected opponent Leader is supplied BEFORE scoring so conditional cards and the deck
        /// plan match the Leader that will actually be piloted.</summary>
        public static SealedPool BuildOpponent(SealedProduct product, string playerSeed, int opponentIndex,
            string difficulty = "advanced", string preferredLeaderId = null)
        {
            var pool = SealedPool.Generate(product, $"{playerSeed}|ai{opponentIndex}");
            Build(pool, difficulty, preferredLeaderId);
            return pool;
        }

        /// <summary>Construct a legal 40-card deck in place. This operation is synchronous and pure C#;
        /// it does not animate or expose the A.I.'s selection process.</summary>
        public static void Build(SealedPool pool, string difficulty = "advanced", string preferredLeaderId = null)
        {
            if (pool == null) return;
            pool.ClearDeck();

            var profile = Profile(difficulty);
            var counts = pool.PoolCounts();
            var mode = pool.LeaderMode;
            if (mode == SealedLeaderMode.RainbowLuffy)
            {
                SealedLeaderRules.EnsureRegistered();
                pool.LeaderId = SealedLeaderRules.RainbowLuffyId;
            }

            var leaders = mode == SealedLeaderMode.RainbowLuffy
                ? new List<string> { SealedLeaderRules.RainbowLuffyId }
                : SealedLeaderRules.LegalLeaders(mode, pool);
            if (leaders.Count == 0) return;

            string requested = !string.IsNullOrWhiteSpace(preferredLeaderId)
                ? preferredLeaderId : pool.LeaderId;
            if (!string.IsNullOrWhiteSpace(requested)
                && leaders.Contains(requested, StringComparer.OrdinalIgnoreCase))
                pool.LeaderId = leaders.First(id => string.Equals(id, requested, StringComparison.OrdinalIgnoreCase));
            else
                pool.LeaderId = ChooseLeader(leaders, counts, profile);

            var leader = CardData.GetCard(pool.LeaderId);
            var candidates = new List<Candidate>();
            foreach (var kv in counts.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
            {
                var def = CardData.GetCard(kv.Key);
                if (def == null || def.Type == "leader") continue;
                for (int copy = 0; copy < kv.Value; copy++)
                {
                    double score = CardScore(def, leader, counts, profile);
                    if (profile.Noise > 0)
                    {
                        var rng = new SealedRng($"{pool.Seed}|build|{profile.Name}|{kv.Key}|{copy}");
                        score += (rng.NextDouble() * 2.0 - 1.0) * profile.Noise;
                    }
                    candidates.Add(new Candidate { Id = kv.Key, Def = def, CopyIndex = copy, Score = score });
                }
            }
            candidates = candidates
                .OrderByDescending(c => c.Score)
                .ThenBy(c => c.Id, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.CopyIndex)
                .ToList();

            int target = pool.Product()?.DeckSize ?? 40;
            var takenByBand = new Dictionary<int, int>();
            int characters = 0;
            int nonCharacters = 0;
            int Band(CardDef d) => Math.Min(7, Math.Max(0, d.Cost));

            bool AddCandidate(Candidate c)
            {
                if (pool.DeckCount() >= target || pool.Add(c.Id) <= 0) return false;
                int band = Band(c.Def);
                takenByBand[band] = takenByBand.TryGetValue(band, out var n) ? n + 1 : 1;
                if (c.Def.Type == "character") characters++; else nonCharacters++;
                return true;
            }

            // High-skill builders treat playable 2K counters and Blockers as the first pile. Lower
            // tiers occasionally miss one, deterministically, before continuing through the same pool.
            foreach (var c in candidates.Where(IsPriorityLimitedCard))
            {
                if (pool.DeckCount() >= target) break;
                if (profile.PriorityKeepChance < 1.0)
                {
                    var keep = new SealedRng($"{pool.Seed}|priority|{profile.Name}|{c.Id}|{c.CopyIndex}");
                    if (keep.NextDouble() > profile.PriorityKeepChance) continue;
                }
                AddCandidate(c);
            }

            // Fill around a real curve. Non-Character slots are capped so a pool full of tempting
            // Events/Stages cannot leave the bot without enough attackers.
            foreach (var c in candidates)
            {
                if (pool.DeckCount() >= target) break;
                if (c.Def.Type != "character" && nonCharacters >= target - profile.MinCharacters) continue;
                int band = Band(c.Def);
                int used = takenByBand.TryGetValue(band, out var n) ? n : 0;
                if (used >= profile.CurveQuota[band]) continue;
                AddCandidate(c);
            }

            // Thin pools can miss a band. Relax quotas but retain score order and the Character floor.
            foreach (var c in candidates)
            {
                if (pool.DeckCount() >= target) break;
                if (c.Def.Type != "character" && nonCharacters >= target - profile.MinCharacters) continue;
                AddCandidate(c);
            }
            // Absolute legality fallback: if the pool cannot satisfy the desired type shape, use the
            // best remaining cards rather than return an undersized deck.
            foreach (var c in candidates)
            {
                if (pool.DeckCount() >= target) break;
                AddCandidate(c);
            }

            EnsureCounters(pool, candidates, profile.MinCounterCards, leader, counts, profile);
            FixCurve(pool, candidates, profile.TargetAverageCost, leader, counts, profile);
        }

        private static TierProfile Profile(string difficulty)
        {
            switch ((difficulty ?? "advanced").Trim().ToLowerInvariant())
            {
                case "beginner":
                    return new TierProfile
                    {
                        Name = "beginner", MinCharacters = 24, MinCounterCards = 7,
                        TargetAverageCost = 5.0, SynergyWeight = 0.25, Noise = 3.2,
                        PriorityKeepChance = 0.62,
                        CurveQuota = Quota(5, 7, 8, 8, 7, 5, 4, 4),
                    };
                case "intermediate":
                    return new TierProfile
                    {
                        Name = "intermediate", MinCharacters = 27, MinCounterCards = 10,
                        TargetAverageCost = 4.4, SynergyWeight = 0.65, Noise = 1.25,
                        PriorityKeepChance = 0.84,
                        CurveQuota = Quota(4, 5, 7, 8, 8, 6, 4, 3),
                    };
                default:
                    return new TierProfile
                    {
                        Name = "advanced", MinCharacters = 30, MinCounterCards = 13,
                        TargetAverageCost = 4.0, SynergyWeight = 1.0, Noise = 0,
                        PriorityKeepChance = 1.0,
                        CurveQuota = Quota(3, 4, 6, 7, 8, 7, 5, 3),
                    };
            }
        }

        private static Dictionary<int, int> Quota(int c0, int c1, int c2, int c3,
            int c4, int c5, int c6, int c7) => new Dictionary<int, int>
        {
            [0] = c0, [1] = c1, [2] = c2, [3] = c3,
            [4] = c4, [5] = c5, [6] = c6, [7] = c7,
        };

        private static string ChooseLeader(List<string> leaders, Dictionary<string, int> pool,
            TierProfile profile)
        {
            string best = leaders[0];
            double bestScore = double.NegativeInfinity;
            foreach (string id in leaders)
            {
                var leader = CardData.GetCard(id);
                if (leader == null) continue;
                double score = leader.Power / 1000.0 + (leader.Life ?? 0) * 0.9;
                if (!string.IsNullOrWhiteSpace(leader.Effect)) score += 1.0;
                foreach (var kv in pool)
                {
                    var card = CardData.GetCard(kv.Key);
                    if (card == null || card.Type == "leader") continue;
                    score += LeaderCompatibility(card, leader) * kv.Value * profile.SynergyWeight * 0.30;
                    score += FeatureSupport(card, leader.Effect, pool) * kv.Value * profile.SynergyWeight * 0.08;
                }
                if (score > bestScore || (Math.Abs(score - bestScore) < 0.0001
                    && string.Compare(id, best, StringComparison.OrdinalIgnoreCase) < 0))
                { best = id; bestScore = score; }
            }
            return best;
        }

        private static bool IsPriorityLimitedCard(Candidate c) =>
            c?.Def != null && (c.Def.Counter >= 2000 || SealedPool.HasKeyword(c.Def, "Blocker"));

        private static double CardScore(CardDef def, CardDef leader, Dictionary<string, int> pool,
            TierProfile profile)
        {
            if (def == null) return double.NegativeInfinity;
            string effect = (def.Effect ?? "").ToLowerInvariant();
            double score = 0;

            if (def.Type == "character")
            {
                score += def.Power / 1250.0;
                double efficientPower = def.Power - def.Cost * 1000.0;
                score += Math.Max(-1.0, Math.Min(2.2, efficientPower / 1000.0));
                if (string.IsNullOrWhiteSpace(def.Effect) && def.Cost >= 3 && efficientPower >= 2000)
                    score += 1.8; // the high-stat vanilla bodies repeatedly recommended for sealed
                if (def.Cost <= 2 && def.Power < 4000 && def.Counter < 2000
                    && !SealedPool.HasKeyword(def, "Blocker")) score -= 1.3;
            }
            else if (def.Type == "event") score += 0.45;
            else if (def.Type == "stage") score -= 0.35;

            if (SealedPool.HasKeyword(def, "Blocker")) score += 4.4;
            if (SealedPool.HasKeyword(def, "Rush")) score += 2.0;
            if (SealedPool.HasKeyword(def, "Double Attack")) score += 1.5;
            if (SealedPool.HasKeyword(def, "Banish")) score += 0.8;

            if (effect.Contains("k.o.") || effect.Contains("bottom of") ||
                (effect.Contains("return") && effect.Contains("hand"))) score += 3.1;
            if (effect.Contains("power -") || effect.Contains("power −")) score += 1.7;
            if (effect.Contains("rest") && effect.Contains("opponent")) score += 1.1;
            if (effect.Contains("draw")) score += 1.25;
            if (effect.Contains("search") || effect.Contains("look at")) score += 0.65;
            if (effect.Contains("play up to")) score += 0.7;

            if (def.Counter >= 2000) score += 4.0;
            else if (def.Counter >= 1000) score += 1.35;
            if (!string.IsNullOrWhiteSpace(def.Trigger)) score += 0.9;

            // Four-to-six DON!! cards are limited's reliable board-development core. Big finishers
            // remain valuable, but too many become uncastable; tiny bodies need utility to justify slots.
            if (def.Cost >= 4 && def.Cost <= 6) score += 1.0;
            if (def.Cost >= 8) score -= 1.35;
            else if (def.Cost == 7) score -= 0.35;

            score += LeaderCompatibility(def, leader) * profile.SynergyWeight;
            score += FeatureSupport(def, (def.Effect ?? "") + "\n" + (def.Trigger ?? ""), pool)
                * profile.SynergyWeight;

            // Beginner builders overrate shiny pulls a little; stronger tiers judge gameplay text.
            if (profile.Name == "beginner")
                score += def.Rarity == Rarity.SecretRare ? 1.0
                    : def.Rarity == Rarity.SuperRare ? 0.6
                    : def.Rarity == Rarity.Rare ? 0.25 : 0;
            return score;
        }

        /// <summary>Reward live Leader gates and heavily discount text that the selected Leader cannot
        /// turn on. Rainbow Luffy's wildcard identity naturally satisfies name/type gates.</summary>
        private static double LeaderCompatibility(CardDef card, CardDef leader)
        {
            if (card == null || leader == null) return 0;
            string text = (card.Effect ?? "") + "\n" + (card.Trigger ?? "");
            if (string.IsNullOrWhiteSpace(text)) return 0;
            double score = 0;
            foreach (Match m in LeaderFeatureGate.Matches(text))
                score += leader.WildcardIdentity || leader.HasFeature(m.Groups[1].Value.Trim()) ? 1.5 : -3.0;
            foreach (Match m in LeaderQuotedFeatureGate.Matches(text))
                score += leader.WildcardIdentity || leader.HasFeature(m.Groups[1].Value.Trim()) ? 1.5 : -3.0;
            foreach (Match m in LeaderNameGate.Matches(text))
                score += leader.WildcardIdentity || string.Equals(leader.Name, m.Groups[1].Value.Trim(),
                    StringComparison.OrdinalIgnoreCase) ? 1.5 : -3.0;
            var colors = SealedPool.SplitColors(leader.Color).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (text.IndexOf("your Leader is multicolored", StringComparison.OrdinalIgnoreCase) >= 0)
                score += colors.Count > 1 ? 1.5 : -3.0;
            foreach (Match m in LeaderColorGate.Matches(text))
                score += colors.Contains(m.Groups[1].Value) ? 1.25 : -2.5;
            return score;
        }

        /// <summary>Searchers and type-based play effects are only premium when the six-pack pool
        /// contains enough real hits. This steers Advanced toward synergies it can execute through its
        /// ordinary legal-action/effect resolver and away from attractive-looking whiffs.</summary>
        private static double FeatureSupport(CardDef source, string text, Dictionary<string, int> pool)
        {
            if (source == null || string.IsNullOrWhiteSpace(text) || pool == null) return 0;
            string lower = text.ToLowerInvariant();
            bool targetEffect = lower.Contains("search") || lower.Contains("look at")
                || lower.Contains("reveal") || lower.Contains("play up to") || lower.Contains("add up to");
            if (!targetEffect) return 0;

            double total = 0;
            foreach (Match m in FeatureReference.Matches(text))
            {
                string feature = m.Groups[1].Value.Trim();
                int hits = pool.Sum(kv => CardData.GetCard(kv.Key)?.HasFeature(feature) == true ? kv.Value : 0);
                total += hits >= 8 ? 1.25 : hits >= 5 ? 0.8 : hits >= 3 ? 0.3 : -1.4;
            }
            return total;
        }

        private static void EnsureCounters(SealedPool pool, List<Candidate> candidates, int floor,
            CardDef leader, Dictionary<string, int> counts, TierProfile profile)
        {
            int Counters() => pool.Deck.Sum(kv => (CardData.GetCard(kv.Key)?.Counter ?? 0) > 0 ? kv.Value : 0);
            foreach (var c in candidates.Where(c => c.Def.Counter > 0).OrderByDescending(c => c.Score))
            {
                if (Counters() >= floor) break;
                if (pool.Remaining(c.Id) <= 0) continue;
                var worst = pool.Deck.Keys
                    .Select(id => new { id, def = CardData.GetCard(id) })
                    .Where(x => x.def != null && x.def.Counter <= 0)
                    .OrderBy(x => CardScore(x.def, leader, counts, profile))
                    .FirstOrDefault();
                if (worst == null) break;
                pool.Remove(worst.id);
                pool.Add(c.Id);
            }
        }

        private static void FixCurve(SealedPool pool, List<Candidate> candidates, double targetAverage,
            CardDef leader, Dictionary<string, int> counts, TierProfile profile)
        {
            var affordable = candidates.Where(c => c.Def.Cost <= 5)
                .OrderByDescending(c => c.Score).ToList();
            for (int guard = 0; guard < 40 && pool.DeckStats().AverageCost > targetAverage; guard++)
            {
                var dearest = pool.Deck.Keys
                    .Select(id => new { id, def = CardData.GetCard(id) })
                    .Where(x => x.def != null)
                    .OrderByDescending(x => x.def.Cost)
                    .ThenBy(x => CardScore(x.def, leader, counts, profile))
                    .FirstOrDefault();
                if (dearest == null || dearest.def.Cost <= 5) return;
                var replacement = affordable.FirstOrDefault(c => pool.Remaining(c.Id) > 0);
                if (replacement == null || replacement.Def.Cost >= dearest.def.Cost) return;
                pool.Remove(dearest.id);
                if (pool.Add(replacement.Id) == 0) { pool.Add(dearest.id); return; }
            }
        }
    }
}
