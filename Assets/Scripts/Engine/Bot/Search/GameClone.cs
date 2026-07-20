// Fast hand-written deep clone of GameState, so the Advanced (search) bot can try a candidate move
// on a copy, evaluate it, and discard it. Deep-copies the objects the engine MUTATES (cards, players,
// battle, effects, modifiers) and shallow-copies the append-only immutable lists (EventLog,
// CommandHistory). Pure C#, no UnityEngine / no serializer dependency. Mirror of the out-of-ship
// Tools/Sim clone (verified faithful there).

using System.Collections.Generic;
using System.Linq;

namespace OnePieceTcg.Engine.Bot.Search
{
    public static class GameClone
    {
        public static GameState Clone(GameState s)
        {
            if (s == null) return null;
            var g = new GameState
            {
                Version = s.Version, Seed = s.Seed, FirstPlayer = s.FirstPlayer, CoinFlipWinner = s.CoinFlipWinner,
                Status = s.Status, ActiveSeat = s.ActiveSeat, Phase = s.Phase, TurnNumber = s.TurnNumber,
                EffectSequence = s.EffectSequence, LogSequence = s.LogSequence, BattleSequence = s.BattleSequence,
                Selected = s.Selected == null ? null : new SelectionRef { InstanceId = s.Selected.InstanceId, Seat = s.Selected.Seat },
                Battle = CloneBattle(s.Battle),
                DeckLook = CloneDeckLook(s.DeckLook),
                ActiveChoice = CloneChoice(s.ActiveChoice),
            };
            g.PendingEffects = s.PendingEffects.Select(ClonePE).ToList();
            g.CommandHistory = new List<GameCommand>(s.CommandHistory);
            g.EventLog = new List<LogEntry>(s.EventLog);
            g.Players = s.Players.ToDictionary(kv => kv.Key, kv => ClonePlayer(kv.Value));
            g.TemporaryPowerBonus = new Dictionary<string, int>(s.TemporaryPowerBonus);
            g.NoBlockerGrantedThisTurn = new HashSet<string>(s.NoBlockerGrantedThisTurn);
            g.ActiveModifiers = s.ActiveModifiers.Select(CloneCM).ToList();
            g.AttackCountThisTurn = new Dictionary<string, int>(s.AttackCountThisTurn);
            g.CharKoedThisTurn = new HashSet<string>(s.CharKoedThisTurn);
            g.HighestEventCostThisTurn = new Dictionary<string, int>(s.HighestEventCostThisTurn);
            g.NameOverrides = new Dictionary<string, string>(s.NameOverrides);
            g.BasePowerOverrides = s.BasePowerOverrides.Select(b => new BasePowerOverride { TargetInstanceId = b.TargetInstanceId, Value = b.Value, OwnerSeat = b.OwnerSeat, Duration = b.Duration }).ToList();
            g.TimedPowerBonuses = s.TimedPowerBonuses.Select(b => new TimedPowerBonus { TargetInstanceId = b.TargetInstanceId, Delta = b.Delta, OwnerSeat = b.OwnerSeat, Duration = b.Duration }).ToList();
            return g;
        }

        private static CardInstance CloneCard(CardInstance c) => c == null ? null : new CardInstance
        {
            InstanceId = c.InstanceId, CardId = c.CardId, Owner = c.Owner, Zone = c.Zone,
            Rested = c.Rested, FaceUp = c.FaceUp, PlayedOnTurn = c.PlayedOnTurn,
            AttachedDonIds = new List<string>(c.AttachedDonIds),
            Modifiers = c.Modifiers.Select(m => new ActiveModifier { Source = m.Source, PowerDelta = m.PowerDelta, CostDelta = m.CostDelta, ExpiresAt = m.ExpiresAt }).ToList(),
        };
        private static List<CardInstance> CloneCards(List<CardInstance> l) => l.Select(CloneCard).ToList();

