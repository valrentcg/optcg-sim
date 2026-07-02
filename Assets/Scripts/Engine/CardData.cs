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
                    ("ST01-001", 1), ("ST01-002", 4), ("ST01-003", 4), ("ST01-004", 4),
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
                    ("ST02-001", 1), ("ST02-002", 4), ("ST02-003", 2), ("ST02-004", 4),
                    ("ST02-005", 2), ("ST02-006", 4), ("ST02-007", 4), ("ST02-008", 4),
                    ("ST02-009", 2), ("ST02-010", 2), ("ST02-011", 4), ("ST02-012", 4),
                    ("ST02-013", 2), ("ST02-014", 4), ("ST02-015", 2), ("ST02-016", 4),
                    ("ST02-017", 2),
                },
            },
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
