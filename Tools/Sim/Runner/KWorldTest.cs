using System;
using System.Collections.Generic;
using OnePieceTcg.Engine;
using OnePieceTcg.Sim.Planning;

namespace OnePieceTcg.Sim.Runner
{
    /// <summary>
    /// FALSIFICATION-FIRST tests for K-world's aggregation (<see cref="HonestPlannerBot.VoteFirstMove"/>). The
    /// load-bearing one is AGGREGATION BITES: when the sampled worlds disagree, the plurality move must win —
    /// NOT world-0's move — or K is silently inert (the "feature does nothing" trap). Voting reads only the
    /// per-world first MOVES (commands over preserved ids), never hidden cards, so it cannot leak.
    /// </summary>
    public static class KWorldTest
    {
        public static int Run()
        {
            Console.WriteLine("=== K-world aggregation (voting) ===");
            int fail = 0;
            fail += Section("unanimous worlds ⇒ that move", Unanimous());
            fail += Section("AGGREGATION BITES: world-0 in the minority is NOT chosen", Bites());
            fail += Section("plurality (not majority) wins", Plurality());
            fail += Section("tie ⇒ earliest first-seen world (deterministic)", Tie());
            fail += Section("single world ⇒ that move (K=1 reproduces single-world path)", Single());
            fail += Section("deterministic: same votes ⇒ same choice", Deterministic());
            Console.WriteLine();
            Console.WriteLine(fail == 0 ? "PASS - voting aggregates across worlds and never merely echoes world-0." : $"FAIL - {fail} check(s) failed.");
            return fail == 0 ? 0 : 1;
        }

        private static int Section(string name, (bool ok, string detail) r)
        {
            Console.WriteLine($"  [{(r.ok ? "ok  " : "FAIL")}] {name}{(r.detail == null ? "" : " - " + r.detail)}");
            return r.ok ? 0 : 1;
        }

        // Distinct legal moves by instance id (as the determinizer preserves own/board ids across worlds).
        private static GameCommand Play(string inst) => new GameCommand { Type = "playCard", Seat = "south", InstanceId = inst };

        private static (bool, string) Unanimous()
        {
            var m = HonestPlannerBot.VoteFirstMove(new[] { Play("A"), Play("A"), Play("A") });
            return m.InstanceId == "A" ? (true, "A") : (false, m.InstanceId);
        }

        private static (bool, string) Bites()
        {
            // world-0 says B, but two worlds say A ⇒ A must win. A single-world (echo world-0) bot would pick B.
            var m = HonestPlannerBot.VoteFirstMove(new[] { Play("B"), Play("A"), Play("A") });
            return m.InstanceId == "A"
                ? (true, "picked the plurality A over world-0's B")
                : (false, $"echoed world-0: got {m.InstanceId}, expected A");
        }

        private static (bool, string) Plurality()
        {
            var m = HonestPlannerBot.VoteFirstMove(new[] { Play("A"), Play("A"), Play("B"), Play("C") });
            return m.InstanceId == "A" ? (true, "A (2 of 4)") : (false, m.InstanceId);
        }

        private static (bool, string) Tie()
        {
            var m = HonestPlannerBot.VoteFirstMove(new[] { Play("A"), Play("B") });
            return m.InstanceId == "A" ? (true, "first-seen A") : (false, $"tie-break not deterministic: {m.InstanceId}");
        }

        private static (bool, string) Single()
        {
            var m = HonestPlannerBot.VoteFirstMove(new[] { Play("Z") });
            return m.InstanceId == "Z" ? (true, "Z") : (false, m.InstanceId);
        }

        private static (bool, string) Deterministic()
        {
            var votes = new[] { Play("B"), Play("A"), Play("A"), Play("C") };
            var m1 = HonestPlannerBot.VoteFirstMove(votes);
            var m2 = HonestPlannerBot.VoteFirstMove(votes);
            return m1.InstanceId == m2.InstanceId ? (true, $"stable ({m1.InstanceId})") : (false, "non-deterministic");
        }
    }
}
