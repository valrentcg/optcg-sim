using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OnePieceTcg.Sim;
using OnePieceTcg.Sim.Heuristics;

// Heuristic-discovery simulation platform for the OPTCG engine. Drives the REAL, pure-C# shipping
// engine (compiled straight from Assets/) through the IAgent seam to generate seeded, reproducible
// self-play games at scale, then distils DECK-AGNOSTIC heuristics via matched-seed counterfactuals.
//
// SHIP BOUNDARY: nothing here reaches players. Tools/ is outside Assets/ (Unity never compiles it)
// and Velopack packs only the Unity build dir. See README.
//
//   dotnet run -c Release -- smoke                          quick self-check
//   dotnet run -c Release -- deckcheck [path]               validate imported meta decks (default Decks/imported)
//   dotnet run -c Release -- run <config.json> [--games N]  self-play league (games.jsonl + summary.json)
//   dotnet run -c Release -- heuristics <config.json>       counterfactual heuristic discovery (heuristics.md)
//   dotnet run -c Release -- heuristics-quick <family> [samples]   quick preset over the starter field

const string CardJsonPath =
    @"C:\Users\Nperr\One Piece TCG Simulator\Assets\StreamingAssets\Cards\official-card-library.json";
const string ImportedDecksDir = "Decks/imported";

int loaded = CardLibraryLoader.Load(CardJsonPath); // load ONCE, then read-only across all threads
Console.WriteLine($"Loaded {loaded} card definitions.\n");

// Every deck the user pastes into Decks/imported/*.deck becomes available to configs by id.
static DeckRegistry BuildRegistry(out List<string> imported)
{
    var reg = new DeckRegistry(includeStarters: true);
    imported = Directory.Exists(ImportedDecksDir) ? reg.ImportDirectory(ImportedDecksDir) : new List<string>();
    if (imported.Count > 0) Console.WriteLine($"Imported {imported.Count} meta deck(s): {string.Join(", ", imported)}\n");
    return reg;
}

string mode = args.Length > 0 ? args[0] : "smoke";

// Global A/B switch: reverts ValuePolicy to the myopic pre-fix policy that never counters or blocks.
// Global rather than per-command so the defence lookahead's cost AND its win-rate contribution can be
// measured on the same binary by any tool — the fix was believed once on a trace alone, and that is
// exactly how a bot that attacked but never defended passed review while losing 94%.
if (args.Contains("--no-lookahead"))
{
    OnePieceTcg.Sim.Planning.ValuePolicy.EnableLookahead = false;
    Console.WriteLine("[--no-lookahead: ValuePolicy is MYOPIC — defence disabled, pre-fix behaviour]");
}
if (args.Contains("--replan"))
{
    OnePieceTcg.Sim.Planning.PlannerBot.ReplanEveryCommand = true;
    Console.WriteLine("[--replan: receding horizon — replan before every command, execute only the first]");
}
if (args.Contains("--leader-pow"))
{
    OnePieceTcg.Sim.Planning.ValueFunction.BoardPowIncludesLeader = true;
    Console.WriteLine("[--leader-pow: BoardPow counts the LEADER (now the default — flag kept for symmetry)]");
}
// --no-leader-pow is the ONLY clean way to A/B the leader-pow claim now that it is the default. Toggling
// it on the SAME binary is essential: comparing bin/ship against an older build confounds leader-pow with
// granted-Blocker, MyDoubleAttackers and UsableDon all at once.
if (args.Contains("--no-leader-pow"))
{
    OnePieceTcg.Sim.Planning.ValueFunction.BoardPowIncludesLeader = false;
    Console.WriteLine("[--no-leader-pow: BoardPow counts CharacterArea only — the pre-2026-07-15 behaviour]");
}
if (args.Contains("--arch-weights"))
{
    OnePieceTcg.Sim.Planning.ValueFunction.ArchetypeWeights = true;
    Console.WriteLine("[--arch-weights: per-archetype rules of engagement — aggro races (board x0.5, life/finish x2)]");
}
if (args.Contains("--counter-afford"))
{
    OnePieceTcg.Sim.Planning.ValueFunction.CounterReserveIsAffordable = true;
    Console.WriteLine("[--counter-afford: MyCounterReserve counts only counter EVENTS the active DON can pay for]");
}
if (args.Contains("--dbl-attack"))
{
    OnePieceTcg.Sim.Planning.ValueFunction.LethalCountsDoubleAttack = true;
    Console.WriteLine("[--dbl-attack: DEAD FLAG — that behaviour measured -9.2pp and was reverted; Double Attack now lives in the MyDoubleAttackers feature]");
}
if (args.Contains("--sick-aware"))
{
    OnePieceTcg.Sim.Planning.ValueFunction.SummoningSickAware = true;
    Console.WriteLine("[--sick-aware: MyActiveAtk/LifeDanger count only Characters that can actually attack (engine IsSummoningSick; [Rush] is the exception)]");
}

