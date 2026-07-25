// One Piece TCG — Sealed / Pre-Release: the deck builder's FILTER / SORT / GROUP model.
//
// Pure C#, no UnityEngine, so the whole "what does the grid show right now" question is testable
// headlessly instead of by clicking chips. The UI layer owns only pixels; every decision about which
// cards appear, in what order, and under which headings lives here.
//
// Filters STACK (Character + 2K + Rare = all three must hold). Quick views are a separate axis: they
// regroup whatever survived the filters under headings, which is what makes "show me my pool by
// pack" and "show me my curve" one click each rather than a filter dance.
//
// Keyword filters read the PARSED keyword list first (SealedPool.HasKeyword), so they match what the
// engine actually grants rather than scraping effect text.

using System;
using System.Collections.Generic;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sealed
{
    public enum SealedSort
    {
        Cost, Power, Counter, Rarity, Color, Alphabetical, CardNumber, NewestPull, Copies, DeckCopies,
    }

    public enum SealedQuickView
    {
        None, Counter, Curve, Type, Rarity, Pack, Color, Keyword,
    }

    /// <summary>One toggleable filter chip. Chips within a GROUP are OR'd; groups are AND'd — so
    /// "Red + Blue + Character" means "(Red or Blue) and Character", which is what a player expects
    /// when they tap two colours.</summary>
    public sealed class SealedFilter
    {
        public string Group;         // "type" | "counter" | "keyword" | "color" | "rarity"
        public string Key;           // "character", "2000", "Blocker", "Red", "SR", ...
        public string Label;
        public Func<CardDef, bool> Match;

        public SealedFilter(string group, string key, string label, Func<CardDef, bool> match)
        { Group = group; Key = key; Label = label; Match = match; }
    }

    public static class SealedFilters
    {
        public static readonly string[] Colors = { "Red", "Green", "Blue", "Purple", "Black", "Yellow" };
        public static readonly string[] Rarities = { "C", "UC", "R", "SR", "SEC", "SP CARD" };
        public static readonly string[] Keywords = { "Blocker", "Rush", "Double Attack", "Banish" };

        /// <summary>Every chip the builder offers, in display order.</summary>
        public static List<SealedFilter> All()
        {
            var list = new List<SealedFilter>
            {
                new SealedFilter("type", "character", "Characters", d => d.Type == "character"),
                new SealedFilter("type", "event", "Events", d => d.Type == "event"),
                new SealedFilter("type", "stage", "Stages", d => d.Type == "stage"),
                new SealedFilter("type", "leader", "Leader", d => d.Type == "leader"),

                new SealedFilter("counter", "none", "No Counter", d => d.Counter <= 0),
                new SealedFilter("counter", "1000", "1K", d => d.Counter == 1000),
                new SealedFilter("counter", "2000", "2K", d => d.Counter >= 2000),
            };

            foreach (var k in Keywords)
            {
                var kw = k;
                list.Add(new SealedFilter("keyword", kw, kw, d => SealedPool.HasKeyword(d, kw)));
            }
            list.Add(new SealedFilter("keyword", "Trigger", "Trigger", d => !string.IsNullOrWhiteSpace(d.Trigger)));

            foreach (var c in Colors)
            {
                var col = c;
                list.Add(new SealedFilter("color", col, col,
                    d => SealedPool.SplitColors(d.Color).Any(x => string.Equals(x, col, StringComparison.OrdinalIgnoreCase))));
            }
            list.Add(new SealedFilter("color", "multi", "Multicolor", SealedPool.IsMulticolor));

            foreach (var r in Rarities)
            {
                var rr = r;
                list.Add(new SealedFilter("rarity", rr, rr == "SP CARD" ? "SP" : rr,
                    d => string.Equals(d.Rarity, rr, StringComparison.OrdinalIgnoreCase)));
            }
            return list;
        }
    }

    /// <summary>A group of cards under a heading, as the grid renders them.</summary>
    public sealed class SealedGroup
    {
        public string Heading;
        public List<string> CardIds = new List<string>();
        public int Count => CardIds.Count;
    }

    /// <summary>The live view state of the builder: search text, active chips, sort, quick view.</summary>
    public sealed class SealedPoolView
    {
        public string Search = "";
        public SealedSort Sort = SealedSort.Cost;
        public SealedQuickView QuickView = SealedQuickView.None;
        public bool DeckOnly;                       // show only cards currently in the deck
        public readonly HashSet<string> Active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static string ChipId(SealedFilter f) => f.Group + ":" + f.Key;

        public bool IsActive(SealedFilter f) => Active.Contains(ChipId(f));
        public void Toggle(SealedFilter f)
        {
            string id = ChipId(f);
            if (!Active.Remove(id)) Active.Add(id);
        }
        public void Reset() { Active.Clear(); Search = ""; DeckOnly = false; QuickView = SealedQuickView.None; }
        public int ActiveCount => Active.Count;

        /// <summary>Apply search + stacked filters to a pool, returning matching card ids.</summary>
        public List<string> Filter(SealedPool pool, List<SealedFilter> chips)
        {
            var result = new List<string>();
            if (pool == null) return result;

            // Group the active chips so groups AND together while chips inside a group OR.
            var activeByGroup = chips.Where(IsActive)
                .GroupBy(f => f.Group)
                .ToDictionary(g => g.Key, g => g.ToList());

            string search = (Search ?? "").Trim();
            foreach (var id in pool.PoolCounts().Keys)
            {
                var def = CardData.GetCard(id);
                if (def == null) continue;
                if (DeckOnly && pool.InDeck(id) <= 0) continue;

                if (search.Length > 0)
                {
                    bool hit = (def.Name ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                        || id.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                        || (def.Effect ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!hit) continue;
                }

                bool passes = activeByGroup.Values.All(group => group.Any(f => f.Match(def)));
                if (passes) result.Add(id);
            }
            return SortIds(result, pool);
        }

        public List<string> SortIds(List<string> ids, SealedPool pool)
        {
            IOrderedEnumerable<string> q;
            CardDef D(string id) => CardData.GetCard(id);

            switch (Sort)
            {
                case SealedSort.Power: q = ids.OrderByDescending(i => D(i)?.Power ?? 0); break;
                case SealedSort.Counter: q = ids.OrderByDescending(i => D(i)?.Counter ?? 0); break;
                case SealedSort.Rarity: q = ids.OrderBy(i => RarityRank(D(i)?.Rarity)); break;
                case SealedSort.Color: q = ids.OrderBy(i => D(i)?.Color ?? "", StringComparer.Ordinal); break;
                case SealedSort.Alphabetical: q = ids.OrderBy(i => D(i)?.Name ?? i, StringComparer.OrdinalIgnoreCase); break;
                case SealedSort.CardNumber: q = ids.OrderBy(i => i, StringComparer.Ordinal); break;
                case SealedSort.NewestPull: q = ids.OrderByDescending(i => FirstPullOrder(pool, i)); break;
                case SealedSort.Copies: q = ids.OrderByDescending(i => pool.InPool(i)); break;
                case SealedSort.DeckCopies: q = ids.OrderByDescending(i => pool.InDeck(i)); break;
                default: q = ids.OrderBy(i => D(i)?.Cost ?? 0); break;
            }
            // Stable, readable secondary ordering so equal keys never shuffle between renders.
            return q.ThenBy(i => D(i)?.Cost ?? 0).ThenBy(i => i, StringComparer.Ordinal).ToList();
        }

        /// <summary>Regroup the filtered ids under the active quick view's headings. With no quick view
        /// there is a single unnamed group, which is just "the grid".</summary>
        public List<SealedGroup> Group(SealedPool pool, List<string> ids)
        {
            var groups = new List<SealedGroup>();
            void Emit(string heading, IEnumerable<string> members)
            {
                var list = members.ToList();
                if (list.Count > 0) groups.Add(new SealedGroup { Heading = heading, CardIds = list });
            }

            switch (QuickView)
            {
                case SealedQuickView.Counter:
                    Emit("No Counter", ids.Where(i => (CardData.GetCard(i)?.Counter ?? 0) <= 0));
                    Emit("1000", ids.Where(i => (CardData.GetCard(i)?.Counter ?? 0) == 1000));
                    Emit("2000", ids.Where(i => (CardData.GetCard(i)?.Counter ?? 0) >= 2000));
                    break;

                case SealedQuickView.Curve:
                    for (int c = 0; c <= 5; c++)
                    {
                        int cost = c;
                        Emit($"{cost} Cost", ids.Where(i => (CardData.GetCard(i)?.Cost ?? 0) == cost));
                    }
                    Emit("6+ Cost", ids.Where(i => (CardData.GetCard(i)?.Cost ?? 0) >= 6));
                    break;

                case SealedQuickView.Type:
                    Emit("Characters", ids.Where(i => CardData.GetCard(i)?.Type == "character"));
                    Emit("Events", ids.Where(i => CardData.GetCard(i)?.Type == "event"));
                    Emit("Stages", ids.Where(i => CardData.GetCard(i)?.Type == "stage"));
                    Emit("Leaders", ids.Where(i => CardData.GetCard(i)?.Type == "leader"));
                    break;

                case SealedQuickView.Rarity:
                    foreach (var r in new[] { "SEC", "SP CARD", "SR", "R", "UC", "C" })
                    {
                        var rr = r;
                        Emit(rr == "SP CARD" ? "SP" : rr,
                            ids.Where(i => string.Equals(CardData.GetCard(i)?.Rarity, rr, StringComparison.OrdinalIgnoreCase)));
                    }
                    break;

                case SealedQuickView.Pack:
                    for (int p = 0; p < (pool?.Packs.Count ?? 0); p++)
                    {
                        int pack = p;
                        var inPack = pool.Packs[pack].Cards.Select(c => c.CardId).Distinct(StringComparer.OrdinalIgnoreCase);
                        Emit($"Pack {pack + 1}", ids.Where(i => inPack.Contains(i, StringComparer.OrdinalIgnoreCase)));
                    }
                    break;

                case SealedQuickView.Color:
                    foreach (var col in SealedFilters.Colors)
                    {
                        var c = col;
                        Emit(c, ids.Where(i => SealedPool.SplitColors(CardData.GetCard(i)?.Color)
                            .Any(x => string.Equals(x, c, StringComparison.OrdinalIgnoreCase))));
                    }
                    break;

                case SealedQuickView.Keyword:
                    foreach (var kw in SealedFilters.Keywords)
                    {
                        var k = kw;
                        Emit(k + "s", ids.Where(i => SealedPool.HasKeyword(CardData.GetCard(i), k)));
                    }
                    Emit("Triggers", ids.Where(i => !string.IsNullOrWhiteSpace(CardData.GetCard(i)?.Trigger)));
                    break;

                default:
                    Emit("", ids);
                    break;
            }
            return groups;
        }

        /// <summary>Filter + group in one call — what the UI actually asks for each render.</summary>
        public List<SealedGroup> Build(SealedPool pool, List<SealedFilter> chips) =>
            Group(pool, Filter(pool, chips));

        private static int RarityRank(string r) => (r ?? "").ToUpperInvariant() switch
        {
            "SEC" => 0, "TR" => 1, "SP CARD" => 2, "SR" => 3, "R" => 4, "UC" => 5, "L" => 6, _ => 7,
        };

        /// <summary>Pull order across the whole kit, so "Newest Pull" sorts by when you actually saw it.</summary>
        private static int FirstPullOrder(SealedPool pool, string cardId)
        {
            if (pool == null) return 0;
            for (int p = pool.Packs.Count - 1; p >= 0; p--)
            {
                var pack = pool.Packs[p];
                for (int c = pack.Cards.Count - 1; c >= 0; c--)
                    if (string.Equals(pack.Cards[c].CardId, cardId, StringComparison.OrdinalIgnoreCase))
                        return p * 100 + c;
            }
            return 0;
        }
    }
}
