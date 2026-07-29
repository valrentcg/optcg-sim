using System;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// The three opponent-decision cards `opponentdecides` left half-driven, and the shape is the
    /// most involved "you may" in the pool: a NESTED opt-in where declining has a price.
    ///
    /// OP05-099 / OP15-059: "[On Your Opponent's Attack] You may rest this Character: Your opponent
    /// may trash 1 card from the top of their Life cards. If they do not, give up to 1 of your
    /// opponent's Leader or Character cards -2000 power during this turn."
    ///
    /// Two decisions by two different players, in order. South opts in by resting the Character;
    /// only then does NORTH get their own optional decision — pay a Life card, or eat -2000. Both
    /// branches must be reachable and they must be mutually exclusive: paying the Life card and
    /// STILL taking -2000 is the card doing double duty, and taking neither is the controller
    /// having rested a Character for nothing.
    ///
    /// OP01-038: "[On K.O.] Your opponent chooses 1 card from your hand; trash that card." The
    /// opponent picks, but the card leaves the CONTROLLER's hand — the same frame-of-reference
    /// trap as ST07-010, in a zone where getting it backwards would trash the wrong player's hand.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- opponentbranch
    /// </summary>
    public static class OpponentBranchTest
    {
        private static int passed, failed;

        private const string Nested = "OP05-099";   // Life-card version
        private const string NestedDon = "OP15-059"; // same shape, DON!! version

        public static int Run()
        {
            Console.WriteLine("=== Nested \"you may\": south opts in, then NORTH decides, and declining costs ===");
            SouthIsAskedFirst();
            NorthIsAskedOnlyAfterSouthOptsIn();
            NorthPaysTheLifeCardAndAvoidsThePenalty();
            NorthDeclinesAndTakesThePenalty();
            TheDonVersionBehavesTheSameWay();
            TheActiveQualifierIsHonoured();
            CannotPayIsTreatedAsWillNotPay();
            CannotPayTheDonVersionEither();
            Console.WriteLine($"opponentbranch: {passed}/{passed + failed} passed ({failed} failed)");
            return failed == 0 ? 0 : 1;
        }

        private static void Check(string label, bool ok, string detail = "")
        {
            if (ok) { passed++; Console.WriteLine("  PASS  " + label); }
            else { failed++; Console.WriteLine("  FAIL  " + label + (detail.Length > 0 ? "  -- " + detail : "")); }
        }

        private static void SouthIsAskedFirst()
        {
            var b = new Board(Nested);
            b.NorthAttacks();
            Check("north attacking offers SOUTH the opt-in",
                  b.Mine("south") != null,
                  "the [On Your Opponent's Attack] \"you may\" never prompted its controller");
        }

        private static void NorthIsAskedOnlyAfterSouthOptsIn()
        {
            var skipped = new Board(Nested);
            skipped.NorthAttacks();
            skipped.Skip("south");
            bool askedAnyway = skipped.Mine("north") != null;

            var taken = new Board(Nested);
            taken.NorthAttacks();
            taken.Use("south");
            bool askedAfter = taken.Mine("north") != null || taken.St.ActiveChoice?.Seat == "north";

            Check("north is asked only when south actually opts in",
                  !askedAnyway && askedAfter,
                  $"askedAfterSouthSkipped={askedAnyway} askedAfterSouthUsed={askedAfter}");
        }

        private static void NorthPaysTheLifeCardAndAvoidsThePenalty()
        {
            var b = new Board(Nested);
            b.NorthAttacks();
            b.Use("south");
            if (b.Mine("north") == null) { Check("north pays the Life card", false, "north was never asked"); return; }

            int nLife0 = b.N.Life.Count;
            int pow0 = GameEngine.GetPower(b.St, b.NorthBody);
            b.Use("north");

            Check("north paying a Life card avoids the -2000",
                  b.N.Life.Count == nLife0 - 1 && GameEngine.GetPower(b.St, b.NorthBody) == pow0,
                  $"north Life {nLife0}->{b.N.Life.Count}, north body power {pow0}"
                  + $"->{GameEngine.GetPower(b.St, b.NorthBody)} — paying AND taking -2000 is double duty");
        }

        private static void NorthDeclinesAndTakesThePenalty()
        {
            var b = new Board(Nested);
            b.NorthAttacks();
            b.Use("south");
            if (b.Mine("north") == null) { Check("north declines", false, "north was never asked"); return; }

            int nLife0 = b.N.Life.Count;
            int leaderPow0 = GameEngine.GetPower(b.St, b.N.Leader);
            int bodyPow0 = GameEngine.GetPower(b.St, b.NorthBody);
            b.Skip("north");
            b.SouthAnswersThePenalty();

            int leaderNow = GameEngine.GetPower(b.St, b.N.Leader);
            int bodyNow = GameEngine.GetPower(b.St, b.NorthBody);
            bool dropped = (leaderPow0 - leaderNow) == 2000 || (bodyPow0 - bodyNow) == 2000;
            Check("north declining keeps their Life and costs them -2000 instead",
                  b.N.Life.Count == nLife0 && dropped,
                  $"north Life {nLife0}->{b.N.Life.Count}, leader {leaderPow0}->{leaderNow}, "
                  + $"body {bodyPow0}->{bodyNow} — declining must not be free");
        }

        /// <summary>The sibling card, same sentence shape with DON!! instead of a Life card. A fix
        /// that only works for the card it was written against is the recurring failure here.</summary>
        private static void TheDonVersionBehavesTheSameWay()
        {
            var b = new Board(NestedDon);
            b.NorthAttacks();
            b.Use("south");
            bool asked = b.Mine("north") != null;
            if (!asked) { Check($"{NestedDon} asks north too", false, "the sibling card never reached north"); return; }

            int don0 = b.N.CostArea.Count;
            b.Use("north");
            Check($"{NestedDon} (DON!! version): north paying returns a DON!! and avoids the penalty",
                  b.N.CostArea.Count == don0 - 1,
                  $"north cost area {don0}->{b.N.CostArea.Count}");
        }

        /// <summary>"return 1 of their ACTIVE DON!! cards" must take an active one. The shared
        /// DON!!-paying helper returns RESTED first by design, so delegating to it would satisfy the
        /// count while ignoring the word the card is built around — and the count-only assertion
        /// above would never notice.</summary>
        private static void TheActiveQualifierIsHonoured()
        {
            var b = new Board(NestedDon);
            // One active DON!! among rested ones: a handler that ignores the qualifier will almost
            // certainly take a rested card here.
            for (int i = 0; i < b.N.CostArea.Count; i++) b.N.CostArea[i].Rested = true;
            b.N.CostArea[3].Rested = false;
            int rested0 = b.N.CostArea.Count(d => d.Rested);

            b.NorthAttacks();
            b.Use("south");
            if (b.Mine("north") == null) { Check("the active qualifier is honoured", false, "north was never asked"); return; }
            b.Use("north");

            int restedNow = b.N.CostArea.Count(d => d.Rested);
            bool anyActiveLeft = b.N.CostArea.Any(d => !d.Rested);
            Check("\"1 of their ACTIVE DON!!\" takes the active one, not a rested one",
                  restedNow == rested0 && !anyActiveLeft,
                  $"rested {rested0}->{restedNow} (must not change), activeLeft={anyActiveLeft}");
        }

        /// <summary>The claim I made when I chose NOT to write a capability table for this fix:
        ///
        ///   "If they physically cannot do it, the clause is unresolvable and the existing retire
        ///    path removes it — and that path fires the decline branch too, so CANNOT and WILL NOT
        ///    converge without a second rule to keep in sync."
        ///
        /// That was reasoning, not a result. If the retire path does not actually route through
        /// PassEffect, the penalty never fires and the controller loses the -2000 they paid a rested
        /// Character for — the card doing nothing at all, in the one case the fix was supposed to
        /// handle for free.</summary>
        private static void CannotPayIsTreatedAsWillNotPay()
        {
            var b = new Board(Nested);
            b.N.Life.Clear();                 // nothing to pay the "trash 1 from the top of their Life" with
            int leaderPow0 = GameEngine.GetPower(b.St, b.N.Leader);
            int bodyPow0 = GameEngine.GetPower(b.St, b.NorthBody);

            b.NorthAttacks();
            b.Use("south");
            b.SouthAnswersThePenalty();

            int leaderNow = GameEngine.GetPower(b.St, b.N.Leader);
            int bodyNow = GameEngine.GetPower(b.St, b.NorthBody);
            bool dropped = (leaderPow0 - leaderNow) == 2000 || (bodyPow0 - bodyNow) == 2000;
            Check("an opponent who CANNOT pay takes the -2000 just like one who declines",
                  dropped,
                  $"leader {leaderPow0}->{leaderNow}, body {bodyPow0}->{bodyNow} — the penalty never "
                  + "fired, so south rested a Character for nothing");
        }

        /// <summary>The capability check must be text-driven, not Life-shaped. The DON!! wording is
        /// the sibling that proves it generalises — a check that only understood "Life card" would
        /// leave this card exactly as broken as before.</summary>
        private static void CannotPayTheDonVersionEither()
        {
            var b = new Board(NestedDon);
            foreach (var d in b.N.CostArea) d.Rested = true;   // no ACTIVE DON!! to return
            int bodyPow0 = GameEngine.GetPower(b.St, b.NorthBody);
            int leaderPow0 = GameEngine.GetPower(b.St, b.N.Leader);

            b.NorthAttacks();
            b.Use("south");
            b.SouthAnswersThePenalty();

            int leaderNow = GameEngine.GetPower(b.St, b.N.Leader);
            int bodyNow = GameEngine.GetPower(b.St, b.NorthBody);
            Check("the DON!! version also penalises an opponent with no ACTIVE DON!!",
                  (leaderPow0 - leaderNow) == 2000 || (bodyPow0 - bodyNow) == 2000,
                  $"leader {leaderPow0}->{leaderNow}, body {bodyPow0}->{bodyNow}");
        }

        private sealed class Board
        {
            public GameState St;
            public PlayerState S => St.Players["south"];
            public PlayerState N => St.Players["north"];
            public CardInstance NorthBody;
            private CardInstance attacker;
            private int southSlot, northSlot, serial;

            public Board(string southCardId)
            {
                St = GameEngine.CreateMatch(new MatchConfig
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "opponent-branch" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "north"; St.TurnNumber = 9;
                foreach (var p in St.Players.Values)
                {
                    p.TurnsStarted = 4;
                    for (int i = 0; i < 5; i++) p.CharacterArea[i] = null;
                    p.Hand.Clear(); p.Life.Clear(); p.CostArea.Clear();
                    p.AbilityUsedThisTurn.Clear();
                    for (int i = 0; i < 4; i++) p.Life.Add(Card("ST01-005", p.Seat, "life"));
                    for (int i = 0; i < 10; i++)
                        p.CostArea.Add(new DonInstance { InstanceId = $"{p.Seat}-ob-don-{serial++}", Rested = false });
                    p.DonDeck = 5;
                }
                St.PendingEffects.Clear();
                St.ActiveChoice = null;

                Character("south", southCardId);          // the reactor
                NorthBody = Character("north", "EB03-002"); // a -2000 victim that is not the Leader
                attacker = Character("north", "OP15-040");
            }

            public PendingEffect Mine(string seat) =>
                St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == seat);

            public void NorthAttacks()
            {
                attacker.Rested = false; attacker.PlayedOnTurn = 0;
                St.ActiveSeat = "north"; St.Phase = "main";
                Apply(new GameCommand
                { Type = "declareAttack", Seat = "north", Attacker = attacker.InstanceId, Target = S.Leader?.InstanceId });
                if (Environment.GetEnvironmentVariable("OPT_DIAG") == "1")
                    foreach (var e in St.EventLog.TakeLast(8)) Console.WriteLine("      log: " + e.Message);
            }

            public void Skip(string seat)
            {
                var pe = Mine(seat);
                if (pe != null) Apply(new GameCommand { Type = "passEffect", Seat = seat, EffectId = pe.EffectId });
            }

            /// <summary>Answer the prompt for real, letting the engine say which cards it accepts
            /// rather than guessing the zone.</summary>
            public void Use(string seat)
            {
                for (int i = 0; i < 6; i++)
                {
                    var pe = Mine(seat);
                    if (pe == null) break;
                    string target = Candidates(seat)
                        .FirstOrDefault(x => GameEngine.IsValidEffectTarget(St, pe, x))?.InstanceId;
                    int peBefore = St.PendingEffects.Count, logBefore = St.EventLog.Count;
                    Apply(new GameCommand
                    { Type = "resolveEffect", Seat = seat, EffectId = pe.EffectId, Target = target });
                    if (Environment.GetEnvironmentVariable("OPT_DIAG") == "1")
                        foreach (var e in St.EventLog.Skip(logBefore)) Console.WriteLine($"      [{seat}] " + e.Message);
                    if (St.PendingEffects.Count == peBefore && St.EventLog.Count == logBefore) break;
                }
            }

            /// <summary>The -2000 is the CONTROLLER's to aim ("give up to 1 of your opponent's
            /// Leader or Character cards -2000"), so south answers it after north declines.</summary>
            public void SouthAnswersThePenalty() => Use("south");

            private System.Collections.Generic.IEnumerable<CardInstance> Candidates(string seat)
            {
                var me = St.Players[seat];
                var them = St.Players[seat == "south" ? "north" : "south"];
                foreach (var x in me.Life.AsEnumerable().Reverse()) yield return x;   // Life top is LAST
                foreach (var x in them.CharacterArea.Where(y => y != null)) yield return x;
                if (them.Leader != null) yield return them.Leader;
                foreach (var x in me.CharacterArea.Where(y => y != null)) yield return x;
                foreach (var x in me.Hand) yield return x;
            }

            public CardInstance Character(string seat, string id)
            {
                var p = St.Players[seat];
                int slot = seat == "south" ? southSlot++ : northSlot++;
                var c = Card(id, seat, "character");
                p.CharacterArea[slot] = c;
                return c;
            }

            public void Apply(GameCommand c) => St = GameEngine.ApplyCommand(St, c);

            private CardInstance Card(string id, string owner, string zone) => new CardInstance
            {
                InstanceId = $"{owner}-{id}-ob-{serial++}",
                CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
