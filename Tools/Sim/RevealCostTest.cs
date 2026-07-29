using System;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// "You may reveal N &lt;filter&gt; card(s) from your hand: &lt;body&gt;" - a cost paid in
    /// INFORMATION. The cards stay in hand; what you spend is letting the opponent see them.
    ///
    /// The auto-payer counted matching cards and logged "Reveals N matching card(s) from hand (cost)"
    /// without recording which, so two things were wrong at once: the player never chose what to show,
    /// and the opponent learned nothing from a cost whose entire content is what they learn.
    ///
    /// The split mirrors the protection-discard fix: exactly enough matches is forced and needs no
    /// question; more than enough is a real choice and must be asked. The forced case is the control -
    /// without it, "always ask" would pass.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- revealcost
    /// </summary>
    public static class RevealCostTest
    {
        private static int passed, failed;

        // OP13-024 Gordon: "[Activate: Main] You may reveal 1 {Music} or {FILM} type card from your
        // hand: Set up to 2 of your DON!! cards as active at the end of this turn."
        private const string CLAUSE =
            "You may reveal 1 {FILM} type card from your hand: Draw 1 card.";

        public static int Run()
        {
            Console.WriteLine("=== \"You may reveal N <filter> from your hand\" ===");
            TwoMatchesIsAChoiceAndMustBeAsked();
            OneMatchIsForcedAndNeedsNoQuestion();
            NoMatchCannotPay();
            TheRevealNamesTheCard();
            EitherTypeOfADisjunctionCanPay();
            ARevealKeepsTheCardInHand();
            ThePlayerChoosesWHICHCardIsRevealed();
            Console.WriteLine($"revealcost: {passed}/{passed + failed} passed ({failed} failed)");
            return failed == 0 ? 0 : 1;
        }

        private static void Check(string label, bool ok, string detail = "")
        {
            if (ok) { passed++; Console.WriteLine("  PASS  " + label); }
            else { failed++; Console.WriteLine("  FAIL  " + label + (detail.Length > 0 ? "  -- " + detail : "")); }
        }

        /// <summary>A Character card carrying the {FILM} feature, found from the library so the test
        /// does not hard-code an id that a set rotation could invalidate.</summary>
        private static string FilmCardId()
        {
            foreach (var d in CardData.Library.Values)
            {
                if (d == null || !string.Equals(d.Type, "character", StringComparison.OrdinalIgnoreCase)) continue;
                if (d.Features != null && d.Features.Any(f => (f ?? "").IndexOf("FILM", StringComparison.OrdinalIgnoreCase) >= 0))
                    return d.Id;
            }
            return null;
        }

        private static void TwoMatchesIsAChoiceAndMustBeAsked()
        {
            string film = FilmCardId();
            if (film == null) { Check("two matches is a choice", false, "fixture: no {FILM} Character in the library"); return; }

            var b = new Board();
            b.Hand(film); b.Hand(film);                   // two legal reveals - a genuine choice
            b.Hand("ST29-004");                           // and one that does not match
            int hand0 = b.S.Hand.Count;

            GameEngine.QueueClauseForTest(b.St, "south", b.Character("ST29-010"), "main", CLAUSE);
            var pe = b.St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
            if (pe == null) { Check("two matches is a choice", false, "nothing was queued"); return; }
            b.Apply(new GameCommand { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId });

            bool stillAsking = b.St.PendingEffects.Any(e => e != null && e.Seat == "south");
            Check("with 2 legal reveals, Use asks WHICH card to show",
                  stillAsking && b.S.Hand.Count == hand0,
                  $"asking={stillAsking} hand {hand0}->{b.S.Hand.Count} "
                  + "(revealing must not move cards - it is an information cost)");
        }

        private static void OneMatchIsForcedAndNeedsNoQuestion()
        {
            // Control: one legal reveal is the only possible answer, so stopping to ask would be an
            // empty question. This is what stops the suite passing on a prompt-for-everything engine.
            string film = FilmCardId();
            if (film == null) { Check("one match is forced", false, "fixture: no {FILM} Character"); return; }

            var b = new Board();
            var only = b.Hand(film);
            b.Hand("ST29-004");
            GameEngine.QueueClauseForTest(b.St, "south", b.Character("ST29-010"), "main", CLAUSE);
            var pe = b.St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
            if (pe == null) { Check("one match is forced", false, "nothing was queued"); return; }
            b.Apply(new GameCommand { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId });

            bool drew = b.S.Hand.Any(c => c.CardId != only.CardId && c.CardId != "ST29-004")
                     || b.S.Hand.Count > 2;
            Check("with exactly 1 legal reveal, it pays outright and the body runs",
                  drew, $"hand={b.S.Hand.Count} - the body (\"Draw 1 card\") should have run");
        }

        private static void NoMatchCannotPay()
        {
            var b = new Board();
            b.Hand("ST29-004"); b.Hand("ST29-009");       // neither is {FILM}
            int hand0 = b.S.Hand.Count;
            GameEngine.QueueClauseForTest(b.St, "south", b.Character("ST29-010"), "main", CLAUSE);
            var pe = b.St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
            if (pe != null)
                b.Apply(new GameCommand { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId });
            Check("with no legal reveal, the cost is not paid and nothing is drawn",
                  b.S.Hand.Count == hand0,
                  $"hand {hand0}->{b.S.Hand.Count} - an unpayable cost must not pay itself");
        }

        private static void TheRevealNamesTheCard()
        {
            // A reveal whose whole content is information must say WHAT was revealed. Logging
            // "Reveals 1 matching card(s)" tells the opponent nothing at all.
            string film = FilmCardId();
            if (film == null) { Check("the reveal names the card", false, "fixture: no {FILM} Character"); return; }

            var b = new Board();
            var only = b.Hand(film);
            GameEngine.QueueClauseForTest(b.St, "south", b.Character("ST29-010"), "main", CLAUSE);
            var pe = b.St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
            if (pe == null) { Check("the reveal names the card", false, "nothing was queued"); return; }
            b.Apply(new GameCommand { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId });
            if (b.St.PendingEffects.Any(e => e != null && e.Seat == "south"))
            {
                var nxt = b.St.PendingEffects.First(e => e != null && e.Seat == "south");
                b.Apply(new GameCommand
                { Type = "resolveEffect", Seat = "south", EffectId = nxt.EffectId, Target = only.InstanceId });
            }

            bool named = b.St.EventLog.Any(l => (l.Message ?? "").IndexOf(film, StringComparison.OrdinalIgnoreCase) >= 0
                                             && (l.Message ?? "").IndexOf("eveal", StringComparison.Ordinal) >= 0);
            Check("the log says WHICH card was revealed, not just how many", named,
                  "the opponent cannot see a reveal that names no card");
        }

        /// <summary>"reveal 1 {Music} or {FILM} type card" - a card of EITHER type pays. The
        /// auto-payer read only the first tag, so a hand holding only the second type reported the
        /// cost as unpayable. 58 clauses in the pool carry a "{A} or {B}" disjunction.</summary>
        private static void EitherTypeOfADisjunctionCanPay()
        {
            string film = FilmCardId();
            if (film == null) { Check("either type of a disjunction pays", false, "fixture: no {FILM} Character"); return; }

            var b = new Board();
            b.Hand(film);                                 // the SECOND tag only - no {Music} at all
            int hand0 = b.S.Hand.Count;
            GameEngine.QueueClauseForTest(b.St, "south", b.Character("ST29-010"), "main",
                "You may reveal 1 {Music} or {FILM} type card from your hand: Draw 1 card.");
            var pe = b.St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
            if (pe == null) { Check("either type of a disjunction pays", false, "nothing was queued"); return; }
            b.Apply(new GameCommand { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId });

            Check("a {A} or {B} cost is payable with the SECOND type alone",
                  b.S.Hand.Count > hand0,
                  $"hand {hand0}->{b.S.Hand.Count} - the body (\"Draw 1 card\") never ran, so the "
                  + "cost was read as unpayable with a legal card in hand");
        }

        /// <summary>The property that makes a reveal a REVEAL: the card is shown and STAYS. Nothing
        /// in this suite asserted it — the forced-reveal case only checks that a draw happened, and
        /// an engine that trashed the revealed card would pass that just as well, since the draw
        /// masks the loss in the hand count.</summary>
        private static void ARevealKeepsTheCardInHand()
        {
            string film = FilmCardId();
            if (film == null) { Check("a reveal keeps the card", false, "fixture: no {FILM} Character"); return; }

            var b = new Board();
            var shown = b.Hand(film);
            b.Hand("ST29-004");
            GameEngine.QueueClauseForTest(b.St, "south", b.Character("ST29-010"), "main", CLAUSE);
            var pe = b.St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
            if (pe == null) { Check("a reveal keeps the card", false, "nothing was queued"); return; }
            b.Apply(new GameCommand { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId });

            Check("the revealed card is still in hand afterwards (a reveal is not a discard)",
                  b.S.Hand.Any(x => x.InstanceId == shown.InstanceId),
                  $"{shown.CardId} left the hand — it was spent, not shown");
        }

        /// <summary>With two legal reveals the player is asked; this checks the answer is honoured.
        /// "It asked" and "it did what I said" are different claims, and only the first was
        /// asserted — which is exactly how the auto-pick defects in this workstream survived.</summary>
        private static void ThePlayerChoosesWHICHCardIsRevealed()
        {
            string film = FilmCardId();
            if (film == null) { Check("the player chooses which", false, "fixture: no {FILM} Character"); return; }

            var b = new Board();
            var first = b.Hand(film);
            var second = b.Hand(film);
            GameEngine.QueueClauseForTest(b.St, "south", b.Character("ST29-010"), "main", CLAUSE);
            var pe = b.St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
            if (pe == null) { Check("the player chooses which", false, "nothing was queued"); return; }

            int logBefore = b.St.EventLog.Count;
            b.Apply(new GameCommand
            { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId, Target = second.InstanceId });

            // Both cards share a CardId, so the log cannot distinguish them — the observable is that
            // the pick was accepted (the effect advanced) and BOTH cards are still in hand.
            bool advanced = b.St.EventLog.Count > logBefore;
            bool bothKept = b.S.Hand.Any(x => x.InstanceId == first.InstanceId)
                         && b.S.Hand.Any(x => x.InstanceId == second.InstanceId);
            Check("naming one of two legal reveals is accepted, and both stay in hand",
                  advanced && bothKept,
                  $"advanced={advanced} bothKept={bothKept}");
        }

        private sealed class Board
        {
            public GameState St;
            public PlayerState S => St.Players["south"];
            private int slot, serial;

            public Board()
            {
                St = GameEngine.CreateMatch(new MatchConfig
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "reveal-cost" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "south"; St.TurnNumber = 8;
                S.TurnsStarted = 4;
                for (int i = 0; i < 5; i++) S.CharacterArea[i] = null;
                S.Hand.Clear(); S.Life.Clear(); S.CostArea.Clear(); St.PendingEffects.Clear();
                for (int i = 0; i < 4; i++) S.Life.Add(Card("ST01-005", "life"));
                for (int i = 0; i < 10; i++)
                    S.CostArea.Add(new DonInstance { InstanceId = $"south-rc-don-{serial++}", Rested = false });
                S.DonDeck = 0;
            }

            public CardInstance Hand(string id)
            { var c = Card(id, "hand"); S.Hand.Add(c); return c; }

            public CardInstance Character(string id)
            { var c = Card(id, "character"); S.CharacterArea[slot++] = c; return c; }

            public void Apply(GameCommand c) => St = GameEngine.ApplyCommand(St, c);

            private CardInstance Card(string id, string zone) => new CardInstance
            {
                InstanceId = $"south-{id}-rc-{serial++}",
                CardId = id, Owner = "south", Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
