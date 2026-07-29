using System;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// "...you may trash THIS Character instead" - a guard that saves another body by sacrificing
    /// itself. Six cards use it: OP05-030 Donquixote Rosinante, OP09-012 Monster, OP13-008
    /// Emporio.Ivankov, OP13-047 Fossa, OP13-060 Amatsuki Toki, OP15-094 Roronoa Zoro.
    ///
    /// This behaviour ALREADY WORKED. I added an engine branch for it believing it was missing, and
    /// the negative control caught that: with the branch removed the suite still passed, because a
    /// handler further down the chain had been doing the job all along (my grep of the replacement
    /// branches was truncated, and my new branch simply shadowed the real one). The branch was
    /// reverted; this suite stays because the behaviour had no coverage either way.
    ///
    /// What actually misled me was the timing sweep, which listed OP05-030 as never firing. Its K.O.
    /// driver passed "K.O." as a command echo to ignore, and that swallowed the success message
    /// itself - "Rosinante: trashed instead of Nico Robin being K.O.'d". The echo is now the narrow
    /// " is K.O.'d". A filter wide enough to hide the evidence turns a working card into a bug report.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- sacrifice
    /// </summary>
    public static class SacrificeProtectionTest
    {
        private static int passed, failed;

        public static int Run()
        {
            Console.WriteLine("=== \"you may trash this Character instead\" ===");
            UsingItSavesTheVictimAndTrashesTheGuard();
            SkippingItLetsTheVictimDieAndKeepsTheGuard();
            TheGuardCannotSaveItself();
            Console.WriteLine($"sacrifice: {passed}/{passed + failed} passed ({failed} failed)");
            return failed == 0 ? 0 : 1;
        }

        private static void Check(string label, bool ok, string detail = "")
        {
            if (ok) { passed++; Console.WriteLine("  PASS  " + label); }
            else { failed++; Console.WriteLine("  FAIL  " + label + (detail.Length > 0 ? "  -- " + detail : "")); }
        }

        private static bool OnField(Board b, CardInstance c) =>
            b.S.CharacterArea.Any(x => x != null && x.InstanceId == c.InstanceId);

        private static void UsingItSavesTheVictimAndTrashesTheGuard()
        {
            var b = new Board();
            var guard = b.Character("OP05-030");          // "If your rested Character would be K.O.'d..."
            var victim = b.Character("ST29-009");
            victim.Rested = true;                         // the clause is about a RESTED Character
            b.St.ActiveSeat = "north";                    // "[Opponent's Turn]"

            GameEngine.AuditKoByEffectAsking(b.St, "south", victim.InstanceId);
            var pe = b.St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
            if (pe == null) { Check("Use saves the victim", false, "the protection was never offered"); return; }
            b.Apply(new GameCommand { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId });

            bool victimSaved = OnField(b, victim);
            bool guardGone = !OnField(b, guard) && b.S.Trash.Any(x => x.InstanceId == guard.InstanceId);
            Check("Use: the victim survives and the GUARD goes to the trash", victimSaved && guardGone,
                  $"victimSaved={victimSaved} guardTrashed={guardGone}");
        }

        private static void SkippingItLetsTheVictimDieAndKeepsTheGuard()
        {
            var b = new Board();
            var guard = b.Character("OP05-030");
            var victim = b.Character("ST29-009");
            victim.Rested = true;
            b.St.ActiveSeat = "north";

            GameEngine.AuditKoByEffectAsking(b.St, "south", victim.InstanceId);
            var pe = b.St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
            if (pe == null) { Check("Skip lets the victim die", false, "the protection was never offered"); return; }
            b.Apply(new GameCommand { Type = "passEffect", Seat = "south", EffectId = pe.EffectId });

            Check("Skip: the victim is removed and the guard is untouched",
                  !OnField(b, victim) && OnField(b, guard),
                  $"victimGone={!OnField(b, victim)} guardAlive={OnField(b, guard)}");
        }

        private static void TheGuardCannotSaveItself()
        {
            // Trashing yourself to avoid being K.O.'d is a no-op dressed as a save - either way the
            // card leaves the field. The guard must not consume its own protection on itself. Uses the
            // NON-asking seam deliberately: the question here is the outcome, not the prompt.
            var b = new Board();
            var guard = b.Character("OP05-030");
            guard.Rested = true;                          // it is itself a "rested Character"
            b.St.ActiveSeat = "north";

            GameEngine.AuditKoByEffect(b.St, "south", guard.InstanceId);
            foreach (var e in b.St.PendingEffects.Where(x => x != null && x.Seat == "south").ToList())
                b.Apply(new GameCommand { Type = "resolveEffect", Seat = "south", EffectId = e.EffectId });

            Check("a guard cannot trash itself to save itself", !OnField(b, guard),
                  "it survived its own K.O. by paying with itself");
        }

        private sealed class Board
        {
            public GameState St;
            public PlayerState S => St.Players["south"];
            private int slot, serial;

            public Board()
            {
                St = GameEngine.CreateMatch(new MatchConfig
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "sacrifice" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "south"; St.TurnNumber = 8;
                S.TurnsStarted = 4;
                for (int i = 0; i < 5; i++) S.CharacterArea[i] = null;
                S.Hand.Clear(); S.Life.Clear(); St.PendingEffects.Clear();
                for (int i = 0; i < 4; i++) S.Life.Add(Card("ST01-005", "life"));
            }

            public CardInstance Character(string id)
            { var c = Card(id, "character"); S.CharacterArea[slot++] = c; return c; }

            public void Apply(GameCommand c) => St = GameEngine.ApplyCommand(St, c);

            private CardInstance Card(string id, string zone) => new CardInstance
            {
                InstanceId = $"south-{id}-sac-{serial++}",
                CardId = id, Owner = "south", Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
