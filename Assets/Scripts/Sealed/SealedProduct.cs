// One Piece TCG — Sealed / Pre-Release: the PRODUCT catalogue and its pack collation.
//
// Pure C#, no UnityEngine — so Tools/Sim can generate and statistically verify boxes headlessly
// (see `sealedtest`) without opening the editor. Everything here is data + deterministic rules.
//
// SET MEMBERSHIP comes from the CARD ID PREFIX ("OP16-091" -> "OP16"), never from the scraped
// seriesCode. Two reasons, both verified against the shipped library:
//   1. The scrape MERGES some sets — OP14 and OP15 both carry seriesCode "OP14EB04"/"OP15EB04".
//   2. A set's official page lists REPRINTS from other sets. OP16's page carries an EB04, an OP10,
//      an OP11, two OP14s and an ST15 — none of which were ever in an OP16 pack.
// Filtering by prefix reproduces the real set pool exactly: for OP16 it yields
// L 6 / C 45 / UC 30 / R 26 / SR 10 / SEC 2 / SP 6 / TR 1, which matches the published composition
// for a One Piece booster set card-for-card.

using System;
using System.Collections.Generic;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sealed
{
    /// <summary>Rarity codes as they appear in the card library.</summary>
    public static class Rarity
    {
        public const string Common = "C";
        public const string Uncommon = "UC";
        public const string Rare = "R";
        public const string SuperRare = "SR";
        public const string SecretRare = "SEC";
        public const string Leader = "L";
        public const string Special = "SP CARD";
        public const string TreasureRare = "TR";
        public const string Promo = "P";
    }

    /// <summary>One slot in a pack. Either a fixed rarity repeated <see cref="Count"/> times, or a
    /// single weighted slot that resolves to one of several rarities.</summary>
    public sealed class PackSlot
    {
        public int Count = 1;
        public string FixedRarity;                       // null when Weighted is used
        public Dictionary<string, double> Weighted;      // rarity -> probability (need not sum to 1; normalized)

        public static PackSlot Fixed(string rarity, int count) =>
            new PackSlot { FixedRarity = rarity, Count = count };

        public static PackSlot Weights(Dictionary<string, double> w) =>
            new PackSlot { Weighted = w, Count = 1 };
    }

    /// <summary>How one pack of a product is built.
    ///
    /// SOURCING — the box-level hit rates below are well attested across independent public guides
    /// (TCG Talk, Card Gamer, Slab-Z, Archive Drops) and agree with each other:
    ///     Super Rare ~8 per 24-pack box   (~1 in 3 packs)
    ///     Secret Rare ~1 per box
    ///     Leader ~12 per box              (~1 in 2 packs)
    ///     Parallel / alt art ~2 per box
    ///     Special (SP) ~1 per CASE of 6 boxes
    ///     Rare: at least 1 guaranteed per pack; an SR replaces the second Rare
    ///
    /// The 6 C / 3 UC / 2 R / 1 variable SPLIT is DERIVED, not quoted: it is the composition that
    /// makes those box rates come out exactly right over 24 packs
    /// (6*24 + 3*24 + 2*24 + 24 = 144 + 72 + 48 + 24 = 288 = 24 packs * 12 cards), with the variable
    /// slot carrying the Leader/SR/SEC/SP hits and an extra Rare otherwise. One source suggests
    /// Japanese packs run closer to 1 Uncommon per pack, which would imply a different fill — so
    /// treat the split as a well-reasoned default, not gospel. It is data: correcting a set is an
    /// edit to <see cref="SealedCatalog"/> with no code change.</summary>
    public sealed class PackCollation
    {
        public int CardsPerPack = 12;
        public int PacksPerBox = 24;
        public List<PackSlot> Slots = new List<PackSlot>();
        /// <summary>Chance any given card is replaced by its parallel/alt-art print (~2 per box).</summary>
        public double ParallelChance = 2.0 / (24 * 12);
        /// <summary>True when these numbers are derived rather than published, so the UI can say so.</summary>
        public bool RatesAreApproximate = true;

        /// <summary>The standard modern booster: 6 C, 3 UC, 2 R, and one variable slot carrying the hits.
        /// Weights are per-pack probabilities implied by the per-box rates above.</summary>
        public static PackCollation StandardBooster() => new PackCollation
        {
            CardsPerPack = 12,
            PacksPerBox = 24,
            Slots =
            {
                PackSlot.Fixed(Rarity.Common, 6),
                PackSlot.Fixed(Rarity.Uncommon, 3),
                PackSlot.Fixed(Rarity.Rare, 2),
                PackSlot.Weights(new Dictionary<string, double>
                {
                    { Rarity.Leader,     12.0 / 24 },   // ~12 per box
                    { Rarity.SuperRare,   8.0 / 24 },   // ~8 per box
                    { Rarity.SecretRare,  1.0 / 24 },   // ~1 per box
                    { Rarity.Special,     1.0 / 144 },  // ~1 per case (6 boxes)
                    { Rarity.Rare,        2.958 / 24 }, // remainder -> the "second Rare" case
                }),
            },
        };
    }

    /// <summary>A sealed product the player can choose: which set, how many packs, and the deck rules
    /// that apply to the pool it produces.</summary>
    public sealed class SealedProduct
    {
        public string SetCode;          // "OP16" — also the card-ID prefix
        public string DisplayName;      // "The Time of Battle"
        public string ReleaseDate;      // display only
        public int PackCount = 6;       // a prerelease kit is 6 packs
        public PackCollation Collation = PackCollation.StandardBooster();

        // Sealed deck-construction rules. Prerelease is 40 cards + a Leader from your own pool, and
        // the copy limit is effectively whatever you opened.
        // SEALED DECK RULES — from Bandai's official tournament rules manual, NOT the constructed rules.
        // Sealed deliberately relaxes almost everything:
        //   • 40-card deck (constructed is 50), plus the usual 10-card DON!! deck.
        //   • COLOUR RESTRICTIONS DO NOT APPLY. In constructed, every card must share a colour with
        //     your Leader; in sealed you may play any colour regardless of Leader. This is not a minor
        //     detail — a 6-pack pool is ~72 cards spread over six colours, so enforcing the colour rule
        //     would make a legal 40-card deck impossible from most pools.
        //   • NO COPY LIMIT: "as many copies of cards with the same card number as they like" — the
        //     only bound is how many you actually opened.
        //   • No banned or restricted cards.
        // Card effects that reference colours still work normally; it is only DECKBUILDING that relaxes.
        public int DeckSize = 40;
        public int MaxCopies = int.MaxValue;
        /// <summary>The official rules let a player BRING a Leader, including from older sets. A
        /// self-contained digital sealed run has nowhere to bring one from, so the default is
        /// pool-only; flip this for a product that should allow any Leader.</summary>
        public bool LeaderMustComeFromPool = true;

        /// <summary>Minimum Leaders the kit must contain. The Leader slot averages ~1 per 2 packs, so a
        /// pure-random 6-pack kit opens ZERO Leaders 1.56% of the time ((1 - 12/24)^6) — and with no
        /// Leader a sealed deck cannot legally be built at all. Real prerelease kits sidestep this by
        /// including a fixed Leader, so the generator guarantees one the same way: if the random pulls
        /// produced none, the last variable slot is promoted to a Leader. Deterministic, and it only
        /// fires on the 1.56% of seeds that would otherwise be unplayable.</summary>
        public int GuaranteedLeaders = 1;

        /// <summary>Every card in this set, by card id. Derived from the ID prefix — see the file
        /// header for why seriesCode is not used.</summary>
        public List<string> CardPool()
        {
            string prefix = SetCode + "-";
            return CardData.Library.Keys
                .Where(id => id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>Set cards grouped by rarity, which is what pack generation draws from.</summary>
        public Dictionary<string, List<string>> PoolByRarity()
        {
            var byRarity = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in CardPool())
            {
                var def = CardData.GetCard(id);
                string r = string.IsNullOrEmpty(def?.Rarity) ? Rarity.Common : def.Rarity;
                if (!byRarity.TryGetValue(r, out var list)) byRarity[r] = list = new List<string>();
                list.Add(id);
            }
            return byRarity;
        }

        /// <summary>Leaders legal for a deck built from this product (the set's own Leaders).</summary>
        public List<string> LegalLeaders() =>
            CardPool().Where(id => CardData.GetCard(id)?.Type == "leader").ToList();

        public int CardCount() => CardPool().Count;

        /// <summary>True when the set actually has enough cards in every rarity its collation asks for.
        /// Guards the picker against listing a set the library only partially covers.</summary>
        public bool IsPlayable(out string reason)
        {
            var byRarity = PoolByRarity();
            foreach (var slot in Collation.Slots)
            {
                var needed = slot.FixedRarity != null
                    ? new[] { slot.FixedRarity }
                    : slot.Weighted.Keys.ToArray();
                foreach (var r in needed)
                {
                    // A weighted slot may legitimately reference a rarity this set lacks (e.g. no SP);
                    // only a FIXED slot is a hard requirement.
                    if (slot.FixedRarity == null) continue;
                    if (!byRarity.TryGetValue(r, out var list) || list.Count == 0)
                    { reason = $"{SetCode} has no {r} cards in the library."; return false; }
                }
            }
            if (LegalLeaders().Count == 0) { reason = $"{SetCode} has no Leader cards."; return false; }
            reason = null;
            return true;
        }
    }

    /// <summary>The sealed products offered in the mode. Booster sets only — starter decks and promo
    /// buckets are not sealed products.</summary>
    public static class SealedCatalog
    {
        // Display names for the booster sets. A set not listed here still works (it falls back to its
        // code) — the table exists so the picker reads like the shelf.
        private static readonly Dictionary<string, (string name, string released)> Meta =
            new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
            {
                ["OP01"] = ("Romance Dawn", "Dec 2022"),
                ["OP02"] = ("Paramount War", "Mar 2023"),
                ["OP03"] = ("Pillars of Strength", "Jun 2023"),
                ["OP04"] = ("Kingdoms of Intrigue", "Sep 2023"),
                ["OP05"] = ("Awakening of the New Era", "Dec 2023"),
                ["OP06"] = ("Wings of the Captain", "Mar 2024"),
                ["OP07"] = ("500 Years in the Future", "Jun 2024"),
                ["OP08"] = ("Two Legends", "Sep 2024"),
                ["OP09"] = ("Emperors in the New World", "Dec 2024"),
                ["OP10"] = ("Royal Blood", "Mar 2025"),
                ["OP11"] = ("A Fist of Divine Speed", "Jun 2025"),
                ["OP12"] = ("Legacy of the Master", "Sep 2025"),
                ["OP13"] = ("The New Emperor", "Dec 2025"),
                ["OP14"] = ("Beyond the Dawn", "Mar 2026"),
                ["OP15"] = ("Crown of Ambition", "Jun 2026"),
                ["OP16"] = ("The Time of Battle", "Sep 2026"),
                ["EB01"] = ("Memorial Collection", "Jul 2024"),
                ["EB02"] = ("Anime 25th Collection", "Feb 2025"),
                ["EB03"] = ("Reflections of Bonds", "Nov 2025"),
                ["EB04"] = ("Ultra Deck Collection", "Mar 2026"),
            };

        /// <summary>Every booster set the loaded card library can actually support, newest first.
        /// Built from the library rather than hardcoded, so a new set drops in automatically.</summary>
        public static List<SealedProduct> Available()
        {
            var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in CardData.Library.Keys)
            {
                int dash = id.IndexOf('-');
                if (dash <= 0) continue;
                string prefix = id.Substring(0, dash).ToUpperInvariant();
                // Booster sets only: OPxx and EBxx. ST/PRB/P are not sealed products.
                if (prefix.StartsWith("OP") || prefix.StartsWith("EB")) codes.Add(prefix);
            }

            var products = new List<SealedProduct>();
            foreach (var code in codes)
            {
                Meta.TryGetValue(code, out var meta);
                var p = new SealedProduct
                {
                    SetCode = code,
                    DisplayName = meta.name ?? code,
                    ReleaseDate = meta.released ?? "",
                };
                if (p.IsPlayable(out _)) products.Add(p);
            }
            // Newest first: OP16 above OP01, and EB after OP of the same number.
            return products
                .OrderByDescending(p => p.SetCode.StartsWith("OP", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(p => p.SetCode, StringComparer.Ordinal)
                .ToList();
        }

        public static SealedProduct Find(string setCode) =>
            Available().FirstOrDefault(p => string.Equals(p.SetCode, setCode, StringComparison.OrdinalIgnoreCase));
    }
}
