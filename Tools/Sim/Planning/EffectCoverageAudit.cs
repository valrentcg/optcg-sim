using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim.Planning
{
    /// <summary>
    /// DB-wide operability sweep, v2. For every card, each printed effect clause is routed by its timing tag
    /// and checked against the ENGINE PATH that actually resolves it:
    ///
    ///  • non-trigger event bodies (On Play / On K.O. / When Attacking / On Block / Activate:Main / bare
    ///    [Main] event-primary / End-of-turn) → dry-run the REAL resolver via
    ///    <see cref="GameEngine.AuditResolverRecognizes"/>. Only an explicit NotAutomated return is a gap.
    ///  • [Trigger] bodies → the trigger gate (<see cref="GameEngine.AuditEffectRecognized"/>) is authoritative
    ///    here (TryResolveKnownTrigger diverts an unrecognized life card to hand), PLUS the two trigger
    ///    special-cases it does handle: "Play this card" for CHARACTERS (handler is character-only, so stages
    ///    are still gaps) and "Activate this card's [Main] effect" (which re-queues the card's own [Main]).
    ///
    /// This fixes v1's core flaw (it used the auto-resolve GATE as the oracle for every tag, over-reporting
    /// ~2/3 of non-trigger bodies) and closes v1's biggest blind spot (bare [Main] event primaries were never
    /// scanned). Passive/continuous auras and keyword grants still resolve on other paths and are excluded.
    /// </summary>
    public static class EffectCoverageAudit
    {
        // Non-trigger event tags → routed to the real resolver.
        static readonly HashSet<string> ResolverEventTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "On Play", "On K.O.", "When this Character is K.O.'d", "When Attacking", "On Block",
            "Activate: Main", "Main", "On Your Opponent's Attack", "End of Your Turn", "End of Your Opponent's Turn",
        };

        static readonly string[] KeywordOnly = { "Rush", "Blocker", "Double Attack", "Banish", "Unblockable" };

        static readonly Regex LeadingTags = new Regex(@"^\s*((?:\[[^\]]+\]\s*/?\s*)+)");
        static readonly Regex TagInner    = new Regex(@"\[([^\]]+)\]");

        public static int Run(string outPath = null)
        {
            var cards = CardData.Library.Values
                .Where(d => d != null && !string.IsNullOrWhiteSpace(d.Id))
                .OrderBy(d => d.Id, StringComparer.Ordinal).ToList();

            var resolverGaps = new List<Row>();
            var triggerGaps  = new List<Row>();
            var triggerChecked = new HashSet<string>();
            int resolverBodies = 0, triggerBodies = 0, threw = 0;

            foreach (var d in cards)
            {
                foreach (var raw in ClausesOf(d))
                {
                    string clause = raw.Trim();
                    if (clause.Length == 0) continue;
                    var tags = TagsOf(clause);
                    if (tags.Count == 0) continue;                       // untagged = continuous/passive → other path
                    string body = StripLeadingTags(clause);
                    if (IsKeywordOnly(body)) continue;

                    bool isTrigger = tags.Any(t => t.Equals("Trigger", StringComparison.OrdinalIgnoreCase));
                    bool isResolverEvent = tags.Any(t => ResolverEventTags.Contains(t));

                    if (isTrigger)
                    {
                        triggerBodies++;
                        // One verdict per card (the real resolver reads the whole Trigger text), deduped.
                        if (!triggerChecked.Add(d.Id)) continue;
                        if (!GameEngine.AuditTriggerRecognizes(d.Id)) triggerGaps.Add(new Row(d, TagStr(tags), body));
                    }
                    else if (isResolverEvent)
                    {
                        resolverBodies++;
                        var outcome = GameEngine.AuditResolverRecognizes(d.Id, "main", clause);
                        if (outcome == GameEngine.AuditResolveOutcome.Threw) threw++;
                        else if (outcome == GameEngine.AuditResolveOutcome.NotAutomated)
                            resolverGaps.Add(new Row(d, TagStr(tags), body));
                    }
                }
            }

            outPath ??= Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "docs",
                "effect-coverage-audit-v2.md"));
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            File.WriteAllText(outPath, BuildReport(cards.Count, resolverBodies, triggerBodies, threw, resolverGaps, triggerGaps));

            Console.WriteLine($"effect-coverage-audit v2: {cards.Count} cards");
            Console.WriteLine($"  resolver-route event bodies: {resolverBodies}  (dry-run TryResolveKnownEffect; {threw} threw = recognized)");
            Console.WriteLine($"  RESOLVER GAPS (NotAutomated): {resolverGaps.Count} across {resolverGaps.Select(g => g.Card.Id).Distinct().Count()} cards");
            Console.WriteLine($"  trigger-route bodies: {triggerBodies}");
            Console.WriteLine($"  TRIGGER GAPS: {triggerGaps.Count} across {triggerGaps.Select(g => g.Card.Id).Distinct().Count()} cards");
            Console.WriteLine($"  report: {outPath}");
            return 0;
        }

        // --- helpers ---------------------------------------------------------------------------------

        static IEnumerable<string> ClausesOf(CardDef d)
        {
            // Merge bullet continuation lines (•/-/‐) into the preceding clause so a "Choose one:" /
            // "Apply all …:" block reaches the resolver WITH its options (else it looks like an empty body).
            var merged = new List<string>();
            void Add(string block, bool asTrigger)
            {
                foreach (var line in (block ?? "").Split('\n'))
                {
                    var t = line.TrimStart();
                    if (t.Length == 0) continue;
                    bool cont = merged.Count > 0 && (t[0] == '•' || t[0] == '-' || t[0] == '‐');
                    if (cont) merged[merged.Count - 1] += "\n" + line.Trim();
                    else merged.Add(asTrigger && !t.StartsWith("[Trigger]") ? "[Trigger] " + line.Trim() : line);
                }
            }
            Add(d.Effect, false);
            if (!string.IsNullOrWhiteSpace(d.Trigger)) Add(d.Trigger, true);
            return merged;
        }

        static List<string> TagsOf(string clause)
        {
            var m = LeadingTags.Match(clause);
            var list = new List<string>();
            if (!m.Success) return list;
            foreach (Match t in TagInner.Matches(m.Groups[1].Value)) list.Add(t.Groups[1].Value.Trim());
            return list;
        }

        static string StripLeadingTags(string clause) => LeadingTags.Replace(clause, "").Trim();
        static string TagStr(List<string> tags) => string.Join(" ", tags.Select(t => "[" + t + "]"));

        static bool IsKeywordOnly(string body)
        {
            string s = TagInner.Replace(body, "$1").Trim().TrimEnd('.', ' ');
            return s.Length == 0 || KeywordOnly.Any(k => s.Equals(k, StringComparison.OrdinalIgnoreCase));
        }

        struct Row
        {
            public CardDef Card; public string Tag; public string Body;
            public Row(CardDef c, string tag, string body) { Card = c; Tag = tag; Body = body; }
        }

        static string SetOf(string id) { int i = id.IndexOf('-'); return i > 0 ? id.Substring(0, i) : id; }

        static string BuildReport(int total, int resBodies, int trigBodies, int threw, List<Row> resGaps, List<Row> trigGaps)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Effect-coverage operability sweep — v2 (resolver-oracle)");
            sb.AppendLine();
            sb.AppendLine($"- Generated: {DateTime.Now:yyyy-MM-dd HH:mm}");
            sb.AppendLine($"- Cards: **{total}**");
            sb.AppendLine($"- Non-trigger event bodies dry-run through the REAL resolver: **{resBodies}** ({threw} threw mid-execution = recognized)");
            sb.AppendLine($"- **RESOLVER GAPS (returned NotAutomated): {resGaps.Count}** across {resGaps.Select(g => g.Card.Id).Distinct().Count()} cards");
            sb.AppendLine($"- Trigger bodies: **{trigBodies}**  → **TRIGGER GAPS: {trigGaps.Count}** across {trigGaps.Select(g => g.Card.Id).Distinct().Count()} cards");
            sb.AppendLine();
            sb.AppendLine("Oracle: non-trigger event bodies are checked by dry-running `TryResolveKnownEffect` on a "
                + "scratch state — only an explicit `NotAutomated` return (no handler matched, no throw) is a gap. "
                + "[Trigger] bodies use the trigger gate plus its Play-this-card(character) and Activate-[Main] "
                + "special-cases. Passive auras / keyword grants excluded (resolved elsewhere).");
            sb.AppendLine();

            AppendSection(sb, "A. RESOLVER GAPS — non-trigger event bodies the resolver cannot handle", resGaps);
            AppendSection(sb, "B. TRIGGER GAPS — [Trigger] life cards that silently divert to hand", trigGaps);
            return sb.ToString();
        }

        static void AppendSection(StringBuilder sb, string title, List<Row> rows)
        {
            sb.AppendLine("## " + title);
            sb.AppendLine();
            foreach (var g in rows.GroupBy(x => SetOf(x.Card.Id)).OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                sb.AppendLine($"### {g.Key}  ({g.Select(x => x.Card.Id).Distinct().Count()} cards)");
                sb.AppendLine();
                sb.AppendLine("| Card | Name | Tag | Body |");
                sb.AppendLine("|---|---|---|---|");
                foreach (var r in g.OrderBy(x => x.Card.Id, StringComparer.Ordinal))
                    sb.AppendLine($"| {r.Card.Id} | {Esc(r.Card.Name)} | {Esc(r.Tag)} | {Esc(Clip(r.Body))} |");
                sb.AppendLine();
            }
        }

        static string Clip(string s) => s.Length > 160 ? s.Substring(0, 157) + "…" : s;
        static string Esc(string s) => (s ?? "").Replace("|", "\\|").Replace("\n", " ");
    }
}
