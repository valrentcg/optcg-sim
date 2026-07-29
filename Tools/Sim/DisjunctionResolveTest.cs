using System;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// The half of the "{A} or {B}" question the glow sweep provably cannot answer.
    ///
    /// disjunctionsweep asks the GLOW filter, which reaches CardPassesFeatureFilter and iterates every
    /// tag, so it is disjunction-safe by construction and reports 24/24 clean. The RESOLVER and COST
    /// paths are a different story: there are 25 single-tag ParseCurlyBraceTag call sites, and the
    /// reveal-cost bug lived on exactly one of them.
    ///
    /// A divergence there is worse than either failure alone: the card LIGHTS UP because the glow says
    /// it is legal, the player clicks it, and the resolver - reading one tag - rejects it. A lit card
    /// that does nothing when clicked.
    ///
    /// So these drive real resolution with a board that supplies ONLY the second tag. If the effect
    /// completes, both paths agree.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- disjunctionresolve
    /// </summary>
    public static class DisjunctionResolveTest
    {
        private static int passed, failed;

        public static int Run()
        {
            Console.WriteLine("=== \"{A} or {B}\": does the RESOLVER accept the second tag? ===");
            ShirahoshiPlaysTheSecondType();
            GlowAndResolverAgreeOnTheSecondType();
            Console.WriteLine($"disjunctionresolve: {passed}/{passed + failed} passed ({failed} failed)");
            return failed == 0 ? 0 : 1;
        }

        private static void Check(string label, bool ok, string detail = "")
        {
            if (ok) { passed++; Console.WriteLine("  PASS  " + label); }
            else { failed++; Console.WriteLine("  FAIL  " + label + (detail.Length > 0 ? "  -- " + detail : "")); }
        }

        /// <summary>A Character with <paramref name="tag"/>, without <paramref name="without"/>, at or
        /// under the clause's cost cap.</summary>
        private static string FindChar(string tag, string without, int maxCost)
        {
            foreach (var d in CardData.Library.Values)
            {
                if (d == null || !string.Equals(d.Type, "character", StringComparison.OrdinalIgnoreCase)) continue;
                if (d.Features == null || d.Cost > maxCost) continue;
                bool has = d.Features.Any(f => (f ?? "").IndexOf(tag, StringComparison.OrdinalIgnoreCase) >= 0);
                bool no = !d.Features.Any(f => (f ?? "").IndexOf(without, StringComparison.OrdinalIgnoreCase) >= 0);
                if (has && no) return d.Id;
            }
            return null;
        }

        private const string CLAUSE =
            "Play up to 1 {Neptunian} or {Fish-Man Island} type Character card with a cost of 5 or less from your hand.";

        /// <summary>P-091 Shirahoshi. The hand holds ONLY the second type, so a resolver reading
        /// "{Neptunian}" alone has nothing legal and the effect cannot complete.</summary>
        private static void ShirahoshiPlaysTheSecondType()
        {
            string secondOnly = FindChar("Fish-Man Island", without: "Neptunian", maxCost: 5);
            if (secondOnly == null) { Check("second type is playable", false, "fixture: no {Fish-Man Island} Character at cost <= 5"); return; }

            var b = new Board();
            var src = b.Character("P-091");
            var target = b.Hand(secondOnly);

            GameEngine.QueueClauseForTest(b.St, "south", src, "onPlay", CLAUSE);
            var pe = b.St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
            if (pe == null) { Check("second type is playable", false, "nothing was queued"); return; }
            b.Apply(new GameCommand { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId });
            for (int i = 0; i < 3 && b.St.PendingEffects.Any(e => e != null && e.Seat == "south"); i++)
            {
                var nxt = b.St.PendingEffects.First(e => e != null && e.Seat == "south");
                b.Apply(new GameCommand
                { Type = "resolveEffect", Seat = "south", EffectId = nxt.EffectId, Target = target.InstanceId });
            }

            bool onField = b.S.CharacterArea.Any(c => c != null && c.InstanceId == target.InstanceId);
            Check("a {Fish-Man Island} card is playable by a \"{Neptunian} or {Fish-Man Island}\" effect",
                  onField,
                  "the card stayed in hand - the resolver read only the FIRST tag");
        }

        /// <summary>The divergence itself. A card the glow lights must be one the resolver accepts;
        /// anything else is a lit card that does nothing when clicked.</summary>
        private static void GlowAndResolverAgreeOnTheSecondType()
        {
            string secondOnly = FindChar("Fish-Man Island", without: "Neptunian", maxCost: 5);
            if (secondOnly == null) { Check("glow and resolver agree", false, "fixture: no suitable Character"); return; }

            var b = new Board();
            var src = b.Character("P-091");
            var target = b.Hand(secondOnly);
            GameEngine.QueueClauseForTest(b.St, "south", src, "onPlay", CLAUSE);
            var pe = b.St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
            if (pe == null) { Check("glow and resolver agree", false, "nothing was queued"); return; }

            bool lit = GameEngine.IsValidEffectTarget(b.St, pe, target);
            b.Apply(new GameCommand { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId });
            for (int i = 0; i < 3 && b.St.PendingEffects.Any(e => e != null && e.Seat == "south"); i++)
            {
                var nxt = b.St.PendingEffects.First(e => e != null && e.Seat == "south");
                b.Apply(new GameCommand
                { Type = "resolveEffect", Seat = "south", EffectId = nxt.EffectId, Target = target.InstanceId });
            }
            bool accepted = b.S.CharacterArea.Any(c => c != null && c.InstanceId == target.InstanceId);

            Check("glow and resolver agree about the second tag", lit == accepted,
                  $"glow={lit} resolver={accepted} — a card that lights but cannot be played is worse "
                  + "than one that never lights, because the click silently does nothing");
        }

        private sealed class Board
        {
            public GameState St;
            public PlayerState S => St.Players["south"];
            private int slot, serial;

            public Board()
            {
                St = GameEngine.CreateMatch(new MatchConfig
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "disjunction-resolve" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "south"; St.TurnNumber = 8;
                S.TurnsStarted = 4;
                for (int i = 0; i < 5; i++) S.CharacterArea[i] = null;
                S.Hand.Clear(); S.Life.Clear(); S.CostArea.Clear(); St.PendingEffects.Clear();
                for (int i = 0; i < 4; i++) S.Life.Add(Card("ST01-005", "life"));
                for (int i = 0; i < 10; i++)
                    S.CostArea.Add(new DonInstance { InstanceId = $"south-dr2-don-{serial++}", Rested = false });
                S.DonDeck = 0;
            }

            public CardInstance Hand(string id)
            { var c = Card(id, "hand"); S.Hand.Add(c); return c; }

            public CardInstance Character(string id)
            { var c = Card(id, "character"); S.CharacterArea[slot++] = c; return c; }

            public void Apply(GameCommand c) => St = GameEngine.ApplyCommand(St, c);

            private CardInstance Card(string id, string zone) => new CardInstance
            {
                InstanceId = $"south-{id}-dr2-{serial++}",
                CardId = id, Owner = "south", Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
