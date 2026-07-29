using System;
using System.Collections.Generic;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// Generalises the PvP hole found in FindPendingEffect: does EVERY command refuse the wrong seat?
    ///
    /// That bug let the opponent resolve your pending effect by omitting the effect id. It was one
    /// command with one weak fallback, and there are two dozen commands. In a networked game both
    /// clients send commands into the same engine, so the engine is the only thing that can say no —
    /// a seat check missing anywhere is an action one player can take on the other's behalf.
    ///
    /// The method is deliberately blunt: build a board where SOUTH plainly owns the action, send the
    /// command as NORTH with arguments that would be valid for south, and assert nothing moved. A
    /// fingerprint over every zone is the assertion, so a command does not have to be understood in
    /// detail to be checked — only that it changed nothing when it should have been refused.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- wrongseat
    /// </summary>
    public static class WrongSeatSweep
    {
        public static int Run()
        {
            Console.WriteLine("=== Does any command accept the WRONG seat? ===");

            var accepted = new List<(string Cmd, string Before, string After)>();
            int tested = 0, refused = 0, threw = 0;

            foreach (var probe in Probes())
            {
                tested++;
                try
                {
                    var b = new Board();
                    var cmd = probe.Build(b);
                    if (cmd == null) { tested--; continue; }
                    cmd.Seat = "north";                    // the seat that must NOT own this
                    string before = b.Fingerprint();
                    b.Apply(cmd);
                    string after = b.Fingerprint();
                    if (before == after) refused++;
                    else accepted.Add((probe.Name, before, after));
                }
                catch (Exception ex)
                {
                    threw++;
                    Console.WriteLine($"  !! {probe.Name} threw: {ex.GetType().Name}: {ex.Message}");
                }
            }

            Console.WriteLine($"  commands probed                   : {tested}");
            Console.WriteLine($"  correctly refused the wrong seat  : {refused}");
            Console.WriteLine($"  ACCEPTED FROM THE WRONG SEAT      : {accepted.Count}");
            if (threw > 0) Console.WriteLine($"  threw                             : {threw}");

            foreach (var a in accepted)
            {
                Console.WriteLine();
                Console.WriteLine($"    {a.Cmd}");
                Console.WriteLine($"      before: {a.Before}");
                Console.WriteLine($"      after : {a.After}");
            }

            // In PvP the engine is the only referee. A command that moves the board for the seat that
            // does not own it is an action one player can take on the other's behalf.
            return accepted.Count == 0 ? 0 : 1;
        }

        private sealed class Probe
        {
            public string Name;
            public Func<Board, GameCommand> Build;
        }

        /// <summary>Each probe sets up a state where SOUTH owns the action and returns the command
        /// south would legitimately send. The sweep then sends it as north.</summary>
        private static IEnumerable<Probe> Probes()
        {
            yield return new Probe { Name = "resolveEffect (with id)", Build = b =>
            {
                var src = b.Character("south", "ST29-009");
                GameEngine.QueueClauseForTest(b.St, "south", src, "main", "You may rest this Character: Draw 1 card.");
                var pe = b.St.PendingEffects.FirstOrDefault(e => e.Seat == "south");
                return pe == null ? null : new GameCommand { Type = "resolveEffect", EffectId = pe.EffectId };
            } };

            yield return new Probe { Name = "resolveEffect (NO id — the hole that was fixed)", Build = b =>
            {
                var src = b.Character("south", "ST29-009");
                GameEngine.QueueClauseForTest(b.St, "south", src, "main", "You may rest this Character: Draw 1 card.");
                return b.St.PendingEffects.Any(e => e.Seat == "south") ? new GameCommand { Type = "resolveEffect" } : null;
            } };

            yield return new Probe { Name = "passEffect (NO id)", Build = b =>
            {
                var src = b.Character("south", "ST29-009");
                GameEngine.QueueClauseForTest(b.St, "south", src, "main", "You may rest this Character: Draw 1 card.");
                return b.St.PendingEffects.Any(e => e.Seat == "south") ? new GameCommand { Type = "passEffect" } : null;
            } };

            yield return new Probe { Name = "playCard from south's hand", Build = b =>
            {
                var c = b.Hand("south", "ST29-009");
                return new GameCommand { Type = "playCard", InstanceId = c.InstanceId, SlotIndex = 0 };
            } };

            yield return new Probe { Name = "activateMain on south's Character", Build = b =>
            {
                var c = b.Character("south", "EB03-028");
                return new GameCommand { Type = "activateMain", Target = c.InstanceId };
            } };

            yield return new Probe { Name = "rest south's Character", Build = b =>
            {
                var c = b.Character("south", "ST29-009");
                return new GameCommand { Type = "rest", Target = c.InstanceId };
            } };

            yield return new Probe { Name = "unrest south's Character", Build = b =>
            {
                var c = b.Character("south", "ST29-009"); c.Rested = true;
                return new GameCommand { Type = "unrest", Target = c.InstanceId };
            } };

            yield return new Probe { Name = "attachDon to south's Character", Build = b =>
            {
                var c = b.Character("south", "ST29-009");
                return new GameCommand { Type = "attachDon", Target = c.InstanceId, Amount = 1 };
            } };

            yield return new Probe { Name = "endTurn on south's turn", Build = b =>
                new GameCommand { Type = "endTurn" } };

            yield return new Probe { Name = "trash a card from south's hand", Build = b =>
            {
                var c = b.Hand("south", "ST29-009");
                return new GameCommand { Type = "trash", InstanceId = c.InstanceId };
            } };

            yield return new Probe { Name = "reorderHand in south's hand", Build = b =>
            {
                b.Hand("south", "ST29-009");
                var c2 = b.Hand("south", "ST29-004");
                return new GameCommand { Type = "reorderHand", InstanceId = c2.InstanceId, SlotIndex = 0 };
            } };

            yield return new Probe { Name = "declareAttack with south's Character", Build = b =>
            {
                var atk = b.Character("south", "EB03-002");
                atk.Rested = false; atk.PlayedOnTurn = 0;
                return new GameCommand { Type = "declareAttack", Attacker = atk.InstanceId, Target = b.N.Leader?.InstanceId };
            } };
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
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "wrong-seat" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "south"; St.TurnNumber = 8;
                foreach (var p in St.Players.Values)
                {
                    p.TurnsStarted = 4;
                    for (int i = 0; i < 5; i++) p.CharacterArea[i] = null;
                    p.Hand.Clear(); p.Life.Clear(); p.CostArea.Clear();
                    for (int i = 0; i < 4; i++) p.Life.Add(Card("ST01-005", p.Seat, "life"));
                    for (int i = 0; i < 10; i++)
                        p.CostArea.Add(new DonInstance { InstanceId = $"{p.Seat}-ws-don-{serial++}", Rested = false });
                    p.DonDeck = 0;
                }
                St.PendingEffects.Clear();
            }

            /// <summary>Every zone on both sides, plus turn and phase. Compared whole, so a command need
            /// not be understood in detail — only shown to have changed nothing.</summary>
            public string Fingerprint()
            {
                string Z(PlayerState p) =>
                    $"h{p.Hand.Count}/d{p.Deck.Count}/t{p.Trash.Count}/l{p.Life.Count}"
                    + $"/c{p.CharacterArea.Count(x => x != null)}/r{p.CharacterArea.Count(x => x != null && x.Rested)}"
                    + $"/don{p.CostArea.Count}/dr{p.CostArea.Count(x => x.Rested)}"
                    + $"/att{p.CharacterArea.Where(x => x != null).Sum(x => x.AttachedDonIds.Count)}"
                    + $"/hand[{string.Join(",", p.Hand.Select(x => x.CardId))}]";
                return Z(S) + "|" + Z(N) + $"|turn{St.TurnNumber}/{St.ActiveSeat}/{St.Phase}/{St.Status}"
                     + $"/pe{St.PendingEffects.Count}/battle{(St.Battle == null ? "-" : St.Battle.Step)}";
            }

            public CardInstance Hand(string seat, string id)
            { var c = Card(id, seat, "hand"); St.Players[seat].Hand.Add(c); return c; }

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
                InstanceId = $"{owner}-{id}-ws-{serial++}",
                CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
