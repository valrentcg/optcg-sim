using System;
using System.Collections.Generic;
using System.Linq;
using OnePieceTcg.Engine;
using OnePieceTcg.Engine.Puzzles;   // shipped-engine solver / hints / runtime

namespace OnePieceTcg.Sim.Puzzles
{
    /// <summary>Validation suite for the shipped-engine puzzle stack (LethalSolver + HintGenerator +
    /// PuzzleRuntime) on hand-built positions with known answers. Run:  dotnet run -c Release lethaltest</summary>
    public static class LethalSolverTest
    {
        static int pass, fail;

        public static int Run()
        {
            Console.WriteLine("=== Puzzle stack validation ===");
            // --- solver ---
            Solve("Test1 0-Life, 1 attacker", b => b.Life(0), LethalSolver.Lethal.Win);
            Solve("Test2 3-Life, 2 attackers", b => b.Life(3).Char("ST01-012"), LethalSolver.Lethal.NoLethal);
            Solve("Test3 1-Life, 2 attackers", b => b.Life(1).Char("ST01-012"), LethalSolver.Lethal.Win, hints: true);
            Solve("Test4 1-Life +1 counter, 2x5000 (counter escapes)", b => b.Life(1).Counter().Char("ST01-013"), LethalSolver.Lethal.NoLethal);
            Solve("Test5 1-Life +1 counter, 3x5000 (out-attacks counter)", b => b.Life(1).Counter().Char("ST01-013").Char("ST01-013"), LethalSolver.Lethal.Win, hints: true);

            // --- runtime ---
            RuntimeSolvesCorrectLine();
            RuntimeFailsOnEarlyEndTurn();
            RuntimeRejectsNonLethalStart();

            // --- starter library: every authored puzzle must be a real, solvable lethal ---
            StarterPuzzlesAreLethalAndSolvable();

            Console.WriteLine($"\nPuzzle stack: {pass} passed, {fail} failed.");
            return fail == 0 ? 0 : 1;
        }

        // ---- solver checks --------------------------------------------------------------------

        static void Solve(string name, Action<Builder> setup, LethalSolver.Lethal want, bool hints = false)
        {
            var b = new Builder(); setup(b);
            var r = LethalSolver.Solve(b.St, "south");
            bool ok = r.Outcome == want;
            if (ok) { pass++; Console.WriteLine($"  PASS  {name} -> {want}  (work={r.Work} nodes={r.Nodes})"); }
            else { fail++; Console.WriteLine($"  FAIL  {name} -> got {r.Outcome} (want {want}) work={r.Work} nodes={r.Nodes}"); }
            if (ok && hints && want == LethalSolver.Lethal.Win)
                foreach (var h in HintGenerator.Generate(b.St, "south", r))
                    Console.WriteLine($"        hint L{h.Level}: {h.Text}");
        }

        // ---- runtime checks -------------------------------------------------------------------

        // A lethal board (3x5000 vs 1 Life + 1 counter): driving attacks solves it.
        static void RuntimeSolvesCorrectLine()
        {
            var b = new Builder().Life(1).Counter().Char("ST01-013").Char("ST01-013");
            var rt = new PuzzleRuntime();
            bool started = rt.Start(b.St, "south");
            int guard = 0;
            while (rt.Status == PuzzleRuntime.PuzzleStatus.InProgress && guard++ < 12)
            {
                var atk = rt.LegalMoves().FirstOrDefault(m => m.Type == "declareAttack");
                if (atk == null) break;
                rt.ApplyMove(atk);
            }
            bool ok = started && rt.Status == PuzzleRuntime.PuzzleStatus.Solved;
            Report("Runtime: driving the attacks solves the lethal puzzle", ok, $"started={started} status={rt.Status} msg='{rt.Message}'");
        }

        // Same lethal board: ending the turn immediately fails (the runtime auto-defends and the turn ends).
        static void RuntimeFailsOnEarlyEndTurn()
        {
            var b = new Builder().Life(1).Counter().Char("ST01-013").Char("ST01-013");
            var rt = new PuzzleRuntime();
            rt.Start(b.St, "south");
            var end = rt.LegalMoves().FirstOrDefault(m => m.Type == "endTurn");
            if (end != null) rt.ApplyMove(end);
            bool ok = rt.Status == PuzzleRuntime.PuzzleStatus.Failed;
            Report("Runtime: ending the turn early fails the puzzle", ok, $"status={rt.Status} msg='{rt.Message}'");
        }

