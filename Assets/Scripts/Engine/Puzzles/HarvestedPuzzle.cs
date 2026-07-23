using System;
using System.Collections.Generic;
using System.Linq;

namespace OnePieceTcg.Engine.Puzzles
{
    /// <summary>
    /// A puzzle harvested from a real recorded game. Stored as a self-contained REPRODUCTION RECIPE — the
    /// match seed, both deck lists, and the command prefix that reaches the position — so <see cref="Build"/>
    /// rebuilds the EXACT GameState deterministically via GameEngine.CreateMatch + ApplyCommand (the same
    /// path the replay viewer uses). Because it is exact, the defender's face-down Life and deck order are the
    /// real ones, so a lethal proven here holds against the actual disruptive triggers.
    ///
    /// Fields are plain public serializable types so the SAME JSON round-trips through Unity's JsonUtility
    /// (client load) and System.Text.Json (Sim emit). See <see cref="HarvestedPuzzleSet"/> for the file
    /// wrapper. The Sim harvester writes these; the client and Sim tests reload them into puzzles.
    /// </summary>
    [Serializable]
    public sealed class HarvestedPuzzle
    {
        public string Id;
        public string Title;
        public string Teaches;
        public string Attacker = "south";   // the seat the player controls
        public int Difficulty = 3;          // 1 Easy · 2 Medium · 3 Hard · 4 Expert
        public string Seed;
        public string FirstPlayer = "south";
        public string SouthLeader;
        public string NorthLeader;
        public List<CardQty> SouthDeck = new List<CardQty>();
        public List<CardQty> NorthDeck = new List<CardQty>();
        public List<SerialCmd> Commands = new List<SerialCmd>();

        /// <summary>Rebuild the exact position by recreating the match and replaying the command prefix.</summary>
        public GameState Build()
        {
            var cfg = new MatchConfig
            {
                Seed = Seed,
                FirstPlayer = string.IsNullOrEmpty(FirstPlayer) ? "south" : FirstPlayer,
                SouthDeckDef = new DeckDef { Leader = SouthLeader, List = SouthDeck.Select(c => (c.id, c.qty)).ToList() },
                NorthDeckDef = new DeckDef { Leader = NorthLeader, List = NorthDeck.Select(c => (c.id, c.qty)).ToList() },
            };
            var s = GameEngine.CreateMatch(cfg);
            foreach (var c in Commands) s = GameEngine.ApplyCommand(s, c.ToCommand());
            return s;
        }

        public AuthoredPuzzle ToAuthored() => new AuthoredPuzzle
        {
            Id = Id, Title = Title, Teaches = Teaches, Attacker = Attacker,
            Category = "harvested", Difficulty = Difficulty, Build = Build,
        };

        public static HarvestedPuzzle FromRecipe(string id, string title, string teaches, int difficulty,
            string attacker, string seed, string firstPlayer, DeckDef south, DeckDef north, List<GameCommand> prefix)
            => new HarvestedPuzzle
            {
                Id = id, Title = title, Teaches = teaches, Difficulty = difficulty, Attacker = attacker,
                Seed = seed, FirstPlayer = firstPlayer,
                SouthLeader = south.Leader, NorthLeader = north.Leader,
                SouthDeck = south.List.Select(t => new CardQty { id = t.cardId, qty = t.qty }).ToList(),
                NorthDeck = north.List.Select(t => new CardQty { id = t.cardId, qty = t.qty }).ToList(),
                Commands = prefix.Select(SerialCmd.From).ToList(),
            };
    }

    [Serializable] public sealed class HarvestedPuzzleSet { public List<HarvestedPuzzle> puzzles = new List<HarvestedPuzzle>(); }

    [Serializable] public sealed class CardQty { public string id; public int qty; }

    /// <summary>A JsonUtility-friendly GameCommand (nullable ints/bools flattened to value + Has-flag, matching
    /// the client's SerializableCommand shape so replay commands round-trip identically).</summary>
    [Serializable]
    public sealed class SerialCmd
    {
        public string Type, Seat, InstanceId, Target, Attacker, Blocker, EffectId;
        public int Amount; public bool HasAmount;
        public int SlotIndex; public bool HasSlotIndex;
        public bool GoingFirst, HasGoingFirst, Mulligan, HasMulligan;
        public List<string> OrderedInstanceIds = new List<string>();
        public List<string> DonInstanceIds = new List<string>();

        public static SerialCmd From(GameCommand c) => new SerialCmd
        {
            Type = c.Type, Seat = c.Seat, InstanceId = c.InstanceId, Target = c.Target, Attacker = c.Attacker,
            Blocker = c.Blocker, EffectId = c.EffectId,
            Amount = c.Amount ?? 0, HasAmount = c.Amount.HasValue,
            SlotIndex = c.SlotIndex ?? 0, HasSlotIndex = c.SlotIndex.HasValue,
            GoingFirst = c.GoingFirst ?? false, HasGoingFirst = c.GoingFirst.HasValue,
            Mulligan = c.Mulligan ?? false, HasMulligan = c.Mulligan.HasValue,
            OrderedInstanceIds = c.OrderedInstanceIds != null ? new List<string>(c.OrderedInstanceIds) : new List<string>(),
            DonInstanceIds = c.DonInstanceIds != null ? new List<string>(c.DonInstanceIds) : new List<string>(),
        };

        public GameCommand ToCommand() => new GameCommand
        {
            Type = Type, Seat = Seat, InstanceId = InstanceId, Target = Target, Attacker = Attacker,
            Blocker = Blocker, EffectId = EffectId,
            Amount = HasAmount ? (int?)Amount : null,
            SlotIndex = HasSlotIndex ? (int?)SlotIndex : null,
            GoingFirst = HasGoingFirst ? (bool?)GoingFirst : null,
            Mulligan = HasMulligan ? (bool?)Mulligan : null,
            OrderedInstanceIds = OrderedInstanceIds != null && OrderedInstanceIds.Count > 0 ? OrderedInstanceIds : null,
            DonInstanceIds = DonInstanceIds != null && DonInstanceIds.Count > 0 ? DonInstanceIds : null,
        };
    }
}
