using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace OnePieceTcg.Engine
{
    /// <summary>The two supported play formats.</summary>
    public enum GameFormat { Standard, ExtraRegulation }

    /// <summary>How a single card sits across the formats.</summary>
    public enum CardLegality { LegalBoth, ExtraOnly, Banned }

    /// <summary>
    /// One Piece TCG format legality — the Standard rotation (block system) and the shared banned/restricted
    /// list, as of the April 2026 update. All of the data below is intentionally in one place so it's easy to
    /// keep current when Bandai rotates blocks or updates the ban list.
    ///
    /// - Standard = the in-rotation blocks only (Block 1 rotated out April 1, 2026).
    /// - Extra Regulation = every block (the full card pool).
    /// - The banned/banned-pair list is the SAME for both formats.
    /// Sources: official card game site + community ban-list guides (see the deck-builder format notes).
    /// </summary>
    public static class FormatLegality
    {
        // ── Rotation: blocks rotated OUT of Standard. April 1, 2026: Block 1 (OP01–OP04, ST01–ST09). ──
        public static readonly HashSet<string> RotatedOutBlocks = new HashSet<string> { "1" };
        public const string StandardBlocksLabel = "Blocks 2–5";           // what Standard currently allows
        public const string RotatedBlocksLabel = "Block 1 (OP01–OP04, ST01–ST09)";

        // ── Banned cards (same list in Standard and Extra Regulation). Effective April 10, 2026. ──
        public static readonly Dictionary<string, string> BannedCards = new Dictionary<string, string>
        {
            ["OP06-116"] = "Reject",
            ["ST10-001"] = "Trafalgar Law",
            ["OP06-086"] = "Gecko Moria",
            ["OP03-040"] = "Nami",
            ["OP06-047"] = "Charlotte Pudding",
        };

        // ── Restricted cards: none currently. (id -> name, for when Bandai adds some.) ──
        public static readonly Dictionary<string, string> RestrictedCards = new Dictionary<string, string>();

        // ── Banned pairs: these ids cannot appear together in one deck. ──
        public static readonly (string a, string b)[] BannedPairs =
        {
            ("EB04-058", "OP07-115"),   // Borsalino + I Re-Quasar Helllp!!
            ("OP11-040", "OP11-067"),   // Monkey.D.Luffy (leader) + Charlotte Katakuri
            ("OP11-040", "OP08-069"),   // Monkey.D.Luffy (leader) + Charlotte LinLin
        };

        // ── Per-card queries ─────────────────────────────────────────────────────
        public static bool IsBanned(string cardId) => BannedCards.ContainsKey(BaseId(cardId));

        public static bool IsRestricted(string cardId) => RestrictedCards.ContainsKey(BaseId(cardId));

        /// <summary>True if the card's block is still in the Standard rotation (not rotated out).</summary>
        public static bool InStandardPool(string cardId)
        {
            var def = CardData.GetCard(BaseId(cardId));
            return !RotatedOutBlocks.Contains(def?.Block ?? "");
        }

        public static CardLegality Legality(string cardId)
        {
            if (IsBanned(cardId)) return CardLegality.Banned;
            return InStandardPool(cardId) ? CardLegality.LegalBoth : CardLegality.ExtraOnly;
        }

        public static bool IsCardLegal(string cardId, GameFormat format)
        {
            if (IsBanned(cardId)) return false;
            return format == GameFormat.ExtraRegulation || InStandardPool(cardId);
        }

        // ── Deck queries ─────────────────────────────────────────────────────────
        public sealed class DeckCheck
        {
            public bool Legal = true;
            public readonly List<string> Reasons = new List<string>();   // human-readable, deck-builder ready
        }

        /// <summary>Check a whole deck (leader id + every card id, duplicates allowed) against a format.</summary>
        public static DeckCheck CheckDeck(IEnumerable<string> cardIds, GameFormat format)
        {
            var chk = new DeckCheck();
            var present = new HashSet<string>();
            foreach (var raw in cardIds)
            {
                string id = BaseId(raw);
                if (id.Length == 0) continue;
                present.Add(id);
                if (IsBanned(id))
                    Fail(chk, $"{Name(id)} ({id}) is banned");
                else if (format == GameFormat.Standard && !InStandardPool(id))
                    Fail(chk, $"{Name(id)} ({id}) is Extra Regulation only (rotated out of Standard)");
            }
            foreach (var (a, b) in BannedPairs)
                if (present.Contains(a) && present.Contains(b))
                    Fail(chk, $"{Name(a)} ({a}) + {Name(b)} ({b}) is a banned pair");
            return chk;
        }

        public static bool IsDeckLegal(IEnumerable<string> cardIds, GameFormat format) => CheckDeck(cardIds, format).Legal;

        // ── Format descriptions for the "what's allowed" panels ──────────────────
        public static string PoolDescription(GameFormat format) => format == GameFormat.Standard
            ? $"Standard — {StandardBlocksLabel} only. {RotatedBlocksLabel} has rotated out."
            : "Extra Regulation — all blocks (the full card pool).";

        /// <summary>Banned cards (+ pairs) as a display list of "Name (OPXX-XXX)" lines.</summary>
        public static List<string> BanListLines()
        {
            var lines = new List<string>();
            foreach (var kv in BannedCards) lines.Add($"{kv.Value} ({kv.Key})");
            foreach (var (a, b) in BannedPairs) lines.Add($"Pair: {Name(a)} ({a}) + {Name(b)} ({b})");
            foreach (var kv in RestrictedCards) lines.Add($"Restricted: {kv.Value} ({kv.Key})");
            return lines;
        }

        // ── helpers ───────────────────────────────────────────────────────────────
        private static void Fail(DeckCheck chk, string reason)
        {
            chk.Legal = false;
            if (!chk.Reasons.Contains(reason)) chk.Reasons.Add(reason);
        }

        private static string Name(string id)
        {
            var d = CardData.GetCard(BaseId(id));
            if (d != null && !string.IsNullOrEmpty(d.Name)) return d.Name;
            return BannedCards.TryGetValue(BaseId(id), out var n) ? n : id;
        }

        // Deck lists may carry alt-art suffixes ("OP01-001_p1"); legality keys on the base "OP01-001".
        private static readonly Regex BaseIdRx = new Regex(@"^[A-Za-z0-9]+-\d+", RegexOptions.Compiled);
        public static string BaseId(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            var m = BaseIdRx.Match(id.Trim());
            return (m.Success ? m.Value : id.Trim()).ToUpperInvariant();
        }
    }
}
