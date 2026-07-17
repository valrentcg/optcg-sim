// TRANSITION-CANDIDATE AUDIT driver. Read-only.
//   dotnet run              → report + write the transition-candidate artifact
//   dotnet run -- --test    → analyzer fixtures (each mutation form asserted)
//   dotnet run -- --check   → assert known probe counts; NON-ZERO EXIT on regression
//
// STATUS: best-effort inventory of AUTHORITATIVE KNOWLEDGE-TRANSITION CANDIDATES, used to drive migration
// onto the ledger funnel -- a BOUNDED MIGRATION AID; completeness is enforced only AFTER ENCAPSULATION
// (§14.7), never by this inventory. NOT a privacy proof and NOT claimed complete. Privacy is proven at
// RUNTIME by Tools/Sim/Search/PrivacyTest (poison scan + paired-world noninterference + typed two-layer
// boundary): NO STATIC PRIVACY-FIELD WHITELIST -- the reachability walk enumerates no fields -- but the
// poison and decision fixtures still enumerate the threat model. Four analyzer versions proved a static
// privacy whitelist never closes.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ZoneAudit;

static class Program
{
    // Known-good probe counts. A MISMATCH FAILS --check. v2 only printed these, so it could regress to
    // zero and still exit 0. Update these deliberately, never to make a red build green.
    static readonly (string Probe, int Expect)[] Probes =
    {
        ("helper ShuffleInPlace", 6),
        ("helper Shift", 10),
        ("helper Pop", 11),
        // 2 = the AUTHORITATIVE transition sites: GameEngine 2499 (damage) + 2521 (cleanup).
        // Briefly 3 while GameClone.cs:69 was in scope; that clone copy is a PRIVACY-REACHABILITY fact,
        // not a transition, and moved to PrivacyTest. The 2->3 bump was correct for the OLD scope and then
        // OUTLIVED it. When scope narrows, RE-DERIVE every probe -- never inherit it.
        ("BattleState.RevealedLife", 2),
    };

    // SIGNATURE probes: specific rows that MUST exist. A bare count ("GameClone.cs = 18") stays green
    // while Deck silently disappears and an unrelated row appears — so the thing that matters most is
    // asserted by identity, not by tally. These are the exact rows the contamination lives in.
    // The GameClone.cs signatures MOVED to PrivacyTest: clone copies are privacy-reachability facts,
    // proven at runtime, not authoritative transitions.
    static readonly (string File, int Line, string Target)[] Signatures =
    {
        ("GameEngine.cs", 2499, "BattleState.RevealedLife"),  // damage: card goes in flight
        ("GameEngine.cs", 2521, "BattleState.RevealedLife"),  // cleanup
    };

    static int Main(string[] args)
    {
        if (args.Contains("--test")) return Fixtures.Run();

        string repo = args.FirstOrDefault(a => !a.StartsWith("--")) ?? FindRepoRoot();
        string engineDir = Path.Combine(repo, "Assets", "Scripts", "Engine");
        string outDir = Path.Combine(repo, "Tools", "Sim", "docs");
        if (!Directory.Exists(engineDir)) { Console.Error.WriteLine($"engine dir not found: {engineDir}"); return 2; }

        var files = Directory.GetFiles(engineDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .ToArray();
        Console.WriteLine($"parsing {files.Length} engine file(s)\n");

        var sites = Analyzer.Analyze(files.Select(f => (f, File.ReadAllText(f))), repo, out var errors);
        if (errors.Count > 0)
        {
            Console.Error.WriteLine($"FAIL: {errors.Count} compilation error(s); symbol resolution would be partial.");
            foreach (var e in errors.Take(8)) Console.Error.WriteLine($"  {e}");
            return 1;
        }

        bool assert = args.Contains("--check");
        int probeFails = ReportProbes(sites, assert);

        // VALIDATE BEFORE WRITING: --check used to overwrite the generated audit and THEN report failure,
        // so a regressed run replaced a good artifact with a bad one before anyone saw the red.
        if (assert && probeFails > 0)
        { Console.Error.WriteLine($"CHECK FAILED: {probeFails} mismatch(es). Generated files left untouched."); return 1; }

        Console.WriteLine("=== by target ===");
        foreach (var g in sites.GroupBy(s => s.Target).OrderByDescending(g => g.Count()))
            Console.WriteLine($"  {g.Key,-30} {g.Count(),4}");
        Console.WriteLine();

        Write(sites, repo, outDir);

        if (args.Contains("--check") && probeFails > 0)
        { Console.Error.WriteLine($"\nCHECK FAILED: {probeFails} probe mismatch(es)."); return 1; }
        return 0;
    }

    static int ReportProbes(List<Site> sites, bool assert)
    {
        Console.WriteLine($"=== regression probes ({(assert ? "ASSERTING" : "informational; use --check to assert")}) ===");
        int fails = 0;
        foreach (var (probe, expect) in Probes)
        {
            int got = probe.StartsWith("helper ") ? sites.Count(s => s.Via.Contains(probe.Substring(7)))
                    : probe.StartsWith("file ")   ? sites.Count(s => s.File.EndsWith(probe.Substring(5)))
                    : sites.Count(s => s.Target == probe);
            bool ok = got == expect;
            if (!ok) fails++;
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {probe,-28} expect {expect,3}  got {got,3}");
        }
        foreach (var (f, line, target) in Signatures)
        {
            bool ok = sites.Any(s => s.File.EndsWith(f) && s.Line == line && s.Target == target);
            if (!ok) fails++;
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} signature {f}:{line} {target}");
        }
        Console.WriteLine();
        return fails;
    }

