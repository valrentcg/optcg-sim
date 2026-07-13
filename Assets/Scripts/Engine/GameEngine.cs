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
            // Hand re-arranging is allowed during the mulligan overlay too (cosmetic only).
            if (state.Status != "active" && command.Type != "startGame"
                && !(state.Status == "mulligan" && command.Type == "reorderHand")) return state;

            switch (command.Type)
            {
                case "draw": ManualDrawCard(state, actor); break;
                case "drawDon": ManualDrawDon(state, actor, command.Amount ?? 2); break;
                case "reorderHand": ReorderHand(state, actor, command.InstanceId, command.SlotIndex ?? 0); break;
                case "reorderTrash": ReorderTrash(state, actor, command.InstanceId, command.SlotIndex ?? 0); break;
                case "sortTrash": SortTrash(state, actor, (command.Amount ?? 1) >= 0); break;
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
                case "resolveAttack": ResolveAttack(state, actor); break;
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
                case "deckLookScryConfirm": ResolveDeckLookScryConfirm(state, actor, command.OrderedInstanceIds); break;
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
            // Continuous base-power auras: "[Opponent's Turn] All of your [Name] cards' base
            // power and this Character's base power become N." (OP15-070/71).
            if (owner != null)
            {
                var bpaSrcs = new List<CardInstance>();
                if (owner.Leader != null) bpaSrcs.Add(owner.Leader);
                foreach (var c in owner.CharacterArea) if (c != null) bpaSrcs.Add(c);
                foreach (var bpaSrc in bpaSrcs)
                {
                    var bpaM = System.Text.RegularExpressions.Regex.Match(GetCard(bpaSrc)?.Effect ?? "",
                        @"All of your \[([^\]]+)\] cards' base power and this Character's base power become (\d{3,6})");
                    if (!bpaM.Success || IsEffectNegated(state, bpaSrc)) continue;
                    var bpaLine = (GetCard(bpaSrc)?.Effect ?? "").Split('\n')
                        .FirstOrDefault(l => l.Contains("base power become"));
                    bool bpaYour = bpaLine != null && HasTiming(bpaLine, "Your Turn");
                    bool bpaOpp = bpaLine != null && HasTiming(bpaLine, "Opponent's Turn");
                    bool bpaOwnTurn = state.ActiveSeat == instance.Owner;
                    if (bpaYour && !bpaOwnTurn) continue;
                    if (bpaOpp && bpaOwnTurn) continue;
                    if (instance.InstanceId == bpaSrc.InstanceId
                        || NameMatches(state, instance, bpaM.Groups[1].Value.Trim()))
                        power = int.Parse(bpaM.Groups[2].Value);
                }
            }
            // "Base power becomes N" overrides replace the PRINTED power (latest wins).
            for (int i = state.BasePowerOverrides.Count - 1; i >= 0; i--)
            {
                if (state.BasePowerOverrides[i].TargetInstanceId == instance.InstanceId)
                {
                    power = state.BasePowerOverrides[i].Value;
                    break;
                }
            }
            // Attached DON!! cards only grant their raw +1000 power during their controller's
            // turn. They stay attached until that player's refresh so [DON!! xN] requirements
            // can still see them, but they do not raise defense on the opponent's turn.
            if (owner != null && state.ActiveSeat == instance.Owner)
                power += instance.AttachedDonIds.Count * 1000;
            // Passive DON!! power bonuses: "[DON!! xN] This card gains +N power."
            power += GetPassiveDonPowerBonus(state, instance, owner);
            // Conditional printed self-penalty: "If <cond>, give this Leader/Character −N power."
            {
                var penM = System.Text.RegularExpressions.Regex.Match(GetCard(instance)?.Effect ?? "",
                    @"If ([^,]+), give this (?:Leader|Character) [-\u2212\u2013\u2011\u2012\u2014](\d{3,5}) power");
                if (penM.Success && owner != null && !IsEffectNegated(state, instance)
                    && EvaluateCondition(state, instance.Owner, penM.Groups[1].Value.Trim(), instance.InstanceId))
                    power -= int.Parse(penM.Groups[2].Value);
            }
            // EffectScope.Turn — cleared at the owning player's next Refresh Phase.
            if (state.TemporaryPowerBonus.TryGetValue(instance.InstanceId, out var temp)) power += temp;
            // EffectScope.Battle — cleared when the BattleState is discarded.
            if (state.Battle != null && state.Battle.BattlePowerBonus.TryGetValue(instance.InstanceId, out var battleBonus)) power += battleBonus;
            // [Your Turn] / [Opponent's Turn] passive auras from other cards.
            power += GetTurnPassiveAuraBonus(state, instance, owner);
            // "Until the start of your next turn" bonuses (outlive normal turn cleanup).
            foreach (var tb in state.TimedPowerBonuses)
                if (tb.TargetInstanceId == instance.InstanceId) power += tb.Delta;
            return power;
        }

        // ---- UI status badges ------------------------------------------------
        // One in-game status indicator on a card: short Label for the on-card chip,
        // longer Detail for the hover preview, Kind drives the chip color:
        // "buff" (green) | "debuff" (red) | "restrict" (amber) | "info" (blue).
        public sealed class StatusBadge
        {
            public string Label;
            public string Detail;
            public string Kind;
        }

        // Everything currently affecting this card that a player needs insight into:
        // net power change, cost change, base-power overrides, keyword grants (Rush,
        // Double Attack, Banish, ...), restrictions (can't attack, frozen, can't be
        // rested, ...), protections (can't be K.O.'d), and negated effects.
        public static List<StatusBadge> GetStatusBadges(GameState state, CardInstance instance)
        {
            var badges = new List<StatusBadge>();
            if (state == null || instance == null) return badges;
            if (instance.Zone != "character" && instance.Zone != "leader" && instance.Zone != "stage") return badges;
            var def = GetCard(instance);
            if (def == null) return badges;

            // Net power change vs the printed card, excluding the raw +1000s of attached
            // DON!! (those are already visible as the DON!! badge/row on the card).
            if (def.Type == "character" || def.Type == "leader")
            {
                var bo = state.BasePowerOverrides.FindLast(b => b.TargetInstanceId == instance.InstanceId);
                int baseline = bo != null ? bo.Value : def.Power;
                if (bo != null)
                    badges.Add(new StatusBadge
                    {
                        Label = "BASE " + bo.Value,
                        Detail = $"Base power is set to {bo.Value} (printed {def.Power}).",
                        Kind = bo.Value >= def.Power ? "buff" : "debuff",
                    });
                int eff = GetPower(state, instance);
                var ownerP = instance.Owner != null && state.Players.ContainsKey(instance.Owner) ? state.Players[instance.Owner] : null;
                int donRaw = (ownerP != null && state.ActiveSeat == instance.Owner) ? instance.AttachedDonIds.Count * 1000 : 0;
                int delta = eff - baseline - donRaw;
                if (delta != 0)
                {
                    // Name the sources when the per-card mirror list has them.
                    string srcs = null;
                    if (instance.Modifiers != null && instance.Modifiers.Count > 0)
                    {
                        var names = new List<string>();
                        foreach (var mm in instance.Modifiers)
                            if (mm.PowerDelta != 0 && !string.IsNullOrEmpty(mm.Source) && !names.Contains(mm.Source)) names.Add(mm.Source);
                        if (names.Count > 0) srcs = string.Join(", ", names.ToArray());
                    }
                    badges.Add(new StatusBadge
                    {
                        Label = (delta > 0 ? "+" : "−") + Math.Abs(delta) + " PWR",
                        Detail = $"Power {(delta > 0 ? "+" : "−")}{Math.Abs(delta)} from effects{(srcs != null ? " (" + srcs + ")" : "")}.",
                        Kind = delta > 0 ? "buff" : "debuff",
                    });
                }
            }

            // Cost change vs printed.
            {
                int effCost = GetCost(state, instance);
                if (effCost != def.Cost)
                {
                    int cd = effCost - def.Cost;
                    badges.Add(new StatusBadge
                    {
                        Label = (cd > 0 ? "+" : "−") + Math.Abs(cd) + " COST",
                        Detail = $"Cost is {effCost} (printed {def.Cost}).",
                        Kind = cd > 0 ? "buff" : "debuff",
                    });
                }
            }

            // Keyword grants and restriction/protection flags.
            foreach (var mod in state.ActiveModifiers)
            {
                if (mod.TargetInstanceId != instance.InstanceId) continue;
                string dur = mod.Duration == "thisTurn" ? "this turn"
                           : mod.Duration == "thisBattle" ? "this battle"
                           : mod.Duration == "untilNextTurn" ? "until the controller's next turn"
                           : "while in play";
                switch (mod.ModifierType)
                {
                    case "cannotAttack":
                        badges.Add(new StatusBadge { Label = "NO ATTACK", Detail = "Cannot attack (" + dur + ").", Kind = "restrict" });
                        break;
                    case "freeze":
                        badges.Add(new StatusBadge { Label = "FROZEN", Detail = "Does not become active at the Refresh Phase (" + dur + ").", Kind = "restrict" });
                        break;
                    case "cannotBeRested":
                        badges.Add(new StatusBadge { Label = "NO REST", Detail = "Cannot be rested by effects (" + dur + ").", Kind = "buff" });
                        break;
                    case "cannotBeKod":
                        badges.Add(new StatusBadge { Label = "K.O.-PROOF", Detail = "Cannot be K.O.'d (" + dur + ").", Kind = "buff" });
                        break;
                    case "canAttackActive":
                        badges.Add(new StatusBadge { Label = "ATK ACTIVE", Detail = "May attack your opponent's active Characters (" + dur + ").", Kind = "info" });
                        break;
                    case "doubleAttack":
                        badges.Add(new StatusBadge { Label = "2x ATTACK", Detail = "May attack twice per turn (" + dur + ").", Kind = "info" });
                        break;
                    case "effectsNegated":
                        badges.Add(new StatusBadge { Label = "NEGATED", Detail = "All of this card's effects are negated (" + dur + ").", Kind = "restrict" });
                        break;
                    case "onPlayNegated":
                        badges.Add(new StatusBadge { Label = "NO ON-PLAY", Detail = "Its [On Play] effect is negated (" + dur + ").", Kind = "restrict" });
                        break;
                    case "trashAtEndOfTurn":
                        badges.Add(new StatusBadge { Label = "TRASH @ END", Detail = "Trashed / returned at the end of the turn (" + dur + ").", Kind = "restrict" });
                        break;
                    case "noBlocker":
                        badges.Add(new StatusBadge { Label = "NO BLOCKER", Detail = "The opponent cannot activate [Blocker] against this attacker (" + dur + ").", Kind = "info" });
                        break;
                    case "keyword":
                        if (!string.IsNullOrEmpty(mod.Keyword))
                            badges.Add(new StatusBadge { Label = "+" + mod.Keyword.ToUpperInvariant(), Detail = "Gained [" + mod.Keyword + "] (" + dur + ").", Kind = "buff" });
                        break;
                }
            }

            // No-Blocker granted this turn (attacker-side turn flag).
            if (state.NoBlockerGrantedThisTurn.Contains(instance.InstanceId)
                && !badges.Exists(b => b.Label == "NO BLOCKER"))
                badges.Add(new StatusBadge { Label = "NO BLOCKER", Detail = "The opponent cannot activate [Blocker] against this attacker this turn.", Kind = "info" });

            // Alternate-name treatment.
            if (state.NameOverrides.TryGetValue(instance.InstanceId, out var aka) && !string.IsNullOrEmpty(aka))
                badges.Add(new StatusBadge { Label = "AKA " + aka.ToUpperInvariant(), Detail = "Also treated as [" + aka + "].", Kind = "info" });

            return badges;
        }

        // Passive DON!! bonus: "[DON!! xN] This Character gains +N power."
        // Generalizes Zoro (+1000 x1) and Urouge (+2000 x1 if 3+ chars) to all cards.
        // Skips any effect that has another timing keyword (When Attacking, On Play, etc.)
        // because those are conditional, not always-on.
        private static int GetPassiveDonPowerBonus(GameState state, CardInstance instance, PlayerState owner)
        {
            if (owner == null || IsEffectNegated(state, instance)) return 0;
            var text = GetCard(instance).Effect ?? "";
            if (!ContainsAll(text, "[DON!! x")) return 0;
            if (HasTiming(text, "When Attacking") || HasTiming(text, "On Play") ||
                HasTiming(text, "Activate: Main") || HasTiming(text, "End of Your Turn") ||
                HasTiming(text, "Your Turn") || HasTiming(text, "Opponent's Turn") ||
                HasTiming(text, "Once Per Turn") || HasTiming(text, "On KO")) return 0;
            int donReq = ParseDonThreshold(text);
            if (donReq <= 0 || instance.AttachedDonIds.Count < donReq) return 0;
            int bonus = ParsePowerGain(text);
            if (bonus <= 0)
            {
                var andPw = System.Text.RegularExpressions.Regex.Match(text, @"and \+(\d{3,5}) power\.");
                if (andPw.Success && !ContainsAll(text, "during this") && !ContainsAll(text, "until the"))
                    bonus = int.Parse(andPw.Groups[1].Value);
            }
            if (bonus <= 0) return 0;
            // Scaling passives: "+N power for every X / for each X".
            var forEvery = System.Text.RegularExpressions.Regex.Match(text,
                @"for (?:every|each)(?: (\d+))? (?:of your )?([^.]+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (forEvery.Success)
            {
                int per = forEvery.Groups[1].Success && forEvery.Groups[1].Value.Length > 0 ? int.Parse(forEvery.Groups[1].Value) : 1;
                string what = forEvery.Groups[2].Value;
                int count;
                if (ContainsAll(what, "rested DON")) count = RestedDonCount(owner);
                else if (ContainsAll(what, "card in your hand") || ContainsAll(what, "cards in your hand")) count = owner.Hand.Count;
                else if (ContainsAll(what, "Events in your trash")) count = owner.Trash.Count(c => GetCard(c).Type == "event");
                else if (ContainsAll(what, "cards in your trash")) count = owner.Trash.Count;
                else if (ContainsAll(what, "Characters with a different card name"))
                    count = owner.CharacterArea.Where(c => c != null).Select(c => GetEffectiveName(state, c)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                else if (ContainsAll(what, "type Characters"))
                {
                    string tagFe = ParseCurlyBraceTag(what);
                    count = owner.CharacterArea.Count(c => c != null && (string.IsNullOrEmpty(tagFe) || GetCard(c).HasFeature(tagFe)));
                }
                else return 0;
                return bonus * (count / Math.Max(1, per));
            }
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
                    if (IsEffectNegated(state, aura)) continue;
                    var fullText = GetCard(aura).Effect ?? "";
                    foreach (var aText in fullText.Split('\n'))
                    {
                        bool yourTurn = HasTiming(aText, "Your Turn");
                        bool oppTurn  = HasTiming(aText, "Opponent's Turn");
                        // Continuous board-wide auras: "All of your (…) Characters/cards … gain +N power."
                        // Active on BOTH turns when no turn tag is printed. Anything with an action
                        // timing ([On Play], [When Attacking], [Activate: Main], …) is NOT an aura.
                        bool boardWide = System.Text.RegularExpressions.Regex.IsMatch(aText,
                            @"All of your .*gain \+\d{3,5} power", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                            || System.Text.RegularExpressions.Regex.IsMatch(aText,
                            @"All of your .*and this Character gain \+?\d*", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        bool hasActionTiming = HasTiming(aText, "On Play") || HasTiming(aText, "When Attacking")
                            || HasTiming(aText, "Activate: Main") || HasTiming(aText, "Main") || HasTiming(aText, "Counter")
                            || HasTiming(aText, "Trigger") || HasTiming(aText, "On K.O.") || HasTiming(aText, "On KO")
                            || HasTiming(aText, "End of Your Turn") || HasTiming(aText, "On Block")
                            || ContainsAll(aText, "during this turn") || ContainsAll(aText, "until the");
                        if (hasActionTiming) continue;
                        if (!yourTurn && !oppTurn && !boardWide) continue;
                        // Self-buffs are handled by GetPassiveDonPowerBonus, not the aura scan —
                        // unless the aura line ALSO buffs the board ("All of your …").
                        if (aura.InstanceId == instance.InstanceId && !boardWide) continue;
                        if (yourTurn && !isActiveSeat) continue;
                        if (oppTurn  &&  isActiveSeat) continue;
                        int donReq = ParseDonThreshold(aText);
                        if (donReq > 0 && aura.AttachedDonIds.Count < donReq) continue;
                        // Rested condition.
                        if (ContainsAll(aText, "is rested") && !aura.Rested) continue;
                        // Leading "If …," condition on the aura line.
                        var condA = System.Text.RegularExpressions.Regex.Match(
                            System.Text.RegularExpressions.Regex.Replace(aText, @"^\s*(\[[^\]]+\]\s*/?\s*)+", ""),
                            @"^If ([^,]+),", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (condA.Success && !EvaluateCondition(state, candidateSeat, condA.Groups[1].Value.Trim(), aura.InstanceId)) continue;
                        int bonus = ParsePowerGain(aText);
                        if (bonus <= 0)
                        {
                            var gainPl = System.Text.RegularExpressions.Regex.Match(aText, @"gain \+(\d{3,5}) power");
                            if (gainPl.Success) bonus = int.Parse(gainPl.Groups[1].Value);
                        }
                        if (bonus <= 0) continue;
                        // Aura-side base power/cost filters on the RECIPIENT.
                        int bpCapA = ParseLimit(aText, @"(\d{3,5}) base power or less");
                        if (bpCapA >= 0 && instanceDef.Power > bpCapA) continue;
                        int bpMinA = ParseLimit(aText, @"(\d{3,5}) base power or more");
                        if (bpMinA >= 0 && instanceDef.Power < bpMinA) continue;
                        var bpEqA = System.Text.RegularExpressions.Regex.Match(aText, @"with (\d{3,5}) base power gain");
                        if (bpEqA.Success && instanceDef.Power != int.Parse(bpEqA.Groups[1].Value)) continue;
                        int bcMinA = ParseLimit(aText, @"base cost of (\d+) or more");
                        if (bcMinA >= 0 && instanceDef.Cost < bcMinA) continue;
                        var bcEqA = System.Text.RegularExpressions.Regex.Match(aText, @"with a base cost of (\d+) gain");
                        if (bcEqA.Success && instanceDef.Cost != int.Parse(bcEqA.Groups[1].Value)) continue;
                        int cMinA = ParseLimit(aText, @"cost of (\d+) or more");
                        if (cMinA >= 0 && GetCost(state, instance) < cMinA) continue;
                        int cMaxA = ParseLimit(aText, @"cost of (\d+) or less");
                        if (cMaxA >= 0 && GetCost(state, instance) > cMaxA) continue;
                        // Color filter ("All of your red Characters …").
                        bool colorOkA = true;
                        foreach (var col in new[] { "red", "green", "blue", "purple", "black", "yellow" })
                            if (System.Text.RegularExpressions.Regex.IsMatch(aText, $@"All of your {col}\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                                && (instanceDef.Color ?? "").IndexOf(col, StringComparison.OrdinalIgnoreCase) < 0)
                                colorOkA = false;
                        if (!colorOkA) continue;
                        if (ContainsAll(aText, "other than this Character") && aura.InstanceId == instance.InstanceId) continue;
                        // Named filter(s): "All of your [A] (and [B]) cards …" — match ANY name.
                        var nameFilts = System.Text.RegularExpressions.Regex.Matches(
                            System.Text.RegularExpressions.Regex.Match(aText, @"All of your [^.]*?(?:gain|cards)").Value,
                            @"\[([^\]]+)\]");
                        if (nameFilts.Count > 0 && aura.InstanceId != instance.InstanceId)
                        {
                            bool anyName = false;
                            foreach (System.Text.RegularExpressions.Match nf in nameFilts)
                                if (NameMatches(state, instance, nf.Groups[1].Value.Trim())) anyName = true;
                            if (!anyName) continue;
                        }
                        var inclFilt = System.Text.RegularExpressions.Regex.Match(aText, @"type including ""([^""]+)""");
                        if (inclFilt.Success && !instanceDef.HasFeature(inclFilt.Groups[1].Value.Trim())) continue;
                        if (!CardPassesFeatureFilter(aText, instanceDef)) continue;
                        total += bonus;
                    }
                }
            }
            return total;
        }

        public static int GetCounterPower(CardInstance instance) => AutomatedCounterPower(instance);

        /// <summary>
        /// Effective cost of a card: printed cost plus every CostDelta in the instance's
        /// Modifiers list (e.g. "-4 cost during this turn"), floored at 0. ALL engine cost
        /// checks (play cost, "cost of N or less" effect filters, IsValidEffectTarget) use
        /// this; the UI should render it instead of the printed cost when they differ.
        /// </summary>
        public static int GetCost(GameState state, CardInstance instance)
        {
            var def = GetCard(instance);
            int cost = def.Cost;
            if (instance?.Modifiers != null)
                foreach (var m in instance.Modifiers) cost += m.CostDelta;
            cost += GetPassiveCostBonus(state, instance);
            cost += GetPassiveCostAuraBonus(state, instance);
            return Math.Max(0, cost);
        }

        // Continuous cost AURAS: "All of your (black) ({T} type) Characters gain +N cost."
        // printed on any of the owner's cards in play (leader/characters/stage).
        private static int GetPassiveCostAuraBonus(GameState state, CardInstance instance)
        {
            if (instance == null || instance.Zone != "character") return 0;
            var ownerP = instance.Owner != null && state.Players.TryGetValue(instance.Owner, out var opc) ? opc : null;
            if (ownerP == null) return 0;
            int total = 0;
            var sources = new List<CardInstance>();
            if (ownerP.Leader != null) sources.Add(ownerP.Leader);
            foreach (var c in ownerP.CharacterArea) if (c != null) sources.Add(c);
            if (ownerP.Stage != null) sources.Add(ownerP.Stage);
            var instDef = GetCard(instance);
            foreach (var src in sources)
            {
                if (IsEffectNegated(state, src)) continue;
                var full = GetCard(src)?.Effect ?? "";
                if (full.IndexOf("cost", StringComparison.OrdinalIgnoreCase) < 0) continue;
                foreach (var line in full.Split('\n'))
                {
                    var m = System.Text.RegularExpressions.Regex.Match(line,
                        @"All of your ([^.]*?)Characters gain \+(\d+) cost",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (!m.Success) continue;
                    if (ContainsAll(line, "during this turn") || ContainsAll(line, "until the")) continue; // timed → modifier path
                    bool yourTurnC = HasTiming(line, "Your Turn");
                    bool oppTurnC = HasTiming(line, "Opponent's Turn");
                    bool isOwnTurn = state.ActiveSeat == instance.Owner;
                    if (yourTurnC && !isOwnTurn) continue;
                    if (oppTurnC && isOwnTurn) continue;
                    int donReqC = ParseDonThreshold(line);
                    if (donReqC > 0 && src.AttachedDonIds.Count < donReqC) continue;
                    string qual = m.Groups[1].Value;
                    bool colorOkC = true;
                    foreach (var col in new[] { "red", "green", "blue", "purple", "black", "yellow" })
                        if (qual.IndexOf(col, StringComparison.OrdinalIgnoreCase) >= 0
                            && (instDef.Color ?? "").IndexOf(col, StringComparison.OrdinalIgnoreCase) < 0)
                            colorOkC = false;
                    if (!colorOkC) continue;
                    if (!CardPassesFeatureFilter(qual, instDef)) continue;
                    int cMin = ParseLimit(line, @"cost of (\d+) or more");
                    if (cMin >= 0 && instDef.Cost < cMin) continue;
                    total += int.Parse(m.Groups[2].Value);
                }
            }
            return total;
        }

        // Passive self cost bonuses printed on the card itself, e.g. ST19-004 Hina:
        // "[DON!! x1] [Opponent's Turn] This Character gains +4 cost." Evaluated live
        // (not stored as a modifier) because the condition flips every turn change.
        // Scanned per line so a multi-ability card's other clauses don't contaminate the parse.
        private static int GetPassiveCostBonus(GameState state, CardInstance instance)
        {
            if (instance == null || (instance.Zone != "character" && instance.Zone != "leader" && instance.Zone != "stage")) return 0;
            var text = GetCard(instance)?.Effect ?? "";
            if (text.IndexOf("cost", StringComparison.OrdinalIgnoreCase) < 0) return 0;
            int total = 0;
            foreach (var line in text.Split('\n'))
            {
                var m = System.Text.RegularExpressions.Regex.Match(line,
                    @"[Tt]his (?:Character|card|Leader) gains \+(\d+) cost");
                if (!m.Success) continue;
                bool yourTurn = HasTiming(line, "Your Turn");
                bool oppTurn = HasTiming(line, "Opponent's Turn");
                bool isOwnersTurn = state.ActiveSeat == instance.Owner;
                if (yourTurn && !isOwnersTurn) continue;
                if (oppTurn && isOwnersTurn) continue;
                int donReq = ParseDonThreshold(line);
                if (donReq > 0 && instance.AttachedDonIds.Count < donReq) continue;
                total += int.Parse(m.Groups[1].Value);
            }
            return total;
        }

        // Register a power modifier chip on the card instance (UI display mirror of the
        // TemporaryPowerBonus/BattlePowerBonus dicts, which GetPower still reads).
        private static void RegisterPowerModifier(CardInstance target, string source, int delta, string expiresAt)
        {
            if (target == null || delta == 0) return;
            target.Modifiers.Add(new ActiveModifier { Source = source, PowerDelta = delta, CostDelta = 0, ExpiresAt = expiresAt });
        }

        // Register an authoritative cost modifier on the card instance (read by GetCost).
        private static void RegisterCostModifier(CardInstance target, string source, int delta, string expiresAt)
        {
            if (target == null || delta == 0) return;
            target.Modifiers.Add(new ActiveModifier { Source = source, PowerDelta = 0, CostDelta = delta, ExpiresAt = expiresAt });
        }

        // Remove every instance modifier with the given ExpiresAt from all cards in play.
        private static void ExpireInstanceModifiers(GameState state, string expiresAt)
        {
            foreach (var s in Seats())
            {
                var p = Player(state, s);
                CleanInstanceModifiers(p.Leader, expiresAt);
                CleanInstanceModifiers(p.Stage, expiresAt);
                foreach (var c in p.CharacterArea) CleanInstanceModifiers(c, expiresAt);
            }
        }

        private static void CleanInstanceModifiers(CardInstance c, string expiresAt)
        {
            if (c?.Modifiers == null || c.Modifiers.Count == 0) return;
            c.Modifiers.RemoveAll(m => m.ExpiresAt == expiresAt);
        }

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
            string modifierType, string duration, string keyword = null, string ownerSeat = null)
        {
            state.ActiveModifiers.Add(new CardModifier
            {
                SourceInstanceId = source?.InstanceId,
                TargetInstanceId = target?.InstanceId,
                ModifierType = modifierType,
                Keyword = keyword,
                Duration = duration,
                BattleId = duration == "thisBattle" ? state.Battle?.Id : null,
                OwnerSeat = ownerSeat,
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
            ExpireInstanceModifiers(state, "endOfBattle");
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

        // Always-on keyword grants: printed on the card itself ("[DON!! xN] This Character
        // gains [KW]", "[Your Turn] … gains [KW] if you have N or more cards in your hand",
        // "… cannot be rested … and gains [KW]") or granted by an aura on the owner's board
        // ("Your [Name] gains [KW]."). Temporary "during this turn" grants use CardModifiers.
        private static bool HasDonGatedKeyword(CardInstance instance, string keyword) =>
            HasPrintedKeywordGrant(null, instance, keyword);

        private static bool HasPrintedKeywordGrant(GameState state, CardInstance instance, string keyword)
        {
            if (instance == null) return false;
            if (state != null && IsEffectNegated(state, instance)) { /* own lines negated; auras below also skipped */ }
            var ownerP = state != null && instance.Owner != null && state.Players.TryGetValue(instance.Owner, out var okp) ? okp : null;
            var text = GetCard(instance)?.Effect ?? "";
            if ((state == null || !IsEffectNegated(state, instance))
                && text.IndexOf("gains [" + keyword + "]", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                foreach (var line in text.Split('\n'))
                {
                    if (line.IndexOf("gains [" + keyword + "]", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (ContainsAll(line, "during this") || ContainsAll(line, "until the")) continue;
                    // Turn-scoped continuous lines ("[Your Turn] / [Opponent's Turn] … gains [KW]").
                    if (state != null)
                    {
                        bool ytK = HasTiming(line, "Your Turn");
                        bool otK = HasTiming(line, "Opponent's Turn");
                        bool ownTurnK = state.ActiveSeat == instance.Owner;
                        if (ytK && !ownTurnK) continue;
                        if (otK && ownTurnK) continue;
                        // "… if you have N or more cards in your hand".
                        var handCond = System.Text.RegularExpressions.Regex.Match(line,
                            @"if you have (\d+) or more cards in your hand",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (handCond.Success && (ownerP == null || ownerP.Hand.Count < int.Parse(handCond.Groups[1].Value))) continue;
                        // Leading "If your Leader has the {T} type, this Character gains [KW]."
                        var leadCond = System.Text.RegularExpressions.Regex.Match(line.TrimStart(),
                            @"^If ([^,]+),", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (leadCond.Success && !EvaluateCondition(state, instance.Owner, leadCond.Groups[1].Value.Trim(), instance.InstanceId)) continue;
                    }
                    else if (line.TrimStart().StartsWith("If ", StringComparison.OrdinalIgnoreCase)) continue;
                    int req = ParseDonThreshold(line);
                    if (req > 0 && instance.AttachedDonIds.Count < req) continue;
                    if (req <= 0 && state == null) continue;   // state-less legacy path: DON-gated only
                    if (req <= 0 && !ContainsAll(line, "This Character") && !ContainsAll(line, "this Character")
                        && !ContainsAll(line, "and gains")) continue;
                    return true;
                }
            }
            // Keyword auras from the owner's other cards: "Your [Name] gains [KW]." /
            // "Your {T} type Characters gain [KW]."
            if (ownerP != null)
            {
                var auraSrcsK = new List<CardInstance>();
                if (ownerP.Leader != null) auraSrcsK.Add(ownerP.Leader);
                foreach (var c in ownerP.CharacterArea) if (c != null) auraSrcsK.Add(c);
                if (ownerP.Stage != null) auraSrcsK.Add(ownerP.Stage);
                var instDefK = GetCard(instance);
                foreach (var srcK in auraSrcsK)
                {
                    if (srcK.InstanceId == instance.InstanceId) continue;
                    if (IsEffectNegated(state, srcK)) continue;
                    var fK = GetCard(srcK)?.Effect ?? "";
                    foreach (System.Text.RegularExpressions.Match mK in System.Text.RegularExpressions.Regex.Matches(fK,
                        @"Your (\[(?<name>[^\]]+)\]|\{(?<tag>[^}]+)\} type Characters?) gains? \[" + keyword + @"\]"))
                    {
                        if (mK.Groups["name"].Success && !NameMatches(state, instance, mK.Groups["name"].Value.Trim())) continue;
                        if (mK.Groups["tag"].Success && !instDefK.HasFeature(mK.Groups["tag"].Value.Trim())) continue;
                        return true;
                    }
                }
            }
            return false;
        }

        // Sanji: [DON!! x2] this Character gains Rush.
        // State-aware overload also checks CardModifiers (Rush granted by an effect).
        public static bool HasRush(GameState state, CardInstance instance) =>
            instance != null && (HasKeyword(instance, "Rush")
                || (instance.CardId == "ST01-004" && instance.AttachedDonIds.Count >= 2)
                || HasPrintedKeywordGrant(state, instance, "Rush")
                || HasKeywordModifier(state, instance, "Rush"));

        // Backward-compatible overload (no modifier check) — kept for external callers.
        public static bool HasRush(CardInstance instance) =>
            instance != null && (HasKeyword(instance, "Rush") || (instance.CardId == "ST01-004" && instance.AttachedDonIds.Count >= 2));

        // Double Attack: can this card attack a second time this turn (while rested)?
        public static bool HasDoubleAttack(GameState state, CardInstance instance) =>
            instance != null && (HasKeyword(instance, "Double Attack")
                || HasPrintedKeywordGrant(state, instance, "Double Attack")
                || HasKeywordModifier(state, instance, "Double Attack")
                || HasModifier(state, instance, "doubleAttack"));

        // Banish: when this card deals damage to a leader, the life card is trashed instead of
        // going to hand and no Trigger step occurs.
        public static bool HasBanish(GameState state, CardInstance instance) =>
            instance != null && (HasKeyword(instance, "Banish")
                || HasPrintedKeywordGrant(state, instance, "Banish")
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

        // Extract N from "DON!! −N (You may return the specified number of DON!! cards from
        // your field to your DON!! deck.)" — a return-DON!!-to-deck cost (purple staple).
        // Accepts '-', '−' (minus sign) and '–' (en dash).
        private static int ParseDonMinusCost(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            var m = System.Text.RegularExpressions.Regex.Match(text, @"DON!!\s*[-\u2212\u2013\u2011\u2012\u2014]\s*(\d+)");
            return m.Success ? int.Parse(m.Groups[1].Value) : 0;
        }

        // Body of a "DON!! −N (…): <body>" effect — the text after the colon that follows the
        // reminder parenthetical. Falls back to the whole text when the shape doesn't match.
        private static string DonMinusBody(string text)
        {
            var m = System.Text.RegularExpressions.Regex.Match(text, @"DON!!\s*[-\u2212\u2013\u2011\u2012\u2014]\s*\d+\s*(?:\([^)]*\))?\s*[,:]?\s*");
            if (!m.Success) return text;
            var body = text.Substring(m.Index + m.Length).Trim();
            // Never return the unchanged text — ResolveEffect rewrites effect.Text to the body
            // after paying, and an unchanged text would double-charge on the next click.
            return string.IsNullOrEmpty(body) ? text : (text.Substring(0, m.Index) + body).Trim();
        }

        // Pays a DON!! -N cost immediately when the choice can't matter (all cost-area DON!!
        // share the same state, or paying empties the cost area anyway); otherwise flags the
        // effect as awaiting per-DON!! clicks and returns false (caller stops and waits).
        private static bool AutoPayOrAwaitDonMinus(GameState state, PendingEffect effect, int donMinus)
        {
            var payer = Player(state, effect.Seat);
            bool hasActive = payer.CostArea.Exists(d => !d.Rested);
            bool hasRested = payer.CostArea.Exists(d => d.Rested);
            if (hasActive && hasRested && payer.CostArea.Count > donMinus)
            {
                effect.DonPaymentRemaining = donMinus;
                Log(state, effect.Seat, $"Click {donMinus} of your DON!! to return for {NameId(CardData.GetCard(effect.SourceCardId))} (active or rested — your choice).");
                return false;
            }
            PayDonMinus(state, effect.Seat, donMinus);
            effect.Text = DonMinusBody(effect.Text);
            return true;
        }

        // Pay a DON!! −N cost: return N DON!! cards from the cost area (rested first, since
        // that's strictly best for the player) to the DON!! deck. Returns false if the player
        // doesn't have N DON!! on their field.
        private static bool PayDonMinus(GameState state, string seat, int amount)
        {
            var p = Player(state, seat);
            if (p.CostArea.Count < amount) return false;
            for (int i = 0; i < amount; i++)
            {
                int idx = p.CostArea.FindIndex(d => d.Rested);
                if (idx < 0) idx = p.CostArea.Count - 1;
                p.CostArea.RemoveAt(idx);
            }
            p.DonDeck += amount;
            Log(state, seat, $"{p.Name} returns {amount} DON!! to the DON!! deck.");
            NotifyDonReturned(state, seat);
            return true;
        }

        // Total DON!! on a player's field: cost area plus every attached DON!!.
        private static int TotalFieldDon(PlayerState p)
        {
            int total = p.CostArea.Count;
            if (p.Leader != null) total += p.Leader.AttachedDonIds.Count;
            foreach (var c in p.CharacterArea) if (c != null) total += c.AttachedDonIds.Count;
            if (p.Stage != null) total += p.Stage.AttachedDonIds.Count;
            return total;
        }

        // Strip a leading "Then," connective and re-capitalize a following "if" so downstream
        // clause handlers (which anchor on "If ") recognize queued second clauses.
        private static string NormalizeClause(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text ?? "";
            var t = text.Trim();
            if (t.StartsWith("Then,", StringComparison.OrdinalIgnoreCase)) t = t.Substring(5).Trim();
            else if (t.StartsWith("Then ", StringComparison.OrdinalIgnoreCase)) t = t.Substring(5).Trim();
            if (t.StartsWith("if ")) t = "If " + t.Substring(3);
            return t;
        }

        // Pull the single ability line containing "[timing]" out of a multi-line effect text
        // (e.g. Uta ST05-004's "[Blocker] …\n[On Block] DON!! −1 …"). Whole text if not found.
        private static string ExtractTimedClause(string text, string timing)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (!HasTiming(lines[i], timing)) continue;
                // Include following bullet lines ("Choose one:" / "Apply each …" blocks list
                // their options on subsequent lines starting with •, - or ‐).
                var sb = new System.Text.StringBuilder(lines[i].Trim());
                for (int j = i + 1; j < lines.Length; j++)
                {
                    var t2 = lines[j].TrimStart();
                    if (t2.Length > 0 && (t2[0] == '\u2022' || t2[0] == '-' || t2[0] == '\u2010'))
                        sb.Append('\n').Append(lines[j].Trim());
                    else break;
                }
                return sb.ToString();
            }
            return text;
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

        // Parses "[Name] or Event/Character/Stage card" (e.g. "reveal up to 1 [Sanji] or Event card")
        // into (name, type). Returns (null, null) if the text doesn't contain this pattern.
        private static (string name, string type) ParseNamedOrTypeFilter(string text)
        {
            var m = System.Text.RegularExpressions.Regex.Match(text,
                @"\[([^\]]+)\]\s+or\s+(Event|Character|Stage)\s+card",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!m.Success) return (null, null);
            return (m.Groups[1].Value.Trim(), m.Groups[2].Value.ToLowerInvariant());
        }

        // Enforce a "[Type] card" qualifier in an add/search clause (e.g. "black Character card
        // from your trash"). Only constrains when the text actually names a card type — clauses
        // that just say "1 card" impose no type restriction.
        private static bool AddClauseTypeMatches(string text, CardDef def)
        {
            if (def == null) return false;
            if (ContainsAll(text, "Character card") && def.Type != "character") return false;
            if (ContainsAll(text, "Event card") && def.Type != "event") return false;
            if (ContainsAll(text, "Stage card") && def.Type != "stage") return false;
            return true;
        }

        // Enforce a colour qualifier on the target of an add/search clause ("red Character card…").
        // Matches the colour immediately qualifying the Character/Event/Stage card phrase, so a
        // colour used elsewhere (a condition on the Leader, say) doesn't falsely constrain.
        private static bool AddClauseColorMatches(string text, CardDef def)
        {
            if (string.IsNullOrEmpty(text)) return true;
            var m = System.Text.RegularExpressions.Regex.Match(text,
                @"\b(red|green|blue|purple|black|yellow)\b[^.\n]*?\b(?:Character|Event|Stage) card",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!m.Success) return true;
            string col = m.Groups[1].Value;
            return (def?.Color ?? "").IndexOf(col, StringComparison.OrdinalIgnoreCase) >= 0;
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
            if (ActiveDonCount(p) < GetCost(state, instance)) return false;
            // A full board does NOT block a Character: the "6th character" rule lets you play it
            // and trash one of your own to make room (PlayCard handles the replaced slot via
            // SlotIndex). The UI must prompt for which Character to replace and send that slot.
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
            ExpireInstanceModifiers(state, "endOfTurn");
            state.NoBlockerGrantedThisTurn.Clear();
            state.AttackCountThisTurn.Clear();
            // CleanupTurnModifiers runs BEFORE unrest so "freeze" modifiers that last
            // only "thisTurn" correctly expire and let the card be refreshed normally.
            CleanupTurnModifiers(state);
            // "until the end of your opponent's next turn" restrictions expire when the
            // CONTROLLER's next turn begins (they last through the opponent's whole turn).
            state.ActiveModifiers.RemoveAll(m => m.Duration == "untilNextTurn" && m.OwnerSeat == seat);
            state.TimedPowerBonuses.RemoveAll(tb => tb.OwnerSeat == seat);
            ExpireInstanceModifiers(state, "untilNextTurnOf:" + seat);
            state.BasePowerOverrides.RemoveAll(bp =>
                (bp.Duration == "untilNextTurn" && bp.OwnerSeat == seat) || bp.Duration == "thisTurn");
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
                string soyClause = ExtractTimedClause(def.Effect, "Start of Your Turn");
                QueueAndAutoResolve(state, seat, c, "startOfYourTurn", soyClause, IsOptionalEffectText(soyClause),
                    EffectScope.Instant, InferTargetZone(soyClause));
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

        // Cosmetic: the owner may freely re-arrange or sort their trash pile (public zone,
        // no hidden information — order has no rules meaning, it's a browsing convenience).
        private static void ReorderTrash(GameState state, string seat, string instanceId, int targetIndex)
        {
            if (string.IsNullOrEmpty(seat) || !state.Players.ContainsKey(seat)) return;
            var p = Player(state, seat);
            int oldIndex = p.Trash.FindIndex(c => c.InstanceId == instanceId);
            if (oldIndex < 0) return;
            int insertIndex = Math.Max(0, Math.Min(targetIndex, p.Trash.Count));
            var card = p.Trash[oldIndex];
            p.Trash.RemoveAt(oldIndex);
            if (oldIndex < insertIndex) insertIndex -= 1;
            insertIndex = Math.Max(0, Math.Min(insertIndex, p.Trash.Count));
            p.Trash.Insert(insertIndex, card);
        }

        // lowestFirst refers to DISPLAY order (the trash viewer shows the END of the list
        // first), so "lowest cost at the top" stores the list in DESCENDING cost order.
        private static void SortTrash(GameState state, string seat, bool lowestFirst)
        {
            if (string.IsNullOrEmpty(seat) || !state.Players.ContainsKey(seat)) return;
            var p = Player(state, seat);
            var sorted = lowestFirst
                ? p.Trash.OrderByDescending(c => GetCard(c)?.Cost ?? 0)
                    .ThenByDescending(c => GetCard(c)?.Name ?? "", StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : p.Trash.OrderBy(c => GetCard(c)?.Cost ?? 0)
                    .ThenBy(c => GetCard(c)?.Name ?? "", StringComparer.OrdinalIgnoreCase)
                    .ToList();
            p.Trash.Clear();
            p.Trash.AddRange(sorted);
            Log(state, seat, $"{p.Name} sorts their trash by cost ({(lowestFirst ? "lowest" : "highest")} first).");
        }

        private static void PlayCard(GameState state, string seat, string instanceId, int? slotIndex)
        {
            if (!IsTurnPlayerInMain(state, seat)) return;
            var p = Player(state, seat);
            int index = p.Hand.FindIndex(c => c.InstanceId == instanceId);
            if (index < 0) return;
            var instance = p.Hand[index];
            var def = GetCard(instance);
            int playCost = GetCost(state, instance); // effective cost (printed + CostDelta modifiers)
            if (ActiveDonCount(p) < playCost) { Log(state, seat, $"Not enough active DON!! to play {NameId(def)}."); return; }
            PayDonCost(p, playCost);
            p.Hand.RemoveAt(index);
            instance.PlayedOnTurn = state.TurnNumber;

            if (def.Type == "character")
            {
                int openSlot = slotIndex ?? p.CharacterArea.FindIndex(e => e == null);
                bool boardFull = p.CharacterArea.All(e => e != null);
                if (openSlot < 0 || openSlot > 4)
                {
                    p.Hand.Add(instance);
                    RefundDonCost(p, playCost);
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
                        RefundDonCost(p, playCost);
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
                // OP06-044: "When your opponent activates an Event, your opponent must place
                // 1 card from their hand at the bottom of their deck." — printed on the
                // OPPONENT's board, taxing THIS player.
                if (BoardHasText(state, OtherSeat(seat), "When your opponent activates an Event") && p.Hand.Count > 0)
                {
                    var taxed = p.Hand[p.Hand.Count - 1];
                    p.Hand.RemoveAt(p.Hand.Count - 1);
                    taxed.Zone = "deck";
                    p.Deck.Add(taxed);
                    Log(state, seat, $"{p.Name} places 1 card from hand at the bottom of the deck (opponent's Event tax).");
                }
            }
            // "Your Character cards are played rested." (printed on one of the player's own cards).
            if (def.Type == "character")
            {
                var playedRestedSrcs = new List<CardInstance>();
                if (p.Leader != null) playedRestedSrcs.Add(p.Leader);
                foreach (var cc in p.CharacterArea) if (cc != null) playedRestedSrcs.Add(cc);
                foreach (var srcPR in playedRestedSrcs)
                    if (!IsEffectNegated(state, srcPR)
                        && ContainsAll(GetCard(srcPR)?.Effect ?? "", "Your Character cards are played rested"))
                    { instance.Rested = true; break; }
            }
            Log(state, seat, $"{p.Name} plays {NameId(def)}.");
            if (HasKeyword(instance, "Rush")) Log(state, seat, $"{NameId(def)} has [Rush] and can attack this turn.");
            // TargetZone is inferred from the effect text ("from your hand"/"from your trash")
            // so the UI routes hand/trash clicks to resolveEffect and highlights the right zone
            // (e.g. Robin's [On Play] "Play up to 1 ... from your hand" must offer HAND targets).
            if (def.Type == "event" && HasTiming(def.Effect, "Main"))
            {
                string mainCl = ExtractTimedClause(def.Effect, "Main");
                QueueAndAutoResolve(state, seat, instance, "main", mainCl, IsOptionalEffectText(mainCl), EffectScope.Instant, InferTargetZone(mainCl));
            }
            else if (HasTiming(def.Effect, "On Play"))
            {
                // [On Play] suppression: an "onPlayNegated" modifier (OP09-081 active effect),
                // the player's own "Your [On Play] effects are negated." drawback line on the
                // board, or full effect negation on the played card.
                bool onPlaySuppressed = HasModifier(state, instance, "onPlayNegated")
                    || IsEffectNegated(state, instance)
                    || BoardHasText(state, seat, "Your [On Play] effects are negated");
                if (onPlaySuppressed)
                {
                    Log(state, seat, $"{NameId(def)}'s [On Play] effect is negated.");
                }
                else
                {
                    string onPlayCl = ExtractTimedClause(def.Effect, "On Play");
                    QueueAndAutoResolve(state, seat, instance, "onPlay", onPlayCl, IsOptionalEffectText(onPlayCl), EffectScope.Instant, InferTargetZone(onPlayCl));
                }
            }
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
            if (IsEffectNegated(state, card))
            {
                Log(state, seat, $"{NameId(def)}'s effects are negated this turn.");
                return;
            }

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
                    // IMPORTANT: multi-ability cards (e.g. ST19-004 Hina: a [DON!! x1] passive
                    // line PLUS an [Activate: Main] line) must be parsed/queued from the
                    // Activate:Main CLAUSE only, or the passive line's [DON!! x1] tag would be
                    // misread as an activation requirement and the queued text would fail the
                    // cost-prefix resolver's anchoring.
                    string mainClause = ExtractTimedClause(def.Effect, "Activate: Main");
                    // [DON!! xN] requirement (e.g. OP02-093 Smoker leader's [DON!! x1][Activate: Main])
                    // gates activation on attached DON!!.
                    int donAttachedReq = ParseDonThreshold(mainClause);
                    if (donAttachedReq > 0 && card.AttachedDonIds.Count < donAttachedReq)
                    {
                        Log(state, seat, $"{NameId(def)} needs {donAttachedReq} DON!! attached to activate its ability.");
                        return;
                    }
                    // DON!! −N (return to DON!! deck) costs are paid in ResolveEffect when the
                    // player commits, but check affordability up front for a clear error.
                    int donMinusReq = ParseDonMinusCost(mainClause);
                    if (donMinusReq > 0 && p.CostArea.Count < donMinusReq)
                    {
                        Log(state, seat, $"Not enough DON!! on your field (need {donMinusReq}) to activate {NameId(def)}.");
                        return;
                    }
                    int genericCost = ParseActivateMainCost(mainClause);
                    if (genericCost > 0 && ActiveDonCount(p) < genericCost)
                    {
                        Log(state, seat, $"Not enough active DON!! (need {genericCost}) to activate {NameId(def)}.");
                        return;
                    }
                    if (genericCost > 0) PayDonCost(p, genericCost);
                    if (oncePerTurn) p.AbilityUsedThisTurn.Add(instanceId);
                    // Deck search: "Look at/Search your entire deck … add to hand … shuffle."
                    if ((ContainsAll(mainClause, "Look at your entire deck") || ContainsAll(mainClause, "Look at your deck")
                         || ContainsAll(mainClause, "Search your deck")) && ContainsAll(mainClause, "hand"))
                    {
                        int maxCostS = ParseCostFilter(mainClause);
                        string featureS = ParseCurlyBraceTag(mainClause);
                        string typeS = ContainsAll(mainClause, "Character") ? "character"
                                     : ContainsAll(mainClause, "Event") ? "event"
                                     : ContainsAll(mainClause, "Stage") ? "stage" : "";
                        StartDeckSearch(state, seat, card, featureS, maxCostS, typeS);
                    }
                    // Scry: "Look at N … place them at the top or bottom of the deck".
                    else if (ContainsAll(mainClause, "Look at", "from the top of your deck")
                             && (ContainsAll(mainClause, "top or bottom of the deck") || ContainsAll(mainClause, "top or bottom of your deck")))
                    {
                        StartDeckScry(state, seat, card, ParseLookCount(mainClause));
                    }
                    // Deck-look pattern: "Look at N cards from the top of your deck".
                    else if (ContainsAll(mainClause, "Look at", "from the top of your deck") && ContainsAll(mainClause, "to your hand"))
                    {
                        int lookN = ParseLookCount(mainClause);
                        var (namedFilter, typeFilter) = ParseNamedOrTypeFilter(mainClause);
                        string filter = namedFilter == null ? ParseCurlyBraceTag(mainClause) : null;
                        StartDeckLook(state, seat, card, filter, lookN, namedFilter, typeFilter,
                            ContainsAll(mainClause, "trash the rest"));
                    }
                    else
                    {
                        EffectTargetZone zone = InferTargetZone(mainClause);
                        QueueEffect(state, seat, card, "activateMain", mainClause, true, EffectScope.Instant, zone);
                    }
                    break;
            }
        }

        private static void StartDeckLook(GameState state, string seat, CardInstance source, string featureFilter, int count,
            string namedCardFilter = null, string cardTypeFilter = null, bool trashRest = false,
            bool playMode = false, bool playRested = false, int maxCost = -1, int maxPower = -1)
        {
            var p = Player(state, seat);
            var looked = new List<CardInstance>();
            for (int i = 0; i < count && p.Deck.Count > 0; i++) looked.Add(Shift(p.Deck));
            state.DeckLook = new DeckLookState
            {
                Seat = seat,
                SourceInstanceId = source.InstanceId,
                SourceName = GetCard(source).Name,
                TrashRest = trashRest,
                PlayMode = playMode,
                PlayRested = playRested,
                MaxCost = maxCost,
                MaxPower = maxPower,
                FeatureFilter = featureFilter,
                NamedCardFilter = namedCardFilter,
                CardTypeFilter = cardTypeFilter,
                Step = "select",
                Cards = looked,
            };
            Log(state, seat, $"{NameId(GetCard(source))} looks at the top {looked.Count} card(s) of the deck.");
        }

        // Full-deck search: move entire deck to DeckLookState.Cards (SearchMode = true).
        // Player picks 0 or 1 matching card; remaining cards are shuffled back into the deck.
        // featureFilter: type tag required (e.g. "Supernovas"), or "" for none.
        // maxCost: -1 = no cost limit; otherwise only cards with Cost <= maxCost qualify.
        // cardTypeFilter: "character" / "event" / "stage" / "" for any.
        // Scry: "Look at N cards from the top of your deck and place them at the top or bottom
        // of the deck in any order." (OP01-073 etc.) Step "scry": the player clicks the cards
        // to keep on TOP (in order); the rest go to the bottom. Confirmed via deckLookScryConfirm.
        private static void StartDeckScry(GameState state, string seat, CardInstance source, int count)
        {
            var p = Player(state, seat);
            var looked = new List<CardInstance>();
            for (int i = 0; i < count && p.Deck.Count > 0; i++) looked.Add(Shift(p.Deck));
            state.DeckLook = new DeckLookState
            {
                Seat = seat,
                SourceInstanceId = source.InstanceId,
                SourceName = GetCard(source).Name,
                FeatureFilter = "",
                Step = "scry",
                Cards = looked,
            };
            Log(state, seat, $"{NameId(GetCard(source))} looks at the top {looked.Count} card(s) of the deck (place at top or bottom).");
        }

        // topInstanceIds = the cards chosen to stay on TOP, first = topmost. Every other looked
        // card goes to the BOTTOM of the deck in its current display order.
        private static void ResolveDeckLookScryConfirm(GameState state, string seat, List<string> topInstanceIds)
        {
            var dl = state.DeckLook;
            if (dl == null || dl.Seat != seat || dl.Step != "scry") return;
            var p = Player(state, seat);
            var tops = new List<CardInstance>();
            if (topInstanceIds != null)
            {
                foreach (var id in topInstanceIds)
                {
                    var c = dl.Cards.Find(x => x.InstanceId == id);
                    if (c == null) return; // invalid selection — ignore the command
                    tops.Add(c);
                }
            }
            foreach (var c in tops) dl.Cards.Remove(c);
            // Insert tops so the FIRST selected ends up topmost (deck index 0 = top).
            for (int i = tops.Count - 1; i >= 0; i--)
            {
                tops[i].Zone = "deck";
                p.Deck.Insert(0, tops[i]);
            }
            foreach (var c in dl.Cards) { c.Zone = "deck"; p.Deck.Add(c); }
            Log(state, seat, $"{p.Name} places {tops.Count} card(s) on top and {dl.Cards.Count} at the bottom of the deck.");
            state.DeckLook = null;
        }

        private static void StartDeckSearch(GameState state, string seat, CardInstance source,
            string featureFilter, int maxCost, string cardTypeFilter,
            bool playMode = false, bool playRested = false, string namedCardFilter = null, int maxPower = -1)
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
                MaxPower = maxPower,
                CardTypeFilter = cardTypeFilter,
                PlayMode = playMode,
                PlayRested = playRested,
                NamedCardFilter = namedCardFilter,
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
                if (!string.IsNullOrEmpty(dl.NamedCardFilter))
                {
                    // "[Name] or Type card" effects (e.g. Charlotte Pudding: "[Sanji] or Event card") — OR, not AND.
                    bool nameMatch = NameMatches(state, dl.Cards[idx], dl.NamedCardFilter);
                    bool typeMatch = !string.IsNullOrEmpty(dl.CardTypeFilter) && def.Type.Equals(dl.CardTypeFilter, StringComparison.OrdinalIgnoreCase);
                    if (!nameMatch && !typeMatch)
                    {
                        Log(state, seat, $"{NameId(def)} is not [{dl.NamedCardFilter}] or a {dl.CardTypeFilter} card.");
                        return;
                    }
                }
                else
                {
                    // Feature filter
                    if (!string.IsNullOrEmpty(dl.FeatureFilter) && !FeatureMatches(def, dl.FeatureFilter))
                    {
                        Log(state, seat, $"{NameId(def)} does not match the required {{{dl.FeatureFilter}}} type.");
                        return;
                    }
                    if (dl.RequireTrigger && string.IsNullOrEmpty(def.Trigger))
                    {
                        Log(state, seat, $"{NameId(def)} has no [Trigger].");
                        return;
                    }
                    // Card type filter (search mode)
                    if (!string.IsNullOrEmpty(dl.CardTypeFilter) && !def.Type.Equals(dl.CardTypeFilter, StringComparison.OrdinalIgnoreCase))
                    {
                        Log(state, seat, $"{NameId(def)} is not a valid type for this search.");
                        return;
                    }
                }
                // Cost filter (search mode)
                if (dl.MaxCost >= 0 && def.Cost > dl.MaxCost)
                {
                    Log(state, seat, $"{NameId(def)} costs too much (max cost {dl.MaxCost}).");
                    return;
                }
                if (dl.MaxPower >= 0 && def.Power > dl.MaxPower)
                {
                    Log(state, seat, $"{NameId(def)} has too much power (max {dl.MaxPower}).");
                    return;
                }
                var taken = dl.Cards[idx];
                dl.Cards.RemoveAt(idx);
                if (dl.TrashSelected)
                {
                    taken.Zone = "trash";
                    p.Trash.Add(taken);
                    Log(state, seat, $"{p.Name} trashes {NameId(def)} from the look.");
                    dl.SelectCount--;
                    if (dl.SelectCount > 0 && dl.Cards.Count > 0) return;   // more picks allowed
                }
                else if (dl.PlayMode && def.Type == "character")
                {
                    int slotPM = p.CharacterArea.FindIndex(c => c == null);
                    if (slotPM < 0)
                    {
                        dl.Cards.Insert(Math.Min(idx, dl.Cards.Count), taken);
                        Log(state, seat, "No open character slot to play into.");
                        return;
                    }
                    taken.Zone = "character";
                    taken.PlayedOnTurn = state.TurnNumber;
                    taken.Rested = dl.PlayRested;
                    p.CharacterArea[slotPM] = taken;
                    Log(state, seat, $"{p.Name} plays {NameId(def)} from the deck{(dl.PlayRested ? " rested" : "")}.");
                    if (HasTiming(def.Effect, "On Play"))
                        QueueAndAutoResolve(state, seat, taken, "onPlay", ExtractTimedClause(def.Effect, "On Play"), true,
                            EffectScope.Instant, InferTargetZone(def.Effect));
                }
                else
                {
                    taken.Zone = "hand";
                    p.Hand.Add(taken);
                    Log(state, seat, $"{p.Name} adds {NameId(def)} to hand.");
                    dl.SelectCount--;
                    if (dl.SelectCount > 0 && !dl.SearchMode && dl.Cards.Count > 0) return;  // more picks
                }
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

            // "Then, trash the rest." variant (e.g. OP03-089 Brannew): no rearrange step —
            // every remaining card goes straight to the trash.
            if (dl.TrashRest)
            {
                foreach (var c in dl.Cards) { c.Zone = "trash"; p.Trash.Add(c); }
                Log(state, seat, $"{p.Name} trashes {dl.Cards.Count} card(s) from the look.");
                dl.Cards.Clear();
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
            if (dl.LifeMode)
            {
                // Arranged left→right = top→bottom of Life; the Life list stores TOP at the END.
                for (int i = dl.Ordered.Count - 1; i >= 0; i--)
                {
                    dl.Ordered[i].Zone = "life";
                    p.Life.Add(dl.Ordered[i]);
                }
                Log(state, seat, $"{p.Name} rearranges their Life cards.");
                state.DeckLook = null;
                return;
            }
            if (dl.ToTop)
            {
                // First in the arranged order ends up topmost.
                for (int i = dl.Ordered.Count - 1; i >= 0; i--)
                {
                    dl.Ordered[i].Zone = "deck";
                    p.Deck.Insert(0, dl.Ordered[i]);
                }
                Log(state, seat, $"{p.Name} places {dl.Ordered.Count} card(s) at the top of the deck.");
                state.DeckLook = null;
                return;
            }
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

            // A rested card cannot attack. ([Double Attack] does NOT grant a second attack — it
            // deals 2 Life damage in a single hit; see ResolveAttack's leader-damage branch.)
            if (attacker.Rested) return;

            if (HasModifier(state, attacker, "cannotAttack"))
            {
                Log(state, seat, $"{NameId(GetCard(attacker))} cannot attack this turn.");
                return;
            }
            // Printed self-restrictions: "This Leader/Character cannot attack." (OP03-058 etc.),
            // "This Character cannot attack a Leader on the turn in which it is played." (OP03-004),
            // "This Character cannot attack unless there is a Character with N base power or more."
            var attackerDefP = GetCard(attacker);
            if (!IsEffectNegated(state, attacker))
            {
                if ((ContainsAll(attackerDefP.Effect, "This Leader cannot attack.") && attackerDefP.Type == "leader")
                    || System.Text.RegularExpressions.Regex.IsMatch(attackerDefP.Effect ?? "",
                        @"(^|\n)\s*This Character cannot attack\.\s*($|\n)"))
                {
                    Log(state, seat, $"{NameId(attackerDefP)} cannot attack.");
                    return;
                }
                var unlessM = System.Text.RegularExpressions.Regex.Match(attackerDefP.Effect ?? "",
                    @"cannot attack unless there is a Character with (\d{3,5}) base power or more");
                if (unlessM.Success)
                {
                    int bpNeed = int.Parse(unlessM.Groups[1].Value);
                    bool found = false;
                    foreach (var st in Seats())
                        foreach (var cc in Player(state, st).CharacterArea)
                            if (cc != null && GetCard(cc).Power >= bpNeed) found = true;
                    if (!found)
                    {
                        Log(state, seat, $"{NameId(attackerDefP)} cannot attack — no Character with {bpNeed}+ base power in play.");
                        return;
                    }
                }
            }
            if (ContainsAll(attackerDefP.Effect, "cannot attack a Leader on the turn in which it is played")
                && attacker.PlayedOnTurn == state.TurnNumber
                && GetCard(FindInPlay(Player(state, OtherSeat(seat)), targetId))?.Type == "leader")
            {
                Log(state, seat, $"{NameId(attackerDefP)} cannot attack a Leader this turn.");
                return;
            }
            if (attacker.PlayedOnTurn == state.TurnNumber && !HasRush(state, attacker))
            {
                // "Rush vs Characters" — printed on the card, granted as a modifier, or from an
                // aura ("Your {T} type Characters can attack Characters on the turn in which
                // they are played."). Only lets it attack CHARACTERS the turn it came down.
                var rushCharTarget = FindInPlay(Player(state, OtherSeat(seat)), targetId);
                bool targetIsChar = rushCharTarget != null && GetCard(rushCharTarget).Type == "character";
                bool rushChars = ContainsAll(attackerDefP.Effect, "can attack Characters on the turn in which it is played")
                    || HasModifier(state, attacker, "rushCharacters");
                if (!rushChars)
                {
                    var pAura = Player(state, seat);
                    var auraSrcsR = new List<CardInstance>();
                    if (pAura.Leader != null) auraSrcsR.Add(pAura.Leader);
                    foreach (var cc in pAura.CharacterArea) if (cc != null) auraSrcsR.Add(cc);
                    foreach (var srcR in auraSrcsR)
                    {
                        var ft = GetCard(srcR)?.Effect ?? "";
                        if (IsEffectNegated(state, srcR)) continue;
                        if (ContainsAll(ft, "Characters can attack Characters on the turn in which they are played")
                            && CardPassesFeatureFilter(ft, attackerDefP)) { rushChars = true; break; }
                    }
                }
                if (!(rushChars && targetIsChar))
                {
                    Log(state, seat, "Only [Rush] characters can attack on the turn they are played.");
                    return;
                }
            }
            var defenderDef = GetCard(defender);
            // canAttackActive: this attacker may target active (non-rested) characters —
            // granted by effect, or printed ("[DON!! x1] This Character can also attack your
            // opponent's active Characters.", OP01-021 etc.).
            bool canHitActive = HasModifier(state, attacker, "canAttackActive") || HasPrintedCanAttackActive(attacker);
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
                // Every post-declaration decision (block, counter, final resolve, trigger)
                // belongs to the defender.
                PrioritySeat = OtherSeat(seat),
                TargetId = targetId,
                OriginalTargetId = targetId,
                Blocked = false,
                CounterPower = 0,
                AttackPower = GetPower(state, attacker),
                DefensePower = GetPower(state, defender),
            };
            Log(state, seat, $"{NameId(GetCard(attacker))} ({state.Battle.AttackPower}) attacks {NameId(GetCard(defender))} ({state.Battle.DefensePower}).");
            ApplyWhenAttackingEffects(state, seat, attacker, defenderDef);
            // [On Your Opponent's Attack] effects on the DEFENDER's board (e.g. OP11-041 Nami
            // leader: "[DON!! x1] [On Your Opponent's Attack] [Once Per Turn] You may trash 1
            // card from your hand: This Leader gains +2000 power during this turn."). Queued
            // optional; the pending-effect panel takes priority over the battle UI.
            {
                string defSeat = OtherSeat(seat);
                var dp = Player(state, defSeat);
                var reactors = new List<CardInstance>();
                if (dp.Leader != null) reactors.Add(dp.Leader);
                foreach (var c in dp.CharacterArea) if (c != null) reactors.Add(c);
                if (dp.Stage != null) reactors.Add(dp.Stage);
                foreach (var rc in reactors)
                {
                    var rDef = GetCard(rc);
                    if (IsEffectNegated(state, rc)) continue;
                    string oaClause = null;
                    if (HasTiming(rDef.Effect, "On Your Opponent's Attack"))
                        oaClause = ExtractTimedClause(rDef.Effect, "On Your Opponent's Attack");
                    else
                    {
                        // Older wording with no tag: "[Once Per Turn] This effect can be activated
                        // when your opponent attacks. <body>" (OP09-001 Shanks leader). Treat the
                        // <body> as the activatable effect.
                        foreach (var line in (rDef.Effect ?? "").Split('\n'))
                            if (line.IndexOf("can be activated when your opponent attacks", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                oaClause = System.Text.RegularExpressions.Regex.Replace(line,
                                    @"^.*?can be activated when your opponent attacks\.?\s*", "",
                                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                // Keep a [Once Per Turn]/[DON!! xN] prefix (stripped by the regex) for the gates below.
                                if (line.IndexOf("[Once Per Turn]", StringComparison.OrdinalIgnoreCase) >= 0) oaClause = "[Once Per Turn] " + oaClause;
                                break;
                            }
                    }
                    if (string.IsNullOrWhiteSpace(oaClause)) continue;
                    int oaDon = ParseDonThreshold(oaClause);
                    if (oaDon > 0 && rc.AttachedDonIds.Count < oaDon) continue;
                    bool oaOnce = oaClause.IndexOf("[Once Per Turn]", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (oaOnce && dp.AbilityUsedThisTurn.Contains(rc.InstanceId + ":onOppAttack")) continue;
                    if (oaOnce) dp.AbilityUsedThisTurn.Add(rc.InstanceId + ":onOppAttack");
                    QueueEffect(state, defSeat, rc, "onOpponentsAttack", oaClause, true,
                        EffectScope.Instant, InferTargetZone(oaClause));
                }
            }

            // No legal Blocker on the defending board → skip the block step entirely instead
            // of making the defender click "Pass Blockers" for nothing.
            MaybeAutoPassBlock(state);
        }

        // Advances block → counter automatically when no defending Character could legally
        // block: needs the [Blocker] keyword (printed or granted), must be active, not the
        // attack target, not negated, not power-banned, not un-restable (blocking rests it),
        // and the attacker must not be unblockable / NoBlocker-flagged.
        private static void MaybeAutoPassBlock(GameState state)
        {
            if (state.Battle == null || state.Battle.Step != "block") return;
            var defSeat = state.Battle.TargetSeat;
            var d = Player(state, defSeat);
            bool anyBlocker = false;
            if (!state.Battle.NoBlocker)
            {
                var atk = FindInPlay(Player(state, state.Battle.AttackerSeat), state.Battle.AttackerId);
                bool unblockable = atk != null && (HasKeyword(atk, "Unblockable") || HasKeywordModifier(state, atk, "Unblockable"));
                if (!unblockable)
                {
                    foreach (var c in d.CharacterArea)
                    {
                        if (c == null || c.Rested || c.InstanceId == state.Battle.TargetId) continue;
                        if (IsEffectNegated(state, c)) continue;
                        if (!HasKeyword(c, "Blocker") && !HasKeywordModifier(state, c, "Blocker") && !HasPrintedKeywordGrant(state, c, "Blocker")) continue;
                        if (state.Battle.BlockerPowerBan.HasValue && GetPower(state, c) >= state.Battle.BlockerPowerBan.Value) continue;
                        if (HasModifier(state, c, "cannotBeRested")) continue;   // blocking rests the blocker
                        anyBlocker = true;
                        break;
                    }
                }
            }
            if (!anyBlocker)
            {
                Log(state, defSeat, $"{d.Name} has no available Blocker — block step skipped.");
                state.Battle.Step = "counter";
            }
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
            if (HasTiming(atkDef.Effect, "When Attacking") && !IsEffectNegated(state, attacker))
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
                        string waClause = ExtractTimedClause(atkDef.Effect, "When Attacking");
                        QueueEffect(state, seat, attacker, "whenAttacking", waClause, IsOptionalEffectText(waClause),
                            EffectScope.Instant, InferTargetZone(waClause));
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
            if (blocker == null || blocker.Rested || IsEffectNegated(state, blocker)
                || (!HasKeyword(blocker, "Blocker") && !HasKeywordModifier(state, blocker, "Blocker") && !HasPrintedKeywordGrant(state, blocker, "Blocker"))) return;
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

            // [On Block] effects (e.g. ST05-004 Uta: "DON!! −1: Rest up to 1 of your opponent's
            // Characters with a cost of 5 or less."). Queued optional; any DON!! −N cost is paid
            // in ResolveEffect when the player commits. The pending-effect panel takes priority
            // over the battle UI, so it resolves before the counter step continues.
            var blockerDef2 = GetCard(blocker);
            if (HasTiming(blockerDef2.Effect, "On Block"))
            {
                string onBlockClause = ExtractTimedClause(blockerDef2.Effect, "On Block");
                QueueEffect(state, defenderSeat, blocker, "onBlock", onBlockClause, IsOptionalEffectText(onBlockClause),
                    EffectScope.Instant, InferTargetZone(onBlockClause));
            }

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
            // Counter auras printed on the defender's board (EB01-001 Oden, OP16-118):
            // "All of your {T} type Character cards without a Counter have a +N Counter" /
            // "The counter of all of your Character cards with N power in your hand becomes +N".
            {
                var auraSrcs = new List<CardInstance>();
                if (defender.Leader != null) auraSrcs.Add(defender.Leader);
                foreach (var c in defender.CharacterArea) if (c != null) auraSrcs.Add(c);
                foreach (var srcA in auraSrcs)
                {
                    if (IsEffectNegated(state, srcA)) continue;
                    var fA = GetCard(srcA)?.Effect ?? "";
                    var noCtr = System.Text.RegularExpressions.Regex.Match(fA,
                        @"without a Counter have a \+(\d{3,5}) Counter");
                    if (noCtr.Success && counterDef.Counter == 0 && counterDef.Type == "character"
                        && CardPassesFeatureFilter(fA, counterDef) && counterPower == 0)
                        counterPower = int.Parse(noCtr.Groups[1].Value);
                    var becomeCtr = System.Text.RegularExpressions.Regex.Match(fA,
                        @"counter of all of your Character cards with (\d{3,5}) power in your hand becomes \+(\d{3,5})",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (becomeCtr.Success && counterDef.Type == "character"
                        && counterDef.Power == int.Parse(becomeCtr.Groups[1].Value))
                        counterPower = int.Parse(becomeCtr.Groups[2].Value);
                }
            }
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

            // Generic secondary effect: anything after "Then," in the card's [Counter]
            // CLAUSE ONLY (e.g. "…+2000 power. Then, set 1 DON!! active"). Never read other
            // clauses — countering with a Character must NOT trigger its [On Play] text
            // (which frequently contains its own "Then," sentence).
            string effectText = ExtractTimedClause(counterDef.Effect ?? "", "Counter") ?? "";
            int thenIdx = effectText.IndexOf("Then,", StringComparison.OrdinalIgnoreCase);
            if (thenIdx < 0) thenIdx = effectText.IndexOf("Then ", StringComparison.OrdinalIgnoreCase);
            if (thenIdx >= 0)
            {
                string secondary = NormalizeClause(effectText.Substring(thenIdx).Trim());
                QueueEffect(state, defenderSeat, counterCard, "counter", secondary,
                    IsOptionalEffectText(secondary), EffectScope.Instant, InferTargetZone(secondary));
            }
        }

        private static int AutomatedCounterPower(CardInstance instance)
        {
            var def = GetCard(instance);
            // Characters/stages with a printed Counter value
            if (def.Counter > 0) return def.Counter;
            // A [Counter] ability lives in the effect TEXT — the JSON keywords array is usually
            // empty even for real counter events (e.g. OP07-116 Blaze Slice "[Main]/[Counter] …
            // gains +1000 power…"), which previously made them un-counterable. Detect the tag,
            // then read the boost from the [Counter] clause specifically.
            if (def.Type != "event" || !HasTiming(def.Effect, "Counter")) return 0;
            string counterClause = ExtractTimedClause(def.Effect ?? "", "Counter") ?? def.Effect ?? "";
            var m = System.Text.RegularExpressions.Regex.Match(
                counterClause, @"\+(\d{3,5})\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
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
                // ST13-003 rule: "Your face-up Life cards are placed at the bottom of your
                // deck instead of being added to your hand."
                bool faceUpToDeck = cardFromLife.FaceUp && BoardHasText(state, defenderSeat,
                    "face-up Life cards are placed at the bottom of your deck");
                if (faceUpToDeck)
                {
                    cardFromLife.Zone = "deck";
                    cardFromLife.FaceUp = false;
                    p.Deck.Add(cardFromLife);
                    Log(state, defenderSeat, $"{p.Name}'s face-up Life card goes to the bottom of the deck.");
                }
                else
                {
                    cardFromLife.Zone = "hand";
                    p.Hand.Add(cardFromLife);
                    Log(state, defenderSeat, $"{p.Name} takes 1 life to hand.");
                }
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
            if (cardFromLife != null)
            {
                cardFromLife.Zone = "hand";
                p.Hand.Add(cardFromLife);
            }
            // More life damage to deal this hit ([Double Attack])? Reveal the next card (which
            // finishes the game if there's no Life left) instead of ending the battle.
            if (state.Battle != null && state.Battle.PendingLifeDamage > 0)
            {
                state.Battle.PendingLifeDamage--;
                state.Battle.RevealedLife = null;
                RevealLifeAndStartTrigger(state, defenderSeat);
                return;
            }
            string endedBattleId = state.Battle?.Id;
            state.Battle = null;
            state.Phase = "main";
            CleanupBattleModifiers(state, endedBattleId);
        }

        // Banish N Life cards in one hit (no Trigger step); finishes the game if Life runs out.
        private static void BanishLifeCards(GameState state, string defenderSeat, int count)
        {
            var p = Player(state, defenderSeat);
            string endedBattleId = state.Battle?.Id;
            for (int i = 0; i < count; i++)
            {
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
            }
            state.Battle = null;
            state.Phase = "main";
            CleanupBattleModifiers(state, endedBattleId);
        }

        // "Your opponent's [Trigger] effects are negated." — printed on the trigger-user's
        // OPPONENT's board; the trigger fizzles (the life card goes to hand as normal).
        private static bool TriggersNegatedFor(GameState state, string seat) =>
            BoardHasText(state, OtherSeat(seat), "opponent's [Trigger] effects are negated");

        private static void UseTrigger(GameState state, string seat)
        {
            if (state.Battle == null || state.Battle.Step != "trigger") return;
            var defenderSeat = state.Battle.TargetSeat;
            if (seat != defenderSeat) return;
            var cardFromLife = state.Battle.RevealedLife;
            if (cardFromLife == null) { FinalizeTrigger(state, defenderSeat); return; }
            if (TriggersNegatedFor(state, defenderSeat))
            {
                Log(state, defenderSeat, $"{NameId(GetCard(cardFromLife))}'s [Trigger] is negated by the opponent.");
                FinalizeTrigger(state, defenderSeat);
                return;
            }
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

        private static void ResolveAttack(GameState state, string seat = null)
        {
            if (state.Battle == null) return;
            // The DEFENDER owns every decision after the attack is declared, including the
            // final resolve. A resolveAttack sent by the attacker's client is ignored.
            // Commands with no Seat (legacy logs / system) are still honored for replay compat.
            if (!string.IsNullOrEmpty(seat) && seat != state.Battle.TargetSeat) return;
            if (state.Battle.Step == "block") { PassBlock(state, state.Battle.TargetSeat); return; }
            if (state.Battle.Step == "counter") { PassCounter(state, state.Battle.TargetSeat); return; }
            if (state.Battle.Step != "damage") return;

            var targetSeat = state.Battle.TargetSeat;
            var target = FindInPlay(Player(state, targetSeat), state.Battle.TargetId);
            if (target == null)
            {
                // Battle fizzles (target already left play, e.g. bounced by a [When Attacking]
                // effect) — end the battle AND return to the main phase, else the turn player is
                // stranded in a battle phase with no battle (a hang). Expire this battle's
                // modifiers so "-N until end of battle" chips don't linger.
                var fizzledBattleId = state.Battle.Id;
                state.Battle = null;
                state.Phase = "main";
                CleanupBattleModifiers(state, fizzledBattleId);
                return;
            }

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
                    // [Double Attack] deals 2 damage to the Leader's Life in this ONE hit (the
                    // real rule) — NOT a second attack. At <2 Life the 2nd point still lands at 0
                    // Life and finishes the game.
                    int dmg = 1 + (atkCard != null && HasDoubleAttack(state, atkCard) ? 1 : 0);
                    if (atkCard != null && HasBanish(state, atkCard))
                    {
                        BanishLifeCards(state, targetSeat, dmg);
                    }
                    else
                    {
                        state.Battle.PendingLifeDamage = dmg - 1;   // the first is dealt now; rest chain via FinalizeTrigger
                        RevealLifeAndStartTrigger(state, targetSeat);
                    }
                }
                else
                {
                    string koedBattleId = state.Battle.Id;
                    if (HasModifier(state, target, "cannotBeKod") || IsBattleKoImmune(state, target, liveAttacker))
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
            var pbAttacker = FindInPlay(Player(state, state.Battle.AttackerSeat), state.Battle.AttackerId);
            string pbSeat = state.Battle.AttackerSeat;
            state.Battle = null;
            state.Phase = "main";
            CleanupBattleModifiers(state, endedId);
            ApplyEndOfBattleEffects(state, pbSeat, pbAttacker, target);
        }

        // "At the end of a battle in which this Character battles your opponent's Character
        // (with a cost of N or less)?, <body>" — OP04-047, ST08-013.
        private static void ApplyEndOfBattleEffects(GameState state, string attackerSeat, CardInstance attacker, CardInstance defender)
        {
            if (attacker == null || defender == null) return;
            if (GetCard(defender)?.Type != "character") return;
            var line = (GetCard(attacker)?.Effect ?? "").Split('\n')
                .FirstOrDefault(l => ContainsAll(l, "At the end of a battle in which this Character battles"));
            if (line == null || IsEffectNegated(state, attacker)) return;
            int donReqB = ParseDonThreshold(line);
            if (donReqB > 0 && attacker.AttachedDonIds.Count < donReqB) return;
            if (HasTiming(line, "Your Turn") && state.ActiveSeat != attackerSeat) return;
            int pbCap = ParseLimit(line, @"cost of (\d+) or less");
            if (pbCap >= 0 && GetCost(state, defender) > pbCap) return;
            if (FindInPlay(Player(state, OtherSeat(attackerSeat)), defender.InstanceId) == null) return;
            if (ContainsAll(line, "K.O. the opponent"))
            {
                if (!CannotBeKoedByEffect(state, defender) && !TryRemovalReplacement(state, OtherSeat(attackerSeat), defender))
                {
                    MoveToTrash(state, OtherSeat(attackerSeat), defender.InstanceId);
                    Log(state, attackerSeat, $"{NameId(GetCard(attacker))}: K.O.s {NameId(GetCard(defender))} at the end of the battle.");
                }
                if (ContainsAll(line, "If you do, K.O. this Character")
                    && FindInPlay(Player(state, attackerSeat), attacker.InstanceId) != null)
                    MoveToTrash(state, attackerSeat, attacker.InstanceId);
            }
            else if (ContainsAll(line, "place the opponent's Character you battled with at the bottom of the owner's deck"))
            {
                var pDef = Player(state, OtherSeat(attackerSeat));
                int di = pDef.CharacterArea.FindIndex(c => c != null && c.InstanceId == defender.InstanceId);
                if (di >= 0 && !TryRemovalReplacement(state, OtherSeat(attackerSeat), defender))
                {
                    ReturnAttachedDon(pDef, defender);
                    pDef.CharacterArea[di] = null;
                    defender.Zone = "deck"; defender.Rested = false; defender.PlayedOnTurn = null; defender.Modifiers.Clear();
                    state.NameOverrides.Remove(defender.InstanceId);
                    pDef.Deck.Add(defender);
                    Log(state, attackerSeat, $"{NameId(GetCard(attacker))}: {NameId(GetCard(defender))} is placed at the bottom of its owner's deck.");
                }
            }
        }

        // Printed "can also attack your opponent's active Characters" (with optional [DON!! xN]
        // gate), scanned per ability line so other clauses' DON!! tags don't interfere.
        private static bool HasPrintedCanAttackActive(CardInstance attacker)
        {
            var text = GetCard(attacker)?.Effect ?? "";
            if (!ContainsAll(text, "can also attack your opponent's active Characters")) return false;
            foreach (var line in text.Split('\n'))
            {
                if (!ContainsAll(line, "can also attack your opponent's active Characters")) continue;
                int donReq = ParseDonThreshold(line);
                if (donReq > 0 && attacker.AttachedDonIds.Count < donReq) continue;
                return true;
            }
            return false;
        }

        // K.O.-by-effect immunity: a cannotBeKod modifier OR printed immunity text OR a
        // protective aura on the owner's board ("All of your Characters with N base power or
        // less cannot be K.O.'d by your opponent's effects…", conditional variants, etc.).
        private static bool CannotBeKoedByEffect(GameState state, CardInstance c)
        {
            if (c == null) return false;
            if (HasModifier(state, c, "cannotBeKod")) return true;
            var own = GetCard(c)?.Effect ?? "";
            if (ContainsAll(own, "cannot be K.O.'d by effects")
                || ContainsAll(own, "cannot be K.O.'d by your opponent's effects")
                || ContainsAll(own, "cannot be removed from the field by your opponent's effects"))
            {
                // "Once per turn, this Character cannot be K.O.'d…" — consume a per-turn charge.
                if (ContainsAll(own, "Once per turn"))
                {
                    var pOnce = c.Owner != null && state.Players.TryGetValue(c.Owner, out var po) ? po : null;
                    if (pOnce == null) return false;
                    if (pOnce.AbilityUsedThisTurn.Contains(c.InstanceId + ":koImmunity")) return false;
                    pOnce.AbilityUsedThisTurn.Add(c.InstanceId + ":koImmunity");
                }
                return true;
            }
            return HasRemovalImmunityAura(state, c);
        }

        // Protective auras on the victim's own board: "All of your (yellow) ({T} type)
        // Characters (with N base power or less) cannot be K.O.'d / removed from the field by
        // your opponent's effects (until …)", with an optional leading "If …" condition.
        private static bool HasRemovalImmunityAura(GameState state, CardInstance victim)
        {
            if (victim == null || victim.Owner == null || !state.Players.TryGetValue(victim.Owner, out var p)) return false;
            var vDef = GetCard(victim);
            var srcs = new List<CardInstance>();
            if (p.Leader != null) srcs.Add(p.Leader);
            foreach (var c in p.CharacterArea) if (c != null) srcs.Add(c);
            foreach (var src in srcs)
            {
                if (IsEffectNegated(state, src)) continue;
                var full = GetCard(src)?.Effect ?? "";
                foreach (var line in full.Split('\n'))
                {
                    if (!System.Text.RegularExpressions.Regex.IsMatch(line,
                            @"all of your .*cannot be (K\.O\.'d|removed from the field) by your opponent's effects",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase)) continue;
                    var condI = System.Text.RegularExpressions.Regex.Match(line,
                        @"^\s*If ([^,]+),", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (condI.Success && !EvaluateCondition(state, victim.Owner, condI.Groups[1].Value.Trim(), src.InstanceId)) continue;
                    int bpCapI = ParseLimit(line, @"(\d{3,5}) base power or less");
                    if (bpCapI >= 0 && vDef.Power > bpCapI) continue;
                    bool colorOkI = true;
                    foreach (var col in new[] { "red", "green", "blue", "purple", "black", "yellow" })
                        if (System.Text.RegularExpressions.Regex.IsMatch(line, $@"all of your {col}\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                            && (vDef.Color ?? "").IndexOf(col, StringComparison.OrdinalIgnoreCase) < 0)
                            colorOkI = false;
                    if (!colorOkI) continue;
                    if (!CardPassesFeatureFilter(line, vDef)) continue;
                    return true;
                }
            }
            return false;
        }

        // Effective-name match including printed aliases ("Also treat this card's name as
        // [X] (and [Y]) according to the rules." — static, unlike runtime NameOverrides).
        public static bool NameMatches(GameState state, CardInstance card, string name)
        {
            if (card == null || string.IsNullOrEmpty(name)) return false;
            if (string.Equals(GetEffectiveName(state, card), name, StringComparison.OrdinalIgnoreCase)) return true;
            var text = GetCard(card)?.Effect ?? "";
            if (text.IndexOf("treat this card's name as", StringComparison.OrdinalIgnoreCase) < 0) return false;
            foreach (System.Text.RegularExpressions.Match am in
                System.Text.RegularExpressions.Regex.Matches(text, @"treat this card's name as \[([^\]]+)\](?: and \[([^\]]+)\])?"))
            {
                if (string.Equals(am.Groups[1].Value.Trim(), name, StringComparison.OrdinalIgnoreCase)) return true;
                if (am.Groups[2].Success && string.Equals(am.Groups[2].Value.Trim(), name, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        // Dynamic numeric caps: "…equal to or less than the number of your opponent's Life
        // cards / your rested DON!! / …". Returns -1 when the text has no dynamic cap.
        private static int ComputeDynamicCap(GameState state, string seat, string text)
        {
            if (text.IndexOf("equal to or less than", StringComparison.OrdinalIgnoreCase) < 0) return -1;
            var p = Player(state, seat);
            var opp = Player(state, OtherSeat(seat));
            if (ContainsAll(text, "total of your and your opponent's Life")) return p.Life.Count + opp.Life.Count;
            if (ContainsAll(text, "number of your opponent's Life")) return opp.Life.Count;
            if (ContainsAll(text, "number of your Life")) return p.Life.Count;
            if (ContainsAll(text, "number of your opponent's rested DON!!")) return RestedDonCount(opp);
            if (ContainsAll(text, "number of your rested DON!!")) return RestedDonCount(p);
            if (ContainsAll(text, "number of your opponent's DON!!")) return TotalFieldDon(opp);
            if (ContainsAll(text, "number of your DON!!")) return TotalFieldDon(p);
            if (ContainsAll(text, "number of cards in your hand")) return p.Hand.Count;
            var tagM = System.Text.RegularExpressions.Regex.Match(text,
                @"number of your \{([^}]+)\} type Characters",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (tagM.Success)
                return p.CharacterArea.Count(c => c != null && GetCard(c).HasFeature(tagM.Groups[1].Value.Trim()));
            if (ContainsAll(text, "number of your Characters")) return p.CharacterArea.Count(c => c != null);
            return -1;
        }

        // Removal replacement: "If (this|your {T} type) Character would be removed from the
        // field by your opponent's effect, you may <X> instead." Scans the victim's own board
        // (the victim card itself plus any aura card whose text protects the victim's type)
        // and auto-applies the first payable replacement. Returns true when the removal was
        // replaced (the victim stays on the field).
        private static bool TryRemovalReplacement(GameState state, string victimSeat, CardInstance victim)
        {
            var p = Player(state, victimSeat);
            var candidates = new List<CardInstance>();
            if (victim != null) candidates.Add(victim);
            foreach (var c in p.CharacterArea) if (c != null && c != victim) candidates.Add(c);
            if (p.Leader != null) candidates.Add(p.Leader);
            foreach (var guard in candidates)
            {
                var text = GetCard(guard)?.Effect ?? "";
                if (!ContainsAll(text, "would be removed from the field by your opponent's effect")) continue;
                foreach (var line in text.Split('\n'))
                {
                    if (!ContainsAll(line, "would be removed from the field by your opponent's effect")) continue;
                    // Whose removal does this replace? "this Character" → the guard itself;
                    // "your {T} type Character" → any of the player's characters of that type.
                    bool selfOnly = ContainsAll(line, "If this Character would be removed");
                    if (selfOnly && guard != victim) continue;
                    if (!selfOnly)
                    {
                        string tag = ParseCurlyBraceTag(line);
                        if (!string.IsNullOrEmpty(tag) && !GetCard(victim).HasFeature(tag)) continue;
                    }
                    bool oncePerTurn = line.IndexOf("[Once Per Turn]", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (oncePerTurn && p.AbilityUsedThisTurn.Contains(guard.InstanceId + ":replace")) continue;
                    // Apply the replacement action.
                    if (ContainsAll(line, "rest this Character instead"))
                    {
                        if (guard.Rested) continue;
                        guard.Rested = true;
                        Log(state, victimSeat, $"{NameId(GetCard(guard))} rests instead of {NameId(GetCard(victim))} being removed.");
                    }
                    else if (ContainsAll(line, "rest 1 of your opponent's Characters instead"))
                    {
                        var oppP = Player(state, OtherSeat(victimSeat));
                        var toRest = oppP.CharacterArea.FirstOrDefault(c => c != null && !c.Rested);
                        if (toRest == null) continue;
                        toRest.Rested = true;
                        Log(state, victimSeat, $"{NameId(GetCard(guard))}: rests {NameId(GetCard(toRest))} instead of the removal.");
                    }
                    else if (ContainsAll(line, "give your Leader") && ContainsAll(line, "power during this turn instead"))
                    {
                        var pwrM = System.Text.RegularExpressions.Regex.Match(line, @"[-\u2212\u2013\u2011\u2012\u2014](\d{3,5})\s+power");
                        int delta = pwrM.Success ? int.Parse(pwrM.Groups[1].Value) : 0;
                        if (delta <= 0 || p.Leader == null) continue;
                        state.TemporaryPowerBonus.TryGetValue(p.Leader.InstanceId, out var exL);
                        state.TemporaryPowerBonus[p.Leader.InstanceId] = exL - delta;
                        RegisterPowerModifier(p.Leader, NameId(GetCard(guard)), -delta, "endOfTurn");
                        Log(state, victimSeat, $"{NameId(GetCard(guard))}: Leader takes -{delta} power instead of the removal.");
                    }
                    else continue;   // unknown replacement action — don't consume the effect
                    if (oncePerTurn) p.AbilityUsedThisTurn.Add(guard.InstanceId + ":replace");
                    return true;
                }
            }
            return false;
        }

        // Reactive hook: "When a card is trashed from your hand by an effect, this
        // Character's effect is negated during this turn." (OP14-056 drawback on either board).
        private static void NotifyHandTrashedByEffect(GameState state, string handOwnerSeat)
        {
            foreach (var st in Seats())
            {
                var p = Player(state, st);
                foreach (var c in p.CharacterArea)
                {
                    if (c == null) continue;
                    if (ContainsAll(GetCard(c)?.Effect ?? "", "When a card is trashed from your hand by an effect")
                        && c.Owner == handOwnerSeat)
                    {
                        AddModifier(state, c, c, "effectsNegated", "thisTurn", null, st);
                        Log(state, st, $"{NameId(GetCard(c))}'s effect is negated this turn (a hand card was trashed by an effect).");
                    }
                }
            }
        }

        // Reactive hook: "When a DON!! card on your field is returned to your DON!! deck,
        // up to 1 of your {T} type Characters gains +N power …" (OP11-077).
        private static void NotifyDonReturned(GameState state, string seat)
        {
            var p = Player(state, seat);
            var cards = new List<CardInstance>();
            if (p.Leader != null) cards.Add(p.Leader);
            foreach (var c in p.CharacterArea) if (c != null) cards.Add(c);
            foreach (var c in cards)
            {
                var line = (GetCard(c)?.Effect ?? "").Split('\n')
                    .FirstOrDefault(l => ContainsAll(l, "When a DON!! card on your field is returned to your DON!! deck"));
                if (line == null || IsEffectNegated(state, c)) continue;
                int comma = line.IndexOf(',');
                if (comma < 0) continue;
                string body = NormalizeClause(line.Substring(comma + 1));
                if (IsAutomatedEffectPattern(body))
                    QueueAndAutoResolve(state, seat, c, "reactive", body, true, EffectScope.Instant, InferTargetZone(body));
            }
        }

        // Reactive hook: "When a Character is removed from the field by your effect, if your
        // opponent has N or more cards in their hand, …" (OP08-046) — fired after the unified
        // removal resolvers succeed.
        private static void NotifyRemovalByEffect(GameState state, string removerSeat)
        {
            var p = Player(state, removerSeat);
            var cards = new List<CardInstance>();
            if (p.Leader != null) cards.Add(p.Leader);
            foreach (var c in p.CharacterArea) if (c != null) cards.Add(c);
            foreach (var c in cards)
            {
                var line = (GetCard(c)?.Effect ?? "").Split('\n')
                    .FirstOrDefault(l => ContainsAll(l, "When a Character is removed from the field by your effect"));
                if (line == null || IsEffectNegated(state, c)) continue;
                int comma = line.IndexOf(',');
                if (comma < 0) continue;
                string body = NormalizeClause(line.Substring(comma + 1));
                if (IsAutomatedEffectPattern(body))
                    QueueAndAutoResolve(state, removerSeat, c, "reactive", body, true, EffectScope.Instant, InferTargetZone(body));
            }
        }

        // "All of your opponent's Characters cannot be removed from the field by your
        // effects." — a drawback printed on the REMOVER's own board (OP14-079).
        private static bool RemoverCannotRemoveByEffect(GameState state, string removerSeat)
        {
            var p = Player(state, removerSeat);
            var cards = new List<CardInstance>();
            if (p.Leader != null) cards.Add(p.Leader);
            foreach (var c in p.CharacterArea) if (c != null) cards.Add(c);
            foreach (var c in cards)
                if (!IsEffectNegated(state, c)
                    && ContainsAll(GetCard(c)?.Effect ?? "", "All of your opponent's Characters cannot be removed from the field by your effects"))
                    return true;
            return false;
        }

        // Effect negation: "Negate the effect of up to N of your opponent's Leader or
        // Character cards during this turn." Checked wherever a card's own printed text
        // grants something (auras, keywords, when-attacking, activate, blocker).
        public static bool IsEffectNegated(GameState state, CardInstance instance)
        {
            if (instance == null) return false;
            if (HasModifier(state, instance, "effectsNegated")) return true;
            // Continuous negation auras: "Your Leader and all of your Characters that do not
            // have a type including \"X\" have their effects negated." (OP13-064 drawback).
            if (instance.Owner != null && state.Players.TryGetValue(instance.Owner, out var pN))
            {
                var srcsN = new List<CardInstance>();
                if (pN.Leader != null) srcsN.Add(pN.Leader);
                foreach (var c in pN.CharacterArea) if (c != null) srcsN.Add(c);
                var instDefN = GetCard(instance);
                foreach (var srcN in srcsN)
                {
                    if (srcN.InstanceId == instance.InstanceId) continue;
                    var mN = System.Text.RegularExpressions.Regex.Match(GetCard(srcN)?.Effect ?? "",
                        @"Your Leader and all of your Characters that do not have a type including ""([^""]+)"" have their effects negated");
                    if (mN.Success && !instDefN.HasFeature(mN.Groups[1].Value.Trim())
                        && (instDefN.Type == "leader" || instDefN.Type == "character"))
                        return true;
                }
            }
            return false;
        }

        // Printed battle K.O. immunity: "[DON!! x1] This Character cannot be K.O.'d in battle."
        // (OP03-079 Vergo) or "If you have 8 or more DON!! cards on your field, this Character
        // cannot be K.O.'d in battle." (ST05-008 Shiki). Evaluated live at battle resolution.
        private static bool IsBattleKoImmune(GameState state, CardInstance instance, CardInstance attacker = null)
        {
            if (instance == null) return false;
            var text = GetCard(instance)?.Effect ?? "";
            if (!ContainsAll(text, "cannot be K.O.'d in battle")) return false;
            bool attackerIsLeader = attacker != null && GetCard(attacker)?.Type == "leader";
            foreach (var line in text.Split('\n'))
            {
                if (!ContainsAll(line, "cannot be K.O.'d in battle")) continue;
                // "…cannot be K.O.'d in battle by Leaders." (ST08-002 Uta) only grants immunity
                // against a Leader attacker — a Character attack K.O.s it normally.
                if (ContainsAll(line, "by leaders") && !attackerIsLeader) continue;
                int donReq = ParseDonThreshold(line);
                if (donReq > 0 && instance.AttachedDonIds.Count < donReq) continue;
                var fieldReq = System.Text.RegularExpressions.Regex.Match(line,
                    @"(\d+) or more DON!! cards on your field",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (fieldReq.Success)
                {
                    var ownerP = instance.Owner != null && state.Players.TryGetValue(instance.Owner, out var op) ? op : null;
                    if (ownerP == null || TotalFieldDon(ownerP) < int.Parse(fieldReq.Groups[1].Value)) continue;
                }
                return true;
            }
            return false;
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

            // "[When Attacking] ①: …" effects carry a circled-digit DON!! cost that is paid when
            // the player chooses to resolve (Activate:Main costs are paid in ActivateMain instead).
            int donCost = effect.Timing == "whenAttacking" ? ParseActivateMainCost(effect.Text ?? "") : 0;
            if (donCost > 0 && ActiveDonCount(Player(state, effect.Seat)) < donCost)
            {
                state.PendingEffects.Remove(effect);
                Log(state, effect.Seat, $"Not enough active DON!! (need {donCost}) — {NameId(CardData.GetCard(effect.SourceCardId))} effect fizzles.");
                return;
            }

            // "DON!! −N (…): body" costs (return N DON!! from field to DON!! deck — Shanks
            // leader, Gild Tesoro, Uta's [On Block], Lion's Threat, …). The pending effect is
            // optional; the first resolve interaction commits: pay the cost, then resolve only
            // the body text from here on.
            int donMinus = ParseDonMinusCost(effect.Text ?? "");
            if (donMinus > 0)
            {
                var payer = Player(state, effect.Seat);
                if (payer.CostArea.Count < donMinus)
                {
                    state.PendingEffects.Remove(effect);
                    Log(state, effect.Seat, $"Not enough DON!! on the field (need {donMinus}) — {NameId(CardData.GetCard(effect.SourceCardId))} effect fizzles.");
                    return;
                }
                // A specific cost-area DON!! was clicked — return exactly that one.
                if (!string.IsNullOrEmpty(targetId))
                {
                    int di = payer.CostArea.FindIndex(d => d.InstanceId == targetId);
                    if (di >= 0)
                    {
                        bool wasRested = payer.CostArea[di].Rested;
                        payer.CostArea.RemoveAt(di);
                        payer.DonDeck += 1;
                        Log(state, effect.Seat, $"{payer.Name} returns 1 {(wasRested ? "rested" : "active")} DON!! to the DON!! deck.");
                        NotifyDonReturned(state, effect.Seat);
                        int rem = (effect.DonPaymentRemaining > 0 ? effect.DonPaymentRemaining : donMinus) - 1;
                        effect.DonPaymentRemaining = rem;
                        if (rem > 0) return;   // more DON!! clicks needed
                        effect.Text = DonMinusBody(effect.Text);
                        targetId = null;       // the click paid the cost; it is not a body target
                    }
                    else if (effect.DonPaymentRemaining > 0)
                    {
                        return;                // mid-payment: ignore clicks that aren't cost-area DON!!
                    }
                    else
                    {
                        // Body-target click before the cost was paid: fall through to the
                        // no-choice auto-pay below so the click still counts.
                        if (!AutoPayOrAwaitDonMinus(state, effect, donMinus)) return;
                    }
                }
                else if (effect.DonPaymentRemaining > 0)
                {
                    return;                    // waiting for the player to click DON!! cards
                }
                else
                {
                    if (!AutoPayOrAwaitDonMinus(state, effect, donMinus)) return;
                }
            }

            var result = TryResolveKnownEffect(state, effect, targetId);
            if (result == EffectResolution.WaitingForTarget) return;
            if (result == EffectResolution.Resolved && donCost > 0)
                PayDonCost(Player(state, effect.Seat), donCost);

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
            if (effect == null) return;
            // "Up to N" effects may always choose zero targets, "You may"/"If" effects are
            // inherently optional — allow skipping even when queued as mandatory, so an
            // effect with no legal target can never deadlock the game.
            bool skippable = effect.Optional || IsOptionalEffectText(effect.Text) || effect.SelectionsRemaining > 0;
            if (!skippable)
                Log(state, seat, $"{NameId(CardData.GetCard(effect.SourceCardId))} mandatory effect skipped (no legal way to resolve it).");
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
            // The chosen option always resolves FOR the effect's controller — even when the
            // OPPONENT made the choice ("Your opponent chooses one:") — because option texts
            // are written from the controller's perspective ("your opponent's Life", …).
            string resolveSeat = string.IsNullOrEmpty(ch.ControllerSeat) ? seat : ch.ControllerSeat;
            var src = FindAnyInPlay(state, ch.SourceInstanceId, out _) ?? Player(state, resolveSeat).Leader;
            if (src != null)
            {
                // Infer the target zone from the sub-option text so the UI routes correctly.
                EffectTargetZone zone = InferTargetZone(chosen);
                QueueAndAutoResolve(state, resolveSeat, src, ch.Timing, chosen.Trim(), false, EffectScope.Instant, zone);
            }
            Log(state, seat, $"Chose: {chosen.Trim()}");
        }

        // Derive the EffectTargetZone a sub-effect needs based on its text.
        private static EffectTargetZone InferTargetZone(string text)
        {
            // Choice-shaped bodies ("play it or add it to Life", "Choose one:") resolve to an
            // A/B choice FIRST — zone targeting belongs to the CHOSEN option, which is queued
            // as its own effect afterwards. Returning Hand/Trash here made the trash/hand
            // picker pop up before the choice (Moria: trash → pointless pick → choice →
            // trash again). With Play, the flow is: choice → then the zone picker.
            if (ContainsAll(text, "and play it or add it")
                || ContainsAll(text, "Choose one") || ContainsAll(text, "chooses one"))
                return EffectTargetZone.Play;
            if (ContainsAll(text, "from your hand") || ContainsAll(text, "from your opponent's hand") || ContainsAll(text, "from their hand")) return EffectTargetZone.Hand;
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
                SelectionsRemaining = src.SelectionsRemaining,
                RemainingBudget = src.RemainingBudget,
                FirstPickId    = src.FirstPickId,
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
        private static bool EvaluateCondition(GameState state, string seat, string condition, string sourceInstanceId = null)
        {
            var p   = Player(state, seat);
            var opp = Player(state, OtherSeat(seat));

            // "there is a Character with a cost of 0" (post -cost effects; either player's board).
            if (ContainsAll(condition, "there is a Character with a cost of 0"))
            {
                foreach (var s in Seats())
                    if (Player(state, s).CharacterArea.Any(c => c != null && GetCost(state, c) == 0))
                        return true;
                return false;
            }

            // "your opponent has more DON!! cards on their field than you" (ST05-005 Carina).
            if (ContainsAll(condition, "opponent has more DON!! cards on their field"))
                return TotalFieldDon(opp) > TotalFieldDon(p);

            // "your opponent has N or more DON!! cards on their field" (OP02-089 etc.)
            var oppDonN = System.Text.RegularExpressions.Regex.Match(condition,
                @"opponent has (\d+) or more DON!! cards on their field",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (oppDonN.Success)
                return TotalFieldDon(opp) >= int.Parse(oppDonN.Groups[1].Value);

            // "your Leader is [Name]" (e.g. ST19-003 Tashigi: "If your Leader is [Smoker]").
            var leaderIs = System.Text.RegularExpressions.Regex.Match(condition, @"Leader is \[([^\]]+)\]",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (leaderIs.Success)
                return p.Leader != null && NameMatches(state, p.Leader, leaderIs.Groups[1].Value.Trim());

            // "this Character was played on this turn" (ST19-003 Tashigi's Activate:Main).
            if (ContainsAll(condition, "was played on this turn") || ContainsAll(condition, "was played this turn"))
            {
                var srcCard = FindCardInstance(state, sourceInstanceId);
                return srcCard != null && srcCard.PlayedOnTurn == state.TurnNumber;
            }

            // Life-count conditions: "your opponent has N or less Life cards", "you have N or
            // less Life cards", "you and your opponent have a total of N or less Life cards".
            var totalLife = System.Text.RegularExpressions.Regex.Match(condition,
                @"total of (\d+) or less Life", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (totalLife.Success)
                return p.Life.Count + opp.Life.Count <= int.Parse(totalLife.Groups[1].Value);
            var oppLife = System.Text.RegularExpressions.Regex.Match(condition,
                @"opponent has (\d+) or (less|more) Life", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (oppLife.Success)
            {
                int n = int.Parse(oppLife.Groups[1].Value);
                return oppLife.Groups[2].Value.Equals("less", StringComparison.OrdinalIgnoreCase)
                    ? opp.Life.Count <= n : opp.Life.Count >= n;
            }
            var youLife = System.Text.RegularExpressions.Regex.Match(condition,
                @"you have (\d+) or (less|more) Life", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (youLife.Success)
            {
                int n = int.Parse(youLife.Groups[1].Value);
                return youLife.Groups[2].Value.Equals("less", StringComparison.OrdinalIgnoreCase)
                    ? p.Life.Count <= n : p.Life.Count >= n;
            }

            // Compound conditions joined by "and" — every part must hold.
            if (condition.IndexOf(" and ", StringComparison.OrdinalIgnoreCase) > 0)
            {
                int andIdx = condition.IndexOf(" and ", StringComparison.OrdinalIgnoreCase);
                string left = condition.Substring(0, andIdx).Trim();
                string right = condition.Substring(andIdx + 5).Trim();
                // Only recurse when both halves look like standalone conditions (avoid splitting
                // phrases like "Leader or Character" — those contain no digits/verbs anyway).
                if (left.Length > 8 && right.Length > 8)
                    return EvaluateCondition(state, seat, left, sourceInstanceId)
                        && EvaluateCondition(state, seat, right, sourceInstanceId);
            }

            // "you have a Character with a cost of N or more"
            var costCond = System.Text.RegularExpressions.Regex.Match(condition,
                @"you have a Character with a cost of (\d+) or more",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (costCond.Success)
                return p.CharacterArea.Any(c => c != null && GetCost(state, c) >= int.Parse(costCond.Groups[1].Value));

            // "your opponent has a Character with a cost of 0" / "of N or less"
            var oppCostCond = System.Text.RegularExpressions.Regex.Match(condition,
                @"opponent has a Character with a cost of (\d+)( or less)?",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (oppCostCond.Success)
            {
                int n0 = int.Parse(oppCostCond.Groups[1].Value);
                bool orLess = oppCostCond.Groups[2].Success;
                return opp.CharacterArea.Any(c => c != null && (orLess ? GetCost(state, c) <= n0 : GetCost(state, c) == n0));
            }

            // "you have N or less cards in your hand" / "N or more cards in your hand"
            var handN = System.Text.RegularExpressions.Regex.Match(condition,
                @"you have (\d+) or (less|more) cards in your hand",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (handN.Success)
            {
                int hn = int.Parse(handN.Groups[1].Value);
                return handN.Groups[2].Value.Equals("less", StringComparison.OrdinalIgnoreCase)
                    ? p.Hand.Count <= hn : p.Hand.Count >= hn;
            }

            // "you have N (or more/less) DON!! cards on your field" (exact when unqualified)
            var donCount = System.Text.RegularExpressions.Regex.Match(condition,
                @"you have (\d+)( or more| or less)? DON!! cards on your field",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (donCount.Success)
            {
                int dn = int.Parse(donCount.Groups[1].Value);
                int have = TotalFieldDon(p);
                if (!donCount.Groups[2].Success) return have == dn;
                return donCount.Groups[2].Value.Contains("more") ? have >= dn : have <= dn;
            }

            // "you have N or more rested Characters" / "N or less Characters"
            var restedN = System.Text.RegularExpressions.Regex.Match(condition,
                @"you have (\d+) or (more|less) (rested )?Characters",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (restedN.Success)
            {
                int rn = int.Parse(restedN.Groups[1].Value);
                bool restedOnly = restedN.Groups[3].Success;
                int cnt = p.CharacterArea.Count(c => c != null && (!restedOnly || c.Rested));
                return restedN.Groups[2].Value.Equals("more", StringComparison.OrdinalIgnoreCase) ? cnt >= rn : cnt <= rn;
            }

            // "you have [Name]" / "you have no other [Name] (with a base cost of N)"
            var haveName = System.Text.RegularExpressions.Regex.Match(condition,
                @"you have (no other )?\[([^\]]+)\]( with a base cost of (\d+))?",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (haveName.Success)
            {
                bool noOther = haveName.Groups[1].Success;
                string wantName = haveName.Groups[2].Value.Trim();
                int? wantBc = haveName.Groups[4].Success ? int.Parse(haveName.Groups[4].Value) : (int?)null;
                int matches = 0;
                var scanC = new List<CardInstance>();
                if (p.Leader != null) scanC.Add(p.Leader);
                foreach (var c in p.CharacterArea) if (c != null) scanC.Add(c);
                foreach (var c in scanC)
                {
                    if (c.InstanceId == sourceInstanceId) continue;   // "no OTHER" excludes self
                    if (!NameMatches(state, c, wantName)) continue;
                    if (wantBc.HasValue && GetCard(c).Cost != wantBc.Value) continue;
                    matches++;
                }
                return noOther ? matches == 0 : matches > 0;
            }

            // "this Character battles your opponent's Character (during this turn)"
            if (ContainsAll(condition, "Character battles your opponent's Character")
                || ContainsAll(condition, "Leader battles your opponent's Character"))
            {
                if (state.Battle == null) return false;
                var bAtk = FindCardInstance(state, state.Battle.AttackerId);
                var bDef = FindCardInstance(state, state.Battle.TargetId);
                return bAtk != null && bAtk.InstanceId == sourceInstanceId
                    && bDef != null && GetCard(bDef)?.Type == "character";
            }

            // "you have N or more {T} type Characters" / "you do not have N Characters with a
            // cost of N or more" / "you have N {T} type Characters with different card names"
            {
                var tagCount = System.Text.RegularExpressions.Regex.Match(condition,
                    @"you (do not have|have) (\d+)( or more| or less)? (?:\{([^}]+)\} type )?Characters( with a cost of (\d+) or more)?( with different card names)?",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (tagCount.Success)
                {
                    bool negated = tagCount.Groups[1].Value.StartsWith("do not", StringComparison.OrdinalIgnoreCase);
                    int wantN = int.Parse(tagCount.Groups[2].Value);
                    string cmp = tagCount.Groups[3].Success ? tagCount.Groups[3].Value.Trim() : "";
                    string tagW = tagCount.Groups[4].Success ? tagCount.Groups[4].Value.Trim() : null;
                    int minCost2 = tagCount.Groups[6].Success ? int.Parse(tagCount.Groups[6].Value) : -1;
                    bool distinct = tagCount.Groups[7].Success;
                    var pool = p.CharacterArea.Where(c => c != null
                        && (tagW == null || GetCard(c).HasFeature(tagW))
                        && (minCost2 < 0 || GetCost(state, c) >= minCost2));
                    int cnt2 = distinct
                        ? pool.Select(c => GetEffectiveName(state, c)).Distinct(StringComparer.OrdinalIgnoreCase).Count()
                        : pool.Count();
                    bool met = cmp.Equals("or less", StringComparison.OrdinalIgnoreCase) ? cnt2 <= wantN : cnt2 >= wantN;
                    return negated ? !met : met;
                }
            }

            // "you have a [Name] (Character)" — indefinite-article variant.
            var haveA = System.Text.RegularExpressions.Regex.Match(condition,
                @"you have an? \[([^\]]+)\]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (haveA.Success)
            {
                var scanA = new List<CardInstance>();
                if (p.Leader != null) scanA.Add(p.Leader);
                foreach (var c in p.CharacterArea) if (c != null) scanA.Add(c);
                return scanA.Any(c => NameMatches(state, c, haveA.Groups[1].Value.Trim()));
            }

            // "you have N or less cards in your deck"
            var deckN = System.Text.RegularExpressions.Regex.Match(condition,
                @"you have (\d+) or (less|more) cards in your deck",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (deckN.Success)
            {
                int dnn = int.Parse(deckN.Groups[1].Value);
                return deckN.Groups[2].Value.Equals("less", StringComparison.OrdinalIgnoreCase)
                    ? p.Deck.Count <= dnn : p.Deck.Count >= dnn;
            }

            // "your Leader's type includes \"X\""
            var leadIncl = System.Text.RegularExpressions.Regex.Match(condition,
                @"Leader's type includes ""([^""]+)""",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (leadIncl.Success)
                return p.Leader != null && GetCard(p.Leader).HasFeature(leadIncl.Groups[1].Value.Trim());

            // "the only Characters on your field are {T} type Characters"
            var onlyTag = System.Text.RegularExpressions.Regex.Match(condition,
                @"only Characters on your field are \{([^}]+)\} type",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (onlyTag.Success)
            {
                string ot = onlyTag.Groups[1].Value.Trim();
                var mine = p.CharacterArea.Where(c => c != null).ToList();
                return mine.Count > 0 && mine.All(c => GetCard(c).HasFeature(ot));
            }

            // "you have N or more Events in your trash"
            var evTrash = System.Text.RegularExpressions.Regex.Match(condition,
                @"you have (\d+) or more Events in your trash",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (evTrash.Success)
                return p.Trash.Count(c => GetCard(c).Type == "event") >= int.Parse(evTrash.Groups[1].Value);

            // "your opponent has N or less DON!! cards on their field"
            var oppDonLe = System.Text.RegularExpressions.Regex.Match(condition,
                @"opponent has (\d+) or (less|more) DON!! cards on their field",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (oppDonLe.Success)
            {
                int odn = int.Parse(oppDonLe.Groups[1].Value);
                return oppDonLe.Groups[2].Value.Equals("less", StringComparison.OrdinalIgnoreCase)
                    ? TotalFieldDon(opp) <= odn : TotalFieldDon(opp) >= odn;
            }

            // "you have N or more cards in your trash"
            var trashN2 = System.Text.RegularExpressions.Regex.Match(condition,
                @"you have (\d+) or (more|less) cards in your trash",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (trashN2.Success)
            {
                int tn2 = int.Parse(trashN2.Groups[1].Value);
                return trashN2.Groups[2].Value.Equals("more", StringComparison.OrdinalIgnoreCase)
                    ? p.Trash.Count >= tn2 : p.Trash.Count <= tn2;
            }

            // "your opponent has a Character with N power or more"
            var oppPwrC = System.Text.RegularExpressions.Regex.Match(condition,
                @"opponent has a Character with (\d{3,5}) power or more",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (oppPwrC.Success)
                return opp.CharacterArea.Any(c => c != null && GetPower(state, c) >= int.Parse(oppPwrC.Groups[1].Value));

            // "your Leader is multicolored"
            if (ContainsAll(condition, "Leader is multicolored"))
                return p.Leader != null && (GetCard(p.Leader).Color ?? "").Contains("/");

            // "this Character is rested"
            if (ContainsAll(condition, "this Character is rested"))
            {
                var srcR = FindCardInstance(state, sourceInstanceId);
                return srcR != null && srcR.Rested;
            }

            // "you have a Character with N base power or more"
            var bpCond = System.Text.RegularExpressions.Regex.Match(condition,
                @"you have a Character with (\d{3,5}) base power or more",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (bpCond.Success)
                return p.CharacterArea.Any(c => c != null && GetCard(c).Power >= int.Parse(bpCond.Groups[1].Value));

            // "you have N or more DON!! cards on your field"
            var donField = System.Text.RegularExpressions.Regex.Match(condition,
                @"you have (\d+) or more DON!! cards on your field",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (donField.Success)
                return TotalFieldDon(p) >= int.Parse(donField.Groups[1].Value);

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
                condition, @"opponent has (\d+) or more cards? in (?:their )?hand",
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
            if (chooseIdx < 0) chooseIdx = text.IndexOf("chooses one", StringComparison.OrdinalIgnoreCase);
            if (chooseIdx < 0) return false;
            // Split on bullet character (• U+2022) or "- " after the colon.
            int colonIdx = text.IndexOf(':', chooseIdx);
            if (colonIdx < 0) return false;
            string after = text.Substring(colonIdx + 1).Trim();
            // Try bullet split first, then newline split.
            string[] parts = after.Contains('•')
                ? after.Split('•')
                : after.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            var options = parts.Select(p => p.Trim().TrimStart('-', '•', '‐').Trim())
                .Where(p => p.Length > 0).ToList();
            if (options.Count < 2) return false;
            // "Your opponent chooses one:" — the OPPONENT makes this choice (its options still
            // resolve for the effect's controller via ResolveChoice, which uses ChoiceState.Seat
            // as the resolver seat — so flip the option texts' perspective is NOT needed: these
            // cards phrase options from the opponent's viewpoint already).
            bool opponentChooses = text.IndexOf("Your opponent chooses one", StringComparison.OrdinalIgnoreCase) >= 0
                                || text.IndexOf("opponent chooses one", StringComparison.OrdinalIgnoreCase) >= 0;
            state.ActiveChoice = new ChoiceState
            {
                Seat = opponentChooses ? OtherSeat(effect.Seat) : effect.Seat,
                ControllerSeat = effect.Seat,
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

        // An effect is genuinely optional when its text says so ("You may …", "up to N", a
        // conditional). Mandatory effects ("Draw 2 cards.") must not offer a skip button.
        private static bool IsOptionalEffectText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return true;
            var t = System.Text.RegularExpressions.Regex.Replace(text, @"^\s*(\[[^\]]+\]\s*/?\s*)+", "").TrimStart();
            return t.StartsWith("You may", StringComparison.OrdinalIgnoreCase)
                || t.StartsWith("If ", StringComparison.OrdinalIgnoreCase)
                || text.IndexOf("up to", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("you may", StringComparison.OrdinalIgnoreCase) >= 0;
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

        // Queues an effect exactly like QueueEffect, then immediately resolves it if
        // TryResolveKnownEffect can handle it without a player-chosen target (e.g. "Search
        // your deck for a card and add it to hand", "Look at N from the top of your deck").
        // Matches the immediate feel Activate:Main abilities already have (e.g. Jewelry
        // Bonney's deck-look opens right away) instead of making the player click
        // "Resolve / Manual" first for something that needed no decision. Reuses ResolveEffect
        // itself, which already safely leaves the effect pending if a target is genuinely
        // required (EffectResolution.WaitingForTarget short-circuits before removal).
        private static void QueueAndAutoResolve(GameState state, string seat, CardInstance source, string timing, string text, bool optional,
            EffectScope scope = EffectScope.Instant, EffectTargetZone targetZone = EffectTargetZone.Play)
        {
            QueueEffect(state, seat, source, timing, text, optional, scope, targetZone);
            // Only explicit "you may" wording is a real opt-in decision — those wait for the
            // player. Everything else auto-resolves: mandatory effects (including "If <cond>,
            // ..." — the CONDITION decides, not the player, e.g. Kikunojo's set-Leader-active)
            // fire immediately, and "up to N" targeting effects auto-enter target selection
            // (the pick itself is the decision; SKIP stays available for the zero-pick option).
            if (!string.IsNullOrEmpty(text) && text.IndexOf("you may", StringComparison.OrdinalIgnoreCase) >= 0) return;
            if (!IsAutomatedEffectPattern(text)) return;
            var queued = state.PendingEffects[state.PendingEffects.Count - 1];
            ResolveEffect(state, seat, queued.EffectId, null);
        }

        // Attempt to auto-pay a "You may <cost>:" whose components need no hidden-zone picks:
        // self-rest, rest N DON!!, return DON!! to deck, trash this Character, return trash to
        // deck, turn Life face-up, add top-of-Life to hand. Returns 1 = paid, 0 = unpayable,
        // -1 = a component wasn't recognized (caller falls through to other flows).
        private static int TryAutoPayCost(GameState state, string seat, CardInstance source, string costText)
        {
            var p = Player(state, seat);
            var actions = new List<Action>();
            foreach (var rawPart in System.Text.RegularExpressions.Regex.Split(costText, @",? and |, "))
            {
                var part = rawPart.Trim();
                if (part.Length == 0) continue;
                if (System.Text.RegularExpressions.Regex.IsMatch(part, @"^rest this (Character|Leader|Stage|card)$",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    if (source == null || source.Rested) return 0;
                    var capS = source;
                    actions.Add(() => { capS.Rested = true; Log(state, seat, $"{NameId(GetCard(capS))} rests (cost)."); });
                    continue;
                }
                var restDonM = System.Text.RegularExpressions.Regex.Match(part, @"^rest (\d+) of your DON!! cards?$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (restDonM.Success)
                {
                    int n = int.Parse(restDonM.Groups[1].Value);
                    if (ActiveDonCount(p) < n) return 0;
                    actions.Add(() => { PayDonCost(p, n); Log(state, seat, $"Rested {n} DON!! (cost)."); });
                    continue;
                }
                var retDonM = System.Text.RegularExpressions.Regex.Match(part, @"^return (\d+) of your (?:active )?DON!! cards? to your DON!! deck$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (retDonM.Success)
                {
                    int n = int.Parse(retDonM.Groups[1].Value);
                    if (p.CostArea.Count < n) return 0;
                    actions.Add(() => PayDonMinus(state, seat, n));
                    continue;
                }
                var restNamed = System.Text.RegularExpressions.Regex.Match(part,
                    @"^(?:rest )?(\d+) of your \[([^\]]+)\] cards?$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (restNamed.Success)
                {
                    int rnN = int.Parse(restNamed.Groups[1].Value);
                    string rnName = restNamed.Groups[2].Value.Trim();
                    var rnCandidates = new List<CardInstance>();
                    if (p.Leader != null) rnCandidates.Add(p.Leader);
                    foreach (var c in p.CharacterArea) if (c != null) rnCandidates.Add(c);
                    var rnPick = rnCandidates.Where(c => !c.Rested && NameMatches(state, c, rnName)).Take(rnN).ToList();
                    if (rnPick.Count < rnN) return 0;
                    actions.Add(() =>
                    {
                        foreach (var c in rnPick) { c.Rested = true; Log(state, seat, $"{NameId(GetCard(c))} rests (cost)."); }
                    });
                    continue;
                }
                if (System.Text.RegularExpressions.Regex.IsMatch(part, @"^trash this Character$",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    if (source == null || source.Zone != "character") return 0;
                    var capT = source;
                    actions.Add(() => MoveToTrash(state, seat, capT.InstanceId));
                    continue;
                }
                var retTrashM = System.Text.RegularExpressions.Regex.Match(part,
                    @"^return (\d+) cards? from your trash to (?:your deck and shuffle it|the bottom of your deck[^:]*)$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (retTrashM.Success)
                {
                    int n = int.Parse(retTrashM.Groups[1].Value);
                    if (p.Trash.Count < n) return 0;
                    bool shuffle = ContainsAll(part, "shuffle");
                    actions.Add(() =>
                    {
                        for (int i = 0; i < n; i++)
                        {
                            var tc = p.Trash[p.Trash.Count - 1];
                            p.Trash.RemoveAt(p.Trash.Count - 1);
                            tc.Zone = "deck";
                            p.Deck.Add(tc);
                        }
                        if (shuffle) ShuffleInPlace(p.Deck, $"{state.Seed}:{seat}:costshuffle:{state.EffectSequence}");
                        Log(state, seat, $"Returned {n} card(s) from trash to the deck (cost).");
                    });
                    continue;
                }
                var faceUpM = System.Text.RegularExpressions.Regex.Match(part,
                    @"^turn (\d+) cards? from the top of your Life cards? face-up$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (faceUpM.Success)
                {
                    int n = int.Parse(faceUpM.Groups[1].Value);
                    if (p.Life.Count < n) return 0;
                    actions.Add(() =>
                    {
                        for (int i = 0; i < n; i++) p.Life[p.Life.Count - 1 - i].FaceUp = true;
                        Log(state, seat, $"Turned {n} Life card(s) face-up (cost).");
                    });
                    continue;
                }
                var addLifeM = System.Text.RegularExpressions.Regex.Match(part,
                    @"^add (\d+) cards? from the top of your Life cards? to your hand$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (addLifeM.Success)
                {
                    int n = int.Parse(addLifeM.Groups[1].Value);
                    if (p.Life.Count < n) return 0;
                    actions.Add(() =>
                    {
                        for (int i = 0; i < n; i++)
                        {
                            var lc = p.Life[p.Life.Count - 1];
                            p.Life.RemoveAt(p.Life.Count - 1);
                            lc.Zone = "hand";
                            p.Hand.Add(lc);
                        }
                        Log(state, seat, $"Added {n} Life card(s) to hand (cost).");
                    });
                    continue;
                }
                return -1;   // unrecognized component
            }
            if (actions.Count == 0) return -1;
            foreach (var a in actions) a();
            return 1;
        }

        // Does a hand card satisfy an optional-cost clause like "trash 2 black {Navy} type
        // cards from your hand"? Checks the color word (if any) and the {Feature} tag (if any).
        private static bool CostCardMatches(string costText, CardDef def)
        {
            if (def == null) return false;
            foreach (var color in new[] { "red", "green", "blue", "purple", "black", "yellow" })
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(costText, $@"\b{color}\b",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    if ((def.Color ?? "").IndexOf(color, StringComparison.OrdinalIgnoreCase) < 0) return false;
                    break;
                }
            }
            if (ContainsAll(costText, "with a [Trigger]") && string.IsNullOrEmpty(def.Trigger)) return false;
            return CardPassesFeatureFilter(costText, def);
        }

        // Queue an optional-cost effect's body once its cost is fully paid, auto-resolving it
        // immediately when the body needs no player decision.
        private static void QueueBody(GameState state, PendingEffect parent, string bodyText)
        {
            if (string.IsNullOrWhiteSpace(bodyText)) return;
            var src = FindCardInstance(state, parent.SourceInstanceId) ?? Player(state, parent.Seat).Leader;
            if (src == null) return;
            string norm = NormalizeClause(bodyText);
            QueueAndAutoResolve(state, parent.Seat, src, parent.Timing, norm,
                IsOptionalEffectText(norm), parent.Scope, InferTargetZone(bodyText));
        }

        // Returns true if `def` satisfies the feature-tag requirement stated in `effectText`.
        // Feature tags appear as {Feature Name} in card text, e.g. {Straw Hat Crew}, {Supernovas}.
        // When multiple tags are listed with "or" (e.g. {Supernovas} or {Heart Pirates}) the card
        // must match at least one. When no tags are present every card is considered a match.
        private static bool CardPassesFeatureFilter(string effectText, CardDef def)
        {
            if (string.IsNullOrEmpty(effectText) || !effectText.Contains("{")) return true;
            // Generic: collect EVERY {Tag} in the text (previously only 5 hardcoded ST01/ST02
            // tags were recognized, so tags like {FILM} were silently ignored). "or"-listed tags
            // mean match-at-least-one. Tags appearing in a "your Leader has the {X} type"
            // CONDITION describe the leader, not the target, so strip those clauses first.
            string scanText = System.Text.RegularExpressions.Regex.Replace(
                effectText, @"Leader (?:has|is) the \{[^}]+\} type", "",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var tags = System.Text.RegularExpressions.Regex.Matches(scanText, @"\{([^}]+)\}");
            if (tags.Count == 0) return true;
            foreach (System.Text.RegularExpressions.Match m in tags)
                if (def.HasFeature(m.Groups[1].Value.Trim())) return true;
            return false;
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
            // Strip leading (possibly '/'-combined) timing tags and "Then," connectives so the
            // phrase checks below see the same text the resolver matches against.
            text = System.Text.RegularExpressions.Regex.Replace(text, @"^\s*(\[[^\]]+\]\s*/?\s*)+", "");
            text = NormalizeClause(text);
            // Strip a leading "If <condition>," clause BEFORE analyzing target words — the
            // condition often names card types that are NOT targets (ST19-003 Tashigi:
            // "If your Leader is [Smoker], give up to 1 of your opponent's Characters
            // −4 cost..." must not mark the Leader as a valid target).
            text = System.Text.RegularExpressions.Regex.Replace(text,
                @"^If [^,]+,\s*", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            // While an optional-cost effect is still paying its cost ("You may <cost>: <body>"),
            // the click chooses a COST card — validate against the cost clause, not the body.
            {
                var costGate = System.Text.RegularExpressions.Regex.Match(text,
                    @"^You may (?<cost>[^:]+):", System.Text.RegularExpressions.RegexOptions.Singleline);
                if (costGate.Success)
                {
                    string costTx = costGate.Groups["cost"].Value;
                    if (ContainsAll(costTx, "trash") && ContainsAll(costTx, "from your hand"))
                        return Player(state, effect.Seat).Hand.Any(c => c.InstanceId == card.InstanceId)
                            && CostCardMatches(costTx, def);
                    if (ContainsAll(costTx, "place") && ContainsAll(costTx, "from your trash"))
                        return Player(state, effect.Seat).Trash.Any(c => c.InstanceId == card.InstanceId);
                    var costPick = System.Text.RegularExpressions.Regex.Match(costTx,
                        @"^(K\.O\.|trash|rest) (\d+) of your ([^:]*?)Characters",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (costPick.Success)
                        return card.Owner == effect.Seat && card.Zone == "character"
                            && CardPassesFeatureFilter(costPick.Groups[3].Value, def)
                            && !(ContainsAll(costTx, "other than this Character") && card.InstanceId == effect.SourceInstanceId)
                            && !(costPick.Groups[1].Value.Equals("rest", StringComparison.OrdinalIgnoreCase) && card.Rested);
                }
            }
            // Self-targeting effects glow only the source card.
            if (ContainsAll(text, "This Character gains") || ContainsAll(text, "this Leader gains")
                || ContainsAll(text, "Set this Character as active") || ContainsAll(text, "rest this Character"))
                return card.InstanceId == effect.SourceInstanceId;
            // Swap picks: the second pick cannot be the first.
            if (ContainsAll(text, "Swap the base power") && card.InstanceId == effect.FirstPickId) return false;
            // Multi-clause effects ("Do X. Then, do Y.") resolve one clause at a time (the
            // splitter in TryResolveKnownEffect queues Y separately), so the current click is
            // always choosing a target for the FIRST clause — validate against it alone.
            // Otherwise clause-Y filters (e.g. Backlight's "…with a cost of 0") wrongly mark
            // clause-X targets invalid (red).
            int thenSplit = FindThenClause(text);
            if (thenSplit > 0) text = text.Substring(0, thenSplit).Trim();

            bool oppZone = text.IndexOf("opponent's hand", StringComparison.OrdinalIgnoreCase) >= 0
                        || text.IndexOf("opponent's trash", StringComparison.OrdinalIgnoreCase) >= 0
                        || text.IndexOf("from their hand", StringComparison.OrdinalIgnoreCase) >= 0;
            var zoneOwner = oppZone ? Player(state, OtherSeat(effect.Seat)) : Player(state, effect.Seat);
            // Owner-agnostic effects ("to the owner's hand/deck", "K.O. up to N Characters"
            // without an ownership word) accept either side's Characters.
            bool ownerAgnostic = ContainsAll(text, "owner's hand") || ContainsAll(text, "owner's deck")
                || ContainsAll(text, "owner's Life")
                || (System.Text.RegularExpressions.Regex.IsMatch(text, @"K\.O\. up to \d+ (rested )?Characters?\b",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                    && !ContainsAll(text, "your opponent's") && !ContainsAll(text, "of your "));

            // Dual-zone plays ("from your hand or trash"): a card in EITHER of the owner's
            // hand or trash is a candidate; the generic filters below still apply.
            if (ContainsAll(text, "from your hand or trash"))
            {
                var dzOwner = Player(state, effect.Seat);
                if (!dzOwner.Hand.Any(c => c.InstanceId == card.InstanceId)
                    && !dzOwner.Trash.Any(c => c.InstanceId == card.InstanceId)) return false;
                if (ContainsAll(text, "Character") && def.Type != "character") return false;
            }
            else
            switch (effect.TargetZone)
            {
                case EffectTargetZone.Hand:
                    if (!zoneOwner.Hand.Any(c => c.InstanceId == card.InstanceId)) return false;
                    // "…Character/Event/Stage card…from your hand" restricts by card type.
                    if (System.Text.RegularExpressions.Regex.IsMatch(text, @"\bCharacter card", System.Text.RegularExpressions.RegexOptions.IgnoreCase) && def.Type != "character") return false;
                    if ((System.Text.RegularExpressions.Regex.IsMatch(text, @"\bEvent card", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                         || ContainsAll(text, "type Event")) && def.Type != "event") return false;
                    if (System.Text.RegularExpressions.Regex.IsMatch(text, @"\bStage card", System.Text.RegularExpressions.RegexOptions.IgnoreCase) && def.Type != "stage") return false;
                    if (ContainsAll(text, "with a [Trigger]") && string.IsNullOrEmpty(def.Trigger)) return false;
                    if (ContainsAll(text, "cannot be played by effects")) { }
                    if (ContainsAll(def.Effect ?? "", "This card in your hand cannot be played by effects")
                        && ContainsAll(text, "Play")) return false;
                    break;
                case EffectTargetZone.Trash:
                    if (!zoneOwner.Trash.Any(c => c.InstanceId == card.InstanceId)) return false;
                    // "Play N [Name] from your trash" name filter.
                    var trashName = System.Text.RegularExpressions.Regex.Match(text, @"Play (?:up to )?1 \[([^\]]+)\]");
                    if (trashName.Success && !NameMatches(state, card, trashName.Groups[1].Value.Trim())) return false;
                    if (ContainsAll(text, "Character") && !ContainsAll(text, "card") && def.Type != "character") return false;
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
                    bool mentionsOpp = text.IndexOf("opponent's", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool mentionsYour = System.Text.RegularExpressions.Regex.IsMatch(text, @"of your (?!opponent)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (!ownerAgnostic)
                    {
                        if (mentionsOpp && !mentionsYour && card.Owner == effect.Seat) return false;
                        if (mentionsYour && !mentionsOpp && card.Owner != effect.Seat) return false;
                    }
                    // "[Name] cards/Characters" named-target filters.
                    var playNameF = System.Text.RegularExpressions.Regex.Match(text,
                        @"of your (?:opponent's )?\[([^\]]+)\]");
                    if (playNameF.Success && !NameMatches(state, card, playNameF.Groups[1].Value.Trim())) return false;
                    // Rested/active requirement, e.g. "rested Characters ... as active" needs a rested target.
                    if (System.Text.RegularExpressions.Regex.IsMatch(text, "rested (leader|character)", System.Text.RegularExpressions.RegexOptions.IgnoreCase) && !card.Rested) return false;
                    if (System.Text.RegularExpressions.Regex.IsMatch(text, "active (leader|character)", System.Text.RegularExpressions.RegexOptions.IgnoreCase) && card.Rested) return false;
                    break;
                default:
                    return false;
            }

            if (!CardPassesFeatureFilter(text, def)) return false;

            // "other than [Name]" self-exclusion (e.g. Robin: "…other than [Nico Robin]").
            var otherThan = System.Text.RegularExpressions.Regex.Match(text, @"other than \[([^\]]+)\]",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (otherThan.Success && NameMatches(state, card, otherThan.Groups[1].Value.Trim()))
                return false;

            // Cost caps compare against the EFFECTIVE cost (printed cost + CostDelta modifiers,
            // e.g. Backlight's "-4 cost"), not the printed cost — otherwise a card made legal by
            // a cost-reduction effect is wrongly flagged invalid. "cost of N" without "or less"
            // (e.g. "with a cost of 0") is an exact-value filter.
            bool baseCostCap = ContainsAll(text, "base cost");
            int costCap = ParseLimit(text, @"cost (?:of )?(\d+) or less");
            int cardCost = baseCostCap ? def.Cost : GetCost(state, card);
            if (costCap >= 0 && cardCost > costCap) return false;
            if (costCap < 0)
            {
                var costRange = System.Text.RegularExpressions.Regex.Match(text, @"cost of (\d+) to (\d+)");
                if (costRange.Success)
                {
                    if (cardCost < int.Parse(costRange.Groups[1].Value) || cardCost > int.Parse(costRange.Groups[2].Value)) return false;
                }
                else
                {
                    int costExact = ParseLimit(text, @"cost of (\d+)\b(?! or)");
                    if (costExact >= 0 && cardCost != costExact) return false;
                }
            }
            int bpCapV = ParseLimit(text, @"(\d{3,5}) base power or less");
            if (bpCapV >= 0 && def.Power > bpCapV) return false;
            int bpMinV = ParseLimit(text, @"(\d{3,5}) base power or more");
            if (bpMinV >= 0 && def.Power < bpMinV) return false;

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
                case "onBlock":             return "[On Block]";
                case "onOpponentsAttack":   return "[On Your Opponent's Attack]";
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
            // Leading [Timing]/[DON!! xN]/[Once Per Turn] tags carry no matching information and
            // break anchored checks ("If …" conditionals, "You may …:" costs) — strip them for
            // pattern matching. effect.Text itself is left untouched for the UI.
            text = System.Text.RegularExpressions.Regex.Replace(text, @"^\s*(\[[^\]]+\]\s*/?\s*)+", "");
            text = NormalizeClause(text);   // strip stray leading "Then," connectives
            var owner = Player(state, effect.Seat);
            var sourceName = NameId(CardData.GetCard(effect.SourceCardId));

            // Leading "If <condition>," gate — evaluated BEFORE any handler, so a body
            // phrase can never resolve while its printed condition is unmet (EB04-058
            // Borsalino: "If you have 2 or less Life cards, add ... to your Life").
            // Met → strip the clause and resolve the body; unmet/unknown → skip.
            {
                var leadIf = System.Text.RegularExpressions.Regex.Match(text,
                    @"^If ([^,]{3,90}), (.+)$", System.Text.RegularExpressions.RegexOptions.Singleline);
                if (leadIf.Success && !ContainsAll(text, "Choose one") && !ContainsAll(text, "chooses one"))
                {
                    if (!EvaluateCondition(state, effect.Seat, leadIf.Groups[1].Value.Trim(), effect.SourceInstanceId))
                    {
                        Log(state, effect.Seat, $"{sourceName}: condition not met — effect skipped.");
                        return EffectResolution.Resolved;
                    }
                    text = leadIf.Groups[2].Value.Trim();
                }
            }

            // "Choose one:" branches must be parsed BEFORE any other handler — option texts
            // routinely embed phrases (K.O., -N power, draw) that would otherwise match a
            // single-effect resolver and resolve option A without offering the choice.
            if ((ContainsAll(text, "Choose one") || ContainsAll(text, "chooses one"))
                && !System.Text.RegularExpressions.Regex.IsMatch(text, @"^You may [^:]+:",
                        System.Text.RegularExpressions.RegexOptions.Singleline))
            {
                if (TryParseChoiceEffect(state, effect)) return EffectResolution.Resolved;
            }

            // ---- Optional-cost prefix: "You may <cost>: <body>" -----------------------------
            // Covers the black/purple staples: "You may trash N [color] {Type} card(s) from your
            // hand: …" (ST19-001 Smoker, ST19-002 Sengoku, OP02-098 Koby), "You may place 1 card
            // from your trash at the bottom of your deck: …" (ST19-004 Hina, ST19-005 Garp) and
            // "You may rest this Character and trash 1 … from your hand: …" (ST05-005 Carina).
            // Must run BEFORE every other pattern so the body's phrases (K.O., draw, …) don't
            // resolve without the cost being paid. The whole effect is optional (SKIP passes).
            {
                string bare = System.Text.RegularExpressions.Regex.Replace(text, @"^\s*(\[[^\]]+\]\s*/?\s*)+", "");
                var costM = System.Text.RegularExpressions.Regex.Match(bare,
                    @"^You may (?<cost>[^:]+):\s*(?<body>.+)$",
                    System.Text.RegularExpressions.RegexOptions.Singleline);
                if (costM.Success)
                {
                    string costText = costM.Groups["cost"].Value.Trim();
                    string bodyText = costM.Groups["body"].Value.Trim();

                    // Fully auto-payable costs (self-rest, rest/return DON!!, trash self, …):
                    // pay immediately and queue the body — no clicks needed.
                    if (!ContainsAll(costText, "from your hand") && !ContainsAll(costText, "from your trash")
                        && !ContainsAll(costText, "top or bottom of your Life"))
                    {
                        var srcAuto = FindCardInstance(state, effect.SourceInstanceId);
                        int payRes = TryAutoPayCost(state, effect.Seat, srcAuto, costText);
                        if (payRes == 0)
                        {
                            Log(state, effect.Seat, $"{sourceName}: cost cannot be paid ({costText}).");
                            return EffectResolution.Resolved;
                        }
                        if (payRes == 1)
                        {
                            QueueBody(state, effect, bodyText);
                            return EffectResolution.Resolved;
                        }
                        // -1 → fall through to the pick-based flows below.
                    }

                    // Cost: pick-based own-board sacrifice/rest: "K.O./trash/rest N of your
                    // ({T} type )Characters (other than this Character)".
                    {
                        var pickM = System.Text.RegularExpressions.Regex.Match(costText,
                            @"^(K\.O\.|trash|rest) (\d+) of your ([^:]*?)Characters",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (pickM.Success)
                        {
                            if (effect.SelectionsRemaining <= 0) effect.SelectionsRemaining = int.Parse(pickM.Groups[2].Value);
                            var pcT = FindAnyInPlay(state, targetId, out var pcSeat);
                            if (pcT == null)
                            {
                                Log(state, effect.Seat, $"Click {effect.SelectionsRemaining} of your Character(s) to pay {sourceName}'s cost, or skip.");
                                return EffectResolution.WaitingForTarget;
                            }
                            if (pcSeat != effect.Seat || GetCard(pcT).Type != "character"
                                || !CardPassesFeatureFilter(pickM.Groups[3].Value, GetCard(pcT))
                                || (ContainsAll(costText, "other than this Character") && pcT.InstanceId == effect.SourceInstanceId))
                            {
                                Log(state, effect.Seat, "That is not a valid cost target.");
                                return EffectResolution.WaitingForTarget;
                            }
                            string verb = pickM.Groups[1].Value.ToLowerInvariant();
                            if (verb == "rest")
                            {
                                if (pcT.Rested) { Log(state, effect.Seat, "That Character is already rested."); return EffectResolution.WaitingForTarget; }
                                pcT.Rested = true;
                                Log(state, effect.Seat, $"{NameId(GetCard(pcT))} rests (cost).");
                            }
                            else
                            {
                                MoveToTrash(state, effect.Seat, pcT.InstanceId);
                                Log(state, effect.Seat, $"{NameId(GetCard(pcT))} paid as cost.");
                            }
                            effect.SelectionsRemaining--;
                            if (effect.SelectionsRemaining > 0) return EffectResolution.WaitingForTarget;
                            if (ContainsAll(costText, "rest this Character"))
                            {
                                var rSelf2 = FindAnyInPlay(state, effect.SourceInstanceId, out _);
                                if (rSelf2 != null) rSelf2.Rested = true;
                            }
                            QueueBody(state, effect, bodyText);
                            return EffectResolution.Resolved;
                        }
                    }

                    // Cost: add 1 card from the top or bottom of your Life cards to your hand.
                    // Routed through the Choose-one modal so the player picks top vs bottom;
                    // each option resolves as its own clause and chains the body via "Then,".
                    if (ContainsAll(costText, "from the top or bottom of your Life"))
                    {
                        if (owner.Life.Count == 0)
                        {
                            Log(state, effect.Seat, $"{sourceName}: no Life cards — cost cannot be paid.");
                            return EffectResolution.Resolved;
                        }
                        state.ActiveChoice = new ChoiceState
                        {
                            Seat = effect.Seat,
                            SourceInstanceId = effect.SourceInstanceId,
                            SourceCardId = effect.SourceCardId,
                            Timing = effect.Timing,
                            OptionA = "Add the top card of your Life cards to your hand. Then, " + bodyText,
                            OptionB = "Add the bottom card of your Life cards to your hand. Then, " + bodyText,
                        };
                        Log(state, effect.Seat, $"{sourceName}: choose the top or bottom Life card as the cost.");
                        return EffectResolution.Resolved;
                    }

                    // Cost: trash N (matching) cards from your hand.
                    if (ContainsAll(costText, "trash") && ContainsAll(costText, "from your hand"))
                    {
                        if (effect.SelectionsRemaining <= 0)
                        {
                            var nM = System.Text.RegularExpressions.Regex.Match(costText, @"trash (\d+)",
                                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            effect.SelectionsRemaining = nM.Success ? int.Parse(nM.Groups[1].Value) : 1;
                            effect.TargetZone = EffectTargetZone.Hand;
                        }
                        if (string.IsNullOrEmpty(targetId))
                        {
                            Log(state, effect.Seat, $"Click {effect.SelectionsRemaining} card(s) in your hand to trash as the cost of {sourceName}, or skip.");
                            return EffectResolution.WaitingForTarget;
                        }
                        int chIdx = owner.Hand.FindIndex(c => c.InstanceId == targetId);
                        if (chIdx < 0) { Log(state, effect.Seat, "That card is not in your hand."); return EffectResolution.WaitingForTarget; }
                        var costCard = owner.Hand[chIdx];
                        var costDef = GetCard(costCard);
                        if (!CostCardMatches(costText, costDef))
                        {
                            Log(state, effect.Seat, $"{NameId(costDef)} does not match the cost requirement: {costText}.");
                            return EffectResolution.WaitingForTarget;
                        }
                        owner.Hand.RemoveAt(chIdx);
                        costCard.Zone = "trash";
                        owner.Trash.Add(costCard);
                        NotifyHandTrashedByEffect(state, effect.Seat);
                        Log(state, effect.Seat, $"{sourceName} cost: trashed {NameId(costDef)} from hand.");
                        effect.SelectionsRemaining--;
                        if (effect.SelectionsRemaining > 0)
                        {
                            Log(state, effect.Seat, $"Trash {effect.SelectionsRemaining} more card(s) to pay {sourceName}'s cost.");
                            return EffectResolution.WaitingForTarget;
                        }
                        // Cost fully paid — optional "rest this Character" rider, then queue the body.
                        if (ContainsAll(costText, "rest this Character"))
                        {
                            var restSelf = FindAnyInPlay(state, effect.SourceInstanceId, out _);
                            if (restSelf != null) { restSelf.Rested = true; Log(state, effect.Seat, $"{sourceName} rests itself (cost)."); }
                        }
                        QueueBody(state, effect, bodyText);
                        return EffectResolution.Resolved;
                    }

                    // Cost: place 1 card from your trash at the bottom of your deck.
                    if (ContainsAll(costText, "place") && ContainsAll(costText, "from your trash")
                        && ContainsAll(costText, "bottom of your deck"))
                    {
                        effect.TargetZone = EffectTargetZone.Trash;
                        if (string.IsNullOrEmpty(targetId))
                        {
                            Log(state, effect.Seat, $"Click a card in your trash to place at the bottom of your deck for {sourceName}, or skip.");
                            return EffectResolution.WaitingForTarget;
                        }
                        int ctIdx = owner.Trash.FindIndex(c => c.InstanceId == targetId);
                        if (ctIdx < 0) { Log(state, effect.Seat, "That card is not in your trash."); return EffectResolution.WaitingForTarget; }
                        var bottomCard = owner.Trash[ctIdx];
                        owner.Trash.RemoveAt(ctIdx);
                        bottomCard.Zone = "deck";
                        owner.Deck.Add(bottomCard);
                        Log(state, effect.Seat, $"{sourceName} cost: {NameId(GetCard(bottomCard))} placed at the bottom of the deck.");
                        QueueBody(state, effect, bodyText);
                        return EffectResolution.Resolved;
                    }
                }
            }

            // ---- Early multi-clause split: "Do X. Then, do Y." ------------------------------
            // When clause A is independently automatable, split FIRST so a full-text pattern
            // match on clause A can't swallow and silently drop clause B (e.g. OP02-093 Smoker
            // leader's "-1 cost … Then, if there is a Character with a cost of 0, …"). Deck-look
            // texts ("… Then, place the rest / trash the rest") and swap effects keep their
            // "Then," internal — those are handled as a unit by their own resolvers.
            {
                int thenEarly = FindThenClause(text);
                if (thenEarly > 0
                    && !ContainsAll(text, "from the top of your deck")
                    && !(ContainsAll(text, "Return") && ContainsAll(text, "Play") && ContainsAll(text, "from your hand")))
                {
                    string partAEarly = text.Substring(0, thenEarly - 2).Trim();
                    string partBEarly = NormalizeClause(text.Substring(thenEarly));
                    if (IsAutomatedEffectPattern(partAEarly))
                    {
                        var cloneA = ShallowCloneEffect(effect, partAEarly);
                        var resA = TryResolveKnownEffect(state, cloneA, targetId);
                        effect.SelectionsRemaining = cloneA.SelectionsRemaining;
                        if (resA == EffectResolution.WaitingForTarget) return EffectResolution.WaitingForTarget;
                        if (resA == EffectResolution.Resolved)
                        {
                            var chainSrcA = FindCardInstance(state, effect.SourceInstanceId);
                            if (chainSrcA != null)
                                QueueAndAutoResolve(state, effect.Seat, chainSrcA, effect.Timing, partBEarly, IsOptionalEffectText(partBEarly),
                                    effect.Scope, InferTargetZone(partBEarly));
                            return EffectResolution.Resolved;
                        }
                        // clause A NotAutomated despite the pattern check — fall through to full-text handlers.
                    }
                }
            }

            // ---- Power REDUCTION: "Give up to N of your opponent's (Leader or) Characters
            // −N power during this turn." (26+ cards, e.g. OP01-006). Negative TemporaryPowerBonus.
            {
                var pwrRed = System.Text.RegularExpressions.Regex.Match(text,
                    @"[-\u2212\u2013\u2011\u2012\u2014](\d{3,5})\s+power\s+during this (turn|battle)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (pwrRed.Success && ContainsAll(text, "opponent") && ContainsAll(text, "Give"))
                {
                    int reduction = int.Parse(pwrRed.Groups[1].Value);
                    bool redBattle = pwrRed.Groups[2].Value.Equals("battle", StringComparison.OrdinalIgnoreCase) && state.Battle != null;
                    if (effect.SelectionsRemaining <= 0)
                    {
                        var upM2 = System.Text.RegularExpressions.Regex.Match(text, @"[Uu]p to (?:a total of )?(\d+)");
                        effect.SelectionsRemaining = upM2.Success ? int.Parse(upM2.Groups[1].Value) : 1;
                    }
                    bool redAllowLeader = ContainsAll(text, "Leader");
                    var redTarget = FindAnyInPlay(state, targetId, out var redSeat);
                    if (redTarget == null)
                    {
                        Log(state, effect.Seat, $"Choose an opponent's {(redAllowLeader ? "Leader or " : "")}Character to give -{reduction} power ({sourceName}), or skip.");
                        return EffectResolution.WaitingForTarget;
                    }
                    var redDef = GetCard(redTarget);
                    bool redTypeOk = redDef.Type == "character" || (redAllowLeader && redDef.Type == "leader");
                    if (redSeat != OtherSeat(effect.Seat) || !redTypeOk)
                    {
                        Log(state, effect.Seat, "That is not a valid power-reduction target.");
                        return EffectResolution.WaitingForTarget;
                    }
                    if (redBattle)
                    {
                        state.Battle.BattlePowerBonus.TryGetValue(redTarget.InstanceId, out var exR);
                        state.Battle.BattlePowerBonus[redTarget.InstanceId] = exR - reduction;
                        RegisterPowerModifier(redTarget, sourceName, -reduction, "endOfBattle");
                    }
                    else
                    {
                        state.TemporaryPowerBonus.TryGetValue(redTarget.InstanceId, out var exR);
                        state.TemporaryPowerBonus[redTarget.InstanceId] = exR - reduction;
                        RegisterPowerModifier(redTarget, sourceName, -reduction, "endOfTurn");
                    }
                    Log(state, effect.Seat, $"{sourceName} gives {NameId(redDef)} -{reduction} power this {(redBattle ? "battle" : "turn")}.");
                    effect.SelectionsRemaining--;
                    if (effect.SelectionsRemaining > 0)
                    {
                        Log(state, effect.Seat, $"You may choose {effect.SelectionsRemaining} more target(s), or skip.");
                        return EffectResolution.WaitingForTarget;
                    }
                    return EffectResolution.Resolved;
                }
            }

            // ---- "Return up to N Character(s) with a cost of X or less to the owner's hand." --
            // Owner-agnostic bounce (either player's Character; "owner's hand" phrasing, 22+ cards).
            if (ContainsAll(text, "Return") && ContainsAll(text, "to the owner's hand"))
            {
                int bounceCap = ParseLimit(text, @"cost (?:of )?(\d+) or less");
                var bTarget = FindAnyInPlay(state, targetId, out var bSeat);
                if (bTarget == null)
                {
                    Log(state, effect.Seat, $"Choose a Character{(bounceCap >= 0 ? $" (cost ≤ {bounceCap})" : "")} to return to its owner's hand ({sourceName}).");
                    return EffectResolution.WaitingForTarget;
                }
                if (GetCard(bTarget).Type != "character"
                    || (bounceCap >= 0 && GetCost(state, bTarget) > bounceCap)
                    || (ContainsAll(text, "active Character") && bTarget.Rested)
                    || (ContainsAll(text, "rested Character") && !bTarget.Rested))
                {
                    Log(state, effect.Seat, "That is not a valid return-to-hand target.");
                    return EffectResolution.WaitingForTarget;
                }
                ReturnToHand(state, bSeat, bTarget);
                Log(state, effect.Seat, $"{sourceName} returns {NameId(GetCard(bTarget))} to its owner's hand.");
                return EffectResolution.Resolved;
            }

            // ---- "Place up to N Character(s) with a cost of X or less at the bottom of the
            // owner's deck." (16+ cards, e.g. OP01-070) — removal that dodges On-KO effects.
            if (ContainsAll(text, "Place up to") && ContainsAll(text, "bottom of the owner's deck"))
            {
                int sinkCap = ParseLimit(text, @"cost (?:of )?(\d+) or less");
                var sTarget = FindAnyInPlay(state, targetId, out var sSeat);
                if (sTarget == null)
                {
                    Log(state, effect.Seat, $"Choose a Character{(sinkCap >= 0 ? $" (cost ≤ {sinkCap})" : "")} to place at the bottom of its owner's deck ({sourceName}).");
                    return EffectResolution.WaitingForTarget;
                }
                if (GetCard(sTarget).Type != "character" || (sinkCap >= 0 && GetCost(state, sTarget) > sinkCap))
                {
                    Log(state, effect.Seat, "That is not a valid target.");
                    return EffectResolution.WaitingForTarget;
                }
                var sOwner = Player(state, sSeat);
                ReturnAttachedDon(sOwner, sTarget);
                int sIdx = sOwner.CharacterArea.FindIndex(c => c != null && c.InstanceId == sTarget.InstanceId);
                if (sIdx >= 0) sOwner.CharacterArea[sIdx] = null;
                sTarget.Zone = "deck";
                sTarget.Rested = false;
                sTarget.PlayedOnTurn = null;
                sTarget.Modifiers.Clear();
                state.NameOverrides.Remove(sTarget.InstanceId);
                state.ActiveModifiers.RemoveAll(m => m.TargetInstanceId == sTarget.InstanceId);
                sOwner.Deck.Add(sTarget);
                Log(state, effect.Seat, $"{sourceName} places {NameId(GetCard(sTarget))} at the bottom of its owner's deck.");
                return EffectResolution.Resolved;
            }

            // ---- "Set this Leader as active." (standalone — no hand-trash cost) --------------
            if (System.Text.RegularExpressions.Regex.IsMatch(text.Trim(), @"^set this Leader as active\.?$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                if (owner.Leader != null && owner.Leader.Rested && !HasModifier(state, owner.Leader, "freeze"))
                {
                    owner.Leader.Rested = false;
                    Log(state, effect.Seat, $"{sourceName} sets the Leader as active.");
                }
                return EffectResolution.Resolved;
            }

            // ---- "Set this Character as active." (self-unrest, e.g. OP04-027) ----------------
            if (ContainsAll(text, "Set this Character as active") || ContainsAll(text, "set this Character as active"))
            {
                var selfActive = FindAnyInPlay(state, effect.SourceInstanceId, out _);
                if (selfActive != null && selfActive.Rested && !HasModifier(state, selfActive, "freeze"))
                {
                    selfActive.Rested = false;
                    Log(state, effect.Seat, $"{sourceName} sets itself as active.");
                }
                return EffectResolution.Resolved;
            }

            // ---- "Trash N card(s) from the top of your deck." (mill, alternate phrasing) -----
            {
                var millAlt = System.Text.RegularExpressions.Regex.Match(text,
                    @"[Tt]rash (\d+) cards? from the top of your deck");
                if (millAlt.Success)
                {
                    int millAltN = int.Parse(millAlt.Groups[1].Value);
                    int milledAlt = 0;
                    for (int i = 0; i < millAltN && owner.Deck.Count > 0; i++)
                    {
                        var mc = Shift(owner.Deck);
                        mc.Zone = "trash";
                        owner.Trash.Add(mc);
                        milledAlt++;
                    }
                    Log(state, effect.Seat, $"{sourceName} trashes {milledAlt} card(s) from the top of the deck.");
                    return EffectResolution.Resolved;
                }
            }

            // ---- "will not become active in your opponent's next Refresh Phase" -------------
            // (OP08-024 etc.) and "cannot be rested until the end of your opponent's next
            // End Phase / turn" (OP13-032 etc.). Both are untilNextTurn modifiers.
            if ((ContainsAll(text, "will not become active") && ContainsAll(text, "Refresh Phase"))
                || ContainsAll(text, "cannot be rested until"))
            {
                bool isFreeze = ContainsAll(text, "will not become active");
                if (effect.SelectionsRemaining <= 0)
                {
                    var frUp = System.Text.RegularExpressions.Regex.Match(text, @"[Uu]p to (?:a total of )?(\d+)");
                    effect.SelectionsRemaining = frUp.Success ? int.Parse(frUp.Groups[1].Value) : 1;
                }
                int frCap = ParseLimit(text, @"cost (?:of )?(\d+) or less");
                var frTarget = FindAnyInPlay(state, targetId, out var frSeat);
                if (frTarget == null)
                {
                    Log(state, effect.Seat, $"Choose up to {effect.SelectionsRemaining} opponent Character(s){(frCap >= 0 ? $" (cost ≤ {frCap})" : "")} for {sourceName}, or skip.");
                    return EffectResolution.WaitingForTarget;
                }
                bool needRested = isFreeze && ContainsAll(text, "rested Characters");
                if (frSeat != OtherSeat(effect.Seat) || GetCard(frTarget).Type != "character"
                    || (frCap >= 0 && GetCost(state, frTarget) > frCap)
                    || (needRested && !frTarget.Rested))
                {
                    Log(state, effect.Seat, "That is not a valid target.");
                    return EffectResolution.WaitingForTarget;
                }
                AddModifier(state, FindAnyInPlay(state, effect.SourceInstanceId, out _), frTarget,
                    isFreeze ? "freeze" : "cannotBeRested", "untilNextTurn", null, effect.Seat);
                Log(state, effect.Seat, isFreeze
                    ? $"{sourceName}: {NameId(GetCard(frTarget))} will not become active in the next Refresh Phase."
                    : $"{sourceName}: {NameId(GetCard(frTarget))} cannot be rested.");
                effect.SelectionsRemaining--;
                if (effect.SelectionsRemaining > 0)
                {
                    Log(state, effect.Seat, $"You may choose {effect.SelectionsRemaining} more target(s), or skip.");
                    return EffectResolution.WaitingForTarget;
                }
                return EffectResolution.Resolved;
            }

            // ---- "Your opponent returns N DON!! card(s) to their DON!! deck." ---------------
            {
                var oppDonRet = System.Text.RegularExpressions.Regex.Match(text,
                    @"opponent returns (\d+) DON!! cards? (?:from their field )?to their DON!! deck",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (oppDonRet.Success)
                {
                    int retN = Math.Min(int.Parse(oppDonRet.Groups[1].Value),
                        Player(state, OtherSeat(effect.Seat)).CostArea.Count);
                    if (retN > 0) PayDonMinus(state, OtherSeat(effect.Seat), retN);
                    else Log(state, effect.Seat, $"{sourceName}: opponent has no DON!! to return.");
                    return EffectResolution.Resolved;
                }
            }

            // ---- "Up to N of your opponent's Characters … cannot attack …" ------------------
            // (ST19-001 Smoker: "Up to 2 … with a cost of 4 or less cannot attack until the end
            // of your opponent's next turn.") Multi-pick: each click restricts one target; the
            // player may SKIP early since the effect is "up to".
            if (ContainsAll(text, "cannot attack") && ContainsAll(text, "opponent"))
            {
                if (effect.SelectionsRemaining <= 0)
                {
                    var upM = System.Text.RegularExpressions.Regex.Match(text, @"[Uu]p to (?:a total of )?(\d+)");
                    effect.SelectionsRemaining = upM.Success ? int.Parse(upM.Groups[1].Value) : 1;
                }
                int atkCostCap = ParseLimit(text, @"cost (?:of )?(\d+) or less");
                var restrictTarget = FindAnyInPlay(state, targetId, out var restrictSeat);
                if (restrictTarget == null)
                {
                    Log(state, effect.Seat, $"Choose up to {effect.SelectionsRemaining} opponent Character(s){(atkCostCap >= 0 ? $" with cost {atkCostCap} or less" : "")} that cannot attack ({sourceName}), or skip.");
                    return EffectResolution.WaitingForTarget;
                }
                if (restrictSeat != OtherSeat(effect.Seat) || GetCard(restrictTarget).Type != "character"
                    || (atkCostCap >= 0 && GetCost(state, restrictTarget) > atkCostCap))
                {
                    Log(state, effect.Seat, "That is not a valid target for the attack restriction.");
                    return EffectResolution.WaitingForTarget;
                }
                if (HasModifier(state, restrictTarget, "cannotAttack"))
                {
                    Log(state, effect.Seat, $"{NameId(GetCard(restrictTarget))} already cannot attack.");
                    return EffectResolution.WaitingForTarget;
                }
                string atkDuration = ContainsAll(text, "until the end of your opponent's next turn") ? "untilNextTurn" : "thisTurn";
                AddModifier(state, FindAnyInPlay(state, effect.SourceInstanceId, out _), restrictTarget,
                    "cannotAttack", atkDuration, null, effect.Seat);
                Log(state, effect.Seat, $"{sourceName}: {NameId(GetCard(restrictTarget))} cannot attack{(atkDuration == "untilNextTurn" ? " until the end of your opponent's next turn" : " this turn")}.");
                effect.SelectionsRemaining--;
                if (effect.SelectionsRemaining > 0)
                {
                    Log(state, effect.Seat, $"You may choose {effect.SelectionsRemaining} more target(s), or skip.");
                    return EffectResolution.WaitingForTarget;
                }
                return EffectResolution.Resolved;
            }

            // ---- Trash (K.O.-like) an opponent Character by cost --------------------------
            // "trash up to 1 of your opponent's Characters with a cost of 0" (ST19-003 Tashigi).
            if (ContainsAll(text, "trash up to 1", "opponent's Characters"))
            {
                int trCapLess = ParseLimit(text, @"cost (?:of )?(\d+) or less");
                int trCapExact = trCapLess >= 0 ? -1 : ParseLimit(text, @"cost of (\d+)\b(?! or)");
                var trTarget = FindAnyInPlay(state, targetId, out var trSeat);
                if (trTarget == null)
                {
                    Log(state, effect.Seat, $"Choose an opponent's Character to trash for {sourceName}.");
                    return EffectResolution.WaitingForTarget;
                }
                int trCost = GetCost(state, trTarget);
                bool trCostOk = trCapLess >= 0 ? trCost <= trCapLess : (trCapExact < 0 || trCost == trCapExact);
                if (trSeat != OtherSeat(effect.Seat) || GetCard(trTarget).Type != "character" || !trCostOk)
                {
                    Log(state, effect.Seat, "That is not a valid target to trash.");
                    return EffectResolution.WaitingForTarget;
                }
                MoveToTrash(state, trSeat, trTarget.InstanceId);
                Log(state, effect.Seat, $"{sourceName} trashes {NameId(GetCard(trTarget))}.");
                return EffectResolution.Resolved;
            }

            // ---- Board-wide buff: "All of your {X} type Characters gain +N power …" ---------
            // (ST05-001 Shanks leader.) No targeting; applies to every matching own Character.
            if (ContainsAll(text, "All of your") && ContainsAll(text, "gain") && ContainsAll(text, "power")
                && (ContainsAll(text, "during this turn") || ContainsAll(text, "during this battle")))
            {
                var gainM = System.Text.RegularExpressions.Regex.Match(text, @"gain \+(\d+) power",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                int allBonus = gainM.Success ? int.Parse(gainM.Groups[1].Value) : ParsePowerGain(text);
                if (allBonus > 0)
                {
                    bool battleScope = ContainsAll(text, "during this battle") && state.Battle != null;
                    int buffed = 0;
                    foreach (var c in owner.CharacterArea)
                    {
                        if (c == null) continue;
                        if (!CardPassesFeatureFilter(text, GetCard(c))) continue;
                        if (battleScope)
                        {
                            state.Battle.BattlePowerBonus.TryGetValue(c.InstanceId, out var ex);
                            state.Battle.BattlePowerBonus[c.InstanceId] = ex + allBonus;
                            RegisterPowerModifier(c, sourceName, allBonus, "endOfBattle");
                        }
                        else
                        {
                            state.TemporaryPowerBonus.TryGetValue(c.InstanceId, out var ex);
                            state.TemporaryPowerBonus[c.InstanceId] = ex + allBonus;
                            RegisterPowerModifier(c, sourceName, allBonus, "endOfTurn");
                        }
                        buffed++;
                    }
                    Log(state, effect.Seat, $"{sourceName} gives +{allBonus} power to {buffed} Character(s).");
                    return EffectResolution.Resolved;
                }
            }

            // ---- Add DON!! from the DON!! deck ----------------------------------------------
            // "Add up to 1 DON!! card from your DON!! deck and rest it." (ST05-002 Ain)
            // "…add 2 DON!! cards from your DON!! deck and rest them." (ST05-005 Carina body)
            // "Add up to 1 DON!! card from your DON!! deck and set it as active." (ST05-016 trigger)
            if ((ContainsAll(text, "Add") || ContainsAll(text, "add")) && ContainsAll(text, "DON!! card")
                && ContainsAll(text, "from your DON!! deck"))
            {
                var addM = System.Text.RegularExpressions.Regex.Match(text, @"[Aa]dd (?:up to )?(\d+) DON!! card");
                int addN = addM.Success ? int.Parse(addM.Groups[1].Value) : 1;
                bool addRested = ContainsAll(text, "rest it") || ContainsAll(text, "rest them") || ContainsAll(text, "and rest");
                int added = Math.Min(addN, owner.DonDeck);
                owner.DonDeck -= added;
                for (int i = 0; i < added; i++)
                {
                    var don = CreateDonInstance(effect.Seat, owner);
                    don.Rested = addRested;
                    owner.CostArea.Add(don);
                }
                Log(state, effect.Seat, added > 0
                    ? $"{sourceName} adds {added} DON!! from the DON!! deck ({(addRested ? "rested" : "active")})."
                    : $"{sourceName}: the DON!! deck is empty.");
                return EffectResolution.Resolved;
            }


            // ================= Unified generic resolvers (Session 10) =================





            // ---- OP06-086: "Choose up to 1 Character card with a cost of 4 or less and up
            // to 1 Character card with a cost of 2 or less from your trash. Play 1 card and
            // play the other card rested." — first pick plays active (≤4), second rested (≤2).
            if (ContainsAll(text, "from your trash") && ContainsAll(text, "Play 1 card and play the other card rested"))
            {
                effect.TargetZone = EffectTargetZone.Trash;
                if (effect.SelectionsRemaining <= 0) effect.SelectionsRemaining = 2;
                int capNow = effect.SelectionsRemaining == 2
                    ? ParseLimit(text, @"cost of (\d+) or less")                       // first listed cap
                    : ParseLimit(text.Substring(text.IndexOf(" and up to ", StringComparison.OrdinalIgnoreCase) + 1), @"cost of (\d+) or less");
                if (string.IsNullOrEmpty(targetId))
                {
                    Log(state, effect.Seat, $"Click a Character in your trash (cost ≤ {capNow}) — pick {3 - effect.SelectionsRemaining} of 2 ({sourceName}), or skip.");
                    return EffectResolution.WaitingForTarget;
                }
                int c86 = owner.Trash.FindIndex(c => c.InstanceId == targetId);
                if (c86 < 0) { Log(state, effect.Seat, "That card is not in your trash."); return EffectResolution.WaitingForTarget; }
                var t86 = owner.Trash[c86];
                var d86 = GetCard(t86);
                if (d86.Type != "character" || (capNow >= 0 && d86.Cost > capNow))
                {
                    Log(state, effect.Seat, "That is not a valid pick.");
                    return EffectResolution.WaitingForTarget;
                }
                int slot86 = owner.CharacterArea.FindIndex(c => c == null);
                if (slot86 < 0) { Log(state, effect.Seat, "No open character slot."); return EffectResolution.Resolved; }
                owner.Trash.RemoveAt(c86);
                t86.Zone = "character";
                t86.PlayedOnTurn = state.TurnNumber;
                t86.Rested = effect.SelectionsRemaining == 1;   // the second pick comes in rested
                owner.CharacterArea[slot86] = t86;
                Log(state, effect.Seat, $"{sourceName} plays {NameId(d86)} from trash{(t86.Rested ? " rested" : "")}.");
                if (HasTiming(d86.Effect, "On Play"))
                    QueueAndAutoResolve(state, effect.Seat, t86, "onPlay", ExtractTimedClause(d86.Effect, "On Play"), true,
                        EffectScope.Instant, InferTargetZone(d86.Effect));
                effect.SelectionsRemaining--;
                if (effect.SelectionsRemaining > 0) return EffectResolution.WaitingForTarget;
                return EffectResolution.Resolved;
            }

            // ---- OP09-081: "[On Play] effects are negated" auras/effects. -------------------
            // Continuous own-side line ("Your [On Play] effects are negated.") is honored at
            // queue time; the activated form negates the OPPONENT's [On Play] effects.
            if (ContainsAll(text, "opponent's [On Play] effects are negated"))
            {
                var oppNp = Player(state, OtherSeat(effect.Seat));
                var npSrc = FindAnyInPlay(state, effect.SourceInstanceId, out _);
                if (oppNp.Leader != null) AddModifier(state, npSrc, oppNp.Leader, "onPlayNegated", "untilNextTurn", null, effect.Seat);
                foreach (var c in oppNp.CharacterArea)
                    if (c != null) AddModifier(state, npSrc, c, "onPlayNegated", "untilNextTurn", null, effect.Seat);
                Log(state, effect.Seat, $"{sourceName}: the opponent's [On Play] effects are negated until the end of their next turn.");
                return EffectResolution.Resolved;
            }

            // ---- OP15-031: "Select up to 1 of your opponent's rested Characters. If the
            // chosen Character has a cost equal to the number of DON!! cards given to it, K.O. it."
            if (ContainsAll(text, "Select up to 1 of your opponent's rested Characters")
                && ContainsAll(text, "cost equal to the number of DON!! cards given to it"))
            {
                var sr31 = FindAnyInPlay(state, targetId, out var sr31Seat);
                if (sr31 == null)
                {
                    Log(state, effect.Seat, $"Select an opponent's rested Character ({sourceName}).");
                    return EffectResolution.WaitingForTarget;
                }
                if (sr31Seat != OtherSeat(effect.Seat) || GetCard(sr31).Type != "character" || !sr31.Rested)
                {
                    Log(state, effect.Seat, "That is not a valid target.");
                    return EffectResolution.WaitingForTarget;
                }
                if (GetCost(state, sr31) == sr31.AttachedDonIds.Count && !CannotBeKoedByEffect(state, sr31)
                    && !TryRemovalReplacement(state, sr31Seat, sr31))
                {
                    MoveToTrash(state, sr31Seat, sr31.InstanceId);
                    Log(state, effect.Seat, $"{sourceName} K.O.s {NameId(GetCard(sr31))}.");
                }
                else Log(state, effect.Seat, "Condition not met — the Character survives.");
                return EffectResolution.Resolved;
            }

            // ---- "Activate up to N {T} type Event with a base cost of N or less from your
            // hand." — play the event for free (its [Main] resolves). ------------------------
            {
                var aeM = System.Text.RegularExpressions.Regex.Match(text,
                    @"Activate up to (\d+) ([^.]*?)Events?(?: with a (base )?cost of (\d+) or less)? from your hand",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (aeM.Success)
                {
                    effect.TargetZone = EffectTargetZone.Hand;
                    if (string.IsNullOrEmpty(targetId))
                    {
                        Log(state, effect.Seat, $"Click an Event in your hand to activate for free ({sourceName}), or skip.");
                        return EffectResolution.WaitingForTarget;
                    }
                    int aeIdx = owner.Hand.FindIndex(c => c.InstanceId == targetId);
                    if (aeIdx < 0) { Log(state, effect.Seat, "That card is not in your hand."); return EffectResolution.WaitingForTarget; }
                    var aeCard = owner.Hand[aeIdx];
                    var aeDef = GetCard(aeCard);
                    if (aeDef.Type != "event"
                        || (aeM.Groups[4].Success && aeDef.Cost > int.Parse(aeM.Groups[4].Value))
                        || !CardPassesFeatureFilter(aeM.Groups[2].Value, aeDef))
                    {
                        Log(state, effect.Seat, "That Event does not match the requirement.");
                        return EffectResolution.WaitingForTarget;
                    }
                    owner.Hand.RemoveAt(aeIdx);
                    aeCard.Zone = "trash";
                    owner.Trash.Add(aeCard);
                    Log(state, effect.Seat, $"{sourceName} activates {NameId(aeDef)} for free.");
                    if (HasTiming(aeDef.Effect, "Main"))
                    {
                        string aeCl = ExtractTimedClause(aeDef.Effect, "Main");
                        QueueAndAutoResolve(state, effect.Seat, aeCard, "main", aeCl, true, EffectScope.Instant, InferTargetZone(aeCl));
                    }
                    return EffectResolution.Resolved;
                }
            }

            // ---- "Look at N cards from the top of your deck and trash up to N cards.
            // Then, place the rest at the bottom …" (OP03-083) --------------------------------
            if (ContainsAll(text, "Look at") && ContainsAll(text, "from the top of your deck")
                && ContainsAll(text, "trash up to"))
            {
                var ltM = System.Text.RegularExpressions.Regex.Match(text, @"trash up to (\d+)");
                var ltSrc = FindCardInstance(state, effect.SourceInstanceId) ?? owner.Leader;
                if (ltSrc != null)
                {
                    StartDeckLook(state, effect.Seat, ltSrc, "", ParseLookCount(text));
                    state.DeckLook.TrashSelected = true;
                    state.DeckLook.SelectCount = ltM.Success ? int.Parse(ltM.Groups[1].Value) : 1;
                }
                return EffectResolution.Resolved;
            }

            // ---- "Look at N cards from the top of your deck and place them at the top of
            // your deck in any order." (ST17-003) ---------------------------------------------
            if (ContainsAll(text, "Look at") && ContainsAll(text, "place them at the top of your deck"))
            {
                var ttSrc = FindCardInstance(state, effect.SourceInstanceId) ?? owner.Leader;
                if (ttSrc != null)
                {
                    StartDeckLook(state, effect.Seat, ttSrc, "", ParseLookCount(text));
                    state.DeckLook.Step = "rearrange";
                    state.DeckLook.ToTop = true;
                }
                return EffectResolution.Resolved;
            }


            // ---- Fuzz round-2 gap fills -----------------------------------------------------
            {
                // "your opponent adds N card from the top of their Life cards to their hand."
                var oal = System.Text.RegularExpressions.Regex.Match(text,
                    @"opponent adds (\d+) cards? from (?:the top of )?their Life (?:cards?|area) to their hand",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (oal.Success)
                {
                    var oppOal = Player(state, OtherSeat(effect.Seat));
                    int oalN = int.Parse(oal.Groups[1].Value);
                    int oalDone = 0;
                    for (int i = 0; i < oalN && oppOal.Life.Count > 0; i++)
                    {
                        var lcO = Pop(oppOal.Life);
                        lcO.Zone = "hand";
                        oppOal.Hand.Add(lcO);
                        oalDone++;
                    }
                    Log(state, effect.Seat, $"{sourceName}: opponent adds {oalDone} Life card(s) to hand.");
                    return EffectResolution.Resolved;
                }
                // "your opponent draws N cards."
                var od = System.Text.RegularExpressions.Regex.Match(text,
                    @"opponent draws (\d+) cards?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (od.Success)
                {
                    for (int i = 0; i < int.Parse(od.Groups[1].Value); i++) DrawCard(state, OtherSeat(effect.Seat), true);
                    Log(state, effect.Seat, $"{sourceName}: opponent draws {od.Groups[1].Value} card(s).");
                    return EffectResolution.Resolved;
                }
                // "trash all cards from your hand."
                if (ContainsAll(text, "trash all cards from your hand"))
                {
                    int ta = owner.Hand.Count;
                    foreach (var hcT in owner.Hand) { hcT.Zone = "trash"; owner.Trash.Add(hcT); }
                    owner.Hand.Clear();
                    if (ta > 0) NotifyHandTrashedByEffect(state, effect.Seat);
                    Log(state, effect.Seat, $"{sourceName}: trashed all {ta} card(s) from hand.");
                    return EffectResolution.Resolved;
                }
                // "Trash up to N cards from your hand." (optional multi-discard)
                var tuo = System.Text.RegularExpressions.Regex.Match(text,
                    @"^[Tt]rash up to (\d+) cards? from your hand\.?$");
                if (tuo.Success)
                {
                    if (effect.SelectionsRemaining <= 0)
                    {
                        effect.SelectionsRemaining = int.Parse(tuo.Groups[1].Value);
                        effect.TargetZone = EffectTargetZone.Hand;
                    }
                    if (string.IsNullOrEmpty(targetId))
                    {
                        Log(state, effect.Seat, $"Click up to {effect.SelectionsRemaining} card(s) in your hand to trash ({sourceName}), or skip.");
                        return EffectResolution.WaitingForTarget;
                    }
                    int tuoIdx = owner.Hand.FindIndex(c => c.InstanceId == targetId);
                    if (tuoIdx < 0) { Log(state, effect.Seat, "That card is not in your hand."); return EffectResolution.WaitingForTarget; }
                    var tuoCard = owner.Hand[tuoIdx];
                    owner.Hand.RemoveAt(tuoIdx);
                    tuoCard.Zone = "trash";
                    owner.Trash.Add(tuoCard);
                    NotifyHandTrashedByEffect(state, effect.Seat);
                    effect.SelectionsRemaining--;
                    if (effect.SelectionsRemaining > 0) return EffectResolution.WaitingForTarget;
                    return EffectResolution.Resolved;
                }
                // "trash cards from the top of your Life cards until you have N Life card(s)."
                var lifeDown = System.Text.RegularExpressions.Regex.Match(text,
                    @"trash cards from the top of your Life cards until you have (\d+) Life",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (lifeDown.Success)
                {
                    int keepL = int.Parse(lifeDown.Groups[1].Value);
                    int cut = 0;
                    while (owner.Life.Count > keepL)
                    {
                        var lcCut = Pop(owner.Life);
                        lcCut.Zone = "trash";
                        owner.Trash.Add(lcCut);
                        cut++;
                    }
                    Log(state, effect.Seat, $"{sourceName}: trashed {cut} Life card(s) — {owner.Life.Count} Life left.");
                    return EffectResolution.Resolved;
                }
                // "None of your Characters can be K.O.'d by your opponent's effects until …"
                if (ContainsAll(text, "None of your Characters can be K.O.'d by your opponent's effects"))
                {
                    string noneDur = ContainsAll(text, "until the") ? "untilNextTurn" : "thisTurn";
                    int noneC = 0;
                    var noneSrc = FindAnyInPlay(state, effect.SourceInstanceId, out _);
                    foreach (var c in owner.CharacterArea)
                        if (c != null) { AddModifier(state, noneSrc, c, "cannotBeKod", noneDur, null, effect.Seat); noneC++; }
                    Log(state, effect.Seat, $"{sourceName}: {noneC} Character(s) cannot be K.O.'d by opponent effects.");
                    return EffectResolution.Resolved;
                }
                // "All of your Characters with N base power or less cannot be K.O.'d …" (queued form)
                var bpImmM = System.Text.RegularExpressions.Regex.Match(text,
                    @"All of your Characters with (\d{3,5}) base power or less cannot be K\.O\.'d",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (bpImmM.Success)
                {
                    int bpImm = int.Parse(bpImmM.Groups[1].Value);
                    string immDur = ContainsAll(text, "until the") ? "untilNextTurn" : "thisTurn";
                    int immC = 0;
                    var immSrc = FindAnyInPlay(state, effect.SourceInstanceId, out _);
                    foreach (var c in owner.CharacterArea)
                        if (c != null && GetCard(c).Power <= bpImm)
                        { AddModifier(state, immSrc, c, "cannotBeKod", immDur, null, effect.Seat); immC++; }
                    Log(state, effect.Seat, $"{sourceName}: {immC} Character(s) protected.");
                    return EffectResolution.Resolved;
                }
                // "rest this Character." / "trash this Character at the end of this turn."
                if (System.Text.RegularExpressions.Regex.IsMatch(text, @"^rest this Character\.?$",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    var rSelf3 = FindAnyInPlay(state, effect.SourceInstanceId, out _);
                    if (rSelf3 != null) { rSelf3.Rested = true; Log(state, effect.Seat, $"{sourceName} rests itself."); }
                    return EffectResolution.Resolved;
                }
                if (ContainsAll(text, "trash this Character at the end of this turn"))
                {
                    var tEnd = FindAnyInPlay(state, effect.SourceInstanceId, out _);
                    if (tEnd != null)
                    {
                        AddModifier(state, tEnd, tEnd, "trashAtEndOfTurn", "thisTurn", null, effect.Seat);
                        Log(state, effect.Seat, $"{sourceName} will be trashed at the end of this turn.");
                    }
                    return EffectResolution.Resolved;
                }
                // "K.O. N of your ({T} type )Characters." (own sacrifice, mandatory pick)
                var koOwn = System.Text.RegularExpressions.Regex.Match(text,
                    @"^K\.O\. (\d+) of your ([^.]*?)Characters",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (koOwn.Success)
                {
                    if (effect.SelectionsRemaining <= 0) effect.SelectionsRemaining = int.Parse(koOwn.Groups[1].Value);
                    var koOwnT = FindAnyInPlay(state, targetId, out var koOwnSeat);
                    if (koOwnT == null)
                    {
                        Log(state, effect.Seat, $"Choose {effect.SelectionsRemaining} of your Character(s) to K.O. ({sourceName}).");
                        return EffectResolution.WaitingForTarget;
                    }
                    if (koOwnSeat != effect.Seat || GetCard(koOwnT).Type != "character"
                        || !CardPassesFeatureFilter(koOwn.Groups[2].Value, GetCard(koOwnT)))
                    {
                        Log(state, effect.Seat, "That is not a valid target.");
                        return EffectResolution.WaitingForTarget;
                    }
                    MoveToTrash(state, effect.Seat, koOwnT.InstanceId);
                    effect.SelectionsRemaining--;
                    if (effect.SelectionsRemaining > 0) return EffectResolution.WaitingForTarget;
                    return EffectResolution.Resolved;
                }
                // "shuffle your deck." leftover after searches (already shuffled by the search).
                if (System.Text.RegularExpressions.Regex.IsMatch(text, @"^(Then, )?shuffle your deck\.?$",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    return EffectResolution.Resolved;
                // "you may place up to N card(s) from your opponent's trash at the bottom of their deck."
                var oppTrBot = System.Text.RegularExpressions.Regex.Match(text,
                    @"place up to (\d+) cards? from your opponent's trash at the bottom of their deck",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (oppTrBot.Success)
                {
                    var oppTb = Player(state, OtherSeat(effect.Seat));
                    int tbN = int.Parse(oppTrBot.Groups[1].Value);
                    int tbDone = 0;
                    for (int i = 0; i < tbN && oppTb.Trash.Count > 0; i++)
                    {
                        var tbC = oppTb.Trash[oppTb.Trash.Count - 1];
                        oppTb.Trash.RemoveAt(oppTb.Trash.Count - 1);
                        tbC.Zone = "deck";
                        oppTb.Deck.Add(tbC);
                        tbDone++;
                    }
                    Log(state, effect.Seat, $"{sourceName}: {tbDone} card(s) from the opponent's trash placed at the bottom of their deck.");
                    return EffectResolution.Resolved;
                }
                // "(all of your …|up to N of your …) cards' base power becomes N during this turn."
                var bpOwnAll = System.Text.RegularExpressions.Regex.Match(text,
                    @"all of your \[([^\]]+)\] cards' base power becomes (\d{3,6})",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (bpOwnAll.Success)
                {
                    int bpVal2 = int.Parse(bpOwnAll.Groups[2].Value);
                    int bpC2 = 0;
                    var bpTargets = new List<CardInstance>();
                    if (owner.Leader != null) bpTargets.Add(owner.Leader);
                    foreach (var c in owner.CharacterArea) if (c != null) bpTargets.Add(c);
                    foreach (var c in bpTargets)
                    {
                        if (!NameMatches(state, c, bpOwnAll.Groups[1].Value.Trim())) continue;
                        state.BasePowerOverrides.Add(new BasePowerOverride { TargetInstanceId = c.InstanceId, Value = bpVal2, OwnerSeat = effect.Seat, Duration = "thisTurn" });
                        bpC2++;
                    }
                    Log(state, effect.Seat, $"{sourceName}: {bpC2} card(s) base power becomes {bpVal2} this turn.");
                    return EffectResolution.Resolved;
                }
                var bpOwnPick = System.Text.RegularExpressions.Regex.Match(text,
                    @"up to (\d+) of your Leader or Character cards' base power becomes (\d{3,6})",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (bpOwnPick.Success)
                {
                    if (effect.SelectionsRemaining <= 0) effect.SelectionsRemaining = int.Parse(bpOwnPick.Groups[1].Value);
                    var bpT2 = FindAnyInPlay(state, targetId, out var bpSeat3);
                    if (bpT2 == null)
                    {
                        Log(state, effect.Seat, $"Choose up to {effect.SelectionsRemaining} of your card(s) ({sourceName}), or skip.");
                        return EffectResolution.WaitingForTarget;
                    }
                    if (bpSeat3 != effect.Seat) { Log(state, effect.Seat, "That is not a valid target."); return EffectResolution.WaitingForTarget; }
                    state.BasePowerOverrides.Add(new BasePowerOverride { TargetInstanceId = bpT2.InstanceId, Value = int.Parse(bpOwnPick.Groups[2].Value), OwnerSeat = effect.Seat, Duration = "thisTurn" });
                    Log(state, effect.Seat, $"{sourceName}: {NameId(GetCard(bpT2))}'s base power becomes {bpOwnPick.Groups[2].Value} this turn.");
                    effect.SelectionsRemaining--;
                    if (effect.SelectionsRemaining > 0) return EffectResolution.WaitingForTarget;
                    return EffectResolution.Resolved;
                }
                // Named restrictions with no automatable board mutation — acknowledged.
                if (ContainsAll(text, "cannot play any Character cards")
                    || ContainsAll(text, "you cannot set DON!! cards as active using Character effects")
                    || System.Text.RegularExpressions.Regex.IsMatch(text, @"you cannot play Character cards( with a base cost of \d+ or more)? during this turn", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                    || ContainsAll(text, "return DON!! cards from your field to your DON!! deck until you have the same number")
                    || ContainsAll(text, "that card gains an additional"))
                {
                    Log(state, effect.Seat, $"{sourceName}: noted — {Truncate(text, 100)} (manual enforcement).");
                    return EffectResolution.Resolved;
                }
                // "from your hand or trash" dual-zone plays — the player may pick the card
                // from EITHER zone (click a hand card, or open the trash and click there).
                if (ContainsAll(text, "from your hand or trash") && ContainsAll(text, "play up to"))
                {
                    effect.TargetZone = EffectTargetZone.Any;
                    int dzCap = ParseLimit(text, @"cost of (\d+) or less");
                    string dzTag = ParseCurlyBraceTag(text);
                    if (!string.IsNullOrEmpty(targetId))
                    {
                        int dzH = owner.Hand.FindIndex(c => c.InstanceId == targetId);
                        int dzT = dzH < 0 ? owner.Trash.FindIndex(c => c.InstanceId == targetId) : -1;
                        if (dzH >= 0 || dzT >= 0)
                        {
                            var dzCard = dzH >= 0 ? owner.Hand[dzH] : owner.Trash[dzT];
                            var dzDef = GetCard(dzCard);
                            int dzSlot = owner.CharacterArea.FindIndex(c => c == null);
                            var dzOt = System.Text.RegularExpressions.Regex.Match(text, @"other than \[([^\]]+)\]",
                                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            bool dzOk = dzDef.Type == "character"
                                && (dzCap < 0 || dzDef.Cost <= dzCap)
                                && (string.IsNullOrEmpty(dzTag) || dzDef.HasFeature(dzTag))
                                && !(dzOt.Success && NameMatches(state, dzCard, dzOt.Groups[1].Value.Trim()))
                                && dzSlot >= 0;
                            if (dzOk)
                            {
                                if (dzH >= 0) owner.Hand.RemoveAt(dzH); else owner.Trash.RemoveAt(dzT);
                                dzCard.Zone = "character";
                                dzCard.Rested = ContainsAll(text, "rested");
                                dzCard.PlayedOnTurn = state.TurnNumber;
                                owner.CharacterArea[dzSlot] = dzCard;
                                Log(state, effect.Seat, $"{sourceName}: plays {NameId(dzDef)} from {(dzH >= 0 ? "hand" : "trash")}.");
                                return EffectResolution.Resolved;
                            }
                        }
                        return EffectResolution.WaitingForTarget;
                    }
                    if (owner.Hand.Count == 0 && owner.Trash.Count == 0) return EffectResolution.Resolved;
                    Log(state, effect.Seat, $"{sourceName}: click a card in your hand or trash to play.");
                    return EffectResolution.WaitingForTarget;
                }
            }

            // ---- DON!! give family (Session 12) --------------------------------------------
            {
                // "Give this Character up to N rested DON!! cards." (self-attach)
                var gdSelf = System.Text.RegularExpressions.Regex.Match(text,
                    @"Give this Character up to (\d+) rested DON!! cards?",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (gdSelf.Success)
                {
                    var gdsT = FindAnyInPlay(state, effect.SourceInstanceId, out _);
                    if (gdsT != null)
                    {
                        var gdsDon = owner.CostArea.Where(d => d.Rested).Take(int.Parse(gdSelf.Groups[1].Value)).ToList();
                        foreach (var d in gdsDon) { owner.CostArea.Remove(d); gdsT.AttachedDonIds.Add(d.InstanceId); }
                        Log(state, effect.Seat, $"{sourceName} attaches {gdsDon.Count} rested DON!! to itself.");
                    }
                    return EffectResolution.Resolved;
                }
                // "Give up to N of your {T} (or {T}) type Characters up to M rested DON!! card each."
                var gdEachN = System.Text.RegularExpressions.Regex.Match(text,
                    @"Give up to (\d+) of your [^.]*?Characters[^.]*? up to (\d+) rested DON!! cards? each",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (gdEachN.Success)
                {
                    if (effect.SelectionsRemaining <= 0) effect.SelectionsRemaining = int.Parse(gdEachN.Groups[1].Value);
                    int perEach = int.Parse(gdEachN.Groups[2].Value);
                    var geT = FindAnyInPlay(state, targetId, out var geSeat);
                    if (geT == null)
                    {
                        Log(state, effect.Seat, $"Choose up to {effect.SelectionsRemaining} Character(s) to receive {perEach} rested DON!! each ({sourceName}), or skip.");
                        return EffectResolution.WaitingForTarget;
                    }
                    if (geSeat != effect.Seat || GetCard(geT).Type != "character" || !CardPassesFeatureFilter(text, GetCard(geT)))
                    {
                        Log(state, effect.Seat, "That is not a valid target.");
                        return EffectResolution.WaitingForTarget;
                    }
                    var geDon = owner.CostArea.Where(d => d.Rested).Take(perEach).ToList();
                    foreach (var d in geDon) { owner.CostArea.Remove(d); geT.AttachedDonIds.Add(d.InstanceId); }
                    Log(state, effect.Seat, $"{sourceName} attaches {geDon.Count} rested DON!! to {NameId(GetCard(geT))}.");
                    effect.SelectionsRemaining--;
                    if (effect.SelectionsRemaining > 0 && owner.CostArea.Any(d => d.Rested)) return EffectResolution.WaitingForTarget;
                    return EffectResolution.Resolved;
                }
                // "Give up to N (total )of your currently given DON!! cards to N of your (…)
                // Characters." — move already-attached DON onto a chosen Character.
                var gdMove = System.Text.RegularExpressions.Regex.Match(text,
                    @"Give up to (\d+) (?:total )?of your currently given DON!! cards to",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (gdMove.Success)
                {
                    int mvN = int.Parse(gdMove.Groups[1].Value);
                    var mvT = FindAnyInPlay(state, targetId, out var mvSeat);
                    if (mvT == null)
                    {
                        Log(state, effect.Seat, $"Choose one of your Characters to move up to {mvN} given DON!! onto ({sourceName}).");
                        return EffectResolution.WaitingForTarget;
                    }
                    if (mvSeat != effect.Seat || GetCard(mvT).Type != "character" || !CardPassesFeatureFilter(text, GetCard(mvT)))
                    {
                        Log(state, effect.Seat, "That is not a valid target.");
                        return EffectResolution.WaitingForTarget;
                    }
                    int movedDon = 0;
                    var donors = new List<CardInstance>();
                    if (owner.Leader != null) donors.Add(owner.Leader);
                    foreach (var c in owner.CharacterArea) if (c != null && c.InstanceId != mvT.InstanceId) donors.Add(c);
                    foreach (var donor in donors)
                    {
                        while (movedDon < mvN && donor.AttachedDonIds.Count > 0)
                        {
                            var dId = donor.AttachedDonIds[donor.AttachedDonIds.Count - 1];
                            donor.AttachedDonIds.RemoveAt(donor.AttachedDonIds.Count - 1);
                            mvT.AttachedDonIds.Add(dId);
                            movedDon++;
                        }
                    }
                    Log(state, effect.Seat, $"{sourceName} moves {movedDon} given DON!! to {NameId(GetCard(mvT))}.");
                    return EffectResolution.Resolved;
                }
            }

            // ---- Hand cost reductions ------------------------------------------------------
            {
                var handCostM = System.Text.RegularExpressions.Regex.Match(text,
                    @"Give (\w+) (Events?|Characters?|cards) in your hand [-\u2212\u2013\u2011\u2012\u2014](\d+) cost",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (handCostM.Success)
                {
                    string colH = handCostM.Groups[1].Value.ToLowerInvariant();
                    bool evOnly = handCostM.Groups[2].Value.StartsWith("Event", StringComparison.OrdinalIgnoreCase);
                    bool chOnly = handCostM.Groups[2].Value.StartsWith("Character", StringComparison.OrdinalIgnoreCase);
                    int hcD = int.Parse(handCostM.Groups[3].Value);
                    int hcC = 0;
                    foreach (var hc3 in owner.Hand)
                    {
                        var hd3 = GetCard(hc3);
                        if (evOnly && hd3.Type != "event") continue;
                        if (chOnly && hd3.Type != "character") continue;
                        if ("red green blue purple black yellow".Contains(colH)
                            && (hd3.Color ?? "").IndexOf(colH, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        RegisterCostModifier(hc3, sourceName, -hcD, "endOfTurn");
                        hcC++;
                    }
                    Log(state, effect.Seat, $"{sourceName}: {hcC} card(s) in hand get -{hcD} cost this turn.");
                    return EffectResolution.Resolved;
                }
                var nextPlay = System.Text.RegularExpressions.Regex.Match(text,
                    @"next time you play (?:\[([^\]]+)\]|an? \{([^}]+)\} type Character card)[^.]*cost will be reduced by (\d+)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (nextPlay.Success)
                {
                    int npD = int.Parse(nextPlay.Groups[3].Value);
                    int npMin = ParseLimit(text, @"cost of (\d+) or more");
                    int npC = 0;
                    foreach (var hc4 in owner.Hand)
                    {
                        if (nextPlay.Groups[1].Success && !NameMatches(state, hc4, nextPlay.Groups[1].Value.Trim())) continue;
                        if (nextPlay.Groups[2].Success && !GetCard(hc4).HasFeature(nextPlay.Groups[2].Value.Trim())) continue;
                        if (npMin >= 0 && GetCard(hc4).Cost < npMin) continue;
                        RegisterCostModifier(hc4, sourceName, -npD, "endOfTurn");
                        npC++;
                    }
                    Log(state, effect.Seat, $"{sourceName}: {npC} matching card(s) in hand get -{npD} cost this turn (approximates the next-play discount).");
                    return EffectResolution.Resolved;
                }
                if (ContainsAll(text, "cost of playing") && ContainsAll(text, "will be reduced by"))
                {
                    Log(state, effect.Seat, $"{sourceName}: play-cost reduction aura is active.");
                    return EffectResolution.Resolved;
                }
            }

            // ---- Bottom-of-deck family -----------------------------------------------------
            {
                if (ContainsAll(text, "Return up to") && ContainsAll(text, "bottom of the owner's deck"))
                {
                    var rbClone = ShallowCloneEffect(effect, text.Replace("Return up to", "Place up to"));
                    rbClone.SelectionsRemaining = effect.SelectionsRemaining;
                    var rbRes = TryResolveKnownEffect(state, rbClone, targetId);
                    effect.SelectionsRemaining = rbClone.SelectionsRemaining;
                    return rbRes;
                }
                var wipeM = System.Text.RegularExpressions.Regex.Match(text,
                    @"Place all Characters with a cost of (\d+) or less at the bottom of the owner's deck",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (wipeM.Success)
                {
                    int wCap = int.Parse(wipeM.Groups[1].Value);
                    int wC = 0;
                    foreach (var st2 in Seats())
                    {
                        var pw = Player(state, st2);
                        for (int i = 0; i < pw.CharacterArea.Count; i++)
                        {
                            var cw = pw.CharacterArea[i];
                            if (cw == null || GetCost(state, cw) > wCap) continue;
                            if (TryRemovalReplacement(state, st2, cw)) continue;
                            ReturnAttachedDon(pw, cw);
                            pw.CharacterArea[i] = null;
                            cw.Zone = "deck"; cw.Rested = false; cw.PlayedOnTurn = null; cw.Modifiers.Clear();
                            state.NameOverrides.Remove(cw.InstanceId);
                            pw.Deck.Add(cw);
                            wC++;
                        }
                    }
                    Log(state, effect.Seat, $"{sourceName}: {wC} Character(s) placed at the bottom of their owners' decks.");
                    return EffectResolution.Resolved;
                }
                if (ContainsAll(text, "Place all of your Characters except this Character at the bottom of your deck"))
                {
                    int sC = 0;
                    for (int i = 0; i < owner.CharacterArea.Count; i++)
                    {
                        var cs = owner.CharacterArea[i];
                        if (cs == null || cs.InstanceId == effect.SourceInstanceId) continue;
                        ReturnAttachedDon(owner, cs);
                        owner.CharacterArea[i] = null;
                        cs.Zone = "deck"; cs.Rested = false; cs.PlayedOnTurn = null; cs.Modifiers.Clear();
                        state.NameOverrides.Remove(cs.InstanceId);
                        owner.Deck.Add(cs);
                        sC++;
                    }
                    Log(state, effect.Seat, $"{sourceName}: {sC} of your Characters placed at the bottom of the deck.");
                    return EffectResolution.Resolved;
                }
            }

            // ---- Benign leftovers & pure restrictions -------------------------------------
            // "Then, place the rest at the bottom of your deck …" leftovers after a deck-look
            // (the DeckLook flow already handles rest placement).
            if (System.Text.RegularExpressions.Regex.IsMatch(text.TrimStart(),
                    @"^(place the rest at the (bottom|top or bottom)|trash the rest)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                return EffectResolution.Resolved;
            // Named self-restrictions with no board mutation — acknowledged in the log.
            if (ContainsAll(text, "cannot add Life cards to your hand using your own effects")
                || ContainsAll(text, "you cannot play cards from your hand during this turn")
                || ContainsAll(text, "you cannot attack a Leader during this turn"))
            {
                Log(state, effect.Seat, $"{sourceName}: restriction noted — {text}");
                return EffectResolution.Resolved;
            }
            // Bare conditional keyword grant body: "this Character gains [KW]."
            {
                var bareKw = System.Text.RegularExpressions.Regex.Match(text,
                    @"^this Character gains \[([A-Za-z !]+)\]\.?$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (bareKw.Success)
                {
                    var bkSelf = FindAnyInPlay(state, effect.SourceInstanceId, out _);
                    if (bkSelf != null)
                    {
                        AddModifier(state, bkSelf, bkSelf, "keyword", "thisTurn", bareKw.Groups[1].Value.Trim(), effect.Seat);
                        Log(state, effect.Seat, $"{sourceName} gains [{bareKw.Groups[1].Value.Trim()}] this turn.");
                    }
                    return EffectResolution.Resolved;
                }
            }
            // Hand→Life with a reveal: "Reveal up to 1 Character card with a cost of N from
            // your hand and add it to the top of your Life cards."
            if (ContainsAll(text, "Reveal up to") && ContainsAll(text, "from your hand")
                && ContainsAll(text, "Life cards"))
            {
                var rlClone = ShallowCloneEffect(effect, text.Replace("Reveal up to", "Add up to"));
                rlClone.SelectionsRemaining = effect.SelectionsRemaining;
                var rlRes = TryResolveKnownEffect(state, rlClone, targetId);
                effect.SelectionsRemaining = rlClone.SelectionsRemaining;
                effect.TargetZone = rlClone.TargetZone;
                return rlRes;
            }

            // "Change the target of that attack to this Leader or to one of your {T} type
            // Character cards." (OP09-093 Teach — [On Your Opponent's Attack] redirect.)
            if (ContainsAll(text, "Change the target of that attack") && state.Battle != null)
            {
                var rt2 = FindAnyInPlay(state, targetId, out var rt2Seat);
                if (rt2 == null)
                {
                    Log(state, effect.Seat, $"Click your Leader or a matching Character to redirect the attack to ({sourceName}).");
                    return EffectResolution.WaitingForTarget;
                }
                var rt2Def = GetCard(rt2);
                bool rt2Ok = rt2Seat == effect.Seat
                    && (rt2Def.Type == "leader" || (rt2Def.Type == "character" && CardPassesFeatureFilter(text, rt2Def)));
                if (!rt2Ok)
                {
                    Log(state, effect.Seat, "That is not a valid redirect target.");
                    return EffectResolution.WaitingForTarget;
                }
                state.Battle.TargetId = rt2.InstanceId;
                state.Battle.TargetSeat = rt2Seat;
                state.Battle.DefensePower = GetPower(state, rt2) + state.Battle.CounterPower;
                Log(state, effect.Seat, $"{sourceName}: the attack target is now {NameId(rt2Def)}.");
                return EffectResolution.Resolved;
            }

            // "Your opponent places 1 of their Characters at the bottom of the owner's deck."
            if (ContainsAll(text, "opponent places") && ContainsAll(text, "of their Characters at the bottom"))
            {
                var oppPl = Player(state, OtherSeat(effect.Seat));
                for (int i = oppPl.CharacterArea.Count - 1; i >= 0; i--)
                {
                    var cp2 = oppPl.CharacterArea[i];
                    if (cp2 == null) continue;
                    ReturnAttachedDon(oppPl, cp2);
                    oppPl.CharacterArea[i] = null;
                    cp2.Zone = "deck"; cp2.Rested = false; cp2.PlayedOnTurn = null; cp2.Modifiers.Clear();
                    state.NameOverrides.Remove(cp2.InstanceId);
                    oppPl.Deck.Add(cp2);
                    Log(state, effect.Seat, $"{sourceName}: opponent places {NameId(GetCard(cp2))} at the bottom of their deck.");
                    break;
                }
                return EffectResolution.Resolved;
            }

            // "K.O. all of your opponent's Characters with N power or less."
            {
                var koAllP = System.Text.RegularExpressions.Regex.Match(text,
                    @"K\.O\. all of your opponent's Characters with (\d{1,5}) power or less",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (koAllP.Success)
                {
                    int kap = int.Parse(koAllP.Groups[1].Value);
                    var oppKa = Player(state, OtherSeat(effect.Seat));
                    int kaC2 = 0;
                    for (int i = 0; i < oppKa.CharacterArea.Count; i++)
                    {
                        var ck2 = oppKa.CharacterArea[i];
                        if (ck2 == null || GetPower(state, ck2) > kap) continue;
                        if (CannotBeKoedByEffect(state, ck2)) continue;
                        if (TryRemovalReplacement(state, OtherSeat(effect.Seat), ck2)) continue;
                        MoveToTrash(state, OtherSeat(effect.Seat), ck2.InstanceId);
                        kaC2++;
                    }
                    Log(state, effect.Seat, $"{sourceName} K.O.s {kaC2} Character(s).");
                    return EffectResolution.Resolved;
                }
            }

            // "place any number of Character cards with a cost of N or more from your trash
            // at the bottom of your deck … gains +N power … for every 3 cards placed."
            if (ContainsAll(text, "place any number of Character cards") && ContainsAll(text, "from your trash"))
            {
                effect.TargetZone = EffectTargetZone.Trash;
                if (effect.RemainingBudget < 0) effect.RemainingBudget = 0;   // counts cards placed
                int minC3 = ParseLimit(text, @"cost of (\d+) or more");
                if (string.IsNullOrEmpty(targetId))
                {
                    Log(state, effect.Seat, $"Click trash Characters to place at the bottom of your deck ({sourceName}); skip when done.");
                    return EffectResolution.WaitingForTarget;
                }
                int anIdx = owner.Trash.FindIndex(c => c.InstanceId == targetId);
                if (anIdx < 0) { Log(state, effect.Seat, "That card is not in your trash."); return EffectResolution.WaitingForTarget; }
                var anCard = owner.Trash[anIdx];
                if (GetCard(anCard).Type != "character" || (minC3 >= 0 && GetCard(anCard).Cost < minC3))
                {
                    Log(state, effect.Seat, "That card does not qualify.");
                    return EffectResolution.WaitingForTarget;
                }
                owner.Trash.RemoveAt(anIdx);
                anCard.Zone = "deck";
                owner.Deck.Add(anCard);
                effect.RemainingBudget++;
                var per3 = System.Text.RegularExpressions.Regex.Match(text, @"\+(\d{3,5}) power[^.]*every (\d+) cards");
                if (per3.Success && effect.RemainingBudget % int.Parse(per3.Groups[2].Value) == 0)
                {
                    var selfAn = FindAnyInPlay(state, effect.SourceInstanceId, out _);
                    if (selfAn != null)
                    {
                        int anB = int.Parse(per3.Groups[1].Value);
                        state.TemporaryPowerBonus.TryGetValue(selfAn.InstanceId, out var exAn);
                        state.TemporaryPowerBonus[selfAn.InstanceId] = exAn + anB;
                        RegisterPowerModifier(selfAn, sourceName, anB, "endOfTurn");
                        Log(state, effect.Seat, $"{sourceName} gains +{anB} power.");
                    }
                }
                return EffectResolution.WaitingForTarget;   // keep placing until skip
            }

            // "place this Character at the bottom of the owner's deck." (self-removal)
            if (ContainsAll(text, "place this Character at the bottom of the owner's deck"))
            {
                var selfBot = FindAnyInPlay(state, effect.SourceInstanceId, out var sbSeat);
                if (selfBot != null)
                {
                    var sbP = Player(state, sbSeat);
                    ReturnAttachedDon(sbP, selfBot);
                    int sbI = sbP.CharacterArea.FindIndex(c => c != null && c.InstanceId == selfBot.InstanceId);
                    if (sbI >= 0) sbP.CharacterArea[sbI] = null;
                    selfBot.Zone = "deck"; selfBot.Rested = false; selfBot.PlayedOnTurn = null; selfBot.Modifiers.Clear();
                    state.NameOverrides.Remove(selfBot.InstanceId);
                    sbP.Deck.Add(selfBot);
                    Log(state, effect.Seat, $"{sourceName} goes to the bottom of the owner's deck.");
                }
                return EffectResolution.Resolved;
            }

            // "add 1 card from the top or bottom of your Life cards to your hand." (as a BODY,
            // not a cost — same top/bottom choice modal.)
            if (System.Text.RegularExpressions.Regex.IsMatch(text.Trim(),
                    @"^add \d+ cards? from the top or bottom of your Life cards? to your hand\.?$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                if (owner.Life.Count == 0) return EffectResolution.Resolved;
                state.ActiveChoice = new ChoiceState
                {
                    Seat = effect.Seat,
                    ControllerSeat = effect.Seat,
                    SourceInstanceId = effect.SourceInstanceId,
                    SourceCardId = effect.SourceCardId,
                    Timing = effect.Timing,
                    OptionA = "Add the top card of your Life cards to your hand.",
                    OptionB = "Add the bottom card of your Life cards to your hand.",
                };
                Log(state, effect.Seat, $"{sourceName}: take the top or bottom Life card.");
                return EffectResolution.Resolved;
            }

            // "Play this Character card from your trash." (the source revives itself — Oars.)
            if (ContainsAll(text, "Play this Character card from your trash"))
            {
                int selfIdx = owner.Trash.FindIndex(c => c.InstanceId == effect.SourceInstanceId);
                int selfSlot = owner.CharacterArea.FindIndex(c => c == null);
                if (selfIdx >= 0 && selfSlot >= 0)
                {
                    var reviv = owner.Trash[selfIdx];
                    owner.Trash.RemoveAt(selfIdx);
                    reviv.Zone = "character";
                    reviv.PlayedOnTurn = state.TurnNumber;
                    owner.CharacterArea[selfSlot] = reviv;
                    Log(state, effect.Seat, $"{sourceName} plays itself from the trash.");
                }
                else Log(state, effect.Seat, $"{sourceName}: cannot revive (no slot or card moved).");
                return EffectResolution.Resolved;
            }

            // "your opponent may add 1 DON!! card from their DON!! deck and set it as active."
            if (ContainsAll(text, "opponent may add") && ContainsAll(text, "DON!! deck"))
            {
                var oppMd = Player(state, OtherSeat(effect.Seat));
                if (oppMd.DonDeck > 0)
                {
                    oppMd.DonDeck--;
                    oppMd.CostArea.Add(CreateDonInstance(OtherSeat(effect.Seat), oppMd));
                    Log(state, effect.Seat, $"{sourceName}: opponent adds 1 active DON!!.");
                }
                return EffectResolution.Resolved;
            }

            // "Give up to N of your opponent's rested DON!! cards to N of your opponent's
            // Characters." (Jango — saddles the opponent's characters with their own DON.)
            {
                var oppGive = System.Text.RegularExpressions.Regex.Match(text,
                    @"Give up to (\d+) of your opponent's rested DON!! cards to",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (oppGive.Success)
                {
                    var oppGd = Player(state, OtherSeat(effect.Seat));
                    var firstChar = oppGd.CharacterArea.FirstOrDefault(c => c != null);
                    int gN = int.Parse(oppGive.Groups[1].Value);
                    int gDone = 0;
                    if (firstChar != null)
                    {
                        for (int i = 0; i < gN; i++)
                        {
                            var rd2 = oppGd.CostArea.FirstOrDefault(d => d.Rested);
                            if (rd2 == null) break;
                            oppGd.CostArea.Remove(rd2);
                            firstChar.AttachedDonIds.Add(rd2.InstanceId);
                            gDone++;
                        }
                    }
                    Log(state, effect.Seat, $"{sourceName}: {gDone} of the opponent's rested DON!! attached to {(firstChar != null ? NameId(GetCard(firstChar)) : "nothing")}.");
                    return EffectResolution.Resolved;
                }
            }

            // "You may K.O. any number of your {T} type Characters with a cost of N or less.
            // Your Leader gains an additional +N power during this turn for every Character K.O.'d."
            if (ContainsAll(text, "K.O. any number of your"))
            {
                var perKo = System.Text.RegularExpressions.Regex.Match(text, @"\+(\d{3,5}) power[^.]*for every Character");
                int perBonus = perKo.Success ? int.Parse(perKo.Groups[1].Value) : 0;
                int anyCap = ParseLimit(text, @"cost of (\d+) or less");
                if (effect.SelectionsRemaining <= 0) effect.SelectionsRemaining = 99;
                var anyT = FindAnyInPlay(state, targetId, out var anySeat);
                if (anyT == null)
                {
                    Log(state, effect.Seat, $"Click your Characters to K.O. one at a time ({sourceName}); skip when done.");
                    return EffectResolution.WaitingForTarget;
                }
                if (anySeat != effect.Seat || GetCard(anyT).Type != "character"
                    || (anyCap >= 0 && GetCost(state, anyT) > anyCap)
                    || !CardPassesFeatureFilter(text, GetCard(anyT)))
                {
                    Log(state, effect.Seat, "That is not a valid sacrifice.");
                    return EffectResolution.WaitingForTarget;
                }
                MoveToTrash(state, effect.Seat, anyT.InstanceId);
                if (perBonus > 0 && owner.Leader != null)
                {
                    state.TemporaryPowerBonus.TryGetValue(owner.Leader.InstanceId, out var exAny);
                    state.TemporaryPowerBonus[owner.Leader.InstanceId] = exAny + perBonus;
                    RegisterPowerModifier(owner.Leader, sourceName, perBonus, "endOfTurn");
                    Log(state, effect.Seat, $"Leader gains +{perBonus} power (running total applies per K.O.).");
                }
                return EffectResolution.WaitingForTarget;   // keep sacrificing until the player skips
            }

            // ---- "Look at N card(s) from the top of your opponent's deck." ------------------
            if (ContainsAll(text, "Look at") && ContainsAll(text, "top of your opponent's deck"))
            {
                var oppPk = Player(state, OtherSeat(effect.Seat));
                int pkN = ParseLookCount(text);
                var pkNames = oppPk.Deck.Take(pkN).Select(c => GetCard(c).Name);
                Log(state, effect.Seat, $"{sourceName}: top of opponent's deck — {string.Join(", ", pkNames)}");
                return EffectResolution.Resolved;
            }

            // ---- "Trash cards from your hand until you have N cards in your hand." ---------
            {
                var tuM = System.Text.RegularExpressions.Regex.Match(text,
                    @"Trash cards from your hand until you have (\d+) cards? in your hand",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (tuM.Success)
                {
                    int keepN = int.Parse(tuM.Groups[1].Value);
                    if (effect.SelectionsRemaining <= 0)
                    {
                        effect.SelectionsRemaining = Math.Max(0, owner.Hand.Count - keepN);
                        effect.TargetZone = EffectTargetZone.Hand;
                        if (effect.SelectionsRemaining == 0) return EffectResolution.Resolved;
                    }
                    if (string.IsNullOrEmpty(targetId))
                    {
                        Log(state, effect.Seat, $"Click {effect.SelectionsRemaining} more card(s) in your hand to trash ({sourceName}).");
                        return EffectResolution.WaitingForTarget;
                    }
                    int tuIdx = owner.Hand.FindIndex(c => c.InstanceId == targetId);
                    if (tuIdx < 0) { Log(state, effect.Seat, "That card is not in your hand."); return EffectResolution.WaitingForTarget; }
                    var tuCard = owner.Hand[tuIdx];
                    owner.Hand.RemoveAt(tuIdx);
                    tuCard.Zone = "trash";
                    owner.Trash.Add(tuCard);
                    NotifyHandTrashedByEffect(state, effect.Seat);
                    effect.SelectionsRemaining--;
                    if (effect.SelectionsRemaining > 0) return EffectResolution.WaitingForTarget;
                    return EffectResolution.Resolved;
                }
            }

            // ---- "Apply each of the following effects based on the number of cards in your
            // trash:" — evaluate every bullet's threshold. ------------------------------------
            if (ContainsAll(text, "Apply each of the following effects"))
            {
                int trashCount = owner.Trash.Count;
                foreach (var bullet in text.Split('\n'))
                {
                    var bl = bullet.TrimStart('•', '-', ' ');
                    var thM = System.Text.RegularExpressions.Regex.Match(bl,
                        @"^If (?:there are|you have) (\d+) or more cards?, (.+)$",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (!thM.Success) continue;
                    if (trashCount < int.Parse(thM.Groups[1].Value)) continue;
                    string blBody = thM.Groups[2].Value.Trim();
                    var blSrc = FindCardInstance(state, effect.SourceInstanceId);
                    if (blSrc != null && IsAutomatedEffectPattern(blBody))
                        QueueAndAutoResolve(state, effect.Seat, blSrc, effect.Timing, blBody, false, effect.Scope, InferTargetZone(blBody));
                    else
                        Log(state, effect.Seat, $"{sourceName}: threshold met — {blBody}");
                }
                return EffectResolution.Resolved;
            }

            // ---- Swap base power (OP14-001 / OP14-017): pick two, swap printed powers. -----
            if (ContainsAll(text, "Swap the base power of the selected Characters"))
            {
                bool oppSwap = ContainsAll(text, "of your opponent's Characters");
                if (effect.SelectionsRemaining <= 0) effect.SelectionsRemaining = 2;
                int swapBpCap = ParseLimit(text, @"(\d{3,5}) base power or less");
                var swT = FindAnyInPlay(state, targetId, out var swSeat);
                if (swT == null)
                {
                    Log(state, effect.Seat, $"Select {effect.SelectionsRemaining} Character(s) to swap base power ({sourceName}).");
                    return EffectResolution.WaitingForTarget;
                }
                bool sideOk = oppSwap ? swSeat == OtherSeat(effect.Seat) : swSeat == effect.Seat;
                if (!sideOk || GetCard(swT).Type != "character" || !CardPassesFeatureFilter(text, GetCard(swT))
                    || (swapBpCap >= 0 && GetCard(swT).Power > swapBpCap)
                    || swT.InstanceId == effect.FirstPickId)
                {
                    Log(state, effect.Seat, "That is not a valid swap target.");
                    return EffectResolution.WaitingForTarget;
                }
                if (effect.SelectionsRemaining == 2)
                {
                    effect.FirstPickId = swT.InstanceId;
                    effect.SelectionsRemaining--;
                    Log(state, effect.Seat, $"First swap target: {NameId(GetCard(swT))}. Select the second.");
                    return EffectResolution.WaitingForTarget;
                }
                var firstT = FindAnyInPlay(state, effect.FirstPickId, out _);
                if (firstT == null) return EffectResolution.Resolved;
                int pA = GetCard(firstT).Power;
                int pB = GetCard(swT).Power;
                foreach (var bp in state.BasePowerOverrides)
                {
                    if (bp.TargetInstanceId == firstT.InstanceId) pA = bp.Value;
                    if (bp.TargetInstanceId == swT.InstanceId) pB = bp.Value;
                }
                state.BasePowerOverrides.Add(new BasePowerOverride { TargetInstanceId = firstT.InstanceId, Value = pB, OwnerSeat = effect.Seat, Duration = "thisTurn" });
                state.BasePowerOverrides.Add(new BasePowerOverride { TargetInstanceId = swT.InstanceId, Value = pA, OwnerSeat = effect.Seat, Duration = "thisTurn" });
                Log(state, effect.Seat, $"{sourceName} swaps base power: {NameId(GetCard(firstT))} ↔ {NameId(GetCard(swT))}.");
                return EffectResolution.Resolved;
            }

            // ---- "Change the attack target to the selected card." (OP14-060) ----------------
            if (ContainsAll(text, "Change the attack target to the selected card") && state.Battle != null)
            {
                var rtT = FindAnyInPlay(state, targetId, out var rtSeat);
                if (rtT == null)
                {
                    Log(state, effect.Seat, $"Select your Leader or a matching Character to redirect the attack to ({sourceName}).");
                    return EffectResolution.WaitingForTarget;
                }
                var rtDef = GetCard(rtT);
                if (rtSeat != effect.Seat || (rtDef.Type != "leader" && !(rtDef.Type == "character" && CardPassesFeatureFilter(text, rtDef))))
                {
                    Log(state, effect.Seat, "That is not a valid redirect target.");
                    return EffectResolution.WaitingForTarget;
                }
                state.Battle.TargetId = rtT.InstanceId;
                state.Battle.TargetSeat = rtSeat;
                state.Battle.DefensePower = GetPower(state, rtT) + state.Battle.CounterPower;
                Log(state, effect.Seat, $"{sourceName}: the attack target is now {NameId(rtDef)}.");
                return EffectResolution.Resolved;
            }

            // ---- "Negate the effects of your opponent's Leader and all of their Characters
            // during this turn." (P-100) ------------------------------------------------------
            if (ContainsAll(text, "Negate the effects of your opponent's Leader and all of their Characters"))
            {
                var oppNg = Player(state, OtherSeat(effect.Seat));
                var ngSrc = FindAnyInPlay(state, effect.SourceInstanceId, out _);
                if (oppNg.Leader != null) AddModifier(state, ngSrc, oppNg.Leader, "effectsNegated", "thisTurn", null, effect.Seat);
                int ngC = 1;
                foreach (var c in oppNg.CharacterArea)
                    if (c != null) { AddModifier(state, ngSrc, c, "effectsNegated", "thisTurn", null, effect.Seat); ngC++; }
                Log(state, effect.Seat, $"{sourceName} negates the effects of {ngC} opponent card(s) this turn.");
                return EffectResolution.Resolved;
            }

            // ---- "Select up to 1 {T} type card … from your hand and play it or add it to the
            // top of your Life cards face-up." (OP07-097) — surfaced as a Choose-one. --------
            if (ContainsAll(text, "from your hand and play it or add it to the top of your Life"))
            {
                int plCap = ParseLimit(text, @"cost of (\d+) or less");
                string plTag = ParseCurlyBraceTag(text);
                state.ActiveChoice = new ChoiceState
                {
                    Seat = effect.Seat,
                    ControllerSeat = effect.Seat,
                    SourceInstanceId = effect.SourceInstanceId,
                    SourceCardId = effect.SourceCardId,
                    Timing = effect.Timing,
                    OptionA = $"Play up to 1 {(string.IsNullOrEmpty(plTag) ? "" : "{" + plTag + "} type ")}card with a cost of {(plCap >= 0 ? plCap : 99)} or less from your hand.",
                    OptionB = $"Add up to 1 {(string.IsNullOrEmpty(plTag) ? "" : "{" + plTag + "} type ")}card with a cost of {(plCap >= 0 ? plCap : 99)} or less from your hand to the top of your Life cards face-up.",
                };
                Log(state, effect.Seat, $"{sourceName}: choose — play it, or add it to Life.");
                return EffectResolution.Resolved;
            }
            // Same shape from the trash (OP14-104).
            if (ContainsAll(text, "from your trash and play it or add it to the top of your Life"))
            {
                int plCap2 = ParseLimit(text, @"cost of (\d+) or less");
                string plTag2 = ParseCurlyBraceTag(text);
                state.ActiveChoice = new ChoiceState
                {
                    Seat = effect.Seat,
                    ControllerSeat = effect.Seat,
                    SourceInstanceId = effect.SourceInstanceId,
                    SourceCardId = effect.SourceCardId,
                    Timing = effect.Timing,
                    OptionA = $"Play up to 1 {(string.IsNullOrEmpty(plTag2) ? "" : "{" + plTag2 + "} type ")}Character with a cost of {(plCap2 >= 0 ? plCap2 : 99)} or less from your trash.",
                    OptionB = $"Add up to 1 {(string.IsNullOrEmpty(plTag2) ? "" : "{" + plTag2 + "} type ")}card with a cost of {(plCap2 >= 0 ? plCap2 : 99)} or less from your trash to the top of your Life cards face-up.",
                };
                Log(state, effect.Seat, $"{sourceName}: choose — play it, or add it to the top of your Life face-up.");
                return EffectResolution.Resolved;
            }

            // "Add ... from your trash to the top of your Life cards face-up." — the resolved
            // Option B of the Moria family (and printed directly on some cards): pick a trash
            // card, it goes on TOP of Life (end of the list) face-up.
            if (ContainsAll(text, "from your trash") && ContainsAll(text, "top of your Life"))
            {
                effect.TargetZone = EffectTargetZone.Trash;
                int tlCap = ParseLimit(text, @"cost of (\d+) or less");
                string tlTag = ParseCurlyBraceTag(text);
                if (!string.IsNullOrEmpty(targetId))
                {
                    int ti = owner.Trash.FindIndex(c => c.InstanceId == targetId);
                    if (ti >= 0)
                    {
                        var tlCard = owner.Trash[ti];
                        var tlDef = GetCard(tlCard);
                        bool tlOk = (tlCap < 0 || tlDef.Cost <= tlCap)
                            && (string.IsNullOrEmpty(tlTag) || tlDef.HasFeature(tlTag));
                        if (tlOk)
                        {
                            owner.Trash.RemoveAt(ti);
                            tlCard.Zone = "life";
                            tlCard.FaceUp = true;
                            owner.Life.Add(tlCard);   // end of the list = TOP of Life
                            Log(state, effect.Seat, $"{sourceName}: {NameId(tlDef)} placed face-up on top of Life.");
                            return EffectResolution.Resolved;
                        }
                    }
                }
                if (owner.Trash.Count == 0)
                {
                    Log(state, effect.Seat, $"{sourceName}: trash is empty — nothing to add to Life.");
                    return EffectResolution.Resolved;
                }
                return EffectResolution.WaitingForTarget;
            }

            // ---- Misc automatic opponent-zone manipulation --------------------------------
            {
                // "Your opponent places N card(s)/Events from their (hand|trash) at the bottom
                // of their deck (in any order)." — auto (their choice simplified to last cards).
                var oppBotM = System.Text.RegularExpressions.Regex.Match(text,
                    @"opponent (?:places|must place) (\d+) (Events?|cards?) from their (hand|trash) at the bottom of their deck",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (oppBotM.Success)
                {
                    int obN = int.Parse(oppBotM.Groups[1].Value);
                    bool eventsOnly = oppBotM.Groups[2].Value.StartsWith("Event", StringComparison.OrdinalIgnoreCase);
                    bool fromHand = oppBotM.Groups[3].Value.Equals("hand", StringComparison.OrdinalIgnoreCase);
                    var oppOb = Player(state, OtherSeat(effect.Seat));
                    var zoneOb = fromHand ? oppOb.Hand : oppOb.Trash;
                    int moved = 0;
                    for (int i = zoneOb.Count - 1; i >= 0 && moved < obN; i--)
                    {
                        if (eventsOnly && GetCard(zoneOb[i]).Type != "event") continue;
                        var mc = zoneOb[i];
                        zoneOb.RemoveAt(i);
                        mc.Zone = "deck";
                        oppOb.Deck.Add(mc);
                        moved++;
                    }
                    Log(state, effect.Seat, $"{sourceName}: opponent places {moved} card(s) from {(fromHand ? "hand" : "trash")} at the bottom of their deck.");
                    return EffectResolution.Resolved;
                }

                // "Look at all of your Life cards and place them back in any order." — real
                // rearrange (ST13-012 Makino): Life opens in the deck-look rearrange UI
                // (LifeMode); the confirmed order is written back to Life.
                if (ContainsAll(text, "Look at all of your Life cards")
                    && (ContainsAll(text, "any order") || ContainsAll(text, "place them back")))
                {
                    if (owner.Life.Count <= 1)
                    {
                        Log(state, effect.Seat, $"{sourceName}: not enough Life cards to rearrange.");
                        return EffectResolution.Resolved;
                    }
                    var dlLife = new DeckLookState
                    {
                        Seat = effect.Seat,
                        SourceInstanceId = effect.SourceInstanceId,
                        SourceName = sourceName,
                        Step = "rearrange",
                        LifeMode = true,
                        MaxCost = -1,
                    };
                    for (int i = owner.Life.Count - 1; i >= 0; i--)   // display top-of-Life first
                    {
                        var lc = owner.Life[i];
                        lc.Zone = "look";
                        dlLife.Cards.Add(lc);
                    }
                    owner.Life.Clear();
                    state.DeckLook = dlLife;
                    Log(state, effect.Seat, $"{sourceName}: rearrange your Life cards (leftmost = top).");
                    return EffectResolution.Resolved;
                }

                // "Your opponent chooses N card from their hand and trashes it." — the
                // OPPONENT picks; simplification: auto (last card).
                var oppChM = System.Text.RegularExpressions.Regex.Match(text,
                    @"opponent chooses (\d+) cards? from their hand and trashes",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (oppChM.Success)
                {
                    int ocN = int.Parse(oppChM.Groups[1].Value);
                    var oppOc = Player(state, OtherSeat(effect.Seat));
                    int ocDone = 0;
                    for (int i = 0; i < ocN && oppOc.Hand.Count > 0; i++)
                    {
                        var hc = oppOc.Hand[oppOc.Hand.Count - 1];
                        oppOc.Hand.RemoveAt(oppOc.Hand.Count - 1);
                        hc.Zone = "trash";
                        oppOc.Trash.Add(hc);
                        ocDone++;
                    }
                    Log(state, effect.Seat, $"{sourceName}: {ocDone} card(s) trashed from the opponent's hand.");
                    return EffectResolution.Resolved;
                }

                // "Trash N cards from your opponent's/their hand." — the CONTROLLER picks:
                // wait for clicks on the opponent's (face-down) hand cards, one per card.
                var oppTrM = System.Text.RegularExpressions.Regex.Match(text,
                    @"trash (\d+) cards? from (?:your opponent's|their) hand",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (oppTrM.Success)
                {
                    int otN2 = int.Parse(oppTrM.Groups[1].Value);
                    var oppOt2 = Player(state, OtherSeat(effect.Seat));
                    if (oppOt2.Hand.Count == 0)
                    {
                        Log(state, effect.Seat, $"{sourceName}: opponent has no cards in hand.");
                        return EffectResolution.Resolved;
                    }
                    effect.TargetZone = EffectTargetZone.Hand;
                    if (!string.IsNullOrEmpty(targetId))
                    {
                        int hi = oppOt2.Hand.FindIndex(c => c.InstanceId == targetId);
                        if (hi >= 0)
                        {
                            var hc = oppOt2.Hand[hi];
                            oppOt2.Hand.RemoveAt(hi);
                            hc.Zone = "trash";
                            oppOt2.Trash.Add(hc);
                            Log(state, effect.Seat, $"{sourceName}: trashed {NameId(GetCard(hc))} from the opponent's hand.");
                            int remaining = (effect.SelectionsRemaining > 0 ? effect.SelectionsRemaining : otN2) - 1;
                            if (remaining <= 0 || oppOt2.Hand.Count == 0) return EffectResolution.Resolved;
                            effect.SelectionsRemaining = remaining;
                            return EffectResolution.WaitingForTarget;
                        }
                    }
                    if (effect.SelectionsRemaining <= 0)
                    {
                        effect.SelectionsRemaining = Math.Min(otN2, oppOt2.Hand.Count);
                        Log(state, effect.Seat, $"{sourceName}: click {effect.SelectionsRemaining} card(s) in the opponent's hand to trash.");
                    }
                    return EffectResolution.WaitingForTarget;
                }

                // "Your opponent chooses N card from your hand; trash that card." — auto (last).
                if (ContainsAll(text, "opponent chooses") && ContainsAll(text, "from your hand")
                    && (ContainsAll(text, "trash that card") || ContainsAll(text, "trashes it")))
                {
                    if (owner.Hand.Count > 0)
                    {
                        var hc2 = owner.Hand[owner.Hand.Count - 1];
                        owner.Hand.RemoveAt(owner.Hand.Count - 1);
                        hc2.Zone = "trash";
                        owner.Trash.Add(hc2);
                        Log(state, effect.Seat, $"{sourceName}: opponent chose {NameId(GetCard(hc2))} — trashed from your hand.");
                    }
                    return EffectResolution.Resolved;
                }

                // "Choose N cards from your opponent's hand; your opponent reveals those cards."
                if (ContainsAll(text, "from your opponent's hand") && ContainsAll(text, "reveal"))
                {
                    var oppRv = Player(state, OtherSeat(effect.Seat));
                    var names = oppRv.Hand.Count > 0
                        ? string.Join(", ", oppRv.Hand.Select(c => GetCard(c).Name))
                        : "(empty)";
                    Log(state, effect.Seat, $"{sourceName}: opponent's hand revealed — {names}");
                    return EffectResolution.Resolved;
                }

                // "Your opponent returns all cards in their hand to their deck and shuffles …
                // Then, your opponent draws N cards."
                if (ContainsAll(text, "opponent returns all cards in their hand to their deck"))
                {
                    var oppRs = Player(state, OtherSeat(effect.Seat));
                    int retC = oppRs.Hand.Count;
                    foreach (var hcR in oppRs.Hand) { hcR.Zone = "deck"; oppRs.Deck.Add(hcR); }
                    oppRs.Hand.Clear();
                    ShuffleInPlace(oppRs.Deck, $"{state.Seed}:{OtherSeat(effect.Seat)}:handshuffle:{state.EffectSequence}");
                    var rdM2 = System.Text.RegularExpressions.Regex.Match(text, @"draws (\d+) cards?");
                    int drawBack = rdM2.Success ? int.Parse(rdM2.Groups[1].Value) : retC;
                    for (int i = 0; i < drawBack; i++) DrawCard(state, OtherSeat(effect.Seat), true);
                    Log(state, effect.Seat, $"{sourceName}: opponent shuffles {retC} hand card(s) into the deck and draws {drawBack}.");
                    return EffectResolution.Resolved;
                }
            }

            // ---- "Set your (…) Leader as active." / "Set all of your (…) Characters as
            // active." / "Set this Character or up to N of your DON!! cards as active." ------
            if (System.Text.RegularExpressions.Regex.IsMatch(text, @"Set your (\{[^}]+\} type |\[[^\]]+\] )?Leader as active",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                if (owner.Leader != null && owner.Leader.Rested && !HasModifier(state, owner.Leader, "freeze"))
                {
                    owner.Leader.Rested = false;
                    Log(state, effect.Seat, $"{sourceName} sets your Leader as active.");
                }
                return EffectResolution.Resolved;
            }
            if (ContainsAll(text, "Set all of your") && ContainsAll(text, "as active"))
            {
                if (ContainsAll(text, "DON!!"))
                {
                    foreach (var d in owner.CostArea) d.Rested = false;
                    Log(state, effect.Seat, $"{sourceName} sets all your DON!! as active.");
                }
                else
                {
                    int saAll = 0;
                    int saAllCap = ParseLimit(text, @"cost of (\d+) or less");
                    foreach (var c in owner.CharacterArea)
                    {
                        if (c == null || !c.Rested || HasModifier(state, c, "freeze")) continue;
                        if (!CardPassesFeatureFilter(text, GetCard(c))) continue;
                        if (saAllCap >= 0 && GetCost(state, c) > saAllCap) continue;
                        bool colorOkSa = true;
                        foreach (var col in new[] { "red", "green", "blue", "purple", "black", "yellow" })
                            if (System.Text.RegularExpressions.Regex.IsMatch(text, $@"all of your {col}\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                                && (GetCard(c).Color ?? "").IndexOf(col, StringComparison.OrdinalIgnoreCase) < 0)
                                colorOkSa = false;
                        if (!colorOkSa) continue;
                        c.Rested = false;
                        saAll++;
                    }
                    Log(state, effect.Seat, $"{sourceName} sets {saAll} Character(s) as active.");
                }
                // "Then, you cannot play cards from your hand / attack a Leader during this
                // turn" drawbacks are informational here (logged for the player).
                if (ContainsAll(text, "you cannot")) Log(state, effect.Seat, $"Note: {text.Substring(text.IndexOf("you cannot", StringComparison.OrdinalIgnoreCase))}");
                return EffectResolution.Resolved;
            }
            if (ContainsAll(text, "Set this Character or up to") && ContainsAll(text, "DON!!"))
            {
                var scSelf = FindAnyInPlay(state, effect.SourceInstanceId, out _);
                if (scSelf != null && scSelf.Rested) { scSelf.Rested = false; Log(state, effect.Seat, $"{sourceName} sets itself as active."); }
                else
                {
                    var scUp = System.Text.RegularExpressions.Regex.Match(text, @"up to (\d+)");
                    int scN = scUp.Success ? int.Parse(scUp.Groups[1].Value) : 1;
                    int scDone = 0;
                    foreach (var d in owner.CostArea) { if (scDone >= scN) break; if (d.Rested) { d.Rested = false; scDone++; } }
                    Log(state, effect.Seat, $"{sourceName} sets {scDone} DON!! as active.");
                }
                return EffectResolution.Resolved;
            }

            // ---- "Rest your opponent's Leader." / "Rest N of your opponent's (…) Characters
            // (with a cost of N or less)." (no "up to" — still player-picked) ----------------
            if (ContainsAll(text, "Rest your opponent's Leader"))
            {
                var oppLd = Player(state, OtherSeat(effect.Seat)).Leader;
                if (oppLd != null && !oppLd.Rested && !HasModifier(state, oppLd, "cannotBeRested"))
                {
                    oppLd.Rested = true;
                    Log(state, effect.Seat, $"{sourceName} rests the opponent's Leader.");
                }
                return EffectResolution.Resolved;
            }
            {
                var rnM = System.Text.RegularExpressions.Regex.Match(text,
                    @"Rest (\d+) of your opponent's ([^.]*?)Characters",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (rnM.Success)
                {
                    if (effect.SelectionsRemaining <= 0) effect.SelectionsRemaining = int.Parse(rnM.Groups[1].Value);
                    int rnCap = ParseLimit(text, @"cost of (\d+) or less");
                    var rnT = FindAnyInPlay(state, targetId, out var rnSeat);
                    if (rnT == null)
                    {
                        Log(state, effect.Seat, $"Choose {effect.SelectionsRemaining} opponent Character(s) to rest ({sourceName}).");
                        return EffectResolution.WaitingForTarget;
                    }
                    if (rnSeat != OtherSeat(effect.Seat) || GetCard(rnT).Type != "character"
                        || (rnCap >= 0 && GetCost(state, rnT) > rnCap)
                        || !CardPassesFeatureFilter(rnM.Groups[2].Value, GetCard(rnT))
                        || HasModifier(state, rnT, "cannotBeRested"))
                    {
                        Log(state, effect.Seat, "That is not a valid target to rest.");
                        return EffectResolution.WaitingForTarget;
                    }
                    rnT.Rested = true;
                    Log(state, effect.Seat, $"{sourceName} rests {NameId(GetCard(rnT))}.");
                    effect.SelectionsRemaining--;
                    if (effect.SelectionsRemaining > 0) return EffectResolution.WaitingForTarget;
                    return EffectResolution.Resolved;
                }
            }

            // ---- Life-zone family ----------------------------------------------------------
            {
                // "Add N card from the top of your Life cards to your hand." (no top/bottom choice)
                var addLife2 = System.Text.RegularExpressions.Regex.Match(text,
                    @"Add (?:up to )?(\d+) cards? from the top of your Life cards? to your hand",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (addLife2.Success)
                {
                    int alN = int.Parse(addLife2.Groups[1].Value);
                    int alDone = 0;
                    for (int i = 0; i < alN && owner.Life.Count > 0; i++)
                    {
                        var lc2 = owner.Life[owner.Life.Count - 1];
                        owner.Life.RemoveAt(owner.Life.Count - 1);
                        lc2.Zone = "hand";
                        owner.Hand.Add(lc2);
                        alDone++;
                    }
                    Log(state, effect.Seat, $"{sourceName}: added {alDone} Life card(s) to hand ({owner.Life.Count} Life left).");
                    return EffectResolution.Resolved;
                }
                // "Add up to N card from the top of your opponent's Life cards to the owner's hand."
                if (ContainsAll(text, "from the top of your opponent's Life") && ContainsAll(text, "owner's hand"))
                {
                    var olM = System.Text.RegularExpressions.Regex.Match(text, @"[Uu]p to (\d+)");
                    int olN = olM.Success ? int.Parse(olM.Groups[1].Value) : 1;
                    var oppOl = Player(state, OtherSeat(effect.Seat));
                    int olDone = 0;
                    for (int i = 0; i < olN && oppOl.Life.Count > 0; i++)
                    {
                        var lc3 = Pop(oppOl.Life);
                        lc3.Zone = "hand";
                        oppOl.Hand.Add(lc3);
                        olDone++;
                    }
                    Log(state, effect.Seat, $"{sourceName}: opponent takes {olDone} Life card(s) to hand.");
                    return EffectResolution.Resolved;
                }
                // Hand → Life: "Add up to N ({T} type Character )card from your hand to the
                // top of your Life cards (face-up)."
                if (ContainsAll(text, "Add up to") && ContainsAll(text, "from your hand") && ContainsAll(text, "Life cards"))
                {
                    if (string.IsNullOrEmpty(targetId))
                    {
                        effect.TargetZone = EffectTargetZone.Hand;
                        Log(state, effect.Seat, $"Click a card in your hand to add to your Life ({sourceName}), or skip.");
                        return EffectResolution.WaitingForTarget;
                    }
                    int hlIdx = owner.Hand.FindIndex(c => c.InstanceId == targetId);
                    if (hlIdx < 0) { Log(state, effect.Seat, "That card is not in your hand."); return EffectResolution.WaitingForTarget; }
                    var hlCard = owner.Hand[hlIdx];
                    if (!CardPassesFeatureFilter(text, GetCard(hlCard))
                        || (ContainsAll(text, "Character card") && GetCard(hlCard).Type != "character"))
                    {
                        Log(state, effect.Seat, "That card does not match the requirement.");
                        return EffectResolution.WaitingForTarget;
                    }
                    owner.Hand.RemoveAt(hlIdx);
                    hlCard.Zone = "life";
                    hlCard.FaceUp = ContainsAll(text, "face-up");
                    owner.Life.Add(hlCard);
                    Log(state, effect.Seat, $"{sourceName}: {NameId(GetCard(hlCard))} added to the top of your Life.");
                    return EffectResolution.Resolved;
                }
                // Field → Life removal: "Add up to N (of your opponent's) Character(s) … to the
                // top or bottom of (the owner's|your opponent's|their) Life cards (face-up)."
                if (ContainsAll(text, "Add up to") && ContainsAll(text, "Life cards")
                    && !ContainsAll(text, "from the top of your deck") && !ContainsAll(text, "from your hand")
                    && !ContainsAll(text, "from the top of your opponent's Life")
                    && (ContainsAll(text, "top or bottom") || ContainsAll(text, "to the top of")))
                {
                    if (effect.SelectionsRemaining <= 0)
                    {
                        var flM = System.Text.RegularExpressions.Regex.Match(text, @"Add up to (\d+)");
                        effect.SelectionsRemaining = flM.Success ? int.Parse(flM.Groups[1].Value) : 1;
                    }
                    int flCap = ParseLimit(text, @"cost of (\d+) or less");
                    var flT = FindAnyInPlay(state, targetId, out var flSeat);
                    if (flT == null)
                    {
                        Log(state, effect.Seat, $"Choose up to {effect.SelectionsRemaining} Character(s) to add to Life ({sourceName}), or skip.");
                        return EffectResolution.WaitingForTarget;
                    }
                    bool mustBeOpp = ContainsAll(text, "of your opponent's Characters");
                    var flDef = GetCard(flT);
                    if (flDef.Type != "character" || (mustBeOpp && flSeat != OtherSeat(effect.Seat))
                        || (flCap >= 0 && GetCost(state, flT) > flCap)
                        || !CardPassesFeatureFilter(text, flDef))
                    {
                        Log(state, effect.Seat, "That is not a valid target.");
                        return EffectResolution.WaitingForTarget;
                    }
                    var flOwner = Player(state, flSeat);
                    if (!TryRemovalReplacement(state, flSeat, flT))
                    {
                        ReturnAttachedDon(flOwner, flT);
                        int flIdx = flOwner.CharacterArea.FindIndex(c => c != null && c.InstanceId == flT.InstanceId);
                        if (flIdx >= 0) flOwner.CharacterArea[flIdx] = null;
                        flT.Zone = "life";
                        flT.Rested = false;
                        flT.PlayedOnTurn = null;
                        flT.Modifiers.Clear();
                        flT.FaceUp = ContainsAll(text, "face-up");
                        state.NameOverrides.Remove(flT.InstanceId);
                        flOwner.Life.Add(flT);   // top of the pile (simplification: top placement)
                        Log(state, effect.Seat, $"{sourceName}: {NameId(flDef)} is added to the top of {(flSeat == effect.Seat ? "your" : "the opponent's")} Life.");
                    }
                    effect.SelectionsRemaining--;
                    if (effect.SelectionsRemaining > 0) return EffectResolution.WaitingForTarget;
                    return EffectResolution.Resolved;
                }
                // "Place up to N of your opponent's Characters … at the top or bottom of their
                // Life cards." — same as above with "Place" verb.
                if (ContainsAll(text, "Place up to") && ContainsAll(text, "Life cards"))
                {
                    // handled identically via the Add-up-to path pattern; rewrite and recurse.
                    var cloneP = ShallowCloneEffect(effect, text.Replace("Place up to", "Add up to"));
                    cloneP.SelectionsRemaining = effect.SelectionsRemaining;
                    var resP = TryResolveKnownEffect(state, cloneP, targetId);
                    effect.SelectionsRemaining = cloneP.SelectionsRemaining;
                    return resP;
                }
                // "Trash all your face-up Life cards."
                if (ContainsAll(text, "Trash all your face-up Life cards"))
                {
                    int fuC = 0;
                    for (int i = owner.Life.Count - 1; i >= 0; i--)
                    {
                        if (!owner.Life[i].FaceUp) continue;
                        var fuCard = owner.Life[i];
                        owner.Life.RemoveAt(i);
                        fuCard.Zone = "trash";
                        owner.Trash.Add(fuCard);
                        fuC++;
                    }
                    Log(state, effect.Seat, $"{sourceName}: trashed {fuC} face-up Life card(s).");
                    return EffectResolution.Resolved;
                }
                // "Turn all of your Life cards face-down." / "…face-up."
                if (ContainsAll(text, "Turn all of your Life cards face"))
                {
                    bool toUp = ContainsAll(text, "face-up");
                    foreach (var lcF in owner.Life) lcF.FaceUp = toUp;
                    Log(state, effect.Seat, $"{sourceName}: all your Life cards are now face-{(toUp ? "up" : "down")}.");
                    return EffectResolution.Resolved;
                }
                // "Look at (all of your|up to N card from the top of your or your opponent's)
                // Life cards … place …" — private-information peeks; in this build the looker
                // sees the names in the log and the cards keep their current order.
                if (ContainsAll(text, "Look at") && ContainsAll(text, "Life cards"))
                {
                    bool oppSide = ContainsAll(text, "opponent's Life");
                    var peekP = oppSide ? Player(state, OtherSeat(effect.Seat)) : owner;
                    string peekNames = peekP.Life.Count > 0
                        ? string.Join(", ", peekP.Life.AsEnumerable().Reverse().Select(c => GetCard(c).Name))
                        : "(no Life)";
                    Log(state, effect.Seat, $"{sourceName}: Life (top→bottom) — {peekNames}. Order kept.");
                    return EffectResolution.Resolved;
                }
            }

            // ---- "Your opponent cannot activate [Blocker] during this turn." ---------------
            if (ContainsAll(text, "cannot activate") && ContainsAll(text, "Blocker") && ContainsAll(text, "during this turn")
                && !ContainsAll(text, "attacks during this turn"))
            {
                var oppLb = Player(state, OtherSeat(effect.Seat));
                int lbPw = ParseLimit(text, @"(\d{3,5}) power or less");
                int lbCount = 0;
                foreach (var c in oppLb.CharacterArea)
                {
                    if (c == null) continue;
                    if (lbPw >= 0 && GetPower(state, c) > lbPw) continue;
                    AddModifier(state, FindAnyInPlay(state, effect.SourceInstanceId, out _), c, "effectsNegated", "thisTurn", null, effect.Seat);
                    lbCount++;
                }
                Log(state, effect.Seat, $"{sourceName}: {lbCount} opponent Character(s) cannot activate [Blocker] this turn.");
                return EffectResolution.Resolved;
            }

            // ---- Comprehensive keyword grants: "<target> gains [KW] (and +N power)
            // (during this turn/battle | until the end of your opponent's next …)". ----------
            {
                var kwM = System.Text.RegularExpressions.Regex.Match(text,
                    @"gains? \[(Rush|Blocker|Double Attack|Banish|Unblockable)\]",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                // Skip pure DON-gated passives ("[DON!! x1] This Character gains [Blocker].")
                // — those are continuous and handled by HasDonGatedKeyword. A queued clause
                // always carries an action context, so require a duration or a target phrase.
                bool kwHasDuration = ContainsAll(text, "during this turn") || ContainsAll(text, "during this battle")
                    || ContainsAll(text, "until the");
                bool kwTargeted = ContainsAll(text, "Up to ") || ContainsAll(text, "All of your") || ContainsAll(text, "Your Leader")
                    || System.Text.RegularExpressions.Regex.IsMatch(text, @"Your (\{[^}]+\} type )?Leader gains", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (kwM.Success && (kwHasDuration || kwTargeted)
                    && !ContainsAll(text, "Choose one") && ParseDonThreshold(text) == 0)
                {
                    string kw2 = System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(kwM.Groups[1].Value.ToLowerInvariant());
                    if (kw2 == "Double attack") kw2 = "Double Attack";
                    string kwDur = ContainsAll(text, "during this battle") ? "thisBattle"
                                 : ContainsAll(text, "until the") ? "untilNextTurn"
                                 : "thisTurn";   // includes duration-less grants (safe default)
                    int kwPower = 0;
                    var kwPw = System.Text.RegularExpressions.Regex.Match(text, @"and \+(\d{3,5}) power");
                    if (kwPw.Success) kwPower = int.Parse(kwPw.Groups[1].Value);
                    var kwSrc = FindAnyInPlay(state, effect.SourceInstanceId, out _);
                    Action<CardInstance> applyKw = t2 =>
                    {
                        AddModifier(state, kwSrc, t2, "keyword", kwDur, kw2, effect.Seat);
                        if (kwPower > 0)
                        {
                            if (kwDur == "thisBattle" && state.Battle != null)
                            {
                                state.Battle.BattlePowerBonus.TryGetValue(t2.InstanceId, out var exK);
                                state.Battle.BattlePowerBonus[t2.InstanceId] = exK + kwPower;
                                RegisterPowerModifier(t2, sourceName, kwPower, "endOfBattle");
                            }
                            else if (kwDur == "untilNextTurn")
                            {
                                state.TimedPowerBonuses.Add(new TimedPowerBonus { TargetInstanceId = t2.InstanceId, Delta = kwPower, OwnerSeat = effect.Seat });
                                RegisterPowerModifier(t2, sourceName, kwPower, "permanent");
                            }
                            else
                            {
                                state.TemporaryPowerBonus.TryGetValue(t2.InstanceId, out var exK);
                                state.TemporaryPowerBonus[t2.InstanceId] = exK + kwPower;
                                RegisterPowerModifier(t2, sourceName, kwPower, "endOfTurn");
                            }
                        }
                        Log(state, effect.Seat, $"{sourceName} gives {NameId(GetCard(t2))} [{kw2}]{(kwPower > 0 ? $" and +{kwPower} power" : "")}.");
                    };
                    // Self / own Leader / board-wide / up-to-N targeted.
                    if (ContainsAll(text, "This Character gains") || ContainsAll(text, "this Character gains")
                        || ContainsAll(text, "this Leader gains"))
                    {
                        var kwSelf = FindAnyInPlay(state, effect.SourceInstanceId, out _);
                        if (kwSelf != null) { applyKw(kwSelf); return EffectResolution.Resolved; }
                    }
                    if (System.Text.RegularExpressions.Regex.IsMatch(text, @"Your (\{[^}]+\} type )?Leader gains",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    {
                        if (owner.Leader != null) { applyKw(owner.Leader); }
                        return EffectResolution.Resolved;
                    }
                    if (ContainsAll(text, "All of your"))
                    {
                        int kwCount = 0;
                        var kwTargets = new List<CardInstance>();
                        foreach (var c in owner.CharacterArea)
                            if (c != null && CardPassesFeatureFilter(text, GetCard(c))) kwTargets.Add(c);
                        var kwNameF = System.Text.RegularExpressions.Regex.Match(text, @"All of your \[([^\]]+)\]");
                        foreach (var t3 in kwTargets)
                        {
                            if (kwNameF.Success && !NameMatches(state, t3, kwNameF.Groups[1].Value.Trim())
                                && t3.InstanceId != effect.SourceInstanceId) continue;
                            applyKw(t3); kwCount++;
                        }
                        if (ContainsAll(text, "and this Character"))
                        {
                            var kwSelf2 = FindAnyInPlay(state, effect.SourceInstanceId, out _);
                            if (kwSelf2 != null && !kwTargets.Contains(kwSelf2)) applyKw(kwSelf2);
                        }
                        return EffectResolution.Resolved;
                    }
                    // "Up to N of your … gains [KW]" — click targets.
                    if (effect.SelectionsRemaining <= 0)
                    {
                        var kwUp = System.Text.RegularExpressions.Regex.Match(text, @"[Uu]p to (\d+)");
                        effect.SelectionsRemaining = kwUp.Success ? int.Parse(kwUp.Groups[1].Value) : 1;
                    }
                    var kwT = FindAnyInPlay(state, targetId, out var kwSeat);
                    if (kwT == null)
                    {
                        Log(state, effect.Seat, $"Choose up to {effect.SelectionsRemaining} of your card(s) to gain [{kw2}] ({sourceName}), or skip.");
                        return EffectResolution.WaitingForTarget;
                    }
                    bool kwLeaderOk = ContainsAll(text, "Leader");
                    var kwDef2 = GetCard(kwT);
                    bool kwTypeOk = kwDef2.Type == "character" || (kwLeaderOk && kwDef2.Type == "leader");
                    if (kwSeat != effect.Seat || !kwTypeOk || !CardPassesFeatureFilter(text, kwDef2))
                    {
                        Log(state, effect.Seat, "That is not a valid keyword grant target.");
                        return EffectResolution.WaitingForTarget;
                    }
                    applyKw(kwT);
                    effect.SelectionsRemaining--;
                    if (effect.SelectionsRemaining > 0) return EffectResolution.WaitingForTarget;
                    return EffectResolution.Resolved;
                }
            }

            // ---- "can (also) attack (active Characters | Characters on the turn …)" grants --
            if (((ContainsAll(text, "can also attack") && ContainsAll(text, "active Characters"))
                    || ContainsAll(text, "can attack Characters on the turn"))
                && (ContainsAll(text, "Up to ") || ContainsAll(text, "This Character can")))
            {
                string grantType = ContainsAll(text, "active Characters") ? "canAttackActive" : "rushCharacters";
                // Self passives ("This Character can also attack active Characters.") are
                // continuous — nothing to queue; mark resolved so they never look manual.
                if (ContainsAll(text, "This Character can"))
                {
                    var caSelf = FindAnyInPlay(state, effect.SourceInstanceId, out _);
                    if (caSelf != null) AddModifier(state, caSelf, caSelf, grantType, "permanent", null, effect.Seat);
                    return EffectResolution.Resolved;
                }
                if (effect.SelectionsRemaining <= 0)
                {
                    var caUp = System.Text.RegularExpressions.Regex.Match(text, @"[Uu]p to (\d+)");
                    effect.SelectionsRemaining = caUp.Success ? int.Parse(caUp.Groups[1].Value) : 1;
                }
                var caT = FindAnyInPlay(state, targetId, out var caSeat);
                if (caT == null)
                {
                    Log(state, effect.Seat, $"Choose up to {effect.SelectionsRemaining} of your card(s) for {sourceName}, or skip.");
                    return EffectResolution.WaitingForTarget;
                }
                var caDef = GetCard(caT);
                bool caLeaderOk = ContainsAll(text, "Leader");
                if (caSeat != effect.Seat || (caDef.Type != "character" && !(caLeaderOk && caDef.Type == "leader"))
                    || !CardPassesFeatureFilter(text, caDef))
                {
                    Log(state, effect.Seat, "That is not a valid target.");
                    return EffectResolution.WaitingForTarget;
                }
                AddModifier(state, FindAnyInPlay(state, effect.SourceInstanceId, out _), caT, grantType,
                    ContainsAll(text, "during this turn") ? "thisTurn" : "thisTurn", null, effect.Seat);
                Log(state, effect.Seat, $"{sourceName}: {NameId(caDef)} {(grantType == "canAttackActive" ? "can attack active Characters" : "can attack Characters this turn")}.");
                effect.SelectionsRemaining--;
                if (effect.SelectionsRemaining > 0) return EffectResolution.WaitingForTarget;
                return EffectResolution.Resolved;
            }

            // ---- Power reduction variants: self, board-wide, until-next-turn ----------------
            {
                // "Give this Character −N power." (self)
                var selfMinus = System.Text.RegularExpressions.Regex.Match(text,
                    @"Give this Character [-\u2212\u2013\u2011\u2012\u2014](\d{3,5}) power",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (selfMinus.Success)
                {
                    var smT = FindAnyInPlay(state, effect.SourceInstanceId, out _);
                    if (smT != null)
                    {
                        int smD = int.Parse(selfMinus.Groups[1].Value);
                        state.TemporaryPowerBonus.TryGetValue(smT.InstanceId, out var exSm);
                        state.TemporaryPowerBonus[smT.InstanceId] = exSm - smD;
                        RegisterPowerModifier(smT, sourceName, -smD, "endOfTurn");
                        Log(state, effect.Seat, $"{sourceName} takes -{smD} power.");
                    }
                    return EffectResolution.Resolved;
                }
                // "Give all of your opponent's Characters −N power."
                var allMinus = System.Text.RegularExpressions.Regex.Match(text,
                    @"Give all of your opponent's Characters [-\u2212\u2013\u2011\u2012\u2014](\d{3,5}) power",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (allMinus.Success)
                {
                    int amD = int.Parse(allMinus.Groups[1].Value);
                    var oppAm = Player(state, OtherSeat(effect.Seat));
                    int amC = 0;
                    foreach (var c in oppAm.CharacterArea)
                    {
                        if (c == null) continue;
                        state.TemporaryPowerBonus.TryGetValue(c.InstanceId, out var exAm);
                        state.TemporaryPowerBonus[c.InstanceId] = exAm - amD;
                        RegisterPowerModifier(c, sourceName, -amD, "endOfTurn");
                        amC++;
                    }
                    Log(state, effect.Seat, $"{sourceName} gives -{amD} power to {amC} Character(s).");
                    return EffectResolution.Resolved;
                }
                // "Give up to N of your opponent's Characters −N power until the end of your
                // opponent's next (turn|End Phase)." — timed reduction.
                var tmMinus = System.Text.RegularExpressions.Regex.Match(text,
                    @"[-\u2212\u2013\u2011\u2012\u2014](\d{3,5}) power until the end of your opponent's next",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (tmMinus.Success && ContainsAll(text, "opponent") && ContainsAll(text, "Give"))
                {
                    if (effect.SelectionsRemaining <= 0)
                    {
                        var tmUp = System.Text.RegularExpressions.Regex.Match(text, @"[Uu]p to (\d+)");
                        effect.SelectionsRemaining = tmUp.Success ? int.Parse(tmUp.Groups[1].Value) : 1;
                    }
                    int tmD = int.Parse(tmMinus.Groups[1].Value);
                    var tmT = FindAnyInPlay(state, targetId, out var tmSeat);
                    if (tmT == null)
                    {
                        Log(state, effect.Seat, $"Choose up to {effect.SelectionsRemaining} opponent Character(s) for -{tmD} power ({sourceName}), or skip.");
                        return EffectResolution.WaitingForTarget;
                    }
                    if (tmSeat != OtherSeat(effect.Seat) || GetCard(tmT).Type != "character")
                    {
                        Log(state, effect.Seat, "That is not a valid target.");
                        return EffectResolution.WaitingForTarget;
                    }
                    state.TimedPowerBonuses.Add(new TimedPowerBonus { TargetInstanceId = tmT.InstanceId, Delta = -tmD, OwnerSeat = effect.Seat });
                    RegisterPowerModifier(tmT, sourceName, -tmD, "permanent");
                    Log(state, effect.Seat, $"{sourceName} gives {NameId(GetCard(tmT))} -{tmD} power until your next turn.");
                    effect.SelectionsRemaining--;
                    if (effect.SelectionsRemaining > 0) return EffectResolution.WaitingForTarget;
                    return EffectResolution.Resolved;
                }
            }

            // ---- Timed cost bumps: "Up to N of your … gains +N cost until the end of your
            // opponent's next (turn|End Phase)." / "All of your {T} … gain +N cost until …" ---
            {
                var tcM = System.Text.RegularExpressions.Regex.Match(text,
                    @"gains? \+(\d+) cost until the end of your opponent's next",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (tcM.Success)
                {
                    int tcD = int.Parse(tcM.Groups[1].Value);
                    if (ContainsAll(text, "All of your"))
                    {
                        int tcC = 0;
                        foreach (var c in owner.CharacterArea)
                        {
                            if (c == null || !CardPassesFeatureFilter(text, GetCard(c))) continue;
                            c.Modifiers.Add(new ActiveModifier { Source = sourceName, PowerDelta = 0, CostDelta = tcD, ExpiresAt = "untilNextTurnOf:" + effect.Seat });
                            tcC++;
                        }
                        Log(state, effect.Seat, $"{sourceName} gives +{tcD} cost to {tcC} Character(s).");
                        return EffectResolution.Resolved;
                    }
                    if (effect.SelectionsRemaining <= 0)
                    {
                        var tcUp = System.Text.RegularExpressions.Regex.Match(text, @"[Uu]p to (\d+)");
                        effect.SelectionsRemaining = tcUp.Success ? int.Parse(tcUp.Groups[1].Value) : 1;
                    }
                    var tcT = FindAnyInPlay(state, targetId, out var tcSeat);
                    if (tcT == null)
                    {
                        Log(state, effect.Seat, $"Choose up to {effect.SelectionsRemaining} of your Character(s) for +{tcD} cost ({sourceName}), or skip.");
                        return EffectResolution.WaitingForTarget;
                    }
                    if (tcSeat != effect.Seat || GetCard(tcT).Type != "character" || !CardPassesFeatureFilter(text, GetCard(tcT)))
                    {
                        Log(state, effect.Seat, "That is not a valid target.");
                        return EffectResolution.WaitingForTarget;
                    }
                    tcT.Modifiers.Add(new ActiveModifier { Source = sourceName, PowerDelta = 0, CostDelta = tcD, ExpiresAt = "untilNextTurnOf:" + effect.Seat });
                    Log(state, effect.Seat, $"{sourceName} gives {NameId(GetCard(tcT))} +{tcD} cost until your next turn.");
                    effect.SelectionsRemaining--;
                    if (effect.SelectionsRemaining > 0) return EffectResolution.WaitingForTarget;
                    return EffectResolution.Resolved;
                }
            }

            // ---- Play from deck: "Play up to N <filter> (Character card )?(with cost/power cap)?
            // from your deck(, then shuffle your deck)." → deck search in PLAY mode. -----------
            {
                var pfdM = System.Text.RegularExpressions.Regex.Match(text,
                    @"Play up to (\d+) ([^.]*?) from your deck",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (pfdM.Success)
                {
                    string pfdDesc = pfdM.Groups[2].Value;
                    int pfdCost = ParseLimit(text, @"cost of (\d+)(?: or less)?");
                    int pfdPower = ParseLimit(text, @"(\d{3,5}) power or less");
                    string pfdFeat = ParseCurlyBraceTag(pfdDesc);
                    var pfdName = System.Text.RegularExpressions.Regex.Match(pfdDesc, @"\[([^\]]+)\]");
                    bool pfdRested = ContainsAll(text, "rested");
                    var pfdSrc = FindCardInstance(state, effect.SourceInstanceId) ?? owner.Leader;
                    if (pfdSrc != null)
                        StartDeckSearch(state, effect.Seat, pfdSrc, pfdFeat, pfdCost, "character",
                            true, pfdRested, pfdName.Success ? pfdName.Groups[1].Value.Trim() : null, pfdPower);
                    return EffectResolution.Resolved;
                }
            }

            // ---- "Look at N cards from the top of your deck (and|;) play up to N <filter>.
            // Then, place the rest at the bottom / trash the rest." → deck look in PLAY mode. --
            if (ContainsAll(text, "Look at") && ContainsAll(text, "from the top of your deck")
                && ContainsAll(text, "play up to"))
            {
                int lpN = ParseLookCount(text);
                int lpCost = ParseLimit(text, @"cost of (\d+)(?: or less)?");
                int lpPower = ParseLimit(text, @"(\d{3,5}) power or less");
                string lpFeat = ParseCurlyBraceTag(text);
                var lpIncl = System.Text.RegularExpressions.Regex.Match(text, @"type including ""([^""]+)""");
                if (lpIncl.Success && string.IsNullOrEmpty(lpFeat)) lpFeat = lpIncl.Groups[1].Value.Trim();
                var lpSrc = FindCardInstance(state, effect.SourceInstanceId) ?? owner.Leader;
                if (lpSrc != null)
                    StartDeckLook(state, effect.Seat, lpSrc, lpFeat, lpN, null, "character",
                        ContainsAll(text, "trash the rest"), true, ContainsAll(text, "play") && ContainsAll(text, "rested"),
                        lpCost, lpPower);
                return EffectResolution.Resolved;
            }

            // ---- "Reveal N card from the top of your deck. If that card is <filter>, (you
            // may) play/add it …" — fully public information, so auto-resolve. --------------
            if (ContainsAll(text, "Reveal") && !ContainsAll(text, "Look at")
                && (ContainsAll(text, "from the top of your deck") || ContainsAll(text, "from your deck and add it to your hand")))
            {
                if (owner.Deck.Count == 0)
                {
                    Log(state, effect.Seat, $"{sourceName}: the deck is empty.");
                    return EffectResolution.Resolved;
                }
                // "Reveal up to N [X] from your deck and add it to your hand. Then, shuffle." — a search.
                if (ContainsAll(text, "from your deck and add it to your hand") && ContainsAll(text, "shuffle"))
                {
                    var rvName = System.Text.RegularExpressions.Regex.Match(text, @"\[([^\]]+)\]");
                    var rvSrc2 = FindCardInstance(state, effect.SourceInstanceId) ?? owner.Leader;
                    if (rvSrc2 != null)
                        StartDeckSearch(state, effect.Seat, rvSrc2, ParseCurlyBraceTag(text), ParseCostFilter(text), "",
                            false, false, rvName.Success ? rvName.Groups[1].Value.Trim() : null);
                    return EffectResolution.Resolved;
                }
                var revealed = owner.Deck[0];
                var revDef = GetCard(revealed);
                Log(state, effect.Seat, $"{sourceName} reveals {NameId(revDef)} from the top of the deck.");
                // Condition on the revealed card.
                bool revMatch = true;
                string revFeat = ParseCurlyBraceTag(text);
                var revIncl = System.Text.RegularExpressions.Regex.Match(text, @"type includes ""([^""]+)""");
                if (revIncl.Success) revMatch &= revDef.HasFeature(revIncl.Groups[1].Value.Trim());
                else if (!string.IsNullOrEmpty(revFeat)) revMatch &= revDef.HasFeature(revFeat);
                if (ContainsAll(text, "Character card")) revMatch &= revDef.Type == "character";
                int revCost = ParseLimit(text, @"cost of (\d+)(?: or less)?");
                if (revCost >= 0) revMatch &= revDef.Cost <= revCost;
                var revExcl = System.Text.RegularExpressions.Regex.Match(text, @"other than \[([^\]]+)\]");
                if (revExcl.Success) revMatch &= !NameMatches(state, revealed, revExcl.Groups[1].Value.Trim());
                if (revMatch && (ContainsAll(text, "play that card") || ContainsAll(text, "play up to")))
                {
                    int slotRv = owner.CharacterArea.FindIndex(c => c == null);
                    if (slotRv >= 0 && revDef.Type == "character")
                    {
                        Shift(owner.Deck);
                        revealed.Zone = "character";
                        revealed.PlayedOnTurn = state.TurnNumber;
                        revealed.Rested = ContainsAll(text, "rested");
                        owner.CharacterArea[slotRv] = revealed;
                        Log(state, effect.Seat, $"{sourceName} plays {NameId(revDef)}.");
                        if (HasTiming(revDef.Effect, "On Play"))
                            QueueAndAutoResolve(state, effect.Seat, revealed, "onPlay",
                                ExtractTimedClause(revDef.Effect, "On Play"), true, EffectScope.Instant, InferTargetZone(revDef.Effect));
                    }
                    else Log(state, effect.Seat, "The revealed card stays on top (no slot or not a Character).");
                }
                else if (revMatch && (ContainsAll(text, "add it to your hand") || ContainsAll(text, "add up to")))
                {
                    Shift(owner.Deck);
                    revealed.Zone = "hand";
                    owner.Hand.Add(revealed);
                    Log(state, effect.Seat, $"{sourceName} adds {NameId(revDef)} to hand.");
                }
                else if (revMatch && ContainsAll(text, "this Character gains"))
                {
                    int revBonus = ParsePowerGain(text);
                    var revSelf = FindAnyInPlay(state, effect.SourceInstanceId, out _);
                    if (revBonus > 0 && revSelf != null)
                    {
                        bool revBattle = ContainsAll(text, "during this battle") && state.Battle != null;
                        if (revBattle)
                        {
                            state.Battle.BattlePowerBonus.TryGetValue(revSelf.InstanceId, out var exRv);
                            state.Battle.BattlePowerBonus[revSelf.InstanceId] = exRv + revBonus;
                            RegisterPowerModifier(revSelf, sourceName, revBonus, "endOfBattle");
                        }
                        else
                        {
                            state.TemporaryPowerBonus.TryGetValue(revSelf.InstanceId, out var exRv);
                            state.TemporaryPowerBonus[revSelf.InstanceId] = exRv + revBonus;
                            RegisterPowerModifier(revSelf, sourceName, revBonus, "endOfTurn");
                        }
                        Log(state, effect.Seat, $"{sourceName} gains +{revBonus} power.");
                    }
                }
                else if (!revMatch)
                {
                    Log(state, effect.Seat, "The revealed card does not match — it stays on top of the deck.");
                }
                // "Then, place the rest at the bottom" style riders on single-reveal texts are
                // no-ops here (the card either moved or stays on top).
                return EffectResolution.Resolved;
            }

            // ---- Unified K.O. / trash removal: "K.O./Trash up to N of your opponent's
            // (rested) Characters …" with static caps (cost≤/exact, base cost, power≤,
            // TOTAL power≤) or dynamic caps ("equal to or less than the number of …").
            {
                // "K.O. all Characters with a cost of N or less." — board-wide, both sides.
                var koAll = System.Text.RegularExpressions.Regex.Match(text,
                    @"K\.O\. all Characters with a cost of (\d+) or less",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (koAll.Success)
                {
                    int kaCap = int.Parse(koAll.Groups[1].Value);
                    int kaC = 0;
                    foreach (var st3 in Seats())
                    {
                        var pk = Player(state, st3);
                        for (int i = 0; i < pk.CharacterArea.Count; i++)
                        {
                            var ck = pk.CharacterArea[i];
                            if (ck == null || GetCost(state, ck) > kaCap) continue;
                            if (st3 != effect.Seat && CannotBeKoedByEffect(state, ck)) continue;
                            if (st3 != effect.Seat && TryRemovalReplacement(state, st3, ck)) continue;
                            MoveToTrash(state, st3, ck.InstanceId);
                            kaC++;
                        }
                    }
                    Log(state, effect.Seat, $"{sourceName} K.O.s {kaC} Character(s).");
                    return EffectResolution.Resolved;
                }
                bool koAnySide = false;
                var koM = System.Text.RegularExpressions.Regex.Match(text,
                    @"(?:K\.O\.|[Tt]rash|Choose) up to (?:a total of )?(\d+) of your opponent's (?:(rested )?)Characters",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (!koM.Success)
                {
                    // Owner-agnostic: "K.O. up to N Character(s) with a cost of N or less."
                    koM = System.Text.RegularExpressions.Regex.Match(text,
                        @"K\.O\. up to (\d+) (?:(rested )?)Characters?\b",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    koAnySide = koM.Success;
                }
                // "Choose up to N … and K.O. it" variant must actually K.O.
                bool koChooseVariant = text.IndexOf("Choose up to", StringComparison.OrdinalIgnoreCase) >= 0;
                if (koM.Success && koChooseVariant && !ContainsAll(text, "K.O.")) koM = System.Text.RegularExpressions.Match.Empty;
                if (koM.Success && koM.Groups.Count > 1)
                {
                    if (RemoverCannotRemoveByEffect(state, effect.Seat))
                    {
                        Log(state, effect.Seat, $"{sourceName}: your opponent's Characters cannot be removed by your effects.");
                        return EffectResolution.Resolved;
                    }
                    if (effect.SelectionsRemaining <= 0)
                        effect.SelectionsRemaining = int.Parse(koM.Groups[1].Value);
                    bool needsRested = koM.Groups[2].Success && koM.Groups[2].Value.Trim().Length > 0;
                    bool needsBlocker = ContainsAll(text, "[Blocker]") || ContainsAll(text, "Blocker Characters");
                    int koCostCap = ParseLimit(text, @"cost of (\d+) or less");
                    bool baseCost = ContainsAll(text, "base cost");
                    int koCostExact = koCostCap >= 0 ? -1 : ParseLimit(text, @"cost of (\d+)\b(?! or)");
                    int koPowerCap = ParseLimit(text, @"(\d{3,5}) power or less");
                    if (koPowerCap < 0) koPowerCap = ParseLimit(text, @"power of (\d{3,5}) or less");
                    int dynCap = ComputeDynamicCap(state, effect.Seat, text);
                    if (dynCap >= 0) koCostCap = dynCap;
                    // Total-power budget shared across picks.
                    int totalPower = ParseLimit(text, @"total power of (\d{3,6}) or less");
                    if (totalPower >= 0 && effect.RemainingBudget < 0) effect.RemainingBudget = totalPower;
                    var koTarget = FindAnyInPlay(state, targetId, out var koSeat);
                    if (koTarget == null)
                    {
                        Log(state, effect.Seat, $"Choose up to {effect.SelectionsRemaining} opponent Character(s) to remove ({sourceName}), or skip.");
                        return EffectResolution.WaitingForTarget;
                    }
                    var koDef = GetCard(koTarget);
                    int effKoCost = baseCost ? koDef.Cost : GetCost(state, koTarget);
                    bool costOk = koCostCap >= 0 ? effKoCost <= koCostCap : (koCostExact < 0 || effKoCost == koCostExact);
                    bool powerOk = koPowerCap < 0 || GetPower(state, koTarget) <= koPowerCap;
                    bool budgetOk = effect.RemainingBudget < 0 || GetPower(state, koTarget) <= effect.RemainingBudget;
                    bool blockerOk = !needsBlocker || HasKeyword(koTarget, "Blocker") || HasKeywordModifier(state, koTarget, "Blocker");
                    if ((!koAnySide && koSeat != OtherSeat(effect.Seat)) || koDef.Type != "character"
                        || (needsRested && !koTarget.Rested) || !costOk || !powerOk || !budgetOk || !blockerOk)
                    {
                        Log(state, effect.Seat, "That is not a valid removal target.");
                        return EffectResolution.WaitingForTarget;
                    }
                    if (CannotBeKoedByEffect(state, koTarget))
                    {
                        Log(state, effect.Seat, $"{NameId(koDef)} cannot be K.O.'d by effects.");
                        return EffectResolution.WaitingForTarget;
                    }
                    if (TryRemovalReplacement(state, koSeat, koTarget))
                    {
                        // Removal replaced by the defender's effect — the pick is consumed.
                    }
                    else
                    {
                        if (effect.RemainingBudget >= 0) effect.RemainingBudget -= GetPower(state, koTarget);
                        MoveToTrash(state, koSeat, koTarget.InstanceId);
                        Log(state, effect.Seat, $"{sourceName} K.O.s {NameId(koDef)}.");
                        NotifyRemovalByEffect(state, effect.Seat);
                    }
                    effect.SelectionsRemaining--;
                    if (effect.SelectionsRemaining > 0)
                    {
                        Log(state, effect.Seat, $"You may remove {effect.SelectionsRemaining} more, or skip.");
                        return EffectResolution.WaitingForTarget;
                    }
                    return EffectResolution.Resolved;
                }
            }

            // ---- Generic "Set up to N of your … as active" (feature/cost filters, multi). --
            {
                var saM = System.Text.RegularExpressions.Regex.Match(text,
                    @"Set up to (\d+) of your (.*?) as active",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (saM.Success && !ContainsAll(saM.Groups[2].Value, "DON!!"))
                {
                    if (effect.SelectionsRemaining <= 0)
                        effect.SelectionsRemaining = int.Parse(saM.Groups[1].Value);
                    string saDesc = saM.Groups[2].Value;
                    bool saLeaderOk = ContainsAll(saDesc, "Leader");
                    int saCap = ParseLimit(text, @"cost of (\d+) or less");
                    var saRange = System.Text.RegularExpressions.Regex.Match(text, @"cost of (\d+) to (\d+)");
                    var saTarget = FindAnyInPlay(state, targetId, out var saSeat);
                    if (saTarget == null)
                    {
                        Log(state, effect.Seat, $"Choose up to {effect.SelectionsRemaining} of your rested card(s) to set active ({sourceName}), or skip.");
                        return EffectResolution.WaitingForTarget;
                    }
                    var saDef = GetCard(saTarget);
                    bool saTypeOk = saDef.Type == "character" || (saLeaderOk && saDef.Type == "leader");
                    if (saSeat != effect.Seat || !saTypeOk || !saTarget.Rested
                        || (saCap >= 0 && GetCost(state, saTarget) > saCap)
                        || (saRange.Success && (GetCost(state, saTarget) < int.Parse(saRange.Groups[1].Value)
                                             || GetCost(state, saTarget) > int.Parse(saRange.Groups[2].Value)))
                        || !CardPassesFeatureFilter(saDesc, saDef)
                        || HasModifier(state, saTarget, "freeze"))
                    {
                        Log(state, effect.Seat, "That is not a valid target to set active.");
                        return EffectResolution.WaitingForTarget;
                    }
                    saTarget.Rested = false;
                    Log(state, effect.Seat, $"{sourceName} sets {NameId(saDef)} as active.");
                    // Trailing rider: "That Character gains [KW] until … / during this turn."
                    var saKw = System.Text.RegularExpressions.Regex.Match(text,
                        @"That Character gains \[([A-Za-z !]+)\]");
                    if (saKw.Success)
                    {
                        string saDur = ContainsAll(text, "until the") ? "untilNextTurn" : "thisTurn";
                        AddModifier(state, FindAnyInPlay(state, effect.SourceInstanceId, out _), saTarget,
                            "keyword", saDur, saKw.Groups[1].Value.Trim(), effect.Seat);
                        Log(state, effect.Seat, $"{NameId(saDef)} gains [{saKw.Groups[1].Value.Trim()}].");
                    }
                    effect.SelectionsRemaining--;
                    if (effect.SelectionsRemaining > 0)
                    {
                        Log(state, effect.Seat, $"You may set {effect.SelectionsRemaining} more as active, or skip.");
                        return EffectResolution.WaitingForTarget;
                    }
                    return EffectResolution.Resolved;
                }
            }

            // ---- "Rest this Character and up to N of your opponent's Characters." ----------
            if (ContainsAll(text, "Rest this Character and up to"))
            {
                if (effect.SelectionsRemaining <= 0)
                {
                    var rsUp = System.Text.RegularExpressions.Regex.Match(text, @"up to (\d+)");
                    effect.SelectionsRemaining = rsUp.Success ? int.Parse(rsUp.Groups[1].Value) : 1;
                    var rsSelf = FindAnyInPlay(state, effect.SourceInstanceId, out _);
                    if (rsSelf != null && !rsSelf.Rested)
                    {
                        rsSelf.Rested = true;
                        Log(state, effect.Seat, $"{sourceName} rests itself.");
                    }
                }
                var rsT = FindAnyInPlay(state, targetId, out var rsSeat);
                if (rsT == null)
                {
                    Log(state, effect.Seat, $"Choose up to {effect.SelectionsRemaining} opponent Character(s) to rest ({sourceName}), or skip.");
                    return EffectResolution.WaitingForTarget;
                }
                if (rsSeat != OtherSeat(effect.Seat) || GetCard(rsT).Type != "character"
                    || HasModifier(state, rsT, "cannotBeRested"))
                {
                    Log(state, effect.Seat, "That is not a valid target to rest.");
                    return EffectResolution.WaitingForTarget;
                }
                rsT.Rested = true;
                Log(state, effect.Seat, $"{sourceName} rests {NameId(GetCard(rsT))}.");
                effect.SelectionsRemaining--;
                if (effect.SelectionsRemaining > 0) return EffectResolution.WaitingForTarget;
                return EffectResolution.Resolved;
            }

            // ---- "Rest all of your opponent's Characters." -------------------------------
            if (ContainsAll(text, "Rest all of your opponent's Characters"))
            {
                var oppAll = Player(state, OtherSeat(effect.Seat));
                int restedAll = 0;
                foreach (var c in oppAll.CharacterArea)
                    if (c != null && !c.Rested && !HasModifier(state, c, "cannotBeRested")) { c.Rested = true; restedAll++; }
                Log(state, effect.Seat, $"{sourceName} rests {restedAll} of the opponent's Characters.");
                return EffectResolution.Resolved;
            }

            // ---- "Rest up to N of your opponent's DON!! cards" (pure DON — fungible). -----
            {
                var rdM = System.Text.RegularExpressions.Regex.Match(text,
                    @"Rest up to (\d+) of your opponent's DON!! cards(?! or)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (rdM.Success)
                {
                    int rdN = int.Parse(rdM.Groups[1].Value);
                    var oppRd = Player(state, OtherSeat(effect.Seat));
                    int rdDone = 0;
                    foreach (var d in oppRd.CostArea) { if (rdDone >= rdN) break; if (!d.Rested) { d.Rested = true; rdDone++; } }
                    Log(state, effect.Seat, $"{sourceName} rests {rdDone} of the opponent's DON!!.");
                    return EffectResolution.Resolved;
                }
            }

            // ---- Mixed "Rest up to N of your opponent's DON!! cards or … Characters …" ----
            // Click an opponent Character to rest it, or press the resolve button with no
            // target to rest one of their DON!! instead (DON are fungible).
            {
                var rmixM = System.Text.RegularExpressions.Regex.Match(text,
                    @"Rest up to (?:a total of )?(\d+) of your opponent's (Characters or DON!! cards|DON!! cards or [^.]*Characters[^.]*)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (rmixM.Success)
                {
                    if (effect.SelectionsRemaining <= 0) effect.SelectionsRemaining = int.Parse(rmixM.Groups[1].Value);
                    int rmixCap = ParseLimit(text, @"cost of (\d+) or less");
                    var rmixT = FindAnyInPlay(state, targetId, out var rmixSeat);
                    if (rmixT == null)
                    {
                        // No target: rest one opponent DON!! for this pick.
                        var oppMix = Player(state, OtherSeat(effect.Seat));
                        var freeDonMix = oppMix.CostArea.FirstOrDefault(d => !d.Rested);
                        if (freeDonMix == null)
                        {
                            Log(state, effect.Seat, $"Click an opponent Character to rest for {sourceName} (no active DON!! to rest), or skip.");
                            return EffectResolution.WaitingForTarget;
                        }
                        freeDonMix.Rested = true;
                        Log(state, effect.Seat, $"{sourceName} rests 1 of the opponent's DON!!.");
                    }
                    else
                    {
                        if (rmixSeat != OtherSeat(effect.Seat) || GetCard(rmixT).Type != "character"
                            || (rmixCap >= 0 && GetCost(state, rmixT) > rmixCap)
                            || !CardPassesFeatureFilter(text, GetCard(rmixT))
                            || HasModifier(state, rmixT, "cannotBeRested"))
                        {
                            Log(state, effect.Seat, "That is not a valid target to rest.");
                            return EffectResolution.WaitingForTarget;
                        }
                        rmixT.Rested = true;
                        Log(state, effect.Seat, $"{sourceName} rests {NameId(GetCard(rmixT))}.");
                    }
                    effect.SelectionsRemaining--;
                    if (effect.SelectionsRemaining > 0)
                    {
                        Log(state, effect.Seat, $"You may rest {effect.SelectionsRemaining} more (click a Character, or resolve to rest a DON!!), or skip.");
                        return EffectResolution.WaitingForTarget;
                    }
                    return EffectResolution.Resolved;
                }
            }

            // ---- Mandatory self-discard: "Trash N card(s) from your hand." -----------------
            {
                var mtM = System.Text.RegularExpressions.Regex.Match(text,
                    @"^Trash (\d+) cards? from your hand\.?$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (mtM.Success)
                {
                    if (effect.SelectionsRemaining <= 0)
                    {
                        effect.SelectionsRemaining = Math.Min(int.Parse(mtM.Groups[1].Value), owner.Hand.Count);
                        effect.TargetZone = EffectTargetZone.Hand;
                        if (effect.SelectionsRemaining == 0) return EffectResolution.Resolved;
                    }
                    if (string.IsNullOrEmpty(targetId))
                    {
                        Log(state, effect.Seat, $"Click {effect.SelectionsRemaining} card(s) in your hand to trash ({sourceName}).");
                        return EffectResolution.WaitingForTarget;
                    }
                    int mtIdx = owner.Hand.FindIndex(c => c.InstanceId == targetId);
                    if (mtIdx < 0) { Log(state, effect.Seat, "That card is not in your hand."); return EffectResolution.WaitingForTarget; }
                    var mtCard = owner.Hand[mtIdx];
                    owner.Hand.RemoveAt(mtIdx);
                    mtCard.Zone = "trash";
                    owner.Trash.Add(mtCard);
                    NotifyHandTrashedByEffect(state, effect.Seat);
                    Log(state, effect.Seat, $"{sourceName}: trashed {NameId(GetCard(mtCard))} from hand.");
                    effect.SelectionsRemaining--;
                    if (effect.SelectionsRemaining > 0) return EffectResolution.WaitingForTarget;
                    return EffectResolution.Resolved;
                }
            }

            // ---- "Your opponent trashes N card(s) from their hand." (auto: last cards) ----
            {
                var otM = System.Text.RegularExpressions.Regex.Match(text,
                    @"opponent (?:trashes|must trash) (\d+) cards? from their hand",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (otM.Success)
                {
                    int otN = int.Parse(otM.Groups[1].Value);
                    var oppOt = Player(state, OtherSeat(effect.Seat));
                    int otDone = 0;
                    for (int i = 0; i < otN && oppOt.Hand.Count > 0; i++)
                    {
                        var oc = oppOt.Hand[oppOt.Hand.Count - 1];
                        oppOt.Hand.RemoveAt(oppOt.Hand.Count - 1);
                        oc.Zone = "trash";
                        oppOt.Trash.Add(oc);
                        otDone++;
                    }
                    Log(state, effect.Seat, $"{sourceName}: opponent trashes {otDone} card(s) from hand.");
                    return EffectResolution.Resolved;
                }
            }

            // ---- Base power set: "…'s base power becomes N …" / "Set the power of up to N
            // of your opponent's Characters to N during this turn." ------------------------
            {
                // Self / own Leader: "This Character's / Your Leader's base power becomes N".
                var bpSelf = System.Text.RegularExpressions.Regex.Match(text,
                    @"(This Character's|Your (?:\{[^}]+\} type )?Leader's) base power becomes (\d{3,6})",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (bpSelf.Success)
                {
                    bool isLeaderBp = bpSelf.Groups[1].Value.IndexOf("Leader", StringComparison.OrdinalIgnoreCase) >= 0;
                    var bpTarget = isLeaderBp ? owner.Leader : FindAnyInPlay(state, effect.SourceInstanceId, out _);
                    if (bpTarget != null)
                    {
                        string bpDur = ContainsAll(text, "until the end of your opponent's next") || ContainsAll(text, "until the start of your next turn")
                            ? "untilNextTurn" : "thisTurn";
                        state.BasePowerOverrides.Add(new BasePowerOverride
                        {
                            TargetInstanceId = bpTarget.InstanceId,
                            Value = int.Parse(bpSelf.Groups[2].Value),
                            OwnerSeat = effect.Seat,
                            Duration = bpDur,
                        });
                        Log(state, effect.Seat, $"{sourceName}: {NameId(GetCard(bpTarget))}'s base power becomes {bpSelf.Groups[2].Value}.");
                        return EffectResolution.Resolved;
                    }
                }
                // "base power becomes the same as your opponent's (attacking) Leader …"
                if (ContainsAll(text, "base power becomes the same as") && ContainsAll(text, "opponent"))
                {
                    var bpSrc = FindAnyInPlay(state, effect.SourceInstanceId, out _);
                    var oppLead = Player(state, OtherSeat(effect.Seat)).Leader;
                    CardInstance mirror = oppLead;
                    if (ContainsAll(text, "attacking") && state.Battle != null)
                        mirror = FindAnyInPlay(state, state.Battle.AttackerId, out _) ?? oppLead;
                    if (ContainsAll(text, "the selected Character"))
                    {
                        var selT = FindAnyInPlay(state, targetId, out var selSeat);
                        if (selT == null)
                        {
                            Log(state, effect.Seat, $"Select an opponent's Character to copy power from ({sourceName}).");
                            return EffectResolution.WaitingForTarget;
                        }
                        mirror = selT;
                    }
                    if (bpSrc != null && mirror != null)
                    {
                        string bpDur2 = ContainsAll(text, "until the start of your next turn") ? "untilNextTurn" : "thisTurn";
                        state.BasePowerOverrides.Add(new BasePowerOverride
                        {
                            TargetInstanceId = bpSrc.InstanceId,
                            Value = GetCard(mirror).Power,
                            OwnerSeat = effect.Seat,
                            Duration = bpDur2,
                        });
                        Log(state, effect.Seat, $"{sourceName}'s base power becomes {GetCard(mirror).Power}.");
                        return EffectResolution.Resolved;
                    }
                }
                // "Set the power of up to N of your opponent's Characters to N during this turn."
                var bpSet = System.Text.RegularExpressions.Regex.Match(text,
                    @"Set the (?:power|cost) of up to (\d+) of your opponent's Characters[^.]*? to (\d+) during this turn",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (bpSet.Success)
                {
                    bool isCostSet = text.IndexOf("Set the cost", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (effect.SelectionsRemaining <= 0) effect.SelectionsRemaining = int.Parse(bpSet.Groups[1].Value);
                    int bpVal = int.Parse(bpSet.Groups[2].Value);
                    var bpT = FindAnyInPlay(state, targetId, out var bpSeat2);
                    if (bpT == null)
                    {
                        Log(state, effect.Seat, $"Choose up to {effect.SelectionsRemaining} opponent Character(s) ({sourceName}), or skip.");
                        return EffectResolution.WaitingForTarget;
                    }
                    if (bpSeat2 != OtherSeat(effect.Seat) || GetCard(bpT).Type != "character")
                    {
                        Log(state, effect.Seat, "That is not a valid target.");
                        return EffectResolution.WaitingForTarget;
                    }
                    if (isCostSet)
                    {
                        int deltaCost = bpVal - GetCost(state, bpT);
                        RegisterCostModifier(bpT, sourceName, deltaCost, "endOfTurn");
                        Log(state, effect.Seat, $"{sourceName}: {NameId(GetCard(bpT))}'s cost becomes {bpVal} this turn.");
                    }
                    else
                    {
                        state.BasePowerOverrides.Add(new BasePowerOverride
                        {
                            TargetInstanceId = bpT.InstanceId, Value = bpVal, OwnerSeat = effect.Seat, Duration = "thisTurn",
                        });
                        Log(state, effect.Seat, $"{sourceName}: {NameId(GetCard(bpT))}'s power becomes {bpVal} this turn.");
                    }
                    effect.SelectionsRemaining--;
                    if (effect.SelectionsRemaining > 0) return EffectResolution.WaitingForTarget;
                    return EffectResolution.Resolved;
                }
            }

            // ---- "until the start of your next turn" power buffs ---------------------------
            {
                var untilM = System.Text.RegularExpressions.Regex.Match(text,
                    @"gains? \+(\d{3,5}) power until the (?:start of your next turn|end of your (?:opponent's )?next (?:End Phase|turn))",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (untilM.Success)
                {
                    int uBonus = int.Parse(untilM.Groups[1].Value);
                    // Board-wide: "Your Leader and all of your Characters gain +N power …"
                    if (ContainsAll(text, "all of your Characters") || ContainsAll(text, "All of your"))
                    {
                        int uCount = 0;
                        var uTargets = new List<CardInstance>();
                        if (ContainsAll(text, "Your Leader") && owner.Leader != null) uTargets.Add(owner.Leader);
                        foreach (var c in owner.CharacterArea)
                            if (c != null && CardPassesFeatureFilter(text, GetCard(c))) uTargets.Add(c);
                        foreach (var t in uTargets)
                        {
                            state.TimedPowerBonuses.Add(new TimedPowerBonus { TargetInstanceId = t.InstanceId, Delta = uBonus, OwnerSeat = effect.Seat });
                            RegisterPowerModifier(t, sourceName, uBonus, "permanent");
                            uCount++;
                        }
                        Log(state, effect.Seat, $"{sourceName} gives +{uBonus} power to {uCount} card(s) until the start of your next turn.");
                        return EffectResolution.Resolved;
                    }
                    // Self / own Leader / single target.
                    CardInstance uT = null;
                    if (ContainsAll(text, "This Character gains") || ContainsAll(text, "this Leader gains"))
                        uT = FindAnyInPlay(state, effect.SourceInstanceId, out _);
                    else if (ContainsAll(text, "Your Leader gains") || ContainsAll(text, "of your Leader gains")) uT = owner.Leader;
                    else
                    {
                        uT = FindAnyInPlay(state, targetId, out var uSeat);
                        if (uT == null)
                        {
                            Log(state, effect.Seat, $"Choose a card for +{uBonus} power ({sourceName}).");
                            return EffectResolution.WaitingForTarget;
                        }
                        if (uSeat != effect.Seat || !CardPassesFeatureFilter(text, GetCard(uT)))
                        {
                            Log(state, effect.Seat, "That is not a valid target.");
                            return EffectResolution.WaitingForTarget;
                        }
                    }
                    if (uT == null)
                    {
                        Log(state, effect.Seat, $"{sourceName} has left the field — the self-buff fizzles.");
                        return EffectResolution.Resolved;
                    }
                    state.TimedPowerBonuses.Add(new TimedPowerBonus { TargetInstanceId = uT.InstanceId, Delta = uBonus, OwnerSeat = effect.Seat });
                    RegisterPowerModifier(uT, sourceName, uBonus, "permanent");
                    Log(state, effect.Seat, $"{sourceName} gives {NameId(GetCard(uT))} +{uBonus} power until the start of your next turn.");
                    return EffectResolution.Resolved;
                }
            }

            // ---- Effect negation: "Negate the effect of up to N of your opponent's Leader
            // or Character cards during this turn." (optionally "and give that card −N power")
            if (ContainsAll(text, "Negate the effect of up to") || ContainsAll(text, "Negate the effects of up to"))
            {
                if (effect.SelectionsRemaining <= 0)
                {
                    var negN = System.Text.RegularExpressions.Regex.Match(text, @"Negate the effects? of up to (\d+)");
                    effect.SelectionsRemaining = negN.Success ? int.Parse(negN.Groups[1].Value) : 1;
                }
                var negT = FindAnyInPlay(state, targetId, out var negSeat);
                if (negT == null)
                {
                    Log(state, effect.Seat, $"Choose an opponent's Leader or Character to negate ({sourceName}), or skip.");
                    return EffectResolution.WaitingForTarget;
                }
                var negDef = GetCard(negT);
                int negCap = ParseLimit(text, @"cost of (\d+) or less");
                if (negSeat != OtherSeat(effect.Seat) || (negDef.Type != "leader" && negDef.Type != "character")
                    || (negCap >= 0 && GetCost(state, negT) > negCap))
                {
                    Log(state, effect.Seat, "That is not a valid negation target.");
                    return EffectResolution.WaitingForTarget;
                }
                AddModifier(state, FindAnyInPlay(state, effect.SourceInstanceId, out _), negT, "effectsNegated", "thisTurn", null, effect.Seat);
                Log(state, effect.Seat, $"{sourceName} negates {NameId(negDef)}'s effects this turn.");
                var negPwr = System.Text.RegularExpressions.Regex.Match(text, @"give that card [-\u2212\u2013\u2011\u2012\u2014](\d{3,5}) power");
                if (negPwr.Success)
                {
                    int nd = int.Parse(negPwr.Groups[1].Value);
                    state.TemporaryPowerBonus.TryGetValue(negT.InstanceId, out var exN);
                    state.TemporaryPowerBonus[negT.InstanceId] = exN - nd;
                    RegisterPowerModifier(negT, sourceName, -nd, "endOfTurn");
                    Log(state, effect.Seat, $"{sourceName} also gives {NameId(negDef)} -{nd} power this turn.");
                }
                effect.SelectionsRemaining--;
                if (effect.SelectionsRemaining > 0) return EffectResolution.WaitingForTarget;
                return EffectResolution.Resolved;
            }

            // ---- "You may deal N damage to your opponent." (life goes to their hand) -------
            {
                var dmgM = System.Text.RegularExpressions.Regex.Match(text,
                    @"deal (\d+) damage to your opponent",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (dmgM.Success)
                {
                    int dmgN = int.Parse(dmgM.Groups[1].Value);
                    var oppDmg = Player(state, OtherSeat(effect.Seat));
                    for (int i = 0; i < dmgN; i++)
                    {
                        var lifeD = Pop(oppDmg.Life);
                        if (lifeD == null)
                        {
                            FinishGame(state);
                            Log(state, "system", $"{Player(state, effect.Seat).Name} wins — damage with no Life left.");
                            return EffectResolution.Resolved;
                        }
                        lifeD.Zone = "hand";
                        oppDmg.Hand.Add(lifeD);
                    }
                    Log(state, effect.Seat, $"{sourceName} deals {dmgN} damage — opponent takes {dmgN} Life to hand.");
                    return EffectResolution.Resolved;
                }
            }

            // ---- "Give up to N rested DON!! card(s) to (each of) your … Characters." -------
            // (Variants without the word "Leader", plus auto-distribute "each of".)
            {
                var gdM = System.Text.RegularExpressions.Regex.Match(text,
                    @"Give up to (\d+) rested DON!! cards? to (each of )?",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (gdM.Success && !ContainsAll(text, "Leader"))
                {
                    int gdN = int.Parse(gdM.Groups[1].Value);
                    bool gdEach = gdM.Groups[2].Success && gdM.Groups[2].Value.Trim().Length > 0;
                    if (gdEach)
                    {
                        int given = 0;
                        foreach (var c in owner.CharacterArea)
                        {
                            if (c == null || !CardPassesFeatureFilter(text, GetCard(c))) continue;
                            var rd = owner.CostArea.FirstOrDefault(d => d.Rested);
                            if (rd == null) break;
                            owner.CostArea.Remove(rd);
                            c.AttachedDonIds.Add(rd.InstanceId);
                            given++;
                        }
                        Log(state, effect.Seat, $"{sourceName} gives 1 rested DON!! to each of {given} Character(s).");
                        return EffectResolution.Resolved;
                    }
                    var gdT = FindAnyInPlay(state, targetId, out var gdSeat);
                    if (gdT == null)
                    {
                        Log(state, effect.Seat, $"Choose one of your Characters to receive {gdN} rested DON!! ({sourceName}).");
                        return EffectResolution.WaitingForTarget;
                    }
                    if (gdSeat != effect.Seat || GetCard(gdT).Type != "character" || !CardPassesFeatureFilter(text, GetCard(gdT)))
                    {
                        Log(state, effect.Seat, "That is not a valid DON!! attachment target.");
                        return EffectResolution.WaitingForTarget;
                    }
                    var gdDon = owner.CostArea.Where(d => d.Rested).Take(gdN).ToList();
                    foreach (var d in gdDon) { owner.CostArea.Remove(d); gdT.AttachedDonIds.Add(d.InstanceId); }
                    Log(state, effect.Seat, $"{sourceName} gives {gdDon.Count} rested DON!! to {NameId(GetCard(gdT))}.");
                    return EffectResolution.Resolved;
                }
            }

            if (ContainsAll(text, "K.O. up to 1", "opponent's rested Characters", "cost of 3 or less"))
            {
                var target = FindAnyInPlay(state, targetId, out var targetSeat);
                if (target == null)
                {
                    Log(state, effect.Seat, $"Choose an opponent's rested character with cost 3 or less for {sourceName}.");
                    return EffectResolution.WaitingForTarget;
                }
                if (targetSeat != OtherSeat(effect.Seat) || GetCard(target).Type != "character" || !target.Rested || GetCost(state, target) > 3)
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
                if (targetSeat != effect.Seat || targetDef5.Type != "character" || !target.Rested || GetCost(state, target) > 5)
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

            // "Rest up to N of your opponent's Characters [with a cost of X or less]" —
            // multi-pick when N > 1 (e.g. ST05-011 Douglas Bullet rests up to 2), with the
            // cost cap enforced against the EFFECTIVE cost (was previously ignored).
            if (System.Text.RegularExpressions.Regex.IsMatch(text,
                    @"Rest up to \d+ of your opponent's [^.]*?(Characters|cards)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                bool restAllowLeader = ContainsAll(text, "Leader") || ContainsAll(text, "opponent's cards");
                // "[Name] Characters" filter (e.g. ST30-012).
                var restNameF = System.Text.RegularExpressions.Regex.Match(text, @"opponent's \[([^\]]+)\]");
                if (effect.SelectionsRemaining <= 0)
                {
                    var restM = System.Text.RegularExpressions.Regex.Match(text, @"Rest up to (\d+)",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    effect.SelectionsRemaining = restM.Success ? int.Parse(restM.Groups[1].Value) : 1;
                }
                int restCostCap = ParseLimit(text, @"cost (?:of )?(\d+) or less");
                var target = FindAnyInPlay(state, targetId, out var targetSeat);
                if (target == null)
                {
                    Log(state, effect.Seat, $"Choose an opponent's character{(restCostCap >= 0 ? $" (cost ≤ {restCostCap})" : "")} to rest for {sourceName}.");
                    return EffectResolution.WaitingForTarget;
                }
                var restDefT = GetCard(target);
                bool restTypeOk = restDefT.Type == "character" || (restAllowLeader && restDefT.Type == "leader");
                if (targetSeat != OtherSeat(effect.Seat) || !restTypeOk
                    || (restNameF.Success && !NameMatches(state, target, restNameF.Groups[1].Value.Trim()))
                    || !CardPassesFeatureFilter(text, restDefT)
                    || (restCostCap >= 0 && GetCost(state, target) > restCostCap))
                {
                    Log(state, effect.Seat, "That is not a valid target to rest.");
                    return EffectResolution.WaitingForTarget;
                }
                if (HasModifier(state, target, "cannotBeRested")
                    || ContainsAll(restDefT.Effect ?? "", "cannot be rested by your opponent's"))
                {
                    Log(state, effect.Seat, $"{NameId(restDefT)} cannot be rested.");
                    return EffectResolution.WaitingForTarget;
                }
                target.Rested = true;
                Log(state, effect.Seat, $"{sourceName} rests {NameId(GetCard(target))}.");
                effect.SelectionsRemaining--;
                if (effect.SelectionsRemaining > 0)
                {
                    Log(state, effect.Seat, $"You may rest {effect.SelectionsRemaining} more Character(s), or skip.");
                    return EffectResolution.WaitingForTarget;
                }
                return EffectResolution.Resolved;
            }

            // Power buff: "up to 1 of your Leader or Character / your Characters gains +NNNN power
            // during this turn/battle." Previously required the literal phrase "Leader or Character",
            // which silently skipped leaders/cards phrased "up to 1 of your Characters gains +N power"
            // (e.g. the ST11-001 Uta leader's [When Attacking]) — those fell through to "manual
            // resolution" and visibly did nothing. Now any single-target buff with a turn/battle
            // duration resolves; the words "Leader"/"Character" in the text scope the legal targets.
            if (System.Text.RegularExpressions.Regex.IsMatch(text, @"gains? \+\d{3,5} power")
                && !ContainsAll(text, "Choose one")
                && (ContainsAll(text, "during this turn") || ContainsAll(text, "during this battle"))
                && (ContainsAll(text, "Leader") || ContainsAll(text, "Character") || ContainsAll(text, "of your cards")
                    || System.Text.RegularExpressions.Regex.IsMatch(text, @"of your \[[^\]]+\] cards")))
            {
                int bonus = ParsePowerGain(text);
                if (bonus <= 0)
                {
                    var gPl = System.Text.RegularExpressions.Regex.Match(text, @"gain \+(\d{3,5}) power");
                    if (gPl.Success) bonus = int.Parse(gPl.Groups[1].Value);
                }
                if (bonus <= 0) return EffectResolution.NotAutomated;
                bool isBattle = ContainsAll(text, "during this battle");
                bool anyCard = ContainsAll(text, "of your cards");
                bool allowLeader = ContainsAll(text, "Leader") || anyCard;
                bool allowChar = ContainsAll(text, "Character") || anyCard;

                bool selfBuff = ContainsAll(text, "This Character gains") || ContainsAll(text, "this card gains")
                    || ContainsAll(text, "this Leader gains");
                // Multi-target buffs: "Up to 2 of your Characters gain +N power" /
                // "up to a total of 2 of your Leader or Character cards".
                if (!selfBuff && effect.SelectionsRemaining <= 0)
                {
                    var buffUpTo = System.Text.RegularExpressions.Regex.Match(text, @"[Uu]p to (?:a total of )?(\d+)");
                    effect.SelectionsRemaining = buffUpTo.Success ? int.Parse(buffUpTo.Groups[1].Value) : 1;
                }
                CardInstance target;
                string targetSeat;
                // Self-buffs ("This Character gains +N power during this turn") need no click.
                if (selfBuff)
                {
                    target = FindAnyInPlay(state, effect.SourceInstanceId, out targetSeat);
                    if (target == null)
                    {
                        Log(state, effect.Seat, $"{sourceName} has left the field — the self-buff fizzles.");
                        return EffectResolution.Resolved;
                    }
                }
                else
                {
                    target = FindAnyInPlay(state, targetId, out targetSeat);
                }
                if (target == null)
                {
                    Log(state, effect.Seat, $"Choose a {(allowLeader && allowChar ? "Leader or Character" : allowLeader ? "Leader" : "Character")} for +{bonus} power ({sourceName}).");
                    return EffectResolution.WaitingForTarget;
                }
                var targetDef = GetCard(target);
                // "[Name] cards" targeting allows either leader or character with that name.
                var buffNameF = System.Text.RegularExpressions.Regex.Match(text, @"of your \[([^\]]+)\] cards");
                if (buffNameF.Success) { allowLeader = true; allowChar = true; }
                bool typeOk = (targetDef.Type == "leader" && allowLeader) || (targetDef.Type == "character" && allowChar);
                if (targetSeat != effect.Seat || !typeOk
                    || (buffNameF.Success && !NameMatches(state, target, buffNameF.Groups[1].Value.Trim())))
                {
                    Log(state, effect.Seat, "That is not a valid power-buff target.");
                    return EffectResolution.WaitingForTarget;
                }
                // "other than this card" (e.g. Jinbe ST01-005) excludes the source itself.
                if (ContainsAll(text, "other than this card") && target.InstanceId == effect.SourceInstanceId)
                {
                    Log(state, effect.Seat, $"{sourceName} cannot target itself.");
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
                    RegisterPowerModifier(target, sourceName, bonus, "endOfBattle");
                    Log(state, effect.Seat, $"{sourceName} gives {NameId(targetDef)} +{bonus} power this battle.");
                }
                else
                {
                    state.TemporaryPowerBonus.TryGetValue(target.InstanceId, out var existing);
                    state.TemporaryPowerBonus[target.InstanceId] = existing + bonus;
                    RegisterPowerModifier(target, sourceName, bonus, "endOfTurn");
                    Log(state, effect.Seat, $"{sourceName} gives {NameId(targetDef)} +{bonus} power this turn.");
                }
                if (!selfBuff)
                {
                    effect.SelectionsRemaining--;
                    if (effect.SelectionsRemaining > 0)
                    {
                        Log(state, effect.Seat, $"You may buff {effect.SelectionsRemaining} more card(s), or skip.");
                        return EffectResolution.WaitingForTarget;
                    }
                }
                return EffectResolution.Resolved;
            }

            // Cost reduction: "Give up to 1 of your opponent's Characters -N cost during this turn."
            // (black-deck staple; the minus may be '-', '−' or '–'). Registers an authoritative
            // CostDelta ActiveModifier on the target — every cost filter reads GetCost().
            {
                var costRed = System.Text.RegularExpressions.Regex.Match(
                    text, @"[-\u2212\u2013\u2011\u2012\u2014](\d+)\s+cost", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (costRed.Success && ContainsAll(text, "opponent") && ContainsAll(text, "Character"))
                {
                    int reduction = int.Parse(costRed.Groups[1].Value);
                    var target = FindAnyInPlay(state, targetId, out var targetSeat);
                    if (target == null)
                    {
                        Log(state, effect.Seat, $"Choose an opponent's Character to give -{reduction} cost ({sourceName}).");
                        return EffectResolution.WaitingForTarget;
                    }
                    if (targetSeat != OtherSeat(effect.Seat) || GetCard(target).Type != "character")
                    {
                        Log(state, effect.Seat, "That is not a valid cost-reduction target.");
                        return EffectResolution.WaitingForTarget;
                    }
                    RegisterCostModifier(target, sourceName, -reduction, "endOfTurn");
                    Log(state, effect.Seat, $"{sourceName} gives {NameId(GetCard(target))} -{reduction} cost this turn (now {GetCost(state, target)}).");
                    return EffectResolution.Resolved;
                }
            }

            // [When Attacking] battle restrictions phrased generically (covers leaders beyond the
            // hardcoded ST01 ids): "Your opponent cannot activate [Blocker] during this battle." /
            // "…cannot activate a [Blocker] Character that has N or more power during this battle."
            if (ContainsAll(text, "cannot activate", "Blocker", "during this battle") && state.Battle != null)
            {
                int ban = ParseLimit(text, @"(\d{3,5}) or more power");
                if (ban > 0)
                {
                    state.Battle.BlockerPowerBan = ban;
                    Log(state, effect.Seat, $"{sourceName}: opponent cannot activate a [Blocker] with {ban}+ power this battle.");
                }
                else
                {
                    state.Battle.NoBlocker = true;
                    Log(state, effect.Seat, $"{sourceName}: opponent cannot activate [Blocker] this battle.");
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

            // Generalized "Play up to 1 … from your hand" (any cost cap / feature tag / card
            // type / "other than [Name]" exclusion). Covers e.g. Robin's [On Play] "Play up to 1
            // {FILM} type Character card with a cost of 4 or less from your hand." Previously
            // only the Straw Sword cost-2 wording was implemented, so these resolved as
            // "manual resolution" with no targets offered.
            if (System.Text.RegularExpressions.Regex.IsMatch(text, @"Play up to \d+", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                && ContainsAll(text, "from your hand") && effect.TargetZone == EffectTargetZone.Hand)
            {
                if (effect.SelectionsRemaining <= 0)
                {
                    var puM = System.Text.RegularExpressions.Regex.Match(text, @"Play up to (\d+)");
                    effect.SelectionsRemaining = puM.Success ? int.Parse(puM.Groups[1].Value) : 1;
                }
                if (string.IsNullOrEmpty(targetId))
                {
                    Log(state, effect.Seat, $"Click a card in your hand to play for {sourceName}, or skip.");
                    return EffectResolution.WaitingForTarget;
                }
                var pH = Player(state, effect.Seat);
                int hIdx2 = pH.Hand.FindIndex(c => c.InstanceId == targetId);
                if (hIdx2 < 0) { Log(state, effect.Seat, "That card is not in your hand."); return EffectResolution.WaitingForTarget; }
                var playCard = pH.Hand[hIdx2];
                var playDef = GetCard(playCard);
                // OP12-036: "This card in your hand cannot be played by effects."
                if (ContainsAll(playDef.Effect ?? "", "This card in your hand cannot be played by effects"))
                {
                    Log(state, effect.Seat, $"{NameId(playDef)} cannot be played by effects.");
                    return EffectResolution.WaitingForTarget;
                }
                if (ContainsAll(text, "Character card") && playDef.Type != "character")
                {
                    Log(state, effect.Seat, $"{NameId(playDef)} is not a Character card.");
                    return EffectResolution.WaitingForTarget;
                }
                if (ContainsAll(text, "with a [Trigger]") && string.IsNullOrEmpty(playDef.Trigger))
                {
                    Log(state, effect.Seat, $"{NameId(playDef)} has no [Trigger].");
                    return EffectResolution.WaitingForTarget;
                }
                int costCapH = ParseLimit(text, @"cost (?:of )?(\d+) or less");
                if (costCapH >= 0 && playDef.Cost > costCapH)
                {
                    Log(state, effect.Seat, $"{NameId(playDef)} costs too much for {sourceName} (max {costCapH}).");
                    return EffectResolution.WaitingForTarget;
                }
                if (!CardPassesFeatureFilter(text, playDef))
                {
                    Log(state, effect.Seat, $"{NameId(playDef)} does not match the required type for {sourceName}.");
                    return EffectResolution.WaitingForTarget;
                }
                var excl = System.Text.RegularExpressions.Regex.Match(text, @"other than \[([^\]]+)\]",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (excl.Success && string.Equals(GetEffectiveName(state, playCard), excl.Groups[1].Value.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    Log(state, effect.Seat, $"{sourceName} cannot play a card named [{excl.Groups[1].Value.Trim()}].");
                    return EffectResolution.WaitingForTarget;
                }
                // Play for free (no DON!! cost).
                pH.Hand.RemoveAt(hIdx2);
                playCard.PlayedOnTurn = state.TurnNumber;
                if (playDef.Type == "character")
                {
                    int openH = pH.CharacterArea.FindIndex(s => s == null);
                    if (openH < 0)
                    {
                        pH.Hand.Insert(hIdx2, playCard);
                        Log(state, effect.Seat, $"No open character slot — {NameId(playDef)} stays in hand.");
                        return EffectResolution.Resolved;
                    }
                    playCard.Zone = "character";
                    pH.CharacterArea[openH] = playCard;
                    if (HasTiming(playDef.Effect, "On Play"))
                        QueueAndAutoResolve(state, effect.Seat, playCard, "onPlay", playDef.Effect, true, EffectScope.Instant, InferTargetZone(playDef.Effect));
                }
                else if (playDef.Type == "stage")
                {
                    if (pH.Stage != null) MoveToTrash(state, effect.Seat, pH.Stage.InstanceId, true);
                    playCard.Zone = "stage";
                    pH.Stage = playCard;
                }
                else
                {
                    playCard.Zone = "trash";
                    pH.Trash.Add(playCard);
                    if (playDef.Type == "event" && HasTiming(playDef.Effect, "Main"))
                    {
                        string evCl = ExtractTimedClause(playDef.Effect, "Main");
                        QueueAndAutoResolve(state, effect.Seat, playCard, "main", evCl, true, EffectScope.Instant, InferTargetZone(evCl));
                    }
                }
                Log(state, effect.Seat, $"{sourceName} plays {NameId(playDef)} from hand for free.");
                effect.SelectionsRemaining--;
                if (effect.SelectionsRemaining > 0)
                {
                    Log(state, effect.Seat, $"You may play {effect.SelectionsRemaining} more card(s), or skip.");
                    return EffectResolution.WaitingForTarget;
                }
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
                if (targetSeat != OtherSeat(effect.Seat) || !HasKeyword(target, "Blocker") || GetCost(state, target) > 3)
                {
                    Log(state, effect.Seat, "That is not a valid target for this K.O. effect.");
                    return EffectResolution.WaitingForTarget;
                }
                MoveToTrash(state, targetSeat, target.InstanceId);
                Log(state, effect.Seat, $"{sourceName} K.O.s {NameId(GetCard(target))}.");
                return EffectResolution.Resolved;
            }

            // Generic K.O. by COST: "K.O. up to 1 of your opponent's Characters with a cost of N
            // or less" (any N) or "with a cost of N" (exact — e.g. "a cost of 0" after a -cost
            // effect). Uses the EFFECTIVE cost so -cost modifiers make targets legal. Checked
            // after the more specific rested/Blocker variants above.
            if (ContainsAll(text, "K.O. up to 1", "opponent") && ContainsAll(text, "cost of")
                && !ContainsAll(text, "rested") && !ContainsAll(text, "Blocker") && !ContainsAll(text, "power"))
            {
                int capLess = ParseLimit(text, @"cost (?:of )?(\d+) or less");
                int capExact = capLess >= 0 ? -1 : ParseLimit(text, @"cost of (\d+)\b(?! or)");
                var target = FindAnyInPlay(state, targetId, out var targetSeat);
                if (target == null)
                {
                    Log(state, effect.Seat, $"Choose an opponent's Character with cost {(capLess >= 0 ? capLess : capExact)}{(capLess >= 0 ? " or less" : "")} for {sourceName}.");
                    return EffectResolution.WaitingForTarget;
                }
                int effCost = GetCost(state, target);
                bool costOk = capLess >= 0 ? effCost <= capLess : (capExact < 0 || effCost == capExact);
                if (targetSeat != OtherSeat(effect.Seat) || GetCard(target).Type != "character" || !costOk)
                {
                    Log(state, effect.Seat, "That is not a valid target for this K.O. effect.");
                    return EffectResolution.WaitingForTarget;
                }
                if (CannotBeKoedByEffect(state, target))
                {
                    Log(state, effect.Seat, $"{NameId(GetCard(target))} cannot be K.O.'d.");
                    return EffectResolution.Resolved;
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
                    if (costCap >= 0 && GetCost(state, target) > costCap)
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
            if (ContainsAll(text, "Choose one") || ContainsAll(text, "chooses one"))
            {
                if (TryParseChoiceEffect(state, effect)) return EffectResolution.Resolved;
            }

            // Scry: "Look at N cards from the top of your deck and place them at the top or
            // bottom of the deck in any order." (OP01-073 etc.)
            if (ContainsAll(text, "Look at") && ContainsAll(text, "from the top of your deck")
                && (ContainsAll(text, "top or bottom of the deck") || ContainsAll(text, "top or bottom of your deck")))
            {
                var srcForScry = FindAnyInPlay(state, effect.SourceInstanceId, out _) ?? Player(state, effect.Seat).Leader;
                if (srcForScry != null) StartDeckScry(state, effect.Seat, srcForScry, ParseLookCount(text));
                return EffectResolution.Resolved;
            }

            // Look at top N cards of deck then optionally take one to hand.
            // (ActivateMain handles this for Activate: timing; here we cover On Play / Trigger.)
            if (ContainsAll(text, "Look at") && ContainsAll(text, "from the top of your deck") && ContainsAll(text, "to your hand"))
            {
                int lookN = ParseLookCount(text);
                if (lookN < 1) lookN = 1;
                var (namedFilter, typeFilter) = ParseNamedOrTypeFilter(text);
                string featureFilter = namedFilter == null ? ParseCurlyBraceTag(text) : null;
                var srcForLook = FindAnyInPlay(state, effect.SourceInstanceId, out _) ?? Player(state, effect.Seat).Leader;
                if (srcForLook != null)
                {
                    StartDeckLook(state, effect.Seat, srcForLook, featureFilter, lookN, namedFilter, typeFilter,
                        ContainsAll(text, "trash the rest"));
                    if (ContainsAll(text, "with a [Trigger]")) state.DeckLook.RequireTrigger = true;
                    var selN = System.Text.RegularExpressions.Regex.Match(text, @"reveal up to (\d+)");
                    if (selN.Success) state.DeckLook.SelectCount = int.Parse(selN.Groups[1].Value);
                }
                return EffectResolution.Resolved;
            }

            // "Add the top/bottom card of your Life cards to your hand." (life-cost payment)
            {
                var lifeTake = System.Text.RegularExpressions.Regex.Match(text,
                    @"Add the (top|bottom) card of your Life cards? to your hand",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (lifeTake.Success)
                {
                    if (owner.Life.Count == 0)
                    {
                        Log(state, effect.Seat, $"{sourceName}: no Life cards to take.");
                        return EffectResolution.Resolved;
                    }
                    bool fromTop = lifeTake.Groups[1].Value.Equals("top", StringComparison.OrdinalIgnoreCase);
                    // Life list convention: TakeLife pops the END, so end = top of the pile.
                    int li = fromTop ? owner.Life.Count - 1 : 0;
                    var lifeCard = owner.Life[li];
                    owner.Life.RemoveAt(li);
                    lifeCard.Zone = "hand";
                    owner.Hand.Add(lifeCard);
                    Log(state, effect.Seat, $"{sourceName}: {Player(state, effect.Seat).Name} adds the {(fromTop ? "top" : "bottom")} Life card to hand ({owner.Life.Count} Life left).");
                    return EffectResolution.Resolved;
                }
            }

            // "Add up to N card(s) from the top of your deck to the top of your Life cards."
            if ((ContainsAll(text, "Add") || ContainsAll(text, "add"))
                && ContainsAll(text, "from the top of your deck") && ContainsAll(text, "top of your Life"))
            {
                var lifeAddM = System.Text.RegularExpressions.Regex.Match(text, @"[Uu]p to (\d+)");
                int lifeAddN = lifeAddM.Success ? int.Parse(lifeAddM.Groups[1].Value) : 1;
                int lifeAdded = 0;
                for (int i = 0; i < lifeAddN && owner.Deck.Count > 0; i++)
                {
                    var lc = Shift(owner.Deck);
                    lc.Zone = "life";
                    owner.Life.Add(lc);   // end of the list = top of the Life pile (TakeLife pops the end)
                    lifeAdded++;
                }
                Log(state, effect.Seat, $"{sourceName} adds {lifeAdded} card(s) from the deck to the top of Life.");
                return EffectResolution.Resolved;
            }

            // "Trash (up to) N card(s) from the top of your opponent's Life cards."
            if ((ContainsAll(text, "trash up to") || ContainsAll(text, "Trash ")) && ContainsAll(text, "opponent's Life"))
            {
                var oppLifeM = System.Text.RegularExpressions.Regex.Match(text, @"[Tt]rash (?:up to )?(\d+)");
                int oppLifeN = oppLifeM.Success ? int.Parse(oppLifeM.Groups[1].Value) : 1;
                var oppLifeP = Player(state, OtherSeat(effect.Seat));
                int trashedLife = 0;
                for (int i = 0; i < oppLifeN && oppLifeP.Life.Count > 0; i++)
                {
                    var tl = Pop(oppLifeP.Life);
                    tl.Zone = "trash";
                    oppLifeP.Trash.Add(tl);
                    trashedLife++;
                }
                Log(state, effect.Seat, $"{sourceName} trashes {trashedLife} card(s) from the top of the opponent's Life.");
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
                    p2.Life.Add(lifeCard);   // end of the list = TOP of Life (TakeLife pops the end)
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
            if (System.Text.RegularExpressions.Regex.IsMatch(text, @"Play (?:up to )?\d+ ", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                && ContainsAll(text, "from your trash") && effect.TargetZone == EffectTargetZone.Trash)
            {
                if (effect.SelectionsRemaining <= 0)
                {
                    var trN = System.Text.RegularExpressions.Regex.Match(text, @"Play (?:up to )?(\d+) ");
                    effect.SelectionsRemaining = trN.Success ? int.Parse(trN.Groups[1].Value) : 1;
                }
                var trNameF = System.Text.RegularExpressions.Regex.Match(text, @"Play (?:up to )?\d+ \[([^\]]+)\]");
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
                if (trNameF.Success && !NameMatches(state, trashCard, trNameF.Groups[1].Value.Trim()))
                {
                    Log(state, effect.Seat, $"{NameId(trashDef)} is not [{trNameF.Groups[1].Value.Trim()}].");
                    return EffectResolution.WaitingForTarget;
                }
                int costCap4 = ParseCostFilter(text);
                if (costCap4 >= 0 && trashDef.Cost > costCap4)
                {
                    Log(state, effect.Seat, $"{NameId(trashDef)} costs too much for {sourceName}.");
                    return EffectResolution.WaitingForTarget;
                }
                int pwrCap4 = ParseLimit(text, @"(\d{3,5}) power or less");
                if (pwrCap4 >= 0 && trashDef.Power > pwrCap4)
                {
                    Log(state, effect.Seat, $"{NameId(trashDef)} has too much power for {sourceName}.");
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
                        QueueAndAutoResolve(state, effect.Seat, trashCard, "onPlay", trashDef.Effect, true, EffectScope.Instant, InferTargetZone(trashDef.Effect));
                }
                else
                {
                    if (p4.Stage != null) MoveToTrash(state, effect.Seat, p4.Stage.InstanceId, true);
                    trashCard.Zone = "stage";
                    p4.Stage = trashCard;
                    if (HasTiming(trashDef.Effect, "On Play"))
                        QueueAndAutoResolve(state, effect.Seat, trashCard, "onPlay", trashDef.Effect, true, EffectScope.Instant, InferTargetZone(trashDef.Effect));
                }
                Log(state, effect.Seat, $"{sourceName} plays {NameId(trashDef)} from trash.");
                effect.SelectionsRemaining--;
                if (effect.SelectionsRemaining > 0)
                {
                    Log(state, effect.Seat, $"You may play {effect.SelectionsRemaining} more from the trash, or skip.");
                    return EffectResolution.WaitingForTarget;
                }
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
                NotifyHandTrashedByEffect(state, effect.Seat);
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
                if (!CannotBeKoedByEffect(state, target))
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
                if (HasModifier(state, target, "cannotBeRested"))
                {
                    Log(state, effect.Seat, $"{NameId(targetDef)} cannot be rested.");
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
                // Enforce the card-TYPE and COLOR the text specifies ("black Character card…").
                // Previously only feature+cost were checked, so ST08-014 Gum-Gum Bell's trigger
                // ("add 1 black Character…") wrongly let an Event be picked from the trash.
                if (!AddClauseTypeMatches(text, addDef))
                {
                    Log(state, effect.Seat, $"{NameId(addDef)} is not the required card type for {sourceName}.");
                    return EffectResolution.WaitingForTarget;
                }
                if (!AddClauseColorMatches(text, addDef))
                {
                    Log(state, effect.Seat, $"{NameId(addDef)} is not the required color for {sourceName}.");
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
                    bool condMet = EvaluateCondition(state, effect.Seat, condPart, effect.SourceInstanceId);
                    if (condMet && !string.IsNullOrEmpty(bodyPart))
                    {
                        var condSrc = FindCardInstance(state, effect.SourceInstanceId);
                        if (condSrc != null)
                            QueueAndAutoResolve(state, effect.Seat, condSrc, effect.Timing, bodyPart, IsOptionalEffectText(bodyPart),
                                effect.Scope, InferTargetZone(bodyPart));
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
                    // Strip "Then," and re-capitalize a following "if" so clause handlers that
                    // anchor on "If " (conditionals) recognize the queued second clause.
                    string partB = NormalizeClause(text.Substring(thenAt));
                    var tempEffect = ShallowCloneEffect(effect, partA);
                    var partARes = TryResolveKnownEffect(state, tempEffect, targetId);
                    // Multi-pick clause-A progress lives on the clone — mirror it back so the
                    // next click continues where this one left off.
                    effect.SelectionsRemaining = tempEffect.SelectionsRemaining;
                    if (partARes == EffectResolution.WaitingForTarget) return EffectResolution.WaitingForTarget;
                    if (partARes == EffectResolution.Resolved)
                    {
                        var chainSrc = FindCardInstance(state, effect.SourceInstanceId);
                        if (chainSrc != null)
                            QueueAndAutoResolve(state, effect.Seat, chainSrc, effect.Timing, partB, IsOptionalEffectText(partB),
                                effect.Scope, InferTargetZone(partB));
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
                // Conditional variants: "If your Leader is [X] / has the {T} type / … , play this
                // card." — evaluate the condition first; unmet → fall through (card goes to hand).
                var condPlay = System.Text.RegularExpressions.Regex.Match(trigger,
                    @"If (.+?), play this card", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                bool condOk = !condPlay.Success || EvaluateCondition(state, defenderSeat, condPlay.Groups[1].Value.Trim(), cardFromLife.InstanceId);
                // "DON!! −N (…): Play this card." — pay the return-DON cost first.
                int trigDonMinus = ParseDonMinusCost(trigger);
                var defender = Player(state, defenderSeat);
                if (condOk && trigDonMinus > 0 && defender.CostArea.Count < trigDonMinus)
                {
                    Log(state, defenderSeat, $"{NameId(def)} Trigger needs {trigDonMinus} DON!! on the field to play this card.");
                    condOk = false;
                }
                if (condOk)
                {
                    int openSlot = defender.CharacterArea.FindIndex(c => c == null);
                    if (openSlot >= 0)
                    {
                        if (trigDonMinus > 0) PayDonMinus(state, defenderSeat, trigDonMinus);
                        cardFromLife.Zone = "character";
                        cardFromLife.PlayedOnTurn = state.TurnNumber;
                        defender.CharacterArea[openSlot] = cardFromLife;
                        Log(state, defenderSeat, $"{NameId(def)} Trigger plays this card to the field.");
                        state.Battle = null;
                        state.Phase = "main";
                        if (HasTiming(def.Effect, "On Play"))
                            QueueAndAutoResolve(state, defenderSeat, cardFromLife, "onPlay", def.Effect, true,
                                EffectScope.Instant, InferTargetZone(def.Effect));
                        return true;
                    }
                    Log(state, defenderSeat, $"{NameId(def)} Trigger could not play because the character area is full.");
                }
                else if (condPlay.Success)
                {
                    Log(state, defenderSeat, $"{NameId(def)} Trigger condition not met — the card goes to your hand.");
                }
            }

            // "Activate this card's [Main] effect." / "Activate this card's effect."
            if (trigger.IndexOf("Activate this card's [Main] effect", StringComparison.OrdinalIgnoreCase) >= 0
                || trigger.IndexOf("Activate this card's effect", StringComparison.OrdinalIgnoreCase) >= 0)
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
            if (IsAutomatedEffectPattern(trigger))
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
                    TargetZone = InferTargetZone(trigger),
                };
                state.PendingEffects.Add(pending);
                if (def.Type == "event")
                {
                    // Using an event's trigger uses the event — it goes to the trash.
                    cardFromLife.Zone = "trash";
                    Player(state, defenderSeat).Trash.Add(cardFromLife);
                }
                else
                {
                    // Characters/stages: the life card goes to hand after its trigger
                    // resolves (unless the trigger itself plays the card — handled above).
                    cardFromLife.Zone = "hand";
                    Player(state, defenderSeat).Hand.Add(cardFromLife);
                }
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
            if (string.IsNullOrWhiteSpace(text)) return false;
            // Normalize "Then, if …" second clauses so anchored checks below see "If …",
            // and drop leading [Timing] tags (they carry no matching information).
            text = NormalizeClause(text);
            text = System.Text.RegularExpressions.Regex.Replace(text, @"^\s*(\[[^\]]+\]\s*/?\s*)+", "");
            // "DON!! −N (…): body" — resolvable when the body is (cost paid in ResolveEffect).
            if (ParseDonMinusCost(text) > 0)
            {
                string donBody = DonMinusBody(text);
                if (!string.Equals(donBody, text, StringComparison.Ordinal) && IsAutomatedEffectPattern(donBody)) return true;
            }
            // "You may <cost>: body" optional-cost effects handled by the cost-prefix resolver.
            {
                string bare = System.Text.RegularExpressions.Regex.Replace(text, @"^\s*(\[[^\]]+\]\s*/?\s*)+", "");
                var costM = System.Text.RegularExpressions.Regex.Match(bare,
                    @"^You may (?<cost>[^:]+):\s*(?<body>.+)$",
                    System.Text.RegularExpressions.RegexOptions.Singleline);
                if (costM.Success)
                {
                    string c = costM.Groups["cost"].Value;
                    if ((ContainsAll(c, "trash") && ContainsAll(c, "from your hand"))
                        || (ContainsAll(c, "place") && ContainsAll(c, "from your trash") && ContainsAll(c, "bottom of your deck"))
                        || ContainsAll(c, "from the top or bottom of your Life"))
                        return true;
                }
            }
            return ContainsAll(text, "K.O. up to 1", "opponent's rested Characters", "cost of 3 or less")
                || ContainsAll(text, "Set up to 1", "rested Characters with a cost of 5 or less as active")
                || ContainsAll(text, "Give", "rested DON!!", "Leader")
                || ContainsAll(text, "K.O. up to 1", "opponent's Characters", "6000 power or less")
                || ContainsAll(text, "Rest up to 1", "opponent's Characters")
                || (ContainsAll(text, "gains +") && ContainsAll(text, " power") && !ContainsAll(text, "Choose one")
                    && (ContainsAll(text, "during this turn") || ContainsAll(text, "during this battle"))
                    && (ContainsAll(text, "Leader") || ContainsAll(text, "Character")))
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
                // Session 7 additions (playtest fixes)
                || ContainsAll(text, "Play up to 1", "from your hand")
                || (System.Text.RegularExpressions.Regex.IsMatch(text ?? "", @"[-\u2212\u2013\u2011\u2012\u2014]\d+\s+cost", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                    && ContainsAll(text, "opponent", "Character"))
                || ContainsAll(text, "cannot activate", "Blocker", "during this battle")
                || (ContainsAll(text, "K.O. up to 1", "opponent") && ContainsAll(text, "cost of"))
                // Session 8 additions (ST05/ST19 playtest sweep)
                || (ContainsAll(text, "cannot attack") && ContainsAll(text, "opponent"))
                || ContainsAll(text, "trash up to 1", "opponent's Characters")
                || (ContainsAll(text, "All of your") && ContainsAll(text, "gain") && ContainsAll(text, "power")
                    && (ContainsAll(text, "during this turn") || ContainsAll(text, "during this battle")))
                || (ContainsAll(text, "DON!! card") && ContainsAll(text, "from your DON!! deck"))
                || (System.Text.RegularExpressions.Regex.IsMatch(text, @"Rest up to \d+",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                    && ContainsAll(text, "opponent's Characters"))
                || (System.Text.RegularExpressions.Regex.IsMatch(text, @"[-\u2212\u2013\u2011\u2012\u2014]\d{3,5}\s+power\s+during this (turn|battle)",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                    && ContainsAll(text, "opponent", "Give"))
                || (ContainsAll(text, "Return") && ContainsAll(text, "to the owner's hand"))
                || (ContainsAll(text, "Place up to") && ContainsAll(text, "bottom of the owner's deck"))
                || ContainsAll(text, "Set this Character as active")
                || System.Text.RegularExpressions.Regex.IsMatch(text, @"[Tt]rash \d+ cards? from the top of your deck")
                // Sessions 11-12 additions
                || (ContainsAll(text, "Look at") && ContainsAll(text, "Life cards"))
                || System.Text.RegularExpressions.Regex.IsMatch(text, @"Add (?:up to )?\d+ cards? from the top of your Life cards? to your hand",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                || ContainsAll(text, "Turn all of your Life cards face")
                || ContainsAll(text, "Trash all your face-up Life cards")
                || (ContainsAll(text, "trash up to") && ContainsAll(text, "opponent's Life"))
                || System.Text.RegularExpressions.Regex.IsMatch(text, @"gains? \+\d+ cost until the end of your opponent's next",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                || (System.Text.RegularExpressions.Regex.IsMatch(text, @"gains? \[(Rush|Blocker|Double Attack|Banish|Unblockable)\]")
                    && (ContainsAll(text, "during this") || ContainsAll(text, "until the")
                        || ContainsAll(text, "Up to ") || ContainsAll(text, "All of your")
                        || System.Text.RegularExpressions.Regex.IsMatch(text, @"(this|Your[^.]*?) (Character|Leader) gains \[",
                               System.Text.RegularExpressions.RegexOptions.IgnoreCase)))
                || System.Text.RegularExpressions.Regex.IsMatch(text, @"gains? \+\d{3,5} power until the (?:start of your next turn|end of your (?:opponent's )?next (?:End Phase|turn))",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                || (ContainsAll(text, "cannot activate") && ContainsAll(text, "Blocker") && ContainsAll(text, "during this turn"))
                || System.Text.RegularExpressions.Regex.IsMatch(text, @"Give up to \d+ of your [^.]*?Characters[^.]*? up to \d+ rested DON!! cards? each",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                || ContainsAll(text, "place the rest at the bottom")
                || ContainsAll(text, "cannot add Life cards to your hand using your own effects")
                || System.Text.RegularExpressions.Regex.IsMatch(text, @"^this Character gains \[[A-Za-z !]+\]\.?$",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                || (ContainsAll(text, "Reveal up to") && ContainsAll(text, "from your hand") && ContainsAll(text, "Life cards"))
                || (ContainsAll(text, "Reveal") && (ContainsAll(text, "from the top of your deck") || ContainsAll(text, "from your deck and add it to your hand")))
                // Session 9 additions (next-wave sweep)
                || (ContainsAll(text, "will not become active") && ContainsAll(text, "Refresh Phase"))
                || ContainsAll(text, "cannot be rested until")
                || System.Text.RegularExpressions.Regex.IsMatch(text, @"opponent returns \d+ DON!! cards? to their DON!! deck",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                || (System.Text.RegularExpressions.Regex.IsMatch(text, @"Rest up to \d+",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                    && (ContainsAll(text, "opponent's Leader or Character") || ContainsAll(text, "opponent's cards")))
                || System.Text.RegularExpressions.Regex.IsMatch(text, @"Add the (top|bottom) card of your Life cards? to your hand",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                || ((ContainsAll(text, "Add") || ContainsAll(text, "add")) && ContainsAll(text, "from the top of your deck") && ContainsAll(text, "top of your Life"))
                || (ContainsAll(text, "trash up to") && ContainsAll(text, "opponent's Life"))
                || (ContainsAll(text, "Look at") && ContainsAll(text, "from the top of your deck")
                    && (ContainsAll(text, "top or bottom of the deck") || ContainsAll(text, "top or bottom of your deck")))
                || FindThenClause(text) > 0;
        }

        // True when any of `seat`'s in-play cards (leader/characters/stage) prints `phrase`.
        private static bool BoardHasText(GameState state, string seat, string phrase)
        {
            var p = Player(state, seat);
            var cards = new List<CardInstance>();
            if (p.Leader != null) cards.Add(p.Leader);
            foreach (var c in p.CharacterArea) if (c != null) cards.Add(c);
            if (p.Stage != null) cards.Add(p.Stage);
            return cards.Any(c => !IsEffectNegated(state, c) && ContainsAll(GetCard(c)?.Effect ?? "", phrase));
        }

        private static string Truncate(string s, int n) =>
            string.IsNullOrEmpty(s) || s.Length <= n ? (s ?? "") : s.Substring(0, n);

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
                    card.Rested = false;   // trash cards render upright (viewer popup / pile)
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
                card.Rested = false;   // trash cards render upright
                ReturnAttachedDon(p, card);
                card.Modifiers.Clear();
                p.Trash.Add(card);
                state.NameOverrides.Remove(card.InstanceId);
                state.BasePowerOverrides.RemoveAll(bp => bp.TargetInstanceId == card.InstanceId);
                state.TimedPowerBonuses.RemoveAll(tb => tb.TargetInstanceId == card.InstanceId);
                if (!silent) Log(state, seat, $"{NameId(GetCard(card))} goes to trash.");
                FireOnKoEffects(state, seat, card);
            }
            if (p.Stage != null && p.Stage.InstanceId == instanceId)
            {
                var card = p.Stage;
                p.Stage = null;
                card.Zone = "trash";
                ReturnAttachedDon(p, card);
                card.Modifiers.Clear();
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
            card.Modifiers.Clear();
            p.Hand.Add(card);
            state.NameOverrides.Remove(card.InstanceId); // name overrides only apply while on field
            state.BasePowerOverrides.RemoveAll(bp => bp.TargetInstanceId == card.InstanceId);
            state.TimedPowerBonuses.RemoveAll(tb => tb.TargetInstanceId == card.InstanceId);
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
            if (IsEffectNegated(state, card)) return;
            if (HasTiming(def.Effect, "On KO") || HasTiming(def.Effect, "On K.O."))
            {
                string koClause = HasTiming(def.Effect, "On K.O.")
                    ? ExtractTimedClause(def.Effect, "On K.O.")
                    : ExtractTimedClause(def.Effect, "On KO");
                bool ytKo = HasTiming(koClause, "Your Turn") && !HasTiming(koClause, "Opponent's Turn");
                bool otKo = HasTiming(koClause, "Opponent's Turn");
                bool ownTurnKo = state.ActiveSeat == seat;
                if ((ytKo && !ownTurnKo) || (otKo && ownTurnKo)) return;
                QueueAndAutoResolve(state, seat, card, "onKo", koClause, IsOptionalEffectText(koClause), EffectScope.Instant, InferTargetZone(koClause));
                Log(state, seat, $"{NameId(def)} [On K.O.] effect triggers.");
            }
            foreach (var line in (def.Effect ?? "").Split('\n'))
            {
                var whenKo = System.Text.RegularExpressions.Regex.Match(line,
                    @"When this (?:Character|Leader) is K\.O\.'d[^,]*, (.+)$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (!whenKo.Success) continue;
                string wkBody = NormalizeClause(whenKo.Groups[1].Value.Trim());
                if (IsAutomatedEffectPattern(wkBody))
                    QueueAndAutoResolve(state, seat, card, "onKo", wkBody, true, EffectScope.Instant, InferTargetZone(wkBody));
                else
                    Log(state, seat, $"{NameId(def)}: K.O. reaction needs manual resolution: {wkBody}");
            }
            // A Character was K.O.'d — fire any board watchers that react to that (distinct from
            // the K.O.'d card's OWN On-K.O. text handled above).
            FireCharacterKoWatchers(state);
        }

        // Board watchers that react to ANY Character being K.O.'d, on EITHER player's field —
        // e.g. ST08-001 "Black" Luffy leader: "[Your Turn] When a Character is K.O.'d, give up to
        // 1 rested DON!! card to this Leader." Previously unimplemented (FireOnKoEffects only fired
        // the K.O.'d card's own effects), so the leader effect never triggered.
        private static void FireCharacterKoWatchers(GameState state)
        {
            foreach (var watchSeat in Seats())
            {
                var wp = Player(state, watchSeat);
                var sources = new List<CardInstance>();
                if (wp.Leader != null) sources.Add(wp.Leader);
                foreach (var c in wp.CharacterArea) if (c != null) sources.Add(c);
                foreach (var src in sources)
                {
                    if (IsEffectNegated(state, src)) continue;
                    var sdef = GetCard(src);
                    foreach (var line in (sdef?.Effect ?? "").Split('\n'))
                    {
                        var m = System.Text.RegularExpressions.Regex.Match(line,
                            @"When a Character is K\.O\.'d[^,]*, (.+)$",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (!m.Success) continue;
                        // "[Your Turn]" / "[Opponent's Turn]" gate on the watcher's owner.
                        bool yourTurn = HasTiming(line, "Your Turn") && !HasTiming(line, "Opponent's Turn");
                        bool oppTurn = HasTiming(line, "Opponent's Turn");
                        if (yourTurn && state.ActiveSeat != watchSeat) continue;
                        if (oppTurn && state.ActiveSeat == watchSeat) continue;
                        // "[Once Per Turn]" gate (e.g. EB01-047 Laboon: "…draw 1 card…") — without
                        // this it would fire on every Character K.O. in the turn.
                        bool once = line.IndexOf("[Once Per Turn]", StringComparison.OrdinalIgnoreCase) >= 0;
                        string onceKey = src.InstanceId + ":koWatch";
                        if (once && wp.AbilityUsedThisTurn.Contains(onceKey)) continue;
                        if (once) wp.AbilityUsedThisTurn.Add(onceKey);
                        string body = NormalizeClause(m.Groups[1].Value.Trim());
                        QueueAndAutoResolve(state, watchSeat, src, "koWatch", body,
                            IsOptionalEffectText(body), EffectScope.Instant, InferTargetZone(body));
                        Log(state, watchSeat, $"{NameId(sdef)}: a Character was K.O.'d.");
                    }
                }
            }
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
            // "Trash this Character at the end of this turn." riders.
            foreach (var mEnd in state.ActiveModifiers.Where(m => m.ModifierType == "trashAtEndOfTurn").ToList())
            {
                var victim = FindAnyInPlay(state, mEnd.TargetInstanceId, out var vSeat);
                if (victim != null) MoveToTrash(state, vSeat, victim.InstanceId);
            }
            state.ActiveModifiers.RemoveAll(m => m.ModifierType == "trashAtEndOfTurn");
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
                string eotClause = ExtractTimedClause(def.Effect, "End of Opponent's Turn");
                QueueAndAutoResolve(state, seat, c, "endOfOpponentsTurn", eotClause, IsOptionalEffectText(eotClause),
                    EffectScope.Instant, InferTargetZone(eotClause));
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
                string eoyClause = ExtractTimedClause(def.Effect, "End of Your Turn");
                QueueAndAutoResolve(state, seat, c, "endOfYourTurn", eoyClause, IsOptionalEffectText(eoyClause),
                    EffectScope.Instant, InferTargetZone(eoyClause));
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

