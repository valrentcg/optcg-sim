using System;
using System.Collections.Generic;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim.Runner
{
    /// <summary>Regression for a played-from-look card whose On Play opens another deck look. The parent
    /// look's remainder must return to the deck before the nested look starts; no card may be orphaned.</summary>
    public static class NestedDeckLookTest
    {
        public static int Run()
        {
            Console.WriteLine("=== nested deck-look conservation ===");
            var st = Build();
            var selected = st.DeckLook.Cards[0];
            GameEngine.ApplyCommand(st, new GameCommand
            {
                Type = "deckLookSelect", Seat = "south", Target = selected.InstanceId,
            });

            bool parentHeld = st.DeckLook != null && st.DeckLook.Cards.Count == 4
                && st.PendingEffects.Count == 1 && Count(st, "south") == 50;
            Console.WriteLine($"  [{(parentHeld ? "ok  " : "FAIL")}] played card queues On Play; parent retains four cards " +
                $"(count={Count(st, "south")}, look={(st.DeckLook == null ? -1 : st.DeckLook.Cards.Count)}, " +
                $"step={st.DeckLook?.Step ?? "none"}, pending={st.PendingEffects.Count})");
            if (!parentHeld) return 1;

            var order = st.DeckLook.Cards.Select(c => c.InstanceId).ToList();
            GameEngine.ApplyCommand(st, new GameCommand
            {
                Type = "deckLookConfirmOrder", Seat = "south", OrderedInstanceIds = order,
            });
            bool parentClosed = st.DeckLook == null && Count(st, "south") == 50;
            Console.WriteLine($"  [{(parentClosed ? "ok  " : "FAIL")}] parent returns remainder before nested effect " +
                $"(count={Count(st, "south")})");
            if (!parentClosed) return 1;

            string effectId = st.PendingEffects[0].EffectId;
            GameEngine.ApplyCommand(st, new GameCommand
            {
                Type = "resolveEffect", Seat = "south", EffectId = effectId,
            });
            bool nestedOpened = st.DeckLook != null && st.DeckLook.Cards.Count == 5
                && Count(st, "south") == 50;
            Console.WriteLine($"  [{(nestedOpened ? "ok  " : "FAIL")}] nested five-card look opens after parent " +
                $"(count={Count(st, "south")})");
            Console.WriteLine(nestedOpened
                ? "PASS - nested deck looks preserve all 50 cards."
                : "FAIL - nested deck look lost or duplicated cards.");
            return nestedOpened ? 0 : 1;
        }

        private static GameState Build()
        {
            var p = new PlayerState { Seat = "south", Name = "South" };
            p.Leader = Card("leader", "OP08-098", "leader");
            for (int i = 0; i < 45; i++) p.Deck.Add(Card($"deck-{i}", "OP08-099", "deck"));
            var looked = new List<CardInstance> { Card("picked-wyper", "OP08-110", "deck") };
            for (int i = 0; i < 4; i++) looked.Add(Card($"look-{i}", "OP08-099", "deck"));
            var st = new GameState
            {
                Status = "active", ActiveSeat = "south", Phase = "main", TurnNumber = 5,
                DeckLook = new DeckLookState
                {
                    Seat = "south", SourceInstanceId = "parent-source", SourceName = "parent",
                    Step = "select", PlayMode = true, MaxCost = -1, MaxPower = -1, Cards = looked,
                },
            };
            st.Players["south"] = p;
            var north = new PlayerState { Seat = "north", Name = "North" };
            north.Leader = new CardInstance { InstanceId = "north-leader", CardId = "OP08-098", Owner = "north", Zone = "leader" };
            north.Deck.Add(new CardInstance { InstanceId = "north-deck", CardId = "OP08-099", Owner = "north", Zone = "deck" });
            st.Players["north"] = north;
            return st;
        }

        private static CardInstance Card(string instance, string id, string zone) => new CardInstance
        {
            InstanceId = instance, CardId = id, Owner = "south", Zone = zone,
        };

        private static int Count(GameState st, string seat)
        {
            var p = st.Players[seat];
            int dl = st.DeckLook?.Seat == seat
                ? (st.DeckLook.Cards?.Count ?? 0) + (st.DeckLook.Ordered?.Count ?? 0) : 0;
            int revealed = st.Battle?.TargetSeat == seat && st.Battle.RevealedLife != null ? 1 : 0;
            return p.Deck.Count + p.Hand.Count + p.Life.Count + p.CharacterArea.Count(c => c != null)
                + (p.Stage == null ? 0 : 1) + p.Trash.Count + dl + revealed;
        }
    }
}
