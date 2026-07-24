using System;
using System.Collections.Generic;

namespace OnePieceTcg.Engine.Puzzles
{
    /// <summary>
    /// Authored mechanic-combination puzzles. Each entry is a different dependency graph, not a seeded reskin:
    /// cost reduction into K.O.; Banish suppressing a Trigger; repeated restand; resource reservation across an
    /// opponent turn; and removing crackback before developing next-turn lethal.
    ///
    /// These builders are candidates until `mechanicpuzzlecheck` proves their configured objective. The
    /// player-facing manifest should remain small and add only mechanically distinct positions.
    /// </summary>
    public static class MechanicPuzzleLibrary
    {
        public static List<AuthoredPuzzle> All() => new List<AuthoredPuzzle>
        {
            new AuthoredPuzzle
            {
                Id = "mechanic-cost-collapse",
                Title = "Collapse the Wall",
                Category = "cost-reduction-removal-order",
                Difficulty = 3,
                Objective = "Win this turn. The blocker cannot be removed until its cost reaches 0.",
                Teaches = "Attack sequencing can be removal sequencing: Brook must reduce and K.O. the wall before it absorbs another attacker.",
                Mechanics = new[] { "Cost reduction", "K.O.", "When Attacking", "Attack order", "DON!! allocation" },
                Build = BuildCostCollapse,
            },
            new AuthoredPuzzle
            {
                Id = "mechanic-banish-trigger",
                Title = "Silence the Trigger",
                Category = "banish-trigger-double-attack",
                Difficulty = 3,
                Objective = "Win this turn. A Life Trigger creates a new blocker unless the correct attack has Banish.",
                Teaches = "Grant Banish to the Double Attack and send it first; otherwise Sanji's Trigger becomes the Blocker that stops the final hit.",
                Mechanics = new[] { "Banish", "Trigger denial", "Double Attack", "On Play targeting", "Attack order" },
                Build = BuildBanishTrigger,
            },
            new AuthoredPuzzle
            {
                Id = "mechanic-triple-zoro",
                Title = "Three-Sword Sequence",
                Category = "restand-resource-sequencing",
                Difficulty = 3,
                Objective = "Win this turn through a blocker and a 6000-point Counter Event.",
                Teaches = "All three Zoro attacks are required. Spending any of the three DON!! elsewhere removes one restand and one necessary attack.",
                Mechanics = new[] { "Restand", "Once per turn", "Counter Event", "Blocker", "Resource sequencing" },
                Build = BuildTripleZoro,
            },
            new AuthoredPuzzle
            {
                Id = "mechanic-counter-reserve",
                Title = "The DON!! You Don't Spend",
                Category = "two-turn-setup-defense-finish",
                Difficulty = 4,
                PlayerTurnLimit = 2,
                Objective = "Force the win by the end of your next turn. Survive their attack without sacrificing a future attacker.",
                Teaches = "Leave one DON!! active after developing Franky, block with Kuma, and protect it with Yasakani Sacred Jewel so all three attackers survive for next turn.",
                Mechanics = new[] { "Multi-turn setup", "Blocker", "Counter Event", "Discard cost", "Resource reservation", "Next-turn lethal" },
                Build = BuildCounterReserve,
            },
            new AuthoredPuzzle
            {
                Id = "mechanic-remove-crackback",
                Title = "Win the Turn Before It Starts",
                Category = "two-turn-board-control",
                Difficulty = 4,
                PlayerTurnLimit = 2,
                Objective = "Force the win by the end of your next turn. You cannot survive both opposing attackers.",
                Teaches = "The correct first attack is board control, not Life pressure. Remove the crackback attacker, develop the third body, defend once, then finish.",
                Mechanics = new[] { "Multi-turn sequencing", "Attack a Character", "Crackback math", "Counter", "Develop before lethal" },
                Build = BuildRemoveCrackback,
            },
        };

        private static GameState BuildCostCollapse()
        {
            var b = new Builder("mechanic-cost-collapse");
            b.SetLeader("south", "ST06-001", rested: true);
            b.SetLeader("north", "ST03-001");
            b.Character("south", "EB01-046");          // Brook: -1 cost, then KO cost 0
            b.Character("south", "ST01-010");
            b.Character("south", "ST01-010");
            b.Character("north", "OP11-083");          // unconditional cost-1 Blocker
            b.Life("north", "EB03-026", 2);
            b.Hand("north", "EB01-025");               // 1K counter
            b.Don("south", 2);
            return b.State;
        }

        private static GameState BuildBanishTrigger()
        {
            var b = new Builder("mechanic-banish-trigger");
            b.SetLeader("south", "ST03-001", rested: true);
            b.SetLeader("north", "ST07-001");
            b.Character("south", "OP02-087");          // printed Double Attack
            b.Character("south", "ST01-010");          // final 6K attacker
            b.Hand("south", "OP06-101");               // O-Nami grants Banish
            b.Life("north", "EB03-026", 1);             // bottom Life
            b.Life("north", "OP04-104", 1, append: true); // top Life: Trigger plays a Blocker
            b.Hand("north", "EB02-052");                // pays Sanji's Trigger cost; no Counter
            b.Don("south", 2);
            return b.State;
        }

