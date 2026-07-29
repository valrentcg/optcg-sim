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
            // Falsifiable, unlike disjunctionsweep: crippling the shared match-any tag loop makes
            // its playability case fail. That is what qualifies it here.
            ("disjunctionresolve",    DisjunctionResolveTest.Run),
            // Every prompt added this session must be answerable by the BOT too - an unanswerable
            // pending effect is a hung solo game, and botstall plays random matchups that may
            // never draw these cards.
            ("botprompts",            BotAnswersPromptsTest.Run),
            ("clonefidelity",         CloneFidelityTest.Run),
            ("sacrifice",             SacrificeProtectionTest.Run),
            ("timingsweep",           TimingDispatchSweep.Run),
            // [DON!! xN] is a GATE, not a timing — timingsweep dispatches on the tag that FOLLOWS
            // it, so a shut gate is indistinguishable there from a card with no effect.
            ("donthreshold",          DonThresholdTest.Run),
            ("onceperturn",           OncePerTurnTest.Run),
            ("optionalonce",          OptionalOnceSkipTest.Run),
            ("opponentdecides",       OpponentDecidesTest.Run),
            ("opponentbranch",        OpponentBranchTest.Run),
            ("opponentpicks",         OpponentPicksTest.Run),
            ("selfdisposal",          SelfDisposalChoiceTest.Run),
            // The instrument the last five auto-pick defects should have been found by, rather than
            // one at a time by reading. Falsifiable: restoring any one of those auto-picks turns it
            // red, which is what earns it a place here.
            ("autopick",              AutoPickSweep.Run),
            // The one selective wording autopick deliberately cannot see: it filters "top of"/
            // "bottom of" as positional, which is right for the ~200 clauses naming ONE end and
            // wrong for the 46 offering a choice BETWEEN them.
            ("lifeend",               LifeEndChoiceTest.Run),
            // glowsweep drives every card's text on its CONTROLLER's seat, so a decision handed to
            // the opponent is a state it cannot construct. Three of those are mandatory, where
            // nothing clickable is a frozen game rather than an annoyance.
            ("crossglow",             CrossSeatGlowTest.Run),
            // The cards the brief names, driven individually. Class fixes are proven by the cards
            // they touched; the ones they did NOT touch are where the next bug lives.
            ("namedcards",            NamedCardsTest.Run),
            // 42 "you may" clauses live in the `trigger` DATA FIELD, which no pool sweep here reads
            // — they all enumerate `effect`. This is also where the brief's two halves meet: a
            // [Trigger] only fires when a Life card is dealt as damage.
            ("triggercost",           TriggerCostTest.Run),
            // Same oracle as autopick, over the population autopick structurally cannot reach.
            // Restoring the Hand[0] trigger auto-pick reports 14 and breaks the ratchet.
            ("triggerfield",          TriggerFieldSweep.Run),
            // Audits MY OWN trigger-cost fix: it pays the cost after the body runs, which rule
            // 8-4-1-3 inverts. Without the eligibility snapshot a body that draws hands the player
            // a fresh card to pay with.
            ("triggerorder",          TriggerCostOrderTest.Run),
            // Audits the highest-reach fix here ("up to N" is a ceiling). Only the 1-of-N case
            // discriminates it — the empty-board cases pass with the fix reverted.
            ("uptonrider",            UpToNRiderTest.Run),
            // The Use-button label, asked for by name and shipped untested because the derivation
            // was private to a MonoBehaviour. Moving it into the engine is what makes it testable.
            ("costlabel",             CostLabelTest.Run),
            // The other half of the prompt: WHERE to click. No engine suite can see a misdirecting
            // prompt, because the engine resolves clicks and never words them.
            ("promptzone",            PromptZoneTest.Run),
            // The progress ledger drops a part it cannot locate SILENTLY — the text just never
            // colours. Both sides of the comparison come from the same resolution, so a miss is two
            // pieces of the engine disagreeing.
            ("ledgerparts",           LedgerPartsTest.Run),
            // The only fixture here that builds a Character at 0 POWER — a state reachable only
            // after a -power effect lands, and the one the reduce-then-remove archetype is about.
            ("zeropowerko",           ZeroPowerKoTest.Run),
            // "heal" is named in the brief every time; its 63 distinct wordings had never been
            // enumerated. Checks source zone, top-of-Life position, and FACING both ways.
            ("healsweep",             HealSweep.Run),
            // The brief's third Life keyword. A reorder is a LOOK: count unchanged, facing
            // unchanged, and the opponent variant must move THEIR stack.
            ("liferearrange",         LifeRearrangeTest.Run),
            // A passive buff that silently fails to apply has no prompt and no log line — the only
            // symptom is a Character losing a fight it should win.
            ("wordingvariant",        WordingVariantTest.Run),
            // The brief in one assertion: a card that says "you may" must never just do it.
            // Removing the opt-in guard turns this red with 127 auto-fires, so it can fail.
            ("optionalfires",         OptionalNeverAutoFiresSweep.Run),
            // The brief's other half, in its one gateable form: if the engine took your payment,
            // it owes you an effect or a prompt. Reverting the up-to-N fix turns this red.
            ("paidfornothing",        PaidForNothingSweep.Run),
            // Diffs the retire predicate against itself (retirement on vs off). It DELETES
            // effects, so a false positive costs the player their card silently. Reverting the
            // conjunction fix turns this red.
            ("retiresweep",           RetireFalsePositiveSweep.Run),
            // Ratcheted reports. These measure things that legitimately are not zero, so they
            // cannot be gated on zero - but they must never grow. See SweepRatchet.
            ("costresolvesweep",      CostPrefixResolveSweep.Run),
            ("glowsweep",             GlowDeadlockSweep.Run),
            // Asserts a real invariant - no clause may leave the player with a mandatory prompt
            // --- Life: flip, heal, re-arrange, and battle damage --------------------
            ("lifefaceup",            LifeFaceUpTest.Run),
            ("lifemechanics",         LifeMechanicsTest.Run),
            ("lifeadvanced",          LifeAdvancedTest.Run),
            ("lifedamage",            LifeDamageTest.Run),
            ("lifedamageedge",        LifeDamageEdgeTest.Run),
            ("lifeboundary",          LifeBoundaryTest.Run),
            ("lifeshapes",            LifeShapeGapTest.Run),
            ("costshapes",            CostShapeGapTest.Run),
            ("bodyoutcome",           BodyOutcomeTest.Run),
            ("realplay",              RealPlayEndToEndTest.Run),
            ("promptabuse",           PromptAbuseTest.Run),
            // In PvP both clients send commands and the engine is the only referee, so a missing
            // seat check is an action one player can take on the other's behalf.
            ("wrongseat",             WrongSeatSweep.Run),
            ("illegaltarget",         IllegalTargetSweep.Run),
            ("drawoutcome",           DrawOutcomeSweep.Run),

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
