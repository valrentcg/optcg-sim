// One Piece TCG - Engine: rules logic.
// Pure C#, no UnityEngine dependency. Faithful port of packages/engine/index.js
// (including the Trigger Step + useTrigger/passTrigger added in the web build).
// Mutates a single GameState in place; CommandHistory + seeded RNG give deterministic replay.
// Drop this into Unity at: Assets/Scripts/Engine/GameEngine.cs

using System;
using System.Collections.Generic;
using System.Linq;

namespace OnePieceTcg.Engine
{
    public sealed class MatchConfig
    {
        public string FirstPlayer = "south";
        public string SouthDeck = "st01";
        public string NorthDeck = "st02";
        public string Seed = "starter-slice-001";
        // When set, these override SouthDeck/NorthDeck's StarterDecks lookup —
        // used to hand in custom decks built in the deck builder.
        public DeckDef SouthDeckDef;
        public DeckDef NorthDeckDef;
    }

    public static class GameEngine
    {
        // ---- Public API ---------------------------------------------------

        public static GameState CreateMatch(MatchConfig config = null)
        {
            config ??= new MatchConfig();
            var southDeck = config.SouthDeckDef ?? CardData.StarterDecks[config.SouthDeck];
            var northDeck = config.NorthDeckDef ?? CardData.StarterDecks[config.NorthDeck];

            var state = new GameState
            {
                Seed = config.Seed,
                FirstPlayer = config.FirstPlayer,
                Status = "coinflip",
                ActiveSeat = config.FirstPlayer,
                Phase = "coinflip",
                TurnNumber = 0,
                Selected = null,
                Battle = null,
            };
            state.Players["south"] = CreatePlayer("south", "South", southDeck);
            state.Players["north"] = CreatePlayer("north", "North", northDeck);

            state.CoinFlipWinner = new SeededRng($"{config.Seed}:coinflip").Next() < 0.5 ? "south" : "north";
            Log(state, "system", $"Match created: {southDeck.Name} vs {northDeck.Name}.");
            Log(state, "system", $"{Player(state, state.CoinFlipWinner).Name} won the coin flip.");
            return state;
        }

        private static void ChooseTurnOrder(GameState state, string seat, bool goingFirst)
        {
            if (state.Status != "coinflip" || seat != state.CoinFlipWinner) return;
            state.FirstPlayer = goingFirst ? seat : OtherSeat(seat);
            state.ActiveSeat = state.FirstPlayer;
            state.Status = "setup";
            Log(state, "system", $"{Player(state, seat).Name} chooses to go {(goingFirst ? "first" : "second")}.");
            StartGame(state);
        }

        public static GameState StartGame(GameState state)
        {
            if (state.Status != "setup") return state;
            ShuffleInPlace(state.Players["south"].Deck, $"{state.Seed}:south:opening");
            ShuffleInPlace(state.Players["north"].Deck, $"{state.Seed}:north:opening");
            foreach (var seat in Seats())
                for (int i = 0; i < 5; i++) DrawCard(state, seat, true);
            state.Status = "mulligan";
            state.Phase = "mulligan";
            Log(state, "system", "Both players may look at their hand and choose whether to mulligan.");
            Record(state, new GameCommand { Type = "startGame" });
            return state;
        }

        private static void ResolveMulligan(GameState state, string seat, bool mulligan)
        {
            if (state.Status != "mulligan" || !state.Players.ContainsKey(seat)) return;
            var p = Player(state, seat);
            if (p.MulliganDecided) return;

            if (mulligan)
            {
                p.Deck.AddRange(p.Hand);
                p.Hand.Clear();
                ShuffleInPlace(p.Deck, $"{state.Seed}:{seat}:mulligan");
                for (int i = 0; i < 5; i++) DrawCard(state, seat, true);
                p.MulliganUsed = true;
                Log(state, seat, $"{p.Name} mulligans their opening hand.");
            }
            else
            {
                Log(state, seat, $"{p.Name} keeps their opening hand.");
            }
            p.MulliganDecided = true;

            if (!Seats().All(s => Player(state, s).MulliganDecided)) return;

            foreach (var s in Seats()) SetupLife(state, s);
            state.Status = "active";
            state.Phase = "refresh";
            state.TurnNumber = 1;
            Log(state, state.ActiveSeat, $"{Player(state, state.ActiveSeat).Name} goes first.");
            ApplyStartOfTurn(state);
        }

        public static GameState ApplyCommand(GameState state, GameCommand command)
        {
            var actor = command.Seat;
            if (command.Type == "chooseTurnOrder")
            {
                ChooseTurnOrder(state, actor, command.GoingFirst ?? true);
                CheckRuleProcessing(state);
                Record(state, command);
                return state;
            }
            if (command.Type == "mulliganDecision")
            {
                ResolveMulligan(state, actor, command.Mulligan ?? false);
                CheckRuleProcessing(state);
                Record(state, command);
                return state;
            }
            if (state.Status != "active" && command.Type != "startGame") return state;

            switch (command.Type)
            {
                case "draw": ManualDrawCard(state, actor); break;
                case "drawDon": ManualDrawDon(state, actor, command.Amount ?? 2); break;
                case "reorderHand": ReorderHand(state, actor, command.InstanceId, command.SlotIndex ?? 0); break;
                case "playCard": PlayCard(state, actor, command.InstanceId, command.SlotIndex); break;
                case "attachDon": AttachDon(state, actor, command.Target, command.Amount ?? 1, command.DonInstanceIds); break;
                case "activateMain": ActivateMain(state, actor, command.Target); break;
                case "rest": SetRested(state, actor, command.Target, true); break;
                case "unrest": SetRested(state, actor, command.Target, false); break;
                case "declareAttack": DeclareAttack(state, actor, command.Attacker, command.Target); break;
                case "blockAttack": BlockAttack(state, actor, command.Blocker); break;
                case "passBlock": PassBlock(state, actor); break;
                case "counterWithCard": CounterWithCard(state, actor, command.InstanceId); break;
                case "passCounter": PassCounter(state, actor); break;
                case "resolveAttack": ResolveAttack(state); break;
                case "useTrigger": UseTrigger(state, actor); break;
                case "passTrigger": PassTrigger(state, actor); break;
                case "resolveEffect": ResolveEffect(state, actor, command.EffectId, command.Target); break;
                case "passEffect": PassEffect(state, actor, command.EffectId); break;
                case "resolveChoice": ResolveChoice(state, actor, command.Target); break;
                case "clearBattle": ClearBattle(state); break;
                case "takeLife": TakeLife(state, actor); break;
                case "trash": MoveToTrash(state, actor, command.InstanceId); break;
                case "endTurn": EndTurn(state, actor); break;
                case "deckLookSelect": ResolveDeckLookSelect(state, actor, command.Target); break;
                case "deckLookConfirmOrder": ResolveDeckLookConfirmOrder(state, actor, command.OrderedInstanceIds); break;
                default: Log(state, "system", $"Unknown command: {command.Type}"); break;
            }
            CheckRuleProcessing(state);
            Record(state, command);
            return state;
        }

        // ---- Public helpers (used by the view layer) ----------------------

        public static CardDef GetCard(CardInstance instance) => CardData.GetCard(instance?.CardId);

        // Card display name with its set id appended, e.g. "Otohime [OP11-100]". The trailing [id]
        // token lets the combat-log UI turn it into a hover link to the card preview.
        public static string NameId(CardDef def) => def == null ? "?" : $"{def.Name} [{def.Id}]";
        public static string NameId(CardInstance instance) => NameId(GetCard(instance));

        public static int GetPower(GameState state, CardInstance instance)
        {
            var owner = instance.Owner != null && state.Players.TryGetValue(instance.Owner, out var op) ? op : null;
            int power = GetCard(instance).Power;
            // Attached DON!! cards only grant their raw +1000 power during their controller's
            // turn. They stay attached until that player's refresh so [DON!! xN] requirements
            // can still see them, but they do not raise defense on the opponent's turn.
            if (owner != null && state.ActiveSeat == instance.Owner)
                power += instance.AttachedDonIds.Count * 1000;
            // Passive DON!! power bonuses: "[DON!! xN] This card gains +N power."
            power += GetPassiveDonPowerBonus(state, instance, owner);
            // EffectScope.Turn — cleared at the owning player's next Refresh Phase.
            if (state.TemporaryPowerBonus.TryGetValue(instance.InstanceId, out var temp)) power += temp;
            // EffectScope.Battle — cleared when the BattleState is discarded.
            if (state.Battle != null && state.Battle.BattlePowerBonus.TryGetValue(instance.InstanceId, out var battleBonus)) power += battleBonus;
            // [Your Turn] / [Opponent's Turn] passive auras from other cards.
            power += GetTurnPassiveAuraBonus(state, instance, owner);
            return power;
        }

        // Passive DON!! bonus: "[DON!! xN] This Character gains +N power."
        // Generalizes Zoro (+1000 x1) and Urouge (+2000 x1 if 3+ chars) to all cards.
        // Skips any effect that has another timing keyword (When Attacking, On Play, etc.)
        // because those are conditional, not always-on.
        private static int GetPassiveDonPowerBonus(GameState state, CardInstance instance, PlayerState owner)
        {
            if (owner == null) return 0;
            var text = GetCard(instance).Effect ?? "";
            if (!ContainsAll(text, "[DON!! x")) return 0;
            if (HasTiming(text, "When Attacking") || HasTiming(text, "On Play") ||
                HasTiming(text, "Activate: Main") || HasTiming(text, "End of Your Turn") ||
                HasTiming(text, "Your Turn") || HasTiming(text, "Opponent's Turn") ||
                HasTiming(text, "Once Per Turn") || HasTiming(text, "On KO")) return 0;
            int donReq = ParseDonThreshold(text);
            if (donReq <= 0 || instance.AttachedDonIds.Count < donReq) return 0;
            int bonus = ParsePowerGain(text);
            if (bonus <= 0) return 0;
            // Board condition: "If you have 3 or more Characters" (Urouge pattern).
            if (ContainsAll(text, "3 or more Characters") && owner.CharacterArea.Count(c => c != null) < 3) return 0;
            return bonus;
        }

        // [Your Turn] / [Opponent's Turn] aura bonuses — scans all cards in play for aura effects
        // that buff other cards based on whose turn it is. Replaces the hardcoded DrakeAuraBonus.
        // Pattern: "[DON!! xN] [Your Turn] If this card is rested, your {Type} Leaders and Characters gain +N power."
        private static int GetTurnPassiveAuraBonus(GameState state, CardInstance instance, PlayerState owner)
        {
            if (owner == null) return 0;
            var instanceDef = GetCard(instance);
            if (instanceDef.Type != "leader" && instanceDef.Type != "character") return 0;
            int total = 0;
            foreach (var candidateSeat in Seats())
            {
                // Aura cards only buff their own controller's cards.
                if (candidateSeat != instance.Owner) continue;
                var p = Player(state, candidateSeat);
                bool isActiveSeat = state.ActiveSeat == candidateSeat;
                var auraCards = new List<CardInstance>(p.CharacterArea.Where(c => c != null));
                if (p.Leader != null) auraCards.Add(p.Leader);
                foreach (var aura in auraCards)
                {
                    if (aura.InstanceId == instance.InstanceId) continue;
                    var aText = GetCard(aura).Effect ?? "";
                    bool yourTurn = HasTiming(aText, "Your Turn");
                    bool oppTurn  = HasTiming(aText, "Opponent's Turn");
                    if (!yourTurn && !oppTurn) continue;
                    // Active when it's this player's turn (Your Turn) or not (Opponent's Turn).
                    if (yourTurn && !isActiveSeat) continue;
                    if (oppTurn  &&  isActiveSeat) continue;
                    int donReq = ParseDonThreshold(aText);
                    if (donReq > 0 && aura.AttachedDonIds.Count < donReq) continue;
                    // Rested condition.
                    if (ContainsAll(aText, "is rested") && !aura.Rested) continue;
                    int bonus = ParsePowerGain(aText);
                    if (bonus <= 0) continue;
                    if (!CardPassesFeatureFilter(aText, instanceDef)) continue;
                    total += bonus;
                }
            }
            return total;
        }

        public static int GetCounterPower(CardInstance instance) => AutomatedCounterPower(instance);

        // ---- Modifier system --------------------------------------------------
        // CardModifier tracks keyword grants and flag restrictions (Rush by effect, Double Attack,
        // cannotAttack, freeze, etc.) with "thisTurn" or "thisBattle" duration.
        // Power bonuses continue to use TemporaryPowerBonus / BattlePowerBonus for simplicity.

        private static string NextBattleId(GameState state)
        {
            state.BattleSequence++;
            return $"battle-{state.BattleSequence}";
        }

        private static void AddModifier(GameState state, CardInstance source, CardInstance target,
            string modifierType, string duration, string keyword = null)
        {
            state.ActiveModifiers.Add(new CardModifier
            {
                SourceInstanceId = source?.InstanceId,
                TargetInstanceId = target?.InstanceId,
                ModifierType = modifierType,
                Keyword = keyword,
                Duration = duration,
                BattleId = duration == "thisBattle" ? state.Battle?.Id : null,
            });
        }

        public static bool HasModifier(GameState state, CardInstance instance, string modifierType)
        {
            if (instance == null) return false;
            return state.ActiveModifiers.Any(m =>
                m.TargetInstanceId == instance.InstanceId && m.ModifierType == modifierType);
        }

        private static bool HasKeywordModifier(GameState state, CardInstance instance, string keyword)
        {
            if (instance == null) return false;
            return state.ActiveModifiers.Any(m =>
                m.TargetInstanceId == instance.InstanceId &&
                m.ModifierType == "keyword" &&
                string.Equals(m.Keyword, keyword, StringComparison.OrdinalIgnoreCase));
        }

        private static void CleanupTurnModifiers(GameState state)
        {
            state.ActiveModifiers.RemoveAll(m => m.Duration == "thisTurn");
        }

        private static void CleanupBattleModifiers(GameState state, string battleId)
        {
            if (string.IsNullOrEmpty(battleId)) return;
            state.ActiveModifiers.RemoveAll(m => m.Duration == "thisBattle" && m.BattleId == battleId);
        }

