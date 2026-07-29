using System;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// A wording-variant the engine's literal phrase does not accept is now this workstream's most
    /// productive defect class: "You CAN" vs "You may", "[On Play]/[When Attacking]" vs a
    /// slash-blind stripper, "place THEM at the top of your deck" vs "place 1", and "Look at all
    /// YOUR Life cards" vs "all OF your". Four defects, one shape.
    ///
    /// So the shape was swept: every literal card-text phrase the engine matches on (302 of them),
    /// against the pool, looking for a card that prints a near-variant and NOT the phrase. Five
    /// candidates; three are false positives or handled elsewhere, and this file pins the one that
    /// needed checking by measurement rather than by reading.
    ///
    /// OP02-024 Moby Dick: "[Your Turn] If you have 1 or less Life cards, your [Edward.Newgate] and
    /// ALL YOUR Characters with a type including "Whitebeard Pirates" gain +2000 power."
    /// The engine's phrase is "all OF your Characters". A passive buff that silently does not apply
    /// is invisible: no prompt, no log line, just a Character that loses a fight it should win.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- wordingvariant
    /// </summary>
    public static class WordingVariantTest
    {
        private static int passed, failed;

        public static int Run()
        {
            Console.WriteLine("=== Wording variants: does the buff whose phrase differs still apply? ===");
            MobyDickBuffsWhitebeardCharacters();
            MobyDickDoesNothingAboveTheLifeThreshold();
            Console.WriteLine($"wordingvariant: {passed}/{passed + failed} passed ({failed} failed)");
            return failed == 0 ? 0 : 1;
        }

        private static void Check(string label, bool ok, string detail = "")
        {
            if (ok) { passed++; Console.WriteLine("  PASS  " + label); }
            else { failed++; Console.WriteLine("  FAIL  " + label + (detail.Length > 0 ? "  -- " + detail : "")); }
        }

        /// <summary>A {Whitebeard Pirates} Character with the Stage out and Life at 1 must be +2000.</summary>
        private static void MobyDickBuffsWhitebeardCharacters()
        {
            string wb = WhitebeardCharacterId();
            if (wb == null) { Check("Moby Dick buffs Whitebeard Characters", false, "fixture: no {Whitebeard Pirates} Character in the library"); return; }

            var bare = new Board(lifeCards: 1, withStage: false);
            var ch0 = bare.Character(wb);
            int basePower = GameEngine.GetPower(bare.St, ch0);

            var b = new Board(lifeCards: 1, withStage: true);
            var ch = b.Character(wb);
            int withStage = GameEngine.GetPower(b.St, ch);

            Check("Moby Dick's \"all YOUR Characters\" buff reaches a {Whitebeard Pirates} Character",
                  withStage - basePower == 2000,
                  $"power {basePower} -> {withStage} (want +2000) — the engine matches "
                  + "\"all OF your Characters\" and the card prints \"all your Characters\"");
        }

        /// <summary>The condition has to still bite, or "+2000 always" would pass the case above.</summary>
        private static void MobyDickDoesNothingAboveTheLifeThreshold()
        {
            string wb = WhitebeardCharacterId();
            if (wb == null) { Check("Moby Dick respects the Life condition", false, "fixture: no {Whitebeard Pirates} Character"); return; }

            var bare = new Board(lifeCards: 4, withStage: false);
            var ch0 = bare.Character(wb);
            int basePower = GameEngine.GetPower(bare.St, ch0);

            var b = new Board(lifeCards: 4, withStage: true);   // 4 Life: "1 or less" is false
            var ch = b.Character(wb);

            Check("with 4 Life the buff does NOT apply",
                  GameEngine.GetPower(b.St, ch) == basePower,
                  $"power {basePower} -> {GameEngine.GetPower(b.St, ch)} with the condition unmet");
        }

        private static string WhitebeardCharacterId()
        {
            foreach (var def in CardData.Library.Values.OrderBy(d => d?.Id, StringComparer.Ordinal))
            {
                if (def == null || !string.Equals(def.Type, "character", StringComparison.OrdinalIgnoreCase)) continue;
                if (def.HasFeature("Whitebeard Pirates")) return def.Id;
            }
            return null;
        }

        private sealed class Board
        {
            public GameState St;
            public PlayerState S => St.Players["south"];
            private int slot, serial;

            public Board(int lifeCards, bool withStage)
            {
                St = GameEngine.CreateMatch(new MatchConfig
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "wording-variant" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "south"; St.TurnNumber = 8;
                foreach (var p in St.Players.Values)
                {
                    p.TurnsStarted = 4;
                    for (int i = 0; i < 5; i++) p.CharacterArea[i] = null;
                    p.Hand.Clear(); p.Life.Clear(); p.CostArea.Clear();
                    for (int i = 0; i < lifeCards; i++) p.Life.Add(Make("ST01-005", p.Seat, "life"));
                    for (int i = 0; i < 10; i++)
                        p.CostArea.Add(new DonInstance { InstanceId = $"{p.Seat}-wv-don-{serial++}", Rested = false });
                    p.DonDeck = 0;
                }
                if (withStage) S.Stage = Make("OP02-024", "south", "stage");   // Moby Dick
                St.PendingEffects.Clear();
            }

            public CardInstance Character(string id)
            { var c = Make(id, "south", "character"); S.CharacterArea[slot++ % 5] = c; return c; }

            private CardInstance Make(string id, string owner, string zone) => new CardInstance
            {
                InstanceId = $"{owner}-{id}-wv-{serial++}",
                CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
