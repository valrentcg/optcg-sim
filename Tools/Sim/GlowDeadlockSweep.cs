using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// The literal symptom in the OP15-114 Wyper report - "offered with nothing clickable" - swept
    /// across the pool.
    ///
    /// stallsweep hunts the opposite failure (a clause that needs NO target and is never auto-run).
    /// This one hunts a clause that DOES stop for a pick while no card anywhere satisfies the glow
    /// filter. The player sees a prompt and an inert board.
    ///
    /// The two halves of the UI are the same function: GameManager lights a card when
    /// GameEngine.IsValidEffectTarget says so, and the resolver accepts a click only if the same call
    /// agrees. So the filter is engine code and testable headlessly, even though the symptom is
    /// visual - glow == clickable is the invariant, and this checks the pair never disagree.
    ///
    /// Severity splits on Optional:
    ///   optional  -> Skip is still available. Annoying, and exactly what was reported.
    ///   mandatory -> Skip is disabled and nothing can be clicked. The game is FROZEN.
    /// RetireUnresolvablePendingEffects is supposed to make the second case impossible, so any hit
    /// there is a hole in that net.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- glowsweep
    /// </summary>
    public static class GlowDeadlockSweep
    {
        public static int Run()
        {
            Console.WriteLine("=== Waiting for a pick with NOTHING clickable ===");

            int tested = 0, waiting = 0, hadGlow = 0, threw = 0;
            var deadOptional = new List<(string Id, string Name, string Clause)>();
            var deadMandatory = new List<(string Id, string Name, string Clause)>();

            foreach (var def in CardData.Library.Values
                        .Where(c => c != null && !string.IsNullOrEmpty(c.Effect))
                        .GroupBy(c => c.Id).Select(g => g.First())
                        .OrderBy(c => c.Id, StringComparer.Ordinal))
            {
                foreach (var raw in def.Effect.Split('\n'))
                {
                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    // EVERY clause, not just "you may". Filtering to "you may" was self-defeating:
                    // those clauses are Optional by definition, so the FROZEN bucket - the only
                    // thing this sweep actually asserts - could never populate. It reported 0
                    // frozen even with RetireUnresolvablePendingEffects disabled, which is how
                    // the flaw surfaced.
                    var clause = Regex.Replace(raw, @"^\s*(\[[^\]]+\]\s*/?\s*)+", "").Trim();
                    if (clause.Length == 0) continue;
                    // A leading "When ..." / "While ..." is a REACTIVE ability: the engine has
                    // its own dispatch for those and never queues them as a player prompt.
                    // Forcing one through the pending path builds a prompt whose triggering
                    // event never happened - OP16-079 Yamato surfaced as a FROZEN game that
                    // way, which was the harness, not the engine.
                    if (Regex.IsMatch(clause, @"^(When|While)\b", RegexOptions.IgnoreCase)) continue;
                    tested++;

                    try
                    {
                        var b = new Board();
                        var src = b.Character("south", def.Id);
                        if (src == null) continue;

                        GameEngine.QueueClauseForTest(b.St, "south", src, "main", clause);
                        var pe = b.St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
                        if (pe == null) continue;
                        b.Apply(new GameCommand
                        { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId });

                        // Only a BOARD pick counts. A deck-look or an A/B choice has its own UI and
                        // never depended on the glow filter.
                        if (b.St.DeckLook != null || b.St.ActiveChoice != null) continue;
                        pe = b.St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
                        if (pe == null) continue;
                        waiting++;

                        if (b.AnythingClickable(pe)) { hadGlow++; continue; }
                        var row = (def.Id, def.Name ?? "", clause);
                        if (pe.Optional) deadOptional.Add(row); else deadMandatory.Add(row);
                    }
                    catch (Exception) { threw++; }
                }
            }

            Console.WriteLine($"  clauses tested (ALL, not just you-may): {tested}");
            Console.WriteLine($"  stopped for a BOARD pick          : {waiting}");
            Console.WriteLine($"  ...with something clickable       : {hadGlow}");
            Console.WriteLine($"  ...with NOTHING clickable (Skip)  : {deadOptional.Count}");
            Console.WriteLine($"  ...with NOTHING clickable, FROZEN : {deadMandatory.Count}");
            if (threw > 0) Console.WriteLine($"  threw                             : {threw}");

            if (deadMandatory.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("  FROZEN - mandatory, nothing clickable, Skip disabled:");
                foreach (var d in deadMandatory.Take(20))
                    Console.WriteLine($"    {d.Id,-10} {Trim(d.Name, 16),-16} {Trim(d.Clause, 104)}");
                if (deadMandatory.Count > 20) Console.WriteLine($"    ... and {deadMandatory.Count - 20} more");
            }
            if (deadOptional.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("  Skip-only (the reported Wyper symptom), first 20:");
                foreach (var d in deadOptional.Take(20))
                    Console.WriteLine($"    {d.Id,-10} {Trim(d.Name, 16),-16} {Trim(d.Clause, 104)}");
                if (deadOptional.Count > 20) Console.WriteLine($"    ... and {deadOptional.Count - 20} more");
            }

            // REPORTING sweep, not a gate. The mandatory case reads 0 even with
            // RetireUnresolvablePendingEffects disabled, so an assertion on it could never fail -
            // and a check that cannot fail is worse than no check, because it looks like coverage.
            // notargettest and replacementchoice cover that regression and do fail. Always exits 0;
            // read the numbers.
            return 0;
        }

        private static string Trim(string s, int n) =>
            string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s.Substring(0, n - 1) + "…");

        private sealed class Board
        {
            public GameState St;
            public PlayerState S => St.Players["south"];
            public PlayerState N => St.Players["north"];
            private int southSlot, northSlot, serial;

            public Board()
            {
                St = GameEngine.CreateMatch(new MatchConfig
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "glow-sweep" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "south"; St.TurnNumber = 8;
                S.TurnsStarted = 4; N.TurnsStarted = 4;
                for (int i = 0; i < 5; i++) { S.CharacterArea[i] = null; N.CharacterArea[i] = null; }
                S.Hand.Clear(); N.Hand.Clear(); S.Life.Clear(); N.Life.Clear();
                S.CostArea.Clear(); N.CostArea.Clear();
                // Something in every zone, so "nothing clickable" means the FILTER rejected everything
                // rather than the fixture having nothing to offer.
                for (int i = 0; i < 5; i++) { S.Life.Add(Card("ST01-005", "south", "life")); N.Life.Add(Card("ST01-005", "north", "life")); }
                for (int i = 0; i < 3; i++) { S.Trash.Add(Card("ST01-005", "south", "trash")); N.Trash.Add(Card("ST01-005", "north", "trash")); }
                Hand("south", "ST29-004"); Hand("south", "OP15-020"); Hand("south", "ST29-009");
                var nr = Character("north", "ST29-009"); if (nr != null) nr.Rested = true;
                Character("north", "OP15-040");
                Character("south", "EB03-002");
                for (int i = 0; i < 10; i++)
                    S.CostArea.Add(new DonInstance { InstanceId = $"south-gs-don-{serial++}", Rested = i >= 8 });
                N.CostArea.Add(new DonInstance { InstanceId = $"north-gs-don-{serial++}", Rested = false });
                S.DonDeck = 0; N.DonDeck = 0;
                St.PendingEffects.Clear();
            }

            /// <summary>Exactly what the UI lights: every card in every zone, both seats, run through
            /// GameEngine.IsValidEffectTarget - the same call GameManager uses to decide the glow and
            /// the same one the resolver uses to accept a click.</summary>
            public bool AnythingClickable(PendingEffect pe)
            {
                foreach (var seat in new[] { "south", "north" })
                {
                    var p = St.Players[seat];
                    var zones = new List<CardInstance>();
                    zones.AddRange(p.Hand);
                    zones.AddRange(p.Trash);
                    zones.AddRange(p.Life);
                    zones.AddRange(p.CharacterArea.Where(c => c != null));
                    if (p.Leader != null) zones.Add(p.Leader);
                    if (p.Stage != null) zones.Add(p.Stage);
                    foreach (var c in zones)
                        if (c != null && GameEngine.IsValidEffectTarget(St, pe, c)) return true;
                }
                return false;
            }

            public CardInstance Hand(string seat, string id)
            { var c = Card(id, seat, "hand"); St.Players[seat].Hand.Add(c); return c; }

            public CardInstance Character(string seat, string id)
            {
                var p = St.Players[seat];
                int slot = seat == "south" ? southSlot++ : northSlot++;
                if (slot > 4) return null;
                var c = Card(id, seat, "character");
                p.CharacterArea[slot] = c;
                return c;
            }

            public void Apply(GameCommand c) => St = GameEngine.ApplyCommand(St, c);

            private CardInstance Card(string id, string owner, string zone) => new CardInstance
            {
                InstanceId = $"{owner}-{id}-gs-{serial++}",
                CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
