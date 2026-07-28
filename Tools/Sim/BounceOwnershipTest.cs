using System;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// From the glow-vs-resolver sweep: ~85 of the 108 "the resolver accepts a card that never lights"
    /// findings are BOUNCE clauses — "Return up to 1 of your opponent's Characters … to the owner's
    /// hand". The glow correctly refuses your own Characters; the resolver accepted them.
    ///
    /// That is reachable in a real game: OnCardClick dispatches resolveEffect for ANY rendered card
    /// while an effect is pending and lets the engine validate, so clicking one of your own Characters
    /// on an opponent-only bounce returned YOUR card to YOUR hand.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- bouncetest
    /// </summary>
    public static class BounceOwnershipTest
    {
        private static int passed;
        private static int failed;

        public static int Run()
        {
            Console.WriteLine("=== Bounce: whose Characters may actually be returned ===");
            OpponentOnlyBounceRefusesMyOwnCharacters();
            OwnBounceStillWorks();
            UnqualifiedBounceOffersBothSides();
            SweepEveryOpponentOnlyBounceClause();
            Console.WriteLine($"bouncetest: {passed}/{passed + failed} passed ({failed} failed)");
            return failed == 0 ? 0 : 1;
        }

        private const string OppOnly =
            "Return up to 1 of your opponent's Characters with a cost of 4 or less to the owner's hand.";
        private const string OwnOnly =
            "Return up to 1 of your Characters to the owner's hand.";

        private static void OpponentOnlyBounceRefusesMyOwnCharacters()
        {
            var b = new Board();
            var src = b.Character("south", "OP07-102");
            var mine = b.Character("south", "ST29-009");     // cost 4, mine — must be refused
            var theirs = b.Character("north", "ST29-009");   // cost 4, theirs — the legal target
            GameEngine.QueueClauseForTest(b.St, "south", src, "main", OppOnly);
            var pe = b.St.PendingEffects.FirstOrDefault();
            if (pe == null) { Check("opponent-only bounce queues", false, "no pending effect"); return; }

            Check("the opponent's Character lights up", GameEngine.IsValidEffectTarget(b.St, pe, theirs));
            Check("my own Character does NOT light up", !GameEngine.IsValidEffectTarget(b.St, pe, mine));

            // The click the glow refuses must be refused by the resolver too — the board is clickable
            // regardless of glow, so this is reachable by a misclick.
            b.Apply(new GameCommand
            { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId, Target = mine.InstanceId });
            bool stillMine = b.S.CharacterArea.Any(c => c != null && c.InstanceId == mine.InstanceId);
            Check("clicking my own Character does NOT return it to my hand", stillMine,
                $"my Character left the board; hand={b.S.Hand.Count}");

            var pe2 = b.St.PendingEffects.FirstOrDefault();
            if (pe2 == null) { Check("the effect survives a refused click", false, "effect consumed"); return; }
            b.Apply(new GameCommand
            { Type = "resolveEffect", Seat = "south", EffectId = pe2.EffectId, Target = theirs.InstanceId });
            Check("clicking the opponent's Character DOES return it",
                !b.N.CharacterArea.Any(c => c != null && c.InstanceId == theirs.InstanceId)
                    && b.N.Hand.Any(c => c.InstanceId == theirs.InstanceId));
        }

        // The mirror case must keep working: a clause that says "your Characters" bounces YOURS.
        private static void OwnBounceStillWorks()
        {
            var b = new Board();
            var src = b.Character("south", "OP07-102");
            var mine = b.Character("south", "ST29-009");
            var theirs = b.Character("north", "ST29-009");
            GameEngine.QueueClauseForTest(b.St, "south", src, "main", OwnOnly);
            var pe = b.St.PendingEffects.FirstOrDefault();
            if (pe == null) { Check("own-bounce queues", false, "no pending effect"); return; }

            Check("own-bounce lights my Character", GameEngine.IsValidEffectTarget(b.St, pe, mine));
            Check("own-bounce does NOT light the opponent's", !GameEngine.IsValidEffectTarget(b.St, pe, theirs));
            b.Apply(new GameCommand
            { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId, Target = mine.InstanceId });
            Check("own-bounce returns my Character",
                !b.S.CharacterArea.Any(c => c != null && c.InstanceId == mine.InstanceId));
        }

        // An UNQUALIFIED "Return up to 1 Character …" names no side, so per the card it may take either
        // player's Character (OP04-044 Kaido is the canonical one). The resolver allows both; the glow is
        // the half under test here.
        private static void UnqualifiedBounceOffersBothSides()
        {
            const string Unqualified = "Return up to 1 Character with a cost of 3 or less to the owner's hand.";
            var b = new Board();
            var src = b.Character("south", "OP07-102");
            var mine = b.Character("south", "ST01-006");     // cost 1
            var theirs = b.Character("north", "ST01-006");   // cost 1
            GameEngine.QueueClauseForTest(b.St, "south", src, "main", Unqualified);
            var pe = b.St.PendingEffects.FirstOrDefault();
            if (pe == null) { Check("unqualified bounce queues", false, "no pending effect"); return; }
            Check("an unqualified bounce lights the opponent's Character",
                GameEngine.IsValidEffectTarget(b.St, pe, theirs));
            Check("an unqualified bounce also lights MY Character (the card names no side)",
                GameEngine.IsValidEffectTarget(b.St, pe, mine));
        }

        // Every printed opponent-only bounce, not just the one the sweep happened to name.
        private static void SweepEveryOpponentOnlyBounceClause()
        {
            var clauses = CardData.Library.Values
                .Where(c => c != null && !string.IsNullOrEmpty(c.Effect))
                .GroupBy(c => c.Id).Select(g => g.First())
                .SelectMany(c => c.Effect.Split('\n').Select(l => new { c.Id, Line = l.Trim() }))
                .Where(x => System.Text.RegularExpressions.Regex.IsMatch(x.Line,
                            @"^\s*(?:\[[^\]]+\]\s*/?\s*)*Return up to \d+ of your opponent's Characters[^.]*to the owner's hand\.?$",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                .GroupBy(x => x.Line).Select(g => g.First())
                .ToList();

            var leaked = new System.Collections.Generic.List<string>();
            foreach (var c in clauses)
            {
                var b = new Board();
                var src = b.Character("south", "OP07-102");
                var mine = b.Character("south", "ST29-009");
                b.Character("north", "ST29-009");
                GameEngine.QueueClauseForTest(b.St, "south", src, "main", c.Line);
                var pe = b.St.PendingEffects.FirstOrDefault();
                if (pe == null) continue;
                b.Apply(new GameCommand
                { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId, Target = mine.InstanceId });
                if (!b.S.CharacterArea.Any(x => x != null && x.InstanceId == mine.InstanceId))
                    leaked.Add(c.Id + " :: " + c.Line.Substring(0, Math.Min(60, c.Line.Length)));
            }

            Console.WriteLine($"    swept {clauses.Count} distinct opponent-only bounce clauses");
            Check("no opponent-only bounce will return one of MY Characters",
                leaked.Count == 0, leaked.Count == 0 ? null : string.Join(" | ", leaked.Take(5)));
        }

        // ---- plumbing ---------------------------------------------------------------------------

        private static void Check(string name, bool ok, string detail = null)
        {
            if (ok) { passed++; Console.WriteLine("  PASS  " + name); }
            else { failed++; Console.WriteLine("  FAIL  " + name + (string.IsNullOrEmpty(detail) ? "" : " — " + detail)); }
        }

        private sealed class Board
        {
            public GameState St;
            public PlayerState S => St.Players["south"];
            public PlayerState N => St.Players["north"];
            private int southSlot, northSlot, serial;

            public Board()
            {
                St = GameEngine.CreateMatch(new MatchConfig
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "bounce-ownership" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "south"; St.TurnNumber = 8;
                S.TurnsStarted = 4; N.TurnsStarted = 4;
                for (int i = 0; i < 5; i++) { S.CharacterArea[i] = null; N.CharacterArea[i] = null; }
                S.Hand.Clear(); N.Hand.Clear();
                S.Life.Clear(); N.Life.Clear();
                S.CostArea.Clear(); N.CostArea.Clear();
                S.DonDeck = 10; N.DonDeck = 10;
                S.Leader.Rested = false; N.Leader.Rested = false;
                St.PendingEffects.Clear();
                for (int i = 0; i < 3; i++) { S.Life.Add(Card("ST01-005", "south", "life")); N.Life.Add(Card("ST01-005", "north", "life")); }
                for (int i = 0; i < 6; i++)
                {
                    S.CostArea.Add(new DonInstance { InstanceId = $"s-bo-don-{serial++}", Rested = i >= 3 });
                    N.CostArea.Add(new DonInstance { InstanceId = $"n-bo-don-{serial++}", Rested = i >= 3 });
                }
            }

            public CardInstance Character(string seat, string id)
            {
                var p = seat == "south" ? S : N;
                var c = Card(id, seat, "character");
                p.CharacterArea[seat == "south" ? southSlot++ : northSlot++] = c;
                return c;
            }

            public void Apply(GameCommand c) => St = GameEngine.ApplyCommand(St, c);

            private CardInstance Card(string id, string owner, string zone) => new CardInstance
            {
                InstanceId = $"{owner}-{id}-bo-{serial++}",
                CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