        private static PlayerState ClonePlayer(PlayerState p) => new PlayerState
        {
            Seat = p.Seat, Name = p.Name, DeckName = p.DeckName,
            Leader = CloneCard(p.Leader), Stage = CloneCard(p.Stage),
            Deck = CloneCards(p.Deck), Hand = CloneCards(p.Hand), Life = CloneCards(p.Life),
            CharacterArea = CloneCards(p.CharacterArea), Trash = CloneCards(p.Trash),
            CostArea = p.CostArea.Select(d => new DonInstance { InstanceId = d.InstanceId, Rested = d.Rested }).ToList(),
            DonDeck = p.DonDeck, DonInstanceCounter = p.DonInstanceCounter, TurnsStarted = p.TurnsStarted,
            MulliganUsed = p.MulliganUsed, MulliganDecided = p.MulliganDecided,
            AbilityUsedThisTurn = new HashSet<string>(p.AbilityUsedThisTurn),
        };

        private static BattleState CloneBattle(BattleState b) => b == null ? null : new BattleState
        {
            Id = b.Id, Step = b.Step, PrioritySeat = b.PrioritySeat, AttackerSeat = b.AttackerSeat,
            AttackerId = b.AttackerId, TargetSeat = b.TargetSeat, TargetId = b.TargetId, OriginalTargetId = b.OriginalTargetId,
            Blocked = b.Blocked, CounterPower = b.CounterPower, AttackPower = b.AttackPower, DefensePower = b.DefensePower,
            RevealedLife = CloneCard(b.RevealedLife), PendingLifeDamage = b.PendingLifeDamage,
            NoBlocker = b.NoBlocker, BlockerPowerBan = b.BlockerPowerBan,
            BattlePowerBonus = new Dictionary<string, int>(b.BattlePowerBonus),
        };

        private static PendingEffect ClonePE(PendingEffect e) => new PendingEffect
        {
            EffectId = e.EffectId, Seat = e.Seat, SourceInstanceId = e.SourceInstanceId, SourceCardId = e.SourceCardId,
            Timing = e.Timing, Text = e.Text, Optional = e.Optional, Scope = e.Scope, TargetZone = e.TargetZone,
            DonPaymentRemaining = e.DonPaymentRemaining, SelectionsRemaining = e.SelectionsRemaining,
            RemainingBudget = e.RemainingBudget, FirstPickId = e.FirstPickId, PendingContinuation = e.PendingContinuation,
            OriginalText = e.OriginalText,
            DoneParts = e.DoneParts != null ? new System.Collections.Generic.List<string>(e.DoneParts) : new System.Collections.Generic.List<string>(),
            SkippedParts = e.SkippedParts != null ? new System.Collections.Generic.List<string>(e.SkippedParts) : new System.Collections.Generic.List<string>(),
            OnceKey = e.OnceKey,
        };

        private static CardModifier CloneCM(CardModifier m) => new CardModifier
        {
            SourceInstanceId = m.SourceInstanceId, TargetInstanceId = m.TargetInstanceId, ModifierType = m.ModifierType,
            Keyword = m.Keyword, Duration = m.Duration, BattleId = m.BattleId, OwnerSeat = m.OwnerSeat,
        };

        private static ChoiceState CloneChoice(ChoiceState c) => c == null ? null : new ChoiceState
        {
            Seat = c.Seat, ControllerSeat = c.ControllerSeat, SourceInstanceId = c.SourceInstanceId,
            SourceCardId = c.SourceCardId, Timing = c.Timing, OptionA = c.OptionA, OptionB = c.OptionB,
        };

        private static DeckLookState CloneDeckLook(DeckLookState d) => d == null ? null : new DeckLookState
        {
            Seat = d.Seat, SourceInstanceId = d.SourceInstanceId, SourceName = d.SourceName,
            FeatureFilter = d.FeatureFilter, NamedCardFilter = d.NamedCardFilter, Step = d.Step,
            Cards = CloneCards(d.Cards), Ordered = CloneCards(d.Ordered),
            SearchMode = d.SearchMode, MaxCost = d.MaxCost, CardTypeFilter = d.CardTypeFilter, TrashRest = d.TrashRest,
            PlayMode = d.PlayMode, PlayRested = d.PlayRested, MaxPower = d.MaxPower, TrashSelected = d.TrashSelected,
            RequireTrigger = d.RequireTrigger, SelectCount = d.SelectCount, ToTop = d.ToTop, LifeMode = d.LifeMode,
            PostLookClause = d.PostLookClause,
        };
    }
}
