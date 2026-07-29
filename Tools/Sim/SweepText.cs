using System;
using System.Text.RegularExpressions;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// The text and log predicates the sweeps share, in one place.
    ///
    /// These were hand-rolled per sweep, and it cost me: the engine marks a paid cost TWO ways —
    /// "(cost)" from the auto-payer and " cost: " from the click-driven pick path — and I wrote a
    /// detector that checked only the first, twice, two days apart. The second time it classified 19 of
    /// 24 cards as "cost unpayable" when they had in fact been paid, which made a sweep look like
    /// coverage while verifying five cards.
    ///
    /// That is precisely the drift this session has been fixing in the ENGINE — two implementations of
    /// one rule falling out of step — so the same remedy applies here: delete the copies.
    ///
    /// Deliberately NOT extracted: the 33 per-sweep Board fixtures (~790 lines). They differ in ways
    /// that matter to each sweep, and a mass refactor of them would risk far more than it saves. What
    /// lives here is only the shared JUDGEMENT, which is where the mistakes actually happened.
    /// </summary>
    public static class SweepText
    {
        /// <summary>Strip leading timing tags — "[On Play] ", "[Activate: Main] / [Once Per Turn] " —
        /// the way the engine does before queueing.</summary>
        public static string StripTimingTags(string raw) =>
            Regex.Replace(raw ?? "", @"^\s*(\[[^\]]+\]\s*/?\s*)+", "").Trim();

        /// <summary>Did the engine take a payment? It says so two ways, and missing either one turns a
        /// paid cost into a phantom "unpayable".</summary>
        public static bool IsPaymentLog(string m) =>
            !string.IsNullOrEmpty(m)
            && (m.IndexOf("(cost)", StringComparison.OrdinalIgnoreCase) >= 0
             || m.IndexOf(" cost: ", StringComparison.OrdinalIgnoreCase) >= 0);

        /// <summary>Bookkeeping, refusals and diagnostics — neither payment nor outcome. Counting these
        /// as "the effect happened" is how a sweep reports work that never occurred.</summary>
        public static bool IsBookkeepingLog(string m)
        {
            if (string.IsNullOrEmpty(m)) return true;
            foreach (var s in Bookkeeping)
                if (m.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static readonly string[] Bookkeeping =
        {
            "is pending", "not carried out", "effect skipped", "cost cannot be paid",
            "cannot pay the cost", "Unknown condition", "acknowledged for manual resolution", "Click ",
        };

        /// <summary>A leading "When ..." / "While ..." is a REACTIVE ability. The engine dispatches
        /// those separately and never queues them as a prompt, so forcing one through the pending path
        /// invents a question that never existed — it once made OP16-079 Yamato look like a frozen
        /// game.</summary>
        public static bool IsReactiveClause(string clause) =>
            Regex.IsMatch(clause ?? "", @"^(When|While)\b", RegexOptions.IgnoreCase);

        /// <summary>A body gated on a condition does nothing when the condition is false, which is
        /// legal — you may pay and get nothing. Sweeps asserting over these report unmet preconditions
        /// as defects; paidfornothing reported 52 that way before this was factored out.</summary>
        public static bool HasConditionalBody(string clause)
        {
            var m = Regex.Match(clause ?? "", @"^You may [^:]+:\s*(?<body>.+)$", RegexOptions.IgnoreCase);
            string body = m.Success ? m.Groups["body"].Value : clause ?? "";
            return Regex.IsMatch(body, @"^\s*If\b", RegexOptions.IgnoreCase);
        }

        /// <summary>Parenthesised text glosses a cost SYMBOL ("➀ (You may rest the specified number of
        /// DON!! cards …)"). It is reminder text, not a decision the player is offered.</summary>
        public static string WithoutReminderText(string line) =>
            Regex.Replace(line ?? "", @"\([^)]*\)", " ");
    }
}