    static string FindRepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "Assets"))) d = d.Parent;
        return d?.FullName ?? Directory.GetCurrentDirectory();
    }

    static void Write(List<Site> sites, string repo, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var csv = Path.Combine(outDir, "zone-mutation-audit.csv");
        using (var w = new StreamWriter(csv))
        {
            w.WriteLine("transition_id,file,line,target,kind,via,identity_viewers,movement_viewers,position_viewers,anchor,rules_cite,routed,snippet");
            foreach (var s in sites.OrderBy(s => s.File).ThenBy(s => s.Line))
                w.WriteLine($"UNGROUPED,{s.File},{s.Line},{s.Target},{s.Kind},{s.Via},TODO,TODO,TODO,TODO,TODO,no,\"{s.Snippet.Replace("\"", "'")}\"");
        }

        var md = Path.Combine(outDir, "zone-mutation-audit.md");
        using (var w = new StreamWriter(md))
        {
            w.WriteLine("# Transition-Candidate Audit — generated by `Tools/Audit`\n");
            w.WriteLine($"Generated {DateTime.Now:yyyy-MM-dd} from `Assets/Scripts/Engine`. **Read-only.**\n");
            w.WriteLine("## Status: best-effort inventory — NOT a privacy proof, NOT claimed complete\n");
            w.WriteLine("Best-effort inventory of **authoritative knowledge-transition candidates**, emitted only from");
            w.WriteLine("the authoritative mutation layer (`GameEngine.cs`), used to drive migration onto the ledger");
            w.WriteLine("funnel — a **bounded migration aid; completeness is enforced only after encapsulation** (§14.7),");
            w.WriteLine("never by this inventory. **This is not a privacy proof.** Privacy is proven at runtime by");
            w.WriteLine("`PrivacyTest` (poison scan + paired-world noninterference + typed two-layer boundary): it carries");
            w.WriteLine("**no static privacy-field whitelist** — the reachability walk enumerates no fields — but the poison");
            w.WriteLine("and decision fixtures still enumerate the threat model and the required decision boundaries.\n");
            w.WriteLine("Four analyzer versions each shipped a completeness claim while blind (helper params → object");
            w.WriteLine("initializers → filename-based A/B). **Completeness becomes durable only by ENCAPSULATION**:");
            w.WriteLine("making the mutation surface private after migration, so a bypass fails compilation.\n");
            w.WriteLine($"- **{sites.Count}** raw sites.");
            w.WriteLine($"- **{sites.Count}** rows `TODO` on visibility; **`transition_id` grouping NOT implemented** (all `UNGROUPED`).");
            w.WriteLine("- Alias tracking is **global and flow-insensitive**; helper summaries understand **bare parameters only**.\n");
            w.WriteLine("\n## By target\n\n| target | sites |\n|---|---|");
            foreach (var g in sites.GroupBy(s => s.Target).OrderByDescending(g => g.Count())) w.WriteLine($"| {g.Key} | {g.Count()} |");
            w.WriteLine("\n## By kind\n\n| kind | sites |\n|---|---|");
            foreach (var g in sites.GroupBy(s => s.Kind).OrderByDescending(g => g.Count())) w.WriteLine($"| {g.Key} | {g.Count()} |");
            w.WriteLine("\n## Transition candidates\n\n| file:line | target | kind | via |\n|---|---|---|---|");
            foreach (var s in sites.OrderBy(s => s.File).ThenBy(s => s.Line))
                w.WriteLine($"| `{s.File}:{s.Line}` | {s.Target} | {s.Kind} | {s.Via} |");
        }
        Console.WriteLine($"wrote {sites.Count} transition candidates -> Tools/Sim/docs/zone-mutation-audit.{{csv,md}}");
        Console.WriteLine("STATUS: best-effort transition candidates. NOT a privacy proof, NOT claimed complete.");
    }
}
