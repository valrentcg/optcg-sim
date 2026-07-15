using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim.Search
{
    /// <summary>
    /// Splits the cost of a "work unit" into its two halves — GameClone.Clone and GameEngine.ApplyCommand.
    /// LegalActions.WorkUnits counts them together (one clone + one apply per candidate), so a measured
    /// ~5ms/unit says nothing about WHICH to optimise. Guessing here would be expensive: the clone fix is
    /// a small change, whereas making ApplyCommand cheaper means touching a 9,700-line engine.
    ///
    /// Also reports how the cost scales with game length, because EventLog/CommandHistory grow with every
    /// command and Clone copies both — if that is the driver, cost per clone RISES over a game (a
    /// quadratic term), and search near the end of a game is far more expensive than at the start.
    /// </summary>
    public static class CloneBench
    {
        public static void Run(DeckDef a, DeckDef b, int maxTurns)
        {
            Console.WriteLine("\n=== clone-bench: where does a work unit actually go? ===");
            var st = GameEngine.CreateMatch(new MatchConfig { SouthDeckDef = a, NorthDeckDef = b, Seed = "cb:1" });
            GameEngine.ApplyCommand(st, new GameCommand { Type = "chooseTurnOrder", Seat = st.CoinFlipWinner, GoingFirst = true });

            // Drive a real game with the planner, sampling clone/apply cost as the log grows.
            var south = new Planning.PlannerBot(null, Planning.DeckContext.Generic,
                new Planning.TurnPlanner.Options { BeamWidth = 3, MaxDepth = 6, NodeBudget = 120, WorkBudget = 800, ConsiderTruncatedLines = true });
            var north = new BaselineAgent();

            Console.WriteLine($"{"turn",5} {"logLen",7} {"clone_us",9} {"cloneNL",9} {"cand_us",8} {"fp_us",7} {"score_us",8} {"plan_ms",9} {"units",7} {"us/unit",9}");
            int total = 0;
            while (st.Status != "finished" && total < 20000 && st.TurnNumber <= maxTurns)
            {
                int did = 0;
                foreach (var (ag, seat) in new[] { ((IAgent)south, "south"), ((IAgent)north, "north") })
                {
                    var bl = new HashSet<string>();
                    for (int i = 0; i < 2000; i++)
                    {
                        var cmd = ag.Decide(st, seat, bl);
                        if (cmd == null) break;
                        string sig = string.Join("|", cmd.Type, cmd.Seat, cmd.InstanceId, cmd.Target, cmd.Attacker, cmd.Blocker, cmd.EffectId, cmd.Amount);
                        if (bl.Contains(sig)) break;
                        long before = LegalActions.StateFingerprint(st);
                        GameEngine.ApplyCommand(st, cmd);
                        total++; did++;
                        if (LegalActions.StateFingerprint(st) == before) bl.Add(sig);
                        if (st.Status == "finished") break;
                    }
                    if (st.Status == "finished") break;
                }
                if (did == 0) break;

                if (st.TurnNumber % 3 == 0 && st.Status == "active") Sample(st);
            }
            Console.WriteLine("\nus/unit is the number that matters: wall-clock microseconds per WorkUnit inside a real");
            Console.WriteLine("planned turn. Compare it to clone_us — if us/unit >> clone_us, then WorkUnits is metering");
            Console.WriteLine("the wrong thing and the budget throttles search without tracking the real cost.");
        }

        private static void Sample(GameState st)
        {
            const int N = 200;
            string seat = st.ActiveSeat;
            var w = Planning.ValueFunction.DefaultWeights();
            var ctx = Planning.DeckContext.Generic;

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < N; i++) { var c = GameClone.Clone(st); GC.KeepAlive(c); }
            double cloneUs = sw.Elapsed.TotalMilliseconds * 1000 / N;

            sw.Restart();
            for (int i = 0; i < N; i++) { var c = GameClone.Clone(st, searchMode: true); GC.KeepAlive(c); }
            double cloneNoLogUs = sw.Elapsed.TotalMilliseconds * 1000 / N;

            sw.Restart();
            for (int i = 0; i < N; i++) { var c = LegalActions.Candidates(st, seat); GC.KeepAlive(c); }
            double candUs = sw.Elapsed.TotalMilliseconds * 1000 / N;

            sw.Restart();
            for (int i = 0; i < N; i++) { var f = LegalActions.StateFingerprint(st); GC.KeepAlive(f); }
            double fpUs = sw.Elapsed.TotalMilliseconds * 1000 / N;

            sw.Restart();
            for (int i = 0; i < N; i++) { var v = Planning.ValueFunction.Score(st, seat, w, ctx); GC.KeepAlive(v); }
            double scoreUs = sw.Elapsed.TotalMilliseconds * 1000 / N;

            // The real in-situ number: microseconds of wall time per WorkUnit inside a whole planned turn.
            // If this is far above clone+apply, the budget is metering the wrong thing entirely.
            var opt = new Planning.TurnPlanner.Options { BeamWidth = 3, MaxDepth = 6, NodeBudget = 120, WorkBudget = 800, ConsiderTruncatedLines = true };
            long wu0 = LegalActions.WorkUnits;
            sw.Restart();
            var plan = Planning.TurnPlanner.PlanTurn(st, seat, w, ctx, opt);
            double planMs = sw.Elapsed.TotalMilliseconds;
            long units = LegalActions.WorkUnits - wu0;
            GC.KeepAlive(plan);

            Console.WriteLine($"{st.TurnNumber,5} {st.EventLog.Count,7} {cloneUs,9:F1} {cloneNoLogUs,9:F1} {candUs,8:F1} {fpUs,7:F1} {scoreUs,8:F1} {planMs,9:F1} {units,7} {(units > 0 ? planMs * 1000 / units : 0),9:F1}");
        }
    }
}