        // A non-lethal position is rejected at Start (Start returns false, status Failed).
        static void RuntimeRejectsNonLethalStart()
        {
            var b = new Builder().Life(3).Char("ST01-012");   // 2 attackers vs 3 Life -> not lethal
            var rt = new PuzzleRuntime();
            bool started = rt.Start(b.St, "south");
            bool ok = !started && rt.Status == PuzzleRuntime.PuzzleStatus.Failed;
            Report("Runtime: a non-lethal position is rejected as a puzzle", ok, $"started={started} status={rt.Status}");
        }

        // Each authored starter must (a) verify as a forced win by the solver, and (b) be solvable through
        // the RUNTIME by playing the solver's proven attacker line — driven against the runtime's own
        // strongest-defense auto-play, so this also confirms the line beats optimal defense end-to-end.
        static void StarterPuzzlesAreLethalAndSolvable()
        {
            foreach (var pz in PuzzleLibrary.Starters())
            {
                var pos = pz.Build();                         // one build; ids stay consistent below
                var proof = LethalSolver.Solve(pos, pz.Attacker);
                bool lethal = proof.Outcome == LethalSolver.Lethal.Win;

                bool solved = false;
                if (lethal)
                {
                    var rt = new PuzzleRuntime();
                    rt.Start(pos, pz.Attacker);               // Start clones pos; GameClone preserves instance ids
                    // Drive ADAPTIVELY: re-solve from the live position each step and play the current best
                    // attacker move. A fixed PV assumes one defence; the runtime plays its OWN strongest
                    // defence, so the winning line must be recomputed against what the defender actually does.
                    for (int guard = 0; guard < 40 && rt.Status == PuzzleRuntime.PuzzleStatus.InProgress; guard++)
                    {
                        var step = LethalSolver.Solve(rt.State, pz.Attacker, rt.SolveOpts);
                        var mv = step.PrincipalVariation.FirstOrDefault(c => c.Seat == pz.Attacker);
                        if (mv == null || !rt.ApplyMove(mv)) break;
                    }
                    solved = rt.Status == PuzzleRuntime.PuzzleStatus.Solved;
                }
                Report($"Starter '{pz.Id}' ({pz.Category}) is lethal and solvable", lethal && solved,
                    $"solver={proof.Outcome} solvedByPlay={solved}");
            }
        }

        static void Report(string name, bool ok, string detail)
        {
            if (ok) { pass++; Console.WriteLine($"  PASS  {name}"); }
            else { fail++; Console.WriteLine($"  FAIL  {name}  [{detail}]"); }
        }

        // ---- fluent board builder -------------------------------------------------------------

        sealed class Builder
        {
            public GameState St;
            private readonly PlayerState S, N;
            private int slot;

            public Builder()
            {
                St = GameEngine.CreateMatch(new MatchConfig { SouthDeck = "st01", NorthDeck = "st01", Seed = "lethal" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "south"; St.TurnNumber = 6;
                S = St.Players["south"]; N = St.Players["north"];
                S.TurnsStarted = 4; N.TurnsStarted = 4;
                for (int i = 0; i < 5; i++) { S.CharacterArea[i] = null; N.CharacterArea[i] = null; }
                S.Hand.Clear(); N.Hand.Clear(); S.CostArea.Clear();
                S.Leader.Rested = false; S.Leader.PlayedOnTurn = 0;
                N.Leader.Rested = false; N.Leader.PlayedOnTurn = 0;
                N.Life.Clear();
            }

            public Builder Life(int n) { N.Life.Clear(); for (int i = 0; i < n; i++) N.Life.Add(In("ST01-006", "north", "life")); return this; }
            public Builder Char(string id) { S.CharacterArea[slot++] = In(id, "south"); return this; }
            public Builder Counter() { N.Hand.Add(In("ST01-002", "north", "hand")); return this; }   // Usopp, counter 1000

            private static CardInstance In(string cardId, string owner, string zone = "character") => new CardInstance
            {
                InstanceId = $"{owner}-{cardId}-{Guid.NewGuid():N}".Substring(0, 20),
                CardId = cardId, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
