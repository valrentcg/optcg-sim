using System;
using System.Collections.Generic;
using System.Linq;
using OnePieceTcg.Engine;
using OnePieceTcg.Engine.Bot;
using OnePieceTcg.Engine.Bot.Search;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// Diagnostic for report 20260725-045539-685: the Advanced bot's DECK-LOOK decision takes seconds.
    /// Replays a bug report to its exact state, then instruments the rollout the way SearchBot does —
    /// how many candidates get rolled out, how long each playout runs, and what fraction of the commands
    /// applied inside the playout are NO-OPs (the "burn to the command cap" failure mode).
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- decklookprofile [bugs.jsonl] [reportId|last]
    /// </summary>
    public static class DeckLookThinkProfile
    {
        public static int Run(string[] args)
        {
            var st = BugReportReplay.LoadState(args.Length > 1 ? args[1] : null, args.Length > 2 ? args[2] : "last", out var seat, out _);
            if (st == null) return 1;
            Console.WriteLine($"=== rollout profile: seat={seat} deckLook={(st.DeckLook == null ? "none" : st.DeckLook.SourceName)} ===");

            var fair = BotDeterminizer.FairView(st, seat, BotDeterminizer.Seed(st, seat));
            var candidates = LegalActions.Candidates(fair, seat);
            var swv = System.Diagnostics.Stopwatch.StartNew();
            var legal = LegalActions.Validate(fair, seat, candidates);
            swv.Stop();
            Console.WriteLine($"candidates={candidates.Count} legal={legal.Count} (validate {swv.ElapsedMilliseconds} ms)");

            var toScore = legal.Count > SearchBot.Shortlist
                ? legal.OrderByDescending(x => Evaluation.Score(x.Value, seat)).Take(SearchBot.Shortlist).ToList()
                : legal;

            long grand = 0;
            foreach (var kv in toScore)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var clone = GameClone.Clone(kv.Value);
                var stats = InstrumentedPlayout(clone, SearchBot.RolloutCap);
                sw.Stop();
                grand += sw.ElapsedMilliseconds;
                Console.WriteLine($"  {IntermediateBot.Signature(kv.Key),-60} {sw.ElapsedMilliseconds,6} ms  " +
                    $"applied={stats.applied,5} noop={stats.noop,5} ({(stats.applied == 0 ? 0 : 100 * stats.noop / stats.applied)}%) " +
                    $"finished={clone.Status == "finished"} endTurn={stats.turns}");
            }
            Console.WriteLine($"total rollout time: {grand} ms  (RolloutCap={SearchBot.RolloutCap}, Shortlist={SearchBot.Shortlist})");
            Console.WriteLine("no-op commands inside the rollouts, by signature (top 15):");
            foreach (var kv in noopBySig.OrderByDescending(k => k.Value).Take(15))
                Console.WriteLine($"    {kv.Value,6}x  {kv.Key}");
            Console.WriteLine($"outer Playout passes: {passes}");
            Console.WriteLine($"engine rejection for the looping attack: \"{rejectionReason}\"");
            Console.WriteLine("NOTE: measured on .NET 8 / CoreCLR. The shipped Unity player runs Mono, typically several times slower.");
            return 0;
        }

        private static readonly Dictionary<string, int> noopBySig = new Dictionary<string, int>();
        private static int passes;
        private static string rejectionReason;

        private struct Stats { public int applied, noop, turns; }

        // Mirror of SearchBot.Playout/DriveSeat with counters.
        private static Stats InstrumentedPlayout(GameState state, int cap)
        {
            var st = new Stats();
            int total = 0;
            while (state.Status != "finished" && total < cap)
            {
                passes++;
                int s = Drive(state, "south", cap - total, ref st); total += s;
                int n = Drive(state, "north", cap - total, ref st); total += n;
                if (s == 0 && n == 0) break;
            }
            return st;
        }

        private static int Drive(GameState state, string seat, int budget, ref Stats st)
        {
            int applied = 0;
            var bl = new HashSet<string>();
            for (int i = 0; i < budget; i++)
            {
                var cmd = ChampionBot.DecideOneCommand(state, seat, bl);
                if (cmd == null) break;
                object before = IntermediateBot.SnapshotFor(state, cmd);
                GameEngine.ApplyCommand(state, cmd);
                applied++; st.applied++;
                if (cmd.Type == "endTurn") st.turns++;
                if (!IntermediateBot.Succeeded(state, cmd, before))
                {
                    var sg = IntermediateBot.Signature(cmd);
                    bl.Add(sg); st.noop++;
                    noopBySig[sg] = noopBySig.TryGetValue(sg, out var q) ? q + 1 : 1;
                    if (cmd.Type == "declareAttack" && rejectionReason == null && state.EventLog.Count > 0)
                        rejectionReason = state.EventLog[state.EventLog.Count - 1].Message;
                }
            }
            return applied;
        }
    }
}
