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
    case "keywordrulescheck":
        return OnePieceTcg.Sim.KeywordRulesTest.Run();

    case "lethaltest":
        return OnePieceTcg.Sim.Puzzles.LethalSolverTest.Run();

    case "puzzlesynth":
    {
        // Solution-first generation: compose multi-constraint positions, then try to disprove their quality by
        // solving every alternative at the first several attacker decisions. Only selective plans survive.
        int scan = args.Length > 1 ? int.Parse(args[1]) : 90;
        var passed = new List<(int seed, OnePieceTcg.Engine.Puzzles.PuzzleSynthesizer.Family family,
            OnePieceTcg.Engine.Puzzles.PuzzleQualityAnalyzer.Report quality)>();
        var reasons = new Dictionary<string, int>();
        for (int seed = 1; seed <= scan; seed++)
        {
            var candidate = OnePieceTcg.Engine.Puzzles.PuzzleSynthesizer.Build(seed);
            var proof = OnePieceTcg.Engine.Puzzles.LethalSolver.Solve(candidate.State, candidate.Attacker,
                new OnePieceTcg.Engine.Puzzles.LethalSolver.Options { WorkBudget = 180_000, MaxDepth = 60 });
            var q = OnePieceTcg.Engine.Puzzles.PuzzleQualityAnalyzer.Analyze(
                candidate.State, candidate.Attacker, proof,
                OnePieceTcg.Engine.Puzzles.PuzzleQualityAnalyzer.StrictHarvest());
            if (q.Passed)
            {
                passed.Add((seed, candidate.Kind, q));
                Console.WriteLine($"  PASS seed={seed,-4} family={candidate.Kind,-16} critical={q.CriticalDecisions} " +
                                  $"first={q.WinningFirstMoves}/{q.LegalFirstMoves} attackOnly={q.AttackOnlyOutcome}");
            }
            else
            {
                string reason = q.Reason ?? "unknown";
                reasons[reason] = reasons.GetValueOrDefault(reason) + 1;
            }
        }
        Console.WriteLine($"puzzlesynth: {passed.Count}/{scan} candidates passed strict quality");
        foreach (var group in passed.GroupBy(x => x.family).OrderBy(g => g.Key))
            Console.WriteLine($"  {group.Key}: {group.Count()}  seeds=[{string.Join(", ", group.Select(x => x.seed).Take(20))}]");
        foreach (var reason in reasons.OrderByDescending(kv => kv.Value).Take(8))
            Console.WriteLine($"  reject {reason.Value,3}: {reason.Key}");
        return passed.Count > 0 ? 0 : 2;
    }

    case "puzzlequalityscan":
    {
        // Diagnostic for catalog work: measure how often the broad procedural generator produces genuinely
        // selective positions. This does not publish anything; it prints seeds that survive the exact quality
        // gate so a reviewed manifest can be baked separately.
        int scan = args.Length > 1 ? int.Parse(args[1]) : 200;
        int target = args.Length > 2 ? int.Parse(args[2]) : 25;
        int startSeed = args.Length > 3 ? int.Parse(args[3]) : 1;
        long solveBudget = args.Length > 4 ? long.Parse(args[4]) : 60_000;
        string familyFilter = args.Length > 5 ? args[5] : null;
        var accepted = new List<(int seed, string family, int critical, int legal, int winning)>();
        var rejected = new Dictionary<string, int>();
        for (int seed = startSeed; seed < startSeed + scan && accepted.Count < target; seed++)
        {
            var gp = OnePieceTcg.Engine.Puzzles.PuzzleGenerator.Build(seed);
            if (!string.IsNullOrEmpty(familyFilter)
                && !string.Equals(gp.Category, familyFilter, StringComparison.Ordinal))
                continue;
            var quality = OnePieceTcg.Engine.Puzzles.PuzzleQualityAnalyzer.Analyze(
                gp.State, gp.AttackerSeat, options: new OnePieceTcg.Engine.Puzzles.PuzzleQualityAnalyzer.Options
                {
                    SolveBudget = solveBudget,
                    MaxDecisionDepth = 5,
                    MinCriticalDecisions = 2,
                    MinAttackerMoves = 3,
                    MinLegalMovesAtRoot = 3,
                    MaxLegalMovesPerDecision = 64,
                });
            if (quality.Passed)
            {
                accepted.Add((seed, gp.Category, quality.CriticalDecisions, quality.LegalFirstMoves,
                    quality.WinningFirstMoves));
                Console.WriteLine($"PASS {seed,5} {gp.Category,-15} critical={quality.CriticalDecisions} " +
                                  $"first={quality.WinningFirstMoves}/{quality.LegalFirstMoves}");
                Console.Out.Flush(); // checkpoint redirected batch logs immediately
            }
            else
            {
                string reason = quality.Reason ?? "unknown";
                rejected[reason] = rejected.GetValueOrDefault(reason) + 1;
            }
            if ((seed - startSeed + 1) % 25 == 0)
            {
                Console.Error.WriteLine($"progress seed={seed} accepted={accepted.Count}");
                Console.Error.Flush();
            }
        }
        Console.WriteLine($"puzzlequalityscan: accepted {accepted.Count}/{scan} from seed {startSeed}");
        Console.WriteLine($"seeds=[{string.Join(", ", accepted.Select(x => x.seed))}]");
        foreach (var family in accepted.GroupBy(x => x.family).OrderBy(x => x.Key))
            Console.WriteLine($"  {family.Key}: {family.Count()}");
        foreach (var reason in rejected.OrderByDescending(x => x.Value).Take(8))
            Console.WriteLine($"  reject {reason.Value,4}: {reason.Key}");
        return accepted.Count >= target ? 0 : 2;
    }

    case "kuzantest":
    {
        // OP10-082 Kuzan sacrifice-recur guard: don't trash a live body when the trash has no valid recur target.
        var st = OnePieceTcg.Engine.GameEngine.CreateMatch(new OnePieceTcg.Engine.MatchConfig { SouthDeck = "st01", NorthDeck = "st01", Seed = "kuzan" });
        st.Status = "active"; st.Phase = "main"; st.ActiveSeat = "south"; st.TurnNumber = 6;
        var S = st.Players["south"];
        for (int i = 0; i < 5; i++) S.CharacterArea[i] = null;
        OnePieceTcg.Engine.CardInstance In(string id, string zone) => new OnePieceTcg.Engine.CardInstance
        { InstanceId = $"south-{id}-{System.Guid.NewGuid():N}".Substring(0, 20), CardId = id, Owner = "south", Zone = zone };
        var kuzan = In("OP10-082", "character"); S.CharacterArea[0] = kuzan;
        var kdef = OnePieceTcg.Engine.GameEngine.GetCard(kuzan);
        S.Trash.Clear();
        S.Trash.Add(In("OP10-082", "trash"));   // only another Kuzan -> excluded by "other than [Kuzan]"
        bool block = !OnePieceTcg.Engine.Bot.Search.AdvancedActivationPolicy.HasValidTrashRecurTarget(st, "south", kdef, kuzan);
        S.Trash.Add(In("OP09-089", "trash"));   // Stronger: Blackbeard, cost 1 -> a real recur target
        bool allow = OnePieceTcg.Engine.Bot.Search.AdvancedActivationPolicy.HasValidTrashRecurTarget(st, "south", kdef, kuzan);
        System.Console.WriteLine($"Kuzan recur guard: only-Kuzan -> block={block}  with-valid -> allow={allow}");
        return (block && allow) ? 0 : 1;
    }

    case "puzzlegen":
    {
        // Offline certifier: build procedural puzzles, keep only those the exact solver proves are forced-lethal,
        // spread across difficulty tiers, and emit the verified-seed manifest to paste into PuzzleLibrary.
        int scanTo = args.Length > 1 ? int.Parse(args[1]) : 60000;
        int[] targets = { 0, 25, 25, 30, 20 };   // by family difficulty 1..4 (total 100)
        var full = new OnePieceTcg.Engine.Puzzles.LethalSolver.Options { WorkBudget = 6_000, MaxDepth = 60 };
        // Pools keyed by (difficulty, family) so selection can vary families within each tier.
        var pool = new System.Collections.Generic.Dictionary<(int, string), List<int>>();
        const int poolCap = 44;
        var sigs = new HashSet<string>();
        int attempted = 0, lethal = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int[] tierMin = { 0, 22, 34, 40, 30 };   // pool at least this many per tier (buffer over targets) before stopping
        for (int seed = 1; seed <= scanTo; seed++)
        {
            bool enough = true;
            for (int d = 1; d <= 4; d++) if (pool.Where(kv => kv.Key.Item1 == d).Sum(kv => kv.Value.Count) < tierMin[d]) enough = false;
            if (enough && seed > 300) break;
            if (seed % 1000 == 0) Console.Error.WriteLine($"  [progress] seed {seed}: pooled {pool.Sum(kv => kv.Value.Count)}, D1234=[{string.Join(",", Enumerable.Range(1, 4).Select(d => pool.Where(kv => kv.Key.Item1 == d).Sum(kv => kv.Value.Count)))}], {sw.ElapsedMilliseconds}ms");
            attempted++;
            var gp = OnePieceTcg.Engine.Puzzles.PuzzleGenerator.Build(seed);
            int diff = Math.Clamp(gp.Difficulty, 1, 4);
            var key = (diff, gp.Category);
            if (pool.TryGetValue(key, out var lst0) && lst0.Count >= poolCap) continue;   // pool full — skip solve
            var res = OnePieceTcg.Engine.Puzzles.LethalSolver.Solve(gp.State, "south", full);
            if (res.Outcome != OnePieceTcg.Engine.Puzzles.LethalSolver.Lethal.Win || res.BudgetHit) continue;
            lethal++;
            int attacks = res.PrincipalVariation.Count(c => c.Type == "declareAttack" && (c.Seat == null || c.Seat == "south"));
            if (attacks < 2) continue;
            if (!sigs.Add(Signature(gp))) continue;
            if (!pool.TryGetValue(key, out var lst)) pool[key] = lst = new List<int>();
            lst.Add(seed);
        }
        sw.Stop();
        // VALIDATE at a higher budget: each must be forced-lethal AND swing-only must LOSE (truly requires a
        // non-attack action). Cache results so a seed is validated once.
        var vfull = new OnePieceTcg.Engine.Puzzles.LethalSolver.Options { WorkBudget = 80_000, MaxDepth = 60 };
            var vswing = new OnePieceTcg.Engine.Puzzles.LethalSolver.Options
            {
                WorkBudget = 80_000, MaxDepth = 60,
                // Keep mandatory [When Attacking]/choice resolution commands. The old declareAttack-only
                // filter mistook rich-leader alpha strikes for setup puzzles because it blocked their effects.
                AttackerFilter = OnePieceTcg.Engine.Puzzles.PuzzleQualityAnalyzer.IsAttackLineAction,
            };
        var valCache = new System.Collections.Generic.Dictionary<int, bool>();
        int badLethal = 0, badSwing = 0;
        bool Valid(int s)
        {
            if (valCache.TryGetValue(s, out var v)) return v;
            var gp = OnePieceTcg.Engine.Puzzles.PuzzleGenerator.Build(s);
            bool ok = true;
            if (OnePieceTcg.Engine.Puzzles.LethalSolver.Solve(gp.State, "south", vfull).Outcome != OnePieceTcg.Engine.Puzzles.LethalSolver.Lethal.Win) { badLethal++; ok = false; }
            else if (OnePieceTcg.Engine.Puzzles.LethalSolver.Solve(gp.State, "south", vswing).Outcome == OnePieceTcg.Engine.Puzzles.LethalSolver.Lethal.Win) { badSwing++; ok = false; }
            valCache[s] = ok; return ok;
        }
        // Select balanced 100 by difficulty; round-robin families within each tier for variety (validated only).
        var chosen = new List<int>[5]; for (int i = 0; i < 5; i++) chosen[i] = new List<int>();
        var picked = new HashSet<int>();
        for (int d = 1; d <= 4; d++)
        {
            var cats = pool.Where(kv => kv.Key.Item1 == d).Select(kv => kv.Key.Item2).Distinct().ToList();
            var idx = cats.ToDictionary(c => c, c => 0);
            while (chosen[d].Count < targets[d] && cats.Count > 0)
            {
                bool progressed = false;
                foreach (var cat in cats.ToList())
                {
                    if (chosen[d].Count >= targets[d]) break;
                    var lst = pool[(d, cat)];
                    while (idx[cat] < lst.Count)
                    {
                        int s = lst[idx[cat]++];
                        if (Valid(s)) { chosen[d].Add(s); picked.Add(s); progressed = true; break; }
                    }
                }
                if (!progressed) break;
            }
        }
        // Backfill to 100 from any remaining validated seed (any difficulty).
        var leftovers = pool.SelectMany(kv => kv.Value).Distinct().Where(s => !picked.Contains(s)).ToList();
        for (int d = 1; d <= 4 && chosen.Sum(c => c.Count) < 100; d++)
            foreach (var s in leftovers.Where(s => Math.Clamp(OnePieceTcg.Engine.Puzzles.PuzzleGenerator.Build(s).Difficulty, 1, 4) == d).ToList())
            {
                if (chosen.Sum(c => c.Count) >= 100) break;
                if (chosen[d].Count >= targets[d] + 6) continue;
                if (!picked.Contains(s) && Valid(s)) { chosen[d].Add(s); picked.Add(s); }
            }
        var all = new List<int>(); for (int d = 1; d <= 4; d++) all.AddRange(chosen[d]);
        var catCount = new System.Collections.Generic.Dictionary<string, int>();
        foreach (var s in all) { var c = OnePieceTcg.Engine.Puzzles.PuzzleGenerator.Build(s).Category; catCount[c] = catCount.GetValueOrDefault(c) + 1; }
        Console.WriteLine($"scanned {attempted}, lethal {lethal}, pooled {pool.Sum(kv => kv.Value.Count)}, selected {all.Count} in {sw.ElapsedMilliseconds} ms (dropped lethal={badLethal} swing={badSwing})");
        for (int d = 1; d <= 4; d++) Console.WriteLine($"  difficulty {d}: {chosen[d].Count}/{targets[d]}");
        Console.WriteLine("  families: " + string.Join(", ", catCount.Select(kv => $"{kv.Key}={kv.Value}")));
        Console.WriteLine("\n// ---- paste into PuzzleLibrary.VerifiedSeeds ----");
        for (int d = 1; d <= 4; d++)
            Console.WriteLine($"        /* D{d} */ {string.Join(", ", chosen[d])},");
        return all.Count >= 100 ? 0 : 2;
    }

    case "mate2test":
    {
        // Controlled "mate in 2": I can't win THIS turn (only my Leader can attack a 1-Life opponent = 1 hit,
        // not lethal), but I can play a finisher now (summoning-sick) and next turn swing Leader + finisher for
        // lethal. The opponent has no board, an empty hand, and a deck stacked with harmless counter-0 non-
        // Blockers, so nothing they draw/play can stop it.
        var st = OnePieceTcg.Engine.GameEngine.CreateMatch(new OnePieceTcg.Engine.MatchConfig { SouthDeck = "st01", NorthDeck = "st01", Seed = "mate2" });
        st.Status = "active"; st.Phase = "main"; st.ActiveSeat = "south"; st.TurnNumber = 6;
        var S = st.Players["south"]; var N = st.Players["north"];
        S.TurnsStarted = 4; N.TurnsStarted = 4;
        for (int i = 0; i < 5; i++) { S.CharacterArea[i] = null; N.CharacterArea[i] = null; }
        S.Hand.Clear(); N.Hand.Clear(); S.CostArea.Clear(); N.CostArea.Clear(); S.Life.Clear(); N.Life.Clear();
        OnePieceTcg.Engine.CardInstance In(string id, string owner, string zone) => new OnePieceTcg.Engine.CardInstance
        { InstanceId = $"{owner}-{id}-{System.Guid.NewGuid():N}".Substring(0, 22), CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0 };
        S.Leader.CardId = "ST01-001"; S.Leader.Rested = false; S.Leader.PlayedOnTurn = 0; S.Leader.AttachedDonIds.Clear();
        N.Leader.CardId = "ST01-001"; N.Leader.Rested = false; N.Leader.PlayedOnTurn = 0; N.Leader.AttachedDonIds.Clear();
        N.Life.Add(In("EB01-002", "north", "life"));                       // 1 Life, counter 0
        for (int i = 0; i < 3; i++) S.Life.Add(In("EB02-052", "south", "life"));   // I have Life so their Leader swing can't just kill me
        for (int i = 0; i < 4; i++) S.CostArea.Add(new OnePieceTcg.Engine.DonInstance { InstanceId = $"south-don-{i}", Rested = false });
        S.Hand.Add(In("ST01-010", "south", "hand"));                       // cost-4 6000 finisher (no Rush)
        N.Deck.Clear(); for (int i = 0; i < 10; i++) N.Deck.Add(In("EB02-052", "north", "deck"));   // cost-10 counter-0: undraw-playable
        S.Deck.Clear(); for (int i = 0; i < 10; i++) S.Deck.Add(In("EB02-052", "south", "deck"));
        N.DonDeck = 0;                                                     // opponent cannot gain DON -> cannot boost its Leader or play

        var one = OnePieceTcg.Engine.Puzzles.LethalSolver.Solve(st, "south", new OnePieceTcg.Engine.Puzzles.LethalSolver.Options { WorkBudget = 500_000 });
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var m2 = OnePieceTcg.Engine.Puzzles.Mate2Solver.Solve(st, "south", new OnePieceTcg.Engine.Puzzles.Mate2Solver.Options { WorkBudget = 3_000_000 });
        var forced = OnePieceTcg.Engine.Puzzles.ForcedWinSolver.Solve(st, "south",
            new OnePieceTcg.Engine.Puzzles.ForcedWinSolver.Options
            {
                WorkBudget = 3_000_000,
                MaxPlayerTurns = 2,
            });
        sw.Stop();
        Console.WriteLine($"mate2test: one-turn={one.Outcome} (want NoLethal), old-mate2={m2} (want Win), " +
                          $"forced={forced.Outcome} work={forced.Work} playerDecisions={forced.PlayerDecisionNodes} " +
                          $"oppBranches={forced.OpponentBranches} (want Win)  [{sw.ElapsedMilliseconds}ms]");
        return (one.Outcome == OnePieceTcg.Engine.Puzzles.LethalSolver.Lethal.NoLethal
             && m2 == OnePieceTcg.Engine.Puzzles.Mate2Solver.Mate.Win
             && forced.Outcome == OnePieceTcg.Engine.Puzzles.ForcedWinSolver.ForcedWin.Win) ? 0 : 1;
    }

    case "mechanicpuzzlecheck":
    {
        // Certify the deliberately different mechanic families. Multi-turn entries must fail the one-turn
        // oracle and pass the adversarial cross-turn oracle; this prevents a labelled "setup" puzzle from
        // secretly being another attack-everything lethal.
        var puzzles = OnePieceTcg.Engine.Puzzles.MechanicPuzzleLibrary.All();
        if (args.Length > 1)
            puzzles = puzzles.Where(p => p.Id == args[1]).ToList();
        int bad = 0;
        foreach (var pz in puzzles)
        {
            var st = pz.Build();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var one = OnePieceTcg.Engine.Puzzles.LethalSolver.Solve(st, pz.Attacker,
                new OnePieceTcg.Engine.Puzzles.LethalSolver.Options
                {
                    WorkBudget = 1_500_000,
                    MaxDepth = 100,
                });

            string outcome;
            long work;
            int playerDecisions;
            long opponentBranches;
            int turnsReached;
            IEnumerable<OnePieceTcg.Engine.GameCommand> pv;
            bool certified;
            string qualityText = "";
            if (pz.PlayerTurnLimit <= 1)
            {
                var quality = OnePieceTcg.Engine.Puzzles.PuzzleQualityAnalyzer.Analyze(st, pz.Attacker, one,
                    new OnePieceTcg.Engine.Puzzles.PuzzleQualityAnalyzer.Options
                    {
                        SolveBudget = 1_500_000,
                        MaxDecisionDepth = 6,
                        MinCriticalDecisions = 2,
                        MinAttackerMoves = 3,
                        MinLegalMovesAtRoot = 3,
                        MaxLegalMovesPerDecision = 64,
                    });
                outcome = one.Outcome.ToString();
                work = one.Work;
                playerDecisions = 0;
                opponentBranches = 0;
                turnsReached = 1;
                pv = one.PrincipalVariation;
                certified = one.Outcome == OnePieceTcg.Engine.Puzzles.LethalSolver.Lethal.Win && quality.Passed;
                qualityText = $" quality={quality.Passed}:{quality.Reason} critical={quality.CriticalDecisions}";
            }
            else
            {
                var forced = OnePieceTcg.Engine.Puzzles.ForcedWinSolver.Solve(st, pz.Attacker,
                    new OnePieceTcg.Engine.Puzzles.ForcedWinSolver.Options
                    {
                        WorkBudget = 5_000_000,
                        MaxDepth = 180,
                        MaxPlayerTurns = pz.PlayerTurnLimit,
                    });
                outcome = forced.Outcome.ToString();
                work = forced.Work;
                playerDecisions = forced.PlayerDecisionNodes;
                opponentBranches = forced.OpponentBranches;
                turnsReached = forced.PlayerTurnsReached;
                pv = forced.PrincipalVariation;
                var quality = OnePieceTcg.Engine.Puzzles.ForcedWinQualityAnalyzer.Analyze(
                    st, pz.Attacker, pz.PlayerTurnLimit,
                    new OnePieceTcg.Engine.Puzzles.ForcedWinQualityAnalyzer.Options
                    {
                        RootBudget = 5_000_000,
                        AlternativeBudget = 40_000,
                        MaxDepth = 180,
                        MaxPlayerDecisionsAudited = 10,
                        MinCriticalDecisions = 2,
                        MinDistinctPlayerActionTypes = 5,
                    });
                certified = one.Outcome == OnePieceTcg.Engine.Puzzles.LethalSolver.Lethal.NoLethal
                         && forced.Outcome == OnePieceTcg.Engine.Puzzles.ForcedWinSolver.ForcedWin.Win
                         && forced.PlayerTurnsReached >= 2
                         && quality.Passed;
                qualityText = $" quality={quality.Passed}:{quality.Reason} critical={quality.CriticalDecisions} types={quality.DistinctPlayerActionTypes}";
            }
            sw.Stop();

            string line = string.Join(" | ", pv.Select(c =>
                $"{c.Seat}:{c.Type}:{c.Attacker ?? c.InstanceId ?? c.Blocker ?? c.Target ?? c.EffectId}"));
            Console.WriteLine($"  {(certified ? "PASS" : "FAIL")} {pz.Id}: one={one.Outcome}, " +
                              $"forced={outcome}, work={work}, playerDecisions={playerDecisions}, " +
                              $"oppBranches={opponentBranches}, turns={turnsReached}{qualityText} [{sw.ElapsedMilliseconds}ms]");
            Console.WriteLine("       " + line);
            if (!certified) bad++;
        }
        Console.WriteLine($"mechanicpuzzlecheck: {puzzles.Count - bad}/{puzzles.Count} certified ({bad} bad)");
        return bad == 0 ? 0 : 1;
    }

    case "mechanictrace":
    {
        if (args.Length < 2)
        {
            Console.WriteLine("usage: mechanictrace <puzzle-id> [legal-index ...]");
            return 2;
        }
        var pz = OnePieceTcg.Engine.Puzzles.MechanicPuzzleLibrary.All()
            .FirstOrDefault(p => p.Id == args[1]);
        if (pz == null) { Console.WriteLine("unknown mechanic puzzle"); return 2; }
        var st = pz.Build();
        string Label(OnePieceTcg.Engine.CardInstance c) =>
            c == null ? "." : $"{c.CardId}/{c.InstanceId}";
        void Dump(int step)
        {
            var decider = OnePieceTcg.Engine.Puzzles.LethalSolver.DecidingSeat(st, pz.Attacker);
            var legal = OnePieceTcg.Engine.Bot.Search.LegalActions.Validate(st, decider,
                OnePieceTcg.Engine.Bot.Search.LegalActions.Candidates(st, decider));
            Console.WriteLine($"step={step} active={st.ActiveSeat} phase={st.Phase} decider={decider} " +
                              $"battle={st.Battle?.Step ?? "-"} pending={st.PendingEffects.Count} status={st.Status}");
            foreach (var seat in new[] { "south", "north" })
            {
                var p = st.Players[seat];
                Console.WriteLine($"  {seat}: leader={Label(p.Leader)} life={p.Life.Count} hand=[{string.Join(",", p.Hand.Select(Label))}] " +
                                  $"chars=[{string.Join(",", p.CharacterArea.Where(c => c != null).Select(Label))}] " +
                                  $"don={p.CostArea.Count(d => !d.Rested)}/{p.CostArea.Count}");
            }
            for (int i = 0; i < legal.Count; i++)
            {
                var c = legal[i].Key;
                Console.WriteLine($"  [{i}] {c.Seat}:{c.Type} inst={c.InstanceId} atk={c.Attacker} " +
                                  $"target={c.Target} blocker={c.Blocker} effect={c.EffectId} amount={c.Amount}");
            }
            if (st.PendingEffects.Count > 0)
                Console.WriteLine("  effect: " + st.PendingEffects[0].Text.Replace("\n", " "));
        }
        for (int step = 0; ; step++)
        {
            Dump(step);
            if (step + 2 >= args.Length) break;
            string decider = OnePieceTcg.Engine.Puzzles.LethalSolver.DecidingSeat(st, pz.Attacker);
            var legal = OnePieceTcg.Engine.Bot.Search.LegalActions.Validate(st, decider,
                OnePieceTcg.Engine.Bot.Search.LegalActions.Candidates(st, decider));
            int index = int.Parse(args[step + 2]);
            if (index < 0 || index >= legal.Count) { Console.WriteLine("index out of range"); return 2; }
            st = legal[index].Value;
        }
        return 0;
    }

    case "formattest":
    {
        int fail = 0;
        void Chk(string label, bool ok) { Console.WriteLine($"  {(ok ? "PASS" : "FAIL")}  {label}"); if (!ok) fail++; }
        var FL = typeof(OnePieceTcg.Engine.FormatLegality);
        // block data actually loaded?
        var op01 = OnePieceTcg.Engine.CardData.GetCard("OP01-001");
        var op16 = OnePieceTcg.Engine.CardData.GetCard("OP16-001");
        Chk($"OP01-001 block loaded (='{op01?.Block}')", op01 != null && op01.Block == "1");
        Chk($"OP16-001 block loaded (='{op16?.Block}')", op16 != null && !string.IsNullOrEmpty(op16.Block));
        // per-card legality
        Chk("OP06-047 (Pudding) is Banned", OnePieceTcg.Engine.FormatLegality.Legality("OP06-047") == OnePieceTcg.Engine.CardLegality.Banned);
        Chk("OP01-001 is ExtraOnly (Block 1 rotated)", OnePieceTcg.Engine.FormatLegality.Legality("OP01-001") == OnePieceTcg.Engine.CardLegality.ExtraOnly);
        Chk("OP16-001 is LegalBoth", OnePieceTcg.Engine.FormatLegality.Legality("OP16-001") == OnePieceTcg.Engine.CardLegality.LegalBoth);
        Chk("OP01-001 legal in Extra", OnePieceTcg.Engine.FormatLegality.IsCardLegal("OP01-001", OnePieceTcg.Engine.GameFormat.ExtraRegulation));
        Chk("OP01-001 NOT legal in Standard", !OnePieceTcg.Engine.FormatLegality.IsCardLegal("OP01-001", OnePieceTcg.Engine.GameFormat.Standard));
        Chk("OP06-047 NOT legal in Extra (banned)", !OnePieceTcg.Engine.FormatLegality.IsCardLegal("OP06-047", OnePieceTcg.Engine.GameFormat.ExtraRegulation));
        Chk("alt-art suffix normalizes (OP01-001_p1)", !OnePieceTcg.Engine.FormatLegality.IsCardLegal("OP01-001_p1", OnePieceTcg.Engine.GameFormat.Standard));
        // deck checks
        var stdDeck = new[] { "OP16-001", "OP16-002", "OP16-002" };
        Chk("clean Standard deck is legal", OnePieceTcg.Engine.FormatLegality.CheckDeck(stdDeck, OnePieceTcg.Engine.GameFormat.Standard).Legal);
        var block1Deck = new[] { "OP16-001", "OP01-005" };
        Chk("Block-1 card illegal in Standard, legal in Extra",
            !OnePieceTcg.Engine.FormatLegality.CheckDeck(block1Deck, OnePieceTcg.Engine.GameFormat.Standard).Legal
            && OnePieceTcg.Engine.FormatLegality.CheckDeck(block1Deck, OnePieceTcg.Engine.GameFormat.ExtraRegulation).Legal);
        var bannedDeck = new[] { "OP16-001", "OP06-047" };
        Chk("banned card illegal in BOTH", !OnePieceTcg.Engine.FormatLegality.CheckDeck(bannedDeck, OnePieceTcg.Engine.GameFormat.Standard).Legal
            && !OnePieceTcg.Engine.FormatLegality.CheckDeck(bannedDeck, OnePieceTcg.Engine.GameFormat.ExtraRegulation).Legal);
        var pairDeck = new[] { "OP11-040", "OP11-067", "OP16-001" };
        var pairChk = OnePieceTcg.Engine.FormatLegality.CheckDeck(pairDeck, OnePieceTcg.Engine.GameFormat.ExtraRegulation);
        Chk("banned pair detected: " + string.Join("; ", pairChk.Reasons), !pairChk.Legal);
        Console.WriteLine("  pool(Standard): " + OnePieceTcg.Engine.FormatLegality.PoolDescription(OnePieceTcg.Engine.GameFormat.Standard));
        Console.WriteLine("  ban list: " + string.Join(" | ", OnePieceTcg.Engine.FormatLegality.BanListLines()));
        Console.WriteLine($"formattest: {(fail == 0 ? "ALL PASS" : fail + " FAILED")}");
        return fail == 0 ? 0 : 1;
    }

    case "puzzleattack":
    {
        // Reproduce the "attack got stuck" report: drive RAW attacks (no setup) through PuzzleRuntime.ApplyMove
        // like the UI does, and time each — a freeze shows up as a slow ApplyMove / non-advancing state.
        int seed = args.Length > 1 ? int.Parse(args[1]) : 12;
        var gp = OnePieceTcg.Engine.Puzzles.PuzzleGenerator.Build(seed);
        var rt = new OnePieceTcg.Engine.Puzzles.PuzzleRuntime();
        if (!rt.Start(gp.State, gp.AttackerSeat)) { Console.WriteLine($"seed {seed} [{gp.Category}] Start failed: {rt.Message}"); return 1; }
        Console.WriteLine($"seed {seed} [{gp.Category}] {gp.Title} — driving raw attacks:");
        for (int step = 0; step < 12 && rt.Status == OnePieceTcg.Engine.Puzzles.PuzzleRuntime.PuzzleStatus.InProgress; step++)
        {
            var atk = rt.LegalMoves().FirstOrDefault(m => m.Type == "declareAttack");
            if (atk == null) { Console.WriteLine($"  step {step}: no attack available (moves: {string.Join(",", rt.LegalMoves().Select(m => m.Type).Distinct())})"); break; }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool ok = rt.ApplyMove(atk);
            sw.Stop();
            var st = rt.State;
            Console.WriteLine($"  step {step}: attack {atk.Attacker}->{atk.Target}  applied={ok}  {sw.ElapsedMilliseconds}ms  status={rt.Status}  battle={(st.Battle == null ? "none" : st.Battle.Step)}  active={st.ActiveSeat}");
        }
        Console.WriteLine($"  final: status={rt.Status}");
        return 0;
    }

    case "puzzledump":
    {
        int seed = args.Length > 1 ? int.Parse(args[1]) : 1;
        var gp = OnePieceTcg.Engine.Puzzles.PuzzleGenerator.Build(seed);
        var st = gp.State; var S = st.Players["south"]; var N = st.Players["north"];
        string Pw(OnePieceTcg.Engine.CardInstance c) => c == null ? "." : $"{c.CardId}({OnePieceTcg.Engine.GameEngine.GetPower(st, c)}{(c.Rested ? "R" : "")})";
        Console.WriteLine($"seed {seed} [{gp.Category}] {gp.Title}");
        Console.WriteLine($"  SOUTH leader {Pw(S.Leader)} don={S.CostArea.Count} chars=[{string.Join(" ", S.CharacterArea.Where(c => c != null).Select(Pw))}] hand=[{string.Join(" ", S.Hand.Select(c => c.CardId))}]");
        Console.WriteLine($"  NORTH leader {Pw(N.Leader)} life={N.Life.Count} don={N.CostArea.Count} chars=[{string.Join(" ", N.CharacterArea.Where(c => c != null).Select(Pw))}] hand=[{string.Join(" ", N.Hand.Select(c => c.CardId))}]");
        System.Console.WriteLine("  SOLUTION: " + OnePieceTcg.Engine.Puzzles.HintGenerator.DescribeSolution(st, "south")?.Replace("\n", " | "));
        var cands = OnePieceTcg.Engine.Bot.Search.LegalActions.Validate(st, "south",
            OnePieceTcg.Engine.Bot.Search.LegalActions.Candidates(st, "south"));
        Console.WriteLine($"  south legal moves: {string.Join(" | ", cands.Select(kv => $"{kv.Key.Type}:{kv.Key.Attacker ?? kv.Key.Target}"))}");
        var r = OnePieceTcg.Engine.Puzzles.LethalSolver.Solve(st, "south", new OnePieceTcg.Engine.Puzzles.LethalSolver.Options { WorkBudget = 500_000 });
        Console.WriteLine($"  => {r.Outcome} work={r.Work}  PV: {string.Join(" | ", r.PrincipalVariation.Select(c => $"{c.Seat}:{c.Type}:{c.Attacker ?? c.Target ?? c.InstanceId}"))}");
        return 0;
    }

    case "puzzletime":
    {
        int n = args.Length > 1 ? int.Parse(args[1]) : 30;
        long bud = args.Length > 2 ? long.Parse(args[2]) : 100_000;
        var o = new OnePieceTcg.Engine.Puzzles.LethalSolver.Options { WorkBudget = bud, MaxDepth = 60 };
        int win = 0;
        for (int seed = 1; seed <= n; seed++)
        {
            var gp = OnePieceTcg.Engine.Puzzles.PuzzleGenerator.Build(seed);
            var t = System.Diagnostics.Stopwatch.StartNew();
            var r = OnePieceTcg.Engine.Puzzles.LethalSolver.Solve(gp.State, "south", o);
            t.Stop();
            if (r.Outcome == OnePieceTcg.Engine.Puzzles.LethalSolver.Lethal.Win) win++;
            Console.WriteLine($"seed {seed,4} {gp.Category,-13} {r.Outcome,-8} work={r.Work,9} nodes={r.Nodes,7} {t.ElapsedMilliseconds,6}ms budgetHit={r.BudgetHit}");
        }
        Console.WriteLine($"lethal {win}/{n}");
        return 0;
    }

    case "puzzlecheck":
    {
        // Validate every baked puzzle through the strict decision gate and RUNTIME, which re-solves at the live
        // budget — catches any that certified offline but would show a false "Failed" at load.
        var lib = OnePieceTcg.Engine.Puzzles.PuzzleLibrary.All();
        int ok = 0, bad = 0;
        foreach (var pz in lib)
        {
            var position = pz.Build();
            bool qualityPassed;
            string qualityReason;
            if (pz.PlayerTurnLimit <= 1)
            {
                var quality = OnePieceTcg.Engine.Puzzles.PuzzleQualityAnalyzer.Analyze(position, pz.Attacker,
                    options: new OnePieceTcg.Engine.Puzzles.PuzzleQualityAnalyzer.Options
                    {
                        SolveBudget = 1_500_000, MaxDecisionDepth = 6, MinCriticalDecisions = 2,
                        MinAttackerMoves = 3, MinLegalMovesAtRoot = 3, MaxLegalMovesPerDecision = 64,
                    });
                qualityPassed = quality.Passed;
                qualityReason = quality.Reason;
            }
            else
            {
                var quality = OnePieceTcg.Engine.Puzzles.ForcedWinQualityAnalyzer.Analyze(
                    position, pz.Attacker, pz.PlayerTurnLimit,
                    new OnePieceTcg.Engine.Puzzles.ForcedWinQualityAnalyzer.Options
                    {
                        RootBudget = 5_000_000, AlternativeBudget = 40_000,
                        MinCriticalDecisions = 2, MinDistinctPlayerActionTypes = 5,
                    });
                qualityPassed = quality.Passed;
                qualityReason = quality.Reason;
            }
            if (!qualityPassed)
            {
                bad++;
                Console.WriteLine($"  FAIL(quality) {pz.Id} [{pz.Category} D{pz.Difficulty}]: {qualityReason}");
                continue;
            }
            var rt = new OnePieceTcg.Engine.Puzzles.PuzzleRuntime();
            if (!rt.Start(position, pz.Attacker, pz.PlayerTurnLimit))
            { bad++; Console.WriteLine($"  FAIL(start) {pz.Id} [{pz.Category} D{pz.Difficulty}]: {rt.Message}"); continue; }
            // Drive adaptively vs the runtime's optimal defence — must reach Solved.
            for (int g2 = 0; g2 < 100 && rt.Status == OnePieceTcg.Engine.Puzzles.PuzzleRuntime.PuzzleStatus.InProgress; g2++)
            {
                OnePieceTcg.Engine.GameCommand mv;
                if (pz.PlayerTurnLimit <= 1)
                {
                    var step = OnePieceTcg.Engine.Puzzles.LethalSolver.Solve(rt.State, pz.Attacker, rt.SolveOpts);
                    mv = step.PrincipalVariation.FirstOrDefault(c => c.Seat == pz.Attacker);
                }
                else
                {
                    var step = OnePieceTcg.Engine.Puzzles.ForcedWinSolver.Solve(rt.State, pz.Attacker,
                        new OnePieceTcg.Engine.Puzzles.ForcedWinSolver.Options
                        {
                            WorkBudget = rt.ForcedSolveOpts.WorkBudget,
                            MaxDepth = rt.ForcedSolveOpts.MaxDepth,
                            MaxPlayerTurns = pz.PlayerTurnLimit,
                            FinalPlayerTurnsStarted = rt.FinalPlayerTurnsStarted,
                        });
                    mv = step.PrincipalVariation.FirstOrDefault(c => c.Seat == pz.Attacker);
                }
                if (mv == null || !rt.ApplyMove(mv)) break;
            }
            if (rt.Status == OnePieceTcg.Engine.Puzzles.PuzzleRuntime.PuzzleStatus.Solved) ok++;
            else { bad++; Console.WriteLine($"  FAIL(play) {pz.Id} [{pz.Category} D{pz.Difficulty}]: ended {rt.Status}"); }
        }
        Console.WriteLine($"puzzlecheck: {ok}/{lib.Count} passed strict quality and solved end-to-end vs optimal defence ({bad} bad)");
        return bad == 0 ? 0 : 1;
    }

    case "catalogcheck":
    {
        var catalog = OnePieceTcg.Engine.Puzzles.PuzzleLibrary.All();
        var caps = OnePieceTcg.Engine.Puzzles.PuzzleCardCapabilities.Current;
        int bad = 0;
        if (catalog.Count != OnePieceTcg.Engine.Puzzles.CertifiedPuzzleCatalog.TargetCount) bad++;
        if (catalog.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != catalog.Count) bad++;
        if (OnePieceTcg.Engine.Puzzles.CertifiedPuzzleCatalog.VerifiedSeeds.Length != 475) bad++;
        if (catalog.Count(p => p.PlayerTurnLimit > 1) < 10) bad++;
        if (catalog.Select(p => p.Category).Distinct(StringComparer.Ordinal).Count() < 12) bad++;

        var built = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in catalog)
        {
            var state = p.Build();
            if (state == null || state.Status != "active") { bad++; continue; }
            var mine = state.Players[p.Attacker];
            string oppSeat = p.Attacker == "south" ? "north" : "south";
            var opp = state.Players[oppSeat];
            string Cards(IEnumerable<OnePieceTcg.Engine.CardInstance> cards) =>
                string.Join(",", cards.Where(c => c != null).Select(c => c.CardId));
            built.Add($"{p.Category}|{mine.Leader.CardId}|{Cards(mine.CharacterArea)}|{Cards(mine.Hand)}|" +
                      $"{mine.CostArea.Count}|{opp.Leader.CardId}|{Cards(opp.CharacterArea)}|" +
                      $"{opp.Hand.Count}|{opp.Life.Count}");
        }
        // A catalog entry can deliberately share a dependency graph with a safe card-recognition variant, but
        // it must still contain hundreds of materially different board recipes.
        if (built.Count < 350) bad++;

        var sample = catalog.Where((_, i) => i % 41 == 0).Take(13).ToList();
        int solved = 0;
        foreach (var p in sample)
        {
            var state = p.Build();
            bool win;
            if (p.PlayerTurnLimit <= 1)
            {
                var proof = OnePieceTcg.Engine.Puzzles.LethalSolver.Solve(state, p.Attacker,
                    new OnePieceTcg.Engine.Puzzles.LethalSolver.Options { WorkBudget = 1_500_000, MaxDepth = 80 });
                win = proof.Outcome == OnePieceTcg.Engine.Puzzles.LethalSolver.Lethal.Win && !proof.BudgetHit;
            }
            else
            {
                var proof = OnePieceTcg.Engine.Puzzles.ForcedWinSolver.Solve(state, p.Attacker,
                    new OnePieceTcg.Engine.Puzzles.ForcedWinSolver.Options
                    {
                        WorkBudget = 5_000_000, MaxDepth = 180, MaxPlayerTurns = p.PlayerTurnLimit,
                    });
                win = proof.Outcome == OnePieceTcg.Engine.Puzzles.ForcedWinSolver.ForcedWin.Win;
            }
            if (win) solved++; else bad++;
        }

        Console.WriteLine($"catalog: {catalog.Count} entries, {built.Count} board signatures, " +
                          $"{catalog.Select(p => p.Category).Distinct().Count()} families, " +
                          $"{catalog.Count(p => p.PlayerTurnLimit > 1)} multi-turn");
        Console.WriteLine("families: " + string.Join(", ", catalog.GroupBy(p => p.Category)
            .OrderBy(g => g.Key).Select(g => $"{g.Key}={g.Count()}")));
        Console.WriteLine($"database: {caps.UniqueCards} unique, {caps.Leaders} leaders, " +
                          $"{caps.Characters} characters, {caps.Events} events, {caps.Stages} stages; " +
                          $"Blocker={caps.Blockers}, Rush={caps.Rush}, DoubleAttack={caps.DoubleAttack}, " +
                          $"Banish={caps.Banish}, Restand={caps.Restand}, KO={caps.KoRemoval}, " +
                          $"CounterEvent={caps.CounterEvents}, Life={caps.LifeEffects}, DON-active={caps.DonReactivation}");
        Console.WriteLine($"sample exact proofs: {solved}/{sample.Count}; errors={bad}");
        return bad == 0 ? 0 : 1;
    }

    case "catalogdump":
    {
        foreach (var p in OnePieceTcg.Engine.Puzzles.PuzzleLibrary.All())
            Console.WriteLine($"{p.Id}\t{p.Category}\t{p.Difficulty}\t{p.PlayerTurnLimit}");
        return 0;
    }

    case "puzzlegradecheck":
    {
        var catalog = OnePieceTcg.Engine.Puzzles.PuzzleLibrary.All();
        int bad = 0;
        foreach (var p in catalog)
        {
            if (p.Difficulty < 1 || p.Difficulty > 4
                || p.DifficultyScore <= 0
                || string.IsNullOrWhiteSpace(p.DifficultyEvidence))
                bad++;
            if (p.Difficulty != OnePieceTcg.Engine.Puzzles.PuzzleDifficultyGrader.TierFor(p.DifficultyScore))
                bad++;
            if (p.PlayerTurnLimit > 1 && p.Difficulty != 4)
                bad++;
        }

        var tiers = catalog.GroupBy(p => p.Difficulty).ToDictionary(g => g.Key, g => g.Count());
        for (int tier = 1; tier <= 4; tier++)
            if (!tiers.TryGetValue(tier, out int count) || count < 50) bad++;

        Console.WriteLine("difficulty: " + string.Join(", ", Enumerable.Range(1, 4)
            .Select(t => $"{t}:{tiers.GetValueOrDefault(t)}")));
        foreach (var family in catalog.GroupBy(p => p.Category).OrderBy(g => g.Key))
            Console.WriteLine($"  {family.Key,-36} " + string.Join("  ", Enumerable.Range(1, 4)
                .Select(t => $"D{t}={family.Count(p => p.Difficulty == t)}")));
        foreach (int tier in Enumerable.Range(1, 4))
        {
            var group = catalog.Where(p => p.Difficulty == tier).ToList();
            Console.WriteLine($"  tier {tier}: score {group.Min(p => p.DifficultyScore):0.00}.." +
                              $"{group.Max(p => p.DifficultyScore):0.00}, count={group.Count}");
        }
        Console.WriteLine($"puzzlegradecheck: {catalog.Count} graded, errors={bad}");
        return bad == 0 ? 0 : 1;
    }

    case "docqtest":
    {
        // OP09-090 Doc Q: "rest this Character: K.O. up to 1 opponent Character cost<=1". Don't rest an
        // attacker to KO a body that poses no threat (Saint Shalria: cost 1, 0 power, spent [On Play], UNRESTED).
        var st = OnePieceTcg.Engine.GameEngine.CreateMatch(new OnePieceTcg.Engine.MatchConfig { SouthDeck = "st01", NorthDeck = "st01", Seed = "docq" });
        st.Status = "active"; st.Phase = "main"; st.ActiveSeat = "south"; st.TurnNumber = 6;
        var S = st.Players["south"]; var N = st.Players["north"];
        for (int i = 0; i < 5; i++) { S.CharacterArea[i] = null; N.CharacterArea[i] = null; }
        OnePieceTcg.Engine.CardInstance In(string id, string owner, string zone, bool rested = false) => new OnePieceTcg.Engine.CardInstance
        { InstanceId = $"{owner}-{id}-{System.Guid.NewGuid():N}".Substring(0, 18), CardId = id, Owner = owner, Zone = zone, Rested = rested };
        var docq = In("OP09-090", "south", "character"); S.CharacterArea[0] = docq;
        var docqDef = OnePieceTcg.Engine.GameEngine.GetCard(docq);
        N.CharacterArea[0] = In("OP13-086", "north", "character", rested: false);   // Saint Shalria: unrested, 0 power, spent [On Play]
        bool skipShalria = OnePieceTcg.Engine.Bot.Search.AdvancedActivationPolicy.RestCostRemovalHasNoLiveTarget(st, "south", docqDef);
        N.CharacterArea[0] = In("ST01-003", "north", "character", rested: false);   // Karoo: cost 1, 3000 power -> in-range threat
        bool skipPowerBody = OnePieceTcg.Engine.Bot.Search.AdvancedActivationPolicy.RestCostRemovalHasNoLiveTarget(st, "south", docqDef);
        System.Console.WriteLine($"Doc Q rest-removal gate: unrested spent 0-power Shalria -> skip={skipShalria}  cost-1 power body -> allow={!skipPowerBody}");
        return (skipShalria && !skipPowerBody) ? 0 : 1;
    }

    case "puzzleharvest":
    {
        // Reconstruct saved real games and harvest forced-lethal positions into StreamingAssets puzzles.
        // usage: puzzleharvest [baseDir] [outPath]   (baseDir = the game's persistentDataPath)
        string baseDir = args.Length > 1 ? args[1]
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                           "..", "LocalLow", "DefaultCompany", "One Piece TCG Simulator");
        string outPath = args.Length > 2 ? args[2]
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                               "Assets", "StreamingAssets", "Puzzles", "harvested.json"));
        Console.WriteLine($"Harvesting from: {Path.GetFullPath(baseDir)}");
        OnePieceTcg.Sim.Puzzles.PuzzleHarvester.Run(baseDir, outPath);
        return 0;
    }

    case "odentest":
    {
        // EB01-001 Kouzuki Oden leader: "[DON!! x1] [When Attacking] If you have a {Land of Wano} type
        // Character with a cost of 5 or more, this Leader gains +1000 power until the start of your next turn."
        // Bug report: leader got +1000 with NO DON attached — ExtractTimedClause dropped the leading [DON!! x1]
        // gate. Verify: 0 DON => no buff (power 5000); 1 DON => buff applies (5000 + 1000 raw DON + 1000 buff).
        int PowerAfterAttack(int donOnLeader)
        {
            var st = OnePieceTcg.Engine.GameEngine.CreateMatch(new OnePieceTcg.Engine.MatchConfig { SouthDeck = "st01", NorthDeck = "st01", Seed = "oden" });
            st.Status = "active"; st.Phase = "main"; st.ActiveSeat = "south"; st.TurnNumber = 6;
            var S = st.Players["south"]; var N = st.Players["north"];
            for (int i = 0; i < 5; i++) { S.CharacterArea[i] = null; N.CharacterArea[i] = null; }
            OnePieceTcg.Engine.CardInstance In(string id, string owner, string zone, bool rested = false) => new OnePieceTcg.Engine.CardInstance
            { InstanceId = $"{owner}-{id}-{System.Guid.NewGuid():N}".Substring(0, 18), CardId = id, Owner = owner, Zone = zone, Rested = rested };
            S.TurnsStarted = 3; N.TurnsStarted = 2;             // past the no-battle-on-first-turn rule
            S.Leader.CardId = "EB01-001";                       // Oden leader
            S.CharacterArea[0] = In("EB01-007", "south", "character");   // Yamato: {Land of Wano}, cost 5 (condition met)
            for (int i = 0; i < donOnLeader; i++) S.Leader.AttachedDonIds.Add("D" + i);   // DON!! attached to the leader
            OnePieceTcg.Engine.GameEngine.ApplyCommand(st, new OnePieceTcg.Engine.GameCommand
            { Type = "declareAttack", Seat = "south", Attacker = S.Leader.InstanceId, Target = N.Leader.InstanceId });
            return OnePieceTcg.Engine.GameEngine.GetPower(st, S.Leader);
        }
        int p0 = PowerAfterAttack(0);
        int p1 = PowerAfterAttack(1);
        bool gateHolds = p0 == 5000;                 // 0 DON => gate blocks the buff
        bool buffApplies = p1 == 7000;               // 1 DON => 5000 + 1000(raw DON) + 1000(Oden buff)
        System.Console.WriteLine($"Oden [DON!! x1] gate: 0-DON power={p0} (want 5000, gate holds={gateHolds}), 1-DON power={p1} (want 7000, buff applies={buffApplies})");
        return (gateHolds && buffApplies) ? 0 : 1;
    }

    case "harvestcheck":
    {
        // Verify each harvested.json puzzle round-trips: rebuild the recipe and confirm PuzzleRuntime.Start
        // proves it a forced lethal (i.e. it's a valid, solvable puzzle), then drive the solver's line to a win.
        string hp = args.Length > 1 ? args[1]
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                               "Assets", "StreamingAssets", "Puzzles", "harvested.json"));
        var setJson = System.Text.Json.JsonSerializer.Deserialize<OnePieceTcg.Engine.Puzzles.HarvestedPuzzleSet>(
            File.ReadAllText(hp), new System.Text.Json.JsonSerializerOptions { IncludeFields = true, PropertyNameCaseInsensitive = true });
        int ok = 0, bad = 0;
        foreach (var puz in setJson.puzzles)
        {
            var rt = new OnePieceTcg.Engine.Puzzles.PuzzleRuntime();
            bool started;
            OnePieceTcg.Engine.Puzzles.PuzzleQualityAnalyzer.Report quality;
            try
            {
                var built = puz.Build();
                started = rt.Start(built, puz.Attacker);
                var proof = OnePieceTcg.Engine.Puzzles.LethalSolver.Solve(
                    built, puz.Attacker,
                    new OnePieceTcg.Engine.Puzzles.LethalSolver.Options { WorkBudget = 150_000 });
                quality = OnePieceTcg.Engine.Puzzles.PuzzleQualityAnalyzer.Analyze(
                    built, puz.Attacker, proof,
                    OnePieceTcg.Engine.Puzzles.PuzzleQualityAnalyzer.StrictHarvest());
            }
            catch (Exception e) { Console.WriteLine($"  THREW {puz.Id}: {e.Message}"); bad++; continue; }
            bool stamped = puz.Quality != null
                && puz.Quality.Version >= OnePieceTcg.Engine.Puzzles.PuzzleQualityAnalyzer.CurrentVersion
                && puz.Quality.Passed;
            bool valid = started && quality.Passed && stamped;
            Console.WriteLine($"  {(valid ? "OK  " : "BAD ")} {puz.Id} D{puz.Difficulty} start={started} " +
                              $"quality={quality.Passed} stamped={stamped} critical={quality.CriticalDecisions} " +
                              $"first={quality.WinningFirstMoves}/{quality.LegalFirstMoves} reason='{quality.Reason}'");
            if (valid) ok++; else bad++;
        }
        Console.WriteLine($"harvestcheck: {ok}/{ok + bad} harvested puzzles are lethal AND quality-certified ({bad} bad)");
        return bad == 0 ? 0 : 1;
    }

    case "datest":
    {
        // Official ruling Q36: [Double Attack] does NOT defeat a Leader with exactly 1 Life (they take the last
        // card and survive at 0). It DOES defeat a Leader already at 0 Life. Oracle via the solver.
        OnePieceTcg.Engine.GameState Setup(int northLife)
        {
            var st = OnePieceTcg.Engine.GameEngine.CreateMatch(new OnePieceTcg.Engine.MatchConfig { SouthDeck = "st01", NorthDeck = "st01", Seed = "da" });
            st.Status = "active"; st.Phase = "main"; st.ActiveSeat = "south"; st.TurnNumber = 6;
            var S = st.Players["south"]; var N = st.Players["north"];
            S.TurnsStarted = 3; N.TurnsStarted = 2;
            for (int i = 0; i < 5; i++) { S.CharacterArea[i] = null; N.CharacterArea[i] = null; }
            N.Hand.Clear();
            N.Life.Clear();
            for (int i = 0; i < northLife; i++)
                N.Life.Add(new OnePieceTcg.Engine.CardInstance { InstanceId = "north-life-" + i, CardId = "ST01-002", Owner = "north", Zone = "life" });
            S.Leader.Rested = true;   // only the Double-Attack Character can attack
            var da = new OnePieceTcg.Engine.CardInstance { InstanceId = "south-da-1", CardId = "OP01-121", Owner = "south", Zone = "character", Rested = false };
            for (int i = 0; i < 3; i++) da.AttachedDonIds.Add("d" + i);   // 5000+3000 power → connects on the 5000 Leader
            S.CharacterArea[0] = da;
            return st;
        }
        var opts = new OnePieceTcg.Engine.Puzzles.LethalSolver.Options { WorkBudget = 60_000 };
        var at1 = OnePieceTcg.Engine.Puzzles.LethalSolver.Solve(Setup(1), "south", opts);
        var at0 = OnePieceTcg.Engine.Puzzles.LethalSolver.Solve(Setup(0), "south", opts);
        bool ok1 = at1.Outcome != OnePieceTcg.Engine.Puzzles.LethalSolver.Lethal.Win;   // DA can't kill at 1 Life
        bool ok0 = at0.Outcome == OnePieceTcg.Engine.Puzzles.LethalSolver.Lethal.Win;   // DA kills at 0 Life
        System.Console.WriteLine($"Double Attack: at 1 Life -> {at1.Outcome} (want not-Win: {ok1}); at 0 Life -> {at0.Outcome} (want Win: {ok0})");
        return (ok1 && ok0) ? 0 : 1;
    }

    case "starterlegaltest":
    {
        foreach (var kv in OnePieceTcg.Engine.CardData.StarterDecks)
        {
            var def = kv.Value;
            var ids = new List<string> { def.Leader };
            foreach (var (cid, qty) in def.List) for (int i = 0; i < qty; i++) ids.Add(cid);
            var std = OnePieceTcg.Engine.FormatLegality.CheckDeck(ids, OnePieceTcg.Engine.GameFormat.Standard);
            var ext = OnePieceTcg.Engine.FormatLegality.CheckDeck(ids, OnePieceTcg.Engine.GameFormat.ExtraRegulation);
            System.Console.WriteLine($"{kv.Key,-10} leader={def.Leader}  Standard={(std.Legal ? "LEGAL " : "ILLEGAL")}  Extra={(ext.Legal ? "LEGAL" : "ILLEGAL")}   {(std.Legal ? "" : std.Reasons.FirstOrDefault())}");
        }
        // Full-library sweep: prove BlockOf runs over EVERY card, and only Block-1 sets (minus overrides) are illegal.
        var bySet = new System.Collections.Generic.SortedDictionary<string, (int legal, int illegal)>();
        int totalIllegal = 0;
        foreach (var id in OnePieceTcg.Engine.CardData.Library.Keys)
        {
            var mm = System.Text.RegularExpressions.Regex.Match(id, @"^([A-Za-z]+\d*)-");
            string pfx = mm.Success ? mm.Groups[1].Value.ToUpperInvariant() : "OTHER";
            bool legal = OnePieceTcg.Engine.FormatLegality.IsCardLegal(id, OnePieceTcg.Engine.GameFormat.Standard);
            var cur = bySet.TryGetValue(pfx, out var v) ? v : (0, 0);
            bySet[pfx] = legal ? (cur.Item1 + 1, cur.Item2) : (cur.Item1, cur.Item2 + 1);
            if (!legal) totalIllegal++;
        }
        System.Console.WriteLine($"--- Standard-legality sweep over {OnePieceTcg.Engine.CardData.Library.Count} loaded cards ({totalIllegal} illegal) ---");
        foreach (var kv in bySet)
            if (kv.Value.illegal > 0) System.Console.WriteLine($"  {kv.Key,-8} legal={kv.Value.legal,4}  ILLEGAL={kv.Value.illegal,4}");
        System.Console.WriteLine("--- spot checks (block derivation + overrides) ---");
        foreach (var cid in new[] { "OP05-082", "OP02-068", "OP01-001", "ST01-002", "OP01-016", "OP13-083", "OP09-001", "P-091" })
            System.Console.WriteLine($"  {cid,-10} block={OnePieceTcg.Engine.FormatLegality.BlockOf(cid),-2} StandardLegal={OnePieceTcg.Engine.FormatLegality.IsCardLegal(cid, OnePieceTcg.Engine.GameFormat.Standard)}");
        return 0;
    }

    case "vivitest":
    {
        // EB03-001 Nefeltari Vivi leader: her ability is [Activate: Main]; it merely REFERENCES the keyword
        // ("...up to 1 of your Characters without a [When Attacking] effect gains [Rush]..."). Attacking with
        // her must NOT fire a When-Attacking trigger — HasTiming was matching the inline reference.
        var st = OnePieceTcg.Engine.GameEngine.CreateMatch(new OnePieceTcg.Engine.MatchConfig { SouthDeck = "st01", NorthDeck = "st01", Seed = "vivi" });
        st.Status = "active"; st.Phase = "main"; st.ActiveSeat = "south"; st.TurnNumber = 6;
        var S = st.Players["south"]; var N = st.Players["north"];
        S.TurnsStarted = 3; N.TurnsStarted = 2;
        for (int i = 0; i < 5; i++) { S.CharacterArea[i] = null; N.CharacterArea[i] = null; }
        S.Leader.CardId = "EB03-001";
        OnePieceTcg.Engine.GameEngine.ApplyCommand(st, new OnePieceTcg.Engine.GameCommand
        { Type = "declareAttack", Seat = "south", Attacker = S.Leader.InstanceId, Target = N.Leader.InstanceId });
        bool noBogusTrigger = st.PendingEffects.Count == 0;
        System.Console.WriteLine($"Vivi attack: pendingEffects={st.PendingEffects.Count} (want 0, no bogus When-Attacking={noBogusTrigger})");
        return noBogusTrigger ? 0 : 1;
    }

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
        int tseed = args.Length > 4 && int.TryParse(args[4], out var tsd) ? tsd : 3;
        var reg = BuildRegistry(out _);
        var pool = reg.Ids.Where(i => !OnePieceTcg.Engine.CardData.StarterDecks.ContainsKey(i))
            .Where(id => { var d = reg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; }).ToList();
        long aWins = 0, done = 0; var rng = new System.Random(tseed); var sw = System.Diagnostics.Stopwatch.StartNew();
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

    case "seatab":
    {
        // Per-seat toggle A/B (donab-style): both seats run the greedy core; the NEW seat gets a named toggle
        // set, the OLD seat baseline. Alternate NEW-seat + first player to cancel seat/first bias. Both policies
        // face each other in the SAME game ⇒ tight paired signal. Reports NEW win rate + Wilson CI. FAST (greedy).
        // Usage: seatab <toggle> [games] [seed].  toggles: weakestfirst | legacyko | savecounter
        string toggle = args.Length > 1 ? args[1] : "weakestfirst";
        int games = args.Length > 2 && int.TryParse(args[2], out var gg2) ? gg2 : 800;
        int abseed = args.Length > 3 && int.TryParse(args[3], out var ab2) ? ab2 : 11;
        var reg = BuildRegistry(out _);
        var pool = reg.Ids.Where(i => !OnePieceTcg.Engine.CardData.StarterDecks.ContainsKey(i))
            .Where(id => { var d = reg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; }).ToList();
        void SetToggle(string newSeat)   // put the NEW policy on `newSeat`, OLD on the other (null clears both)
        {
            string oldSeat = newSeat == "south" ? "north" : (newSeat == "north" ? "south" : null);
            switch (toggle)
            {
                // weakest-first is now the DEFAULT; NEW seat = default, OLD seat = legacy strongest-first.
                case "weakestfirst": OnePieceTcg.Engine.Bot.IntermediateBot.LegacyStrongestFirstSeat = oldSeat; break;
                case "legacyko": OnePieceTcg.Engine.Bot.IntermediateBot.LegacyKoSeat = newSeat; break;
                case "savecounter": OnePieceTcg.Engine.Bot.IntermediateBot.SaveCounterForLeaderSeat = newSeat; break;
                case "racedrop": OnePieceTcg.Engine.Bot.IntermediateBot.RaceDropReserveSeat = newSeat; break;
                case "attackfirst": OnePieceTcg.Engine.Bot.IntermediateBot.AttackFirstSeat = newSeat; break;
                case "surplusoverload": OnePieceTcg.Engine.Bot.IntermediateBot.SurplusOverloadSeat = newSeat; break;
                case "doubleattackfirst": OnePieceTcg.Engine.Bot.IntermediateBot.DoubleAttackFirstSeat = newSeat; break;
                case "widedeploy": OnePieceTcg.Engine.Bot.IntermediateBot.WideDeploySeat = newSeat; break;
                case "holdblockers": OnePieceTcg.Engine.Bot.IntermediateBot.HoldBlockersSeat = newSeat; break;
                case "onkoaversion": OnePieceTcg.Engine.Bot.IntermediateBot.OnKoAversionSeat = newSeat; break;
                case "richdeploy": OnePieceTcg.Engine.Bot.IntermediateBot.RichDeployValueSeat = newSeat; break;
            }
        }
        int newWins = 0, done = 0; var rng = new System.Random(abseed); var sw = System.Diagnostics.Stopwatch.StartNew();
        // MATCHUP-PAIRED: each deck pair is played through a fully-crossed 4-game block (NEW∈{south,north} ×
        // first∈{south,north}) with the SAME two decks. So NEW and OLD each pilot each deck, in each seat, going
        // first once — deck+seat+first variance cancels PER MATCHUP. (The old design redrew decks per game inside
        // the balancing group, leaving a per-seed ±3pp bias — a NULLCONTROL hit 53% at seed 31.)
        int matchups = System.Math.Max(1, games / 4);
        for (int m = 0; m < matchups; m++)
        {
            string dSouth = pool[rng.Next(pool.Count)], dNorth = pool[rng.Next(pool.Count)];
            foreach (bool newSouth in new[] { true, false })
                foreach (bool firstSouth in new[] { true, false })
                {
                    string newSeat = newSouth ? "south" : "north";
                    SetToggle(newSeat);
                    var rec = OnePieceTcg.Sim.MatchDriver.Play(new OnePieceTcg.Sim.BaselineAgent(), new OnePieceTcg.Sim.BaselineAgent(),
                        reg.Resolve(dSouth), reg.Resolve(dNorth), $"seatab:{abseed}:{m}:{newSouth}:{firstSouth}",
                        firstSouth ? "south" : "north", new OnePieceTcg.Sim.MatchDriver.Options { CommandCap = 20000 });
                    SetToggle(null);
                    if (rec.winner != null) { done++; if (rec.winner == newSeat) newWins++; }
                }
        }
        double p = done > 0 ? (double)newWins / done : 0, z = 1.96, z2 = z * z, N = System.Math.Max(1, done);
        double denom = 1 + z2 / N, center = (p + z2 / (2 * N)) / denom, half = z * System.Math.Sqrt(p * (1 - p) / N + z2 / (4 * N * N)) / denom;
        Console.WriteLine($"[{toggle}] NEW vs OLD: {newWins}/{done} = {100 * p:F1}%  (95% CI [{100 * (center - half):F1}%, {100 * (center + half):F1}%])  {games} games, {sw.Elapsed.TotalSeconds:F0}s, seed={abseed}");
        return 0;
    }

    case "puzzle-harvest":
    {
        // puzzle-harvest <nGames> [K] [regretThreshold] [outfile] [seed]  → frozen suite of high-regret positions.
        int nG = args.Length > 1 && int.TryParse(args[1], out var ng) ? ng : 60;
        int K = args.Length > 2 && int.TryParse(args[2], out var kk) ? kk : 12;
        double thr = args.Length > 3 && double.TryParse(args[3], out var th) ? th : 0.12;
        string outf = args.Length > 4 ? args[4] : "puzzles.json";
        int sd = args.Length > 5 && int.TryParse(args[5], out var s5) ? s5 : 1;
        bool advRef = args.Contains("adv");   // reference play: greedy (fast, default) or advanced (relevant, slow)
        OnePieceTcg.Sim.Runner.PuzzleSuite.Harvest(BuildRegistry(out _), nG, K, thr, outf, sd, advRef);
        return 0;
    }
    case "puzzle-score":
    {
        // puzzle-score <suitefile> [bot]  → regret of `bot` on the suite. bot: advanced (default) | intermediate.
        string suite = args.Length > 1 ? args[1] : "puzzles.json";
        string botName = args.Length > 2 ? args[2] : "advanced";
        Func<OnePieceTcg.Sim.IAgent> make = botName == "intermediate" ? () => new OnePieceTcg.Sim.BaselineAgent()
            : botName == "evalgreedy" ? () => new OnePieceTcg.Sim.EvalGreedyAgent()
            : () => new OnePieceTcg.Sim.ShippedAdvancedAgent();
        OnePieceTcg.Sim.Runner.PuzzleSuite.Score(BuildRegistry(out _), suite, make, botName);
        return 0;
    }

    case "fit-value":
    {
        // fit-value <nGames> [seed] [ridge] [adv]  → train value on REAL self-play outcomes → fitted_w.txt.
        int ng = args.Length > 1 && int.TryParse(args[1], out var g1) ? g1 : 400;
        int sd = args.Length > 2 && int.TryParse(args[2], out var s1) ? s1 : 1;
        double rg = args.Length > 3 && double.TryParse(args[3], out var r1) ? r1 : 1.0;
        bool adv = args.Contains("adv");
        OnePieceTcg.Sim.Runner.PuzzleSuite.TrainValueOnOutcomes(BuildRegistry(out _), ng, sd, rg, adv);
        return 0;
    }

    case "fit-eval":
    {
        // fit-eval <suite> [ridge]  → fit eval weights to the oracle labels, score eval-greedy before/after.
        string suite = args.Length > 1 ? args[1] : "puzzles.json";
        double ridge = args.Length > 2 && double.TryParse(args[2], out var rg) ? rg : 1.0;
        string testf = args.Length > 3 ? args[3] : null;
        OnePieceTcg.Sim.Runner.PuzzleSuite.FitEval(BuildRegistry(out _), suite, ridge, () => new OnePieceTcg.Sim.EvalGreedyAgent(), testf);
        return 0;
    }

    case "oracle":
    {
        // SINGLE-SEED DECISION-QUALITY ORACLE (the user's "solve the game" tool). Play a fixed game with the
        // greedy bot; at each SOUTH clean-main decision, evaluate EVERY legal action by a K-determinized rollout
        // win-rate (fair: marginalize over the opponent's hidden info via BotDeterminizer), then compare the
        // bot's chosen action to the best. High "regret" = a concrete main-phase ERROR — low-variance signal,
        // no ±5pp win-rate fog. Usage: oracle <deckA> <deckB> [seed] [K] [maxDecisions].
        string dA = args[1], dB = args[2];
        string oseed = args.Length > 3 ? args[3] : "oracle1";
        int K = args.Length > 4 && int.TryParse(args[4], out var kk) ? kk : 10;
        int maxDec = args.Length > 5 && int.TryParse(args[5], out var md) ? md : 40;
        var reg = BuildRegistry(out _);
        string WinnerOf(OnePieceTcg.Engine.GameState g)
        {
            for (int i = g.EventLog.Count - 1; i >= 0; i--)
            { var m = g.EventLog[i].Message; if (string.IsNullOrEmpty(m) || m.IndexOf("wins", StringComparison.OrdinalIgnoreCase) < 0) continue;
              if (m.StartsWith("South", StringComparison.Ordinal)) return "south"; if (m.StartsWith("North", StringComparison.Ordinal)) return "north"; }
            return null;
        }
        // K-determinized rollout win-rate of a position for south (greedy playout both sides).
        double Evaluate(OnePieceTcg.Engine.GameState pos, int baseSeed)
        {
            int wins = 0, done = 0;
            for (int k = 0; k < K; k++)
            {
                var world = OnePieceTcg.Engine.Bot.Search.BotDeterminizer.FairView(pos, "south", baseSeed * 131 + k);
                OnePieceTcg.Sim.MatchDriver.Playout(world, new OnePieceTcg.Sim.BaselineAgent(), new OnePieceTcg.Sim.BaselineAgent(), 6000);
                var w = WinnerOf(world); if (w != null) { done++; if (w == "south") wins++; }
            }
            return done > 0 ? (double)wins / done : 0.5;
        }
        var st = OnePieceTcg.Engine.GameEngine.CreateMatch(new OnePieceTcg.Engine.MatchConfig
        { SouthDeckDef = reg.Resolve(dA), NorthDeckDef = reg.Resolve(dB), Seed = oseed });
        if (st.Status == "coinflip") { string w = st.CoinFlipWinner; OnePieceTcg.Engine.GameEngine.ApplyCommand(st, new OnePieceTcg.Engine.GameCommand { Type = "chooseTurnOrder", Seat = w, GoingFirst = w == "south" }); }
        var greedy = new OnePieceTcg.Sim.BaselineAgent();          // north advancement + rollout policy (fast, consistent)
        var southBot = new OnePieceTcg.Sim.ShippedAdvancedAgent(); // ANALYZE the ADVANCED bot's residual errors
        int decisions = 0, errors = 0; double regretSum = 0; int total = 0; var sw = System.Diagnostics.Stopwatch.StartNew();
        var byType = new Dictionary<string, (double reg, int n)>();
        Console.WriteLine($"ORACLE {dA} vs {dB} seed={oseed} K={K}");
        while (st.Status != "finished" && total < 4000 && decisions < maxDec)
        {
            bool southCleanMain = st.ActiveSeat == "south" && st.Phase == "main" && st.Battle == null
                && st.PendingEffects.Count == 0 && st.ActiveChoice == null && st.DeckLook == null;
            if (southCleanMain)
            {
                var cands = OnePieceTcg.Sim.Search.LegalActions.Validate(st, "south", OnePieceTcg.Sim.Search.LegalActions.Candidates(st, "south"));
                var botCmd = southBot.Decide(st, "south", new HashSet<string>());
                if (botCmd != null && cands.Count > 1)
                {
                    double best = -1; string bestType = "?"; double botVal = -1;
                    foreach (var kv in cands)
                    {
                        double v = Evaluate(kv.result, decisions + 1);
                        if (v > best) { best = v; bestType = $"{kv.cmd.Type}:{kv.cmd.Target ?? kv.cmd.InstanceId ?? kv.cmd.Attacker ?? "-"}"; }
                        if (OnePieceTcg.Engine.Bot.IntermediateBot.Signature(kv.cmd) == OnePieceTcg.Engine.Bot.IntermediateBot.Signature(botCmd)) botVal = v;
                    }
                    if (botVal < 0) { var bc = OnePieceTcg.Engine.Bot.Search.GameClone.Clone(st); OnePieceTcg.Engine.GameEngine.ApplyCommand(bc, botCmd); botVal = Evaluate(bc, decisions + 1); }
                    double regret = best - botVal; regretSum += regret; decisions++;
                    var pt = byType.GetValueOrDefault(botCmd.Type); byType[botCmd.Type] = (pt.reg + regret, pt.n + 1);
                    if (regret >= 0.10)
                    {
                        errors++;
                        Console.WriteLine($"  turn{st.TurnNumber} ERROR regret={regret:F2}  bot={botCmd.Type}:{botCmd.Target ?? botCmd.InstanceId ?? botCmd.Attacker ?? "-"}(wr={botVal:F2})  best={bestType}(wr={best:F2})  [{cands.Count} options]");
                    }
                }
                if (botCmd == null) break;
                OnePieceTcg.Engine.GameEngine.ApplyCommand(st, botCmd); total++;
                continue;
            }
            // Non-main / opponent decisions: south = advanced (faithful line), north = greedy.
            foreach (var seat in new[] { "south", "north" })
            {
                var c = (seat == "south" ? (OnePieceTcg.Sim.IAgent)southBot : greedy).Decide(st, seat, new HashSet<string>());
                if (c != null) { OnePieceTcg.Engine.GameEngine.ApplyCommand(st, c); total++; }
            }
        }
        Console.WriteLine($"ORACLE done: {decisions} main decisions, {errors} errors (regret≥0.10), avg regret={(decisions>0?regretSum/decisions:0):F3}, {sw.Elapsed.TotalSeconds:F0}s");
        foreach (var kv in byType.OrderByDescending(x => x.Value.reg))
            Console.WriteLine($"   by-type {kv.Key,-14} totalRegret={kv.Value.reg:F2}  n={kv.Value.n}  avg={kv.Value.reg / System.Math.Max(1, kv.Value.n):F3}");
        return 0;
    }

    case "champ":
    {
        // CHAMPION GAUNTLET (user's ratchet idea): CANDIDATE (advanced bot + a seat-scoped experimental toggle)
        // vs CHAMPION (the current best advanced bot, no toggle), HEAD-TO-HEAD in the same game. Far more
        // sensitive to top-end gains than measuring vs the weak intermediate. Matchup-paired (4-game blocks).
        // A candidate that beats the champion >50% (CI clear) is a real improvement ⇒ PROMOTE (make it default,
        // it becomes the new champion). Usage: champ <toggle> [pairs] [seed].  toggle: actgate|weakestfirst|...
        string tog = args.Length > 1 ? args[1] : "actgate";
        int cpairs = args.Length > 2 && int.TryParse(args[2], out var cp) ? cp : 150;
        int cseed = args.Length > 3 && int.TryParse(args[3], out var cs) ? cs : 3;
        void SetCand(string seat)   // enable the candidate toggle for `seat` (null clears)
        {
            switch (tog)
            {
                case "actgate": OnePieceTcg.Engine.Bot.Search.AdvancedActivationPolicy.ActivationValueGateSeat = seat; break;
                case "weakestfirst": OnePieceTcg.Engine.Bot.IntermediateBot.LegacyStrongestFirstSeat = seat == null ? null : (seat == "south" ? "north" : "south"); break; // NEW=default weakest, legacy on the OTHER seat
            }
        }
        var reg = BuildRegistry(out _);
        var pool = reg.Ids.Where(i => !OnePieceTcg.Engine.CardData.StarterDecks.ContainsKey(i))
            .Where(id => { var d = reg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; }).ToList();
        var rng = new System.Random(cseed); int candWins = 0, done = 0; var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int m = 0; m < cpairs; m++)
        {
            string dS = pool[rng.Next(pool.Count)], dN = pool[rng.Next(pool.Count)];
            foreach (bool candSouth in new[] { true, false })
                foreach (bool fSouth in new[] { true, false })
                {
                    string candSeat = candSouth ? "south" : "north";
                    SetCand(candSeat);
                    var rec = OnePieceTcg.Sim.MatchDriver.Play(new OnePieceTcg.Sim.ShippedAdvancedAgent(), new OnePieceTcg.Sim.ShippedAdvancedAgent(),
                        reg.Resolve(candSouth ? dS : dN), reg.Resolve(candSouth ? dN : dS), $"champ:{cseed}:{m}:{candSouth}:{fSouth}",
                        fSouth ? "south" : "north", new OnePieceTcg.Sim.MatchDriver.Options { CommandCap = 20000 });
                    SetCand(null);
                    if (rec.winner != null) { done++; if (rec.winner == candSeat) candWins++; }
                }
        }
        double p = done > 0 ? (double)candWins / done : 0, z = 1.96, z2 = z * z, N = System.Math.Max(1, done);
        double denom = 1 + z2 / N, center = (p + z2 / (2 * N)) / denom, half = z * System.Math.Sqrt(p * (1 - p) / N + z2 / (4 * N * N)) / denom;
        Console.WriteLine($"CANDIDATE[{tog}] vs CHAMPION(advanced): {candWins}/{done} = {100 * p:F1}%  (95% CI [{100 * (center - half):F1}%, {100 * (center + half):F1}%])  {cpairs}pairs×4, {sw.Elapsed.TotalSeconds:F0}s, seed={cseed}  ⇒ {(center - half > 0.50 ? "PROMOTE" : center + half < 0.50 ? "REJECT" : "inconclusive")}");
        return 0;
    }

    case "moduleab":
    {
        // Measure an Advanced MODULE's contribution: run shipped-advanced vs intermediate (matchup-paired) with
        // that module SKIPPED (falls back to greedy); the drop vs the ~56% all-modules baseline = its value.
        // Usage: moduleab <act|pressure|search|trigger|none> [pairs] [seed].
        string mod = args.Length > 1 ? args[1] : "none";
        int mpairs = args.Length > 2 && int.TryParse(args[2], out var mp) ? mp : 150;
        int mseed = args.Length > 3 && int.TryParse(args[3], out var ms2) ? ms2 : 3;
        OnePieceTcg.Engine.Bot.Search.AdvancedContractBot.SkipActivation = mod == "act";
        OnePieceTcg.Engine.Bot.Search.AdvancedContractBot.SkipPressure = mod == "pressure";
        OnePieceTcg.Engine.Bot.Search.AdvancedContractBot.SkipSearch = mod == "search";
        OnePieceTcg.Engine.Bot.Search.AdvancedContractBot.SkipTriggerUtility = mod == "trigger";
        OnePieceTcg.Engine.Bot.Search.AdvancedContractBot.SearchCleanMain = mod == "cleanmain";
        OnePieceTcg.Engine.Bot.Search.AdvancedActivationPolicy.ActivationValueGate = mod == "actgate";
        OnePieceTcg.Engine.Bot.Search.SearchBot.StrongRollout = mod == "strongroll" || mod == "strongmain";
        if (mod == "strongmain") OnePieceTcg.Engine.Bot.Search.AdvancedContractBot.SearchCleanMain = true;
        OnePieceTcg.Engine.Bot.Search.AdvancedContractBot.EvalGreedyMain = mod == "evalmain";
        if (mod == "evalmain" || mod == "learnedw") { try { OnePieceTcg.Engine.Bot.Search.Evaluation.SetWeights(System.IO.File.ReadAllText("fitted_w.txt").Split(' ').Select(double.Parse).ToArray()); Console.WriteLine("(loaded fitted eval weights)"); } catch (Exception ex) { Console.WriteLine("WARN: no fitted_w.txt: " + ex.Message); } }
        var reg = BuildRegistry(out _);
        var pool = reg.Ids.Where(i => !OnePieceTcg.Engine.CardData.StarterDecks.ContainsKey(i))
            .Where(id => { var d = reg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; }).ToList();
        var rng = new System.Random(mseed); int aWins = 0, done = 0; var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int m = 0; m < mpairs; m++)
        {
            string dS = pool[rng.Next(pool.Count)], dN = pool[rng.Next(pool.Count)];
            foreach (bool aSouth in new[] { true, false })
                foreach (bool fSouth in new[] { true, false })
                {
                    var south = aSouth ? (OnePieceTcg.Sim.IAgent)new OnePieceTcg.Sim.ShippedAdvancedAgent() : OnePieceTcg.Sim.Learning.BotTiers.Make("intermediate");
                    var north = aSouth ? OnePieceTcg.Sim.Learning.BotTiers.Make("intermediate") : (OnePieceTcg.Sim.IAgent)new OnePieceTcg.Sim.ShippedAdvancedAgent();
                    var rec = OnePieceTcg.Sim.MatchDriver.Play(south, north, reg.Resolve(aSouth ? dS : dN), reg.Resolve(aSouth ? dN : dS),
                        $"moduleab:{mseed}:{m}:{aSouth}:{fSouth}", fSouth ? "south" : "north",
                        new OnePieceTcg.Sim.MatchDriver.Options { CommandCap = 20000 });
                    string aSeat = aSouth ? "south" : "north";
                    if (rec.winner != null) { done++; if (rec.winner == aSeat) aWins++; }
                }
        }
        OnePieceTcg.Engine.Bot.Search.AdvancedContractBot.SkipActivation = OnePieceTcg.Engine.Bot.Search.AdvancedContractBot.SkipPressure =
            OnePieceTcg.Engine.Bot.Search.AdvancedContractBot.SkipSearch = OnePieceTcg.Engine.Bot.Search.AdvancedContractBot.SkipTriggerUtility =
            OnePieceTcg.Engine.Bot.Search.AdvancedContractBot.SearchCleanMain = false;
        OnePieceTcg.Engine.Bot.Search.AdvancedActivationPolicy.ActivationValueGate = false;
        OnePieceTcg.Engine.Bot.Search.SearchBot.StrongRollout = false;
        OnePieceTcg.Engine.Bot.Search.AdvancedContractBot.EvalGreedyMain = false;
        double pp2 = done > 0 ? (double)aWins / done : 0, z = 1.96, z2 = z * z, N = System.Math.Max(1, done);
        double denom = 1 + z2 / N, center = (pp2 + z2 / (2 * N)) / denom, half = z * System.Math.Sqrt(pp2 * (1 - pp2) / N + z2 / (4 * N * N)) / denom;
        Console.WriteLine($"advanced(skip={mod}) vs intermediate: {aWins}/{done} = {100 * pp2:F1}%  (95% CI [{100 * (center - half):F1}%, {100 * (center + half):F1}%])  {mpairs}pairs×4, {sw.Elapsed.TotalSeconds:F0}s, seed={mseed}");
        return 0;
    }

    case "abtest":
    {
        // PAIRED A/B: tierA vs tierB, each random deck pair played in BOTH seat orientations (cancels deck+seat
        // bias), first-player alternated across pairs. Reports tierA's win rate vs tierB with a Wilson 95% CI.
        // Usage: abtest <tierA> <tierB> [pairs] [seed]  (total games = 2*pairs). This is the trustworthy harness.
        string tA = args.Length > 1 ? args[1] : "shipped-advanced";
        string tB = args.Length > 2 ? args[2] : "intermediate";
        int pairs = args.Length > 3 && int.TryParse(args[3], out var pp) ? pp : 100;
        int seed = args.Length > 4 && int.TryParse(args[4], out var sd) ? sd : 3;
        var reg = BuildRegistry(out _);
        var pool = reg.Ids.Where(i => !OnePieceTcg.Engine.CardData.StarterDecks.ContainsKey(i))
            .Where(id => { var d = reg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; }).ToList();
        var rng = new System.Random(seed);
        int aWins = 0, done = 0, unfinished = 0; var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int m = 0; m < pairs; m++)
        {
            string d1 = pool[rng.Next(pool.Count)], d2 = pool[rng.Next(pool.Count)];
            string first = m % 2 == 0 ? "south" : "north";
            for (int orient = 0; orient < 2; orient++)   // orient 0: A=south; orient 1: A=north (same decks)
            {
                bool aSouth = orient == 0;
                var south = OnePieceTcg.Sim.Learning.BotTiers.Make(aSouth ? tA : tB);
                var north = OnePieceTcg.Sim.Learning.BotTiers.Make(aSouth ? tB : tA);
                var rec = OnePieceTcg.Sim.MatchDriver.Play(south, north,
                    reg.Resolve(aSouth ? d1 : d2), reg.Resolve(aSouth ? d2 : d1), $"abtest:{seed}:{m}:{orient}", first,
                    new OnePieceTcg.Sim.MatchDriver.Options { CommandCap = 20000 });
                string aSeat = aSouth ? "south" : "north";
                if (rec.winner != null) { done++; if (rec.winner == aSeat) aWins++; } else unfinished++;
            }
        }
        double p = done > 0 ? (double)aWins / done : 0, z = 1.96, z2 = z * z, N = System.Math.Max(1, done);
        double denom = 1 + z2 / N, center = (p + z2 / (2 * N)) / denom;
        double half = z * System.Math.Sqrt(p * (1 - p) / N + z2 / (4 * N * N)) / denom;
        Console.WriteLine($"{tA} vs {tB}: {aWins}/{done} = {100 * p:F1}%  (95% CI [{100 * (center - half):F1}%, {100 * (center + half):F1}%])  " +
                          $"unfinished={unfinished}  {pairs} pairs×2, {sw.Elapsed.TotalSeconds:F0}s, seed={seed}");
        return 0;
    }

    case "advdiag":
    {
        // advdiag [n] [cap] [seed] → play shipped-advanced (south) vs intermediate over meta pairs, ONE game at
        // a time with per-game timing + command count, flushing each line so a HANG shows up immediately (the
        // culprit game prints commands≈cap and a long wall time). Lower cap keeps a hung game from grinding.
        int n = args.Length > 1 && int.TryParse(args[1], out var nn) ? nn : 40;
        int cap = args.Length > 2 && int.TryParse(args[2], out var cc) ? cc : 4000;
        int seed = args.Length > 3 && int.TryParse(args[3], out var ss) ? ss : 3;
        var reg = BuildRegistry(out _);
        var pool = reg.Ids.Where(i => !OnePieceTcg.Engine.CardData.StarterDecks.ContainsKey(i))
            .Where(id => { var d = reg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; }).ToList();
        var rng = new System.Random(seed);
        Console.WriteLine($"advdiag: shipped-advanced vs intermediate, n={n} cap={cap} seed={seed}, pool={pool.Count}");
        for (int i = 0; i < n; i++)
        {
            string dA = pool[rng.Next(pool.Count)], dB = pool[rng.Next(pool.Count)];
            string first = i % 2 == 0 ? "south" : "north";
            var gsw = System.Diagnostics.Stopwatch.StartNew();
            var rec = OnePieceTcg.Sim.MatchDriver.Play(
                new OnePieceTcg.Sim.ShippedAdvancedAgent(), OnePieceTcg.Sim.Learning.BotTiers.Make("intermediate"),
                reg.Resolve(dA), reg.Resolve(dB), $"advdiag:{i}", first,
                new OnePieceTcg.Sim.MatchDriver.Options { CommandCap = cap });
            gsw.Stop();
            string flag = (rec.commands >= cap || gsw.Elapsed.TotalSeconds > 8) ? "  <<< SLOW/HANG" : "";
            Console.WriteLine($"  g{i,-3} {gsw.Elapsed.TotalSeconds,6:F1}s cmds={rec.commands,-6} end={rec.end,-7} " +
                              $"win={rec.winner ?? "-"} first={first}  A={dA} B={dB}{flag}");
            Console.Out.Flush();
        }
        return 0;
    }

    case "advtrace":
    {
        // advtrace <deckA> <deckB> <first> [seed] [dumpAfter] → play shipped-advanced(south)=deckA vs
        // intermediate(north)=deckB; after `dumpAfter` commands, log each command + state context so a LOOP
        // is visible. Reproduces a hang found by advdiag.
        string dA = args[1], dB = args[2], first = args.Length > 3 ? args[3] : "south";
        string seedStr = args.Length > 4 ? args[4] : "advtrace";
        int dumpAfter = args.Length > 5 && int.TryParse(args[5], out var da) ? da : 250;
        var reg = BuildRegistry(out _);
        var st = OnePieceTcg.Engine.GameEngine.CreateMatch(new OnePieceTcg.Engine.MatchConfig
        { SouthDeckDef = reg.Resolve(dA), NorthDeckDef = reg.Resolve(dB), Seed = seedStr });
        // Force turn order like MatchDriver.
        if (st.Status == "coinflip")
        {
            string w = st.CoinFlipWinner;
            OnePieceTcg.Engine.GameEngine.ApplyCommand(st, new OnePieceTcg.Engine.GameCommand
            { Type = "chooseTurnOrder", Seat = w, GoingFirst = w == first });
        }
        var south = new OnePieceTcg.Sim.ShippedAdvancedAgent();
        var north = OnePieceTcg.Sim.Learning.BotTiers.Make("intermediate");
        int total = 0, cap = 700;
        var counts = new Dictionary<string, int>();
        while (st.Status != "finished" && total < cap)
        {
            foreach (var (agent, seat) in new[] { ((OnePieceTcg.Sim.IAgent)south, "south"), (north, "north") })
            {
                var bl = new HashSet<string>();
                for (int i = 0; i < 60 && st.Status != "finished"; i++)
                {
                    var cmd = agent.Decide(st, seat, bl);
                    if (cmd == null) break;
                    if (total >= dumpAfter)
                    {
                        var pe = st.PendingEffects.Count > 0 ? st.PendingEffects[0] : null;
                        string peText = pe?.Text == null ? "" : (pe.Text.Length > 40 ? pe.Text.Substring(0, 40) : pe.Text);
                        string ctx = $"phase={st.Phase} turn={st.TurnNumber} pend={st.PendingEffects.Count}" +
                            (pe != null ? $" pe0='{peText}' zone={pe.TargetZone} donRem={pe.DonPaymentRemaining} selRem={pe.SelectionsRemaining}" : "") +
                            (st.Battle != null ? $" battle={st.Battle.Step}" : "") +
                            (st.DeckLook != null ? $" deckLook={st.DeckLook.Seat}/{st.DeckLook.Step}/{st.DeckLook.Cards.Count}" : "");
                        Console.WriteLine($"  [{total}] {seat} {cmd.Type} tgt={cmd.Target ?? cmd.InstanceId ?? cmd.Attacker ?? "-"} | {ctx}");
                        string key = $"{seat}:{cmd.Type}";
                        counts[key] = counts.GetValueOrDefault(key) + 1;
                    }
                    object before = OnePieceTcg.Engine.Bot.IntermediateBot.SnapshotFor(st, cmd);
                    OnePieceTcg.Engine.GameEngine.ApplyCommand(st, cmd);
                    total++;
                    if (!OnePieceTcg.Engine.Bot.IntermediateBot.Succeeded(st, cmd, before))
                        bl.Add(OnePieceTcg.Engine.Bot.IntermediateBot.Signature(cmd));
                    if (total >= cap) break;
                }
                if (total >= cap) break;
            }
        }
        Console.WriteLine($"END status={st.Status} total={total}");
        foreach (var kv in counts.OrderByDescending(k => k.Value).Take(8)) Console.WriteLine($"   {kv.Key} x{kv.Value}");
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

    case "donab":
    {
        // Head-to-head A/B: both seats run the baseline (Intermediate) bot, but one seat uses the
        // OLD "front-load all DON!! before attacking" policy (LegacyDonSeat) and the other the NEW
        // "one attacker at a time" policy. Alternate which seat is NEW + who goes first to cancel
        // seat / first-player bias. Reports the NEW policy's win rate vs the OLD.
        var reg = BuildRegistry(out _);
        int games = args.Length > 1 && int.TryParse(args[1], out var gg) ? gg : 600;
        var metaIds = reg.Ids.Where(i => !OnePieceTcg.Engine.CardData.StarterDecks.ContainsKey(i)).ToList();
        var pool = metaIds.Where(id => { var d = reg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; }).ToList();
        int newWins = 0, done = 0;
        var rng = new System.Random(11);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < games; i++)
        {
            string dA = pool[rng.Next(pool.Count)], dB = pool[rng.Next(pool.Count)];
            bool southIsNew = i % 2 == 0;
            string newSeat = southIsNew ? "south" : "north";
            OnePieceTcg.Engine.Bot.IntermediateBot.LegacyDonSeat = southIsNew ? "north" : "south";
            string first = (i / 2) % 2 == 0 ? "south" : "north";
            var rec = OnePieceTcg.Sim.MatchDriver.Play(
                new OnePieceTcg.Sim.BaselineAgent(), new OnePieceTcg.Sim.BaselineAgent(),
                reg.Resolve(dA), reg.Resolve(dB), $"donab:{i}", first,
                new OnePieceTcg.Sim.MatchDriver.Options { CommandCap = 20000 });
            if (rec.winner != null) { done++; if (rec.winner == newSeat) newWins++; }
        }
        OnePieceTcg.Engine.Bot.IntermediateBot.LegacyDonSeat = null;
        double pct = done > 0 ? 100.0 * newWins / done : 0;
        double se = done > 0 ? 100.0 * System.Math.Sqrt(pct / 100 * (1 - pct / 100) / done) : 0;
        Console.WriteLine($"NEW interleaved-DON vs OLD front-load: {newWins}/{done} = {pct:F1}%  (95% CI ±{1.96 * se:F1})  {pool.Count} meta decks, {sw.Elapsed.TotalSeconds:F0}s");
        return 0;
    }

    case "counterab":
    {
        // A/B: one seat over-loads Leader swings to +2000 (force two counters) vs baseline "just
        // enough to connect". Alternate which seat is the variant + first player to cancel bias.
        var reg = BuildRegistry(out _);
        int games = args.Length > 1 && int.TryParse(args[1], out var gg) ? gg : 5000;
        var metaIds = reg.Ids.Where(i => !OnePieceTcg.Engine.CardData.StarterDecks.ContainsKey(i)).ToList();
        var pool = metaIds.Where(id => { var d = reg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; }).ToList();
        int varWins = 0, done = 0;
        var rng = new System.Random(13);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < games; i++)
        {
            string dA = pool[rng.Next(pool.Count)], dB = pool[rng.Next(pool.Count)];
            bool southIsVar = i % 2 == 0;
            string varSeat = southIsVar ? "south" : "north";
            OnePieceTcg.Engine.Bot.IntermediateBot.ForceCounterPressureSeat = varSeat;
            string first = (i / 2) % 2 == 0 ? "south" : "north";
            var rec = OnePieceTcg.Sim.MatchDriver.Play(
                new OnePieceTcg.Sim.BaselineAgent(), new OnePieceTcg.Sim.BaselineAgent(),
                reg.Resolve(dA), reg.Resolve(dB), $"cab:{i}", first,
                new OnePieceTcg.Sim.MatchDriver.Options { CommandCap = 20000 });
            if (rec.winner != null) { done++; if (rec.winner == varSeat) varWins++; }
        }
        OnePieceTcg.Engine.Bot.IntermediateBot.ForceCounterPressureSeat = null;
        double pct = done > 0 ? 100.0 * varWins / done : 0;
        double se = done > 0 ? 100.0 * System.Math.Sqrt(pct / 100 * (1 - pct / 100) / done) : 0;
        Console.WriteLine($"Force-2-counters (Leader +2000) vs baseline: {varWins}/{done} = {pct:F1}%  (95% CI ±{1.96 * se:F1})  {pool.Count} meta decks, {sw.Elapsed.TotalSeconds:F0}s");
        return 0;
    }

    case "defab":
    {
        // A/B: one seat starts defending its Leader (countering) at a DIFFERENT Life threshold
        // (arg 2, default 2) vs the baseline's 3. Alternate variant seat + first player.
        var reg = BuildRegistry(out _);
        int games = args.Length > 1 && int.TryParse(args[1], out var gg) ? gg : 5000;
        int thr = args.Length > 2 && int.TryParse(args[2], out var tt) ? tt : 2;
        var metaIds = reg.Ids.Where(i => !OnePieceTcg.Engine.CardData.StarterDecks.ContainsKey(i)).ToList();
        var pool = metaIds.Where(id => { var d = reg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; }).ToList();
        int varWins = 0, done = 0;
        var rng = new System.Random(17);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        OnePieceTcg.Engine.Bot.IntermediateBot.DefenseLifeThreshold = thr;
        for (int i = 0; i < games; i++)
        {
            string dA = pool[rng.Next(pool.Count)], dB = pool[rng.Next(pool.Count)];
            bool southIsVar = i % 2 == 0;
            string varSeat = southIsVar ? "south" : "north";
            OnePieceTcg.Engine.Bot.IntermediateBot.DefenseVariantSeat = varSeat;
            string first = (i / 2) % 2 == 0 ? "south" : "north";
            var rec = OnePieceTcg.Sim.MatchDriver.Play(
                new OnePieceTcg.Sim.BaselineAgent(), new OnePieceTcg.Sim.BaselineAgent(),
                reg.Resolve(dA), reg.Resolve(dB), $"dab:{i}", first,
                new OnePieceTcg.Sim.MatchDriver.Options { CommandCap = 20000 });
            if (rec.winner != null) { done++; if (rec.winner == varSeat) varWins++; }
        }
        OnePieceTcg.Engine.Bot.IntermediateBot.DefenseVariantSeat = null;
        OnePieceTcg.Engine.Bot.IntermediateBot.DefenseLifeThreshold = 3;
        double pct = done > 0 ? 100.0 * varWins / done : 0;
        double se = done > 0 ? 100.0 * System.Math.Sqrt(pct / 100 * (1 - pct / 100) / done) : 0;
        Console.WriteLine($"Leader-defense threshold {thr} vs baseline 3: {varWins}/{done} = {pct:F1}%  (95% CI ±{1.96 * se:F1})  {pool.Count} meta decks, {sw.Elapsed.TotalSeconds:F0}s");
        return 0;
    }

    case "blockab":
    {
        // A/B: one seat DECLINES to block a Leader hit while Life > threshold (arg 2, default 2) —
        // taking the hit for the Life->hand refill + keeping the Blocker active — vs the baseline
        // that always blocks with a surviving Blocker. Alternate variant seat + first player.
        var reg = BuildRegistry(out _);
        int games = args.Length > 1 && int.TryParse(args[1], out var gg) ? gg : 5000;
        int thr = args.Length > 2 && int.TryParse(args[2], out var tt) ? tt : 2;
        var metaIds = reg.Ids.Where(i => !OnePieceTcg.Engine.CardData.StarterDecks.ContainsKey(i)).ToList();
        var pool = metaIds.Where(id => { var d = reg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; }).ToList();
        int varWins = 0, done = 0;
        var rng = new System.Random(19);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        OnePieceTcg.Engine.Bot.IntermediateBot.BlockLifeThreshold = thr;
        for (int i = 0; i < games; i++)
        {
            string dA = pool[rng.Next(pool.Count)], dB = pool[rng.Next(pool.Count)];
            bool southIsVar = i % 2 == 0;
            string varSeat = southIsVar ? "south" : "north";
            OnePieceTcg.Engine.Bot.IntermediateBot.BlockVariantSeat = varSeat;
            string first = (i / 2) % 2 == 0 ? "south" : "north";
            var rec = OnePieceTcg.Sim.MatchDriver.Play(
                new OnePieceTcg.Sim.BaselineAgent(), new OnePieceTcg.Sim.BaselineAgent(),
                reg.Resolve(dA), reg.Resolve(dB), $"bab:{i}", first,
                new OnePieceTcg.Sim.MatchDriver.Options { CommandCap = 20000 });
            if (rec.winner != null) { done++; if (rec.winner == varSeat) varWins++; }
        }
        OnePieceTcg.Engine.Bot.IntermediateBot.BlockVariantSeat = null;
        OnePieceTcg.Engine.Bot.IntermediateBot.BlockLifeThreshold = 2;
        double pct = done > 0 ? 100.0 * varWins / done : 0;
        double se = done > 0 ? 100.0 * System.Math.Sqrt(pct / 100 * (1 - pct / 100) / done) : 0;
        Console.WriteLine($"Take-Leader-hit-above-Life-{thr} vs always-block: {varWins}/{done} = {pct:F1}%  (95% CI ±{1.96 * se:F1})  {pool.Count} meta decks, {sw.Elapsed.TotalSeconds:F0}s");
        return 0;
    }

    case "chardefab":
    {
        // A/B: one seat uses a different min Character cost worth saving with counters (arg 2,
        // default 3) vs baseline 5. Leader Life threshold stays at baseline 3.
        var reg = BuildRegistry(out _);
        int games = args.Length > 1 && int.TryParse(args[1], out var gg) ? gg : 5000;
        int cost = args.Length > 2 && int.TryParse(args[2], out var cc) ? cc : 3;
        var metaIds = reg.Ids.Where(i => !OnePieceTcg.Engine.CardData.StarterDecks.ContainsKey(i)).ToList();
        var pool = metaIds.Where(id => { var d = reg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; }).ToList();
        int varWins = 0, done = 0;
        var rng = new System.Random(23);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        OnePieceTcg.Engine.Bot.IntermediateBot.CharDefenseCostThreshold = cost;
        for (int i = 0; i < games; i++)
        {
            string dA = pool[rng.Next(pool.Count)], dB = pool[rng.Next(pool.Count)];
            bool southIsVar = i % 2 == 0;
            string varSeat = southIsVar ? "south" : "north";
            OnePieceTcg.Engine.Bot.IntermediateBot.DefenseVariantSeat = varSeat;
            string first = (i / 2) % 2 == 0 ? "south" : "north";
            var rec = OnePieceTcg.Sim.MatchDriver.Play(
                new OnePieceTcg.Sim.BaselineAgent(), new OnePieceTcg.Sim.BaselineAgent(),
                reg.Resolve(dA), reg.Resolve(dB), $"cd:{i}", first,
                new OnePieceTcg.Sim.MatchDriver.Options { CommandCap = 20000 });
            if (rec.winner != null) { done++; if (rec.winner == varSeat) varWins++; }
        }
        OnePieceTcg.Engine.Bot.IntermediateBot.DefenseVariantSeat = null;
        OnePieceTcg.Engine.Bot.IntermediateBot.CharDefenseCostThreshold = 5;
        double pct = done > 0 ? 100.0 * varWins / done : 0;
        double se = done > 0 ? 100.0 * System.Math.Sqrt(pct / 100 * (1 - pct / 100) / done) : 0;
        Console.WriteLine($"Char-save cost>={cost} vs baseline>=5: {varWins}/{done} = {pct:F1}%  (95% CI ±{1.96 * se:F1})  {pool.Count} meta decks, {sw.Elapsed.TotalSeconds:F0}s");
        return 0;
    }

    case "holdab":
    {
        // A/B: one seat holds a big-counter (>=2000) Character in hand once its board is developed
        // (>=3 Characters) vs the baseline that always deploys. Alternate variant seat + first player.
        var reg = BuildRegistry(out _);
        int games = args.Length > 1 && int.TryParse(args[1], out var gg) ? gg : 6000;
        var metaIds = reg.Ids.Where(i => !OnePieceTcg.Engine.CardData.StarterDecks.ContainsKey(i)).ToList();
        var pool = metaIds.Where(id => { var d = reg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; }).ToList();
        int varWins = 0, done = 0;
        var rng = new System.Random(29);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < games; i++)
        {
            string dA = pool[rng.Next(pool.Count)], dB = pool[rng.Next(pool.Count)];
            bool southIsVar = i % 2 == 0;
            string varSeat = southIsVar ? "south" : "north";
            OnePieceTcg.Engine.Bot.IntermediateBot.HoldCounterVariantSeat = varSeat;
            string first = (i / 2) % 2 == 0 ? "south" : "north";
            var rec = OnePieceTcg.Sim.MatchDriver.Play(
                new OnePieceTcg.Sim.BaselineAgent(), new OnePieceTcg.Sim.BaselineAgent(),
                reg.Resolve(dA), reg.Resolve(dB), $"hold:{i}", first,
                new OnePieceTcg.Sim.MatchDriver.Options { CommandCap = 20000 });
            if (rec.winner != null) { done++; if (rec.winner == varSeat) varWins++; }
        }
        OnePieceTcg.Engine.Bot.IntermediateBot.HoldCounterVariantSeat = null;
        double pct = done > 0 ? 100.0 * varWins / done : 0;
        double se = done > 0 ? 100.0 * System.Math.Sqrt(pct / 100 * (1 - pct / 100) / done) : 0;
        Console.WriteLine($"Hold big-counter char (board>=3) vs always-deploy: {varWins}/{done} = {pct:F1}%  (95% CI ±{1.96 * se:F1})  {pool.Count} meta decks, {sw.Elapsed.TotalSeconds:F0}s");
        return 0;
    }

    case "holdtuneab":
    {
        // A/B: one seat uses tuned hold thresholds (arg2 board size, arg3 counter value) vs the
        // shipped 3 / 2000. Both seats still HOLD (this measures the tuning, not hold-vs-deploy).
        var reg = BuildRegistry(out _);
        int games = args.Length > 1 && int.TryParse(args[1], out var gg) ? gg : 6000;
        int bthr = args.Length > 2 && int.TryParse(args[2], out var bb) ? bb : 2;
        int cthr = args.Length > 3 && int.TryParse(args[3], out var cc) ? cc : 2000;
        var metaIds = reg.Ids.Where(i => !OnePieceTcg.Engine.CardData.StarterDecks.ContainsKey(i)).ToList();
        var pool = metaIds.Where(id => { var d = reg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; }).ToList();
        int varWins = 0, done = 0;
        var rng = new System.Random(31);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        OnePieceTcg.Engine.Bot.IntermediateBot.HoldBoardThreshold = bthr;
        OnePieceTcg.Engine.Bot.IntermediateBot.HoldCounterThreshold = cthr;
        for (int i = 0; i < games; i++)
        {
            string dA = pool[rng.Next(pool.Count)], dB = pool[rng.Next(pool.Count)];
            bool southIsVar = i % 2 == 0;
            string varSeat = southIsVar ? "south" : "north";
            OnePieceTcg.Engine.Bot.IntermediateBot.HoldTuneSeat = varSeat;
            string first = (i / 2) % 2 == 0 ? "south" : "north";
            var rec = OnePieceTcg.Sim.MatchDriver.Play(
                new OnePieceTcg.Sim.BaselineAgent(), new OnePieceTcg.Sim.BaselineAgent(),
                reg.Resolve(dA), reg.Resolve(dB), $"ht:{i}", first,
                new OnePieceTcg.Sim.MatchDriver.Options { CommandCap = 20000 });
            if (rec.winner != null) { done++; if (rec.winner == varSeat) varWins++; }
        }
        OnePieceTcg.Engine.Bot.IntermediateBot.HoldTuneSeat = null;
        OnePieceTcg.Engine.Bot.IntermediateBot.HoldBoardThreshold = 3;
        OnePieceTcg.Engine.Bot.IntermediateBot.HoldCounterThreshold = 2000;
        double pct = done > 0 ? 100.0 * varWins / done : 0;
        double se = done > 0 ? 100.0 * System.Math.Sqrt(pct / 100 * (1 - pct / 100) / done) : 0;
        Console.WriteLine($"Hold-tune board>={bthr}/cp>={cthr} vs shipped 3/2000: {varWins}/{done} = {pct:F1}%  (95% CI ±{1.96 * se:F1})  {pool.Count} meta decks, {sw.Elapsed.TotalSeconds:F0}s");
        return 0;
    }

    case "attackorderab":
    {
        // A/B: one seat sequences attackers WEAKEST-relevant first (drain counters, save the big hitter)
        // vs the shipped strongest-first.
        var reg = BuildRegistry(out _);
        int games = args.Length > 1 && int.TryParse(args[1], out var gg) ? gg : 6000;
        var metaIds = reg.Ids.Where(i => !OnePieceTcg.Engine.CardData.StarterDecks.ContainsKey(i)).ToList();
        var pool = metaIds.Where(id => { var d = reg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; }).ToList();
        int varWins = 0, done = 0;
        var rng = new System.Random(53);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < games; i++)
        {
            string dA = pool[rng.Next(pool.Count)], dB = pool[rng.Next(pool.Count)];
            bool southIsVar = i % 2 == 0;
            string varSeat = southIsVar ? "south" : "north";
            OnePieceTcg.Engine.Bot.IntermediateBot.WeakestFirstSeat = varSeat;
            string first = (i / 2) % 2 == 0 ? "south" : "north";
            var rec = OnePieceTcg.Sim.MatchDriver.Play(
                new OnePieceTcg.Sim.BaselineAgent(), new OnePieceTcg.Sim.BaselineAgent(),
                reg.Resolve(dA), reg.Resolve(dB), $"ao:{i}", first,
                new OnePieceTcg.Sim.MatchDriver.Options { CommandCap = 20000 });
            if (rec.winner != null) { done++; if (rec.winner == varSeat) varWins++; }
        }
        OnePieceTcg.Engine.Bot.IntermediateBot.WeakestFirstSeat = null;
        double pct = done > 0 ? 100.0 * varWins / done : 0;
        double se = done > 0 ? 100.0 * System.Math.Sqrt(pct / 100 * (1 - pct / 100) / done) : 0;
        Console.WriteLine($"Weakest-first attack order vs strongest-first: {varWins}/{done} = {pct:F1}%  (95% CI ±{1.96 * se:F1})  {pool.Count} meta decks, {sw.Elapsed.TotalSeconds:F0}s");
        return 0;
    }

    case "attackkofloorab":
    {
        // A/B: varSeat uses the SHIPPED Life-pressure floor (optional arg2 overrides it); the OTHER seat
        // reverts to legacy KO-everything (floor 0). Reports the shipped seat's win% vs the legacy seat.
        var reg = BuildRegistry(out _);
        int games = args.Length > 1 && int.TryParse(args[1], out var gg) ? gg : 6000;
        if (args.Length > 2 && int.TryParse(args[2], out var ff)) OnePieceTcg.Engine.Bot.IntermediateBot.KoValueFloor = ff;
        int floor = OnePieceTcg.Engine.Bot.IntermediateBot.KoValueFloor;
        var metaIds = reg.Ids.Where(i => !OnePieceTcg.Engine.CardData.StarterDecks.ContainsKey(i)).ToList();
        var pool = metaIds.Where(id => { var d = reg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; }).ToList();
        int varWins = 0, done = 0;
        var rng = new System.Random(67);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < games; i++)
        {
            string dA = pool[rng.Next(pool.Count)], dB = pool[rng.Next(pool.Count)];
            bool southIsVar = i % 2 == 0;
            string varSeat = southIsVar ? "south" : "north";
            OnePieceTcg.Engine.Bot.IntermediateBot.LegacyKoSeat = southIsVar ? "north" : "south";  // the OTHER seat is legacy
            string first = (i / 2) % 2 == 0 ? "south" : "north";
            var rec = OnePieceTcg.Sim.MatchDriver.Play(
                new OnePieceTcg.Sim.BaselineAgent(), new OnePieceTcg.Sim.BaselineAgent(),
                reg.Resolve(dA), reg.Resolve(dB), $"kf:{i}", first,
                new OnePieceTcg.Sim.MatchDriver.Options { CommandCap = 20000 });
            if (rec.winner != null) { done++; if (rec.winner == varSeat) varWins++; }
        }
        OnePieceTcg.Engine.Bot.IntermediateBot.LegacyKoSeat = null;
        OnePieceTcg.Engine.Bot.IntermediateBot.KoValueFloor = 25000;   // shipped default (iter 19 raise)
        double pct = done > 0 ? 100.0 * varWins / done : 0;
        double se = done > 0 ? 100.0 * System.Math.Sqrt(pct / 100 * (1 - pct / 100) / done) : 0;
        Console.WriteLine($"SHIPPED Life-pressure floor {floor} vs legacy KO-everything: {varWins}/{done} = {pct:F1}%  (95% CI ±{1.96 * se:F1})  {pool.Count} meta decks, {sw.Elapsed.TotalSeconds:F0}s");
        return 0;
    }

    case "beginnerhandicapab":
    {
        // Verify the difficulty ladder: BEGINNER seat (resource-discipline wins reverted, keeps Life-pressure)
        // vs INTERMEDIATE seat (full-strength core). Beginner should win clearly < 50% (Intermediate stronger).
        var reg = BuildRegistry(out _);
        int games = args.Length > 1 && int.TryParse(args[1], out var gg) ? gg : 12000;
        var metaIds = reg.Ids.Where(i => !OnePieceTcg.Engine.CardData.StarterDecks.ContainsKey(i)).ToList();
        var pool = metaIds.Where(id => { var d = reg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; }).ToList();
        int begWins = 0, done = 0;
        var rng = new System.Random(271);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < games; i++)
        {
            string dA = pool[rng.Next(pool.Count)], dB = pool[rng.Next(pool.Count)];
            bool southIsBeg = i % 2 == 0;
            string begSeat = southIsBeg ? "south" : "north";
            OnePieceTcg.Engine.Bot.IntermediateBot.LegacyDonSeat = begSeat;
            OnePieceTcg.Engine.Bot.IntermediateBot.HoldCounterVariantSeat = begSeat;
            OnePieceTcg.Engine.Bot.IntermediateBot.LegacyStackCharCounterSeat = begSeat;
            OnePieceTcg.Engine.Bot.IntermediateBot.LegacyStackLeaderCounterSeat = begSeat;
            OnePieceTcg.Engine.Bot.IntermediateBot.LegacyMulliganSeat = begSeat;
            string first = (i / 2) % 2 == 0 ? "south" : "north";
            var rec = OnePieceTcg.Sim.MatchDriver.Play(
                new OnePieceTcg.Sim.BaselineAgent(), new OnePieceTcg.Sim.BaselineAgent(),
                reg.Resolve(dA), reg.Resolve(dB), $"bh:{i}", first,
                new OnePieceTcg.Sim.MatchDriver.Options { CommandCap = 20000 });
            if (rec.winner != null) { done++; if (rec.winner == begSeat) begWins++; }
        }
        OnePieceTcg.Engine.Bot.IntermediateBot.LegacyDonSeat = null;
        OnePieceTcg.Engine.Bot.IntermediateBot.HoldCounterVariantSeat = null;
        OnePieceTcg.Engine.Bot.IntermediateBot.LegacyStackCharCounterSeat = null;
        OnePieceTcg.Engine.Bot.IntermediateBot.LegacyStackLeaderCounterSeat = null;
        OnePieceTcg.Engine.Bot.IntermediateBot.LegacyMulliganSeat = null;
        double pct = done > 0 ? 100.0 * begWins / done : 0;
        double se = done > 0 ? 100.0 * System.Math.Sqrt(pct / 100 * (1 - pct / 100) / done) : 0;
        Console.WriteLine($"BEGINNER (handicapped) vs INTERMEDIATE (full core): beginner wins {begWins}/{done} = {pct:F1}%  (95% CI ±{1.96 * se:F1})  => Intermediate {100-pct:F1}%.  {pool.Count} meta decks, {sw.Elapsed.TotalSeconds:F0}s");
        return 0;
    }

    case "smartmulliganab":
    {
        // GLOBAL A/B: user's richer mulligan (position curve-coverage + searcher forgiveness).
        var reg = BuildRegistry(out _);
        int games = args.Length > 1 && int.TryParse(args[1], out var gg) ? gg : 12000;
        var metaIds = reg.Ids.Where(i => !OnePieceTcg.Engine.CardData.StarterDecks.ContainsKey(i)).ToList();
        var pool = metaIds.Where(id => { var d = reg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; }).ToList();
        int varWins = 0, done = 0;
        var rng = new System.Random(269);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < games; i++)
        {
            string dA = pool[rng.Next(pool.Count)], dB = pool[rng.Next(pool.Count)];
            bool southIsVar = i % 2 == 0;
            string varSeat = southIsVar ? "south" : "north";
            OnePieceTcg.Engine.Bot.IntermediateBot.LegacyMulliganSeat = southIsVar ? "north" : "south";  // OTHER seat = old rule
            string first = (i / 2) % 2 == 0 ? "south" : "north";
            var rec = OnePieceTcg.Sim.MatchDriver.Play(
                new OnePieceTcg.Sim.BaselineAgent(), new OnePieceTcg.Sim.BaselineAgent(),
                reg.Resolve(dA), reg.Resolve(dB), $"sm:{i}", first,
                new OnePieceTcg.Sim.MatchDriver.Options { CommandCap = 20000 });
            if (rec.winner != null) { done++; if (rec.winner == varSeat) varWins++; }
        }
        OnePieceTcg.Engine.Bot.IntermediateBot.LegacyMulliganSeat = null;
        double pct = done > 0 ? 100.0 * varWins / done : 0;
        double se = done > 0 ? 100.0 * System.Math.Sqrt(pct / 100 * (1 - pct / 100) / done) : 0;
        Console.WriteLine($"Smart mulligan (position curve + searcher) vs baseline: {varWins}/{done} = {pct:F1}%  (95% CI ±{1.96 * se:F1})  {pool.Count} meta decks, {sw.Elapsed.TotalSeconds:F0}s");
        return 0;
    }

    case "positionmulliganab":
    {
        // GLOBAL A/B: one seat uses a position-aware mulligan (tighter on the play, looser on the draw).
        var reg = BuildRegistry(out _);
        int games = args.Length > 1 && int.TryParse(args[1], out var gg) ? gg : 12000;
        var metaIds = reg.Ids.Where(i => !OnePieceTcg.Engine.CardData.StarterDecks.ContainsKey(i)).ToList();
        var pool = metaIds.Where(id => { var d = reg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; }).ToList();
        int varWins = 0, done = 0;
        var rng = new System.Random(263);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < games; i++)
        {
            string dA = pool[rng.Next(pool.Count)], dB = pool[rng.Next(pool.Count)];
            bool southIsVar = i % 2 == 0;
            string varSeat = southIsVar ? "south" : "north";
            OnePieceTcg.Engine.Bot.IntermediateBot.PositionMulliganSeat = varSeat;
            string first = (i / 2) % 2 == 0 ? "south" : "north";
            var rec = OnePieceTcg.Sim.MatchDriver.Play(
                new OnePieceTcg.Sim.BaselineAgent(), new OnePieceTcg.Sim.BaselineAgent(),
                reg.Resolve(dA), reg.Resolve(dB), $"pm:{i}", first,
                new OnePieceTcg.Sim.MatchDriver.Options { CommandCap = 20000 });
            if (rec.winner != null) { done++; if (rec.winner == varSeat) varWins++; }
        }
        OnePieceTcg.Engine.Bot.IntermediateBot.PositionMulliganSeat = null;
        double pct = done > 0 ? 100.0 * varWins / done : 0;
        double se = done > 0 ? 100.0 * System.Math.Sqrt(pct / 100 * (1 - pct / 100) / done) : 0;
        Console.WriteLine($"Position-aware mulligan vs baseline: {varWins}/{done} = {pct:F1}%  (95% CI ±{1.96 * se:F1})  {pool.Count} meta decks, {sw.Elapsed.TotalSeconds:F0}s");
        return 0;
    }

    case "savecounterforleaderab":
    {
        // GLOBAL A/B: one seat won't counter to save Characters while at Life <= 3 (saves counters for Leader).
        var reg = BuildRegistry(out _);
        int games = args.Length > 1 && int.TryParse(args[1], out var gg) ? gg : 12000;
        var metaIds = reg.Ids.Where(i => !OnePieceTcg.Engine.CardData.StarterDecks.ContainsKey(i)).ToList();
        var pool = metaIds.Where(id => { var d = reg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; }).ToList();
        int varWins = 0, done = 0;
        var rng = new System.Random(257);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < games; i++)
        {
            string dA = pool[rng.Next(pool.Count)], dB = pool[rng.Next(pool.Count)];
            bool southIsVar = i % 2 == 0;
            string varSeat = southIsVar ? "south" : "north";
            OnePieceTcg.Engine.Bot.IntermediateBot.SaveCounterForLeaderSeat = varSeat;
            string first = (i / 2) % 2 == 0 ? "south" : "north";
            var rec = OnePieceTcg.Sim.MatchDriver.Play(
                new OnePieceTcg.Sim.BaselineAgent(), new OnePieceTcg.Sim.BaselineAgent(),
                reg.Resolve(dA), reg.Resolve(dB), $"sc:{i}", first,
                new OnePieceTcg.Sim.MatchDriver.Options { CommandCap = 20000 });
            if (rec.winner != null) { done++; if (rec.winner == varSeat) varWins++; }
        }
        OnePieceTcg.Engine.Bot.IntermediateBot.SaveCounterForLeaderSeat = null;
        double pct = done > 0 ? 100.0 * varWins / done : 0;
        double se = done > 0 ? 100.0 * System.Math.Sqrt(pct / 100 * (1 - pct / 100) / done) : 0;
        Console.WriteLine($"Save-counter-for-Leader (no char counter at Life<=3) vs baseline: {varWins}/{done} = {pct:F1}%  (95% CI ±{1.96 * se:F1})  {pool.Count} meta decks, {sw.Elapsed.TotalSeconds:F0}s");
        return 0;
    }

    case "blockleaderonlyab":
    {
        // GLOBAL A/B: one seat only blocks Leader attacks (saves the Blocker; Characters take the hit).
        var reg = BuildRegistry(out _);
        int games = args.Length > 1 && int.TryParse(args[1], out var gg) ? gg : 12000;
        var metaIds = reg.Ids.Where(i => !OnePieceTcg.Engine.CardData.StarterDecks.ContainsKey(i)).ToList();
        var pool = metaIds.Where(id => { var d = reg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; }).ToList();
        int varWins = 0, done = 0;
        var rng = new System.Random(251);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < games; i++)
        {
            string dA = pool[rng.Next(pool.Count)], dB = pool[rng.Next(pool.Count)];
            bool southIsVar = i % 2 == 0;
            string varSeat = southIsVar ? "south" : "north";
            OnePieceTcg.Engine.Bot.IntermediateBot.BlockLeaderOnlySeat = varSeat;
            string first = (i / 2) % 2 == 0 ? "south" : "north";
            var rec = OnePieceTcg.Sim.MatchDriver.Play(
                new OnePieceTcg.Sim.BaselineAgent(), new OnePieceTcg.Sim.BaselineAgent(),
                reg.Resolve(dA), reg.Resolve(dB), $"bl:{i}", first,
                new OnePieceTcg.Sim.MatchDriver.Options { CommandCap = 20000 });
            if (rec.winner != null) { done++; if (rec.winner == varSeat) varWins++; }
        }
        OnePieceTcg.Engine.Bot.IntermediateBot.BlockLeaderOnlySeat = null;
        double pct = done > 0 ? 100.0 * varWins / done : 0;
        double se = done > 0 ? 100.0 * System.Math.Sqrt(pct / 100 * (1 - pct / 100) / done) : 0;
        Console.WriteLine($"Block-Leader-only (save blocker, char takes hit) vs baseline: {varWins}/{done} = {pct:F1}%  (95% CI ±{1.96 * se:F1})  {pool.Count} meta decks, {sw.Elapsed.TotalSeconds:F0}s");
        return 0;
    }

    case "leaderdeflifeab":
    {
        // GLOBAL A/B (post-no-stack): varSeat defends its Leader only at Life <= arg2 (take the hit above that
        // for the card); baseline uses shipped 3. Char-defense held at shipped 4 to isolate the Leader threshold.
        var reg = BuildRegistry(out _);
        int games = args.Length > 1 && int.TryParse(args[1], out var gg) ? gg : 12000;
        int thr = args.Length > 2 && int.TryParse(args[2], out var tt) ? tt : 2;
        var metaIds = reg.Ids.Where(i => !OnePieceTcg.Engine.CardData.StarterDecks.ContainsKey(i)).ToList();
        var pool = metaIds.Where(id => { var d = reg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; }).ToList();
        int varWins = 0, done = 0;
        var rng = new System.Random(241);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < games; i++)
        {
            string dA = pool[rng.Next(pool.Count)], dB = pool[rng.Next(pool.Count)];
            bool southIsVar = i % 2 == 0;
            string varSeat = southIsVar ? "south" : "north";
            OnePieceTcg.Engine.Bot.IntermediateBot.DefenseVariantSeat = varSeat;
            OnePieceTcg.Engine.Bot.IntermediateBot.DefenseLifeThreshold = thr;
            OnePieceTcg.Engine.Bot.IntermediateBot.CharDefenseCostThreshold = 4;
            string first = (i / 2) % 2 == 0 ? "south" : "north";
            var rec = OnePieceTcg.Sim.MatchDriver.Play(
                new OnePieceTcg.Sim.BaselineAgent(), new OnePieceTcg.Sim.BaselineAgent(),
                reg.Resolve(dA), reg.Resolve(dB), $"ld:{i}", first,
                new OnePieceTcg.Sim.MatchDriver.Options { CommandCap = 20000 });
            if (rec.winner != null) { done++; if (rec.winner == varSeat) varWins++; }
        }
        OnePieceTcg.Engine.Bot.IntermediateBot.DefenseVariantSeat = null;
        OnePieceTcg.Engine.Bot.IntermediateBot.DefenseLifeThreshold = 3;
        OnePieceTcg.Engine.Bot.IntermediateBot.CharDefenseCostThreshold = 5;
        double pct = done > 0 ? 100.0 * varWins / done : 0;
        double se = done > 0 ? 100.0 * System.Math.Sqrt(pct / 100 * (1 - pct / 100) / done) : 0;
        Console.WriteLine($"Leader-defense Life<={thr} (take hits above) vs shipped 3: {varWins}/{done} = {pct:F1}%  (95% CI ±{1.96 * se:F1})  {pool.Count} meta decks, {sw.Elapsed.TotalSeconds:F0}s");
        return 0;
    }

    case "cheapcountercharab":
    {
        // GLOBAL A/B: one seat saves a Character only with a +1000 counter (never a premium +2000).
        var reg = BuildRegistry(out _);
        int games = args.Length > 1 && int.TryParse(args[1], out var gg) ? gg : 12000;
        var metaIds = reg.Ids.Where(i => !OnePieceTcg.Engine.CardData.StarterDecks.ContainsKey(i)).ToList();
        var pool = metaIds.Where(id => { var d = reg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; }).ToList();
        int varWins = 0, done = 0;
        var rng = new System.Random(239);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < games; i++)
        {
            string dA = pool[rng.Next(pool.Count)], dB = pool[rng.Next(pool.Count)];
            bool southIsVar = i % 2 == 0;
            string varSeat = southIsVar ? "south" : "north";
            OnePieceTcg.Engine.Bot.IntermediateBot.CheapCounterCharOnlySeat = varSeat;
            string first = (i / 2) % 2 == 0 ? "south" : "north";
            var rec = OnePieceTcg.Sim.MatchDriver.Play(
                new OnePieceTcg.Sim.BaselineAgent(), new OnePieceTcg.Sim.BaselineAgent(),
                reg.Resolve(dA), reg.Resolve(dB), $"cc:{i}", first,
                new OnePieceTcg.Sim.MatchDriver.Options { CommandCap = 20000 });
            if (rec.winner != null) { done++; if (rec.winner == varSeat) varWins++; }
        }
        OnePieceTcg.Engine.Bot.IntermediateBot.CheapCounterCharOnlySeat = null;
        double pct = done > 0 ? 100.0 * varWins / done : 0;
        double se = done > 0 ? 100.0 * System.Math.Sqrt(pct / 100 * (1 - pct / 100) / done) : 0;
        Console.WriteLine($"Cheap-counter-char-only (+1000 or pass) vs baseline: {varWins}/{done} = {pct:F1}%  (95% CI ±{1.96 * se:F1})  {pool.Count} meta decks, {sw.Elapsed.TotalSeconds:F0}s");
        return 0;
    }

    case "nostackleadercounterab":
    {
        // GLOBAL A/B: one seat won't stack 2+ counters to save the Leader while Life >= 2 (buffer).
        var reg = BuildRegistry(out _);
        int games = args.Length > 1 && int.TryParse(args[1], out var gg) ? gg : 12000;
        var metaIds = reg.Ids.Where(i => !OnePieceTcg.Engine.CardData.StarterDecks.ContainsKey(i)).ToList();
        var pool = metaIds.Where(id => { var d = reg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; }).ToList();
        int varWins = 0, done = 0;
        var rng = new System.Random(233);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < games; i++)
        {
            string dA = pool[rng.Next(pool.Count)], dB = pool[rng.Next(pool.Count)];
            bool southIsVar = i % 2 == 0;
            string varSeat = southIsVar ? "south" : "north";
            OnePieceTcg.Engine.Bot.IntermediateBot.LegacyStackLeaderCounterSeat = southIsVar ? "north" : "south";  // OTHER seat = old always-stack
            string first = (i / 2) % 2 == 0 ? "south" : "north";
            var rec = OnePieceTcg.Sim.MatchDriver.Play(
                new OnePieceTcg.Sim.BaselineAgent(), new OnePieceTcg.Sim.BaselineAgent(),
                reg.Resolve(dA), reg.Resolve(dB), $"nl:{i}", first,
                new OnePieceTcg.Sim.MatchDriver.Options { CommandCap = 20000 });
            if (rec.winner != null) { done++; if (rec.winner == varSeat) varWins++; }
        }
        OnePieceTcg.Engine.Bot.IntermediateBot.LegacyStackLeaderCounterSeat = null;
        double pct = done > 0 ? 100.0 * varWins / done : 0;
        double se = done > 0 ? 100.0 * System.Math.Sqrt(pct / 100 * (1 - pct / 100) / done) : 0;
        Console.WriteLine($"No-stack Leader counter (Life>=2) vs baseline: {varWins}/{done} = {pct:F1}%  (95% CI ±{1.96 * se:F1})  {pool.Count} meta decks, {sw.Elapsed.TotalSeconds:F0}s");
        return 0;
    }

    case "nostackcharcounterab":
    {
        // GLOBAL A/B: one seat won't stack 2+ counters to save a Character (single-counter or pass).
        var reg = BuildRegistry(out _);
        int games = args.Length > 1 && int.TryParse(args[1], out var gg) ? gg : 6000;
        var metaIds = reg.Ids.Where(i => !OnePieceTcg.Engine.CardData.StarterDecks.ContainsKey(i)).ToList();
        var pool = metaIds.Where(id => { var d = reg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; }).ToList();
        int varWins = 0, done = 0;
        var rng = new System.Random(229);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < games; i++)
        {
            string dA = pool[rng.Next(pool.Count)], dB = pool[rng.Next(pool.Count)];
            bool southIsVar = i % 2 == 0;
            string varSeat = southIsVar ? "south" : "north";
            OnePieceTcg.Engine.Bot.IntermediateBot.LegacyStackCharCounterSeat = southIsVar ? "north" : "south";  // OTHER seat = old always-stack
            string first = (i / 2) % 2 == 0 ? "south" : "north";
            var rec = OnePieceTcg.Sim.MatchDriver.Play(
                new OnePieceTcg.Sim.BaselineAgent(), new OnePieceTcg.Sim.BaselineAgent(),
                reg.Resolve(dA), reg.Resolve(dB), $"nc:{i}", first,
                new OnePieceTcg.Sim.MatchDriver.Options { CommandCap = 20000 });
            if (rec.winner != null) { done++; if (rec.winner == varSeat) varWins++; }
        }
        OnePieceTcg.Engine.Bot.IntermediateBot.LegacyStackCharCounterSeat = null;
        double pct = done > 0 ? 100.0 * varWins / done : 0;
        double se = done > 0 ? 100.0 * System.Math.Sqrt(pct / 100 * (1 - pct / 100) / done) : 0;
        Console.WriteLine($"No-stack char counter (single or pass) vs baseline: {varWins}/{done} = {pct:F1}%  (95% CI ±{1.96 * se:F1})  {pool.Count} meta decks, {sw.Elapsed.TotalSeconds:F0}s");
        return 0;
    }

    case "attackfirstab":
    {
        // GLOBAL A/B: one seat attacks with its existing board BEFORE deploying new Characters.
        var reg = BuildRegistry(out _);
        int games = args.Length > 1 && int.TryParse(args[1], out var gg) ? gg : 6000;
        var metaIds = reg.Ids.Where(i => !OnePieceTcg.Engine.CardData.StarterDecks.ContainsKey(i)).ToList();
        var pool = metaIds.Where(id => { var d = reg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; }).ToList();
        int varWins = 0, done = 0;
        var rng = new System.Random(227);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < games; i++)
        {
            string dA = pool[rng.Next(pool.Count)], dB = pool[rng.Next(pool.Count)];
            bool southIsVar = i % 2 == 0;
            string varSeat = southIsVar ? "south" : "north";
            OnePieceTcg.Engine.Bot.IntermediateBot.AttackFirstSeat = varSeat;
            string first = (i / 2) % 2 == 0 ? "south" : "north";
            var rec = OnePieceTcg.Sim.MatchDriver.Play(
                new OnePieceTcg.Sim.BaselineAgent(), new OnePieceTcg.Sim.BaselineAgent(),
                reg.Resolve(dA), reg.Resolve(dB), $"af:{i}", first,
                new OnePieceTcg.Sim.MatchDriver.Options { CommandCap = 20000 });
            if (rec.winner != null) { done++; if (rec.winner == varSeat) varWins++; }
        }
        OnePieceTcg.Engine.Bot.IntermediateBot.AttackFirstSeat = null;
        double pct = done > 0 ? 100.0 * varWins / done : 0;
        double se = done > 0 ? 100.0 * System.Math.Sqrt(pct / 100 * (1 - pct / 100) / done) : 0;
        Console.WriteLine($"Attack-first (swing before deploy) vs baseline: {varWins}/{done} = {pct:F1}%  (95% CI ±{1.96 * se:F1})  [completed {done}/{games}]  {sw.Elapsed.TotalSeconds:F0}s");
        return 0;
    }

    case "activatemainsickab":
    {
        // GLOBAL A/B: fire beneficial OPT [Activate: Main] ONLY on summoning-sick Characters (pure upside).
        var reg = BuildRegistry(out _);
        int games = args.Length > 1 && int.TryParse(args[1], out var gg) ? gg : 6000;
        var metaIds = reg.Ids.Where(i => !OnePieceTcg.Engine.CardData.StarterDecks.ContainsKey(i)).ToList();
        var pool = metaIds.Where(id => { var d = reg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; }).ToList();
        int varWins = 0, done = 0;
        var rng = new System.Random(223);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < games; i++)
        {
            string dA = pool[rng.Next(pool.Count)], dB = pool[rng.Next(pool.Count)];
            bool southIsVar = i % 2 == 0;
            string varSeat = southIsVar ? "south" : "north";
            OnePieceTcg.Engine.Bot.IntermediateBot.ActivateMainSickOnlySeat = varSeat;
            string first = (i / 2) % 2 == 0 ? "south" : "north";
            var rec = OnePieceTcg.Sim.MatchDriver.Play(
                new OnePieceTcg.Sim.BaselineAgent(), new OnePieceTcg.Sim.BaselineAgent(),
                reg.Resolve(dA), reg.Resolve(dB), $"as:{i}", first,
                new OnePieceTcg.Sim.MatchDriver.Options { CommandCap = 20000 });
            if (rec.winner != null) { done++; if (rec.winner == varSeat) varWins++; }
        }
        OnePieceTcg.Engine.Bot.IntermediateBot.ActivateMainSickOnlySeat = null;
        double pct = done > 0 ? 100.0 * varWins / done : 0;
        double se = done > 0 ? 100.0 * System.Math.Sqrt(pct / 100 * (1 - pct / 100) / done) : 0;
        Console.WriteLine($"Activate:Main on SUMMONING-SICK only vs baseline: {varWins}/{done} = {pct:F1}%  (95% CI ±{1.96 * se:F1})  [completed {done}/{games}]  {sw.Elapsed.TotalSeconds:F0}s");
        return 0;
    }

    case "enableactivatemainab":
    {
        // GLOBAL A/B: one seat unlocks beneficial [Once Per Turn] Character [Activate: Main] abilities.
        var reg = BuildRegistry(out _);
        int games = args.Length > 1 && int.TryParse(args[1], out var gg) ? gg : 6000;
        var metaIds = reg.Ids.Where(i => !OnePieceTcg.Engine.CardData.StarterDecks.ContainsKey(i)).ToList();
        var pool = metaIds.Where(id => { var d = reg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; }).ToList();
        int varWins = 0, done = 0;
        var rng = new System.Random(211);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < games; i++)
        {
            string dA = pool[rng.Next(pool.Count)], dB = pool[rng.Next(pool.Count)];
            bool southIsVar = i % 2 == 0;
            string varSeat = southIsVar ? "south" : "north";
            OnePieceTcg.Engine.Bot.IntermediateBot.EnableActivateMainSeat = varSeat;
            string first = (i / 2) % 2 == 0 ? "south" : "north";
            var rec = OnePieceTcg.Sim.MatchDriver.Play(
                new OnePieceTcg.Sim.BaselineAgent(), new OnePieceTcg.Sim.BaselineAgent(),
                reg.Resolve(dA), reg.Resolve(dB), $"am:{i}", first,
                new OnePieceTcg.Sim.MatchDriver.Options { CommandCap = 20000 });
            if (rec.winner != null) { done++; if (rec.winner == varSeat) varWins++; }
        }
        OnePieceTcg.Engine.Bot.IntermediateBot.EnableActivateMainSeat = null;
        double pct = done > 0 ? 100.0 * varWins / done : 0;
        double se = done > 0 ? 100.0 * System.Math.Sqrt(pct / 100 * (1 - pct / 100) / done) : 0;
        Console.WriteLine($"Enable [Activate:Main] (beneficial OPT) vs baseline: {varWins}/{done} = {pct:F1}%  (95% CI ±{1.96 * se:F1})  [completed {done}/{games}]  {sw.Elapsed.TotalSeconds:F0}s");
        return 0;
    }

    case "baitblockerab":
    {
        // GLOBAL A/B (valid): one seat orders attackers weakest-first when the opponent has an active Blocker.
        var reg = BuildRegistry(out _);
        int games = args.Length > 1 && int.TryParse(args[1], out var gg) ? gg : 6000;
        var metaIds = reg.Ids.Where(i => !OnePieceTcg.Engine.CardData.StarterDecks.ContainsKey(i)).ToList();
        var pool = metaIds.Where(id => { var d = reg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; }).ToList();
        int varWins = 0, done = 0;
        var rng = new System.Random(199);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < games; i++)
        {
            string dA = pool[rng.Next(pool.Count)], dB = pool[rng.Next(pool.Count)];
            bool southIsVar = i % 2 == 0;
            string varSeat = southIsVar ? "south" : "north";
            OnePieceTcg.Engine.Bot.IntermediateBot.BaitBlockerSeat = varSeat;
            string first = (i / 2) % 2 == 0 ? "south" : "north";
            var rec = OnePieceTcg.Sim.MatchDriver.Play(
                new OnePieceTcg.Sim.BaselineAgent(), new OnePieceTcg.Sim.BaselineAgent(),
                reg.Resolve(dA), reg.Resolve(dB), $"bb:{i}", first,
                new OnePieceTcg.Sim.MatchDriver.Options { CommandCap = 20000 });
            if (rec.winner != null) { done++; if (rec.winner == varSeat) varWins++; }
        }
        OnePieceTcg.Engine.Bot.IntermediateBot.BaitBlockerSeat = null;
        double pct = done > 0 ? 100.0 * varWins / done : 0;
        double se = done > 0 ? 100.0 * System.Math.Sqrt(pct / 100 * (1 - pct / 100) / done) : 0;
        Console.WriteLine($"Bait-blocker (weakest-first vs active Blocker) vs baseline: {varWins}/{done} = {pct:F1}%  (95% CI ±{1.96 * se:F1})  {pool.Count} meta decks, {sw.Elapsed.TotalSeconds:F0}s");
        return 0;
    }

    case "holdblockersab":
    {
        // GLOBAL A/B (valid, symmetric): one seat holds non-Leader Blockers back from attacking vs baseline.
        var reg = BuildRegistry(out _);
        int games = args.Length > 1 && int.TryParse(args[1], out var gg) ? gg : 6000;
        var metaIds = reg.Ids.Where(i => !OnePieceTcg.Engine.CardData.StarterDecks.ContainsKey(i)).ToList();
        var pool = metaIds.Where(id => { var d = reg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; }).ToList();
        int varWins = 0, done = 0;
        var rng = new System.Random(197);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < games; i++)
        {
            string dA = pool[rng.Next(pool.Count)], dB = pool[rng.Next(pool.Count)];
            bool southIsVar = i % 2 == 0;
            string varSeat = southIsVar ? "south" : "north";
            OnePieceTcg.Engine.Bot.IntermediateBot.HoldBlockersSeat = varSeat;
            string first = (i / 2) % 2 == 0 ? "south" : "north";
            var rec = OnePieceTcg.Sim.MatchDriver.Play(
                new OnePieceTcg.Sim.BaselineAgent(), new OnePieceTcg.Sim.BaselineAgent(),
                reg.Resolve(dA), reg.Resolve(dB), $"hb:{i}", first,
                new OnePieceTcg.Sim.MatchDriver.Options { CommandCap = 20000 });
            if (rec.winner != null) { done++; if (rec.winner == varSeat) varWins++; }
        }
        OnePieceTcg.Engine.Bot.IntermediateBot.HoldBlockersSeat = null;
        double pct = done > 0 ? 100.0 * varWins / done : 0;
        double se = done > 0 ? 100.0 * System.Math.Sqrt(pct / 100 * (1 - pct / 100) / done) : 0;
        Console.WriteLine($"Hold Blockers back (don't attack) vs baseline: {varWins}/{done} = {pct:F1}%  (95% CI ±{1.96 * se:F1})  {pool.Count} meta decks, {sw.Elapsed.TotalSeconds:F0}s");
        return 0;
    }

    case "nullsegdiag":
    {
        // REVIEWER DIAGNOSTIC: run the segmented harness with NO lever (both seats identical). Every bucket
        // MUST read ~50% by symmetry; anything else is a harness/bucketing bias that invalidates the segmented
        // A/B results. Uses the SAME seed/assignment scheme as the byarch modes.
        var reg = BuildRegistry(out _);
        int games = args.Length > 1 && int.TryParse(args[1], out var gg) ? gg : 9000;
        var metaIds = reg.Ids.Where(i => !OnePieceTcg.Engine.CardData.StarterDecks.ContainsKey(i)).ToList();
        var pool = metaIds.Where(id => { var d = reg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; }).ToList();
        double AvgCost(string id)
        {
            var d = reg.Resolve(id);
            double sum = 0; int n = 0;
            foreach (var e in d.List.Where(e => e.cardId != d.Leader))
            { var cd = OnePieceTcg.Engine.CardData.GetCard(e.cardId); if (cd == null || cd.Type == "leader") continue; sum += cd.Cost * e.qty; n += e.qty; }
            return n > 0 ? sum / n : 0;
        }
        var avg = pool.ToDictionary(id => id, id => AvgCost(id));
        var wins = new int[3]; var tot = new int[3];
        var rng = new System.Random(193);   // SAME seed as racedropbyarchab
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < games; i++)
        {
            string dA = pool[rng.Next(pool.Count)], dB = pool[rng.Next(pool.Count)];
            bool southIsVar = i % 2 == 0;
            string varSeat = southIsVar ? "south" : "north";
            string varDeck = southIsVar ? dA : dB;
            // NO toggle set — both seats are the plain shipped bot.
            string first = (i / 2) % 2 == 0 ? "south" : "north";
            var rec = OnePieceTcg.Sim.MatchDriver.Play(
                new OnePieceTcg.Sim.BaselineAgent(), new OnePieceTcg.Sim.BaselineAgent(),
                reg.Resolve(dA), reg.Resolve(dB), $"rd:{i}", first,
                new OnePieceTcg.Sim.MatchDriver.Options { CommandCap = 20000 });
            if (rec.winner == null) continue;
            double a = avg[varDeck];
            int b = a < 3.3 ? 0 : (a <= 4.3 ? 1 : 2);
            tot[b]++; if (rec.winner == varSeat) wins[b]++;
        }
        string[] names = { "aggro(<3.3)", "mid(3.3-4.3)", "control(>4.3)" };
        int gW = wins[0] + wins[1] + wins[2], gT = tot[0] + tot[1] + tot[2];
        Console.WriteLine($"NULL lever (both seats identical), by variant archetype ({sw.Elapsed.TotalSeconds:F0}s) — expect ~50% everywhere:");
        for (int b = 0; b < 3; b++)
        {
            double pct = tot[b] > 0 ? 100.0 * wins[b] / tot[b] : 0;
            double se = tot[b] > 0 ? 100.0 * System.Math.Sqrt(pct / 100 * (1 - pct / 100) / tot[b]) : 0;
            Console.WriteLine($"  {names[b],-16} {wins[b]}/{tot[b]} = {pct:F1}%  (±{1.96 * se:F1})");
        }
        Console.WriteLine($"  GLOBAL           {gW}/{gT} = {(gT>0?100.0*gW/gT:0):F1}%");
        return 0;
    }

    case "deckavgdiag":
    {
        // REVIEWER DIAGNOSTIC: for each meta deck, compare the bot's runtime MyDeckAvgCost (on the fresh
        // match state) to the Sim's decklist avg cost. At match start they MUST match; any gap is a bug.
        var reg = BuildRegistry(out _);
        var metaIds = reg.Ids.Where(i => !OnePieceTcg.Engine.CardData.StarterDecks.ContainsKey(i)).ToList();
        var pool = metaIds.Where(id => { var d = reg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; }).ToList();
        double AvgCost(OnePieceTcg.Engine.DeckDef d)
        {
            double sum = 0; int n = 0;
            foreach (var e in d.List.Where(e => e.cardId != d.Leader))
            { var cd = OnePieceTcg.Engine.CardData.GetCard(e.cardId); if (cd == null || cd.Type == "leader") continue; sum += cd.Cost * e.qty; n += e.qty; }
            return n > 0 ? sum / n : 0;
        }
        int misAsControl = 0, midCount = 0; double maxGap = 0;
        Console.WriteLine("deck                       sim-avg  bot-avg   gap  simBucket botCtrl?");
        foreach (var id in pool)
        {
            var d = reg.Resolve(id);
            double simAvg = AvgCost(d);
            var st = OnePieceTcg.Engine.GameEngine.CreateMatch(new OnePieceTcg.Engine.MatchConfig
            { SouthDeckDef = d, NorthDeckDef = reg.Resolve(pool[0]), Seed = "diag" });
            double botAvg = OnePieceTcg.Engine.Bot.IntermediateBot.MyDeckAvgCost(st.Players["south"]);
            double gap = botAvg - simAvg;
            if (System.Math.Abs(gap) > maxGap) maxGap = System.Math.Abs(gap);
            string simB = simAvg < 3.3 ? "aggro" : (simAvg <= 4.3 ? "mid" : "control");
            bool botCtrl = botAvg > 4.3;
            if (simB == "mid") { midCount++; if (botCtrl) misAsControl++; }
            Console.WriteLine($"{id,-26} {simAvg,6:F2}  {botAvg,6:F2}  {gap,5:F2}  {simB,-8} {botCtrl}");
        }
        Console.WriteLine($"\nmax |gap| = {maxGap:F2}   mid decks misclassified as control: {misAsControl}/{midCount}");
        return 0;
    }

    case "racedropbyarchab":
    {
        // Segmented+global A/B: varSeat drops the counter-reserve when it has lethal in reach (race) vs baseline.
        var reg = BuildRegistry(out _);
        int games = args.Length > 1 && int.TryParse(args[1], out var gg) ? gg : 9000;
        var metaIds = reg.Ids.Where(i => !OnePieceTcg.Engine.CardData.StarterDecks.ContainsKey(i)).ToList();
        var pool = metaIds.Where(id => { var d = reg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; }).ToList();
        double AvgCost(string id)
        {
            var d = reg.Resolve(id);
            double sum = 0; int n = 0;
            foreach (var e in d.List.Where(e => e.cardId != d.Leader))
            { var cd = OnePieceTcg.Engine.CardData.GetCard(e.cardId); if (cd == null || cd.Type == "leader") continue; sum += cd.Cost * e.qty; n += e.qty; }
            return n > 0 ? sum / n : 0;
        }
        var avg = pool.ToDictionary(id => id, id => AvgCost(id));
        var wins = new int[3]; var tot = new int[3];
        var rng = new System.Random(193);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < games; i++)
        {
            string dA = pool[rng.Next(pool.Count)], dB = pool[rng.Next(pool.Count)];
            bool southIsVar = i % 2 == 0;
            string varSeat = southIsVar ? "south" : "north";
            string varDeck = southIsVar ? dA : dB;
            OnePieceTcg.Engine.Bot.IntermediateBot.RaceDropReserveSeat = varSeat;
            string first = (i / 2) % 2 == 0 ? "south" : "north";
            var rec = OnePieceTcg.Sim.MatchDriver.Play(
                new OnePieceTcg.Sim.BaselineAgent(), new OnePieceTcg.Sim.BaselineAgent(),
                reg.Resolve(dA), reg.Resolve(dB), $"rd:{i}", first,
                new OnePieceTcg.Sim.MatchDriver.Options { CommandCap = 20000 });
            if (rec.winner == null) continue;
            double a = avg[varDeck];
            int b = a < 3.3 ? 0 : (a <= 4.3 ? 1 : 2);
            tot[b]++; if (rec.winner == varSeat) wins[b]++;
        }
        OnePieceTcg.Engine.Bot.IntermediateBot.RaceDropReserveSeat = null;
        string[] names = { "aggro(<3.3)", "mid(3.3-4.3)", "control(>4.3)" };
        int gW = wins[0] + wins[1] + wins[2], gT = tot[0] + tot[1] + tot[2];
        Console.WriteLine($"Race-drop-reserve vs baseline, by variant archetype ({sw.Elapsed.TotalSeconds:F0}s):");
        for (int b = 0; b < 3; b++)
        {
            double pct = tot[b] > 0 ? 100.0 * wins[b] / tot[b] : 0;
            double se = tot[b] > 0 ? 100.0 * System.Math.Sqrt(pct / 100 * (1 - pct / 100) / tot[b]) : 0;
            Console.WriteLine($"  {names[b],-16} {wins[b]}/{tot[b]} = {pct:F1}%  (±{1.96 * se:F1})");
        }
        Console.WriteLine($"  GLOBAL           {gW}/{gT} = {(gT>0?100.0*gW/gT:0):F1}%  (±{(gT>0?1.96*100.0*System.Math.Sqrt((100.0*gW/gT)/100*(1-(100.0*gW/gT)/100)/gT):0):F1})");
        return 0;
    }

    case "surplusoverloadbyarchab":
    {
        // Segmented A/B: varSeat overloads a Leader swing by +2 DON ONLY with surplus DON (>= arg2 beyond the
        // connect cost) vs baseline. Goal: keep the control forcing win, drop the aggro/mid DON-waste loss.
        var reg = BuildRegistry(out _);
        int games = args.Length > 1 && int.TryParse(args[1], out var gg) ? gg : 9000;
        if (args.Length > 2 && int.TryParse(args[2], out var sm)) OnePieceTcg.Engine.Bot.IntermediateBot.SurplusOverloadMin = sm;
        int smin = OnePieceTcg.Engine.Bot.IntermediateBot.SurplusOverloadMin;
        var metaIds = reg.Ids.Where(i => !OnePieceTcg.Engine.CardData.StarterDecks.ContainsKey(i)).ToList();
        var pool = metaIds.Where(id => { var d = reg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; }).ToList();
        double AvgCost(string id)
        {
            var d = reg.Resolve(id);
            double sum = 0; int n = 0;
            foreach (var e in d.List.Where(e => e.cardId != d.Leader))
            { var cd = OnePieceTcg.Engine.CardData.GetCard(e.cardId); if (cd == null || cd.Type == "leader") continue; sum += cd.Cost * e.qty; n += e.qty; }
            return n > 0 ? sum / n : 0;
        }
        var avg = pool.ToDictionary(id => id, id => AvgCost(id));
        var wins = new int[3]; var tot = new int[3];
        var rng = new System.Random(191);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < games; i++)
        {
            string dA = pool[rng.Next(pool.Count)], dB = pool[rng.Next(pool.Count)];
            bool southIsVar = i % 2 == 0;
            string varSeat = southIsVar ? "south" : "north";
            string varDeck = southIsVar ? dA : dB;
            OnePieceTcg.Engine.Bot.IntermediateBot.SurplusOverloadSeat = varSeat;
            string first = (i / 2) % 2 == 0 ? "south" : "north";
            var rec = OnePieceTcg.Sim.MatchDriver.Play(
                new OnePieceTcg.Sim.BaselineAgent(), new OnePieceTcg.Sim.BaselineAgent(),
                reg.Resolve(dA), reg.Resolve(dB), $"so:{i}", first,
                new OnePieceTcg.Sim.MatchDriver.Options { CommandCap = 20000 });
            if (rec.winner == null) continue;
            double a = avg[varDeck];
            int b = a < 3.3 ? 0 : (a <= 4.3 ? 1 : 2);
            tot[b]++; if (rec.winner == varSeat) wins[b]++;
        }
        OnePieceTcg.Engine.Bot.IntermediateBot.SurplusOverloadSeat = null;
        OnePieceTcg.Engine.Bot.IntermediateBot.SurplusOverloadMin = 3;
        string[] names = { "aggro(<3.3)", "mid(3.3-4.3)", "control(>4.3)" };
        int gW = wins[0] + wins[1] + wins[2], gT = tot[0] + tot[1] + tot[2];
        Console.WriteLine($"Surplus-overload (min {smin}) vs baseline, by variant archetype ({sw.Elapsed.TotalSeconds:F0}s):");
        for (int b = 0; b < 3; b++)
        {
            double pct = tot[b] > 0 ? 100.0 * wins[b] / tot[b] : 0;
            double se = tot[b] > 0 ? 100.0 * System.Math.Sqrt(pct / 100 * (1 - pct / 100) / tot[b]) : 0;
            Console.WriteLine($"  {names[b],-16} {wins[b]}/{tot[b]} = {pct:F1}%  (±{1.96 * se:F1})");
        }
        Console.WriteLine($"  GLOBAL           {gW}/{gT} = {(gT>0?100.0*gW/gT:0):F1}%  (±{(gT>0?1.96*100.0*System.Math.Sqrt((100.0*gW/gT)/100*(1-(100.0*gW/gT)/100)/gT):0):F1})");
        return 0;
    }

    case "forcecounterbyarchab":
    {
        // Segmented A/B: varSeat overloads a Leader swing by +2 DON (force a 2nd counter) vs baseline.
        // Reports win% split by variant deck archetype — looking for a positive-somewhere/neutral-elsewhere
        // pattern that would ship as a GLOBAL change (per the iter-19/20 lesson).
        var reg = BuildRegistry(out _);
        int games = args.Length > 1 && int.TryParse(args[1], out var gg) ? gg : 9000;
        var metaIds = reg.Ids.Where(i => !OnePieceTcg.Engine.CardData.StarterDecks.ContainsKey(i)).ToList();
        var pool = metaIds.Where(id => { var d = reg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; }).ToList();
        double AvgCost(string id)
        {
            var d = reg.Resolve(id);
            double sum = 0; int n = 0;
            foreach (var e in d.List.Where(e => e.cardId != d.Leader))
            { var cd = OnePieceTcg.Engine.CardData.GetCard(e.cardId); if (cd == null || cd.Type == "leader") continue; sum += cd.Cost * e.qty; n += e.qty; }
            return n > 0 ? sum / n : 0;
        }
        var avg = pool.ToDictionary(id => id, id => AvgCost(id));
        var wins = new int[3]; var tot = new int[3];
        var rng = new System.Random(181);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < games; i++)
        {
            string dA = pool[rng.Next(pool.Count)], dB = pool[rng.Next(pool.Count)];
            bool southIsVar = i % 2 == 0;
            string varSeat = southIsVar ? "south" : "north";
            string varDeck = southIsVar ? dA : dB;
            OnePieceTcg.Engine.Bot.IntermediateBot.ForceCounterPressureSeat = varSeat;
            string first = (i / 2) % 2 == 0 ? "south" : "north";
            var rec = OnePieceTcg.Sim.MatchDriver.Play(
                new OnePieceTcg.Sim.BaselineAgent(), new OnePieceTcg.Sim.BaselineAgent(),
                reg.Resolve(dA), reg.Resolve(dB), $"fc:{i}", first,
                new OnePieceTcg.Sim.MatchDriver.Options { CommandCap = 20000 });
            if (rec.winner == null) continue;
            double a = avg[varDeck];
            int b = a < 3.3 ? 0 : (a <= 4.3 ? 1 : 2);
            tot[b]++; if (rec.winner == varSeat) wins[b]++;
        }
        OnePieceTcg.Engine.Bot.IntermediateBot.ForceCounterPressureSeat = null;
        string[] names = { "aggro(<3.3)", "mid(3.3-4.3)", "control(>4.3)" };
        Console.WriteLine($"Force-counter +2 overload vs baseline, by variant archetype ({sw.Elapsed.TotalSeconds:F0}s):");
        for (int b = 0; b < 3; b++)
        {
            double pct = tot[b] > 0 ? 100.0 * wins[b] / tot[b] : 0;
            double se = tot[b] > 0 ? 100.0 * System.Math.Sqrt(pct / 100 * (1 - pct / 100) / tot[b]) : 0;
            Console.WriteLine($"  {names[b],-16} {wins[b]}/{tot[b]} = {pct:F1}%  (±{1.96 * se:F1})");
        }
        return 0;
    }

    case "condefconfirmab":
    {
        // Confirm the SHIPPED conditional leader-defense: varSeat uses the conditional default (control→4,
        // else 3 via MyDeckAvgCost); the OTHER seat is forced to flat-3 (old behavior). Both hold charDef=4.
        // Reports varSeat win% by archetype — expect control ~+3pp, mid/aggro ~neutral.
        var reg = BuildRegistry(out _);
        int games = args.Length > 1 && int.TryParse(args[1], out var gg) ? gg : 9000;
        var metaIds = reg.Ids.Where(i => !OnePieceTcg.Engine.CardData.StarterDecks.ContainsKey(i)).ToList();
        var pool = metaIds.Where(id => { var d = reg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; }).ToList();
        double AvgCost(string id)
        {
            var d = reg.Resolve(id);
            double sum = 0; int n = 0;
            foreach (var e in d.List.Where(e => e.cardId != d.Leader))
            { var cd = OnePieceTcg.Engine.CardData.GetCard(e.cardId); if (cd == null || cd.Type == "leader") continue; sum += cd.Cost * e.qty; n += e.qty; }
            return n > 0 ? sum / n : 0;
        }
        var avg = pool.ToDictionary(id => id, id => AvgCost(id));
        if (args.Length > 2 && double.TryParse(args[2], out var ct)) OnePieceTcg.Engine.Bot.IntermediateBot.ControlAvgCostThreshold = ct;
        double cthr = OnePieceTcg.Engine.Bot.IntermediateBot.ControlAvgCostThreshold;
        // Clean setup: varSeat uses the conditional (via its own toggle); the OTHER seat is the plain shipped
        // default (flat Life<=3). Nothing else forced. Also logs MyDeckAvgCost distribution by SIM archetype.
        var botAvgSum = new double[3]; var botAvgN = new int[3];
        var wins = new int[3]; var tot = new int[3];
        var rng = new System.Random(179);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < games; i++)
        {
            string dA = pool[rng.Next(pool.Count)], dB = pool[rng.Next(pool.Count)];
            bool southIsVar = i % 2 == 0;
            string varSeat = southIsVar ? "south" : "north";
            string varDeck = southIsVar ? dA : dB;
            OnePieceTcg.Engine.Bot.IntermediateBot.ConditionalLeaderDefSeat = varSeat;
            string first = (i / 2) % 2 == 0 ? "south" : "north";
            var rec = OnePieceTcg.Sim.MatchDriver.Play(
                new OnePieceTcg.Sim.BaselineAgent(), new OnePieceTcg.Sim.BaselineAgent(),
                reg.Resolve(dA), reg.Resolve(dB), $"cd:{i}", first,
                new OnePieceTcg.Sim.MatchDriver.Options { CommandCap = 20000 });
            if (rec.winner == null) continue;
            double a = avg[varDeck];
            int b = a < 3.3 ? 0 : (a <= 4.3 ? 1 : 2);
            tot[b]++; if (rec.winner == varSeat) wins[b]++;
        }
        OnePieceTcg.Engine.Bot.IntermediateBot.ConditionalLeaderDefSeat = null;
        OnePieceTcg.Engine.Bot.IntermediateBot.ControlAvgCostThreshold = 4.3;
        string[] names = { "aggro(<3.3)", "mid(3.3-4.3)", "control(>4.3)" };
        Console.WriteLine($"Conditional leader-def (bot control-thr {cthr}) vs flat-3, by SIM-archetype ({sw.Elapsed.TotalSeconds:F0}s):");
        for (int b = 0; b < 3; b++)
        {
            double pct = tot[b] > 0 ? 100.0 * wins[b] / tot[b] : 0;
            double se = tot[b] > 0 ? 100.0 * System.Math.Sqrt(pct / 100 * (1 - pct / 100) / tot[b]) : 0;
            Console.WriteLine($"  {names[b],-16} {wins[b]}/{tot[b]} = {pct:F1}%  (±{1.96 * se:F1})");
        }
        return 0;
    }

    case "defbyarchab":
    {
        // Segmented A/B: varSeat counters to save its Leader at Life <= arg2 (vs shipped 3); char-defense held
        // at the shipped 4 to isolate the LEADER threshold. Reports win% split by variant deck archetype.
        var reg = BuildRegistry(out _);
        int games = args.Length > 1 && int.TryParse(args[1], out var gg) ? gg : 9000;
        int thr = args.Length > 2 && int.TryParse(args[2], out var tt) ? tt : 5;
        OnePieceTcg.Engine.Bot.IntermediateBot.DefenseLifeThreshold = thr;
        OnePieceTcg.Engine.Bot.IntermediateBot.CharDefenseCostThreshold = 4;   // = shipped baseline, isolate leader
        var metaIds = reg.Ids.Where(i => !OnePieceTcg.Engine.CardData.StarterDecks.ContainsKey(i)).ToList();
        var pool = metaIds.Where(id => { var d = reg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; }).ToList();
        double AvgCost(string id)
        {
            var d = reg.Resolve(id);
            double sum = 0; int n = 0;
            foreach (var e in d.List.Where(e => e.cardId != d.Leader))
            { var cd = OnePieceTcg.Engine.CardData.GetCard(e.cardId); if (cd == null || cd.Type == "leader") continue; sum += cd.Cost * e.qty; n += e.qty; }
            return n > 0 ? sum / n : 0;
        }
        var avg = pool.ToDictionary(id => id, id => AvgCost(id));
        var wins = new int[3]; var tot = new int[3];
        var rng = new System.Random(173);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < games; i++)
        {
            string dA = pool[rng.Next(pool.Count)], dB = pool[rng.Next(pool.Count)];
            bool southIsVar = i % 2 == 0;
            string varSeat = southIsVar ? "south" : "north";
            string varDeck = southIsVar ? dA : dB;
            OnePieceTcg.Engine.Bot.IntermediateBot.DefenseVariantSeat = varSeat;
            string first = (i / 2) % 2 == 0 ? "south" : "north";
            var rec = OnePieceTcg.Sim.MatchDriver.Play(
                new OnePieceTcg.Sim.BaselineAgent(), new OnePieceTcg.Sim.BaselineAgent(),
                reg.Resolve(dA), reg.Resolve(dB), $"db:{i}", first,
                new OnePieceTcg.Sim.MatchDriver.Options { CommandCap = 20000 });
            if (rec.winner == null) continue;
            double a = avg[varDeck];
            int b = a < 3.3 ? 0 : (a <= 4.3 ? 1 : 2);
            tot[b]++; if (rec.winner == varSeat) wins[b]++;
        }
        OnePieceTcg.Engine.Bot.IntermediateBot.DefenseVariantSeat = null;
        OnePieceTcg.Engine.Bot.IntermediateBot.DefenseLifeThreshold = 3;
        OnePieceTcg.Engine.Bot.IntermediateBot.CharDefenseCostThreshold = 5;
        string[] names = { "aggro(<3.3)", "mid(3.3-4.3)", "control(>4.3)" };
        Console.WriteLine($"Leader-defense Life<={thr} vs shipped 3, by variant deck archetype ({sw.Elapsed.TotalSeconds:F0}s):");
        for (int b = 0; b < 3; b++)
        {
            double pct = tot[b] > 0 ? 100.0 * wins[b] / tot[b] : 0;
            double se = tot[b] > 0 ? 100.0 * System.Math.Sqrt(pct / 100 * (1 - pct / 100) / tot[b]) : 0;
            Console.WriteLine($"  {names[b],-16} {wins[b]}/{tot[b]} = {pct:F1}%  (±{1.96 * se:F1})");
        }
        return 0;
    }

    case "kofloorbyarchab":
    {
        // Segmented A/B: varSeat uses an alternative KO floor (arg2, default 3000 = trade more); baseline seat
        // keeps the shipped 15000. Reports the variant's win% SPLIT by the variant deck's avg cost bucket
        // (aggro <3.3 / mid 3.3-4.3 / control >4.3) to surface an archetype-conditional optimum the full-pool
        // aggregate hides.
        var reg = BuildRegistry(out _);
        int games = args.Length > 1 && int.TryParse(args[1], out var gg) ? gg : 9000;
        if (args.Length > 2 && int.TryParse(args[2], out var af)) OnePieceTcg.Engine.Bot.IntermediateBot.AltFloor = af;
        int altFloor = OnePieceTcg.Engine.Bot.IntermediateBot.AltFloor;
        var metaIds = reg.Ids.Where(i => !OnePieceTcg.Engine.CardData.StarterDecks.ContainsKey(i)).ToList();
        var pool = metaIds.Where(id => { var d = reg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; }).ToList();
        // avg cost per deck (characters+events, excl. leader), weighted by copies
        double AvgCost(string id)
        {
            var d = reg.Resolve(id);
            double sum = 0; int n = 0;
            foreach (var e in d.List.Where(e => e.cardId != d.Leader))
            { var cd = OnePieceTcg.Engine.CardData.GetCard(e.cardId); if (cd == null || cd.Type == "leader") continue; sum += cd.Cost * e.qty; n += e.qty; }
            return n > 0 ? sum / n : 0;
        }
        var avg = pool.ToDictionary(id => id, id => AvgCost(id));
        // buckets: 0 aggro, 1 mid, 2 control
        var wins = new int[3]; var tot = new int[3];
        var rng = new System.Random(167);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < games; i++)
        {
            string dA = pool[rng.Next(pool.Count)], dB = pool[rng.Next(pool.Count)];
            bool southIsVar = i % 2 == 0;
            string varSeat = southIsVar ? "south" : "north";
            string varDeck = southIsVar ? dA : dB;
            OnePieceTcg.Engine.Bot.IntermediateBot.AltFloorSeat = varSeat;
            string first = (i / 2) % 2 == 0 ? "south" : "north";
            var rec = OnePieceTcg.Sim.MatchDriver.Play(
                new OnePieceTcg.Sim.BaselineAgent(), new OnePieceTcg.Sim.BaselineAgent(),
                reg.Resolve(dA), reg.Resolve(dB), $"ka:{i}", first,
                new OnePieceTcg.Sim.MatchDriver.Options { CommandCap = 20000 });
            if (rec.winner == null) continue;
            double a = avg[varDeck];
            int b = a < 3.3 ? 0 : (a <= 4.3 ? 1 : 2);
            tot[b]++; if (rec.winner == varSeat) wins[b]++;
        }
        OnePieceTcg.Engine.Bot.IntermediateBot.AltFloorSeat = null;
        OnePieceTcg.Engine.Bot.IntermediateBot.AltFloor = 3000;
        string[] names = { "aggro(<3.3)", "mid(3.3-4.3)", "control(>4.3)" };
        Console.WriteLine($"KO floor {altFloor} vs shipped 15000, by variant deck archetype ({sw.Elapsed.TotalSeconds:F0}s):");
        for (int b = 0; b < 3; b++)
        {
            double pct = tot[b] > 0 ? 100.0 * wins[b] / tot[b] : 0;
            double se = tot[b] > 0 ? 100.0 * System.Math.Sqrt(pct / 100 * (1 - pct / 100) / tot[b]) : 0;
            Console.WriteLine($"  {names[b],-16} {wins[b]}/{tot[b]} = {pct:F1}%  (±{1.96 * se:F1})");
        }
        return 0;
    }

    case "donaccelfirstab":
    {
        // A/B: one seat plays a DON!!-accel card first (extra DON can fund another play this turn).
        var reg = BuildRegistry(out _);
        int games = args.Length > 1 && int.TryParse(args[1], out var gg) ? gg : 6000;
        var metaIds = reg.Ids.Where(i => !OnePieceTcg.Engine.CardData.StarterDecks.ContainsKey(i)).ToList();
        var pool = metaIds.Where(id => { var d = reg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; }).ToList();
        int varWins = 0, done = 0;
        var rng = new System.Random(163);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < games; i++)
        {
            string dA = pool[rng.Next(pool.Count)], dB = pool[rng.Next(pool.Count)];
            bool southIsVar = i % 2 == 0;
            string varSeat = southIsVar ? "south" : "north";
            OnePieceTcg.Engine.Bot.IntermediateBot.DonAccelFirstSeat = varSeat;
            string first = (i / 2) % 2 == 0 ? "south" : "north";
            var rec = OnePieceTcg.Sim.MatchDriver.Play(
                new OnePieceTcg.Sim.BaselineAgent(), new OnePieceTcg.Sim.BaselineAgent(),
                reg.Resolve(dA), reg.Resolve(dB), $"df:{i}", first,
                new OnePieceTcg.Sim.MatchDriver.Options { CommandCap = 20000 });
            if (rec.winner != null) { done++; if (rec.winner == varSeat) varWins++; }
        }
        OnePieceTcg.Engine.Bot.IntermediateBot.DonAccelFirstSeat = null;
        double pct = done > 0 ? 100.0 * varWins / done : 0;
        double se = done > 0 ? 100.0 * System.Math.Sqrt(pct / 100 * (1 - pct / 100) / done) : 0;
        Console.WriteLine($"DON-accel-first vs value-first: {varWins}/{done} = {pct:F1}%  (95% CI ±{1.96 * se:F1})  {pool.Count} meta decks, {sw.Elapsed.TotalSeconds:F0}s");
        return 0;
    }

    case "raceawarefloorab":
    {
        // A/B: one seat drops the KO floor to arg2 when BEHIND on Life (trade to stabilize); ahead/even keeps
        // the shipped 15000.
        var reg = BuildRegistry(out _);
        int games = args.Length > 1 && int.TryParse(args[1], out var gg) ? gg : 6000;
        if (args.Length > 2 && int.TryParse(args[2], out var bf)) OnePieceTcg.Engine.Bot.IntermediateBot.RaceBehindFloor = bf;
        int behind = OnePieceTcg.Engine.Bot.IntermediateBot.RaceBehindFloor;
        var metaIds = reg.Ids.Where(i => !OnePieceTcg.Engine.CardData.StarterDecks.ContainsKey(i)).ToList();
        var pool = metaIds.Where(id => { var d = reg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; }).ToList();
        int varWins = 0, done = 0;
        var rng = new System.Random(157);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < games; i++)
        {
            string dA = pool[rng.Next(pool.Count)], dB = pool[rng.Next(pool.Count)];
            bool southIsVar = i % 2 == 0;
            string varSeat = southIsVar ? "south" : "north";
            OnePieceTcg.Engine.Bot.IntermediateBot.RaceAwareFloorSeat = varSeat;
            string first = (i / 2) % 2 == 0 ? "south" : "north";
            var rec = OnePieceTcg.Sim.MatchDriver.Play(
                new OnePieceTcg.Sim.BaselineAgent(), new OnePieceTcg.Sim.BaselineAgent(),
                reg.Resolve(dA), reg.Resolve(dB), $"ra:{i}", first,
                new OnePieceTcg.Sim.MatchDriver.Options { CommandCap = 20000 });
            if (rec.winner != null) { done++; if (rec.winner == varSeat) varWins++; }
        }
        OnePieceTcg.Engine.Bot.IntermediateBot.RaceAwareFloorSeat = null;
        OnePieceTcg.Engine.Bot.IntermediateBot.RaceBehindFloor = 5000;
        double pct = done > 0 ? 100.0 * varWins / done : 0;
        double se = done > 0 ? 100.0 * System.Math.Sqrt(pct / 100 * (1 - pct / 100) / done) : 0;
        Console.WriteLine($"Race-aware floor (behind→{behind}) vs static 15000: {varWins}/{done} = {pct:F1}%  (95% CI ±{1.96 * se:F1})  {pool.Count} meta decks, {sw.Elapsed.TotalSeconds:F0}s");
        return 0;
    }

    case "triggerbankab":
    {
        // A/B: one seat declines buff-only Triggers to bank the Life card into hand instead.
        var reg = BuildRegistry(out _);
        int games = args.Length > 1 && int.TryParse(args[1], out var gg) ? gg : 6000;
        var metaIds = reg.Ids.Where(i => !OnePieceTcg.Engine.CardData.StarterDecks.ContainsKey(i)).ToList();
        var pool = metaIds.Where(id => { var d = reg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; }).ToList();
        int varWins = 0, done = 0;
        var rng = new System.Random(151);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < games; i++)
        {
            string dA = pool[rng.Next(pool.Count)], dB = pool[rng.Next(pool.Count)];
            bool southIsVar = i % 2 == 0;
            string varSeat = southIsVar ? "south" : "north";
            OnePieceTcg.Engine.Bot.IntermediateBot.TriggerBankBuffsSeat = varSeat;
            string first = (i / 2) % 2 == 0 ? "south" : "north";
            var rec = OnePieceTcg.Sim.MatchDriver.Play(
                new OnePieceTcg.Sim.BaselineAgent(), new OnePieceTcg.Sim.BaselineAgent(),
                reg.Resolve(dA), reg.Resolve(dB), $"tb:{i}", first,
                new OnePieceTcg.Sim.MatchDriver.Options { CommandCap = 20000 });
            if (rec.winner != null) { done++; if (rec.winner == varSeat) varWins++; }
        }
        OnePieceTcg.Engine.Bot.IntermediateBot.TriggerBankBuffsSeat = null;
        double pct = done > 0 ? 100.0 * varWins / done : 0;
        double se = done > 0 ? 100.0 * System.Math.Sqrt(pct / 100 * (1 - pct / 100) / done) : 0;
        Console.WriteLine($"Trigger-bank buffs (decline pump, keep card) vs use-any: {varWins}/{done} = {pct:F1}%  (95% CI ±{1.96 * se:F1})  {pool.Count} meta decks, {sw.Elapsed.TotalSeconds:F0}s");
        return 0;
    }

    case "mulliganab":
    {
        // A/B: one seat uses a curve-aware mulligan (require >=minEarly cost<=3 cards, avg<=maxAvg).
        // args: <games> <minEarly> <maxAvg>
        var reg = BuildRegistry(out _);
        int games = args.Length > 1 && int.TryParse(args[1], out var gg) ? gg : 6000;
        if (args.Length > 2 && int.TryParse(args[2], out var me2)) OnePieceTcg.Engine.Bot.IntermediateBot.MulliganMinEarly = me2;
        if (args.Length > 3 && double.TryParse(args[3], out var ma)) OnePieceTcg.Engine.Bot.IntermediateBot.MulliganMaxAvg = ma;
        int minE = OnePieceTcg.Engine.Bot.IntermediateBot.MulliganMinEarly;
        double maxA = OnePieceTcg.Engine.Bot.IntermediateBot.MulliganMaxAvg;
        var metaIds = reg.Ids.Where(i => !OnePieceTcg.Engine.CardData.StarterDecks.ContainsKey(i)).ToList();
        var pool = metaIds.Where(id => { var d = reg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; }).ToList();
        int varWins = 0, done = 0;
        var rng = new System.Random(149);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < games; i++)
        {
            string dA = pool[rng.Next(pool.Count)], dB = pool[rng.Next(pool.Count)];
            bool southIsVar = i % 2 == 0;
            string varSeat = southIsVar ? "south" : "north";
            OnePieceTcg.Engine.Bot.IntermediateBot.MulliganVariantSeat = varSeat;
            string first = (i / 2) % 2 == 0 ? "south" : "north";
            var rec = OnePieceTcg.Sim.MatchDriver.Play(
                new OnePieceTcg.Sim.BaselineAgent(), new OnePieceTcg.Sim.BaselineAgent(),
                reg.Resolve(dA), reg.Resolve(dB), $"mu:{i}", first,
                new OnePieceTcg.Sim.MatchDriver.Options { CommandCap = 20000 });
            if (rec.winner != null) { done++; if (rec.winner == varSeat) varWins++; }
        }
        OnePieceTcg.Engine.Bot.IntermediateBot.MulliganVariantSeat = null;
        OnePieceTcg.Engine.Bot.IntermediateBot.MulliganMinEarly = 2;
        OnePieceTcg.Engine.Bot.IntermediateBot.MulliganMaxAvg = 4.5;
        double pct = done > 0 ? 100.0 * varWins / done : 0;
        double se = done > 0 ? 100.0 * System.Math.Sqrt(pct / 100 * (1 - pct / 100) / done) : 0;
        Console.WriteLine($"Curve-aware mulligan (>={minE} cost<=3, avg<={maxA}) vs baseline: {varWins}/{done} = {pct:F1}%  (95% CI ±{1.96 * se:F1})  {pool.Count} meta decks, {sw.Elapsed.TotalSeconds:F0}s");
        return 0;
    }

    case "doubleattackfirstab":
    {
        // A/B: one seat prioritizes spreading DON!! to ready a Double Attacker aimed at the Leader (2 Life)
        // over the cheapest attacker.
        var reg = BuildRegistry(out _);
        int games = args.Length > 1 && int.TryParse(args[1], out var gg) ? gg : 6000;
        var metaIds = reg.Ids.Where(i => !OnePieceTcg.Engine.CardData.StarterDecks.ContainsKey(i)).ToList();
        var pool = metaIds.Where(id => { var d = reg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; }).ToList();
        int varWins = 0, done = 0;
        var rng = new System.Random(137);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < games; i++)
        {
            string dA = pool[rng.Next(pool.Count)], dB = pool[rng.Next(pool.Count)];
            bool southIsVar = i % 2 == 0;
            string varSeat = southIsVar ? "south" : "north";
            OnePieceTcg.Engine.Bot.IntermediateBot.DoubleAttackFirstSeat = varSeat;
            string first = (i / 2) % 2 == 0 ? "south" : "north";
            var rec = OnePieceTcg.Sim.MatchDriver.Play(
                new OnePieceTcg.Sim.BaselineAgent(), new OnePieceTcg.Sim.BaselineAgent(),
                reg.Resolve(dA), reg.Resolve(dB), $"da:{i}", first,
                new OnePieceTcg.Sim.MatchDriver.Options { CommandCap = 20000 });
            if (rec.winner != null) { done++; if (rec.winner == varSeat) varWins++; }
        }
        OnePieceTcg.Engine.Bot.IntermediateBot.DoubleAttackFirstSeat = null;
        double pct = done > 0 ? 100.0 * varWins / done : 0;
        double se = done > 0 ? 100.0 * System.Math.Sqrt(pct / 100 * (1 - pct / 100) / done) : 0;
        Console.WriteLine($"DON-to-Double-Attacker-first vs cheapest-first: {varWins}/{done} = {pct:F1}%  (95% CI ±{1.96 * se:F1})  {pool.Count} meta decks, {sw.Elapsed.TotalSeconds:F0}s");
        return 0;
    }

    case "onkoaversionlegacyab":
    {
        // Confirmatory: BOTH seats trade heavily (legacy KO-everything); the variant ALSO avoids [On K.O.]
        // targets. Isolates the On-KO downside in the regime where the bot actually KOs a lot.
        var reg = BuildRegistry(out _);
        int games = args.Length > 1 && int.TryParse(args[1], out var gg) ? gg : 6000;
        var metaIds = reg.Ids.Where(i => !OnePieceTcg.Engine.CardData.StarterDecks.ContainsKey(i)).ToList();
        var pool = metaIds.Where(id => { var d = reg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; }).ToList();
        int varWins = 0, done = 0;
        var rng = new System.Random(131);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < games; i++)
        {
            string dA = pool[rng.Next(pool.Count)], dB = pool[rng.Next(pool.Count)];
            bool southIsVar = i % 2 == 0;
            string varSeat = southIsVar ? "south" : "north";
            // Both seats legacy KO-everything; only the variant also avoids [On K.O.] targets.
            OnePieceTcg.Engine.Bot.IntermediateBot.LegacyKoSeat = "both";   // sentinel handled below
            OnePieceTcg.Engine.Bot.IntermediateBot.OnKoAversionSeat = varSeat;
            string first = (i / 2) % 2 == 0 ? "south" : "north";
            var rec = OnePieceTcg.Sim.MatchDriver.Play(
                new OnePieceTcg.Sim.BaselineAgent(), new OnePieceTcg.Sim.BaselineAgent(),
                reg.Resolve(dA), reg.Resolve(dB), $"okl:{i}", first,
                new OnePieceTcg.Sim.MatchDriver.Options { CommandCap = 20000 });
            if (rec.winner != null) { done++; if (rec.winner == varSeat) varWins++; }
        }
        OnePieceTcg.Engine.Bot.IntermediateBot.LegacyKoSeat = null;
        OnePieceTcg.Engine.Bot.IntermediateBot.OnKoAversionSeat = null;
        double pct = done > 0 ? 100.0 * varWins / done : 0;
        double se = done > 0 ? 100.0 * System.Math.Sqrt(pct / 100 * (1 - pct / 100) / done) : 0;
        Console.WriteLine($"On-KO aversion WITHIN legacy KO-everything: {varWins}/{done} = {pct:F1}%  (95% CI ±{1.96 * se:F1})  {pool.Count} meta decks, {sw.Elapsed.TotalSeconds:F0}s");
        return 0;
    }

    case "onkoaversionab":
    {
        // A/B: one seat avoids KO'ing opponents with an [On K.O.] trigger (routes to Life) vs baseline.
        var reg = BuildRegistry(out _);
        int games = args.Length > 1 && int.TryParse(args[1], out var gg) ? gg : 6000;
        var metaIds = reg.Ids.Where(i => !OnePieceTcg.Engine.CardData.StarterDecks.ContainsKey(i)).ToList();
        var pool = metaIds.Where(id => { var d = reg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; }).ToList();
        int varWins = 0, done = 0;
        var rng = new System.Random(127);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < games; i++)
        {
            string dA = pool[rng.Next(pool.Count)], dB = pool[rng.Next(pool.Count)];
            bool southIsVar = i % 2 == 0;
            string varSeat = southIsVar ? "south" : "north";
            OnePieceTcg.Engine.Bot.IntermediateBot.OnKoAversionSeat = varSeat;
            string first = (i / 2) % 2 == 0 ? "south" : "north";
            var rec = OnePieceTcg.Sim.MatchDriver.Play(
                new OnePieceTcg.Sim.BaselineAgent(), new OnePieceTcg.Sim.BaselineAgent(),
                reg.Resolve(dA), reg.Resolve(dB), $"ok:{i}", first,
                new OnePieceTcg.Sim.MatchDriver.Options { CommandCap = 20000 });
            if (rec.winner != null) { done++; if (rec.winner == varSeat) varWins++; }
        }
        OnePieceTcg.Engine.Bot.IntermediateBot.OnKoAversionSeat = null;
        double pct = done > 0 ? 100.0 * varWins / done : 0;
        double se = done > 0 ? 100.0 * System.Math.Sqrt(pct / 100 * (1 - pct / 100) / done) : 0;
        Console.WriteLine($"On-KO aversion (route to Life) vs KO-anyway: {varWins}/{done} = {pct:F1}%  (95% CI ±{1.96 * se:F1})  {pool.Count} meta decks, {sw.Elapsed.TotalSeconds:F0}s");
        return 0;
    }

    case "richthreatab":
    {
        // A/B: one seat also ALWAYS answers recurring effect-engines (like Blockers) under the iter-8 floor.
        var reg = BuildRegistry(out _);
        int games = args.Length > 1 && int.TryParse(args[1], out var gg) ? gg : 6000;
        var metaIds = reg.Ids.Where(i => !OnePieceTcg.Engine.CardData.StarterDecks.ContainsKey(i)).ToList();
        var pool = metaIds.Where(id => { var d = reg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; }).ToList();
        int varWins = 0, done = 0;
        var rng = new System.Random(113);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < games; i++)
        {
            string dA = pool[rng.Next(pool.Count)], dB = pool[rng.Next(pool.Count)];
            bool southIsVar = i % 2 == 0;
            string varSeat = southIsVar ? "south" : "north";
            OnePieceTcg.Engine.Bot.IntermediateBot.RichThreatSeat = varSeat;
            string first = (i / 2) % 2 == 0 ? "south" : "north";
            var rec = OnePieceTcg.Sim.MatchDriver.Play(
                new OnePieceTcg.Sim.BaselineAgent(), new OnePieceTcg.Sim.BaselineAgent(),
                reg.Resolve(dA), reg.Resolve(dB), $"rt:{i}", first,
                new OnePieceTcg.Sim.MatchDriver.Options { CommandCap = 20000 });
            if (rec.winner != null) { done++; if (rec.winner == varSeat) varWins++; }
        }
        OnePieceTcg.Engine.Bot.IntermediateBot.RichThreatSeat = null;
        double pct = done > 0 ? 100.0 * varWins / done : 0;
        double se = done > 0 ? 100.0 * System.Math.Sqrt(pct / 100 * (1 - pct / 100) / done) : 0;
        Console.WriteLine($"Rich-threat (always answer engines) vs iter-8 floor only: {varWins}/{done} = {pct:F1}%  (95% CI ±{1.96 * se:F1})  {pool.Count} meta decks, {sw.Elapsed.TotalSeconds:F0}s");
        return 0;
    }

    case "richdeployab":
    {
        // A/B: one seat enriches deployment value with [On Play] ETB impact + [DON!! x_] scaling.
        var reg = BuildRegistry(out _);
        int games = args.Length > 1 && int.TryParse(args[1], out var gg) ? gg : 6000;
        var metaIds = reg.Ids.Where(i => !OnePieceTcg.Engine.CardData.StarterDecks.ContainsKey(i)).ToList();
        var pool = metaIds.Where(id => { var d = reg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; }).ToList();
        int varWins = 0, done = 0;
        var rng = new System.Random(109);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < games; i++)
        {
            string dA = pool[rng.Next(pool.Count)], dB = pool[rng.Next(pool.Count)];
            bool southIsVar = i % 2 == 0;
            string varSeat = southIsVar ? "south" : "north";
            OnePieceTcg.Engine.Bot.IntermediateBot.RichDeployValueSeat = varSeat;
            string first = (i / 2) % 2 == 0 ? "south" : "north";
            var rec = OnePieceTcg.Sim.MatchDriver.Play(
                new OnePieceTcg.Sim.BaselineAgent(), new OnePieceTcg.Sim.BaselineAgent(),
                reg.Resolve(dA), reg.Resolve(dB), $"rd:{i}", first,
                new OnePieceTcg.Sim.MatchDriver.Options { CommandCap = 20000 });
            if (rec.winner != null) { done++; if (rec.winner == varSeat) varWins++; }
        }
        OnePieceTcg.Engine.Bot.IntermediateBot.RichDeployValueSeat = null;
        double pct = done > 0 ? 100.0 * varWins / done : 0;
        double se = done > 0 ? 100.0 * System.Math.Sqrt(pct / 100 * (1 - pct / 100) / done) : 0;
        Console.WriteLine($"Rich deploy value ([On Play]+[DON!! x]) vs plain: {varWins}/{done} = {pct:F1}%  (95% CI ±{1.96 * se:F1})  {pool.Count} meta decks, {sw.Elapsed.TotalSeconds:F0}s");
        return 0;
    }

    case "widedeployab":
    {
        // A/B: one seat deploys cheapest-first (go wide) vs baseline highest-value-first (go tall).
        var reg = BuildRegistry(out _);
        int games = args.Length > 1 && int.TryParse(args[1], out var gg) ? gg : 6000;
        var metaIds = reg.Ids.Where(i => !OnePieceTcg.Engine.CardData.StarterDecks.ContainsKey(i)).ToList();
        var pool = metaIds.Where(id => { var d = reg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; }).ToList();
        int varWins = 0, done = 0;
        var rng = new System.Random(103);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < games; i++)
        {
            string dA = pool[rng.Next(pool.Count)], dB = pool[rng.Next(pool.Count)];
            bool southIsVar = i % 2 == 0;
            string varSeat = southIsVar ? "south" : "north";
            OnePieceTcg.Engine.Bot.IntermediateBot.WideDeploySeat = varSeat;
            string first = (i / 2) % 2 == 0 ? "south" : "north";
            var rec = OnePieceTcg.Sim.MatchDriver.Play(
                new OnePieceTcg.Sim.BaselineAgent(), new OnePieceTcg.Sim.BaselineAgent(),
                reg.Resolve(dA), reg.Resolve(dB), $"wd:{i}", first,
                new OnePieceTcg.Sim.MatchDriver.Options { CommandCap = 20000 });
            if (rec.winner != null) { done++; if (rec.winner == varSeat) varWins++; }
        }
        OnePieceTcg.Engine.Bot.IntermediateBot.WideDeploySeat = null;
        double pct = done > 0 ? 100.0 * varWins / done : 0;
        double se = done > 0 ? 100.0 * System.Math.Sqrt(pct / 100 * (1 - pct / 100) / done) : 0;
        Console.WriteLine($"Wide deploy (cheapest-first) vs tall (value-first): {varWins}/{done} = {pct:F1}%  (95% CI ±{1.96 * se:F1})  {pool.Count} meta decks, {sw.Elapsed.TotalSeconds:F0}s");
        return 0;
    }

    case "zerocounterreserveab":
    {
        // A/B: one seat holds NO DON!! back for a defensive [Counter] Event (attaches everything to attackers)
        // vs the baseline evidence-based reserve.
        var reg = BuildRegistry(out _);
        int games = args.Length > 1 && int.TryParse(args[1], out var gg) ? gg : 6000;
        var metaIds = reg.Ids.Where(i => !OnePieceTcg.Engine.CardData.StarterDecks.ContainsKey(i)).ToList();
        var pool = metaIds.Where(id => { var d = reg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; }).ToList();
        int varWins = 0, done = 0;
        var rng = new System.Random(97);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < games; i++)
        {
            string dA = pool[rng.Next(pool.Count)], dB = pool[rng.Next(pool.Count)];
            bool southIsVar = i % 2 == 0;
            string varSeat = southIsVar ? "south" : "north";
            OnePieceTcg.Engine.Bot.IntermediateBot.ZeroCounterReserveSeat = varSeat;
            string first = (i / 2) % 2 == 0 ? "south" : "north";
            var rec = OnePieceTcg.Sim.MatchDriver.Play(
                new OnePieceTcg.Sim.BaselineAgent(), new OnePieceTcg.Sim.BaselineAgent(),
                reg.Resolve(dA), reg.Resolve(dB), $"zc:{i}", first,
                new OnePieceTcg.Sim.MatchDriver.Options { CommandCap = 20000 });
            if (rec.winner != null) { done++; if (rec.winner == varSeat) varWins++; }
        }
        OnePieceTcg.Engine.Bot.IntermediateBot.ZeroCounterReserveSeat = null;
        double pct = done > 0 ? 100.0 * varWins / done : 0;
        double se = done > 0 ? 100.0 * System.Math.Sqrt(pct / 100 * (1 - pct / 100) / done) : 0;
        Console.WriteLine($"Zero counter-reserve (attach all) vs evidence-based reserve: {varWins}/{done} = {pct:F1}%  (95% CI ±{1.96 * se:F1})  {pool.Count} meta decks, {sw.Elapsed.TotalSeconds:F0}s");
        return 0;
    }

    case "lethalpivotab":
    {
        // A/B: one seat enables the desperation lethal pivot (swing all-in at the Leader when DyingNextTurn),
        // re-measured now that the damage model is connect-aware. Baseline (null) never pivots.
        var reg = BuildRegistry(out _);
        int games = args.Length > 1 && int.TryParse(args[1], out var gg) ? gg : 6000;
        var metaIds = reg.Ids.Where(i => !OnePieceTcg.Engine.CardData.StarterDecks.ContainsKey(i)).ToList();
        var pool = metaIds.Where(id => { var d = reg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; }).ToList();
        int varWins = 0, done = 0;
        var rng = new System.Random(89);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < games; i++)
        {
            string dA = pool[rng.Next(pool.Count)], dB = pool[rng.Next(pool.Count)];
            bool southIsVar = i % 2 == 0;
            string varSeat = southIsVar ? "south" : "north";
            OnePieceTcg.Engine.Bot.IntermediateBot.LethalPivotSeat = varSeat;
            string first = (i / 2) % 2 == 0 ? "south" : "north";
            var rec = OnePieceTcg.Sim.MatchDriver.Play(
                new OnePieceTcg.Sim.BaselineAgent(), new OnePieceTcg.Sim.BaselineAgent(),
                reg.Resolve(dA), reg.Resolve(dB), $"lp:{i}", first,
                new OnePieceTcg.Sim.MatchDriver.Options { CommandCap = 20000 });
            if (rec.winner != null) { done++; if (rec.winner == varSeat) varWins++; }
        }
        OnePieceTcg.Engine.Bot.IntermediateBot.LethalPivotSeat = null;
        double pct = done > 0 ? 100.0 * varWins / done : 0;
        double se = done > 0 ? 100.0 * System.Math.Sqrt(pct / 100 * (1 - pct / 100) / done) : 0;
        Console.WriteLine($"Lethal pivot (connect-aware) vs never-pivot: {varWins}/{done} = {pct:F1}%  (95% CI ±{1.96 * se:F1})  {pool.Count} meta decks, {sw.Elapsed.TotalSeconds:F0}s");
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

// Board fingerprint for de-duping generated puzzles: same category + same material on both sides = a dupe.
static string Signature(OnePieceTcg.Engine.Puzzles.GeneratedPuzzle gp)
{
    var s = gp.State; var S = s.Players["south"]; var N = s.Players["north"];
    string Cards(IEnumerable<OnePieceTcg.Engine.CardInstance> zone) =>
        string.Join(",", zone.Where(c => c != null).Select(c => c.CardId).OrderBy(x => x, StringComparer.Ordinal));
    return $"{gp.Category}|SA:{Cards(S.CharacterArea)}|Sd:{S.CostArea.Count}|Sh:{Cards(S.Hand)}"
         + $"|NA:{Cards(N.CharacterArea)}|Nd:{N.CostArea.Count}|Nh:{Cards(N.Hand)}|NL:{N.Life.Count}";
}
