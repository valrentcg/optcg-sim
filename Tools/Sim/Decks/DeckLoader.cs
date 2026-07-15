using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// Parses a pasted/exported constructed decklist into an engine <see cref="DeckDef"/>
    /// and validates it against the card library. Tolerant of the common export shapes seen on
    /// onepiecetopdecks / official sim exports:
    ///   <c>4xOP01-016</c>, <c>4 OP01-016</c>, <c>OP01-016 x4</c>, <c>OP01-016</c> (count 1),
    ///   with an optional " (Leader)" tag. The single leader-type card becomes DeckDef.Leader.
    /// Directives: lines beginning <c># name:</c> / <c># id:</c> set metadata; other <c>#</c>/<c>//</c>
    /// lines are comments.
    ///
    /// The engine itself never checks deck legality ("it just plays whatever DeckDef it's given"),
    /// so validation lives here: unknown cards / missing-or-multiple leaders are hard ERRORS; a
    /// non-50 mainboard or >4 copies are WARNINGS (some meta lists include sideboard notes or use
    /// leaders that alter copy limits). Nothing about a deck reaches players — this is tooling.
    /// </summary>
    public static class DeckLoader
    {
        // OP16-001, ST01-001, EB01-001, PRB01-001, P-001, ...
        private static readonly Regex CardId = new Regex(@"\b([A-Z]{1,5}\d{0,2}-\d{1,3})\b", RegexOptions.Compiled);
        private static readonly Regex LeadCount = new Regex(@"^\s*(\d+)\s*[xX]?\s+", RegexOptions.Compiled);
        private static readonly Regex TrailCount = new Regex(@"[xX]\s*(\d+)\s*$", RegexOptions.Compiled);

        public sealed class Result
        {
            public DeckDef Deck;
            public readonly List<string> Errors = new List<string>();
            public readonly List<string> Warnings = new List<string>();
            public bool Ok => Errors.Count == 0 && Deck != null;
            public int MainboardCount;
        }

        public static Result LoadFile(string path)
        {
            var text = File.ReadAllText(path);
            string fallbackId = Path.GetFileNameWithoutExtension(path);
            return Parse(text, fallbackId);
        }

        public static Result Parse(string text, string fallbackId = "imported-deck")
        {
            var r = new Result();
            string name = null, id = null, leader = null;
            var counts = new Dictionary<string, int>(); // non-leader cardId -> qty (order-preserving below)
            var order = new List<string>();

            foreach (var raw in text.Replace("\r", "").Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;

                if (line.StartsWith("#") || line.StartsWith("//"))
                {
                    var m = Regex.Match(line, @"(?i)^[#/]+\s*(name|id)\s*[:=]\s*(.+)$");
                    if (m.Success)
                    {
                        if (m.Groups[1].Value.Equals("name", StringComparison.OrdinalIgnoreCase)) name = m.Groups[2].Value.Trim();
                        else id = m.Groups[2].Value.Trim();
                    }
                    continue; // other comments ignored
                }

                var idm = CardId.Match(line);
                if (!idm.Success) { r.Warnings.Add($"Unparseable line ignored: '{line}'"); continue; }
                string cardId = idm.Groups[1].Value;

                int qty = 1;
                var lc = LeadCount.Match(line);
                var tc = TrailCount.Match(line);
                if (lc.Success) qty = int.Parse(lc.Groups[1].Value);
                else if (tc.Success) qty = int.Parse(tc.Groups[1].Value);

                var def = CardData.GetCard(cardId);
                if (def == null || def.Type == "unknown")
                {
                    r.Errors.Add($"Unknown card id '{cardId}' (not in library).");
                    continue;
                }

                bool taggedLeader = line.IndexOf("leader", StringComparison.OrdinalIgnoreCase) >= 0;
                if (def.Type == "leader" || taggedLeader)
                {
                    if (leader != null && leader != cardId)
                        r.Errors.Add($"Multiple leaders found ('{leader}' and '{cardId}').");
                    leader = cardId;
                    continue;
                }

                if (!counts.ContainsKey(cardId)) order.Add(cardId);
                counts[cardId] = counts.GetValueOrDefault(cardId) + qty;
            }

            if (leader == null) { r.Errors.Add("No leader card found (need exactly one leader-type card)."); return r; }

            int total = counts.Values.Sum();
            r.MainboardCount = total;
            if (total != 50) r.Warnings.Add($"Mainboard is {total} cards, expected 50.");
            foreach (var kv in counts.Where(kv => kv.Value > 4))
                r.Warnings.Add($"{kv.Key} x{kv.Value} exceeds the usual 4-copy limit.");

            r.Deck = new DeckDef
            {
                Id = string.IsNullOrWhiteSpace(id) ? Slug(fallbackId) : Slug(id),
                Name = name ?? CardData.GetCard(leader).Name ?? fallbackId,
                Leader = leader,
                List = order.Select(cid => (cid, counts[cid])).ToList(),
            };
            return r;
        }

        private static string Slug(string s) =>
            Regex.Replace(s.Trim().ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
    }

    /// <summary>Resolves deck ids to DeckDefs across the built-in starter decks plus any imported
    /// meta decks, so the runner can cross starters and imports uniformly.</summary>
    public sealed class DeckRegistry
    {
        private readonly Dictionary<string, DeckDef> _decks = new Dictionary<string, DeckDef>(StringComparer.OrdinalIgnoreCase);

        public DeckRegistry(bool includeStarters = true)
        {
            if (includeStarters)
                foreach (var kv in CardData.StarterDecks) _decks[kv.Key] = kv.Value;
        }

        public void Register(DeckDef d)
        {
            if (d == null || string.IsNullOrWhiteSpace(d.Id)) throw new ArgumentException("Deck needs an id.");
            _decks[d.Id] = d;
        }

        /// <summary>Loads every .deck/.txt file in a directory as an imported deck.</summary>
        public List<string> ImportDirectory(string dir)
        {
            var loaded = new List<string>();
            if (!Directory.Exists(dir)) return loaded;
            foreach (var f in Directory.EnumerateFiles(dir).Where(f => f.EndsWith(".deck") || f.EndsWith(".txt")).OrderBy(f => f))
            {
                var res = DeckLoader.LoadFile(f);
                if (res.Ok) { Register(res.Deck); loaded.Add(res.Deck.Id); }
                else Console.Error.WriteLine($"  ✗ {Path.GetFileName(f)}: {string.Join("; ", res.Errors)}");
            }
            return loaded;
        }

        public bool Has(string id) => _decks.ContainsKey(id);
        public DeckDef Resolve(string id) =>
            _decks.TryGetValue(id, out var d) ? d : throw new ArgumentException($"Unknown deck id '{id}'.");
        public IEnumerable<string> Ids => _decks.Keys;
    }
}
