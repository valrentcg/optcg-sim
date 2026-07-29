using System;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// Turns a REPORTING sweep into a regression detector without demanding perfection.
    ///
    /// Several sweeps here measure something that legitimately is not zero — clauses whose cost cannot
    /// be paid on the fixture board, clauses with no clickable target because they name a type nobody
    /// is holding. Those cannot be gated on zero, so they exit 0 and print numbers for a human, which
    /// means a change that makes them WORSE sails straight through. Every one of the engine bugs found
    /// this session would have been invisible that way if it had been introduced rather than
    /// discovered.
    ///
    /// A ratchet is the middle ground: record what the number is today and fail if it grows. The number
    /// is free to fall, and when it does the baseline should be lowered in the same commit so the
    /// improvement is locked in rather than left as slack.
    ///
    /// Baselines are FIXTURE-DEPENDENT. Widening a sweep's board legitimately moves them - usually
    /// down, as unmet preconditions become met. Re-baseline deliberately in that commit; never nudge a
    /// baseline up to make a red run green, which is exactly the failure this exists to catch.
    /// </summary>
    public static class SweepRatchet
    {
        private static int worsened;

        public static void Reset() => worsened = 0;

        /// <summary>Fails when <paramref name="actual"/> exceeds <paramref name="baseline"/>. Reports a
        /// fall too, so an improvement is noticed and can be locked in.</summary>
        public static void AtMost(string what, int actual, int baseline)
        {
            if (actual > baseline)
            {
                worsened++;
                Console.WriteLine($"  RATCHET BROKEN  {what}: {actual} (was {baseline}) — this got WORSE");
            }
            else if (actual < baseline)
            {
                Console.WriteLine($"  ratchet improved: {what}: {actual} (baseline {baseline})"
                                  + " — lower the baseline in this commit to lock it in");
            }
        }

        /// <summary>0 = every ratchet held.</summary>
        public static int Result() => worsened == 0 ? 0 : 1;
    }
}
