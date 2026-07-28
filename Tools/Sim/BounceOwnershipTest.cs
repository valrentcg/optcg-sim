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
            ExactBaseCostIsAFilterNotACap();
            TypeFilteredBounceIsEnforced();
            BuffFiltersAreEnforced();
            OpponentOnlyTargetingOnTwoMoreHandlers();
            MiniMerryCostProbe();
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

        // OP03-047 Zeff: "[On Play] Place up to 1 Character with a base cost of 1 at the bottom of the
        // owner's deck." Only the "cost of N OR LESS" form was ever parsed, so an exact "base cost of 1"
        // parsed as no filter at all and any Character could be sunk. The glow and the resolver must agree
        // on which ones qualify.
        private static void ExactBaseCostIsAFilterNotACap()
        {
            const string Zeff = "Place up to 1 Character with a base cost of 1 at the bottom of the owner's deck.";
            var b = new Board();
            var src = b.Character("south", "OP03-047");
            var costOne = b.Character("north", "ST01-006");    // base cost 1 — the only legal target
            var costThree = b.Character("north", "ST01-005");  // base cost 3 — must be refused
            GameEngine.QueueClauseForTest(b.St, "south", src, "main", Zeff);
            var pe = b.St.PendingEffects.FirstOrDefault();
            if (pe == null) { Check("Zeff's placement queues", false, "no pending effect"); return; }

            Check("exact base cost: the cost-1 Character lights up",
                GameEngine.IsValidEffectTarget(b.St, pe, costOne));
            Check("exact base cost: a cost-3 Character does NOT light up",
                !GameEngine.IsValidEffectTarget(b.St, pe, costThree));

            b.Apply(new GameCommand
            { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId, Target = costThree.InstanceId });
            Check("exact base cost: the resolver refuses the cost-3 Character",
                b.N.CharacterArea.Any(c => c != null && c.InstanceId == costThree.InstanceId),
                "a cost-3 Character was sunk by a base-cost-1 effect");

            var pe2 = b.St.PendingEffects.FirstOrDefault();
            if (pe2 == null) { Check("Zeff survives a refused click", false, "effect consumed"); return; }
            b.Apply(new GameCommand
            { Type = "resolveEffect", Seat = "south", EffectId = pe2.EffectId, Target = costOne.InstanceId });
            Check("exact base cost: the cost-1 Character IS sunk",
                !b.N.CharacterArea.Any(c => c != null && c.InstanceId == costOne.InstanceId));
        }

        // A bounce may be restricted to a {type} — "return up to 1 of your {The Vinsmoke Family} type
        // Characters to the owner's hand" — and the handler checked type/cost/power/rested/ownership but
        // never the tag, so any Character you owned could be returned.
        private static void TypeFilteredBounceIsEnforced()
        {
            const string Typed = "Return up to 1 of your {Straw Hat Crew} type Characters to the owner's hand.";
            var b = new Board();
            var src = b.Character("south", "OP07-102");
            var inType = b.Character("south", "ST01-005");     // Jinbe — {Straw Hat Crew}
            var offType = b.Character("south", "OP15-040");    // Viola — {Dressrosa}, must be refused
            GameEngine.QueueClauseForTest(b.St, "south", src, "main", Typed);
            var pe = b.St.PendingEffects.FirstOrDefault();
            if (pe == null) { Check("type-filtered bounce queues", false, "no pending effect"); return; }

            Check("type-filtered bounce lights a Character of that type",
                GameEngine.IsValidEffectTarget(b.St, pe, inType));
            Check("type-filtered bounce does NOT light one outside the type",
                !GameEngine.IsValidEffectTarget(b.St, pe, offType));

            b.Apply(new GameCommand
            { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId, Target = offType.InstanceId });
            Check("type-filtered bounce: the resolver refuses the wrong type too",
                b.S.CharacterArea.Any(c => c != null && c.InstanceId == offType.InstanceId),
                "a Character outside the named type was returned");
        }

        // Power-buff targeting carried two unenforced filters. OP09-106 Nico Olvia says "Up to 1 of your
        // [Nico Robin] LEADER gains +3000 power" — the name regex accepted the nouns cards/Characters but
        // not Leader, so any Leader took it. OP09-007 Heat says "Up to 1 of your Leader with 4000 power or
        // less gains +1000 power" — only BASE-power caps were parsed, so a 5000-power Leader took it too.
        private static void BuffFiltersAreEnforced()
        {
            // Name filter with a Leader noun.
            {
                var b = new Board();
                var src = b.Character("south", "OP09-106");
                GameEngine.QueueClauseForTest(b.St, "south", src, "main",
                    "Up to 1 of your [Nico Robin] Leader gains +3000 power during this turn.");
                var pe = b.St.PendingEffects.FirstOrDefault();
                if (pe == null) { Check("named-Leader buff queues", false, "no pending effect"); return; }
                int before = GameEngine.GetPower(b.St, b.S.Leader);
                Check("a Leader that is not the named card does NOT light",
                    !GameEngine.IsValidEffectTarget(b.St, pe, b.S.Leader));
                b.Apply(new GameCommand
                { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId, Target = b.S.Leader.InstanceId });
                Check("…and the resolver refuses it too",
                    GameEngine.GetPower(b.St, b.S.Leader) == before,
                    $"Leader power {before} -> {GameEngine.GetPower(b.St, b.S.Leader)}");
            }
            // Current-power cap on a Leader buff (the fixture Leader is 5000, the cap is 4000).
            {
                var b = new Board();
                var src = b.Character("south", "OP09-007");
                GameEngine.QueueClauseForTest(b.St, "south", src, "main",
                    "Up to 1 of your Leader with 4000 power or less gains +1000 power during this turn.");
                var pe = b.St.PendingEffects.FirstOrDefault();
                if (pe == null) { Check("power-capped buff queues", false, "no pending effect"); return; }
                int before = GameEngine.GetPower(b.St, b.S.Leader);
                Check("a Leader above the power cap does NOT light",
                    !GameEngine.IsValidEffectTarget(b.St, pe, b.S.Leader), $"leader power={before}");
                b.Apply(new GameCommand
                { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId, Target = b.S.Leader.InstanceId });
                Check("…and the resolver refuses it too",
                    GameEngine.GetPower(b.St, b.S.Leader) == before,
                    $"Leader power {before} -> {GameEngine.GetPower(b.St, b.S.Leader)}");
            }
        }

        // Two more handlers that named "your opponent's Characters" and then never checked the seat.
        private static void OpponentOnlyTargetingOnTwoMoreHandlers()
        {
            // EB01-061 Bentham / OP16-104 Catarina Devon: the copy-power select took the seat out of
            // FindAnyInPlay and never looked at it, so this Character could copy ITS OWN power.
            {
                var b = new Board();
                var src = b.Character("south", "EB01-061");
                var mine = b.Character("south", "ST01-005");
                b.Character("north", "ST01-005");
                GameEngine.QueueClauseForTest(b.St, "south", src, "main",
                    "Select up to 1 of your opponent's Characters. This Character's base power becomes the same as the selected Character's power during this turn.");
                var pe = b.St.PendingEffects.FirstOrDefault();
                if (pe == null) { Check("copy-power select queues", false, "no pending effect"); return; }
                int before = GameEngine.GetPower(b.St, src);
                Check("copy-power: one of MY Characters does not light",
                    !GameEngine.IsValidEffectTarget(b.St, pe, mine));
                b.Apply(new GameCommand
                { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId, Target = mine.InstanceId });
                Check("copy-power: the resolver refuses my own Character too",
                    GameEngine.GetPower(b.St, src) == before,
                    $"source power {before} -> {GameEngine.GetPower(b.St, src)}");
            }
            // OP04-097 Otama: "Add up to 1 of your opponent's {Animal} or {SMILE} type Characters … to the
            // top of your opponent's Life cards face-up" was routed to the add-to-YOUR-Life handler, which
            // checks no ownership, so it buried one of YOUR Characters in your opponent's Life.
            {
                var b = new Board();
                var src = b.Character("south", "OP04-097");
                var mine = b.Character("south", "ST01-006");
                b.Character("north", "ST01-006");
                int myLifeBefore = b.S.Life.Count, oppLifeBefore = b.N.Life.Count;
                GameEngine.QueueClauseForTest(b.St, "south", src, "onPlay",
                    "Add up to 1 of your opponent's {Animal} or {SMILE} type Characters with a cost of 3 or less to the top of your opponent's Life cards face-up.");
                var pe = b.St.PendingEffects.FirstOrDefault();
                if (pe == null) { Check("add-to-opponent-Life queues", false, "no pending effect"); return; }
                Check("add-to-opponent-Life: one of MY Characters does not light",
                    !GameEngine.IsValidEffectTarget(b.St, pe, mine));
                b.Apply(new GameCommand
                { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId, Target = mine.InstanceId });
                Check("add-to-opponent-Life: the resolver refuses my own Character too",
                    b.S.CharacterArea.Any(c => c != null && c.InstanceId == mine.InstanceId)
                        && b.S.Life.Count == myLifeBefore && b.N.Life.Count == oppLifeBefore,
                    $"myLife {myLifeBefore}->{b.S.Life.Count}, oppLife {oppLifeBefore}->{b.N.Life.Count}");
            }
        }

        // EB01-011 Mini-Merry: "You may rest this card and place 1 of your Characters with 1000 base
        // power at the bottom of your deck: Draw 1 card." Both probe Characters are exactly 1000 base
        // power; the only difference between them is rested state, which a PLACE cost does not care about.
        private static void MiniMerryCostProbe()
        {
            var b = new Board();
            var stage = new CardInstance { InstanceId = "s-stage-mm", CardId = "EB01-011", Owner = "south", Zone = "stage" };
            b.S.Stage = stage;
            var active = b.Character("south", "ST01-006");            // 1000 base power, active
            var rested = b.Character("south", "ST02-004");            // 1000 base power, rested
            rested.Rested = true;
            GameEngine.QueueClauseForTest(b.St, "south", stage, "activateMain",
                "You may rest this card and place 1 of your Characters with 1000 base power at the bottom of your deck: Draw 1 card.");
            var pe = b.St.PendingEffects.FirstOrDefault();
            if (pe == null) { Check("Mini-Merry cost queues", false, "no pending effect"); return; }
            bool gActive = GameEngine.IsValidEffectTarget(b.St, pe, active);
            bool gRested = GameEngine.IsValidEffectTarget(b.St, pe, rested);
            Console.WriteLine($"    [probe] active1000={gActive} rested1000={gRested}");
            Check("a PLACE cost does not care whether the Character is rested", gActive == gRested,
                $"active={gActive} rested={gRested} — the rest-verb restriction is leaking onto a place conjunct");
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
