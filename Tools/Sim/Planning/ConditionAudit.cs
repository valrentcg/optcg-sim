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
    /// Aura/condition blind-spot sweep (task #28): enumerates every "If &lt;cond&gt;," (leading) and
    /// "… if &lt;cond&gt;" (trailing) condition printed across the card pool and reports which ones
    /// <see cref="GameEngine.AuditConditionRecognized"/> cannot parse. An unrecognized condition FAILS CLOSED
    /// in the engine — the gated effect/aura is silently treated as "not met" for the whole game — so this
    /// surfaces exactly the conditions worth adding handlers for, ranked by how many cards each blocks.
    /// </summary>
    public static class ConditionAudit
    {
        static readonly Regex LeadingIf  = new Regex(@"\bIf ([^,]{3,90}),", RegexOptions.IgnoreCase);
        static readonly Regex TrailingIf = new Regex(@"\bif ([^.,]{3,90})\.?\s*$", RegexOptions.IgnoreCase);
        static readonly Regex Tags       = new Regex(@"^\s*((?:\[[^\]]+\]\s*/?\s*)+)");

        public static int Run(string outPath = null)
        {
            var cards = CardData.Library.Values
                .Where(d => d != null && !string.IsNullOrWhiteSpace(d.Id)).ToList();

            // condition text -> set of card ids that print it
            var byCondition = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in cards)
                foreach (var clause in Clauses(d))
                    foreach (var cond in ConditionsIn(clause))
                    {
                        if (!byCondition.TryGetValue(cond, out var ids))
                            byCondition[cond] = ids = new SortedSet<string>(StringComparer.Ordinal);
                        ids.Add(d.Id);
                    }

            var unknown = new List<(string cond, SortedSet<string> ids)>();
            foreach (var kv in byCondition)
                if (!GameEngine.AuditConditionRecognized(kv.Key))
                    unknown.Add((kv.Key, kv.Value));
            unknown.Sort((a, b) => b.ids.Count.CompareTo(a.ids.Count));   // most-blocking first

            int distinctCards = unknown.SelectMany(u => u.ids).Distinct().Count();
            outPath ??= Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "docs",
                "condition-audit.md"));
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            File.WriteAllText(outPath, Report(byCondition.Count, unknown, distinctCards));

            Console.WriteLine($"condition-audit: {byCondition.Count} distinct printed conditions");
            Console.WriteLine($"  UNRECOGNIZED (fail-closed): {unknown.Count} distinct, across {distinctCards} cards");
            Console.WriteLine($"  report: {outPath}");
            return 0;
        }

        static IEnumerable<string> Clauses(CardDef d)
        {
            foreach (var line in (d.Effect ?? "").Split('\n')) yield return line;
            foreach (var line in (d.Trigger ?? "").Split('\n')) yield return line;
        }

        static IEnumerable<string> ConditionsIn(string clause)
        {
            if (string.IsNullOrWhiteSpace(clause)) yield break;
            string body = Tags.Replace(clause, "").Trim();
            var lead = LeadingIf.Match(body);
            if (lead.Success) yield return lead.Groups[1].Value.Trim();
            var trail = TrailingIf.Match(body);
            if (trail.Success && (!lead.Success || trail.Index != lead.Index)) yield return trail.Groups[1].Value.Trim();
        }

        static string Report(int total, List<(string cond, SortedSet<string> ids)> unknown, int distinctCards)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Aura/condition blind-spot sweep (task #28)");
            sb.AppendLine();
            sb.AppendLine($"- Generated: {DateTime.Now:yyyy-MM-dd HH:mm}");
            sb.AppendLine($"- Distinct printed conditions: **{total}**");
            sb.AppendLine($"- **Unrecognized (fail-closed) conditions: {unknown.Count}**, blocking **{distinctCards}** cards");
            sb.AppendLine();
            sb.AppendLine("Each row is a printed `If <cond>,` / `… if <cond>` the engine's `EvaluateCondition` cannot "
                + "parse, so the gated effect/aura is silently treated as *not met* all game. Ranked by cards blocked.");
            sb.AppendLine();
            sb.AppendLine("| Cards | Condition | Example card ids |");
            sb.AppendLine("|---|---|---|");
            foreach (var u in unknown)
            {
                string ex = string.Join(", ", u.ids.Take(4));
                if (u.ids.Count > 4) ex += ", …";
                sb.AppendLine($"| {u.ids.Count} | {Esc(u.cond)} | {Esc(ex)} |");
            }
            sb.AppendLine();
            return sb.ToString();
        }

        static string Esc(string s) => (s ?? "").Replace("|", "\\|").Replace("\n", " ");
    }
}
