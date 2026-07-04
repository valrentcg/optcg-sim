// One Piece TCG - Engine: static card data.
// Pure C#, no UnityEngine dependency. Ported from packages/engine/index.js.
// Drop this into Unity at: Assets/Scripts/Engine/CardData.cs

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace OnePieceTcg.Engine
{
    /// <summary>Immutable definition of a card (the "blueprint"), shared by all instances of that card.</summary>
    public sealed class CardDef
    {
        public string Id;
        public string Name;
        public string Type;     // "leader" | "character" | "event" | "stage" | "unknown"
        public string Color;    // "Red" | "Green" | ...
        public int Cost;
        public int Power;
        public int? Life;       // leaders only
        public int Counter;
        public List<string> Keywords;
        public string Effect;
        public string Trigger;
        /// <summary>
        /// Affiliation/attribute tags shown in {curly braces} on the card, e.g. "Straw Hat Crew",
        /// "Supernovas", "Heart Pirates". Used for effects that restrict by type (Thousand Sunny,
        /// Diable Jambe, Trafalgar Law On Play, Straw Sword trigger, etc.).
        /// </summary>
        public List<string> Features;

        /// <summary>Card rarity from the official library: "SR","SEC","L","R","C","UC","SP CARD","TR","P".</summary>
        public string Rarity = "";

        public CardDef(string id, string name, string type, string color, int cost,
                       int power = 0, int? life = null, int counter = 0, List<string> keywords = null,
                       string effect = "", string trigger = "", List<string> features = null)
        {
            Id = id;
            Name = name;
            Type = type;
            Color = color;
            Cost = cost;
            Power = power;
            Life = life;
            Counter = counter;
            Keywords = keywords ?? new List<string>();
            Effect = effect ?? "";
            Trigger = trigger ?? "";
            Features = features ?? new List<string>();
        }

        public bool HasFeature(string feature)
        {
            if (string.IsNullOrWhiteSpace(feature) || Features == null) return false;
            foreach (var value in Features)
            {
                if (string.IsNullOrWhiteSpace(value)) continue;
                foreach (var part in value.Split('/'))
                {
                    if (string.Equals(part.Trim(), feature.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            return false;
        }
    }

    /// <summary>A starter deck list: leader id + (cardId, quantity) entries.</summary>
    public sealed class DeckDef
    {
        public string Id;
        public string Name;
        public string Leader;
        public List<(string cardId, int qty)> List;
    }

    /// <summary>Static game data: card library, starter decks, ruleset constants.</summary>
    public static class CardData
    {
        // Helper mirroring the JS card() factory.
        private static CardDef Card(string id, string name, string type, string color, int cost,
                                    int power = 0, int? life = null, int counter = 0, string[] keywords = null,
                                    string effect = "", string trigger = "", string[] features = null)
        {
            return new CardDef(id, name, type, color, cost, power, life, counter,
                               keywords != null ? new List<string>(keywords) : new List<string>(),
                               effect, trigger,
                               features != null ? new List<string>(features) : new List<string>());
        }

        public static void UpsertCard(string id, string name, string type, string color, int cost,
                                      int power = 0, int? life = null, int counter = 0,
                                      string[] keywords = null, string effect = "", string trigger = "",
                                      string[] features = null, string rarity = null)
        {
            if (string.IsNullOrEmpty(id)) return;
            var def = Card(id, name, type, color, cost, power, life, counter, keywords, effect, trigger, features);
            def.Rarity = rarity ?? "";
            Library[id] = def;
        }

        // Feature tags correspond to the {attribute} text on cards.
        // They gate effects like Thousand Sunny's buff, Diable Jambe's target, Straw Sword's trigger.
        private static readonly string[] StrawHatCrew = { "Straw Hat Crew" };
        private static readonly string[] Supernovas   = { "Supernovas" };
        private static readonly string[] HeartPirates = { "Supernovas", "Heart Pirates" };
        private static readonly string[] KidPirates   = { "Supernovas", "Kid Pirates" };
        private static readonly string[] Navy         = { "Navy" };

        public static readonly Dictionary<string, CardDef> Library = new Dictionary<string, CardDef>
        {
            // ---- ST01 Straw Hat Crew ----
            ["ST01-001"] = Card("ST01-001", "Monkey.D.Luffy", "leader", "Red", 0, 5000, 5, 0, null,
                "[Activate: Main] [Once Per Turn] Give this Leader or 1 of your Characters up to 1 rested DON!! card.", "",
                StrawHatCrew),
            ["ST01-002"] = Card("ST01-002", "Usopp", "character", "Red", 2, 2000, null, 1000, null,
                "[DON!! x2] [When Attacking] Your opponent cannot activate a [Blocker] Character that has 5000 or more power during this battle.", "[Trigger] Play this card.",
                StrawHatCrew),
            ["ST01-003"] = Card("ST01-003", "Karoo", "character", "Red", 1, 3000, null, 1000, null, "", "",
                StrawHatCrew),
            ["ST01-004"] = Card("ST01-004", "Sanji", "character", "Red", 2, 4000, null, 0, null,
                "[DON!! x2] This Character gains [Rush].\n(This card can attack on the turn in which it is played.)", "",
                StrawHatCrew),
            ["ST01-005"] = Card("ST01-005", "Jinbe", "character", "Red", 3, 5000, null, 0, null,
                "[DON!! x1] [When Attacking] Up to 1 of your Leader or Character cards other than this card gains +1000 power during this turn.", "",
                StrawHatCrew),
            ["ST01-006"] = Card("ST01-006", "Tony Tony.Chopper", "character", "Red", 1, 1000, null, 0, new[] { "Blocker" },
                "[Blocker] (After your opponent declares an attack, you may rest this card to make it the new target of the attack.)", "",
                StrawHatCrew),
            ["ST01-007"] = Card("ST01-007", "Nami", "character", "Red", 1, 1000, null, 1000, null,
                "[Activate: Main] [Once Per Turn] Give up to 1 rested DON!! card to your Leader or 1 of your Characters.", "",
                StrawHatCrew),
            ["ST01-008"] = Card("ST01-008", "Nico Robin", "character", "Red", 3, 5000, null, 1000, null, "", "",
                StrawHatCrew),
            ["ST01-009"] = Card("ST01-009", "Nefeltari Vivi", "character", "Red", 2, 4000, null, 1000, null, "", "",
                StrawHatCrew),
            ["ST01-010"] = Card("ST01-010", "Franky", "character", "Red", 4, 6000, null, 1000, null, "", "",
                StrawHatCrew),
            ["ST01-011"] = Card("ST01-011", "Brook", "character", "Red", 2, 3000, null, 2000, null,
                "[On Play] Give up to 2 rested DON!! cards to your Leader or 1 of your Characters.", "",
                StrawHatCrew),
            ["ST01-012"] = Card("ST01-012", "Monkey.D.Luffy", "character", "Red", 5, 6000, null, 0, new[] { "Rush" },
                "[Rush] (This card can attack on the turn in which it is played.)\n[DON!! x2] [When Attacking] Your opponent cannot activate [Blocker] during this battle.", "",
                StrawHatCrew),
            ["ST01-013"] = Card("ST01-013", "Roronoa Zoro", "character", "Red", 3, 5000, null, 0, null,
                "[DON!! x1] This Character gains +1000 power.", "",
                StrawHatCrew),
            ["ST01-014"] = Card("ST01-014", "Guard Point", "event", "Red", 1, 0, null, 0, new[] { "Counter" },
                "[Counter] Up to 1 of your Leader or Character cards gains +3000 power during this battle.",
                "[Trigger] Up to 1 of your Leader or Character cards gains +1000 power during this turn."),
            ["ST01-015"] = Card("ST01-015", "Gum-Gum Jet Pistol", "event", "Red", 4, 0, null, 0, null,
                "[Main] K.O. up to 1 of your opponent's Characters with 6000 power or less.",
                "[Trigger] Activate this card's [Main] effect."),
            ["ST01-016"] = Card("ST01-016", "Diable Jambe", "event", "Red", 1, 0, null, 0, null,
                "[Main] Select up to 1 of your {Straw Hat Crew} type Leader or Character cards. Your opponent cannot activate [Blocker] if that Leader or Character attacks during this turn.",
                "[Trigger] K.O. up to 1 of your opponent's [Blocker] Characters with a cost of 3 or less."),
            ["ST01-017"] = Card("ST01-017", "Thousand Sunny", "stage", "Red", 2, 0, null, 0, null,
                "[Activate: Main] You may rest this Stage: Up to 1 {Straw Hat Crew} type Leader or Character card on your field gains +1000 power during this turn.", ""),

            // ---- ST02 Worst Generation ----
            ["ST02-001"] = Card("ST02-001", "Eustass\"Captain\"Kid", "leader", "Green", 0, 5000, 5, 0, null,
                "[Activate: Main] [Once Per Turn] \u2462 (You may rest the specified number of DON!! cards in your cost area.) You may trash 1 card from your hand: Set this Leader as active.", "",
                KidPirates),
            ["ST02-002"] = Card("ST02-002", "Vito", "character", "Green", 3, 5000, null, 1000, null, "", "",
                new[] { "Beasts Pirates" }),
            ["ST02-003"] = Card("ST02-003", "Urouge", "character", "Green", 2, 3000, null, 1000, null,
                "[DON!! x1] If you have 3 or more Characters, this card gains +2000 power.", "",
                Supernovas),
            ["ST02-004"] = Card("ST02-004", "Capone\"Gang\"Bege", "character", "Green", 1, 1000, null, 0, new[] { "Blocker" },
                "[Blocker] (After your opponent declares an attack, you may rest this card to make it the new target of the attack.)", "",
                Supernovas),
            ["ST02-005"] = Card("ST02-005", "Killer", "character", "Green", 3, 3000, null, 1000, null,
                "[On Play] K.O. up to 1 of your opponent's rested Characters with a cost of 3 or less.", "[Trigger] Play this card.",
                Supernovas),
            ["ST02-006"] = Card("ST02-006", "Koby", "character", "Green", 4, 6000, null, 1000, null, "", "",
                Navy),
            ["ST02-007"] = Card("ST02-007", "Jewelry Bonney", "character", "Green", 1, 1000, null, 1000, null,
                "[Activate: Main] \u2780 (You may rest the specified number of DON!! cards in your cost area.) You may rest this Character: Look at 5 cards from the top of your deck; reveal up to 1 {Supernovas} type card and add it to your hand. Then, place the rest at the bottom of your deck in any order.", "",
                Supernovas),
            ["ST02-008"] = Card("ST02-008", "Scratchmen Apoo", "character", "Green", 2, 3000, null, 2000, null,
                "[DON!! x1] [When Attacking] Rest up to 1 of your opponent's DON!! cards.", "",
                Supernovas),
            ["ST02-009"] = Card("ST02-009", "Trafalgar Law", "character", "Green", 5, 6000, null, 1000, null,
                "[On Play] Set up to 1 of your {Supernovas} or {Heart Pirates} type rested Characters with a cost of 5 or less as active.", "",
                HeartPirates),
            ["ST02-010"] = Card("ST02-010", "Basil Hawkins", "character", "Green", 5, 6000, null, 0, null,
                "[DON!! x1] [Once Per Turn] [Your Turn] If this Character battles your opponent's Character, set this card as active.", "",
                Supernovas),
            ["ST02-011"] = Card("ST02-011", "Heat", "character", "Green", 2, 4000, null, 1000, null, "", "",
                KidPirates),
            ["ST02-012"] = Card("ST02-012", "Bepo", "character", "Green", 1, 3000, null, 1000, null, "", "",
                HeartPirates),
            ["ST02-013"] = Card("ST02-013", "Eustass\"Captain\"Kid", "character", "Green", 7, 7000, null, 0, new[] { "Blocker" },
                "[Blocker] (After your opponent declares an attack, you may rest this card to make it the new target of the attack.)\n[DON!! x1] [End of Your Turn] Set this Character as active.", "",
                KidPirates),
            ["ST02-014"] = Card("ST02-014", "X.Drake", "character", "Green", 4, 5000, null, 1000, null,
                "[DON!! x1] [Your Turn] If this Character is rested, your {Supernovas} or {Navy} type Leaders and Characters gain +1000 power.", "",
                Supernovas),
            ["ST02-015"] = Card("ST02-015", "Scalpel", "event", "Green", 1, 0, null, 0, new[] { "Counter" },
                "[Counter] Up to 1 of your Leader or Character cards gains +2000 power during this battle. Then, set up to 1 of your DON!! cards as active.",
                "[Trigger] Set up to 2 of your DON!! cards as active."),
            ["ST02-016"] = Card("ST02-016", "Repel", "event", "Green", 2, 0, null, 0, new[] { "Counter" },
                "[Counter] Up to 1 of your Leader or Character cards gains +4000 power during this battle. Then, set up to 1 of your DON!! cards as active.", ""),
            ["ST02-017"] = Card("ST02-017", "Straw Sword", "event", "Green", 2, 0, null, 0, null,
                "[Main] Rest up to 1 of your opponent's Characters.",
                "[Trigger] Play up to 1 {Supernovas} type card with a cost of 2 or less from your hand."),
        };

        public static readonly Dictionary<string, DeckDef> StarterDecks = new Dictionary<string, DeckDef>
        {
            ["st01"] = new DeckDef
            {
                Id = "st01",
                Name = "ST01 - Straw Hat Crew",
                Leader = "ST01-001",
                List = new List<(string, int)>
                {
                    ("ST01-002", 4), ("ST01-003", 4), ("ST01-004", 4),
                    ("ST01-005", 4), ("ST01-006", 4), ("ST01-007", 4), ("ST01-008", 4),
                    ("ST01-009", 4), ("ST01-010", 4), ("ST01-011", 2), ("ST01-012", 2),
                    ("ST01-013", 2), ("ST01-014", 2), ("ST01-015", 2), ("ST01-016", 2),
                    ("ST01-017", 2),
                },
            },
            ["st02"] = new DeckDef
            {
                Id = "st02",
                Name = "ST02 - Worst Generation",
                Leader = "ST02-001",
                List = new List<(string, int)>
                {
                    ("ST02-002", 4), ("ST02-003", 2), ("ST02-004", 4),
                    ("ST02-005", 2), ("ST02-006", 4), ("ST02-007", 4), ("ST02-008", 4),
                    ("ST02-009", 2), ("ST02-010", 2), ("ST02-011", 4), ("ST02-012", 4),
                    ("ST02-013", 2), ("ST02-014", 4), ("ST02-015", 2), ("ST02-016", 4),
                    ("ST02-017", 2),
                },
            },
            ["st03"] = new DeckDef
            {
                Id = "st03",
                Name = "ST03 - The Seven Warlords of the Sea",
                Leader = "ST03-001",
                List = new List<(string, int)>
                {
                    ("ST03-002", 4), ("ST03-003", 2), ("ST03-004", 2), ("ST03-005", 2),
                    ("ST03-006", 4), ("ST03-007", 2), ("ST03-008", 4), ("ST03-009", 2),
                    ("ST03-010", 4), ("ST03-011", 4), ("ST03-012", 4), ("ST03-013", 4),
                    ("ST03-014", 2), ("ST03-015", 4), ("ST03-016", 4), ("ST03-017", 2),
                },
            },
            ["st04"] = new DeckDef
            {
                Id = "st04",
                Name = "ST04 - Animal Kingdom Pirates",
                Leader = "ST04-001",
                List = new List<(string, int)>
                {
                    ("ST04-002", 4), ("ST04-003", 2), ("ST04-004", 2), ("ST04-005", 2),
                    ("ST04-006", 4), ("ST04-007", 4), ("ST04-008", 2), ("ST04-009", 4),
                    ("ST04-010", 2), ("ST04-011", 4), ("ST04-012", 4), ("ST04-013", 4),
                    ("ST04-014", 2), ("ST04-015", 2), ("ST04-016", 4), ("ST04-017", 4),
                },
            },
            ["st05"] = new DeckDef
            {
                Id = "st05",
                Name = "ST05 - ONE PIECE FILM edition",
                Leader = "ST05-001",
                List = new List<(string, int)>
                {
                    ("ST05-002", 4), ("ST05-003", 4), ("ST05-004", 2), ("ST05-005", 2),
                    ("ST05-006", 2), ("ST05-007", 4), ("ST05-008", 2), ("ST05-009", 4),
                    ("ST05-010", 2), ("ST05-011", 2), ("ST05-012", 4), ("ST05-013", 4),
                    ("ST05-014", 4), ("ST05-015", 4), ("ST05-016", 2), ("ST05-017", 4),
                },
            },
            ["st06"] = new DeckDef
            {
                Id = "st06",
                Name = "ST06 - Absolute Justice",
                Leader = "ST06-001",
                List = new List<(string, int)>
                {
                    ("ST06-002", 2), ("ST06-003", 4), ("ST06-004", 2), ("ST06-005", 4),
                    ("ST06-006", 4), ("ST06-007", 4), ("ST06-008", 2), ("ST06-009", 4),
                    ("ST06-010", 4), ("ST06-011", 4), ("ST06-012", 2), ("ST06-013", 4),
                    ("ST06-014", 2), ("ST06-015", 2), ("ST06-016", 4), ("ST06-017", 2),
                },
            },
            ["st07"] = new DeckDef
            {
                Id = "st07",
                Name = "ST07 - Big Mom Pirates",
                Leader = "ST07-001",
                List = new List<(string, int)>
                {
                    ("ST07-002", 4), ("ST07-003", 2), ("ST07-004", 4), ("ST07-005", 2),
                    ("ST07-006", 4), ("ST07-007", 2), ("ST07-008", 4), ("ST07-009", 2),
                    ("ST07-010", 2), ("ST07-011", 4), ("ST07-012", 4), ("ST07-013", 4),
                    ("ST07-014", 4), ("ST07-015", 4), ("ST07-016", 2), ("ST07-017", 2),
                },
            },
            ["st08"] = new DeckDef
            {
                Id = "st08",
                Name = "ST08 - Monkey D. Luffy",
                Leader = "ST08-001",
                List = new List<(string, int)>
                {
                    ("ST08-002", 2), ("ST08-003", 4), ("ST08-004", 4), ("ST08-005", 2),
                    ("ST08-006", 4), ("ST08-007", 4), ("ST08-008", 4), ("ST08-009", 4),
                    ("ST08-010", 4), ("ST08-011", 4), ("ST08-012", 4), ("ST08-013", 4),
                    ("ST08-014", 4), ("ST08-015", 2),
                },
            },
            ["st09"] = new DeckDef
            {
                Id = "st09",
                Name = "ST09 - Yamato",
                Leader = "ST09-001",
                List = new List<(string, int)>
                {
                    ("ST09-002", 4), ("ST09-003", 4), ("ST09-004", 4), ("ST09-005", 2),
                    ("ST09-006", 4), ("ST09-007", 4), ("ST09-008", 4), ("ST09-009", 4),
                    ("ST09-010", 2), ("ST09-011", 4), ("ST09-012", 4), ("ST09-013", 4),
                    ("ST09-014", 2), ("ST09-015", 4),
                },
            },
            // ST10 and ST13 are "ULTRA DECK" boxes shipping 3 interchangeable leader cards
            // (Law/Luffy/Kid for ST10, Sabo/Ace/Luffy for ST13) sharing one 50-card main deck.
            // DeckDef only models a single Leader, so the first-numbered one is used here.
            ["st10"] = new DeckDef
            {
                Id = "st10",
                Name = "ST10 - The Three Captains",
                Leader = "ST10-001",
                List = new List<(string, int)>
                {
                    ("ST10-004", 4), ("ST10-005", 2), ("ST10-006", 2), ("ST10-007", 4),
                    ("ST10-008", 4), ("ST10-009", 4), ("ST10-010", 2), ("ST10-011", 4),
                    ("ST10-012", 4), ("ST10-013", 2), ("ST10-014", 4), ("ST10-015", 2),
                    ("ST10-016", 2), ("ST10-017", 2), ("OP01-016", 4), ("OP01-025", 4),
                },
            },
            ["st11"] = new DeckDef
            {
                Id = "st11",
                Name = "ST11 - Uta",
                Leader = "ST11-001",
                List = new List<(string, int)>
                {
                    ("ST11-002", 2), ("ST11-003", 2), ("ST11-004", 2), ("ST11-005", 4),
                    ("OP02-028", 4), ("OP02-033", 4), ("OP02-034", 4), ("OP02-035", 4),
                    ("OP02-037", 4), ("OP02-039", 4), ("OP02-040", 4), ("OP02-041", 4),
                    ("OP02-043", 4), ("OP02-045", 4),
                },
            },
            ["st12"] = new DeckDef
            {
                Id = "st12",
                Name = "ST12 - Zoro and Sanji",
                Leader = "ST12-001",
                List = new List<(string, int)>
                {
                    ("ST12-002", 4), ("ST12-003", 2), ("ST12-004", 4), ("ST12-005", 4),
                    ("ST12-006", 4), ("ST12-007", 4), ("ST12-008", 2), ("ST12-009", 4),
                    ("ST12-010", 2), ("ST12-011", 2), ("ST12-012", 4), ("ST12-013", 4),
                    ("ST12-014", 2), ("ST12-015", 4), ("ST12-016", 2), ("ST12-017", 2),
                },
            },
            ["st13"] = new DeckDef
            {
                Id = "st13",
                Name = "ST13 - The Three Brothers",
                Leader = "ST13-001",
                List = new List<(string, int)>
                {
                    ("ST13-004", 4), ("ST13-005", 2), ("ST13-006", 4), ("ST13-007", 4),
                    ("ST13-008", 2), ("ST13-009", 4), ("ST13-010", 4), ("ST13-011", 2),
                    ("ST13-012", 4), ("ST13-013", 2), ("ST13-014", 4), ("ST13-015", 2),
                    ("ST13-016", 4), ("ST13-017", 2), ("ST13-018", 2), ("ST13-019", 4),
                },
            },
            ["st14"] = new DeckDef
            {
                Id = "st14",
                Name = "ST14 - 3D2Y",
                Leader = "ST14-001",
                List = new List<(string, int)>
                {
                    ("ST14-002", 4), ("ST14-003", 2), ("ST14-004", 4), ("ST14-005", 4),
                    ("ST14-006", 2), ("ST14-007", 4), ("ST14-008", 4), ("ST14-009", 4),
                    ("ST14-010", 4), ("ST14-011", 4), ("ST14-012", 2), ("ST14-013", 4),
                    ("ST14-014", 2), ("ST14-015", 2), ("ST14-016", 2), ("ST14-017", 2),
                },
            },
            // ST15's leader is the reprinted OP02-001 Edward.Newgate, not a native ST15-001
            // card (verified against the official Bandai cardlist) - ST15-001 is an ordinary
            // non-leader character in this deck.
            ["st15"] = new DeckDef
            {
                Id = "st15",
                Name = "ST15 - Edward.Newgate",
                Leader = "OP02-001",
                List = new List<(string, int)>
                {
                    ("OP02-008", 4), ("OP02-018", 4), ("OP02-019", 4), ("OP02-023", 4),
                    ("OP03-003", 4), ("OP03-006", 4), ("OP03-007", 4), ("OP03-009", 4),
                    ("OP03-010", 4), ("ST15-001", 4), ("ST15-002", 2), ("ST15-003", 4),
                    ("ST15-004", 2), ("ST15-005", 2),
                },
            },
            // ST16's leader is the reprinted ST11-001 Uta, not a native ST16-001 card
            // (verified against the official Bandai cardlist).
            ["st16"] = new DeckDef
            {
                Id = "st16",
                Name = "ST16 - GREEN Uta",
                Leader = "ST11-001",
                List = new List<(string, int)>
                {
                    ("P-029", 4), ("P-057", 4), ("P-058", 4), ("P-059", 4),
                    ("P-060", 4), ("P-061", 4), ("ST11-003", 4), ("ST11-004", 4),
                    ("ST11-005", 4), ("ST16-001", 2), ("ST16-002", 4), ("ST16-003", 4),
                    ("ST16-004", 2), ("ST16-005", 2),
                },
            },
            // ST17-ST20 are "reprint" starter decks: the Leader is a reprinted card from an
            // earlier booster set (confirmed against official-card-library.json - ST17-001 etc.
            // are type "character", not "leader"), not a native ST-001 card like ST01-ST09/ST21/ST22.
            ["st17"] = new DeckDef
            {
                Id = "st17",
                Name = "ST17 - Blue Donquixote Doflamingo",
                Leader = "OP01-060",
                List = new List<(string, int)>
                {
                    ("ST17-001", 4), ("ST17-002", 2), ("ST17-003", 2), ("ST17-004", 2), ("ST17-005", 4),
                    ("OP01-073", 4), ("OP01-086", 4), ("OP02-054", 4), ("OP02-057", 4),
                    ("ST03-002", 4), ("ST03-004", 4), ("ST03-005", 4), ("ST03-008", 4), ("P-030", 4),
                },
            },
            ["st18"] = new DeckDef
            {
                Id = "st18",
                Name = "ST18 - Purple Monkey.D.Luffy",
                Leader = "OP05-060",
                List = new List<(string, int)>
                {
                    ("ST18-001", 2), ("ST18-002", 4), ("ST18-003", 4), ("ST18-004", 2), ("ST18-005", 2),
                    ("OP05-061", 4), ("OP05-063", 4), ("OP05-066", 4), ("OP05-067", 4), ("OP05-068", 4),
                    ("OP05-070", 4), ("OP05-072", 4), ("OP05-076", 4), ("P-041", 4),
                },
            },
            ["st19"] = new DeckDef
            {
                Id = "st19",
                Name = "ST19 - Black Smoker",
                Leader = "OP02-093",
                List = new List<(string, int)>
                {
                    ("ST19-001", 4), ("ST19-002", 2), ("ST19-003", 2), ("ST19-004", 2), ("ST19-005", 4),
                    ("OP02-098", 4), ("OP02-106", 4), ("OP02-108", 4), ("OP02-109", 4), ("OP02-113", 4),
                    ("OP02-116", 4), ("OP02-117", 4), ("OP03-079", 4), ("OP03-089", 4),
                },
            },
            ["st20"] = new DeckDef
            {
                Id = "st20",
                Name = "ST20 - Yellow Charlotte Katakuri",
                Leader = "OP03-099",
                List = new List<(string, int)>
                {
                    ("ST20-001", 2), ("ST20-002", 4), ("ST20-003", 4), ("ST20-004", 2), ("ST20-005", 2),
                    ("OP03-106", 4), ("OP03-107", 4), ("OP03-110", 4), ("OP03-112", 4), ("OP03-115", 4),
                    ("OP03-118", 4), ("OP03-121", 4), ("ST07-005", 4), ("ST07-014", 4),
                },
            },
            ["st21"] = new DeckDef
            {
                Id = "st21",
                Name = "ST21 - Starter Deck EX -GEAR5-",
                Leader = "ST21-001",
                List = new List<(string, int)>
                {
                    ("ST21-002", 2), ("ST21-003", 2), ("ST21-004", 4), ("ST21-005", 4),
                    ("ST21-006", 4), ("ST21-007", 4), ("ST21-008", 4), ("ST21-009", 4),
                    ("ST21-010", 2), ("ST21-011", 4), ("ST21-012", 4), ("ST21-013", 4),
                    ("ST21-014", 2), ("ST21-015", 2), ("ST21-016", 2), ("ST21-017", 2),
                },
            },
            ["st22"] = new DeckDef
            {
                Id = "st22",
                Name = "ST22 - Ace & Newgate",
                Leader = "ST22-001",
                List = new List<(string, int)>
                {
                    ("ST22-002", 2), ("ST22-003", 2), ("ST22-004", 4), ("ST22-005", 4),
                    ("ST22-006", 4), ("ST22-007", 4), ("ST22-008", 4), ("ST22-009", 4),
                    ("ST22-010", 2), ("ST22-011", 4), ("ST22-012", 4), ("ST22-013", 4),
                    ("ST22-014", 2), ("ST22-015", 2), ("ST22-016", 2), ("ST22-017", 2),
                },
            },
            // ST24-ST28 are precon decks that reuse original booster-set numbers for most
            // reprinted cards (only ~5 cards per deck get fresh ST2X-0YY numbers); ST29/ST30
            // use full dedicated numbering like the classic starter decks. ST23 follows the
            // same reprint pattern (leader is the OP09-001 Shanks reprint).
            ["st23"] = new DeckDef
            {
                Id = "st23",
                Name = "ST23 - RED Shanks",
                Leader = "OP09-001",
                List = new List<(string, int)>
                {
                    ("ST23-001", 2), ("ST23-002", 2), ("ST23-003", 2), ("ST23-004", 4), ("ST23-005", 4),
                    ("OP09-006", 4), ("OP09-010", 4), ("OP09-011", 4), ("OP09-012", 4), ("OP09-013", 4),
                    ("OP09-014", 4), ("OP09-015", 4), ("OP09-016", 4), ("OP09-020", 4),
                },
            },
            ["st24"] = new DeckDef
            {
                Id = "st24",
                Name = "ST24 - GREEN Jewelry Bonney",
                Leader = "OP07-019",
                List = new List<(string, int)>
                {
                    ("ST24-001", 4), ("ST24-002", 2), ("ST24-003", 2), ("ST24-004", 2), ("ST24-005", 4),
                    ("EB01-015", 4), ("OP07-021", 4), ("OP07-023", 4), ("OP07-025", 4), ("OP07-031", 4),
                    ("OP07-033", 4), ("OP07-034", 4), ("OP07-036", 4), ("OP07-037", 4),
                },
            },
            ["st25"] = new DeckDef
            {
                Id = "st25",
                Name = "ST25 - BLUE Buggy",
                Leader = "OP09-042",
                List = new List<(string, int)>
                {
                    ("ST25-001", 2), ("ST25-002", 4), ("ST25-003", 2), ("ST25-004", 2), ("ST25-005", 4),
                    ("OP09-043", 4), ("OP09-045", 4), ("OP09-051", 4), ("OP09-053", 4), ("OP09-054", 4),
                    ("OP09-055", 4), ("OP09-056", 4), ("P-084", 4), ("OP09-057", 4),
                },
            },
            ["st26"] = new DeckDef
            {
                Id = "st26",
                Name = "ST26 - PURPLE/BLACK Monkey.D.Luffy",
                Leader = "OP09-061",
                List = new List<(string, int)>
                {
                    ("ST26-001", 4), ("ST26-002", 2), ("ST26-003", 2), ("ST26-004", 4), ("ST26-005", 2),
                    ("OP05-065", 4), ("OP05-066", 4), ("OP05-070", 4), ("OP09-063", 4), ("OP09-070", 4),
                    ("OP09-076", 4), ("ST14-010", 4), ("OP09-077", 4), ("OP09-078", 4),
                },
            },
            ["st27"] = new DeckDef
            {
                Id = "st27",
                Name = "ST27 - BLACK Marshall.D.Teach",
                Leader = "OP09-081",
                List = new List<(string, int)>
                {
                    ("ST27-001", 4), ("ST27-002", 4), ("ST27-003", 2), ("ST27-004", 2), ("ST27-005", 2),
                    ("OP09-083", 4), ("OP10-084", 4), ("OP09-086", 4), ("OP09-088", 4), ("OP09-089", 4),
                    ("OP09-090", 4), ("OP09-091", 4), ("OP09-095", 4), ("OP09-099", 4),
                },
            },
            ["st28"] = new DeckDef
            {
                Id = "st28",
                Name = "ST28 - GREEN/YELLOW Yamato",
                Leader = "OP06-022",
                List = new List<(string, int)>
                {
                    ("ST28-001", 4), ("ST28-002", 4), ("ST28-003", 2), ("ST28-004", 2), ("ST28-005", 2),
                    ("OP06-100", 4), ("OP06-103", 4), ("OP06-104", 4), ("OP06-109", 4), ("OP06-110", 4),
                    ("OP06-112", 4), ("OP09-035", 4), ("ST13-016", 4), ("OP07-116", 4),
                },
            },
            ["st29"] = new DeckDef
            {
                Id = "st29",
                Name = "ST29 - Egghead",
                Leader = "ST29-001",
                List = new List<(string, int)>
                {
                    ("ST29-002", 4), ("ST29-003", 4), ("ST29-004", 2), ("ST29-005", 2),
                    ("ST29-006", 4), ("ST29-007", 2), ("ST29-008", 2), ("ST29-009", 2),
                    ("ST29-010", 4), ("ST29-011", 4), ("ST29-012", 4), ("ST29-013", 4),
                    ("ST29-014", 2), ("ST29-015", 4), ("ST29-016", 2), ("ST29-017", 4),
                },
            },
            ["st30"] = new DeckDef
            {
                Id = "st30",
                Name = "ST30 - Luffy & Ace",
                Leader = "ST30-001",
                List = new List<(string, int)>
                {
                    ("ST30-002", 2), ("ST30-003", 4), ("ST30-004", 2), ("ST30-005", 4),
                    ("ST30-006", 4), ("ST30-007", 2), ("ST30-008", 4), ("ST30-009", 4),
                    ("ST30-010", 4), ("ST30-011", 2), ("ST30-012", 2), ("ST30-013", 4),
                    ("ST30-014", 2), ("ST30-015", 4), ("ST30-016", 2), ("ST30-017", 4),
                },
            },
            // Learn Together Deck Set (LT-01, released 2025-10-03): three 2-player-intro
            // decks built almost entirely from reprints spanning EB01/EB02/OP01/OP06-12/
            // ST10/ST12/ST21/promo cards, rather than a dedicated STxx-numbered card pool.
            ["lt01luffy"] = new DeckDef
            {
                Id = "lt01luffy",
                Name = "LT01 - Luffy (Learn Together)",
                Leader = "ST21-001",
                List = new List<(string, int)>
                {
                    ("P-069", 4), ("ST21-015", 2), ("ST21-014", 2), ("OP11-003", 4),
                    ("ST21-017", 2), ("OP06-018", 2), ("ST21-008", 4), ("OP11-016", 4),
                    ("OP10-013", 4), ("ST21-003", 4), ("ST21-010", 4), ("OP01-016", 4),
                    ("OP10-019", 2), ("OP09-005", 4), ("OP10-011", 4),
                },
            },
            ["lt01zoro"] = new DeckDef
            {
                Id = "lt01zoro",
                Name = "LT01 - Zoro (Learn Together)",
                Leader = "OP12-020",
                List = new List<(string, int)>
                {
                    ("OP12-028", 4), ("OP12-027", 4), ("OP12-023", 4), ("OP12-025", 4),
                    ("OP12-039", 4), ("OP06-036", 4), ("OP12-032", 4), ("OP12-036", 4),
                    ("OP12-029", 4), ("OP06-038", 2), ("EB01-012", 2), ("OP10-030", 2),
                    ("OP12-031", 4), ("EB02-019", 4),
                },
            },
            ["lt01nami"] = new DeckDef
            {
                Id = "lt01nami",
                Name = "LT01 - Nami (Learn Together)",
                Leader = "OP11-041",
                List = new List<(string, int)>
                {
                    ("OP11-048", 4), ("P-053", 4), ("OP11-056", 4), ("EB02-022", 4),
                    ("P-056", 4), ("OP11-058", 4), ("OP11-052", 2), ("OP11-053", 4),
                    ("OP11-051", 2), ("OP11-118", 2), ("OP11-105", 4), ("OP09-107", 4),
                    ("OP11-060", 4), ("OP11-114", 4),
                },
            },
        };

        // Some starter/ultra decks reprint a leader from an earlier booster set using a
        // dedicated alternate-art print made for that deck (e.g. ST17's Doflamingo is
        // OP01-060 but printed with the "_p2" art, seriesCode ST17 in
        // official-card-library.json). DeckDef.Leader stays the plain stats id (OP01-060)
        // so Card() lookups keep working; this table is consulted only when resolving
        // which art file to display, keyed by DeckDef.Id.
        public static readonly Dictionary<string, string> StarterLeaderArtOverride = new Dictionary<string, string>
        {
            ["st17"] = "OP01-060_p2",
            ["st18"] = "OP05-060_p3",

            // Learn Together Deck Set (LT-01): same situation - each deck's box leader is a
            // dedicated alt-print distinct from the plain booster reprint DeckDef.Leader
            // points at.
            ["lt01luffy"] = "ST21-001_p2",
            ["lt01zoro"] = "OP12-020_p3",

            // st23-28: art reconstructed by geometrically aligning Bandai's own clean,
            // high-res promo hero art (renewal/images/products/decks/{id}/mv.webp) onto
            // the official card render and grafting it over the SAMPLE-watermarked region,
            // keeping the card's own frame/power-badge/text-box/footer pixels intact.
            ["st23"] = "OP09-001_p2",
            ["st24"] = "OP07-019_p3",
            ["st25"] = "OP09-042_p2",
            ["st26"] = "OP09-061_p2",
            ["st27"] = "OP09-081_p2",
            ["st28"] = "OP06-022_p3",

            // The following use Bandai's own SAMPLE-watermarked preview image - no clean
            // source exists at adequate resolution: no glare-free photo anywhere, and the
            // only promo art available (the older st15-20.php mv_01.jpg collage) is lower
            // resolution than the card render itself, so warping it in would look blurry
            // rather than clean. Watermark was judged the lesser evil.
            ["st15"] = "OP02-001_p2",
            ["st16"] = "ST11-001_p1",
            ["st19"] = "OP02-093_p2",
            ["st20"] = "OP03-099_p2",
            ["lt01nami"] = "OP11-041_p2",
        };

        // Ruleset constants (mirrors OP_RULESET).
        public const string RulesetSource = "ONE PIECE Card Game Comprehensive Rules";
        public const string RulesetSourceDate = "2026-01-16";
        public const int CharacterSlots = 5;
        public const int OpeningHandCards = 5;
        public const int DonDeckCards = 10;

        /// <summary>Look up a card definition by id, returning a placeholder for unknown ids (mirrors getCard).</summary>
        public static CardDef GetCard(string cardId)
        {
            if (cardId != null && Library.TryGetValue(cardId, out var def)) return def;
            return new CardDef(cardId, cardId, "unknown", "Gray", 0);
        }

        // ── Niche gameplay overrides ────────────────────────────────────────────
        // A handful of leaders print their own exception to the normal rules
        // directly on the card. Most use "Under the rules of this game, ..." but
        // not all do (Nami's deck-out rule is worded differently across her two
        // prints), so each check below looks for the specific clause rather than
        // a single fixed prefix — that still means any current or future card
        // reusing the same official wording is picked up automatically, without
        // a hardcoded card-id list. Deck-construction-only exceptions (unlimited
        // copies, cost ceilings, feature-locked decks) live in DeckBuilderManager
        // instead — the engine never validates deck legality, it just plays
        // whatever DeckDef it's given, so only rules that affect live play belong
        // here: Enel (OP15-058, purple): "your DON!! deck consists of 6 cards";
        // Brook (OP15-022): "you do not lose when your deck has 0 cards. You lose
        // at the end of the turn in which your deck becomes 0 cards"; Nami
        // (OP03-040, P-117): "you win the game instead of losing" on deck-out.
        private static readonly Regex DonDeckSizeRegex =
            new Regex(@"your DON!! deck consists of (\d+) cards", RegexOptions.Compiled);

        /// <summary>DON!! deck size for a match built around this leader (default 10).</summary>
        public static int DonDeckSizeForLeader(string leaderId)
        {
            var m = DonDeckSizeRegex.Match(GetCard(leaderId)?.Effect ?? "");
            return m.Success ? int.Parse(m.Groups[1].Value) : DonDeckCards;
        }

        /// <summary>
        /// True if this leader defers the deck-out loss to the end of the turn
        /// (e.g. Brook) instead of losing the instant the deck reaches 0 cards.
        /// </summary>
        public static bool HasDeferredDeckOutRule(string leaderId)
        {
            return (GetCard(leaderId)?.Effect ?? "").Contains("you do not lose when your deck has 0 cards");
        }

        /// <summary>
        /// True if this leader wins instead of losing when their deck reaches 0
        /// cards (e.g. Nami), overriding the normal deck-out loss entirely.
        /// </summary>
        public static bool WinsOnDeckOut(string leaderId)
        {
            return (GetCard(leaderId)?.Effect ?? "").Contains("you win the game instead of losing");
        }

        /// <summary>
        /// True if this card grants an instant win when its controller's
        /// opponent activates [Blocker] while either player has 0 Life cards
        /// (e.g. Gol.D.Roger, OP09-118).
        /// </summary>
        public static bool WinsWhenOpponentBlocks(string cardId)
        {
            return (GetCard(cardId)?.Effect ?? "").Contains("if either you or your opponent has 0 Life cards, you win the game");
        }
    }
}