        private static GameState BuildTripleZoro()
        {
            var b = new Builder("mechanic-triple-zoro");
            b.SetLeader("south", "OP01-001");
            b.SetLeader("north", "ST06-001");
            b.Character("south", "OP06-118");          // 1 DON when-attacking restand + 2 DON main restand
            b.Character("north", "OP02-108");          // one attack absorbed
            b.Life("north", "EB03-026", 1);
            b.Hand("north", "OP07-095");                // +6K because trash >= 10
            b.Don("south", 3);
            b.Don("north", 2);
            b.Trash("north", "EB03-026", 10);
            return b.State;
        }

        private static GameState BuildCounterReserve()
        {
            var b = new Builder("mechanic-counter-reserve");
            b.SetLeader("south", "OP11-040", rested: true);
            b.SetLeader("north", "OP11-040");
            b.Character("south", "OP01-074", playedThisTurn: true); // 5K Blocker; required third attacker next turn
            b.Hand("south", "ST01-010");                           // spend 4, leaving exactly 1 active
            b.Hand("south", "OP02-118");                           // Yasakani: protect the blocker from battle K.O.
            b.Hand("south", "EB02-052");                           // discard cost for Yasakani
            b.Don("south", 5);
            b.Life("north", "EB03-026", 2);
            b.Life("south", "EB03-026", 0);
            return b.State;
        }

        private static GameState BuildRemoveCrackback()
        {
            var b = new Builder("mechanic-remove-crackback");
            b.SetLeader("south", "ST01-001", rested: true);
            b.SetLeader("north", "ST03-001");
            b.Character("south", "ST01-010");                      // must attack the rested opposing Character
            b.Character("north", "ST01-010", rested: true);
            b.Hand("south", "ST01-010");                           // develop third next-turn attacker
            b.Hand("south", "OP11-045");                           // 2K counter survives the one remaining attack
            b.Don("south", 4);
            b.Life("south", "EB03-026", 0);
            b.Life("north", "EB03-026", 2);
            return b.State;
        }

        private sealed class Builder
        {
            private int _next;
            public GameState State { get; }

            public Builder(string seed)
            {
                State = GameEngine.CreateMatch(new MatchConfig
                {
                    SouthDeck = "st02",
                    NorthDeck = "st03",
                    Seed = seed,
                });
                State.Status = "active";
                State.Phase = "main";
                State.ActiveSeat = "south";
                State.TurnNumber = 8;
                State.Battle = null;
                State.ActiveChoice = null;
                State.DeckLook = null;
                State.PendingEffects.Clear();
                State.Selected = null;

                foreach (var seat in new[] { "south", "north" })
                {
                    var p = State.Players[seat];
                    p.TurnsStarted = 4;
                    p.Hand.Clear();
                    p.Life.Clear();
                    p.Trash.Clear();
                    p.CostArea.Clear();
                    p.Stage = null;
                    p.DonDeck = 0;
                    for (int i = 0; i < p.CharacterArea.Count; i++) p.CharacterArea[i] = null;
                    p.Deck.Clear();
                    for (int i = 0; i < 8; i++) p.Deck.Add(Card("EB02-052", seat, "deck"));
                    p.Leader.Rested = false;
                    p.Leader.PlayedOnTurn = null;
                    p.Leader.AttachedDonIds.Clear();
                }
            }

            public void SetLeader(string seat, string id, bool rested = false)
            {
                var leader = State.Players[seat].Leader;
                leader.CardId = id;
                leader.Rested = rested;
                leader.PlayedOnTurn = null;
                leader.AttachedDonIds.Clear();
            }

            public void Character(string seat, string id, bool rested = false, bool playedThisTurn = false)
            {
                var p = State.Players[seat];
                for (int i = 0; i < p.CharacterArea.Count; i++)
                {
                    if (p.CharacterArea[i] != null) continue;
                    var c = Card(id, seat, "character");
                    c.Rested = rested;
                    c.PlayedOnTurn = playedThisTurn ? State.TurnNumber : (int?)null;
                    p.CharacterArea[i] = c;
                    return;
                }
                throw new InvalidOperationException("No open Character slot.");
            }

            public void Hand(string seat, string id) => State.Players[seat].Hand.Add(Card(id, seat, "hand"));

            public void Life(string seat, string id, int count, bool append = false)
            {
                var life = State.Players[seat].Life;
                if (!append) life.Clear();
                for (int i = 0; i < count; i++) life.Add(Card(id, seat, "life"));
            }

            public void Trash(string seat, string id, int count)
            {
                for (int i = 0; i < count; i++) State.Players[seat].Trash.Add(Card(id, seat, "trash"));
            }

            public void Don(string seat, int count)
            {
                var p = State.Players[seat];
                for (int i = 0; i < count; i++)
                    p.CostArea.Add(new DonInstance { InstanceId = Next(seat + "-don"), Rested = false });
            }

            private CardInstance Card(string id, string owner, string zone) => new CardInstance
            {
                InstanceId = Next(owner + "-" + id),
                CardId = id,
                Owner = owner,
                Zone = zone,
                PlayedOnTurn = null,
            };

            private string Next(string prefix) => $"{prefix}-{_next++}";
        }
    }
}