switch (mode)
{
    case "smoke":
    {
        var cfg = new ExperimentConfig
        {
            experimentId = "smoke",
            decks = new List<string> { "st01", "st02", "st03" },
            gamesPerCondition = 50,
            saveDecisionLogs = true, decisionSampleRate = 0.1,
        };
        SelfPlayRunner.Run(cfg, BuildRegistry(out _));
        return 0;
    }

    case "deckcheck":
    {
        string path = args.Length > 1 ? args[1] : ImportedDecksDir;
        var files = Directory.Exists(path)
            ? Directory.EnumerateFiles(path).Where(f => f.EndsWith(".deck") || f.EndsWith(".txt")).OrderBy(f => f).ToList()
            : new List<string> { path };
        if (files.Count == 0) { Console.WriteLine($"No .deck/.txt files in {path}. Paste meta lists there, one file each."); return 0; }

        int ok = 0;
        foreach (var f in files)
        {
            var r = DeckLoader.LoadFile(f);
            string tag = r.Ok ? "OK " : "ERR";
            Console.WriteLine($"[{tag}] {Path.GetFileName(f)}  →  id='{r.Deck?.Id}' leader={r.Deck?.Leader} main={r.MainboardCount}");
            foreach (var e in r.Errors) Console.WriteLine($"        ERROR: {e}");
            foreach (var w in r.Warnings) Console.WriteLine($"        warn:  {w}");
            if (r.Ok) ok++;
        }
        Console.WriteLine($"\n{ok}/{files.Count} deck(s) valid and ready. Effect coverage across the library is near-total " +
                          $"(2 unimplemented cards; run Tools/Harness 'coverage' to re-verify for a new set).");
        return 0;
    }

    case "run":
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: run <config.json> [--games N]"); return 2; }
        var cfg = LoadJson<ExperimentConfig>(args[1]);
        if (cfg == null) return 2;
        for (int i = 2; i < args.Length - 1; i++)
            if (args[i] == "--games" && int.TryParse(args[i + 1], out var g)) cfg.gamesPerCondition = g;
        SelfPlayRunner.Run(cfg, BuildRegistry(out _));
        return 0;
    }

    case "heuristics":
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: heuristics <config.json>"); return 2; }
        var cfg = LoadJson<HeuristicConfig>(args[1]);
        if (cfg == null) return 2;
        HeuristicRunner.Run(cfg, BuildRegistry(out _));
        return 0;
    }

    case "fingerprint":
    {
        var reg = BuildRegistry(out _);
        var pool = reg.Ids.Where(i => !OnePieceTcg.Engine.CardData.StarterDecks.ContainsKey(i)).OrderBy(x => x).ToList();
        foreach (var id in pool)
        {
            var d = reg.Resolve(id);
            Console.WriteLine($"  {id,-26} {OnePieceTcg.Sim.Planning.DeckFingerprint.Describe(OnePieceTcg.Sim.Planning.DeckFingerprint.Analyze(d))}");
        }
        return 0;
    }

    case "planner-bench":
    {
        // Measures REAL two-planner games/s at tournament search budget — the number every
        // tournament-size estimate depends on. Reports single-thread cost and parallel throughput.
        int n = args.Length > 1 && int.TryParse(args[1], out var bn) ? bn : 12;
        int bdop = args.Length > 2 && int.TryParse(args[2], out var bd) ? bd : 1;
        var breg = BuildRegistry(out _);
        var bpool = breg.Ids.Where(i => !OnePieceTcg.Engine.CardData.StarterDecks.ContainsKey(i))
            .Where(id => { var d = breg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; }).OrderBy(x => x).ToList();
        var bcfg = new OnePieceTcg.Sim.Planning.PlannerTourConfig();
        long bwork = bcfg.workBudget; int bturncap = bcfg.maxTurns;
        for (int i = 1; i < args.Length - 1; i++)
        {
            if (args[i] == "--work" && long.TryParse(args[i + 1], out var wbv)) bwork = wbv;
            if (args[i] == "--turns" && int.TryParse(args[i + 1], out var tcv)) bturncap = tcv;
        }
        var bopt = new OnePieceTcg.Sim.Planning.TurnPlanner.Options { BeamWidth = bcfg.beamWidth, MaxDepth = bcfg.maxDepth, NodeBudget = bcfg.nodeBudget, WorkBudget = bwork };
        // --perturb S mimics the tournament's SEEDED population (random weights), whose games behave
        // very differently from default-weight games — that gap is the real tournament cost driver.
        double bpert = 0; for (int i = 1; i < args.Length - 1; i++) if (args[i] == "--perturb") double.TryParse(args[i + 1], out bpert);
        var bw = OnePieceTcg.Sim.Planning.ValueFunction.DefaultWeights();
        var brng = new System.Random(99);
        System.Func<double[]> mkW = () => { var c = (double[])OnePieceTcg.Sim.Planning.ValueFunction.DefaultWeights().Clone(); if (bpert > 0) lock (brng) for (int i = 0; i < c.Length; i++) c[i] += bpert * (brng.NextDouble() * 2 - 1); return c; };
        var bdefs = bpool.ToDictionary(d => d, breg.Resolve);
        var bctx = bpool.ToDictionary(d => d, d => OnePieceTcg.Sim.Planning.DeckFingerprint.Analyze(bdefs[d]));
        var bturns = new System.Collections.Concurrent.ConcurrentBag<int>();
        var btimes = new System.Collections.Concurrent.ConcurrentBag<double>();
        var bwork_ = new System.Collections.Concurrent.ConcurrentBag<long>();
        int bdone = 0; int bstuck = 0;
        var bsw = System.Diagnostics.Stopwatch.StartNew();
        System.Threading.Tasks.Parallel.For(0, n, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = bdop }, i =>
        {
            var dA = bpool[(i * 7) % bpool.Count]; var dB = bpool[(i * 13 + 1) % bpool.Count];
            var g = System.Diagnostics.Stopwatch.StartNew();
            // WorkUnits is [ThreadStatic] and one Parallel.For iteration owns its thread for the whole
            // game, so this delta is that game's true clone count — the deterministic cost metric budgets
            // are denominated in, and the only way to tell a budget problem from a clone-speed problem.
            long w0 = OnePieceTcg.Sim.Search.LegalActions.WorkUnits;
            var st = OnePieceTcg.Sim.Planning.GameRunner.Play(
                new OnePieceTcg.Sim.Planning.PlannerBot(mkW(), bctx[dA], bopt),
                new OnePieceTcg.Sim.Planning.PlannerBot(mkW(), bctx[dB], bopt),
                bdefs[dA], bdefs[dB], $"bench:{i}", i % 2 == 0 ? "south" : "north", bcfg.commandCap, bturncap);
            g.Stop();
            bwork_.Add(OnePieceTcg.Sim.Search.LegalActions.WorkUnits - w0);
            btimes.Add(g.Elapsed.TotalSeconds); bturns.Add(st.TurnNumber);
            if (OnePieceTcg.Sim.Planning.GameRunner.Stuck) System.Threading.Interlocked.Increment(ref bstuck);
            if (OnePieceTcg.Sim.Planning.GameRunner.Winner(st) != null) System.Threading.Interlocked.Increment(ref bdone);
        });
        bsw.Stop();
        var tl = btimes.OrderBy(x => x).ToList();
        Console.WriteLine($"\n=== planner-bench: {n} two-planner games, dop={bdop} ===");
        Console.WriteLine($"  budget: beam={bcfg.beamWidth} depth={bcfg.maxDepth} nodes={bcfg.nodeBudget} work={bwork} turnCap={bturncap}");
        Console.WriteLine($"  per-game seconds: mean={tl.Average():F2}  median={tl[tl.Count/2]:F2}  min={tl.First():F2}  max={tl.Last():F2}");
        Console.WriteLine($"  turns/game: mean={bturns.Average():F1}  max={bturns.Max()}");
        var wl = bwork_.OrderBy(x => x).ToList();
        Console.WriteLine($"  clones/game: mean={wl.Average():F0}  median={wl[wl.Count/2]}  max={wl.Last()}  ({wl.Average()/System.Math.Max(1,bturns.Average()):F0}/turn)");
        Console.WriteLine($"  clones/sec: {wl.Sum()/System.Math.Max(0.001,tl.Sum()):F0}");
        Console.WriteLine($"  decisive: {bdone}/{n}");
        Console.WriteLine($"  STUCK (aborted — no legal command for either seat): {bstuck}/{n} = {100.0 * bstuck / n:F1}%   <-- forfeits, not losses");
        Console.WriteLine($"  wall={bsw.Elapsed.TotalSeconds:F1}s  throughput={n / bsw.Elapsed.TotalSeconds:F2} games/s");
        return 0;
    }

    case "clone-bench":
    {
        var cbreg = BuildRegistry(out _);
        var cbpool = cbreg.Ids.Where(i => !OnePieceTcg.Engine.CardData.StarterDecks.ContainsKey(i))
            .Where(id => { var d = cbreg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; }).OrderBy(x => x).ToList();
        OnePieceTcg.Sim.Search.CloneBench.Run(cbreg.Resolve(cbpool[0]), cbreg.Resolve(cbpool[1]), 24);
        return 0;
    }

    case "value-audit":
    {
        // What does the eval actually weigh on real positions? A weight means nothing without its
        // feature's scale — see ValueAudit.
        int van = args.Length > 1 && int.TryParse(args[1], out var vn) ? vn : 6;
        var vreg = BuildRegistry(out _);
        var vpool = vreg.Ids.Where(i => !OnePieceTcg.Engine.CardData.StarterDecks.ContainsKey(i))
            .Where(id => { var d = vreg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; }).OrderBy(x => x).ToList();
        var vcfg = new OnePieceTcg.Sim.Planning.PlannerTourConfig();
        var vdefs = vpool.ToDictionary(d => d, vreg.Resolve);
        var vctx = vpool.ToDictionary(d => d, d => OnePieceTcg.Sim.Planning.DeckFingerprint.Analyze(vdefs[d]));
        var vopt = new OnePieceTcg.Sim.Planning.TurnPlanner.Options { BeamWidth = vcfg.beamWidth, MaxDepth = vcfg.maxDepth, NodeBudget = vcfg.nodeBudget, WorkBudget = 800, ConsiderTruncatedLines = true };
        OnePieceTcg.Sim.Planning.ValueAudit.Run(vpool, vdefs, vctx, vopt, van, 24);
        return 0;
    }

    case "planner-duel":
    {
        // Absolute yardstick for the tournament. Ladder Elo is RELATIVE to a co-evolving population, so a
        // flat topElo cannot distinguish "champion improving with the field" from "nothing learned". This
        // plays two weight vectors (any two ladder generations, or one vs the fixed BaselineAgent) directly.
        // Every matchup is played twice on the SAME seed with seats swapped, so seat advantage and deck luck
        // cancel out and the remaining difference is attributable to the weights.
        // Usage: planner-duel <ladder.csv> <genA> <genB|baseline> [N] [dop]
        if (args.Length < 4) { Console.WriteLine("usage: planner-duel <ladder.csv> <genA> <genB|baseline> [N] [dop]"); return 1; }
        string csvPath = args[1];
        var lines = System.IO.File.ReadAllLines(csvPath);
        System.Func<string, double[]> rowOf = gen =>
        {
            foreach (var ln in lines.Skip(1))
            {
                var p = ln.Split(',');
                if (p.Length > 3 && p[0] == gen) return p.Skip(3).Select(x => double.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            }
            throw new System.Exception($"generation {gen} not found in {csvPath}");
        };
        // A genome spec is: a ladder generation number, "default" (untuned DefaultWeights), or
        // "baseline" (the fixed BaselineAgent — not a genome at all, hence null).
        System.Func<string, double[]> genomeOf = spec =>
            spec.Equals("baseline", System.StringComparison.OrdinalIgnoreCase) ? null
            : spec.Equals("default", System.StringComparison.OrdinalIgnoreCase) ? OnePieceTcg.Sim.Planning.ValueFunction.DefaultWeights()
            : rowOf(spec);
        var wA = genomeOf(args[2]);
        bool vsBaseline = args[3].Equals("baseline", System.StringComparison.OrdinalIgnoreCase);
        var wB = genomeOf(args[3]);
        int dn = args.Length > 4 && int.TryParse(args[4], out var dnv) ? dnv : 60;
        int ddop = args.Length > 5 && int.TryParse(args[5], out var ddv) ? ddv : 24;

        var dreg = BuildRegistry(out _);
        var dpool = dreg.Ids.Where(i => !OnePieceTcg.Engine.CardData.StarterDecks.ContainsKey(i))
            .Where(id => { var d = dreg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; }).OrderBy(x => x).ToList();
        // --arch <aggro|midrange|control|combo>: restrict the deck pool to ONE archetype. Aggro is 6 of 41
        // decks, so a general duel spends only ~14% of its games on the archetype that is actually broken
        // (26.2% in mirrors) — ~42 games, ±13pp, which cannot resolve a fix. Filtering the pool puts the
        // whole sample on the deck class under test.
        for (int i = 1; i < args.Length - 1; i++)
            if (args[i] == "--arch")
            {
                string want = args[i + 1];
                dpool = dpool.Where(id => OnePieceTcg.Sim.Planning.DeckFingerprint
                    .Analyze(dreg.Resolve(id)).Archetype == want).ToList();
                if (dpool.Count == 0) { Console.WriteLine($"no decks with archetype '{want}'"); return 1; }
                Console.WriteLine($"[--arch {want}: pool restricted to {dpool.Count} deck(s): {string.Join(", ", dpool)}]");
            }
        var dcfg = new OnePieceTcg.Sim.Planning.PlannerTourConfig();
        // --full runs the SHIPPING search budget instead of the tournament's reduced one. The weights are
        // evolved under a cut-down search for speed; whether they transfer to the real budget is a separate
        // question from whether they win the tournament, and only this flag can answer it.
        var dfull = new OnePieceTcg.Sim.Planning.TurnPlanner.Options();   // shipping defaults
        bool useFull = args.Contains("--full");
        var dopt = useFull
            ? new OnePieceTcg.Sim.Planning.TurnPlanner.Options { BeamWidth = dfull.BeamWidth, MaxDepth = dfull.MaxDepth, NodeBudget = dfull.NodeBudget, WorkBudget = 0 }
            : new OnePieceTcg.Sim.Planning.TurnPlanner.Options { BeamWidth = dcfg.beamWidth, MaxDepth = dcfg.maxDepth, NodeBudget = dcfg.nodeBudget, WorkBudget = dcfg.workBudget };
        int dturns = dcfg.maxTurns;
        for (int i = 1; i < args.Length - 1; i++)
        {
            if (args[i] == "--work" && long.TryParse(args[i + 1], out var dwv)) dopt.WorkBudget = dwv;
            if (args[i] == "--turns" && int.TryParse(args[i + 1], out var dtv)) dturns = dtv;
            // beam/depth/nodes were picked for SPEED and never measured against STRENGTH. Work saturates
            // at ~4000 only because NodeBudget=120 becomes the binding constraint, so the search frontier
            // cannot be mapped without these.
            if (args[i] == "--nodes" && int.TryParse(args[i + 1], out var dnv2)) dopt.NodeBudget = dnv2;
            if (args[i] == "--beam" && int.TryParse(args[i + 1], out var dbv2)) dopt.BeamWidth = dbv2;
            if (args[i] == "--depth" && int.TryParse(args[i + 1], out var ddv2)) dopt.MaxDepth = ddv2;
        }
        // --reply-ply: score each line AFTER the opponent's reply turn instead of at the instant our turn
        // ends. Without it the search is 1-ply and literally cannot see a lethal counter-swing.
        // REBUILT 2026-07-15 as a top-K re-rank with its own budget. The old inline-per-leaf version was
        // recorded as "invalid — starves the search"; it also mixed replied and non-replied scores in one
        // comparison, so the CONCEPT has never actually been measured. Treat prior --reply-ply numbers (40%)
        // as measuring the bug, not the idea.
        if (args.Contains("--reply-ply")) dopt.OpponentReplyPly = true;
        // Reply budget is spent ON TOP of --work (free at the knee, where NodeBudget binds), so --reply-ply
        // on/off is a clean one-variable A/B: identical search, with or without the reply-informed re-rank.
        dopt.ReplyBudget = dopt.WorkBudget > 0 ? dopt.WorkBudget : 20000;
        for (int i = 1; i < args.Length - 1; i++)
        {
            if (args[i] == "--reply-budget" && long.TryParse(args[i + 1], out var rb)) dopt.ReplyBudget = rb;
            if (args[i] == "--rerank-k" && int.TryParse(args[i + 1], out var rk)) dopt.RerankK = rk;
        }
        // Match PlannerTournament: a work budget can truncate the search, and without this a truncated
        // plan falls back to a passive endTurn. Leaving it off here made the duel measure a DIFFERENT bot
        // from the one the tournament evolves, so the duel's win rate could not anchor the benchmark.
        dopt.ConsiderTruncatedLines = dopt.WorkBudget > 0;
        // --trunc on|off isolates ConsiderTruncatedLines from the work budget. The tournament forces it on
        // whenever a budget is set, so it has never been measured on its own — and by its own docs it lets
        // an incomplete line outscore a complete one, which is a plausible passivity source.
        for (int i = 1; i < args.Length - 1; i++)
            if (args[i] == "--trunc") dopt.ConsiderTruncatedLines = args[i + 1] == "on";
        Console.WriteLine($"  budget: beam={dopt.BeamWidth} depth={dopt.MaxDepth} nodes={dopt.NodeBudget} work={dopt.WorkBudget} truncLines={dopt.ConsiderTruncatedLines} turnCap={dturns}");
        var ddefs = dpool.ToDictionary(d => d, dreg.Resolve);
        var dctx = dpool.ToDictionary(d => d, d => OnePieceTcg.Sim.Planning.DeckFingerprint.Analyze(ddefs[d]));
        System.Func<double[], string, OnePieceTcg.Sim.IAgent> mk = (w, deck) =>
            (w == null) ? (OnePieceTcg.Sim.IAgent)new OnePieceTcg.Sim.BaselineAgent()
                        : new OnePieceTcg.Sim.Planning.PlannerBot(w, dctx[deck], dopt);

        int aWins = 0, bWins = 0, drawn = 0;
        // Per-game attribution for --by-deck. The headline win rate averages over every archetype in the
        // pool, so a bot that crushes aggro and gets crushed by control reads as a flat coin flip — the
        // average is the one number that CANNOT see a matchup-shaped gap. (aDeck = the deck A actually
        // played that game, which swaps with the seats.)
        var perGame = new List<(string aDeck, string bDeck, bool? aWon, bool adj)>();
        var pairsList = new List<int>(); for (int i = 0; i < dn / 2; i++) pairsList.Add(i);
        var dlock = new object();
        System.Threading.Tasks.Parallel.ForEach(pairsList, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = ddop }, i =>
        {
            // --mirror plays BOTH genomes on the SAME deck. Deck advantage then contributes zero variance
            // instead of dominating each game, so the measured win rate reflects the genome difference
            // rather than the draw. Comparing the same genome pair with and without this is the honest
            // test of whether mirroring actually buys signal.
            string dA = dpool[(i * 7) % dpool.Count], dB = dpool[(i * 13 + 1) % dpool.Count];
            if (args.Contains("--mirror")) dB = dA;
            // same seed, seats swapped — the paired comparison
            var s1 = OnePieceTcg.Sim.Planning.GameRunner.Play(mk(wA, dA), mk(wB, dB), ddefs[dA], ddefs[dB], $"duel:{i}", "south", dcfg.commandCap, dturns);
            var s2 = OnePieceTcg.Sim.Planning.GameRunner.Play(mk(wB, dA), mk(wA, dB), ddefs[dA], ddefs[dB], $"duel:{i}", "south", dcfg.commandCap, dturns);
            // Result, not Winner: score truncated games by the rules-8.2 tiebreak exactly as the ladder
            // does, so this yardstick and the thing it measures agree on what winning means.
            var w1 = OnePieceTcg.Sim.Planning.GameRunner.Result(s1);   // south = A
            var w2 = OnePieceTcg.Sim.Planning.GameRunner.Result(s2);   // south = B
            // Winner() is null exactly when nobody actually won and rules-8.2 decided it on raw Life. That
            // tiebreak is a known turtle incentive, so a win rate that silently blends played-out wins with
            // life-hoarding adjudications cannot tell "good bot" from "bot that sat still".
            bool adj1 = OnePieceTcg.Sim.Planning.GameRunner.Winner(s1) == null;
            bool adj2 = OnePieceTcg.Sim.Planning.GameRunner.Winner(s2) == null;
            lock (dlock)
            {
                if (w1 == "south") aWins++; else if (w1 == "north") bWins++; else drawn++;
                if (w2 == "north") aWins++; else if (w2 == "south") bWins++; else drawn++;
                // game 1: A is south on dA. game 2: seats swap, so A is north on dB.
                perGame.Add((dA, dB, w1 == "south" ? true : w1 == "north" ? (bool?)false : null, adj1));
                perGame.Add((dB, dA, w2 == "north" ? true : w2 == "south" ? (bool?)false : null, adj2));
            }
        });
        int decided = aWins + bWins;
        double p = decided > 0 ? (double)aWins / decided : 0;
        double z = 1.96, den = 1 + z * z / decided;
        double centre = (p + z * z / (2 * decided)) / den;
        double half = z * System.Math.Sqrt(p * (1 - p) / decided + z * z / (4.0 * decided * decided)) / den;
        Console.WriteLine($"\n=== duel: gen{args[2]} vs {(vsBaseline ? "BASELINE" : "gen" + args[3])} ===");
        Console.WriteLine($"  A(gen{args[2]}) wins {aWins} | B wins {bWins} | no-result {drawn}");
        Console.WriteLine($"  A win rate = {100 * p:F1}%   95% CI [{100 * (centre - half):F1}%, {100 * (centre + half):F1}%]  (n={decided} decided)");
        Console.WriteLine(centre - half > 0.5 ? "  => A is significantly STRONGER" : (centre + half < 0.5 ? "  => A is significantly WEAKER" : "  => NOT significant — indistinguishable at this n"));
        // --by-deck: split the win rate by the deck the PLANNER played and by the deck it FACED. The question
        // it answers is whether the gap to the IntermediateBot is matchup-shaped — i.e. whether one set of
        // rules of engagement is being averaged across archetypes that want opposite play.
        if (args.Contains("--by-deck"))
        {
            // How many games were actually WON versus handed over by the rules-8.2 life tiebreak, and does
            // the archetype spread live in the played-out games or only in the adjudicated ones? If aggro's
            // deficit is concentrated in adjudications, the bot is fine and the YARDSTICK is the bug.
            {
                int adjN = perGame.Count(g => g.adj), tot = perGame.Count;
                Console.WriteLine($"\n  === ADJUDICATION (rules-8.2 raw-Life tiebreak) ===");
                Console.WriteLine($"  {adjN}/{tot} games ({100.0 * adjN / tot:F1}%) hit the turn cap and were decided on LIFE, not won");
                foreach (var grp in new[] { (t: "OUTRIGHT wins only", f: (System.Func<bool, bool>)(a => !a)),
                                            (t: "ADJUDICATED only", f: a => a) })
                {
                    var sub = perGame.Where(g => g.aWon.HasValue && grp.f(g.adj)).ToList();
                    if (sub.Count == 0) { Console.WriteLine($"  {grp.t}: none"); continue; }
                    Console.WriteLine($"  --- {grp.t} (n={sub.Count}) ---");
                    foreach (var g in sub.GroupBy(x => dctx[x.aDeck].Archetype).OrderBy(x => x.Key))
                        Console.WriteLine($"    {g.Key,-9} {100.0 * g.Count(x => x.aWon.Value) / g.Count(),5:F1}%  ({g.Count(x => x.aWon.Value),3}/{g.Count(),3})");
                }
            }
            void Table(string title, System.Func<(string aDeck, string bDeck, bool? aWon, bool adj), string> key)
            {
                Console.WriteLine($"\n  === {title} ===");
                var rows = perGame.Where(g => g.aWon.HasValue).GroupBy(key)
                    .Select(g => new { Deck = g.Key, W = g.Count(x => x.aWon.Value), N = g.Count() })
                    .OrderBy(r => (double)r.W / r.N).ToList();
                foreach (var r in rows)
                {
                    var lead = ddefs.TryGetValue(r.Deck, out var dd) ? dd.Leader : "?";
                    var c = dctx.TryGetValue(r.Deck, out var cc)
                        ? OnePieceTcg.Sim.Planning.DeckFingerprint.Describe(cc) : "";
                    Console.WriteLine($"  {100.0 * r.W / r.N,5:F1}%  ({r.W,3}/{r.N,3})  {r.Deck,-26} {lead,-12} {c}");
                }
                if (rows.Count > 1)
                    Console.WriteLine($"  spread: worst {100.0 * rows[0].W / rows[0].N:F1}% → best " +
                        $"{100.0 * rows[^1].W / rows[^1].N:F1}%  ({rows.Count} decks)");
            }
            Table("PLANNER win rate BY ITS OWN DECK", g => g.aDeck);
            Table("PLANNER win rate BY OPPONENT DECK", g => g.bDeck);
            // The decisive cut for "rules of engagement per leader style": if one weight vector is being
            // averaged across archetypes that want OPPOSITE play, it shows up here as a spread. A flat
            // profile means per-archetype rules have nothing to fix and the gap lives elsewhere.
            void ArchTable(string title, System.Func<(string aDeck, string bDeck, bool? aWon, bool adj), string> deckKey)
            {
                Console.WriteLine($"\n  === {title} ===");
                foreach (var g in perGame.Where(x => x.aWon.HasValue)
                             .GroupBy(x => dctx[deckKey(x)].Archetype).OrderBy(x => x.Key))
                {
                    int w = g.Count(x => x.aWon.Value), n2 = g.Count();
                    double pp = (double)w / n2;
                    double d2 = 1 + 1.96 * 1.96 / n2;
                    double ce = (pp + 1.96 * 1.96 / (2 * n2)) / d2;
                    double hf = 1.96 * System.Math.Sqrt(pp * (1 - pp) / n2 + 1.96 * 1.96 / (4.0 * n2 * n2)) / d2;
                    Console.WriteLine($"  {g.Key,-9} {100 * pp,5:F1}%  ({w,3}/{n2,3})  95% CI [{100 * (ce - hf):F1}%, {100 * (ce + hf):F1}%]");
                }
            }
            ArchTable("BY PLANNER'S OWN ARCHETYPE", x => x.aDeck);
            ArchTable("BY OPPONENT ARCHETYPE", x => x.bDeck);
        }
        // Prove the mechanism ENGAGED before believing any win rate it produced. An all-abstain re-rank plays
        // exactly like reply-ply off, which would otherwise read as "the idea does not help".
        if (dopt.OpponentReplyPly)
        {
            long applied = OnePieceTcg.Sim.Planning.TurnPlanner.RerankApplied, abstained = OnePieceTcg.Sim.Planning.TurnPlanner.RerankAbstained;
            long done = OnePieceTcg.Sim.Planning.TurnPlanner.ReplyComplete, oob = OnePieceTcg.Sim.Planning.TurnPlanner.ReplyOutOfBudget;
            long stuck = OnePieceTcg.Sim.Planning.TurnPlanner.ReplyStuck, cap = OnePieceTcg.Sim.Planning.TurnPlanner.ReplyHitCap;
            double appliedPct = applied + abstained > 0 ? 100.0 * applied / (applied + abstained) : 0;
            double donePct = done + oob + stuck + cap > 0 ? 100.0 * done / (done + oob + stuck + cap) : 0;
            long changed = OnePieceTcg.Sim.Planning.TurnPlanner.RerankChangedMind;
            double changedPct = applied > 0 ? 100.0 * changed / applied : 0;
            double bite = OnePieceTcg.Sim.Planning.TurnPlanner.BiteN > 0
                ? OnePieceTcg.Sim.Planning.TurnPlanner.BiteSum / OnePieceTcg.Sim.Planning.TurnPlanner.BiteN : 0;
            Console.WriteLine($"\n  reply-ply: re-rank APPLIED on {applied}/{applied + abstained} turns ({appliedPct:F1}%)");
            Console.WriteLine($"  replies complete {done} ({donePct:F1}%) | out-of-budget {oob} | stuck {stuck} | hit-cap {cap}");
            Console.WriteLine($"  CHANGED THE PLAN on {changed}/{applied} turns ({changedPct:F1}%) | mean reply bite = {bite:F2}");
            if (appliedPct < 50) Console.WriteLine("  ⚠ re-rank mostly ABSTAINED — this run did NOT test the idea; raise --reply-budget.");
            if (changedPct < 5) Console.WriteLine("  ⚠ re-rank is INERT — it re-elects the myopic line; the win rate is NOT testing the idea.");
        }
        return 0;
    }

    case "planner-tournament":
    {
        var cfg = new OnePieceTcg.Sim.Planning.PlannerTourConfig();
        for (int i = 1; i < args.Length - 1; i++)
        {
            if (args[i] == "--gens" && int.TryParse(args[i + 1], out var gn)) cfg.generations = gn;
            if (args[i] == "--pop" && int.TryParse(args[i + 1], out var pp)) cfg.populationSize = pp;
            if (args[i] == "--work" && long.TryParse(args[i + 1], out var wk)) cfg.workBudget = wk;
            if (args[i] == "--turns" && int.TryParse(args[i + 1], out var tc)) cfg.maxTurns = tc;
            if (args[i] == "--rounds" && int.TryParse(args[i + 1], out var rd)) cfg.roundsPerGeneration = rd;
            if (args[i] == "--gpp" && int.TryParse(args[i + 1], out var gp)) cfg.gamesPerPairing = gp;
            if (args[i] == "--mirror") cfg.mirrorMatches = args[i + 1] == "on";
            if (args[i] == "--nodes" && int.TryParse(args[i + 1], out var nb)) cfg.nodeBudget = nb;
            if (args[i] == "--beam" && int.TryParse(args[i + 1], out var bw)) cfg.beamWidth = bw;
            if (args[i] == "--depth" && int.TryParse(args[i + 1], out var md)) cfg.maxDepth = md;
            if (args[i] == "--id") cfg.experimentId = args[i + 1];
            if (args[i] == "--bench-every" && int.TryParse(args[i + 1], out var be)) cfg.benchmarkEvery = be;
            if (args[i] == "--bench-games" && int.TryParse(args[i + 1], out var bg)) cfg.benchmarkGames = bg;
        }
        OnePieceTcg.Sim.Planning.PlannerTournament.Run(cfg, BuildRegistry(out _));
        return 0;
    }

    case "plannertrace":
    case "plannertest":
    {
        var reg = BuildRegistry(out _);
        var pool = reg.Ids.Where(i => !OnePieceTcg.Engine.CardData.StarterDecks.ContainsKey(i))
            .Where(id => { var d = reg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; }).OrderBy(x => x).ToList();

        // --tourbudget traces at the TOURNAMENT search budget rather than the full one. The tournament's
        // reduced budget is a different bot: a truncated search is precisely how passivity could creep
        // back in, so the "does it attack?" check has to be run against the budget we actually ship to it.
        var tcfg = new OnePieceTcg.Sim.Planning.PlannerTourConfig();
        bool tourBudget = args.Contains("--tourbudget");
        int traceTurnCap = 60;
        for (int i = 1; i < args.Length - 1; i++)
        {
            if (args[i] == "--work" && long.TryParse(args[i + 1], out var twv)) tcfg.workBudget = twv;
            if (args[i] == "--turns" && int.TryParse(args[i + 1], out var ttv)) traceTurnCap = ttv;
        }
        var traceOpt = tourBudget
            ? new OnePieceTcg.Sim.Planning.TurnPlanner.Options { BeamWidth = tcfg.beamWidth, MaxDepth = tcfg.maxDepth, NodeBudget = tcfg.nodeBudget, WorkBudget = tcfg.workBudget }
            : null;

        // --champ <ladder.csv> <gen> traces the EVOLVED weights, not the defaults. The whole point of the
        // rule "verify on a traced game before trusting a win rate" is that it must be run on the bot we
        // intend to believe in — tracing default weights would validate a bot nobody is proposing to ship.
        double[] traceW = null;
        for (int i = 1; i < args.Length - 2; i++)
            if (args[i] == "--champ")
            {
                var cl = System.IO.File.ReadAllLines(args[i + 1]); string wantGen = args[i + 2];
                foreach (var ln in cl.Skip(1))
                {
                    var p = ln.Split(',');
                    if (p.Length > 3 && p[0] == wantGen) { traceW = p.Skip(3).Select(x => double.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray(); break; }
                }
                if (traceW == null) { Console.WriteLine($"generation {wantGen} not found in {args[i + 1]}"); return 1; }
                Console.WriteLine($"[tracing EVOLVED weights from {args[i + 1]} gen {wantGen}]");
            }

        System.Func<OnePieceTcg.Engine.DeckDef, OnePieceTcg.Engine.DeckDef, string, string, OnePieceTcg.Engine.GameState> playFull =
            (sDef, nDef, seed, first) =>
            OnePieceTcg.Sim.Planning.GameRunner.Play(
                new OnePieceTcg.Sim.Planning.PlannerBot(traceW, OnePieceTcg.Sim.Planning.DeckFingerprint.Analyze(sDef), traceOpt),
                new OnePieceTcg.Sim.BaselineAgent(),
                sDef, nDef, seed, first, 20000, traceTurnCap);

        if (mode == "plannertrace")
        {
            // --seed / --deck let us trace a game the bot LOSES. Tracing one fixed winnable game tells us
            // nothing about the ~50% it loses, and "it attacked on the traced game" is precisely the check
            // that passed while the bot was losing 94%.
            string tseed = "trace:1"; int tdA = 0, tdB = 1;
            for (int i = 1; i < args.Length - 1; i++)
            {
                if (args[i] == "--seed") tseed = args[i + 1];
                if (args[i] == "--deck-a" && int.TryParse(args[i + 1], out var ta)) tdA = ta % pool.Count;
                if (args[i] == "--deck-b" && int.TryParse(args[i + 1], out var tb)) tdB = tb % pool.Count;
            }
            // --full-log dumps EVERY event, not just combat. The default filter (attack|takes|damage|
            // wins|blocks|counter) hides every MAIN-PHASE decision — playCard, attachDon, activateMain,
            // endTurn — which is where DON management and card choice actually happen. Those have never
            // been inspected.
            bool tfull = args.Contains("--full-log");
            var st = playFull(reg.Resolve(pool[tdA]), reg.Resolve(pool[tdB]), tseed, "south");
            Console.WriteLine($"\n=== {pool[tdA]} (PlannerBot, south) vs {pool[tdB]} (baseline, north) seed={tseed} ===");
            var evs = st.EventLog.Where(e => e.Message != null);
            if (!tfull) evs = evs.Where(e => System.Text.RegularExpressions.Regex.IsMatch(e.Message, "(?i)attack|takes|damage|wins|blocks|counter"));
            foreach (var e in evs.TakeLast(tfull ? 200 : 40))
                Console.WriteLine($"  t{e.Turn} [{e.Actor}]: {e.Message}");
            Console.WriteLine($"status={st.Status} turn={st.TurnNumber} winner={OnePieceTcg.Sim.Planning.GameRunner.Result(st) ?? "none"}");
            return 0;
        }
        else
        {
            int n = args.Length > 1 && int.TryParse(args[1], out var nn) ? nn : 20;
            long wins = 0, done = 0; var rng = new System.Random(5); var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < n; i++)
            {
                var st = playFull(reg.Resolve(pool[rng.Next(pool.Count)]), reg.Resolve(pool[rng.Next(pool.Count)]), $"pt:{i}", "south");
                var last = st.EventLog.LastOrDefault(e => e.Message != null && e.Message.Contains("wins"));
                if (last != null) { done++; if (last.Message.StartsWith("South")) wins++; }
            }
            Console.WriteLine($"PlannerBot vs Baseline: {wins}/{done} = {(done>0?100.0*wins/done:0):F1}% ({n} games, {sw.Elapsed.TotalSeconds:F0}s)");
            return 0;
        }
    }

    case "deckrank":
    {
        // Round-robin every clean meta deck vs every other (both orders), champion piloting BOTH
        // sides, and rank decks by win rate. CAVEAT: the bot pilots all decks the same way, so this
        // is deck power *in the bot's hands*, not true competitive tier (see the improvement doc).
        int seeds = args.Length > 1 && int.TryParse(args[1], out var ss) ? ss : 6;
        var reg = BuildRegistry(out _);
        var pool = reg.Ids.Where(i => !OnePieceTcg.Engine.CardData.StarterDecks.ContainsKey(i))
            .Where(id => { var d = reg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; })
            .OrderBy(x => x).ToList();
        var wins = pool.ToDictionary(d => d, d => 0L);
        var games = pool.ToDictionary(d => d, d => 0L);
        var champ = OnePieceTcg.Sim.Learning.BotTiers.IntermediateGenome();
        System.Func<OnePieceTcg.Sim.IAgent> mk = () => new OnePieceTcg.Sim.Learning.WeightedAgent(champ);
        long total = 0; var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int a = 0; a < pool.Count; a++)
        for (int b = 0; b < pool.Count; b++)
        {
            if (a == b) continue;
            for (int s = 0; s < seeds; s++)
            {
                var rec = OnePieceTcg.Sim.MatchDriver.Play(mk(), mk(), reg.Resolve(pool[a]), reg.Resolve(pool[b]),
                    $"rank:{a}:{b}:{s}", s % 2 == 0 ? "south" : "north",
                    new OnePieceTcg.Sim.MatchDriver.Options { CommandCap = 20000, AgentChoosesTurnOrder = true });
                if (rec.winner != null)
                {
                    games[pool[a]]++; games[pool[b]]++;
                    if (rec.winner == "south") wins[pool[a]]++; else wins[pool[b]]++;
                    total++;
                }
            }
        }
        Console.WriteLine($"\n=== Meta deck win rates ({total:N0} games, {pool.Count} decks, champion piloting both sides) ===");
        foreach (var kv in pool.Select(d => (deck: d, wr: games[d] > 0 ? (double)wins[d] / games[d] : 0, n: games[d]))
                                .OrderByDescending(x => x.wr))
            Console.WriteLine($"  {kv.wr,6:P1}  {kv.deck,-26} ({kv.n} games)");
        Console.WriteLine($"({sw.Elapsed.TotalSeconds:F0}s)");
        return 0;
    }

    case "duel":
    {
        // duel [games] → emerging tournament champion vs the current BotTiers champion (WeightedAgent).
        int n = args.Length > 1 && int.TryParse(args[1], out var nn) ? nn : 200;
        var reg = BuildRegistry(out _);
        var pool = reg.Ids.Where(i => !OnePieceTcg.Engine.CardData.StarterDecks.ContainsKey(i))
            .Where(id => { var d = reg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; }).ToList();
        var emerging = new OnePieceTcg.Sim.Learning.Genome { MulliganKeep = 4.0, FaceBias = 0.81, CounterLifeFloor = 5.0, CounterCharCost = 4.96, BlockBias = 0.95, TurnOrderFirst = 0.75 };
        var current = OnePieceTcg.Sim.Learning.BotTiers.IntermediateGenome();
        long aWins = 0, done = 0; var rng = new System.Random(11);
        for (int i = 0; i < n; i++)
        {
            string dA = pool[rng.Next(pool.Count)], dB = pool[rng.Next(pool.Count)];
            string first = i % 2 == 0 ? "south" : "north";
            var rec = OnePieceTcg.Sim.MatchDriver.Play(
                new OnePieceTcg.Sim.Learning.WeightedAgent(emerging, "emerging"),
                new OnePieceTcg.Sim.Learning.WeightedAgent(current, "current"),
                reg.Resolve(dA), reg.Resolve(dB), $"duel:{i}", first,
                new OnePieceTcg.Sim.MatchDriver.Options { CommandCap = 20000, AgentChoosesTurnOrder = true });
            if (rec.winner == "south") aWins++;
            if (rec.winner != null) done++;
        }
        Console.WriteLine($"emerging champ vs current champ: {aWins}/{done} = {(done>0?100.0*aWins/done:0):F1}% ({n} games)");
        return 0;
    }

    case "tiers":
    {
        // tiers <A> <B> [games]  → win rate of tier A vs tier B on meta decks
        string tierA = args.Length > 1 ? args[1] : "intermediate";
        string tierB = args.Length > 2 ? args[2] : "beginner";
        int n = args.Length > 3 && int.TryParse(args[3], out var nn) ? nn : 60;
        var reg = BuildRegistry(out _);
        var pool = reg.Ids.Where(i => !OnePieceTcg.Engine.CardData.StarterDecks.ContainsKey(i))
            .Where(id => { var d = reg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; }).ToList();
        long aWins = 0, done = 0; var rng = new System.Random(3); var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < n; i++)
        {
            string dA = pool[rng.Next(pool.Count)], dB = pool[rng.Next(pool.Count)];
            string first = i % 2 == 0 ? "south" : "north";
            var rec = OnePieceTcg.Sim.MatchDriver.Play(
                OnePieceTcg.Sim.Learning.BotTiers.Make(tierA), OnePieceTcg.Sim.Learning.BotTiers.Make(tierB),
                reg.Resolve(dA), reg.Resolve(dB), $"tiers:{i}", first,
                new OnePieceTcg.Sim.MatchDriver.Options { CommandCap = 20000 });
            if (rec.winner == "south") aWins++;
            if (rec.winner != null) done++;
        }
        Console.WriteLine($"{tierA} vs {tierB}: {aWins}/{done} = {(done>0?100.0*aWins/done:0):F1}% ({n} games, {sw.Elapsed.TotalSeconds:F0}s)");
        return 0;
    }

    case "evolve-search":
    {
        var cfg = new OnePieceTcg.Sim.Search.EvalEvolveConfig();
        for (int i = 1; i < args.Length - 1; i++)
        {
            if (args[i] == "--gens" && int.TryParse(args[i + 1], out var gn)) cfg.generations = gn;
            if (args[i] == "--games" && int.TryParse(args[i + 1], out var gm)) cfg.gamesPerEval = gm;
            if (args[i] == "--rollout" && int.TryParse(args[i + 1], out var rc)) cfg.rolloutCap = rc;
        }
        OnePieceTcg.Sim.Search.EvalEvolver.Run(cfg, BuildRegistry(out _));
        return 0;
    }

    case "searchtest":
    {
        var reg = BuildRegistry(out _);
        int games = args.Length > 1 && int.TryParse(args[1], out var gg) ? gg : 30;
        int rollout = args.Length > 2 && int.TryParse(args[2], out var rr) ? rr : 0; // 0 = 1-ply eval; >0 = rollout cap
        var metaIds = reg.Ids.Where(i => !OnePieceTcg.Engine.CardData.StarterDecks.ContainsKey(i)).ToList();
        var pool = metaIds.Where(id => { var d = reg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; }).ToList();
        int searchWins = 0, done = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var rng = new System.Random(7);
        for (int i = 0; i < games; i++)
        {
            string dA = pool[rng.Next(pool.Count)], dB = pool[rng.Next(pool.Count)];
            string first = i % 2 == 0 ? "south" : "north";
            var rec = OnePieceTcg.Sim.MatchDriver.Play(
                new OnePieceTcg.Sim.Search.SearchAgent(null, null, "search", rollout), new OnePieceTcg.Sim.BaselineAgent(),
                reg.Resolve(dA), reg.Resolve(dB), $"search:{i}", first,
                new OnePieceTcg.Sim.MatchDriver.Options { CommandCap = 20000 });
            if (rec.winner == "south") searchWins++;
            if (rec.winner != null) done++;
        }
        Console.WriteLine($"SearchAgent ({(rollout>0?"rollout cap "+rollout:"1-ply")}) vs Baseline: {searchWins}/{done} = {(done>0?100.0*searchWins/done:0):F1}% over {sw.Elapsed.TotalSeconds:F0}s ({games} games, {pool.Count} meta decks)");
        return 0;
    }

    case "enumtest":
    {
        var reg = BuildRegistry(out _);
        var sDef = reg.Resolve(reg.Ids.First()); var nDef = reg.Resolve(reg.Ids.Skip(1).First());
        var state = OnePieceTcg.Engine.GameEngine.CreateMatch(new OnePieceTcg.Engine.MatchConfig { SouthDeckDef = sDef, NorthDeckDef = nDef, Seed = "enum:1" });
        OnePieceTcg.Engine.GameEngine.ApplyCommand(state, new OnePieceTcg.Engine.GameCommand { Type = "chooseTurnOrder", Seat = state.CoinFlipWinner, GoingFirst = true });
        // advance one command at a time until an active-seat clean main-phase decision past turn 3
        var bl = new System.Collections.Generic.HashSet<string>();
        for (int i = 0; i < 4000 && state.Status != "finished"; i++)
        {
            bool clean = state.Phase == "main" && state.Battle == null && state.PendingEffects.Count == 0
                         && state.ActiveChoice == null && state.DeckLook == null && state.TurnNumber >= 4;
            if (clean)
            {
                var cands = OnePieceTcg.Sim.Search.LegalActions.Candidates(state, state.ActiveSeat);
                var legal = OnePieceTcg.Sim.Search.LegalActions.Validate(state, state.ActiveSeat, cands);
                if (legal.Count >= 3)
                {
                    Console.WriteLine($"Turn {state.TurnNumber} {state.ActiveSeat} main: {cands.Count} candidates -> {legal.Count} legal moves");
                    foreach (var (cmd, _) in legal.Take(12))
                        Console.WriteLine($"    {cmd.Type} {cmd.InstanceId}{cmd.Attacker}{(cmd.Target != null ? " -> " + cmd.Target : "")}{(cmd.Amount != null ? " x" + cmd.Amount : "")}");
                    return 0;
                }
            }
            var seat = state.ActiveSeat;
            var cmd2 = OnePieceTcg.Engine.Bot.IntermediateBot.DecideOneCommand(state, seat, bl)
                       ?? OnePieceTcg.Engine.Bot.IntermediateBot.DecideOneCommand(state, OnePieceTcg.Engine.GameEngine.OtherSeat(seat), bl);
            if (cmd2 == null) break;
            OnePieceTcg.Engine.GameEngine.ApplyCommand(state, cmd2);
        }
        Console.WriteLine("no clean decision reached");
        return 0;
    }

    case "clonetest":
    {
        var reg = BuildRegistry(out _);
        var south = reg.Resolve(reg.Ids.First()); var north = reg.Resolve(reg.Ids.Skip(1).First());
        int ok = 0, fail = 0;
        for (int t = 0; t < 20; t++)
        {
            var state = OnePieceTcg.Engine.GameEngine.CreateMatch(new OnePieceTcg.Engine.MatchConfig { SouthDeckDef = south, NorthDeckDef = north, Seed = $"clone:{t}" });
            OnePieceTcg.Engine.GameEngine.ApplyCommand(state, new OnePieceTcg.Engine.GameCommand { Type = "chooseTurnOrder", Seat = state.CoinFlipWinner, GoingFirst = true });
            OnePieceTcg.Engine.Bot.IntermediateBot.PlayFullMatch(state, 40 + t * 7); // stop mid-game
            if (state.Status == "finished") continue;
            var clone = OnePieceTcg.Sim.Search.GameClone.Clone(state);
            int histBefore = state.CommandHistory.Count;
            // independence: advance the clone, original must be untouched
            OnePieceTcg.Engine.Bot.IntermediateBot.PlayFullMatch(clone, 5);
            bool independent = state.CommandHistory.Count == histBefore;
            // fidelity: play both to completion; identical outcome
            OnePieceTcg.Engine.Bot.IntermediateBot.PlayFullMatch(state, 20000);
            OnePieceTcg.Sim.Search.GameClone.Clone(state); // touch to ensure no exceptions on finished states too
            var freshClone = OnePieceTcg.Sim.Search.GameClone.Clone(GameStateAt(south, north, $"clone:{t}", 40 + t * 7));
            OnePieceTcg.Engine.Bot.IntermediateBot.PlayFullMatch(freshClone, 20000);
            bool sameOutcome = freshClone.Status == state.Status && freshClone.TurnNumber == state.TurnNumber;
            if (independent && sameOutcome) ok++; else { fail++; Console.WriteLine($"  t={t} independent={independent} sameOutcome={sameOutcome} (orig turn={state.TurnNumber} clone turn={freshClone.TurnNumber})"); }
        }
        Console.WriteLine($"clone test: {ok} ok, {fail} fail");
        return fail == 0 ? 0 : 1;

        static OnePieceTcg.Engine.GameState GameStateAt(OnePieceTcg.Engine.DeckDef s, OnePieceTcg.Engine.DeckDef n, string seed, int cap)
        {
            var st = OnePieceTcg.Engine.GameEngine.CreateMatch(new OnePieceTcg.Engine.MatchConfig { SouthDeckDef = s, NorthDeckDef = n, Seed = seed });
            OnePieceTcg.Engine.GameEngine.ApplyCommand(st, new OnePieceTcg.Engine.GameCommand { Type = "chooseTurnOrder", Seat = st.CoinFlipWinner, GoingFirst = true });
            OnePieceTcg.Engine.Bot.IntermediateBot.PlayFullMatch(st, cap);
            return st;
        }
    }

    case "strategy-fixture-test":
        return OnePieceTcg.Sim.Planning.StrategyFixtureTest.Run(BuildRegistry(out _));

    case "effect-coverage-audit":
        // DB-wide operability sweep: printed effect text vs what the engine can resolve.
        return OnePieceTcg.Sim.Planning.EffectCoverageAudit.Run(args.Length > 1 ? args[1] : null);

    case "condition-audit":
        // Aura/condition blind-spot sweep: printed conditions EvaluateCondition can't parse (fail-closed).
        return OnePieceTcg.Sim.Planning.ConditionAudit.Run(args.Length > 1 ? args[1] : null);

    case "privacy-test":
    {
        var reg = BuildRegistry(out _);
        var south = reg.Resolve(reg.Ids.First()); var north = reg.Resolve(reg.Ids.Skip(1).First());
        int trials = 8;
        for (int i = 1; i < args.Length - 1; i++)
            if (args[i] == "--trials" && int.TryParse(args[i + 1], out var tn)) trials = tn;
        return OnePieceTcg.Sim.Search.PrivacyTest.Run(south, north, trials);
    }

    case "observed-seam-test":
    {
        var reg = BuildRegistry(out _);
        var south = reg.Resolve(reg.Ids.First()); var north = reg.Resolve(reg.Ids.Skip(1).First());
        return OnePieceTcg.Sim.Runner.ObservedSeamTest.Run(south, north);
    }

    case "boundary-test":
    {
        var reg = BuildRegistry(out _);
        var pool = reg.Ids.Select(reg.Resolve).ToList();
        return OnePieceTcg.Sim.Runner.BoundaryFixtureTest.Run(pool);
    }

    case "determinizer-test":
    {
        var reg = BuildRegistry(out _);
        var south = reg.Resolve(reg.Ids.First()); var north = reg.Resolve(reg.Ids.Skip(1).First());
        return OnePieceTcg.Sim.Runner.DeterminizerTest.Run(south, north);
    }

    case "ledger-test":
        return OnePieceTcg.Sim.Runner.LedgerTest.Run();

    case "nested-decklook-test":
        BuildRegistry(out _);
        return OnePieceTcg.Sim.Runner.NestedDeckLookTest.Run();

    case "kworld-test":
        return OnePieceTcg.Sim.Runner.KWorldTest.Run();

    case "honest-play":
    {
        // Honest (K=1 determinized) planner vs the held-out IntermediateBot baseline. A FIRST honest number,
        // not a trustworthy measurement (small n, no pairing/CI, K=1, pre-ledger).
        int n = args.Length > 1 && int.TryParse(args[1], out var hn) ? hn : 20;
        int kWorlds = args.Length > 2 && int.TryParse(args[2], out var kw) ? kw : 1;   // honest-play [n] [k] [nodeBudget] [arch]
        int nodeBudget = args.Length > 3 && int.TryParse(args[3], out var nb) ? nb : 600;
        if (args.Contains("arch")) OnePieceTcg.Sim.Planning.ValueFunction.ArchetypeWeights = true;   // bend eval per deck archetype
        var reg = BuildRegistry(out _);
        var pool = reg.Ids.Where(i => !OnePieceTcg.Engine.CardData.StarterDecks.ContainsKey(i))
            .Where(id => { var d = reg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; })
            .OrderBy(x => x).ToList();
        // Replan-every-command determinizes + searches per command × K worlds; NodeBudget is the search knee lever.
        var opt = new OnePieceTcg.Sim.Planning.TurnPlanner.Options { BeamWidth = 8, MaxDepth = 12, NodeBudget = nodeBudget, WorkBudget = System.Math.Max(30000, nodeBudget * 50) };
        var rng = new System.Random(7);
        int decided = 0, resultWins = 0, outright = 0, outrightWins = 0, stuck = 0, threw = 0; var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < n; i++)
        {
            string dS = pool[rng.Next(pool.Count)], dR = pool[rng.Next(pool.Count)];
            var sDef = reg.Resolve(dS); var nDef = reg.Resolve(dR);
            string first = i % 2 == 0 ? "south" : "north";
            var honest = new OnePieceTcg.Sim.Planning.HonestPlannerBot(sDef, nDef,
                ctx: OnePieceTcg.Sim.Planning.DeckFingerprint.Analyze(sDef), goFirst: first == "south", opt: opt, kWorlds: kWorlds);
            var baseline = new OnePieceTcg.Sim.BaselineAgent();
            try
            {
                var st = OnePieceTcg.Sim.Planning.GameRunner.Play(honest, baseline, sDef, nDef, $"honest:{i}", first, 20000, 60);
                if (OnePieceTcg.Sim.Planning.GameRunner.Stuck) { stuck++; System.Console.WriteLine($"  game {i + 1}/{n}: STUCK ({sw.Elapsed.TotalSeconds:F0}s)"); continue; }
                decided++;
                bool outrightGame = st.Status == "finished" && OnePieceTcg.Sim.Planning.GameRunner.Winner(st) != null;
                if (outrightGame) { outright++; if (OnePieceTcg.Sim.Planning.GameRunner.Winner(st) == "south") outrightWins++; }
                if (OnePieceTcg.Sim.Planning.GameRunner.Result(st) == "south") resultWins++;
                System.Console.WriteLine($"  game {i + 1}/{n}: {OnePieceTcg.Sim.Planning.GameRunner.Result(st) ?? "tie"}{(outrightGame ? "" : " (adjudicated)")} ({sw.Elapsed.TotalSeconds:F0}s)");
            }
            catch (System.Exception e)
            {
                threw++;
                System.Console.WriteLine($"  game {i + 1}/{n}: DETERMINIZE THREW ({dS} vs {dR}): {e.Message}");
            }
        }
        // Wilson 95% score interval on the result win rate — small n has a WIDE interval, printed so it can't
        // be over-read as "strength".
        double p = decided > 0 ? (double)resultWins / decided : 0;
        double z = 1.96, z2 = z * z, N = System.Math.Max(1, decided);
        double denom = 1 + z2 / N, center = (p + z2 / (2 * N)) / denom;
        double half = z * System.Math.Sqrt(p * (1 - p) / N + z2 / (4 * N * N)) / denom;
        System.Console.WriteLine();
        System.Console.WriteLine($"HONEST (K={kWorlds}) vs IntermediateBot: {resultWins}/{decided} = {100 * p:F0}% (95% CI [{100 * (center - half):F0}%, {100 * (center + half):F0}%])");
        System.Console.WriteLine($"  outright-only: {outrightWins}/{outright}; adjudicated: {resultWins - outrightWins}/{decided - outright}; stuck: {stuck}; determinize-threw: {threw}; {n} games, {sw.Elapsed.TotalSeconds:F0}s");
        System.Console.WriteLine($"⚠ NOT a trustworthy measurement: unpaired, K={kWorlds}, modest budget. Trustworthy = paired (deck+turn-order swap), n≥300, honest-vs-perfect-info for the contamination delta.");
        return 0;
    }

    case "honest-mutate":
    {
        // Small, paired, archetype-specific EOD pilot. Usage:
        // honest-mutate [trainPairs] [holdoutPairs] [nodeBudget] [archetype] [dop] [seedOffset] [mirror|field]
        int trainPairs = args.Length > 1 && int.TryParse(args[1], out var mt) ? mt : 6;
        int holdoutPairs = args.Length > 2 && int.TryParse(args[2], out var mh) ? mh : 10;
        int mutationBudget = args.Length > 3 && int.TryParse(args[3], out var mb) ? mb : 250;
        string mutationArch = args.Length > 4 ? args[4] : "aggro";
        int mutationDop = args.Length > 5 && int.TryParse(args[5], out var md) ? md : 4;
        int mutationSeedOffset = args.Length > 6 && int.TryParse(args[6], out var ms) ? ms : 0;
        bool mutationMirror = args.Length <= 7 || !string.Equals(args[7], "field", System.StringComparison.OrdinalIgnoreCase);
        return OnePieceTcg.Sim.Planning.HonestMutationPilot.Run(
            BuildRegistry(out _), trainPairs, holdoutPairs, mutationBudget, mutationArch, mutationDop,
            mutationSeedOffset, mutationMirror);
    }

    case "honest-policy-check":
    {
        // Direct paired search-vs-policy A/B. Usage:
        // honest-policy-check [pairs] [nodeBudget] [archetype] [dop] [seedOffset] [mirror|field]
        int policyPairs = args.Length > 1 && int.TryParse(args[1], out var pp) ? pp : 10;
        int policyBudget = args.Length > 2 && int.TryParse(args[2], out var pb) ? pb : 100;
        string policyArch = args.Length > 3 ? args[3] : "aggro";
        int policyDop = args.Length > 4 && int.TryParse(args[4], out var pd) ? pd : 4;
        int policySeedOffset = args.Length > 5 && int.TryParse(args[5], out var ps) ? ps : 0;
        bool policyMirror = args.Length > 6 && string.Equals(args[6], "mirror", System.StringComparison.OrdinalIgnoreCase);
        return OnePieceTcg.Sim.Planning.HonestMutationPilot.RunPolicyCheck(
            BuildRegistry(out _), policyPairs, policyBudget, policyArch, policyDop, policySeedOffset,
            policyMirror);
    }

    case "expert-sync":
    {
        // Download the rolling public OPBounty feed plus the stable high-bounty bootstrap replays,
        // aggregate observable expert actions, and match their card pools to validated local meta lists.
        // expert-sync [minimumBounty] [outputDir]
        double expertBounty = args.Length > 1 && double.TryParse(args[1], out var eb) ? eb : 1000;
        string expertOutput = args.Length > 2 ? args[2] : OnePieceTcg.Sim.Expert.ExpertReplayCorpus.DefaultOutputDir;
        return OnePieceTcg.Sim.Expert.ExpertReplayCorpus.Sync(BuildRegistry(out _), expertBounty, expertOutput);
    }

    case "honest-expert-check":
    {
        // Paired contract-v2 vs replay-learned policy on replay-matched high-bounty deck proxies.
        // honest-expert-check [pairs] [dop] [seedOffset] [corpusDir]
        int expertPairs = args.Length > 1 && int.TryParse(args[1], out var ep) ? ep : 30;
        int expertDop = args.Length > 2 && int.TryParse(args[2], out var ed) ? ed : 6;
        int expertSeed = args.Length > 3 && int.TryParse(args[3], out var es) ? es : 0;
        string expertDir = args.Length > 4 ? args[4] : OnePieceTcg.Sim.Expert.ExpertReplayCorpus.DefaultOutputDir;
        return OnePieceTcg.Sim.Planning.HonestMutationPilot.RunExpertCheck(
            BuildRegistry(out _), expertPairs, expertDop, expertSeed, expertDir);
    }

    case "honest-expert-gate":
    {
        // Fresh-seed single-arm confirmation of the retained policy's 55% high-bounty-suite target.
        // honest-expert-gate [pairs] [dop] [seedOffset] [corpusDir]
        int gatePairs = args.Length > 1 && int.TryParse(args[1], out var gp) ? gp : 30;
        int gateDop = args.Length > 2 && int.TryParse(args[2], out var gd) ? gd : 6;
        int gateSeed = args.Length > 3 && int.TryParse(args[3], out var gs) ? gs : 900000;
        string gateDir = args.Length > 4 ? args[4] : OnePieceTcg.Sim.Expert.ExpertReplayCorpus.DefaultOutputDir;
        return OnePieceTcg.Sim.Planning.HonestMutationPilot.RunExpertGate(
            BuildRegistry(out _), gatePairs, gateDop, gateSeed, gateDir);
    }

    case "honest-matchup-check":
    {
        // Exact A->B and B->A matchup, with the honest Advanced candidate in both seats per seed.
        // honest-matchup-check <deckA> <deckB> [pairs] [dop] [seedOffset] [scope] [opponent]
        if (args.Length < 3)
        {
            Console.WriteLine("usage: honest-matchup-check <deckA> <deckB> [pairs] [dop] [seedOffset] [scope] [opponent]");
            return 1;
        }
        int matchupPairs = args.Length > 3 && int.TryParse(args[3], out var mp) ? mp : 20;
        int matchupDop = args.Length > 4 && int.TryParse(args[4], out var mdp) ? mdp : 8;
        int matchupSeed = args.Length > 5 && int.TryParse(args[5], out var mso) ? mso : 0;
        string matchupScope = args.Length > 6 ? args[6].ToLowerInvariant() : "contract-v2";
        string matchupOpponent = args.Length > 7 ? args[7].ToLowerInvariant() : "champion";
        return OnePieceTcg.Sim.Planning.HonestMutationPilot.RunMatchupCheck(
            BuildRegistry(out _), args[1], args[2], matchupPairs, matchupDop, matchupSeed,
            matchupScope, matchupOpponent);
    }

    case "honest-advanced-selfplay":
    {
        // Same honest Advanced architecture on both sides; each deck goes first once per pair.
        // honest-advanced-selfplay <deckA> <deckB> [pairs] [dop] [seedOffset] [scopeA] [scopeB]
        if (args.Length < 3)
        {
            Console.WriteLine("usage: honest-advanced-selfplay <deckA> <deckB> [pairs] [dop] [seedOffset] [scopeA] [scopeB]");
            return 1;
        }
        int selfPairs = args.Length > 3 && int.TryParse(args[3], out var sp) ? sp : 30;
        int selfDop = args.Length > 4 && int.TryParse(args[4], out var sd) ? sd : 8;
        int selfSeed = args.Length > 5 && int.TryParse(args[5], out var ss) ? ss : 0;
        string selfScopeA = args.Length > 6 ? args[6].ToLowerInvariant() : "contract-v2";
        string selfScopeB = args.Length > 7 ? args[7].ToLowerInvariant() : selfScopeA;
        return OnePieceTcg.Sim.Planning.HonestMutationPilot.RunAdvancedSelfPlay(
            BuildRegistry(out _), args[1], args[2], selfPairs, selfDop, selfSeed, selfScopeA, selfScopeB);
    }

    case "trigger-policy-ab":
    {
        // Paired A/B of the dedicated battle-Trigger evaluator vs the generic rollout, on identical seeds.
        // trigger-policy-ab <deckA> <deckB> [pairs] [dop] [seedOffset]
        if (args.Length < 3)
        {
            Console.WriteLine("usage: trigger-policy-ab <deckA> <deckB> [pairs] [dop] [seedOffset]");
            return 1;
        }
        int abPairs = args.Length > 3 && int.TryParse(args[3], out var abp) ? abp : 20;
        int abDop = args.Length > 4 && int.TryParse(args[4], out var abd) ? abd : 8;
        int abSeed = args.Length > 5 && int.TryParse(args[5], out var abs) ? abs : 0;
        return OnePieceTcg.Sim.Planning.HonestMutationPilot.RunTriggerPolicyAB(
            BuildRegistry(out _), args[1], args[2], abPairs, abDop, abSeed);
    }

    case "removal-model-ab":
    {
        // Paired A/B of the WS-3 removal-capability targeting model vs the generic ranking, identical seeds.
        // removal-model-ab <deckA> <deckB> [pairs] [dop] [seedOffset]
        if (args.Length < 3)
        {
            Console.WriteLine("usage: removal-model-ab <deckA> <deckB> [pairs] [dop] [seedOffset]");
            return 1;
        }
        int rmPairs = args.Length > 3 && int.TryParse(args[3], out var rmp) ? rmp : 20;
        int rmDop = args.Length > 4 && int.TryParse(args[4], out var rmd) ? rmd : 8;
        int rmSeed = args.Length > 5 && int.TryParse(args[5], out var rms) ? rms : 0;
        return OnePieceTcg.Sim.Planning.HonestMutationPilot.RunRemovalModelAB(
            BuildRegistry(out _), args[1], args[2], rmPairs, rmDop, rmSeed);
    }

    case "enel-diagnostic":
    {
        // WS-1 play-trace: what does the advanced bot actually DO piloting a DON-engine deck?
        // enel-diagnostic [engineDeck] [oppDeck] [games] [seedOffset]
        string edEngine = args.Length > 1 ? args[1] : "op16-p-enel";
        string edOpp = args.Length > 2 ? args[2] : "op16-blue-lucy";
        int edGames = args.Length > 3 && int.TryParse(args[3], out var edg) ? edg : 6;
        int edSeed = args.Length > 4 && int.TryParse(args[4], out var eds) ? eds : 0;
        return OnePieceTcg.Sim.Planning.HonestMutationPilot.RunEnelDiagnostic(
            BuildRegistry(out _), edEngine, edOpp, edGames, edSeed);
    }

    case "trash-ab":
    {
        // Paired A/B of the trash-recursion model, identical seeds. (deckA should be the recursion deck.)
        // trash-ab <deckA> <deckB> [pairs] [dop] [seedOffset]
        if (args.Length < 3)
        {
            Console.WriteLine("usage: trash-ab <deckA> <deckB> [pairs] [dop] [seedOffset]");
            return 1;
        }
        int trP = args.Length > 3 && int.TryParse(args[3], out var trp) ? trp : 20;
        int trD = args.Length > 4 && int.TryParse(args[4], out var trd) ? trd : 8;
        int trS = args.Length > 5 && int.TryParse(args[5], out var trs) ? trs : 0;
        return OnePieceTcg.Sim.Planning.HonestMutationPilot.RunTrashAB(
            BuildRegistry(out _), args[1], args[2], trP, trD, trS);
    }

    case "threat-model-ab":
    {
        // Paired A/B of the effect-threat target-value model, identical seeds.
        // threat-model-ab <deckA> <deckB> [pairs] [dop] [seedOffset]
        if (args.Length < 3)
        {
            Console.WriteLine("usage: threat-model-ab <deckA> <deckB> [pairs] [dop] [seedOffset]");
            return 1;
        }
        int tmP = args.Length > 3 && int.TryParse(args[3], out var tmp) ? tmp : 20;
        int tmD = args.Length > 4 && int.TryParse(args[4], out var tmd) ? tmd : 8;
        int tmS = args.Length > 5 && int.TryParse(args[5], out var tms) ? tms : 0;
        return OnePieceTcg.Sim.Planning.HonestMutationPilot.RunThreatModelAB(
            BuildRegistry(out _), args[1], args[2], tmP, tmD, tmS);
    }

    case "sac-recur-ab":
    {
        // Paired A/B of the sacrifice-to-recur unlock (trash a small body to deploy a bigger one from trash).
        // sac-recur-ab <deckA> <deckB> [pairs] [dop] [seedOffset]   (deckA should be the recursion deck)
        if (args.Length < 3)
        {
            Console.WriteLine("usage: sac-recur-ab <deckA> <deckB> [pairs] [dop] [seedOffset]");
            return 1;
        }
        int srP = args.Length > 3 && int.TryParse(args[3], out var srp) ? srp : 20;
        int srD = args.Length > 4 && int.TryParse(args[4], out var srd) ? srd : 8;
        int srS = args.Length > 5 && int.TryParse(args[5], out var srs) ? srs : 0;
        return OnePieceTcg.Sim.Planning.HonestMutationPilot.RunSacRecurAB(
            BuildRegistry(out _), args[1], args[2], srP, srD, srS);
    }

    case "restand-ab":
    {
        // Paired A/B of the restand engine (Zoro-style multi-attack), identical seeds.
        // restand-ab <deckA> <deckB> [pairs] [dop] [seedOffset]   (deckA should be the restand deck)
        if (args.Length < 3)
        {
            Console.WriteLine("usage: restand-ab <deckA> <deckB> [pairs] [dop] [seedOffset]");
            return 1;
        }
        int rsP = args.Length > 3 && int.TryParse(args[3], out var rsp) ? rsp : 20;
        int rsD = args.Length > 4 && int.TryParse(args[4], out var rsd) ? rsd : 8;
        int rsS = args.Length > 5 && int.TryParse(args[5], out var rss) ? rss : 0;
        return OnePieceTcg.Sim.Planning.HonestMutationPilot.RunRestandAB(
            BuildRegistry(out _), args[1], args[2], rsP, rsD, rsS);
    }

    case "lethal-pivot-ab":
    {
        // Paired A/B of the lethal/desperation pivot (all-in when dead next turn), identical seeds.
        // lethal-pivot-ab <deckA> <deckB> [pairs] [dop] [seedOffset]
        if (args.Length < 3)
        {
            Console.WriteLine("usage: lethal-pivot-ab <deckA> <deckB> [pairs] [dop] [seedOffset]");
            return 1;
        }
        int lvP = args.Length > 3 && int.TryParse(args[3], out var lvp) ? lvp : 20;
        int lvD = args.Length > 4 && int.TryParse(args[4], out var lvd) ? lvd : 8;
        int lvS = args.Length > 5 && int.TryParse(args[5], out var lvs) ? lvs : 0;
        return OnePieceTcg.Sim.Planning.HonestMutationPilot.RunLethalPivotAB(
            BuildRegistry(out _), args[1], args[2], lvP, lvD, lvS);
    }

    case "leader-setup-ab":
    {
        // Paired A/B of the proactive [DON!! xN] leader-engine setup, identical seeds.
        // leader-setup-ab <deckA> <deckB> [pairs] [dop] [seedOffset]   (deckA should have the [DON!! xN] leader)
        if (args.Length < 3)
        {
            Console.WriteLine("usage: leader-setup-ab <deckA> <deckB> [pairs] [dop] [seedOffset]");
            return 1;
        }
        int lsP = args.Length > 3 && int.TryParse(args[3], out var lsp) ? lsp : 20;
        int lsD = args.Length > 4 && int.TryParse(args[4], out var lsd) ? lsd : 8;
        int lsS = args.Length > 5 && int.TryParse(args[5], out var lss) ? lss : 0;
        return OnePieceTcg.Sim.Planning.HonestMutationPilot.RunLeaderSetupAB(
            BuildRegistry(out _), args[1], args[2], lsP, lsD, lsS);
    }

    case "opportunity-ab":
    {
        // Paired A/B of the WS-1 opportunity-cost hold model (use-Main vs hold-for-Counter), identical seeds.
        // opportunity-ab <deckA> <deckB> [pairs] [dop] [seedOffset]   (deckA should be the DON-engine deck)
        if (args.Length < 3)
        {
            Console.WriteLine("usage: opportunity-ab <deckA> <deckB> [pairs] [dop] [seedOffset]");
            return 1;
        }
        int opP = args.Length > 3 && int.TryParse(args[3], out var opp) ? opp : 20;
        int opD = args.Length > 4 && int.TryParse(args[4], out var opd) ? opd : 8;
        int opS = args.Length > 5 && int.TryParse(args[5], out var ops) ? ops : 0;
        return OnePieceTcg.Sim.Planning.HonestMutationPilot.RunOpportunityAB(
            BuildRegistry(out _), args[1], args[2], opP, opD, opS);
    }

    case "cost-combo-ab":
    {
        // Paired A/B of the WS-2 -cost->KO-by-cost targeting vs generic, identical seeds.
        // cost-combo-ab <deckA> <deckB> [pairs] [dop] [seedOffset]   (deckA should be the cost-down deck)
        if (args.Length < 3)
        {
            Console.WriteLine("usage: cost-combo-ab <deckA> <deckB> [pairs] [dop] [seedOffset]");
            return 1;
        }
        int ccP = args.Length > 3 && int.TryParse(args[3], out var ccp) ? ccp : 20;
        int ccD = args.Length > 4 && int.TryParse(args[4], out var ccd) ? ccd : 8;
        int ccS = args.Length > 5 && int.TryParse(args[5], out var ccs) ? ccs : 0;
        return OnePieceTcg.Sim.Planning.HonestMutationPilot.RunCostComboAB(
            BuildRegistry(out _), args[1], args[2], ccP, ccD, ccS);
    }

    case "don-engine-ab":
    {
        // Paired A/B of the WS-1 DON-engine scoring vs generic DON scoring, identical seeds.
        // don-engine-ab <deckA> <deckB> [pairs] [dop] [seedOffset]   (deckA should be the DON-engine deck)
        if (args.Length < 3)
        {
            Console.WriteLine("usage: don-engine-ab <deckA> <deckB> [pairs] [dop] [seedOffset]");
            return 1;
        }
        int deP = args.Length > 3 && int.TryParse(args[3], out var dep) ? dep : 20;
        int deD = args.Length > 4 && int.TryParse(args[4], out var ded) ? ded : 8;
        int deS = args.Length > 5 && int.TryParse(args[5], out var des) ? des : 0;
        return OnePieceTcg.Sim.Planning.HonestMutationPilot.RunDonEngineAB(
            BuildRegistry(out _), args[1], args[2], deP, deD, deS);
    }

    case "honest-advanced-check":
    {
        // Rollout-improved honest policy vs the proven greedy floor. Usage:
        // honest-advanced-check [pairs] [archetype] [dop] [seedOffset] [mirror|field]
        //                       [all|resolution|defense|pressure|pressure-resolution|contract-v1|contract-v2]
        //                       [baseline|champion|aggro|conservative]
        int advancedPairs = args.Length > 1 && int.TryParse(args[1], out var ap) ? ap : 6;
        string advancedArch = args.Length > 2 ? args[2] : "all";
        int advancedDop = args.Length > 3 && int.TryParse(args[3], out var ad) ? ad : 4;
        int advancedSeedOffset = args.Length > 4 && int.TryParse(args[4], out var ass) ? ass : 0;
        bool advancedMirror = args.Length > 5 && string.Equals(args[5], "mirror", System.StringComparison.OrdinalIgnoreCase);
        string advancedScope = args.Length > 6 ? args[6].ToLowerInvariant() : "all";
        string advancedOpponent = args.Length > 7 ? args[7].ToLowerInvariant() : "baseline";
        return OnePieceTcg.Sim.Planning.HonestMutationPilot.RunAdvancedCheck(
            BuildRegistry(out _), advancedPairs, advancedArch, advancedDop, advancedSeedOffset,
            advancedMirror, advancedScope, advancedOpponent);
    }

    case "honest-advanced-invalid-replay":
    {
        // Diagnostic-only replay of the advanced arm; stops after the first invalid.
        // honest-advanced-invalid-replay [pairs] [dop] [seedOffset] [scope]
        int replayPairs = args.Length > 1 && int.TryParse(args[1], out var rp) ? rp : 150;
        int replayDop = args.Length > 2 && int.TryParse(args[2], out var rd) ? rd : 8;
        int replaySeed = args.Length > 3 && int.TryParse(args[3], out var rs) ? rs : 1180000;
        string replayScope = args.Length > 4 ? args[4].ToLowerInvariant() : "contract-v2";
        return OnePieceTcg.Sim.Planning.HonestMutationPilot.RunAdvancedInvalidReplay(
            BuildRegistry(out _), replayPairs, replayDop, replaySeed, replayScope);
    }

    case "honest-advanced-replay-one":
    {
        // honest-advanced-replay-one [scenarioIndex] [seedOffset] [scope] [south|north]
        int oneIndex = args.Length > 1 && int.TryParse(args[1], out var oi) ? oi : 96;
        int oneSeed = args.Length > 2 && int.TryParse(args[2], out var os) ? os : 1180000;
        string oneScope = args.Length > 3 ? args[3].ToLowerInvariant() : "contract-v2";
        bool oneSouth = args.Length <= 4 || !string.Equals(args[4], "north", System.StringComparison.OrdinalIgnoreCase);
        return OnePieceTcg.Sim.Planning.HonestMutationPilot.RunAdvancedReplayOne(
            BuildRegistry(out _), oneIndex, oneSeed, oneScope, oneSouth);
    }

    case "contamination":
    {
        // THE CONTAMINATION DELTA (paired): perfect-info PlannerBot vs baseline AND honest HonestPlannerBot vs
        // baseline on the SAME deck pair + seed + turn order. delta = perfect-info winrate − honest winrate is
        // how much the illegal perfect information inflated the bot. First paired signal, CI'd; NOT the final
        // n≥300 experiment.
        int n = args.Length > 1 && int.TryParse(args[1], out var cn) ? cn : 12;
        int cseed = args.Length > 2 && int.TryParse(args[2], out var cs) ? cs : 11;   // contamination [n] [seed] — vary seed for independent samples to pool
        var reg = BuildRegistry(out _);
        var pool = reg.Ids.Where(i => !OnePieceTcg.Engine.CardData.StarterDecks.ContainsKey(i))
            .Where(id => { var d = reg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; })
            .OrderBy(x => x).ToList();
        var opt = new OnePieceTcg.Sim.Planning.TurnPlanner.Options { BeamWidth = 6, MaxDepth = 10, NodeBudget = 600, WorkBudget = 30000 };
        var rng = new System.Random(cseed);
        int piDecided = 0, piWins = 0, hoDecided = 0, hoWins = 0, threw = 0; var sw = System.Diagnostics.Stopwatch.StartNew();
        string piRun = OnePieceTcg.Sim.ProgressBoard.Start("perfect", $"perfect-info planner (contamination) | n={n} | seed={cseed}", n);
        string hoRun = OnePieceTcg.Sim.ProgressBoard.Start("honest", $"honest planner (contamination) | n={n} | seed={cseed}", n);
        System.Console.WriteLine($"progress dashboard: {OnePieceTcg.Sim.ProgressBoard.DashboardPath}");
        System.Console.WriteLine("  open that file in a browser to watch the bars fill live.");
        for (int i = 0; i < n; i++)
        {
            string dS = pool[rng.Next(pool.Count)], dR = pool[rng.Next(pool.Count)];
            var sDef = reg.Resolve(dS); var nDef = reg.Resolve(dR);
            string first = i % 2 == 0 ? "south" : "north"; string seed = $"contam:{cseed}:{i}";
            var ctx = OnePieceTcg.Sim.Planning.DeckFingerprint.Analyze(sDef);
            var baseP = new OnePieceTcg.Sim.BaselineAgent();
            var pi = new OnePieceTcg.Sim.Planning.PlannerBot(ctx: ctx, opt: opt, goFirst: first == "south");
            var piSt = OnePieceTcg.Sim.Planning.GameRunner.Play(pi, baseP, sDef, nDef, seed, first, 20000, 60);
            if (OnePieceTcg.Sim.Planning.GameRunner.Stuck) { OnePieceTcg.Sim.ProgressBoard.Tick(piRun, -1); }
            else { piDecided++; bool w = OnePieceTcg.Sim.Planning.GameRunner.Result(piSt) == "south"; if (w) piWins++; OnePieceTcg.Sim.ProgressBoard.Tick(piRun, w ? 1 : 0); }
            try
            {
                var baseH = new OnePieceTcg.Sim.BaselineAgent();
                var ho = new OnePieceTcg.Sim.Planning.HonestPlannerBot(sDef, nDef, ctx: ctx, opt: opt, goFirst: first == "south");
                var hoSt = OnePieceTcg.Sim.Planning.GameRunner.Play(ho, baseH, sDef, nDef, seed, first, 20000, 60);
                if (OnePieceTcg.Sim.Planning.GameRunner.Stuck) { OnePieceTcg.Sim.ProgressBoard.Tick(hoRun, -1); }
                else { hoDecided++; bool w = OnePieceTcg.Sim.Planning.GameRunner.Result(hoSt) == "south"; if (w) hoWins++; OnePieceTcg.Sim.ProgressBoard.Tick(hoRun, w ? 1 : 0); }
            }
            catch (System.Exception e) { threw++; OnePieceTcg.Sim.ProgressBoard.Tick(hoRun, -1); System.Console.WriteLine($"  game {i + 1}: honest threw: {e.Message}"); }
            System.Console.WriteLine($"  game {i + 1}/{n}: perfect={(OnePieceTcg.Sim.Planning.GameRunner.Result(piSt) ?? "tie")} ({sw.Elapsed.TotalSeconds:F0}s)");
        }
        OnePieceTcg.Sim.ProgressBoard.Finish(piRun); OnePieceTcg.Sim.ProgressBoard.Finish(hoRun);
        double piP = piDecided > 0 ? (double)piWins / piDecided : 0, hoP = hoDecided > 0 ? (double)hoWins / hoDecided : 0;
        System.Console.WriteLine();
        System.Console.WriteLine($"PERFECT-INFO vs baseline: {piWins}/{piDecided} = {100 * piP:F0}%");
        System.Console.WriteLine($"HONEST (K=1)  vs baseline: {hoWins}/{hoDecided} = {100 * hoP:F0}% ({threw} honest threw)");
        System.Console.WriteLine($"CONTAMINATION DELTA (perfect − honest): {100 * (piP - hoP):F0}pp — how much perfect info inflated the bot.");
        System.Console.WriteLine($"⚠ n={n}, unpaired-outcome, K=1 honest, modest budget — a FIRST signal, not the n≥300 verdict. {sw.Elapsed.TotalSeconds:F0}s");
        return 0;
    }

    case "evolve":
    {
        OnePieceTcg.Sim.Learning.EvolveConfig cfg = (args.Length > 1 && File.Exists(args[1]))
            ? LoadJson<OnePieceTcg.Sim.Learning.EvolveConfig>(args[1])
            : new OnePieceTcg.Sim.Learning.EvolveConfig();
        for (int i = 1; i < args.Length - 1; i++)
        {
            if (args[i] == "--gens" && int.TryParse(args[i + 1], out var gn)) cfg.generations = gn;
            if (args[i] == "--pop" && int.TryParse(args[i + 1], out var pp)) cfg.populationSize = pp;
            if (args[i] == "--rounds" && int.TryParse(args[i + 1], out var rd)) cfg.roundsPerGeneration = rd;
            if (args[i] == "--gpp" && int.TryParse(args[i + 1], out var gp)) cfg.gamesPerPairing = gp;
        }
        OnePieceTcg.Sim.Learning.Evolver.Run(cfg, BuildRegistry(out _));
        return 0;
    }

    case "policy-eval":
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: policy-eval <policy.json> [config.json]"); return 2; }
        PolicyEvalConfig cfg = (args.Length > 2 && File.Exists(args[2]))
            ? LoadJson<PolicyEvalConfig>(args[2])
            : new PolicyEvalConfig { decks = new List<string> { "st01", "st02", "st03" }, samplesPerMatchup = 40 };
        cfg.policyPath = args[1];
        PolicyEval.Run(cfg, BuildRegistry(out _));
        return 0;
    }

    case "heuristics-quick":
    {
        string family = args.Length > 1 ? args[1] : "turn-order";
        int samples = args.Length > 2 && int.TryParse(args[2], out var s) ? s : 60;
        var cfg = new HeuristicConfig
        {
            experimentId = $"heur-quick-{family}",
            family = family,
            decks = new List<string> { "st01", "st02", "st03", "st04" },
            samplesPerMatchup = samples,
            minSamples = 150,
        };
        HeuristicRunner.Run(cfg, BuildRegistry(out _));
        return 0;
    }

    default:
        Console.Error.WriteLine($"unknown mode '{mode}'. modes: smoke, deckcheck, run, heuristics, heuristics-quick");
        return 2;
}

static T LoadJson<T>(string path) where T : class
{
    if (!File.Exists(path)) { Console.Error.WriteLine($"config not found: {path}"); return null; }
    return JsonSerializer.Deserialize<T>(File.ReadAllText(path),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
}
