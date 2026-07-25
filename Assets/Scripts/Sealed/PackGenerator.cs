// One Piece TCG — Sealed / Pre-Release: deterministic booster-pack generation.
//
// Pure C#, no UnityEngine. Everything is a pure function of (product, seed), so:
//   • the same seed always yields byte-identical packs on every machine — which is what makes
//     "today's OP16 seed" shareable and what makes a sealed tournament fair;
//   • Tools/Sim can open millions of boxes headlessly and check the pull rates statistically
//     (`sealedtest`) without ever opening Unity.
//
// The RNG is a local copy of the engine's SeededRng (mulberry32-style) rather than System.Random,
// for the same reason the engine uses it: System.Random's algorithm is not contractually stable
// across .NET versions, so a shared seed could silently produce different packs for different
// players. This one is fully specified by its arithmetic.

using System;
using System.Collections.Generic;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sealed
{
    /// <summary>A single card pulled from a pack, in pull order.</summary>
    public sealed class PulledCard
    {
        public string CardId;
        public string RarityCode;
        /// <summary>An alt-art / parallel print of the same card. Gameplay-identical; art differs.</summary>
        public bool IsParallel;
        /// <summary>0-based index of the pack this came from, so pack history survives forever.</summary>
        public int PackIndex;
        /// <summary>Position within the pack (0-based), so the reveal can build to the hit.</summary>
        public int SlotIndex;

        public string Name => CardData.GetCard(CardId)?.Name ?? CardId;

        /// <summary>Hit = worth pausing on during the reveal (SR and above, or any parallel).</summary>
        public bool IsHit =>
            IsParallel
            || RarityCode == Rarity.SuperRare
            || RarityCode == Rarity.SecretRare
            || RarityCode == Rarity.Special
            || RarityCode == Rarity.TreasureRare;
    }

    /// <summary>One opened pack, retained verbatim for the lifetime of the pool.</summary>
    public sealed class SealedPack
    {
        public int Index;
        public List<PulledCard> Cards = new List<PulledCard>();

        /// <summary>The card the reveal should build up to — the best hit, else the last card.</summary>
        public PulledCard Headline()
        {
            int Rank(PulledCard c) =>
                c.RarityCode == Rarity.SecretRare ? 5
                : c.RarityCode == Rarity.TreasureRare ? 4
                : c.RarityCode == Rarity.Special ? 3
                : c.RarityCode == Rarity.SuperRare ? 2
                : c.IsParallel ? 1 : 0;
            return Cards.OrderByDescending(Rank).ThenByDescending(c => c.SlotIndex).FirstOrDefault();
        }
    }

    public static class PackGenerator
    {
        /// <summary>Open <see cref="SealedProduct.PackCount"/> packs for this product under
        /// <paramref name="seed"/>. Deterministic: same (product, seed) → same packs, always.</summary>
        public static List<SealedPack> Open(SealedProduct product, string seed)
        {
            if (product == null) return new List<SealedPack>();
            return OpenCount(product, seed, product.PackCount);
        }

        /// <summary>Open an arbitrary number of packs — used by the statistical tests (a full box or
        /// case) and by any future product with a different pack count.</summary>
        public static List<SealedPack> OpenCount(SealedProduct product, string seed, int packCount)
        {
            var packs = new List<SealedPack>();
            if (product == null) return packs;

            var byRarity = product.PoolByRarity();
            var col = product.Collation;

            for (int p = 0; p < packCount; p++)
            {
                // Seed PER PACK, so pack 4 of a 6-pack pool is identical whether you opened 4 or 40.
                // That keeps a shared seed stable even if a product's pack count ever changes.
                var rng = new SealedRng($"{seed}|{product.SetCode}|pack{p}");
                var pack = new SealedPack { Index = p };
                int slotIndex = 0;

                foreach (var slot in col.Slots)
                {
                    for (int i = 0; i < slot.Count; i++)
                    {
                        string rarity = slot.FixedRarity ?? PickWeighted(slot.Weighted, rng);
                        string cardId = PickCard(byRarity, rarity, rng);
                        // A set may legitimately lack a weighted rarity (no SP in the set, say).
                        // Fall back down the rarity ladder rather than dropping a card from the pack.
                        if (cardId == null)
                        {
                            rarity = FallbackRarity(byRarity, rarity);
                            cardId = PickCard(byRarity, rarity, rng);
                        }
                        if (cardId == null) continue;   // set has nothing at all — cannot happen for a playable product

                        pack.Cards.Add(new PulledCard
                        {
                            CardId = cardId,
                            RarityCode = rarity,
                            IsParallel = rng.NextDouble() < col.ParallelChance,
                            PackIndex = p,
                            SlotIndex = slotIndex++,
                        });
                    }
                }
                packs.Add(pack);
            }

            EnsureGuaranteedLeaders(product, byRarity, packs, seed);
            return packs;
        }

        /// <summary>A kit with no Leader cannot produce a legal deck, and that happens on 1.56% of
        /// seeds (see SealedProduct.GuaranteedLeaders). Promote the last variable slot of the last
        /// packs to Leaders until the guarantee is met — deterministic, and a no-op on the ~98% of
        /// seeds that already opened one.</summary>
        private static void EnsureGuaranteedLeaders(SealedProduct product,
            Dictionary<string, List<string>> byRarity, List<SealedPack> packs, string seed)
        {
            int need = product.GuaranteedLeaders;
            if (need <= 0 || packs.Count == 0) return;
            if (!byRarity.TryGetValue(Rarity.Leader, out var leaders) || leaders.Count == 0) return;

            int have = packs.Sum(p => p.Cards.Count(c => c.RarityCode == Rarity.Leader));
            for (int p = packs.Count - 1; p >= 0 && have < need; p--)
            {
                var pack = packs[p];
                // The variable slot is the last card in the pack — the one carrying the hits.
                var slot = pack.Cards.LastOrDefault();
                if (slot == null || slot.RarityCode == Rarity.Leader) continue;
                var rng = new SealedRng($"{seed}|{product.SetCode}|guaranteedLeader{p}");
                slot.CardId = leaders[rng.NextInt(leaders.Count)];
                slot.RarityCode = Rarity.Leader;
                have++;
            }
        }

        /// <summary>Resolve a weighted slot. Weights need not sum to 1 — they are normalized.</summary>
        private static string PickWeighted(Dictionary<string, double> weights, SealedRng rng)
        {
            if (weights == null || weights.Count == 0) return Rarity.Rare;
            double total = weights.Values.Sum();
            if (total <= 0) return Rarity.Rare;
            double roll = rng.NextDouble() * total;
            foreach (var kv in weights.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                roll -= kv.Value;
                if (roll <= 0) return kv.Key;
            }
            return weights.Keys.Last();
        }

        private static string PickCard(Dictionary<string, List<string>> byRarity, string rarity, SealedRng rng)
        {
            if (rarity == null || !byRarity.TryGetValue(rarity, out var list) || list.Count == 0) return null;
            return list[rng.NextInt(list.Count)];
        }

        /// <summary>When a set has no cards of the rolled rarity, step DOWN to the next one that exists,
        /// so the pack still contains the right number of cards.</summary>
        private static string FallbackRarity(Dictionary<string, List<string>> byRarity, string rarity)
        {
            var ladder = new[]
            {
                Rarity.SecretRare, Rarity.TreasureRare, Rarity.Special, Rarity.SuperRare,
                Rarity.Leader, Rarity.Rare, Rarity.Uncommon, Rarity.Common,
            };
            int start = Array.IndexOf(ladder, rarity);
            if (start < 0) start = 0;
            for (int i = start; i < ladder.Length; i++)
                if (byRarity.TryGetValue(ladder[i], out var l) && l.Count > 0) return ladder[i];
            return byRarity.Keys.FirstOrDefault();
        }

        /// <summary>A fresh shareable seed. Digits only, so it is easy to read aloud and retype.</summary>
        public static string NewSeed()
        {
            // Time-based but formatted as a plain 9-digit number, matching the "842918361" shape a
            // player expects to be able to type into a friend's client.
            long t = DateTime.UtcNow.Ticks;
            uint h = SealedRng.HashString(t.ToString());
            return (h % 1000000000u).ToString("D9");
        }

        /// <summary>Accepts what a player might paste: trims spaces and keeps it non-empty.</summary>
        public static string NormalizeSeed(string seed)
        {
            seed = (seed ?? "").Trim();
            return string.IsNullOrEmpty(seed) ? NewSeed() : seed;
        }
    }

    /// <summary>Deterministic, fully-specified PRNG. Mirrors the engine's SeededRng so sealed
    /// generation is reproducible across machines and .NET versions.</summary>
    public sealed class SealedRng
    {
        private uint _value;
        public SealedRng(string seed) { _value = HashString(seed); }

        public double NextDouble()
        {
            _value = unchecked(_value + 0x6d2b79f5u);
            uint next = _value;
            next = unchecked((next ^ (next >> 15)) * (next | 1u));
            next ^= unchecked(next + (next ^ (next >> 7)) * (next | 61u));
            return (next ^ (next >> 14)) / 4294967296.0;
        }

        /// <summary>Uniform integer in [0, maxExclusive).</summary>
        public int NextInt(int maxExclusive) =>
            maxExclusive <= 0 ? 0 : Math.Min(maxExclusive - 1, (int)(NextDouble() * maxExclusive));

        public static uint HashString(string s)
        {
            unchecked
            {
                uint h = 2166136261u;
                if (s != null) foreach (char c in s) { h ^= c; h *= 16777619u; }
                return h;
            }
        }
    }
}
