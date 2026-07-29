using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// Blast radius for the OP15-114 Wyper bug, which was never Wyper-specific.
    ///
    /// The pending-effect panel chose between "click a target on the board" and "press Use Effect"
    /// by asking EffectHasValidTarget. For a "You may &lt;cost&gt;: &lt;body&gt;" clause that is the
    /// wrong question on the FIRST interaction — the player is paying a cost, not picking a target —
    /// and it answered true whenever the BODY had legal targets. Every such card showed Skip alone,
    /// with nothing clickable, exactly as reported.
    ///
    /// So the failure needed two things at once:
    ///   1. an unpaid "You may &lt;cost&gt;:" prefix, and
    ///   2. a body whose text targets something that can plausibly be on the board.
    ///
    /// This enumerates (1) from the card pool and flags the subset that also satisfies (2), which is
    /// the set that was broken in play rather than merely at risk.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- costprefixsweep
    /// </summary>
    public static class CostPrefixSweep
    {
        // Body wording that implies a board/hand/trash pick, i.e. EffectHasValidTarget can return true.
        private static readonly string[] TargetingBody =
        {
            "your opponent's character", "your opponent's leader", "of your characters",
            "your leader", "1 of your", "up to", "all of your opponent's", "k.o.", "rest ",
            "give ", "return ", "trash ", "draw ", "play ", "add ",
        };

        public static int Run()
        {
            Console.WriteLine("=== \"You may <cost>:\" prefix sweep ===");

            var withPrefix = new List<(string Id, string Name, string Clause, bool BodyTargets)>();
            int cards = 0;

            foreach (var def in CardData.Library.Values
                        .Where(c => c != null && !string.IsNullOrEmpty(c.Effect))
                        .GroupBy(c => c.Id).Select(g => g.First())
                        .OrderBy(c => c.Id, StringComparer.Ordinal))
            {
                cards++;
                foreach (var raw in def.Effect.Split('\n'))
                {
                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    // strip leading [timing] tags the same way the engine does before queueing
                    var clause = Regex.Replace(raw, @"^\s*(\[[^\]]+\]\s*/?\s*)+", "").Trim();
                    var m = Regex.Match(clause, @"^You may ([^:]+):\s*(.+)$", RegexOptions.IgnoreCase);
                    if (!m.Success) continue;
                    string body = m.Groups[2].Value.ToLowerInvariant();
                    bool bodyTargets = TargetingBody.Any(t => body.Contains(t));
                    withPrefix.Add((def.Id, def.Name ?? "", clause, bodyTargets));
                }
            }

            int broken = withPrefix.Count(w => w.BodyTargets);
            Console.WriteLine($"  scanned {cards} cards");
            Console.WriteLine($"  clauses with an unpaid \"You may <cost>:\" prefix : {withPrefix.Count}");
            Console.WriteLine($"  ...whose BODY also targets something (were broken) : {broken}");
            Console.WriteLine($"  ...body has no target, so Use was already offered    : {withPrefix.Count - broken}");

            Console.WriteLine();
            Console.WriteLine("  affected cards (first 40):");
            foreach (var w in withPrefix.Where(x => x.BodyTargets).Take(40))
                Console.WriteLine($"    {w.Id,-10} {Trim(w.Name, 18),-18} {Trim(w.Clause, 96)}");
            if (broken > 40) Console.WriteLine($"    ... and {broken - 40} more");

            // Reporting sweep, not a gate: the fix is in the UI and applies to all of these at once.
            return 0;
        }

        private static string Trim(string s, int n) =>
            string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s.Substring(0, n - 1) + "…");
    }
}
