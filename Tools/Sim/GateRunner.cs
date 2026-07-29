using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// One command that runs every deterministic pass/fail suite and fails if any of them does.
    ///
    /// The suites existed; nothing ran them together. Sim has 60+ modes and no aggregate, so each new
    /// regression suite was a mode somebody had to remember to type - which is how coverage quietly
    /// rots. A single gate makes "did I break something" one command instead of twenty-three.
    ///
    /// Deliberately NOT included: `smoke` (a statistical run, not pass/fail - its P(first) moves with
    /// legitimate engine changes), and the reporting sweeps (`costresolvesweep`, `costprefixsweep`,
    /// `stallsweep`) which print leads for a human and always exit 0 by design. A gate whose members
    /// can pass without asserting anything is theatre.
    ///
    /// `glowsweep` was briefly a member and was removed. Its one assertion - that no clause may
    /// leave the player with a MANDATORY prompt and an inert board - is not falsifiable: it still
    /// reads 0 with RetireUnresolvablePendingEffects disabled, so it cannot fail. A check that
    /// cannot fail is worse than no check, because it reads as coverage. That regression IS
    /// caught, by notargettest and replacementchoice, which both go red when the retire sweep is
    /// disabled. Do not re-add it without first making it fail on purpose.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- gate
    /// </summary>
    public static class GateRunner
    {
        private static readonly (string Name, Func<int> Run)[] Suites =
        {
            // --- rules and keywords -------------------------------------------------
            ("keywordrulescheck",     KeywordRulesTest.Run),
            ("stagereplacetest",      StageReplacementTest.Run),
            ("retargetlooptest",      ChampionRetargetLoopTest.Run),
            ("bouncetest",            BounceOwnershipTest.Run),
            ("donrecipient",          DonRecipientTest.Run),
            ("triggerprecedencetest", AdvancedTriggerPrecedenceTest.Run),
            ("handtrashtest",         HandTrashKoTest.Run),
            ("notargettest",          NoLegalTargetTest.Run),
            ("lucytest",              LucyPlaytestTest.Run),

            // --- "you may": is the player asked, and does it then resolve? ----------
            ("wypertest",             WyperOnPlayTest.Run),
            ("replacementchoice",     ReplacementChoiceTest.Run),
            ("playerchoice",          PlayerChoiceTest.Run),
            ("endtoend",              EffectEndToEndTest.Run),
            ("donrest",               DonRestCostTest.Run),
            ("countercost",           CounterCostTest.Run),
            ("revealcost",            RevealCostTest.Run),
            ("sacrifice",             SacrificeProtectionTest.Run),
            ("timingsweep",           TimingDispatchSweep.Run),
            // Asserts a real invariant - no clause may leave the player with a mandatory prompt
            // --- Life: flip, heal, re-arrange, and battle damage --------------------
            ("lifefaceup",            LifeFaceUpTest.Run),
            ("lifemechanics",         LifeMechanicsTest.Run),
            ("lifeadvanced",          LifeAdvancedTest.Run),
            ("lifedamage",            LifeDamageTest.Run),

            // --- tooling hygiene ----------------------------------------------------
            ("hygiene",               SourceHygieneTest.Run),
        };

        public static int Run()
        {
            var failures = new List<string>();
            var timings = new List<(string Name, long Ms, int Code)>();
            var total = Stopwatch.StartNew();

            foreach (var s in Suites)
            {
                var sw = Stopwatch.StartNew();
                int code;
                try { code = s.Run(); }
                catch (Exception ex)
                {
                    // A suite that throws is a failure, not a crash of the gate - the remaining
                    // suites still carry information and should still run.
                    Console.WriteLine($"  !! {s.Name} threw: {ex.GetType().Name}: {ex.Message}");
                    code = 1;
                }
                sw.Stop();
                timings.Add((s.Name, sw.ElapsedMilliseconds, code));
                if (code != 0) failures.Add(s.Name);
            }
            total.Stop();

            Console.WriteLine();
            Console.WriteLine("=========================================================");
            Console.WriteLine($"  GATE  {Suites.Length - failures.Count}/{Suites.Length} suites passed"
                              + $"   ({total.ElapsedMilliseconds / 1000.0:F1}s)");
            Console.WriteLine("=========================================================");
            foreach (var t in timings.OrderByDescending(x => x.Ms).Take(5))
                Console.WriteLine($"    slowest: {t.Name,-22} {t.Ms,6} ms");

            if (failures.Count == 0)
            {
                Console.WriteLine("    all green");
                return 0;
            }
            Console.WriteLine();
            Console.WriteLine("    FAILED: " + string.Join(", ", failures));
            return 1;
        }
    }
}