        // Extract the "+NNNN" value from "gains +NNNN power" text. Returns 0 if not found.
        private static int ParsePowerGain(string text)
        {
            int idx = text.IndexOf("gains +", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return 0;
            idx += 7; // skip "gains +"
            int end = idx;
            while (end < text.Length && char.IsDigit(text[end])) end++;
            if (end == idx || !int.TryParse(text.Substring(idx, end - idx), out int v)) return 0;
            return v;
        }

        // Sanji: [DON!! x2] this Character gains Rush.
        // State-aware overload also checks CardModifiers (Rush granted by an effect).
        public static bool HasRush(GameState state, CardInstance instance) =>
            instance != null && (HasKeyword(instance, "Rush")
                || (instance.CardId == "ST01-004" && instance.AttachedDonIds.Count >= 2)
                || HasKeywordModifier(state, instance, "Rush"));

        // Backward-compatible overload (no modifier check) — kept for external callers.
        public static bool HasRush(CardInstance instance) =>
            instance != null && (HasKeyword(instance, "Rush") || (instance.CardId == "ST01-004" && instance.AttachedDonIds.Count >= 2));

        // Double Attack: can this card attack a second time this turn (while rested)?
        public static bool HasDoubleAttack(GameState state, CardInstance instance) =>
            instance != null && (HasKeyword(instance, "Double Attack")
                || HasKeywordModifier(state, instance, "Double Attack")
                || HasModifier(state, instance, "doubleAttack"));

        // Banish: when this card deals damage to a leader, the life card is trashed instead of
        // going to hand and no Trigger step occurs.
        public static bool HasBanish(GameState state, CardInstance instance) =>
            instance != null && (HasKeyword(instance, "Banish")
                || HasKeywordModifier(state, instance, "Banish"));

        // Extract the DON!! cost encoded as circled Unicode digits in Activate:Main effect text.
        // Two series: U+2460-U+2469 (①-⑩) and U+2780-U+2789 (➀-➉).
        private static int ParseActivateMainCost(string text)
        {
            foreach (char c in text)
            {
                if (c >= '①' && c <= '⑩') return (c - '①') + 1;
                if (c >= '➀' && c <= '➉') return (c - '➀') + 1;
            }
            return 0;
        }

        // Extract N from "Look at N cards from the top of your deck".
        private static int ParseLookCount(string text)
        {
            int idx = text.IndexOf("Look at ", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return 5;
            idx += 8;
            int end = idx;
            while (end < text.Length && char.IsDigit(text[end])) end++;
            if (end == idx || !int.TryParse(text.Substring(idx, end - idx), out int v)) return 5;
            return v;
        }

        // Extract N from "Place the top N card(s)" or "trash the top N".
        private static int ParseTopN(string text, string after)
        {
            int idx = text.IndexOf(after, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return 1;
            idx += after.Length;
            while (idx < text.Length && text[idx] == ' ') idx++;
            int end = idx;
            while (end < text.Length && char.IsDigit(text[end])) end++;
            if (end == idx || !int.TryParse(text.Substring(idx, end - idx), out int v)) return 1;
            return v;
        }

        // Extract the first {Feature Tag} from effect text, e.g. "{Supernovas}" → "Supernovas".
        // Returns empty string if none found (means no type restriction).
        private static string ParseCurlyBraceTag(string text)
        {
            int open = text.IndexOf('{');
            if (open < 0) return "";
            int close = text.IndexOf('}', open);
            if (close < 0) return "";
            return text.Substring(open + 1, close - open - 1).Trim();
        }

        // Parse the count after "Draw " — handles "Draw 1 card", "Draw 2 cards", "Draw a card".
        private static int ParseDrawCount(string text)
        {
            int idx = text.IndexOf("Draw ", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return 1;
            idx += 5;
            while (idx < text.Length && text[idx] == ' ') idx++;
            if (idx < text.Length && char.IsDigit(text[idx]))
            {
                int end = idx;
                while (end < text.Length && char.IsDigit(text[end])) end++;
                if (int.TryParse(text.Substring(idx, end - idx), out int v)) return v;
            }
            return 1;
        }

        public static int ActiveDonCount(PlayerState p) => p.CostArea.Count(d => !d.Rested);
        public static int RestedDonCount(PlayerState p) => p.CostArea.Count(d => d.Rested);
        public static string OtherSeat(string seat) => seat == "south" ? "north" : "south";

        // ---- Playability queries (UI: green "valid" hover glow) -------------------------
        // Can this hand card be played right now (main phase, your turn, enough active DON,
        // and an open slot for characters)?
        public static bool CanPlayFromHand(GameState state, string seat, CardInstance instance)
        {
            if (state == null || instance == null || string.IsNullOrEmpty(seat)) return false;
            if (!state.Players.ContainsKey(seat)) return false;
            if (!IsTurnPlayerInMain(state, seat)) return false;
            var p = Player(state, seat);
            if (!p.Hand.Any(c => c.InstanceId == instance.InstanceId)) return false;
            var def = GetCard(instance);
            if (def == null) return false;
            if (ActiveDonCount(p) < def.Cost) return false;
            if (def.Type == "character" && !p.CharacterArea.Any(slot => slot == null)) return false;
            if (def.Type == "event" && !HasTiming(def.Effect, "Main")) return false;
            if (def.Type != "character" && def.Type != "stage" && def.Type != "event") return false;
            return true;
        }

        // Can this hand card be used to counter right now (counter step, you're the defender,
        // it has counter power, and enough active DON if it's an event counter)?
        public static bool CanCounterFromHand(GameState state, string seat, CardInstance instance)
        {
            if (state == null || instance == null || string.IsNullOrEmpty(seat)) return false;
            if (!state.Players.ContainsKey(seat)) return false;
            if (state.Battle == null || state.Battle.Step != "counter" || state.Battle.TargetSeat != seat) return false;
            var p = Player(state, seat);
            if (!p.Hand.Any(c => c.InstanceId == instance.InstanceId)) return false;
            var def = GetCard(instance);
            if (def == null) return false;
            if (AutomatedCounterPower(instance) <= 0) return false;
            if (def.Type == "event" && ActiveDonCount(p) < def.Cost) return false;
            return true;
        }

        public static bool IsPlayableNow(GameState state, string seat, CardInstance instance)
            => CanPlayFromHand(state, seat, instance) || CanCounterFromHand(state, seat, instance);

        // Returns the effective name of a card, respecting any active name-override effects
        // (e.g. "This card's name is also treated as Monkey D. Luffy").
        public static string GetEffectiveName(GameState state, CardInstance card)
        {
            if (state.NameOverrides.TryGetValue(card.InstanceId, out var alt)) return alt;
            return GetCard(card).Name;
        }

        // ---- Setup --------------------------------------------------------

        private static PlayerState CreatePlayer(string seat, string name, DeckDef deckDef)
        {
            var leader = CreateInstance(deckDef.Leader, seat, "leader", "leader");
            var deckIds = new List<string>();
            foreach (var (cardId, qty) in deckDef.List)
            {
                if (cardId == deckDef.Leader) continue;
                for (int i = 0; i < qty; i++) deckIds.Add(cardId);
            }

            var p = new PlayerState
            {
                Seat = seat,
                Name = name,
                DeckName = deckDef.Name,
                Leader = leader,
                DonDeck = CardData.DonDeckSizeForLeader(deckDef.Leader),   // 10 unless the leader overrides it (e.g. Enel: 6)
                DonInstanceCounter = 0,
                MulliganUsed = false,
            };
            for (int i = 0; i < deckIds.Count; i++)
                p.Deck.Add(CreateInstance(deckIds[i], seat, "deck", (i + 1).ToString()));
            return p;
        }

        private static CardInstance CreateInstance(string cardId, string owner, string zone, string suffix)
        {
            return new CardInstance
            {
                InstanceId = $"{owner}-{cardId}-{suffix}",
                CardId = cardId,
                Owner = owner,
                Zone = zone,
                Rested = false,
                PlayedOnTurn = null,
            };
        }

        private static DonInstance CreateDonInstance(string seat, PlayerState p)
        {
            p.DonInstanceCounter += 1;
            return new DonInstance { InstanceId = $"{seat}-DON-{p.DonInstanceCounter}", Rested = false };
        }

        private static void SetupLife(GameState state, string seat)
        {
            var p = Player(state, seat);
            int lifeCount = CardData.GetCard(p.Leader.CardId).Life ?? 5;
            for (int i = 0; i < lifeCount; i++)
            {
                var top = Shift(p.Deck);
                if (top != null) { top.Zone = "life"; p.Life.Add(top); }
            }
        }

        // ---- Turn structure ----------------------------------------------

        private static void ApplyStartOfTurn(GameState state)
        {
            var seat = state.ActiveSeat;
            var p = Player(state, seat);
            p.TurnsStarted += 1;
            p.AbilityUsedThisTurn.Clear();
            state.TemporaryPowerBonus.Clear();
            state.NoBlockerGrantedThisTurn.Clear();
            state.AttackCountThisTurn.Clear();
            // CleanupTurnModifiers runs BEFORE unrest so "freeze" modifiers that last
            // only "thisTurn" correctly expire and let the card be refreshed normally.
            CleanupTurnModifiers(state);
            state.Phase = "refresh";
            p.Leader.Rested = false;
            ReturnAttachedDon(p, p.Leader);
            foreach (var c in p.CharacterArea)
            {
                if (c == null) continue;
                ReturnAttachedDon(p, c);
                // A "freeze" modifier that survived turn cleanup keeps this card rested.
                if (!HasModifier(state, c, "freeze")) c.Rested = false;
                c.PlayedOnTurn = null;
            }
            if (p.Stage != null) { ReturnAttachedDon(p, p.Stage); p.Stage.Rested = false; }
            foreach (var d in p.CostArea) d.Rested = false;
            state.Phase = "draw";
            if (state.TurnNumber != 1) DrawCard(state, seat, true);
            state.Phase = "don";
            int donToDraw = state.TurnNumber == 1 ? 1 : 2;
            DrawDon(state, seat, donToDraw, true);
            state.Phase = "main";
            Log(state, seat, $"{p.Name} begins turn {state.TurnNumber}.");
            ApplyStartOfTurnEffects(state, seat);
        }

        // Scan the active player's cards for [Start of Your Turn] triggered effects.
        private static void ApplyStartOfTurnEffects(GameState state, string seat)
        {
            var p = Player(state, seat);
            var cards = new List<CardInstance>(p.CharacterArea.Where(c => c != null));
            if (p.Leader != null) cards.Add(p.Leader);
            foreach (var c in cards)
            {
                var def = GetCard(c);
                if (!HasTiming(def.Effect, "Start of Your Turn")) continue;
                int donReq = ParseDonThreshold(def.Effect);
                if (donReq > 0 && c.AttachedDonIds.Count < donReq) continue;
                QueueEffect(state, seat, c, "startOfYourTurn", def.Effect, true);
            }
        }

        private static void ReturnAttachedDon(PlayerState p, CardInstance instance)
        {
            if (instance.AttachedDonIds.Count == 0) return;
            foreach (var donId in instance.AttachedDonIds)
                p.CostArea.Add(new DonInstance { InstanceId = donId, Rested = true });
            instance.AttachedDonIds = new List<string>();
        }

        private static void DrawCard(GameState state, string seat, bool silent = false)
        {
            var p = Player(state, seat);
            var top = Shift(p.Deck);
            if (top == null) { Log(state, "system", $"{p.Name} cannot draw. Deck is empty."); return; }
            top.Zone = "hand";
            p.Hand.Add(top);
            if (!silent) Log(state, seat, $"{p.Name} draws a card.");
        }

        private static void ManualDrawCard(GameState state, string seat)
        {
            if (state.ActiveSeat != seat || state.Phase != "draw" || state.Battle != null) return;
            DrawCard(state, seat);
        }

        private static void DrawDon(GameState state, string seat, int amount, bool silent = false)
        {
            var p = Player(state, seat);
            int count = Math.Min(amount, p.DonDeck);
            p.DonDeck -= count;
            for (int i = 0; i < count; i++) p.CostArea.Add(CreateDonInstance(seat, p));
            if (!silent) Log(state, seat, $"{p.Name} adds {count} DON!! from their DON!! deck.");
        }

        private static void ManualDrawDon(GameState state, string seat, int amount)
        {
            if (state.ActiveSeat != seat || state.Phase != "don" || state.Battle != null) return;
            DrawDon(state, seat, amount);
        }

        private static void PayDonCost(PlayerState p, int cost)
        {
            int remaining = cost;
            foreach (var d in p.CostArea) { if (remaining <= 0) break; if (!d.Rested) { d.Rested = true; remaining -= 1; } }
        }

        private static void RefundDonCost(PlayerState p, int cost)
        {
            int remaining = cost;
            foreach (var d in p.CostArea) { if (remaining <= 0) break; if (d.Rested) { d.Rested = false; remaining -= 1; } }
        }

        // ---- Main-phase actions ------------------------------------------

        private static void ReorderHand(GameState state, string seat, string instanceId, int targetIndex)
        {
            if (string.IsNullOrEmpty(seat) || !state.Players.ContainsKey(seat)) return;
            var p = Player(state, seat);
            int oldIndex = p.Hand.FindIndex(c => c.InstanceId == instanceId);
            if (oldIndex < 0) return;
            int insertIndex = Math.Max(0, Math.Min(targetIndex, p.Hand.Count));
            var card = p.Hand[oldIndex];
            p.Hand.RemoveAt(oldIndex);
            if (oldIndex < insertIndex) insertIndex -= 1;
            insertIndex = Math.Max(0, Math.Min(insertIndex, p.Hand.Count));
            p.Hand.Insert(insertIndex, card);
        }

        private static void PlayCard(GameState state, string seat, string instanceId, int? slotIndex)
        {
            if (!IsTurnPlayerInMain(state, seat)) return;
            var p = Player(state, seat);
            int index = p.Hand.FindIndex(c => c.InstanceId == instanceId);
            if (index < 0) return;
            var instance = p.Hand[index];
            var def = GetCard(instance);
            if (ActiveDonCount(p) < def.Cost) { Log(state, seat, $"Not enough active DON!! to play {NameId(def)}."); return; }
            PayDonCost(p, def.Cost);
            p.Hand.RemoveAt(index);
            instance.PlayedOnTurn = state.TurnNumber;

            if (def.Type == "character")
            {
                int openSlot = slotIndex ?? p.CharacterArea.FindIndex(e => e == null);
                bool boardFull = p.CharacterArea.All(e => e != null);
                if (openSlot < 0 || openSlot > 4)
                {
                    p.Hand.Add(instance);
                    RefundDonCost(p, def.Cost);
                    Log(state, seat, "No open character slot.");
                    return;
                }
                // Replacing an occupied slot is the 6th-character rule: you may play a Character with a
                // full board, then trash one of your Characters. We only allow it when the board is full
                // (otherwise an empty slot should be used).
                if (p.CharacterArea[openSlot] != null)
                {
                    if (!boardFull)
                    {
                        p.Hand.Add(instance);
                        RefundDonCost(p, def.Cost);
                        Log(state, seat, "No open character slot.");
                        return;
                    }
                    var replaced = p.CharacterArea[openSlot];
                    Log(state, seat, $"{p.Name} trashes {NameId(GetCard(replaced))} to make room.");
                    MoveToTrash(state, seat, replaced.InstanceId, true);
                }
                instance.Zone = "character";
                p.CharacterArea[openSlot] = instance;
            }
            else if (def.Type == "stage")
            {
                if (p.Stage != null) MoveToTrash(state, seat, p.Stage.InstanceId, true);
                instance.Zone = "stage";
                p.Stage = instance;
            }
            else
            {
                instance.Zone = "trash";
                p.Trash.Add(instance);
            }
            Log(state, seat, $"{p.Name} plays {NameId(def)}.");
            if (HasKeyword(instance, "Rush")) Log(state, seat, $"{NameId(def)} has [Rush] and can attack this turn.");
            if (def.Type == "event" && HasTiming(def.Effect, "Main")) QueueEffect(state, seat, instance, "main", def.Effect, true);
            else if (HasTiming(def.Effect, "On Play")) QueueEffect(state, seat, instance, "onPlay", def.Effect, true);
        }

        private static void AttachDon(GameState state, string seat, string target, int amount, List<string> donInstanceIds = null)
        {
            if (!IsTurnPlayerInMain(state, seat)) return;
            var p = Player(state, seat);
            var card = FindInPlay(p, target);
            if (card == null) return;

            List<DonInstance> moving;
            if (donInstanceIds != null && donInstanceIds.Count > 0)
            {
                var availableById = p.CostArea
                    .Where(d => !d.Rested)
                    .ToDictionary(d => d.InstanceId, d => d);
                moving = new List<DonInstance>();
                foreach (var donId in donInstanceIds)
                {
                    if (!string.IsNullOrEmpty(donId) && availableById.TryGetValue(donId, out var don))
                        moving.Add(don);
                }
            }
            else
            {
                var available = p.CostArea.Where(d => !d.Rested).ToList();
                int moveCount = Math.Min(amount, available.Count);
                moving = moveCount > 0 ? available.GetRange(0, moveCount) : new List<DonInstance>();
            }

            int count = moving.Count;
            if (count <= 0) return;
            var movingIds = new HashSet<string>(moving.Select(d => d.InstanceId));
            p.CostArea = p.CostArea.Where(d => !movingIds.Contains(d.InstanceId)).ToList();
            foreach (var d in moving) card.AttachedDonIds.Add(d.InstanceId);
            Log(state, seat, $"{p.Name} attaches {count} DON!! to {NameId(GetCard(card))}.");
        }

        // Resolves a card's own [Activate: Main] ability. Costs/effects beyond the [Once Per
        // Turn] gate are hand-implemented per card id, same as AutomatedCounterPower below -
        // OPTCGSim's compiled client uses the identical per-card pattern (see e.g. its
        // CardHasActivateMain/SetCardAbilityUsed/QueueUpActivateMainOfCard methods).
        private static void ActivateMain(GameState state, string seat, string instanceId)
        {
            if (!IsTurnPlayerInMain(state, seat)) return;
            var p = Player(state, seat);
            var card = FindInPlay(p, instanceId);
            if (card == null) return;
            var def = GetCard(card);
            if (!HasTiming(def.Effect, "Activate: Main")) return;

            bool oncePerTurn = def.Effect.IndexOf("[Once Per Turn]", StringComparison.OrdinalIgnoreCase) >= 0;
            if (oncePerTurn && p.AbilityUsedThisTurn.Contains(instanceId))
            {
                Log(state, seat, $"{NameId(def)}'s ability has already been used this turn.");
                return;
            }

            switch (def.Id)
            {
                // Leader Luffy / Nami: free, [Once Per Turn] - give up to 1 rested DON!! card.
                case "ST01-001":
                case "ST01-007":
                    if (oncePerTurn) p.AbilityUsedThisTurn.Add(instanceId);
                    QueueEffect(state, seat, card, "activateMain", def.Effect, true);
                    break;

                // Thousand Sunny: cost = rest this Stage (self-limiting, no separate counter needed).
                case "ST01-017":
                    if (card.Rested) { Log(state, seat, $"{NameId(def)} is already rested."); return; }
                    card.Rested = true;
                    QueueEffect(state, seat, card, "activateMain", def.Effect, true);
                    break;

                // Leader Eustass Kid: [Once Per Turn] ③ → trash 1 card from hand → set self active.
                // Cost (3 DON) is paid up-front; if no hand cards exist the ability still fails early.
                case "ST02-001":
                    if (ActiveDonCount(p) < 3) { Log(state, seat, $"Not enough active DON!! (need 3) to activate {NameId(def)}."); return; }
                    if (p.Hand.Count == 0) { Log(state, seat, $"No cards in hand to trash for {NameId(def)}."); return; }
                    PayDonCost(p, 3);
                    if (oncePerTurn) p.AbilityUsedThisTurn.Add(instanceId);
                    // TargetZone.Hand routes hand-card clicks to resolveEffect in the UI layer.
                    QueueEffect(state, seat, card, "activateMain", def.Effect, false, EffectScope.Instant, EffectTargetZone.Hand);
                    break;

                // Jewelry Bonney: cost = rest 1 active DON!! + rest this Character. Look at top 5,
                // may take up to 1 {Supernovas} card to hand, then place the rest at the bottom of
                // the deck in any order (handled by the DeckLook UI flow).
                case "ST02-007":
                    if (card.Rested) { Log(state, seat, $"{NameId(def)} is already resting."); return; }
                    if (ActiveDonCount(p) < 1) { Log(state, seat, $"Not enough active DON!! to activate {NameId(def)}."); return; }
                    PayDonCost(p, 1);
                    card.Rested = true;
                    StartDeckLook(state, seat, card, "Supernovas", 5);
                    break;

                default:
                    // Generic fallback: parse the DON cost circled digit, pay it, then queue
                    // the effect text for TryResolveKnownEffect.  Cards whose effect text
                    // matches no known pattern will log "acknowledged for manual resolution".
                    int genericCost = ParseActivateMainCost(def.Effect);
                    if (genericCost > 0 && ActiveDonCount(p) < genericCost)
                    {
                        Log(state, seat, $"Not enough active DON!! (need {genericCost}) to activate {NameId(def)}.");
                        return;
                    }
                    if (genericCost > 0) PayDonCost(p, genericCost);
                    if (oncePerTurn) p.AbilityUsedThisTurn.Add(instanceId);
                    // Deck search: "Look at/Search your entire deck … add to hand … shuffle."
                    if ((ContainsAll(def.Effect, "Look at your entire deck") || ContainsAll(def.Effect, "Look at your deck")
                         || ContainsAll(def.Effect, "Search your deck")) && ContainsAll(def.Effect, "hand"))
                    {
                        int maxCostS = ParseCostFilter(def.Effect);
                        string featureS = ParseCurlyBraceTag(def.Effect);
                        string typeS = ContainsAll(def.Effect, "Character") ? "character"
                                     : ContainsAll(def.Effect, "Event") ? "event"
                                     : ContainsAll(def.Effect, "Stage") ? "stage" : "";
                        StartDeckSearch(state, seat, card, featureS, maxCostS, typeS);
                    }
                    // Deck-look pattern: "Look at N cards from the top of your deck".
                    else if (ContainsAll(def.Effect, "Look at", "from the top of your deck") && ContainsAll(def.Effect, "to your hand"))
                    {
                        int lookN = ParseLookCount(def.Effect);
                        string filter = ParseCurlyBraceTag(def.Effect);
                        StartDeckLook(state, seat, card, filter, lookN);
                    }
                    else
                    {
                        EffectTargetZone zone = InferTargetZone(def.Effect);
                        QueueEffect(state, seat, card, "activateMain", def.Effect, true, EffectScope.Instant, zone);
                    }
                    break;
            }
        }

        private static void StartDeckLook(GameState state, string seat, CardInstance source, string featureFilter, int count)
        {
            var p = Player(state, seat);
            var looked = new List<CardInstance>();
            for (int i = 0; i < count && p.Deck.Count > 0; i++) looked.Add(Shift(p.Deck));
            state.DeckLook = new DeckLookState
            {
                Seat = seat,
                SourceInstanceId = source.InstanceId,
                SourceName = GetCard(source).Name,
                FeatureFilter = featureFilter,
                Step = "select",
                Cards = looked,
                MaxCost = -1,
            };
            Log(state, seat, $"{NameId(GetCard(source))} looks at the top {looked.Count} card(s) of the deck.");
        }

        // Full-deck search: move entire deck to DeckLookState.Cards (SearchMode = true).
        // Player picks 0 or 1 matching card; remaining cards are shuffled back into the deck.
        // featureFilter: type tag required (e.g. "Supernovas"), or "" for none.
        // maxCost: -1 = no cost limit; otherwise only cards with Cost <= maxCost qualify.
        // cardTypeFilter: "character" / "event" / "stage" / "" for any.
        private static void StartDeckSearch(GameState state, string seat, CardInstance source,
            string featureFilter, int maxCost, string cardTypeFilter)
        {
            var p = Player(state, seat);
            var allCards = new List<CardInstance>(p.Deck);
            p.Deck.Clear();
            state.DeckLook = new DeckLookState
            {
                Seat = seat,
                SourceInstanceId = source.InstanceId,
                SourceName = GetCard(source).Name,
                FeatureFilter = featureFilter,
                Step = "select",
                Cards = allCards,
                SearchMode = true,
                MaxCost = maxCost,
                CardTypeFilter = cardTypeFilter,
            };
            Log(state, seat, $"{NameId(GetCard(source))} searches the deck ({allCards.Count} card(s) available).");
        }

        // Delegates to CardDef.HasFeature which reads the proper Features list.
        private static bool FeatureMatches(CardDef def, string feature) => def.HasFeature(feature);

        private static void ResolveDeckLookSelect(GameState state, string seat, string targetInstanceId)
        {
            var dl = state.DeckLook;
            if (dl == null || dl.Seat != seat || dl.Step != "select") return;
            var p = Player(state, seat);

            if (!string.IsNullOrEmpty(targetInstanceId))
            {
                var idx = dl.Cards.FindIndex(c => c.InstanceId == targetInstanceId);
                if (idx < 0) return;
                var def = GetCard(dl.Cards[idx]);
                // Feature filter
                if (!string.IsNullOrEmpty(dl.FeatureFilter) && !FeatureMatches(def, dl.FeatureFilter))
                {
                    Log(state, seat, $"{NameId(def)} does not match the required {{{dl.FeatureFilter}}} type.");
                    return;
                }
                // Cost filter (search mode)
                if (dl.MaxCost >= 0 && def.Cost > dl.MaxCost)
                {
                    Log(state, seat, $"{NameId(def)} costs too much (max cost {dl.MaxCost}).");
                    return;
                }
                // Card type filter (search mode)
                if (!string.IsNullOrEmpty(dl.CardTypeFilter) && !def.Type.Equals(dl.CardTypeFilter, StringComparison.OrdinalIgnoreCase))
                {
                    Log(state, seat, $"{NameId(def)} is not a valid type for this search.");
                    return;
                }
                var taken = dl.Cards[idx];
                dl.Cards.RemoveAt(idx);
                taken.Zone = "hand";
                p.Hand.Add(taken);
                Log(state, seat, $"{p.Name} adds {NameId(def)} to hand.");
            }
            else
            {
                Log(state, seat, $"{p.Name} takes nothing.");
            }

            if (dl.SearchMode)
            {
                // Shuffle all remaining cards back into the deck (deck search mechanic).
                string shuffleSeed = $"{state.Seed}:{seat}:search:{state.EffectSequence}";
                ShuffleInPlace(dl.Cards, shuffleSeed);
                foreach (var c in dl.Cards) { c.Zone = "deck"; p.Deck.Add(c); }
                Log(state, seat, $"{p.Name} shuffles the deck.");
                state.DeckLook = null;
                return;
            }

            dl.Step = "rearrange";
            if (dl.Cards.Count == 0) FinishDeckLook(state, seat, dl);
        }

        // Player free-arranges the remaining cards client-side (drag to reorder) and submits the
        // whole final order at once, rather than one placement per round-trip command.
        private static void ResolveDeckLookConfirmOrder(GameState state, string seat, List<string> orderedInstanceIds)
        {
            var dl = state.DeckLook;
            if (dl == null || dl.Seat != seat || dl.Step != "rearrange" || orderedInstanceIds == null) return;
            if (orderedInstanceIds.Count != dl.Cards.Count) return;
            var ordered = new List<CardInstance>(dl.Cards.Count);
            foreach (var id in orderedInstanceIds)
            {
                var card = dl.Cards.Find(c => c.InstanceId == id);
                if (card == null) return; // not a valid permutation of the remaining cards
                ordered.Add(card);
            }
            dl.Ordered = ordered;
            dl.Cards.Clear();
            FinishDeckLook(state, seat, dl);
        }

        private static void FinishDeckLook(GameState state, string seat, DeckLookState dl)
        {
            var p = Player(state, seat);
            foreach (var c in dl.Ordered)
            {
                c.Zone = "deck";
                p.Deck.Add(c);
            }
            Log(state, seat, $"{p.Name} places {dl.Ordered.Count} card(s) at the bottom of the deck.");
            state.DeckLook = null;
        }

        private static void SetRested(GameState state, string seat, string target, bool rested)
        {
            if (state.Battle != null) return;
            var card = FindInPlay(Player(state, seat), target);
            if (card == null) return;
            if (!rested && HasModifier(state, card, "freeze"))
            {
                Log(state, seat, $"{NameId(GetCard(card))} is frozen and cannot be set active.");
                return;
            }
            card.Rested = rested;
            Log(state, seat, $"{NameId(GetCard(card))} is {(rested ? "rested" : "active")}.");
        }

        // ---- Battle state machine ----------------------------------------

        private static void DeclareAttack(GameState state, string seat, string attackerId, string targetId)
        {
            if (!IsTurnPlayerInMain(state, seat)) return;
            var p = Player(state, seat);
            var attacker = FindInPlay(p, attackerId);
            var defender = FindInPlay(Player(state, OtherSeat(seat)), targetId);
            if (p.TurnsStarted <= 1)
            {
                Log(state, seat, "Neither player can battle on their first turn.");
                return;
            }
            if (attacker == null || defender == null) return;

            // Rested check: Double Attack cards may attack a second time while rested.
            if (attacker.Rested)
            {
                bool dblAtk = HasDoubleAttack(state, attacker);
                int atkCount = state.AttackCountThisTurn.TryGetValue(attacker.InstanceId, out int ac) ? ac : 0;
                if (!dblAtk || atkCount >= 2)
                {
                    if (dblAtk) Log(state, seat, $"{NameId(GetCard(attacker))} has already made 2 attacks this turn.");
                    return;
                }
            }

            if (HasModifier(state, attacker, "cannotAttack"))
            {
                Log(state, seat, $"{NameId(GetCard(attacker))} cannot attack this turn.");
                return;
            }
            if (attacker.PlayedOnTurn == state.TurnNumber && !HasRush(state, attacker))
            {
                Log(state, seat, "Only [Rush] characters can attack on the turn they are played.");
                return;
            }
            var defenderDef = GetCard(defender);
            // canAttackActive: this attacker may target active (non-rested) characters.
            bool canHitActive = HasModifier(state, attacker, "canAttackActive");
            if (defenderDef.Type == "character" && !defender.Rested && !canHitActive)
            {
                Log(state, seat, "Only the opponent's leader or rested characters can be attacked.");
                return;
            }
            attacker.Rested = true;
            // Track attack count for Double Attack bookkeeping.
            state.AttackCountThisTurn[attacker.InstanceId] =
                (state.AttackCountThisTurn.TryGetValue(attacker.InstanceId, out int prev) ? prev : 0) + 1;
            state.Phase = "battle";
            string battleId = NextBattleId(state);
            state.Battle = new BattleState
            {
                Id = battleId,
                Step = "block",
                AttackerSeat = seat,
                AttackerId = attackerId,
                TargetSeat = OtherSeat(seat),
                TargetId = targetId,
                OriginalTargetId = targetId,
                Blocked = false,
                CounterPower = 0,
                AttackPower = GetPower(state, attacker),
                DefensePower = GetPower(state, defender),
            };
            Log(state, seat, $"{NameId(GetCard(attacker))} ({state.Battle.AttackPower}) attacks {NameId(GetCard(defender))} ({state.Battle.DefensePower}).");
            ApplyWhenAttackingEffects(state, seat, attacker, defenderDef);
        }

        // [When Attacking] effects, applied right after the battle is declared.
        private static void ApplyWhenAttackingEffects(GameState state, string seat, CardInstance attacker, CardDef defenderDef)
        {
            var don = attacker.AttachedDonIds.Count;
            switch (attacker.CardId)
            {
                case "ST01-002": // Usopp: [DON!!x2] opponent can't Blocker with 5000+ power this battle.
                    if (don >= 2) state.Battle.BlockerPowerBan = 5000;
                    break;
                case "ST01-005": // Jinbe: [DON!!x1] +1000 power to up to 1 other Leader/Character this turn.
                    if (don >= 1) QueueEffect(state, seat, attacker, "whenAttacking", GetCard(attacker).Effect, true);
                    break;
                case "ST01-012": // Luffy (5-cost): [DON!!x2] opponent can't Blocker at all this battle.
                    if (don >= 2) state.Battle.NoBlocker = true;
                    break;
                case "ST02-008": // Apoo: [DON!!x1] rest up to 1 of the opponent's active DON!!.
                    if (don >= 1)
                    {
                        var opponent = Player(state, OtherSeat(seat));
                        var activeDon = opponent.CostArea.FirstOrDefault(d => !d.Rested);
                        if (activeDon != null)
                        {
                            activeDon.Rested = true;
                            Log(state, seat, $"{NameId(GetCard(attacker))} rests 1 of {opponent.Name}'s DON!!.");
                        }
                    }
                    break;
                case "ST02-010": // Hawkins: [DON!!x1][Once Per Turn][Your Turn] if battling opp Character, set self active.
                    if (don >= 1 && defenderDef.Type == "character" && !Player(state, seat).AbilityUsedThisTurn.Contains(attacker.InstanceId))
                    {
                        Player(state, seat).AbilityUsedThisTurn.Add(attacker.InstanceId);
                        attacker.Rested = false;
                        Log(state, seat, $"{NameId(GetCard(attacker))} sets itself as active.");
                    }
                    break;
            }
            if (state.NoBlockerGrantedThisTurn.Contains(attacker.InstanceId)) state.Battle.NoBlocker = true; // Diable Jambe grant.

            // Generic fallback for [When Attacking] effects not handled by the switch above.
            // Checks DON requirement and once-per-turn gate, then queues the effect for resolution.
            var atkDef = GetCard(attacker);
            if (HasTiming(atkDef.Effect, "When Attacking"))
            {
                // Already handled by the switch for known cards — skip those.
                bool handled = attacker.CardId == "ST01-002" || attacker.CardId == "ST01-005" ||
                               attacker.CardId == "ST01-012" || attacker.CardId == "ST02-008" ||
                               attacker.CardId == "ST02-010";
                if (!handled)
                {
                    int donReq = ParseDonThreshold(atkDef.Effect);
                    bool donOk = donReq <= 0 || attacker.AttachedDonIds.Count >= donReq;
                    bool optFlag = atkDef.Effect.IndexOf("[Once Per Turn]", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool alreadyUsed = optFlag && Player(state, seat).AbilityUsedThisTurn.Contains(attacker.InstanceId);
                    if (donOk && !alreadyUsed)
                    {
                        if (optFlag) Player(state, seat).AbilityUsedThisTurn.Add(attacker.InstanceId);
                        QueueEffect(state, seat, attacker, "whenAttacking", atkDef.Effect, true);
                    }
                }
            }
        }

        private static void BlockAttack(GameState state, string defenderSeat, string blockerId)
        {
            if (state.Battle == null || state.Battle.Step != "block" || state.Battle.TargetSeat != defenderSeat) return;
            var defender = Player(state, defenderSeat);
            var blocker = FindInPlay(defender, blockerId);
            // Blocker keyword check: printed keyword OR granted via CardModifier.
            if (blocker == null || blocker.Rested || (!HasKeyword(blocker, "Blocker") && !HasKeywordModifier(state, blocker, "Blocker"))) return;
            if (blocker.InstanceId == state.Battle.TargetId) return;
            if (state.Battle.NoBlocker) return;
            if (state.Battle.BlockerPowerBan.HasValue && GetPower(state, blocker) >= state.Battle.BlockerPowerBan.Value) return;
            var attacker = FindInPlay(Player(state, state.Battle.AttackerSeat), state.Battle.AttackerId);
            // Unblockable: printed keyword OR granted via CardModifier.
            if (attacker == null || HasKeyword(attacker, "Unblockable") || HasKeywordModifier(state, attacker, "Unblockable")) return;
            blocker.Rested = true;
            state.Battle.TargetId = blocker.InstanceId;
            state.Battle.Blocked = true;
            state.Battle.DefensePower = GetPower(state, blocker) + state.Battle.CounterPower;
            Log(state, defenderSeat, $"{NameId(GetCard(blocker))} blocks the attack.");

            // Some cards win the match outright when their controller's opponent
            // activates [Blocker] while either player is at 0 Life (e.g. Gol.D.Roger).
            var attackerPlayer = Player(state, state.Battle.AttackerSeat);
            var winCard = FindOpponentBlockWinCard(attackerPlayer);
            if (winCard != null && (attackerPlayer.Life.Count == 0 || defender.Life.Count == 0))
            {
                FinishGame(state);
                Log(state, "system", $"{attackerPlayer.Name} wins instantly — {NameId(winCard)} triggers when a Blocker is activated with a player at 0 Life.");
                return;
            }

            PassBlock(state, defenderSeat, true);
        }

        // Finds a card (leader or character) this player controls that grants an
        // instant win when their opponent activates [Blocker] at 0 Life (e.g. Roger).
        private static CardInstance FindOpponentBlockWinCard(PlayerState p)
        {
            if (p.Leader != null && CardData.WinsWhenOpponentBlocks(p.Leader.CardId)) return p.Leader;
            return p.CharacterArea.FirstOrDefault(c => c != null && CardData.WinsWhenOpponentBlocks(c.CardId));
        }

        private static void PassBlock(GameState state, string defenderSeat, bool silent = false)
        {
            if (state.Battle == null || state.Battle.Step != "block" || state.Battle.TargetSeat != defenderSeat) return;
            state.Battle.Step = "counter";
            if (!silent) Log(state, defenderSeat, $"{Player(state, defenderSeat).Name} passes blockers.");
        }

        private static void CounterWithCard(GameState state, string defenderSeat, string instanceId)
        {
            if (state.Battle == null || state.Battle.Step != "counter" || state.Battle.TargetSeat != defenderSeat) return;
            var defender = Player(state, defenderSeat);
            int index = defender.Hand.FindIndex(e => e.InstanceId == instanceId);
            if (index < 0) return;
            var counterCard = defender.Hand[index];
            var counterDef = GetCard(counterCard);
            int counterPower = AutomatedCounterPower(counterCard);
            if (counterPower <= 0)
            {
                Log(state, defenderSeat, $"{NameId(GetCard(counterCard))} has no usable counter value.");
                return;
            }
            if (counterDef.Type == "event" && ActiveDonCount(defender) < counterDef.Cost)
            {
                Log(state, defenderSeat, $"Not enough active DON!! to counter with {NameId(counterDef)}.");
                return;
            }
            if (counterDef.Type == "event") PayDonCost(defender, counterDef.Cost);
            defender.Hand.RemoveAt(index);
            counterCard.Zone = "trash";
            defender.Trash.Add(counterCard);
            state.Battle.CounterPower += counterPower;
            state.Battle.DefensePower += counterPower;
            Log(state, defenderSeat, $"{defender.Name} counters with {NameId(GetCard(counterCard))} (+{counterPower}).");

            // Generic secondary effect: anything after "Then," in the counter event text.
            // Queued as a pending effect so TryResolveKnownEffect handles it (e.g. set DON!! active,
            // draw cards, rest opponent's character, etc.).
            string effectText = counterDef.Effect ?? "";
            int thenIdx = effectText.IndexOf("Then,", StringComparison.OrdinalIgnoreCase);
            if (thenIdx < 0) thenIdx = effectText.IndexOf("Then ", StringComparison.OrdinalIgnoreCase);
            if (thenIdx >= 0)
            {
                string secondary = effectText.Substring(thenIdx).Trim();
                if (IsAutomatedEffectPattern(secondary))
                    QueueEffect(state, defenderSeat, counterCard, "counter", secondary, true);
                else
                    Log(state, defenderSeat, $"{NameId(counterDef)} secondary effect requires manual resolution: {secondary}");
            }
        }

        private static int AutomatedCounterPower(CardInstance instance)
        {
            var def = GetCard(instance);
            // Characters/stages with a printed Counter value
            if (def.Counter > 0) return def.Counter;
            if (def.Type != "event" || !def.Keywords.Contains("Counter")) return 0;
            // Events: parse the counter power from text (e.g. "gives your Leader or Character +2000 power")
            // or from the card's Counter field if populated; otherwise parse "+NNNN" from effect/counter text.
            string counterText = def.Counter > 0 ? def.Counter.ToString() : (def.Effect ?? "");
            var m = System.Text.RegularExpressions.Regex.Match(
                counterText, @"\+(\d{3,5})\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (m.Success && int.TryParse(m.Groups[1].Value, out int parsed)) return parsed;
            return 0;
        }

        private static void PassCounter(GameState state, string defenderSeat)
        {
            if (state.Battle == null || state.Battle.Step != "counter" || state.Battle.TargetSeat != defenderSeat) return;
            state.Battle.Step = "damage";
            Log(state, defenderSeat, $"{Player(state, defenderSeat).Name} passes counters.");
        }

        private static void TakeLife(GameState state, string defenderSeat)
        {
            var p = Player(state, defenderSeat);
            var cardFromLife = Pop(p.Life);
            string endedBattleId = state.Battle?.Id;
            if (cardFromLife != null)
            {
                cardFromLife.Zone = "hand";
                p.Hand.Add(cardFromLife);
                Log(state, defenderSeat, $"{p.Name} takes 1 life to hand.");
                state.Phase = "main";
            }
            else
            {
                state.Status = "finished";
                state.Phase = "finished";
                Log(state, "system", $"{Player(state, OtherSeat(defenderSeat)).Name} wins.");
            }
            state.Battle = null;
            CleanupBattleModifiers(state, endedBattleId);
        }

        // [Banish] path: the attacker's damage trashes the life card instead of sending it to hand;
        // no Trigger step occurs. Win condition check is identical to RevealLifeAndStartTrigger.
        private static void BanishLifeCard(GameState state, string defenderSeat)
        {
            var p = Player(state, defenderSeat);
            string endedBattleId = state.Battle?.Id;
            var lifeCard = Pop(p.Life);
            if (lifeCard == null)
            {
                state.Status = "finished";
                state.Phase = "finished";
                Log(state, "system", $"{Player(state, OtherSeat(defenderSeat)).Name} wins.");
                state.Battle = null;
                CleanupBattleModifiers(state, endedBattleId);
                return;
            }
            lifeCard.Zone = "trash";
            p.Trash.Add(lifeCard);
            Log(state, defenderSeat, $"[Banish] {NameId(GetCard(lifeCard))} is trashed (no Trigger).");
            state.Battle = null;
            state.Phase = "main";
            CleanupBattleModifiers(state, endedBattleId);
        }

        // Reveal the top life card and enter the Trigger Step. Auto-resolves when the
        // revealed card has no imported trigger text.
        private static void RevealLifeAndStartTrigger(GameState state, string defenderSeat)
        {
            var p = Player(state, defenderSeat);
            var cardFromLife = Pop(p.Life);
            if (cardFromLife == null)
            {
                state.Status = "finished";
                state.Phase = "finished";
                Log(state, "system", $"{Player(state, OtherSeat(defenderSeat)).Name} wins.");
                state.Battle = null;
                return;
            }
            state.Battle.RevealedLife = cardFromLife;
            state.Battle.Step = "trigger";
            // Taking damage just takes the top life card to hand; the card is NOT revealed unless its
            // owner chooses to use its [Trigger]. So log only the damage here, never the card identity.
            Log(state, defenderSeat, $"{p.Name} takes 1 damage.");
            if (string.IsNullOrWhiteSpace(GetCard(cardFromLife).Trigger)) FinalizeTrigger(state, defenderSeat);
        }

        private static void FinalizeTrigger(GameState state, string defenderSeat)
        {
            var p = Player(state, defenderSeat);
            var cardFromLife = state.Battle?.RevealedLife;
            string endedBattleId = state.Battle?.Id;
            if (cardFromLife != null)
            {
                cardFromLife.Zone = "hand";
                p.Hand.Add(cardFromLife);
            }
            state.Battle = null;
            state.Phase = "main";
            CleanupBattleModifiers(state, endedBattleId);
        }

        private static void UseTrigger(GameState state, string seat)
        {
            if (state.Battle == null || state.Battle.Step != "trigger") return;
            var defenderSeat = state.Battle.TargetSeat;
            if (seat != defenderSeat) return;
            var cardFromLife = state.Battle.RevealedLife;
            if (cardFromLife == null) { FinalizeTrigger(state, defenderSeat); return; }
            Log(state, defenderSeat, $"{Player(state, defenderSeat).Name} reveals and activates {NameId(GetCard(cardFromLife))}'s [Trigger].");
            if (TryResolveKnownTrigger(state, defenderSeat, cardFromLife)) return;
            var triggerText = GetCard(cardFromLife).Trigger;
            Log(state, defenderSeat, $"{NameId(GetCard(cardFromLife))} Trigger needs manual resolution: {triggerText}");
            FinalizeTrigger(state, defenderSeat);
        }

        private static void PassTrigger(GameState state, string seat)
        {
            if (state.Battle == null || state.Battle.Step != "trigger") return;
            var defenderSeat = state.Battle.TargetSeat;
            if (seat != defenderSeat) return;
            FinalizeTrigger(state, defenderSeat);
        }

        private static void ResolveAttack(GameState state)
        {
            if (state.Battle == null) return;
            if (state.Battle.Step == "block") { PassBlock(state, state.Battle.TargetSeat); return; }
            if (state.Battle.Step == "counter") { PassCounter(state, state.Battle.TargetSeat); return; }
            if (state.Battle.Step != "damage") return;

            var targetSeat = state.Battle.TargetSeat;
            var target = FindInPlay(Player(state, targetSeat), state.Battle.TargetId);
            if (target == null) { state.Battle = null; return; }

            // Recompute live powers so any buffs applied during the battle (BattlePowerBonus,
            // TemporaryPowerBonus) are included. CounterPower is accumulated separately and
            // does not flow through GetPower, so it's added explicitly for the defense side.
            var liveAttacker = FindInPlay(Player(state, state.Battle.AttackerSeat), state.Battle.AttackerId);
            int liveAttackPower = liveAttacker != null ? GetPower(state, liveAttacker) : state.Battle.AttackPower;
            int liveDefensePower = GetPower(state, target) + state.Battle.CounterPower;

            if (liveAttackPower >= liveDefensePower)
            {
                var targetDef = GetCard(target);
                if (targetDef.Type == "leader")
                {
                    var atkCard = FindInPlay(Player(state, state.Battle.AttackerSeat), state.Battle.AttackerId);
                    if (atkCard != null && HasBanish(state, atkCard)) BanishLifeCard(state, targetSeat);
                    else RevealLifeAndStartTrigger(state, targetSeat);
                }
                else
                {
                    string koedBattleId = state.Battle.Id;
                    if (HasModifier(state, target, "cannotBeKod"))
                    {
                        Log(state, "system", $"{NameId(targetDef)} cannot be K.O.'d and survives the battle.");
                        state.Battle = null;
                        state.Phase = "main";
                        CleanupBattleModifiers(state, koedBattleId);
                    }
                    else
                    {
                        MoveToTrash(state, targetSeat, state.Battle.TargetId);
                        state.Battle = null;
                        CleanupBattleModifiers(state, koedBattleId);
                    }
                }
                if (state.Battle == null && state.Status == "active") state.Phase = "main";
                return;
            }

            Log(state, "system", "Attack resolves with no damage.");
            string endedId = state.Battle.Id;
            state.Battle = null;
            state.Phase = "main";
            CleanupBattleModifiers(state, endedId);
        }

        private enum EffectResolution
        {
            Resolved,
            WaitingForTarget,
            NotAutomated
        }

        private static void ResolveEffect(GameState state, string seat, string effectId, string targetId)
        {
            var effect = FindPendingEffect(state, seat, effectId);
            if (effect == null) return;

            var result = TryResolveKnownEffect(state, effect, targetId);
            if (result == EffectResolution.WaitingForTarget) return;

            state.PendingEffects.Remove(effect);
            if (result == EffectResolution.NotAutomated)
            {
                var source = CardData.GetCard(effect.SourceCardId);
                Log(state, effect.Seat, $"{source.Name} effect acknowledged for manual resolution: {effect.Text}");
            }
        }

        private static void PassEffect(GameState state, string seat, string effectId)
        {
            var effect = FindPendingEffect(state, seat, effectId);
            if (effect == null || !effect.Optional) return;
            state.PendingEffects.Remove(effect);
            Log(state, seat, $"{NameId(CardData.GetCard(effect.SourceCardId))} effect skipped.");
        }

        // "Choose one: •A •B" resolution. Target = "A" queues OptionA as a new pending effect;
        // Target = "B" queues OptionB.  The split is done by the bullet character '•'.
        private static void ResolveChoice(GameState state, string seat, string choice)
        {
            var ch = state.ActiveChoice;
            if (ch == null || ch.Seat != seat) return;
            string chosen = (choice == "B") ? ch.OptionB : ch.OptionA;
            state.ActiveChoice = null;
            if (string.IsNullOrWhiteSpace(chosen)) return;
            var src = FindAnyInPlay(state, ch.SourceInstanceId, out _) ?? Player(state, seat).Leader;
            if (src != null)
            {
                // Infer the target zone from the sub-option text so the UI routes correctly.
                EffectTargetZone zone = InferTargetZone(chosen);
                QueueEffect(state, seat, src, ch.Timing, chosen.Trim(), false, EffectScope.Instant, zone);
            }
            Log(state, seat, $"Chose: {chosen.Trim()}");
        }

        // Derive the EffectTargetZone a sub-effect needs based on its text.
        private static EffectTargetZone InferTargetZone(string text)
        {
            if (ContainsAll(text, "from your hand") || ContainsAll(text, "from your opponent's hand")) return EffectTargetZone.Hand;
            if (ContainsAll(text, "from your trash") || ContainsAll(text, "from your opponent's trash")) return EffectTargetZone.Trash;
            return EffectTargetZone.Play;
        }

        // Search every zone across both seats for a card by instance id.
        // Used when the source card may have moved to trash (events, counter cards).
        private static CardInstance FindCardInstance(GameState state, string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId)) return null;
            foreach (var s in Seats())
            {
                var p = Player(state, s);
                var inPlay = FindInPlay(p, instanceId);
                if (inPlay != null) return inPlay;
                var inTrash = p.Trash.FirstOrDefault(c => c.InstanceId == instanceId);
                if (inTrash != null) return inTrash;
                var inHand = p.Hand.FirstOrDefault(c => c.InstanceId == instanceId);
                if (inHand != null) return inHand;
            }
            return null;
        }

        // Clone a PendingEffect with a different text, preserving all other fields.
        // Used by the multi-clause splitter to process one sentence at a time.
        private static PendingEffect ShallowCloneEffect(PendingEffect src, string newText) =>
            new PendingEffect
            {
                EffectId       = src.EffectId,
                Seat           = src.Seat,
                SourceInstanceId = src.SourceInstanceId,
                SourceCardId   = src.SourceCardId,
                Timing         = src.Timing,
                Text           = newText,
                Optional       = src.Optional,
                Scope          = src.Scope,
                TargetZone     = src.TargetZone,
            };

        // Returns the index of "Then," that begins a second clause ("<sentence>. Then, <rest>").
        // Returns -1 if no such split exists or the effect is a "Choose one" block.
        private static int FindThenClause(string text)
        {
            if (ContainsAll(text, "Choose one")) return -1;
            int idx = text.IndexOf(". Then,", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0) return idx + 2; // point just past ". " to "Then,"
            idx = text.IndexOf(".\nThen,", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0) return idx + 2;
            return -1;
        }

        // Evaluate the most common One Piece TCG conditional expressions.
        // Returns true if the condition is met for the given seat.
        private static bool EvaluateCondition(GameState state, string seat, string condition)
        {
            var p   = Player(state, seat);
            var opp = Player(state, OtherSeat(seat));

            // "you have N or more [Type] Characters in play"
            var charCount = System.Text.RegularExpressions.Regex.Match(
                condition, @"you have (\d+) or more .* Characters? in play",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (charCount.Success)
            {
                int n = int.Parse(charCount.Groups[1].Value);
                string tag = ParseCurlyBraceTag(condition);
                int count = p.CharacterArea.Count(c => c != null &&
                    (string.IsNullOrEmpty(tag) || GetCard(c).HasFeature(tag)));
                return count >= n;
            }

            // "your opponent has N or more cards in their hand"
            var oppHand = System.Text.RegularExpressions.Regex.Match(
                condition, @"opponent has (\d+) or more cards? in their hand",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (oppHand.Success)
                return opp.Hand.Count >= int.Parse(oppHand.Groups[1].Value);

            // "your Leader has the {Type} type"
            if (ContainsAll(condition, "Leader has the") && ContainsAll(condition, "type"))
            {
                string tag2 = ParseCurlyBraceTag(condition);
                return !string.IsNullOrEmpty(tag2) && GetCard(p.Leader).HasFeature(tag2);
            }

            // "your Leader's power is N or more"
            var leaderPwr = System.Text.RegularExpressions.Regex.Match(
                condition, @"Leader.s power is (\d+) or more",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (leaderPwr.Success)
                return GetPower(state, p.Leader) >= int.Parse(leaderPwr.Groups[1].Value);

            // "you have fewer cards in your hand than your opponent"
            if (ContainsAll(condition, "fewer cards in your hand than your opponent"))
                return p.Hand.Count < opp.Hand.Count;

            // Unknown condition: skip and log.
            Log(state, seat, $"Unknown condition '{condition}' — treating as not met.");
            return false;
        }

        // Parse "Choose one:" effects into ChoiceState.
        // Text format: "Choose one:\n• Option A\n• Option B" or "Choose one: •A •B".
        private static bool TryParseChoiceEffect(GameState state, PendingEffect effect)
        {
            var text = effect.Text ?? "";
            int chooseIdx = text.IndexOf("Choose one", StringComparison.OrdinalIgnoreCase);
            if (chooseIdx < 0) return false;
            // Split on bullet character (• U+2022) or "- " after the colon.
            int colonIdx = text.IndexOf(':', chooseIdx);
            if (colonIdx < 0) return false;
            string after = text.Substring(colonIdx + 1).Trim();
            // Try bullet split first, then newline split.
            string[] parts = after.Contains('•')
                ? after.Split('•')
                : after.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            var options = parts.Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
            if (options.Count < 2) return false;
            state.ActiveChoice = new ChoiceState
            {
                Seat = effect.Seat,
                SourceInstanceId = effect.SourceInstanceId,
                SourceCardId = effect.SourceCardId,
                Timing = effect.Timing,
                OptionA = options[0],
                OptionB = options[1],
            };
            state.PendingEffects.Remove(effect);
            Log(state, effect.Seat, $"{NameId(CardData.GetCard(effect.SourceCardId))}: Choose one effect pending.");
            return true;
        }

        private static PendingEffect FindPendingEffect(GameState state, string seat, string effectId)
        {
            if (state.PendingEffects.Count == 0) return null;
            if (!string.IsNullOrEmpty(effectId)) return state.PendingEffects.FirstOrDefault(e => e.EffectId == effectId && e.Seat == seat);
            return state.PendingEffects.FirstOrDefault(e => e.Seat == seat) ?? state.PendingEffects[0];
        }

        private static void QueueEffect(GameState state, string seat, CardInstance source, string timing, string text, bool optional,
            EffectScope scope = EffectScope.Instant, EffectTargetZone targetZone = EffectTargetZone.Play)
        {
            if (source == null || string.IsNullOrWhiteSpace(text)) return;
            state.EffectSequence += 1;
            state.PendingEffects.Add(new PendingEffect
            {
                EffectId = $"effect-{state.EffectSequence}",
                Seat = seat,
                SourceInstanceId = source.InstanceId,
                SourceCardId = source.CardId,
                Timing = timing,
                Text = text.Trim(),
                Optional = optional,
                Scope = scope,
                TargetZone = targetZone,
            });
            Log(state, seat, $"{NameId(GetCard(source))} {TimingLabel(timing)} effect is pending.");
        }

        // Returns true if `def` satisfies the feature-tag requirement stated in `effectText`.
        // Feature tags appear as {Feature Name} in card text, e.g. {Straw Hat Crew}, {Supernovas}.
        // When multiple tags are listed with "or" (e.g. {Supernovas} or {Heart Pirates}) the card
        // must match at least one. When no tags are present every card is considered a match.
        private static bool CardPassesFeatureFilter(string effectText, CardDef def)
        {
            if (string.IsNullOrEmpty(effectText) || !effectText.Contains("{")) return true;
            bool anyRequired = false;
            bool anyMatched = false;
            void Check(string tag) { anyRequired = true; if (def.HasFeature(tag)) anyMatched = true; }
            if (effectText.IndexOf("{Supernovas}", StringComparison.OrdinalIgnoreCase) >= 0)    Check("Supernovas");
            if (effectText.IndexOf("{Straw Hat Crew}", StringComparison.OrdinalIgnoreCase) >= 0) Check("Straw Hat Crew");
            if (effectText.IndexOf("{Heart Pirates}", StringComparison.OrdinalIgnoreCase) >= 0)  Check("Heart Pirates");
            if (effectText.IndexOf("{Kid Pirates}", StringComparison.OrdinalIgnoreCase) >= 0)    Check("Kid Pirates");
            if (effectText.IndexOf("{Navy}", StringComparison.OrdinalIgnoreCase) >= 0)           Check("Navy");
            return !anyRequired || anyMatched;
        }

        // Heuristic "is this card a legal pick for the current pending effect" used by the UI to glow
        // valid targets green. Mirrors the engine's own resolve-time checks (zone + ownership + feature
        // filter + cost/power caps). The authoritative validation still happens in ResolveEffect, so a
        // rare false positive just means a click is ignored - never an illegal play.
        public static bool IsValidEffectTarget(GameState state, PendingEffect effect, CardInstance card)
        {
            if (state == null || effect == null || card == null) return false;
            var def = GetCard(card);
            if (def == null) return false;
            string text = effect.Text ?? "";

            bool oppZone = text.IndexOf("opponent's hand", StringComparison.OrdinalIgnoreCase) >= 0
                        || text.IndexOf("opponent's trash", StringComparison.OrdinalIgnoreCase) >= 0;
            var zoneOwner = oppZone ? Player(state, OtherSeat(effect.Seat)) : Player(state, effect.Seat);

            switch (effect.TargetZone)
            {
                case EffectTargetZone.Hand:
                    if (!zoneOwner.Hand.Any(c => c.InstanceId == card.InstanceId)) return false;
                    break;
                case EffectTargetZone.Trash:
                    if (!zoneOwner.Trash.Any(c => c.InstanceId == card.InstanceId)) return false;
                    break;
                case EffectTargetZone.Play:
                case EffectTargetZone.Any:
                    bool isLeader = card.Zone == "leader";
                    bool isChar = card.Zone == "character";
                    if (!isLeader && !isChar) return false;
                    // Leader and Character are DISTINCT card types - only allow the type(s) the text names
                    // (e.g. "rested Characters" must not match a Leader).
                    bool textLeader = text.IndexOf("Leader", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool textChar = text.IndexOf("Character", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (textLeader || textChar)
                    {
                        if (isLeader && !textLeader) return false;
                        if (isChar && !textChar) return false;
                    }
                    bool mentionsOpp = text.IndexOf("opponent", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool mentionsYour = text.IndexOf("your ", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (mentionsOpp && !mentionsYour && card.Owner == effect.Seat) return false;
                    if (mentionsYour && !mentionsOpp && card.Owner != effect.Seat) return false;
                    // Rested/active requirement, e.g. "rested Characters ... as active" needs a rested target.
                    if (System.Text.RegularExpressions.Regex.IsMatch(text, "rested (leader|character)", System.Text.RegularExpressions.RegexOptions.IgnoreCase) && !card.Rested) return false;
                    if (System.Text.RegularExpressions.Regex.IsMatch(text, "active (leader|character)", System.Text.RegularExpressions.RegexOptions.IgnoreCase) && card.Rested) return false;
                    break;
                default:
                    return false;
            }

            if (!CardPassesFeatureFilter(text, def)) return false;

            int costCap = ParseLimit(text, @"cost (?:of )?(\d+) or less");
            if (costCap >= 0 && def.Cost > costCap) return false;

            int powerCap = ParseLimit(text, @"(\d{3,5}) power or less");
            if (powerCap < 0) powerCap = ParseLimit(text, @"power of (\d{3,5}) or less");
            if (powerCap >= 0 && GetPower(state, card) > powerCap) return false;

            return true;
        }

        private static int ParseLimit(string text, string pattern)
        {
            if (string.IsNullOrEmpty(text)) return -1;
            var m = System.Text.RegularExpressions.Regex.Match(text, pattern,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return m.Success ? int.Parse(m.Groups[1].Value) : -1;
        }

        private static string TimingLabel(string timing)
        {
            switch (timing)
            {
                case "onPlay":              return "[On Play]";
                case "trigger":             return "[Trigger]";
                case "main":                return "[Main]";
                case "whenAttacking":       return "[When Attacking]";
                case "activateMain":        return "[Activate: Main]";
                case "onKo":                return "[On KO]";
                case "endOfYourTurn":       return "[End of Your Turn]";
                case "endOfOpponentsTurn":  return "[End of Opponent's Turn]";
                case "startOfYourTurn":     return "[Start of Your Turn]";
                case "counter":             return "[Counter]";
                default:                    return timing ?? "Effect";
            }
        }

        private static bool HasTiming(string text, string timing)
        {
            return !string.IsNullOrWhiteSpace(text) && text.IndexOf("[" + timing + "]", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static EffectResolution TryResolveKnownEffect(GameState state, PendingEffect effect, string targetId)
        {
            var text = effect.Text ?? "";
            var owner = Player(state, effect.Seat);
            var sourceName = NameId(CardData.GetCard(effect.SourceCardId));

            if (ContainsAll(text, "K.O. up to 1", "opponent's rested Characters", "cost of 3 or less"))
            {
                var target = FindAnyInPlay(state, targetId, out var targetSeat);
                if (target == null)
                {
                    Log(state, effect.Seat, $"Choose an opponent's rested character with cost 3 or less for {sourceName}.");
                    return EffectResolution.WaitingForTarget;
                }
                if (targetSeat != OtherSeat(effect.Seat) || GetCard(target).Type != "character" || !target.Rested || GetCard(target).Cost > 3)
                {
                    Log(state, effect.Seat, "That is not a valid target for this K.O. effect.");
                    return EffectResolution.WaitingForTarget;
                }
                MoveToTrash(state, targetSeat, target.InstanceId);
                Log(state, effect.Seat, $"{sourceName} K.O.s {NameId(GetCard(target))}.");
                return EffectResolution.Resolved;
            }

            if (ContainsAll(text, "Set up to 1", "rested Characters with a cost of 5 or less as active"))
            {
                var target = FindAnyInPlay(state, targetId, out var targetSeat);
                if (target == null)
                {
                    Log(state, effect.Seat, $"Choose one of your rested characters with cost 5 or less for {sourceName}.");
                    return EffectResolution.WaitingForTarget;
                }
                var targetDef5 = GetCard(target);
                if (targetSeat != effect.Seat || targetDef5.Type != "character" || !target.Rested || targetDef5.Cost > 5)
                {
                    Log(state, effect.Seat, "That is not a valid target to set active.");
                    return EffectResolution.WaitingForTarget;
                }
                // Trafalgar Law requires {Supernovas} or {Heart Pirates} type.
                if (!CardPassesFeatureFilter(text, targetDef5))
                {
                    Log(state, effect.Seat, $"{NameId(targetDef5)} does not match the required type for {sourceName}.");
                    return EffectResolution.WaitingForTarget;
                }
                target.Rested = false;
                Log(state, effect.Seat, $"{sourceName} sets {NameId(targetDef5)} as active.");
                return EffectResolution.Resolved;
            }

            if (ContainsAll(text, "Give", "rested DON!!", "Leader"))
            {
                int giveCount = text.IndexOf("up to 2", StringComparison.OrdinalIgnoreCase) >= 0 ? 2 : 1;
                var target = FindAnyInPlay(state, targetId, out var targetSeat);
                if (target == null)
                {
                    Log(state, effect.Seat, $"Choose your leader or one of your characters for {sourceName}.");
                    return EffectResolution.WaitingForTarget;
                }
                var targetDef = GetCard(target);
                if (targetSeat != effect.Seat || (targetDef.Type != "leader" && targetDef.Type != "character"))
                {
                    Log(state, effect.Seat, "That is not a valid DON!! attachment target.");
                    return EffectResolution.WaitingForTarget;
                }
                var restedDon = owner.CostArea.Where(d => d.Rested).Take(giveCount).ToList();
                if (restedDon.Count == 0)
                {
                    Log(state, effect.Seat, "There are no rested DON!! cards to attach.");
                    return EffectResolution.Resolved;
                }
                var movingIds = new HashSet<string>(restedDon.Select(d => d.InstanceId));
                owner.CostArea = owner.CostArea.Where(d => !movingIds.Contains(d.InstanceId)).ToList();
                foreach (var don in restedDon) target.AttachedDonIds.Add(don.InstanceId);
                Log(state, effect.Seat, $"{sourceName} gives {restedDon.Count} rested DON!! to {NameId(targetDef)}.");
                return EffectResolution.Resolved;
            }

            if (ContainsAll(text, "K.O. up to 1", "opponent's Characters", "6000 power or less"))
            {
                var target = FindAnyInPlay(state, targetId, out var targetSeat);
                if (target == null)
                {
                    Log(state, effect.Seat, $"Choose an opponent character with 6000 power or less for {sourceName}.");
                    return EffectResolution.WaitingForTarget;
                }
                if (targetSeat != OtherSeat(effect.Seat) || GetCard(target).Type != "character" || GetPower(state, target) > 6000)
                {
                    Log(state, effect.Seat, "That is not a valid target for this K.O. effect.");
                    return EffectResolution.WaitingForTarget;
                }
                MoveToTrash(state, targetSeat, target.InstanceId);
                Log(state, effect.Seat, $"{sourceName} K.O.s {NameId(GetCard(target))}.");
                return EffectResolution.Resolved;
            }

            if (ContainsAll(text, "Rest up to 1", "opponent's Characters"))
            {
                var target = FindAnyInPlay(state, targetId, out var targetSeat);
                if (target == null)
                {
                    Log(state, effect.Seat, $"Choose an opponent's character to rest for {sourceName}.");
                    return EffectResolution.WaitingForTarget;
                }
                if (targetSeat != OtherSeat(effect.Seat) || GetCard(target).Type != "character")
                {
                    Log(state, effect.Seat, "That is not a valid target to rest.");
                    return EffectResolution.WaitingForTarget;
                }
                target.Rested = true;
                Log(state, effect.Seat, $"{sourceName} rests {NameId(GetCard(target))}.");
                return EffectResolution.Resolved;
            }

            // Power buff: "up to 1 of your Leader or Character gains +NNNN power during this turn/battle."
            // Handles any value; Thousand Sunny / Diable Jambe-adjacent effects enforce {feature} filter.
            if (ContainsAll(text, "gains +") && ContainsAll(text, " power") &&
                (ContainsAll(text, "Leader or Character") || ContainsAll(text, "Leader or 1 of your Character")))
            {
                int bonus = ParsePowerGain(text);
                if (bonus <= 0) return EffectResolution.NotAutomated;
                bool isBattle = ContainsAll(text, "during this battle");

                var target = FindAnyInPlay(state, targetId, out var targetSeat);
                if (target == null)
                {
                    Log(state, effect.Seat, $"Choose a Leader or Character for +{bonus} power ({sourceName}).");
                    return EffectResolution.WaitingForTarget;
                }
                var targetDef = GetCard(target);
                if (targetSeat != effect.Seat || (targetDef.Type != "leader" && targetDef.Type != "character"))
                {
                    Log(state, effect.Seat, "That is not a valid power-buff target.");
                    return EffectResolution.WaitingForTarget;
                }
                if (!CardPassesFeatureFilter(text, targetDef))
                {
                    Log(state, effect.Seat, $"{NameId(targetDef)} does not match the required type for {sourceName}.");
                    return EffectResolution.WaitingForTarget;
                }
                if (isBattle && state.Battle != null)
                {
                    state.Battle.BattlePowerBonus.TryGetValue(target.InstanceId, out var existing);
                    state.Battle.BattlePowerBonus[target.InstanceId] = existing + bonus;
                    Log(state, effect.Seat, $"{sourceName} gives {NameId(targetDef)} +{bonus} power this battle.");
                }
                else
                {
                    state.TemporaryPowerBonus.TryGetValue(target.InstanceId, out var existing);
                    state.TemporaryPowerBonus[target.InstanceId] = existing + bonus;
                    Log(state, effect.Seat, $"{sourceName} gives {NameId(targetDef)} +{bonus} power this turn.");
                }
                return EffectResolution.Resolved;
            }

            // Diable Jambe (Main): your opponent can't Blocker if the chosen {Straw Hat Crew}
            // card attacks this turn. Feature filter enforced on the target.
            if (ContainsAll(text, "cannot activate", "Blocker", "attacks during this turn"))
            {
                var target = FindAnyInPlay(state, targetId, out var targetSeat);
                if (target == null)
                {
                    Log(state, effect.Seat, $"Choose your {(text.Contains("{") ? "Straw Hat Crew " : "")}Leader or Character for {sourceName}.");
                    return EffectResolution.WaitingForTarget;
                }
                var targetDef = GetCard(target);
                if (targetSeat != effect.Seat || (targetDef.Type != "leader" && targetDef.Type != "character"))
                {
                    Log(state, effect.Seat, "That is not a valid target.");
                    return EffectResolution.WaitingForTarget;
                }
                if (!CardPassesFeatureFilter(text, targetDef))
                {
                    Log(state, effect.Seat, $"{NameId(targetDef)} does not match the required type for {sourceName}.");
                    return EffectResolution.WaitingForTarget;
                }
                state.NoBlockerGrantedThisTurn.Add(target.InstanceId);
                Log(state, effect.Seat, $"{sourceName} grants {NameId(targetDef)} a no-Blocker attack this turn.");
                return EffectResolution.Resolved;
            }

            // Leader Kid (ST02-001) Activate:Main: trash 1 from hand → set self active.
            // TargetZone is Hand, so the UI routes hand-card clicks here directly.
            if (ContainsAll(text, "trash 1 card from your hand", "Set this Leader as active"))
            {
                if (string.IsNullOrEmpty(targetId))
                {
                    Log(state, effect.Seat, $"Click a card in your hand to trash for {sourceName}.");
                    return EffectResolution.WaitingForTarget;
                }
                var p = Player(state, effect.Seat);
                int handIdx = p.Hand.FindIndex(c => c.InstanceId == targetId);
                if (handIdx < 0)
                {
                    Log(state, effect.Seat, "That card is not in your hand.");
                    return EffectResolution.WaitingForTarget;
                }
                var trashed = p.Hand[handIdx];
                p.Hand.RemoveAt(handIdx);
                trashed.Zone = "trash";
                p.Trash.Add(trashed);
                p.Leader.Rested = false;
                Log(state, effect.Seat, $"{sourceName} trashes {NameId(GetCard(trashed))} and sets itself as active.");
                return EffectResolution.Resolved;
            }

            // Straw Sword (ST02-017) Trigger: play up to 1 {Supernovas} card with cost ≤ 2 from hand for free.
            // TargetZone is Hand; the effect is optional ("up to 1").
            if (ContainsAll(text, "Play up to 1", "type card with a cost of 2 or less from your hand"))
            {
                if (string.IsNullOrEmpty(targetId))
                {
                    Log(state, effect.Seat, $"Click a Supernovas card (cost ≤ 2) in your hand for {sourceName}, or skip.");
                    return EffectResolution.WaitingForTarget;
                }
                var p = Player(state, effect.Seat);
                int handIdx = p.Hand.FindIndex(c => c.InstanceId == targetId);
                if (handIdx < 0) { Log(state, effect.Seat, "That card is not in your hand."); return EffectResolution.WaitingForTarget; }
                var handCard = p.Hand[handIdx];
                var handDef = GetCard(handCard);
                if (!CardPassesFeatureFilter(text, handDef) || handDef.Cost > 2)
                {
                    Log(state, effect.Seat, $"{NameId(handDef)} must be a Supernovas type card with cost 2 or less.");
                    return EffectResolution.WaitingForTarget;
                }
                // Play for free (no DON cost); characters go to a slot, others to trash.
                p.Hand.RemoveAt(handIdx);
                handCard.PlayedOnTurn = state.TurnNumber;
                if (handDef.Type == "character")
                {
                    int openSlot = p.CharacterArea.FindIndex(s => s == null);
                    if (openSlot < 0)
                    {
                        p.Hand.Insert(handIdx, handCard);
                        Log(state, effect.Seat, $"No open character slot — {NameId(handDef)} stays in hand.");
                        return EffectResolution.Resolved;
                    }
                    handCard.Zone = "character";
                    p.CharacterArea[openSlot] = handCard;
                    if (HasTiming(handDef.Effect, "On Play"))
                        QueueEffect(state, effect.Seat, handCard, "onPlay", handDef.Effect, true);
                }
                else
                {
                    handCard.Zone = "trash";
                    p.Trash.Add(handCard);
                }
                Log(state, effect.Seat, $"{sourceName} plays {NameId(handDef)} from hand for free.");
                return EffectResolution.Resolved;
            }

            // Diable Jambe (Trigger): K.O. up to 1 of the opponent's Blocker Characters, cost <= 3.
            if (ContainsAll(text, "K.O. up to 1", "Blocker", "cost of 3 or less"))
            {
                var target = FindAnyInPlay(state, targetId, out var targetSeat);
                if (target == null)
                {
                    Log(state, effect.Seat, $"Choose an opponent's Blocker character with cost 3 or less for {sourceName}.");
                    return EffectResolution.WaitingForTarget;
                }
                if (targetSeat != OtherSeat(effect.Seat) || !HasKeyword(target, "Blocker") || GetCard(target).Cost > 3)
                {
                    Log(state, effect.Seat, "That is not a valid target for this K.O. effect.");
                    return EffectResolution.WaitingForTarget;
                }
                MoveToTrash(state, targetSeat, target.InstanceId);
                Log(state, effect.Seat, $"{sourceName} K.O.s {NameId(GetCard(target))}.");
                return EffectResolution.Resolved;
            }

            // Scalpel (Trigger): set up to 2 of your DON!! cards as active. No target needed.
            if (ContainsAll(text, "Set up to 2", "DON!! cards as active"))
            {
                var restedDon = owner.CostArea.Where(d => d.Rested).Take(2).ToList();
                foreach (var d in restedDon) d.Rested = false;
                Log(state, effect.Seat, restedDon.Count > 0 ? $"{sourceName} sets {restedDon.Count} DON!! as active." : $"{sourceName} has no rested DON!! to set active.");
                return EffectResolution.Resolved;
            }

            // Draw effects: "Draw X card(s)." — no targeting required.
            if (ContainsAll(text, "Draw ") && ContainsAll(text, " card"))
            {
                int drawCount = 1;
                int di = text.IndexOf("Draw ", StringComparison.OrdinalIgnoreCase) + 5;
                if (di < text.Length)
                {
                    // Try to parse a digit directly ("Draw 1 card", "Draw 2 cards")
                    if (char.IsDigit(text[di]))
                    {
                        int de = di;
                        while (de < text.Length && char.IsDigit(text[de])) de++;
                        int.TryParse(text.Substring(di, de - di), out drawCount);
                    }
                }
                for (int i = 0; i < drawCount; i++) DrawCard(state, effect.Seat);
                Log(state, effect.Seat, $"{sourceName} draws {drawCount} card(s).");
                return EffectResolution.Resolved;
            }

            // Grant Rush to a Leader or Character this turn.
            if (ContainsAll(text, "gains [Rush]") && ContainsAll(text, "this turn"))
            {
                var target = FindAnyInPlay(state, targetId, out var targetSeat);
                if (target == null)
                {
                    Log(state, effect.Seat, $"Choose a Leader or Character to gain [Rush] for {sourceName}.");
                    return EffectResolution.WaitingForTarget;
                }
                var targetDef = GetCard(target);
                if (targetSeat != effect.Seat || (targetDef.Type != "leader" && targetDef.Type != "character"))
                {
                    Log(state, effect.Seat, "That is not a valid [Rush] grant target.");
                    return EffectResolution.WaitingForTarget;
                }
                if (!CardPassesFeatureFilter(text, targetDef))
                {
                    Log(state, effect.Seat, $"{NameId(targetDef)} does not match the required type for {sourceName}.");
                    return EffectResolution.WaitingForTarget;
                }
                AddModifier(state, FindAnyInPlay(state, effect.SourceInstanceId, out _), target, "keyword", "thisTurn", "Rush");
                Log(state, effect.Seat, $"{sourceName} gives {NameId(targetDef)} [Rush] this turn.");
                return EffectResolution.Resolved;
            }

            // Return to hand: "Return up to 1 of your opponent's Characters to their hand."
            if (ContainsAll(text, "Return") && ContainsAll(text, "to their hand") && ContainsAll(text, "opponent's"))
            {
                var target = FindAnyInPlay(state, targetId, out var targetSeat);
                if (target == null)
                {
                    Log(state, effect.Seat, $"Choose an opponent's card to return to hand for {sourceName}.");
                    return EffectResolution.WaitingForTarget;
                }
                var targetDef = GetCard(target);
                if (targetSeat != OtherSeat(effect.Seat) || targetDef.Type == "leader")
                {
                    Log(state, effect.Seat, "That is not a valid return-to-hand target.");
                    return EffectResolution.WaitingForTarget;
                }
                // Apply cost filter if present ("cost of X or less")
                if (ContainsAll(text, "cost of") && ContainsAll(text, "or less"))
                {
                    int costCap = ParseCostFilter(text);
                    if (costCap >= 0 && targetDef.Cost > costCap)
                    {
                        Log(state, effect.Seat, $"{NameId(targetDef)} costs too much for {sourceName}.");
                        return EffectResolution.WaitingForTarget;
                    }
                }
                ReturnToHand(state, targetSeat, target);
                Log(state, effect.Seat, $"{sourceName} returns {NameId(targetDef)} to hand.");
                return EffectResolution.Resolved;
            }

            // Return own card to hand: "Return up to 1 of your Characters/Cards to your hand."
            // The Swap pattern below also has "to your hand" + "from your hand"; exclude it here.
            if (ContainsAll(text, "Return") && ContainsAll(text, "to your hand") && ContainsAll(text, "your ")
                && !(ContainsAll(text, "Play") && ContainsAll(text, "from your hand")))
            {
                var target = FindAnyInPlay(state, targetId, out var targetSeat);
                if (target == null)
                {
                    Log(state, effect.Seat, $"Choose one of your cards to return to hand for {sourceName}.");
                    return EffectResolution.WaitingForTarget;
                }
                var targetDef = GetCard(target);
                if (targetSeat != effect.Seat || targetDef.Type == "leader")
                {
                    Log(state, effect.Seat, "That is not a valid return-to-hand target.");
                    return EffectResolution.WaitingForTarget;
                }
                ReturnToHand(state, effect.Seat, target);
                Log(state, effect.Seat, $"{sourceName} returns {NameId(targetDef)} to your hand.");
                return EffectResolution.Resolved;
            }

            // Set up to 1 DON!! as active (no-target; common counter side-effect).
            if (ContainsAll(text, "set up to 1", "DON!!", "as active"))
            {
                var restedDon = owner.CostArea.FirstOrDefault(d => d.Rested);
                if (restedDon != null)
                {
                    restedDon.Rested = false;
                    Log(state, effect.Seat, $"{sourceName} sets 1 DON!! as active.");
                }
                return EffectResolution.Resolved;
            }

            // "Choose one:" branch — must be checked before any other pattern because the full
            // text may embed sub-phrases that match other handlers (e.g. "Draw 1 card" inside option B).
            if (ContainsAll(text, "Choose one"))
            {
                if (TryParseChoiceEffect(state, effect)) return EffectResolution.Resolved;
            }

            // Look at top N cards of deck then optionally take one to hand.
            // (ActivateMain handles this for Activate: timing; here we cover On Play / Trigger.)
            if (ContainsAll(text, "Look at") && ContainsAll(text, "from the top of your deck") && ContainsAll(text, "to your hand"))
            {
                int lookN = ParseLookCount(text);
                if (lookN < 1) lookN = 1;
                var srcForLook = FindAnyInPlay(state, effect.SourceInstanceId, out _) ?? Player(state, effect.Seat).Leader;
                if (srcForLook != null) StartDeckLook(state, effect.Seat, srcForLook, ParseCurlyBraceTag(text), lookN);
                return EffectResolution.Resolved;
            }

            // Add top N cards of deck to top of Life.
            if ((ContainsAll(text, "Place the top") || ContainsAll(text, "place the top"))
                && ContainsAll(text, "of your deck") && ContainsAll(text, "Life"))
            {
                int addN = ParseTopN(text, "top");
                if (addN < 1) addN = 1;
                var p2 = Player(state, effect.Seat);
                int moved = 0;
                for (int i = 0; i < addN && p2.Deck.Count > 0; i++)
                {
                    var lifeCard = Shift(p2.Deck);
                    lifeCard.Zone = "life";
                    p2.Life.Insert(0, lifeCard);
                    moved++;
                }
                Log(state, effect.Seat, $"{sourceName} adds {moved} card(s) to the top of Life.");
                return EffectResolution.Resolved;
            }

            // Mill: trash the top N cards of your deck.
            if (ContainsAll(text, "trash the top") && ContainsAll(text, "card") && ContainsAll(text, "of your deck"))
            {
                int millN = ParseTopN(text, "top");
                if (millN < 1) millN = 1;
                var p3 = Player(state, effect.Seat);
                int milled = 0;
                for (int i = 0; i < millN && p3.Deck.Count > 0; i++)
                {
                    var millCard = Shift(p3.Deck);
                    millCard.Zone = "trash";
                    p3.Trash.Add(millCard);
                    milled++;
                }
                Log(state, effect.Seat, $"{sourceName} mills {milled} card(s) from the top of the deck.");
                return EffectResolution.Resolved;
            }

            // Play from trash: TargetZone must be Trash (set when queuing) so UI routes trash clicks here.
            if (ContainsAll(text, "Play up to 1") && ContainsAll(text, "from your trash") && effect.TargetZone == EffectTargetZone.Trash)
            {
                if (string.IsNullOrEmpty(targetId))
                {
                    Log(state, effect.Seat, $"Click a card in your trash to play for {sourceName}, or skip.");
                    return EffectResolution.WaitingForTarget;
                }
                var p4 = Player(state, effect.Seat);
                int trashIdx = p4.Trash.FindIndex(c => c.InstanceId == targetId);
                if (trashIdx < 0) { Log(state, effect.Seat, "That card is not in your trash."); return EffectResolution.WaitingForTarget; }
                var trashCard = p4.Trash[trashIdx];
                var trashDef = GetCard(trashCard);
                int costCap4 = ParseCostFilter(text);
                if (costCap4 >= 0 && trashDef.Cost > costCap4)
                {
                    Log(state, effect.Seat, $"{NameId(trashDef)} costs too much for {sourceName}.");
                    return EffectResolution.WaitingForTarget;
                }
                if (!CardPassesFeatureFilter(text, trashDef))
                {
                    Log(state, effect.Seat, $"{NameId(trashDef)} does not match the required type for {sourceName}.");
                    return EffectResolution.WaitingForTarget;
                }
                p4.Trash.RemoveAt(trashIdx);
                trashCard.PlayedOnTurn = state.TurnNumber;
                if (trashDef.Type == "character")
                {
                    int openSlot2 = p4.CharacterArea.FindIndex(s => s == null);
                    if (openSlot2 < 0)
                    {
                        p4.Trash.Insert(trashIdx, trashCard);
                        Log(state, effect.Seat, $"No open slot — {NameId(trashDef)} stays in trash.");
                        return EffectResolution.Resolved;
                    }
                    trashCard.Zone = "character";
                    p4.CharacterArea[openSlot2] = trashCard;
                    if (HasTiming(trashDef.Effect, "On Play"))
                        QueueEffect(state, effect.Seat, trashCard, "onPlay", trashDef.Effect, true);
                }
                else
                {
                    if (p4.Stage != null) MoveToTrash(state, effect.Seat, p4.Stage.InstanceId, true);
                    trashCard.Zone = "stage";
                    p4.Stage = trashCard;
                    if (HasTiming(trashDef.Effect, "On Play"))
                        QueueEffect(state, effect.Seat, trashCard, "onPlay", trashDef.Effect, true);
                }
                Log(state, effect.Seat, $"{sourceName} plays {NameId(trashDef)} from trash.");
                return EffectResolution.Resolved;
            }

            // Forced opponent discard: "Your opponent discards X card(s) from their hand."
            if (ContainsAll(text, "opponent") && ContainsAll(text, "discard") && ContainsAll(text, "from their hand"))
            {
                var discardMatch = System.Text.RegularExpressions.Regex.Match(
                    text, @"discards?\s+(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                int discardN = discardMatch.Success ? int.Parse(discardMatch.Groups[1].Value) : 1;
                string oppSeat = OtherSeat(effect.Seat);
                var oppP = Player(state, oppSeat);
                int actualDiscard = 0;
                for (int i = 0; i < discardN && oppP.Hand.Count > 0; i++)
                {
                    var dc = oppP.Hand[oppP.Hand.Count - 1];
                    oppP.Hand.RemoveAt(oppP.Hand.Count - 1);
                    dc.Zone = "trash";
                    oppP.Trash.Add(dc);
                    actualDiscard++;
                }
                Log(state, effect.Seat, $"{sourceName} forces opponent to discard {actualDiscard} card(s).");
                return EffectResolution.Resolved;
            }

            // Self discard/trash from hand — TargetZone.Hand guard prevents matching unrelated effects.
            if ((ContainsAll(text, "Discard") || ContainsAll(text, "discard") || ContainsAll(text, "trash"))
                && ContainsAll(text, "from your hand") && effect.TargetZone == EffectTargetZone.Hand
                && !ContainsAll(text, "Set this Leader as active"))
            {
                if (string.IsNullOrEmpty(targetId))
                {
                    Log(state, effect.Seat, $"Click a card in your hand to discard for {sourceName}.");
                    return EffectResolution.WaitingForTarget;
                }
                var p5 = Player(state, effect.Seat);
                int hIdx = p5.Hand.FindIndex(c => c.InstanceId == targetId);
                if (hIdx < 0) { Log(state, effect.Seat, "That card is not in your hand."); return EffectResolution.WaitingForTarget; }
                var discarded = p5.Hand[hIdx];
                p5.Hand.RemoveAt(hIdx);
                discarded.Zone = "trash";
                p5.Trash.Add(discarded);
                Log(state, effect.Seat, $"{sourceName}: you discard {NameId(GetCard(discarded))}.");
                return EffectResolution.Resolved;
            }

            // Grant keyword (Double Attack, Blocker, Unblockable) this turn.
            if ((ContainsAll(text, "gains [Double Attack]") || ContainsAll(text, "gains [Blocker]") || ContainsAll(text, "gains [Unblockable]"))
                && ContainsAll(text, "this turn"))
            {
                string kw = ContainsAll(text, "[Double Attack]") ? "Double Attack"
                           : ContainsAll(text, "[Blocker]") ? "Blocker" : "Unblockable";
                var target = FindAnyInPlay(state, targetId, out var targetSeat);
                if (target == null)
                {
                    Log(state, effect.Seat, $"Choose a Leader or Character to gain [{kw}] for {sourceName}.");
                    return EffectResolution.WaitingForTarget;
                }
                var targetDef = GetCard(target);
                if (targetSeat != effect.Seat || (targetDef.Type != "leader" && targetDef.Type != "character"))
                {
                    Log(state, effect.Seat, "That is not a valid keyword grant target.");
                    return EffectResolution.WaitingForTarget;
                }
                if (!CardPassesFeatureFilter(text, targetDef))
                {
                    Log(state, effect.Seat, $"{NameId(targetDef)} does not match the required type for {sourceName}.");
                    return EffectResolution.WaitingForTarget;
                }
                var src2 = FindAnyInPlay(state, effect.SourceInstanceId, out _);
                AddModifier(state, src2, target, "keyword", "thisTurn", kw);
                Log(state, effect.Seat, $"{sourceName} gives {NameId(targetDef)} [{kw}] this turn.");
                return EffectResolution.Resolved;
            }

            // "Cannot be K.O.'d during this turn" — applies to source card or explicit target.
            if (ContainsAll(text, "cannot be K.O.'d") && ContainsAll(text, "this turn"))
            {
                var selfCard = FindAnyInPlay(state, targetId, out var selfSeat2);
                if (selfCard == null) { selfCard = FindAnyInPlay(state, effect.SourceInstanceId, out selfSeat2); }
                if (selfCard == null || selfSeat2 != effect.Seat) return EffectResolution.NotAutomated;
                var srcCard2 = FindAnyInPlay(state, effect.SourceInstanceId, out _);
                AddModifier(state, srcCard2, selfCard, "cannotBeKod", "thisTurn", null);
                Log(state, effect.Seat, $"{sourceName}: {NameId(GetCard(selfCard))} cannot be K.O.'d this turn.");
                return EffectResolution.Resolved;
            }

            // Generic K.O. by power threshold (generalizes the hardcoded 6000 case above for other values).
            if (ContainsAll(text, "K.O. up to 1") && ContainsAll(text, "opponent") && ContainsAll(text, "power") && ContainsAll(text, "or less")
                && !ContainsAll(text, "rested") && !ContainsAll(text, "Blocker"))
            {
                var target = FindAnyInPlay(state, targetId, out var targetSeat);
                if (target == null)
                {
                    Log(state, effect.Seat, $"Choose an opponent's character to K.O. for {sourceName}.");
                    return EffectResolution.WaitingForTarget;
                }
                if (targetSeat != OtherSeat(effect.Seat) || GetCard(target).Type != "character")
                {
                    Log(state, effect.Seat, "That is not a valid K.O. target.");
                    return EffectResolution.WaitingForTarget;
                }
                var pwrMatch = System.Text.RegularExpressions.Regex.Match(
                    text, @"(\d{3,5})\s+power\s+or\s+less|power\s+of\s+(\d{3,5})\s+or\s+less",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                int pwrCap = -1;
                if (pwrMatch.Success)
                    int.TryParse(pwrMatch.Groups[1].Success ? pwrMatch.Groups[1].Value : pwrMatch.Groups[2].Value, out pwrCap);
                if (pwrCap > 0 && GetPower(state, target) > pwrCap)
                {
                    Log(state, effect.Seat, $"{NameId(GetCard(target))} is too powerful for {sourceName}.");
                    return EffectResolution.WaitingForTarget;
                }
                if (!HasModifier(state, target, "cannotBeKod"))
                    MoveToTrash(state, targetSeat, target.InstanceId);
                else
                    Log(state, effect.Seat, $"{NameId(GetCard(target))} cannot be K.O.'d.");
                Log(state, effect.Seat, $"{sourceName} K.O.s {NameId(GetCard(target))}.");
                return EffectResolution.Resolved;
            }

            // Rest one of your own characters (distinct from the existing "rest opponent's" pattern).
            if (ContainsAll(text, "Rest up to 1") && ContainsAll(text, "your") && !ContainsAll(text, "opponent"))
            {
                var target = FindAnyInPlay(state, targetId, out var targetSeat);
                if (target == null)
                {
                    Log(state, effect.Seat, $"Choose one of your characters to rest for {sourceName}.");
                    return EffectResolution.WaitingForTarget;
                }
                var targetDef = GetCard(target);
                if (targetSeat != effect.Seat || (targetDef.Type != "leader" && targetDef.Type != "character"))
                {
                    Log(state, effect.Seat, "That is not a valid target to rest.");
                    return EffectResolution.WaitingForTarget;
                }
                if (!CardPassesFeatureFilter(text, targetDef))
                {
                    Log(state, effect.Seat, $"{NameId(targetDef)} does not match the required type for {sourceName}.");
                    return EffectResolution.WaitingForTarget;
                }
                target.Rested = true;
                Log(state, effect.Seat, $"{sourceName} rests {NameId(targetDef)}.");
                return EffectResolution.Resolved;
            }

            // Generic "Set up to N DON!! as active" (catches any N not already matched by the specific-N checks above).
            if (ContainsAll(text, "Set up to") && ContainsAll(text, "DON!!") && ContainsAll(text, "as active"))
            {
                var nDonMatch = System.Text.RegularExpressions.Regex.Match(text, @"Set up to (\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                int nDon = nDonMatch.Success ? int.Parse(nDonMatch.Groups[1].Value) : 1;
                var restedDon2 = owner.CostArea.Where(d => d.Rested).Take(nDon).ToList();
                foreach (var d in restedDon2) d.Rested = false;
                Log(state, effect.Seat, restedDon2.Count > 0
                    ? $"{sourceName} sets {restedDon2.Count} DON!! as active."
                    : $"{sourceName} has no rested DON!! to set active.");
                return EffectResolution.Resolved;
            }

            // Name replacement: "This card's name is also treated as [Name]."
            // Stores the alternate name in state.NameOverrides; cleared when card leaves play.
            if (ContainsAll(text, "name is") && ContainsAll(text, "treated as"))
            {
                int asIdx = text.IndexOf("treated as", StringComparison.OrdinalIgnoreCase);
                string namePart = text.Substring(asIdx + 10).Trim().TrimEnd('.').Trim('"', ' ');
                if (!string.IsNullOrEmpty(namePart))
                {
                    var srcForName = FindAnyInPlay(state, effect.SourceInstanceId, out _);
                    if (srcForName != null)
                    {
                        state.NameOverrides[srcForName.InstanceId] = namePart;
                        Log(state, effect.Seat, $"{sourceName}'s name is also treated as {namePart}.");
                        return EffectResolution.Resolved;
                    }
                }
                return EffectResolution.NotAutomated;
            }

            // Swap: "Return 1 of your [X] Characters to your hand: Play 1 [Y] from your hand."
            // Step 1 — click own field card to return.  Step 2 — queued play-from-hand sub-effect.
            // Must be checked BEFORE the simple "Return … to your hand" pattern.
            if (ContainsAll(text, "Return") && ContainsAll(text, "to your hand")
                && ContainsAll(text, "Play") && ContainsAll(text, "from your hand"))
            {
                var swapTarget = FindAnyInPlay(state, targetId, out var swapSeat);
                if (swapTarget == null)
                {
                    Log(state, effect.Seat, $"Choose one of your characters to return to hand for {sourceName}.");
                    return EffectResolution.WaitingForTarget;
                }
                var swapDef = GetCard(swapTarget);
                if (swapSeat != effect.Seat || (swapDef.Type != "leader" && swapDef.Type != "character"))
                {
                    Log(state, effect.Seat, "That is not a valid target to return to hand.");
                    return EffectResolution.WaitingForTarget;
                }
                if (!CardPassesFeatureFilter(text, swapDef))
                {
                    Log(state, effect.Seat, $"{NameId(swapDef)} does not match the required type for {sourceName}.");
                    return EffectResolution.WaitingForTarget;
                }
                ReturnToHand(state, effect.Seat, swapTarget);
                Log(state, effect.Seat, $"{sourceName}: {NameId(swapDef)} returned to hand.");
                // Queue the play-from-hand sub-effect (the text after the colon).
                int colonPos = text.IndexOf(':');
                string playSubText = colonPos >= 0 ? text.Substring(colonPos + 1).Trim() : text;
                var swapSrc = FindAnyInPlay(state, effect.SourceInstanceId, out _) ?? Player(state, effect.Seat).Leader;
                if (swapSrc != null && !string.IsNullOrEmpty(playSubText))
                    QueueEffect(state, effect.Seat, swapSrc, effect.Timing, playSubText, true, EffectScope.Instant, EffectTargetZone.Hand);
                return EffectResolution.Resolved;
            }

            // Deck search: "Look at your entire deck / Search your deck and select 1 [X] ... add to hand. Shuffle."
            if ((ContainsAll(text, "Look at your entire deck") || ContainsAll(text, "Look at your deck")
                || ContainsAll(text, "Search your deck")) && ContainsAll(text, "hand"))
            {
                int maxCostSearch = ParseCostFilter(text);
                string featureSearch = ParseCurlyBraceTag(text);
                string cardTypeSearch = ContainsAll(text, "Character") ? "character"
                                      : ContainsAll(text, "Event") ? "event"
                                      : ContainsAll(text, "Stage") ? "stage" : "";
                var srcForSearch = FindAnyInPlay(state, effect.SourceInstanceId, out _) ?? Player(state, effect.Seat).Leader;
                if (srcForSearch != null)
                    StartDeckSearch(state, effect.Seat, srcForSearch, featureSearch, maxCostSearch, cardTypeSearch);
                return EffectResolution.Resolved;
            }

            // Add card from trash to hand (not playing to field — distinct from play-from-trash).
            // "Add 1 [Type] Character from your trash to your hand."
            // TargetZone.Trash is set by InferTargetZone in ResolveChoice / ActivateMain fallback.
            if ((ContainsAll(text, "Add") || ContainsAll(text, "add"))
                && ContainsAll(text, "from your trash") && ContainsAll(text, "to your hand")
                && effect.TargetZone == EffectTargetZone.Trash)
            {
                if (string.IsNullOrEmpty(targetId))
                {
                    Log(state, effect.Seat, $"Click a card in your trash to add to hand for {sourceName}.");
                    return EffectResolution.WaitingForTarget;
                }
                var p6 = Player(state, effect.Seat);
                int trashIdx2 = p6.Trash.FindIndex(c => c.InstanceId == targetId);
                if (trashIdx2 < 0) { Log(state, effect.Seat, "That card is not in your trash."); return EffectResolution.WaitingForTarget; }
                var addCard = p6.Trash[trashIdx2];
                var addDef = GetCard(addCard);
                if (!CardPassesFeatureFilter(text, addDef))
                {
                    Log(state, effect.Seat, $"{NameId(addDef)} does not match the required type for {sourceName}.");
                    return EffectResolution.WaitingForTarget;
                }
                int costCapAdd = ParseCostFilter(text);
                if (costCapAdd >= 0 && addDef.Cost > costCapAdd)
                {
                    Log(state, effect.Seat, $"{NameId(addDef)} costs too much for {sourceName}.");
                    return EffectResolution.WaitingForTarget;
                }
                p6.Trash.RemoveAt(trashIdx2);
                addCard.Zone = "hand";
                p6.Hand.Add(addCard);
                Log(state, effect.Seat, $"{sourceName} adds {NameId(addDef)} from trash to hand.");
                return EffectResolution.Resolved;
            }

            // Look at opponent's hand (information effect — log all card names).
            if (ContainsAll(text, "Look at your opponent's hand") || ContainsAll(text, "look at your opponent's hand"))
            {
                var opp3 = Player(state, OtherSeat(effect.Seat));
                string handList = opp3.Hand.Count > 0
                    ? string.Join(", ", opp3.Hand.Select(c => GetCard(c).Name))
                    : "(empty)";
                Log(state, effect.Seat, $"{sourceName}: opponent's hand ({opp3.Hand.Count}) — {handList}");
                return EffectResolution.Resolved;
            }

            // Conditional: "If [condition], [do effect]."
            // Evaluates the condition and, if met, queues the body for resolution.
            if (text.StartsWith("If ", StringComparison.OrdinalIgnoreCase))
            {
                int commaIdx = text.IndexOf(',');
                if (commaIdx > 3)
                {
                    string condPart = text.Substring(3, commaIdx - 3).Trim();
                    string bodyPart = text.Substring(commaIdx + 1).Trim();
                    bool condMet = EvaluateCondition(state, effect.Seat, condPart);
                    if (condMet && !string.IsNullOrEmpty(bodyPart))
                    {
                        var condSrc = FindCardInstance(state, effect.SourceInstanceId);
                        if (condSrc != null)
                        {
                            if (IsAutomatedEffectPattern(bodyPart))
                                QueueEffect(state, effect.Seat, condSrc, effect.Timing, bodyPart, false,
                                    effect.Scope, InferTargetZone(bodyPart));
                            else
                                Log(state, effect.Seat, $"{sourceName}: condition met — manual resolution needed: {bodyPart}");
                        }
                    }
                    else if (!condMet)
                    {
                        Log(state, effect.Seat, $"{sourceName}: condition not met, effect skipped.");
                    }
                    return EffectResolution.Resolved;
                }
            }

            // Multi-clause splitter: "Do X. Then, do Y." — process X; queue Y.
            // Applies after all single-clause patterns so it only fires when no single pattern matched.
            {
                int thenAt = FindThenClause(text);
                if (thenAt > 0)
                {
                    string partA = text.Substring(0, thenAt - 2).Trim(); // text before ". Then,"
                    string partB = text.Substring(thenAt).Trim();         // "Then, do Y."
                    var tempEffect = ShallowCloneEffect(effect, partA);
                    var partARes = TryResolveKnownEffect(state, tempEffect, targetId);
                    if (partARes == EffectResolution.WaitingForTarget) return EffectResolution.WaitingForTarget;
                    if (partARes == EffectResolution.Resolved)
                    {
                        var chainSrc = FindCardInstance(state, effect.SourceInstanceId);
                        if (chainSrc != null && IsAutomatedEffectPattern(partB))
                            QueueEffect(state, effect.Seat, chainSrc, effect.Timing, partB, false,
                                effect.Scope, InferTargetZone(partB));
                        else if (chainSrc != null)
                            Log(state, effect.Seat, $"{sourceName} secondary effect requires manual resolution: {partB}");
                        return EffectResolution.Resolved;
                    }
                    // partA is NotAutomated — fall through.
                }
            }

            return EffectResolution.NotAutomated;
        }

        private static bool TryResolveKnownTrigger(GameState state, string defenderSeat, CardInstance cardFromLife)
        {
            if (cardFromLife == null) return false;
            var def = GetCard(cardFromLife);
            var trigger = def.Trigger ?? "";

            if (trigger.IndexOf("Play this card", StringComparison.OrdinalIgnoreCase) >= 0 && def.Type == "character")
            {
                var defender = Player(state, defenderSeat);
                int openSlot = defender.CharacterArea.FindIndex(c => c == null);
                if (openSlot >= 0)
                {
                    cardFromLife.Zone = "character";
                    cardFromLife.PlayedOnTurn = state.TurnNumber;
                    defender.CharacterArea[openSlot] = cardFromLife;
                    Log(state, defenderSeat, $"{NameId(def)} Trigger plays this card to the field.");
                    state.Battle = null;
                    state.Phase = "main";
                    return true;
                }
                Log(state, defenderSeat, $"{NameId(def)} Trigger could not play because the character area is full.");
            }

            if (trigger.IndexOf("Activate this card's [Main] effect", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var pending = new PendingEffect
                {
                    EffectId = $"trigger-{++state.EffectSequence}",
                    Seat = defenderSeat,
                    SourceInstanceId = cardFromLife.InstanceId,
                    SourceCardId = cardFromLife.CardId,
                    Timing = "trigger",
                    Text = def.Effect,
                    Optional = true,
                };
                state.PendingEffects.Add(pending);
                cardFromLife.Zone = "trash";
                Player(state, defenderSeat).Trash.Add(cardFromLife);
                Log(state, defenderSeat, $"{NameId(def)} Trigger activates its Main effect.");
                state.Battle = null;
                state.Phase = "main";
                return true;
            }

            // Straw Sword (ST02-017): "Play up to 1 {Supernovas} type card with a cost of 2 or less
            // from your hand." The card goes to trash on use; the chosen hand card is played for free.
            // TargetZone.Hand is set so the UI routes hand clicks to resolveEffect.
            if (ContainsAll(trigger, "Play up to 1", "type card with a cost of 2 or less from your hand"))
            {
                var pending = new PendingEffect
                {
                    EffectId = $"trigger-{++state.EffectSequence}",
                    Seat = defenderSeat,
                    SourceInstanceId = cardFromLife.InstanceId,
                    SourceCardId = cardFromLife.CardId,
                    Timing = "trigger",
                    Text = trigger,
                    Optional = true,
                    Scope = EffectScope.Instant,
                    TargetZone = EffectTargetZone.Hand,
                };
                state.PendingEffects.Add(pending);
                cardFromLife.Zone = "trash";
                Player(state, defenderSeat).Trash.Add(cardFromLife);
                Log(state, defenderSeat, $"{NameId(def)} Trigger: play up to 1 Supernovas card (cost ≤ 2) from hand.");
                state.Battle = null;
                state.Phase = "main";
                return true;
            }

            // Generic: any other event whose [Trigger] text matches a known resolvable effect
            // (Diable Jambe's K.O., Guard Point's power buff, Scalpel's set-DON-active, etc.).
            // Using the trigger uses the event, so it goes to trash just like the [Main]-effect
            // case above, not to hand.
            if (def.Type == "event" && IsAutomatedEffectPattern(trigger))
            {
                var pending = new PendingEffect
                {
                    EffectId = $"trigger-{++state.EffectSequence}",
                    Seat = defenderSeat,
                    SourceInstanceId = cardFromLife.InstanceId,
                    SourceCardId = cardFromLife.CardId,
                    Timing = "trigger",
                    Text = trigger,
                    Optional = true,
                };
                state.PendingEffects.Add(pending);
                cardFromLife.Zone = "trash";
                Player(state, defenderSeat).Trash.Add(cardFromLife);
                state.Battle = null;
                state.Phase = "main";
                return true;
            }

            return false;
        }

        // Mirrors the phrase combinations TryResolveKnownEffect actually knows how to resolve,
        // so generic event-trigger queuing only fires for effects we can really automate.
        private static bool IsAutomatedEffectPattern(string text)
        {
            return ContainsAll(text, "K.O. up to 1", "opponent's rested Characters", "cost of 3 or less")
                || ContainsAll(text, "Set up to 1", "rested Characters with a cost of 5 or less as active")
                || ContainsAll(text, "Give", "rested DON!!", "Leader")
                || ContainsAll(text, "K.O. up to 1", "opponent's Characters", "6000 power or less")
                || ContainsAll(text, "Rest up to 1", "opponent's Characters")
                || (ContainsAll(text, "gains +") && ContainsAll(text, " power") && ContainsAll(text, "Leader or Character"))
                || ContainsAll(text, "K.O. up to 1", "Blocker", "cost of 3 or less")
                || ContainsAll(text, "Set up to 2", "DON!! cards as active")
                || ContainsAll(text, "set up to 1", "DON!!", "as active")
                || ContainsAll(text, "trash 1 card from your hand", "Set this Leader as active")
                || ContainsAll(text, "Play up to 1", "type card with a cost of 2 or less from your hand")
                || (ContainsAll(text, "Draw ") && ContainsAll(text, " card"))
                || ContainsAll(text, "gains [Rush]", "this turn")
                || (ContainsAll(text, "Return") && ContainsAll(text, "to their hand") && ContainsAll(text, "opponent's"))
                || (ContainsAll(text, "Return") && ContainsAll(text, "to your hand") && ContainsAll(text, "your ") && !(ContainsAll(text, "Play") && ContainsAll(text, "from your hand")))
                // Session 4 additions
                || ContainsAll(text, "Choose one")
                || (ContainsAll(text, "Look at") && ContainsAll(text, "from the top of your deck") && ContainsAll(text, "to your hand"))
                || ((ContainsAll(text, "Place the top") || ContainsAll(text, "place the top")) && ContainsAll(text, "of your deck") && ContainsAll(text, "Life"))
                || (ContainsAll(text, "trash the top") && ContainsAll(text, "card") && ContainsAll(text, "of your deck"))
                || (ContainsAll(text, "Play up to 1") && ContainsAll(text, "from your trash"))
                || (ContainsAll(text, "opponent") && ContainsAll(text, "discard") && ContainsAll(text, "from their hand"))
                || (ContainsAll(text, "gains [Double Attack]") && ContainsAll(text, "this turn"))
                || (ContainsAll(text, "gains [Blocker]") && ContainsAll(text, "this turn"))
                || (ContainsAll(text, "gains [Unblockable]") && ContainsAll(text, "this turn"))
                || (ContainsAll(text, "cannot be K.O.'d") && ContainsAll(text, "this turn"))
                || (ContainsAll(text, "K.O. up to 1") && ContainsAll(text, "opponent") && ContainsAll(text, "power") && ContainsAll(text, "or less"))
                || (ContainsAll(text, "Rest up to 1") && ContainsAll(text, "your") && !ContainsAll(text, "opponent"))
                || (ContainsAll(text, "Set up to") && ContainsAll(text, "DON!!") && ContainsAll(text, "as active"))
                // Session 5 additions
                || (ContainsAll(text, "name is") && ContainsAll(text, "treated as"))
                || (ContainsAll(text, "Return") && ContainsAll(text, "to your hand") && ContainsAll(text, "Play") && ContainsAll(text, "from your hand"))
                || ((ContainsAll(text, "Look at your entire deck") || ContainsAll(text, "Look at your deck") || ContainsAll(text, "Search your deck")) && ContainsAll(text, "hand"))
                // Session 6 additions
                || ((ContainsAll(text, "Add") || ContainsAll(text, "add")) && ContainsAll(text, "from your trash") && ContainsAll(text, "to your hand"))
                || ContainsAll(text, "Look at your opponent's hand")
                || text.StartsWith("If ", StringComparison.OrdinalIgnoreCase)
                || FindThenClause(text) > 0;
        }

        private static bool ContainsAll(string text, params string[] parts)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            return parts.All(part => text.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static CardInstance FindAnyInPlay(GameState state, string instanceId, out string seat)
        {
            seat = null;
            if (string.IsNullOrEmpty(instanceId)) return null;
            foreach (var candidateSeat in Seats())
            {
                var found = FindInPlay(Player(state, candidateSeat), instanceId);
                if (found == null) continue;
                seat = candidateSeat;
                return found;
            }
            return null;
        }

        private static void ClearBattle(GameState state)
        {
            if (state.Battle == null) return;
            string endedId = state.Battle.Id;
            Log(state, "system", "Attack cleared.");
            state.Battle = null;
            state.Phase = "main";
            CleanupBattleModifiers(state, endedId);
        }

        private static void MoveToTrash(GameState state, string seat, string instanceId, bool silent = false)
        {
            var p = Player(state, seat);
            var zones = new List<List<CardInstance>> { p.Hand, p.Life, p.Trash };
            foreach (var zone in zones)
            {
                int index = zone.FindIndex(c => c.InstanceId == instanceId);
                if (index >= 0)
                {
                    var card = zone[index];
                    zone.RemoveAt(index);
                    card.Zone = "trash";
                    p.Trash.Add(card);
                    if (!silent) Log(state, seat, $"{NameId(GetCard(card))} goes to trash.");
                    return;
                }
            }
            int charIndex = p.CharacterArea.FindIndex(c => c != null && c.InstanceId == instanceId);
            if (charIndex >= 0)
            {
                var card = p.CharacterArea[charIndex];
                p.CharacterArea[charIndex] = null;
                card.Zone = "trash";
                ReturnAttachedDon(p, card);
                p.Trash.Add(card);
                state.NameOverrides.Remove(card.InstanceId);
                if (!silent) Log(state, seat, $"{NameId(GetCard(card))} goes to trash.");
                FireOnKoEffects(state, seat, card);
            }
            if (p.Stage != null && p.Stage.InstanceId == instanceId)
            {
                var card = p.Stage;
                p.Stage = null;
                card.Zone = "trash";
                ReturnAttachedDon(p, card);
                p.Trash.Add(card);
                state.NameOverrides.Remove(card.InstanceId);
            }
        }

        // Return a card from the field to its owner's hand. Reattached DON!! returns to cost area.
        private static void ReturnToHand(GameState state, string seat, CardInstance card)
        {
            var p = Player(state, seat);
            ReturnAttachedDon(p, card);
            if (card.Zone == "character")
            {
                int idx = p.CharacterArea.IndexOf(card);
                if (idx >= 0) p.CharacterArea[idx] = null;
            }
            else if (p.Stage == card) p.Stage = null;
            card.Zone = "hand";
            card.PlayedOnTurn = null;
            p.Hand.Add(card);
            state.NameOverrides.Remove(card.InstanceId); // name overrides only apply while on field
        }

        // Parse "cost of N or less" from effect text. Returns -1 if not found.
        private static int ParseCostFilter(string text)
        {
            int idx = text.IndexOf("cost of ", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return -1;
            idx += 8;
            int end = idx;
            while (end < text.Length && char.IsDigit(text[end])) end++;
            if (end == idx || !int.TryParse(text.Substring(idx, end - idx), out int v)) return -1;
            return v;
        }

        // Fire [On KO] effects for a character being sent to trash from the field.
        private static void FireOnKoEffects(GameState state, string seat, CardInstance card)
        {
            var def = GetCard(card);
            if (!HasTiming(def.Effect, "On KO")) return;
            QueueEffect(state, seat, card, "onKo", def.Effect, true);
            Log(state, seat, $"{NameId(def)} [On KO] effect triggers.");
        }

        private static void EndTurn(GameState state, string seat)
        {
            if (!IsTurnPlayerInMain(state, seat)) return;

            // Finalize a deferred deck-out loss (e.g. Brook): their deck hit 0
            // sometime this turn but CheckRuleProcessing held off ending the
            // game until now — the end of the turn in which it happened.
            var endingPlayer = Player(state, seat);
            if (endingPlayer.Deck.Count == 0 && CardData.HasDeferredDeckOutRule(endingPlayer.Leader.CardId))
            {
                FinishGame(state);
                Log(state, "system", $"{Player(state, OtherSeat(seat)).Name} wins. {endingPlayer.Name} has no cards in deck at the end of their turn.");
                return;
            }

            state.Phase = "end";
            Log(state, seat, $"{Player(state, seat).Name} ends turn {state.TurnNumber}.");
            ApplyEndOfTurnEffects(state, seat);
            ApplyEndOfOpponentTurnEffects(state, OtherSeat(seat));
            state.Selected = null;
            state.Battle = null;
            state.ActiveSeat = OtherSeat(seat);
            state.TurnNumber += 1;
            ApplyStartOfTurn(state);
        }

        // Scan the non-active player's cards for [End of Opponent's Turn] effects,
        // which fire when THEIR opponent (the current active player) ends their turn.
        private static void ApplyEndOfOpponentTurnEffects(GameState state, string seat)
        {
            var p = Player(state, seat);
            var cards = new List<CardInstance>(p.CharacterArea.Where(c => c != null));
            if (p.Leader != null) cards.Add(p.Leader);
            foreach (var c in cards)
            {
                var def = GetCard(c);
                if (!HasTiming(def.Effect, "End of Opponent's Turn")) continue;
                int donReq = ParseDonThreshold(def.Effect);
                if (donReq > 0 && c.AttachedDonIds.Count < donReq) continue;
                if (ContainsAll(def.Effect, "Set this", "as active") && c.Rested)
                {
                    c.Rested = false;
                    Log(state, seat, $"{NameId(def)} [End of Opponent's Turn] sets itself as active.");
                    continue;
                }
                QueueEffect(state, seat, c, "endOfOpponentsTurn", def.Effect, true);
            }
        }

        // Scan all characters (and the leader) for [End of Your Turn] / [End of Opponent's Turn]
        // effects and fire any that can be automated. Also queues effects that need player choice.
        private static void ApplyEndOfTurnEffects(GameState state, string seat)
        {
            var p = Player(state, seat);
            // Collect cards to iterate (avoid mid-iteration mutation issues).
            var cards = new List<CardInstance>(p.CharacterArea.Where(c => c != null));
            if (p.Leader != null) cards.Add(p.Leader);

            foreach (var c in cards)
            {
                var def = GetCard(c);
                if (!HasTiming(def.Effect, "End of Your Turn")) continue;

                // DON!! threshold requirement (common pattern: "[DON!! x1] [End of Your Turn]").
                // Count DON from the card text; default 0 means no restriction.
                int donReq = ParseDonThreshold(def.Effect);
                if (donReq > 0 && c.AttachedDonIds.Count < donReq) continue;

                // "Set this Character as active." — automated.
                if (ContainsAll(def.Effect, "Set this", "as active") && c.Rested)
                {
                    c.Rested = false;
                    Log(state, seat, $"{NameId(def)} [End of Your Turn] sets itself as active.");
                    continue;
                }

                // Unknown [End of Your Turn] patterns — queue for manual resolution.
                QueueEffect(state, seat, c, "endOfYourTurn", def.Effect, true);
            }
        }

        // Parse "[DON!! xN]" DON requirement from effect text. Returns 0 if none found.
        private static int ParseDonThreshold(string text)
        {
            int idx = text.IndexOf("[DON!! x", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return 0;
            idx += 8; // skip "[DON!! x"
            int end = idx;
            while (end < text.Length && char.IsDigit(text[end])) end++;
            if (end == idx || !int.TryParse(text.Substring(idx, end - idx), out int v)) return 0;
            return v;
        }

        // ---- Queries ------------------------------------------------------

        private static bool IsTurnPlayerInMain(GameState state, string seat)
        {
            return state.Status == "active" && state.ActiveSeat == seat && state.Phase == "main" && state.Battle == null && state.PendingEffects.Count == 0 && state.DeckLook == null;
        }

        private static CardInstance FindInPlay(PlayerState p, string instanceId)
        {
            if (p.Leader.InstanceId == instanceId) return p.Leader;
            if (p.Stage != null && p.Stage.InstanceId == instanceId) return p.Stage;
            return p.CharacterArea.FirstOrDefault(c => c != null && c.InstanceId == instanceId);
        }

        private static bool HasKeyword(CardInstance instance, string keyword)
        {
            return GetCard(instance).Keywords.Contains(keyword);
        }

        // Shared "end the match right now" plumbing for every deck-out branch below.
        private static void FinishGame(GameState state)
        {
            state.Status = "finished";
            state.Phase = "finished";
            state.Battle = null;
            state.PendingEffects.Clear();
        }

        private static void CheckRuleProcessing(GameState state)
        {
            if (state.Status != "active") return;
            var deckedOut = Seats().Where(seat => Player(state, seat).Deck.Count == 0).ToList();
            if (deckedOut.Count == 0) return;

            // Leaders that win instead of losing on deck-out (e.g. Nami) take
            // priority over everything else — the instant their deck hits 0 they
            // win, full stop.
            var winsInstead = deckedOut.FirstOrDefault(seat => CardData.WinsOnDeckOut(Player(state, seat).Leader.CardId));
            if (winsInstead != null)
            {
                FinishGame(state);
                Log(state, "system", $"{Player(state, winsInstead).Name}'s deck reached 0 cards — they win instead of losing.");
                return;
            }

            // Leaders with a deferred deck-out rule (e.g. Brook) don't lose the
            // instant their deck hits 0 — EndTurn finalizes that loss instead.
            var defeated = deckedOut.Where(seat => !CardData.HasDeferredDeckOutRule(Player(state, seat).Leader.CardId)).ToList();
            if (defeated.Count == 0) return;
            FinishGame(state);
            if (defeated.Count == 2)
            {
                Log(state, "system", "Both players have no cards in deck. The game ends in simultaneous defeat.");
                return;
            }
            Log(state, "system", $"{Player(state, OtherSeat(defeated[0])).Name} wins. {Player(state, defeated[0]).Name} has no cards in deck.");
        }

        // ---- Deterministic RNG (ports the JS mulberry/FNV hash exactly) ---

        private static void ShuffleInPlace(List<CardInstance> cards, string seed)
        {
            var rng = new SeededRng(seed);
            for (int i = cards.Count - 1; i > 0; i--)
            {
                int j = (int)(rng.Next() * (i + 1));
                var tmp = cards[i];
                cards[i] = cards[j];
                cards[j] = tmp;
            }
        }

        private sealed class SeededRng
        {
            private uint _value;
            public SeededRng(string seed) { _value = HashString(seed); }
            public double Next()
            {
                _value = unchecked(_value + 0x6d2b79f5u);
                uint next = _value;
                next = unchecked((next ^ (next >> 15)) * (next | 1u));
                next ^= unchecked(next + (next ^ (next >> 7)) * (next | 61u));
                return (next ^ (next >> 14)) / 4294967296.0;
            }
        }

        private static uint HashString(string seed)
        {
            uint hash = 2166136261u;
            foreach (char ch in seed)
            {
                hash ^= (uint)ch;
                hash = unchecked(hash * 16777619u);
            }
            return hash;
        }

        // ---- Small list helpers (JS shift/pop) ----------------------------

        private static CardInstance Shift(List<CardInstance> list)
        {
            if (list.Count == 0) return null;
            var item = list[0];
            list.RemoveAt(0);
            return item;
        }

        private static CardInstance Pop(List<CardInstance> list)
        {
            if (list.Count == 0) return null;
            var item = list[list.Count - 1];
            list.RemoveAt(list.Count - 1);
            return item;
        }

        // ---- Infrastructure ----------------------------------------------

        private static PlayerState Player(GameState state, string seat) => state.Players[seat];
        private static string[] Seats() => new[] { "south", "north" };

        private static void Log(GameState state, string actor, string message)
        {
            state.LogSequence += 1;
            state.EventLog.Add(new LogEntry
            {
                Actor = actor,
                Message = message,
                Time = DateTime.Now.ToString("HH:mm:ss"),
                Turn = state.TurnNumber,
                Sequence = state.LogSequence
            });
        }

        private static void Record(GameState state, GameCommand command)
        {
            state.CommandHistory.Add(command);
        }
    }
}

