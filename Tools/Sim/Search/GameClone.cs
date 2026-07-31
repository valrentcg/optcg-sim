using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim.Search
{
    /// <summary>
    /// Deep-clones a <see cref="GameState"/> so search can try a candidate action on a copy, evaluate
    /// the result, and discard it (blueprint §2 Clone, §9 solvers, §10.6 shallow search). Out-of-ship.
    ///
    /// <see cref="Clone"/> is a hand-written deep copy — fast enough for per-move search: it deep-copies
    /// only the objects the engine MUTATES (cards, players, battle, pending effects, modifiers) and
    /// shallow-copies the append-only immutable lists (EventLog, CommandHistory — their entries are
    /// never mutated after creation). <see cref="CloneJson"/> is a slow but obviously-correct JSON
    /// round-trip kept as a cross-check (clonetest asserts the two agree).
    /// </summary>
    public static class GameClone
    {
        // ---- fast hand-written deep clone ----
        /// <param name="searchMode">Give the copy EMPTY EventLog/CommandHistory instead of copying them.
        /// Both are append-only OUTPUT: the engine only ever calls .Add on them (3 write sites, zero reads
        /// — it never consults either for game logic), and nothing in search reads them. Copying them costs
        /// O(commands so far) PER CLONE and both grow with every command, so late-game search pays far more
        /// than early-game for identical positions.
        /// Safe for scoring: ValueFunction.Score only reads EventLog when Status=="finished", and a clone
        /// can only reach "finished" via a command applied to the clone itself — which logs the "wins"
        /// message into the clone's own fresh list. The real game state in GameRunner is never cloned, so
        /// Winner/Adjudicate still see the full log.
        /// Off by default: the full copy is what `clonetest` cross-checks against the JSON round-trip.</param>
        public static GameState Clone(GameState s, bool searchMode = false)
        {
            if (s == null) return null;
            var g = new GameState
            {
                Version = s.Version, Seed = s.Seed, FirstPlayer = s.FirstPlayer, CoinFlipWinner = s.CoinFlipWinner,
                Status = s.Status, ActiveSeat = s.ActiveSeat, Phase = s.Phase, TurnNumber = s.TurnNumber,
                EndTurnStage = s.EndTurnStage, EndTurnSeat = s.EndTurnSeat,
                CommandBatch = s.CommandBatch,
                BattleReactionSeat = s.BattleReactionSeat,
                EffectSequence = s.EffectSequence, LogSequence = s.LogSequence, BattleSequence = s.BattleSequence,
                Selected = s.Selected == null ? null : new SelectionRef { InstanceId = s.Selected.InstanceId, Seat = s.Selected.Seat },
                Battle = CloneBattle(s.Battle),
                DeckLook = CloneDeckLook(s.DeckLook),
                ActiveChoice = CloneChoice(s.ActiveChoice),
                DeferredActivatedTriggerSeat = s.DeferredActivatedTriggerSeat,
            };
            g.PendingEffects = s.PendingEffects.Select(ClonePE).ToList();
            // append-only, never mutated in place → shallow list copy is correct (and skippable in search)
            g.CommandHistory = searchMode ? new List<GameCommand>() : new List<GameCommand>(s.CommandHistory);
            g.EventLog = searchMode ? new List<LogEntry>() : new List<LogEntry>(s.EventLog);
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
            // These twelve were silently absent. A hand-written clone falls behind GameState every time
            // a field is added, and the cost is paid by the SEARCH: the bot plans against a board where
            // the restriction never happened. Most of these are "this turn" prohibitions, so the bot
            // would consider plays the engine then refuses — which is a rejected command, a wasted
            // rollout, and a worse decision, all without anything looking broken.
            //
            // DeferredRemovals matters most: it is the record that a Character is only still on the
            // field because a protection question has not been answered yet. Drop it and the search
            // sees the victim alive with nothing owed for it.
            g.DeferredRemovals = s.DeferredRemovals.Select(d => new DeferredRemoval
            {
                EffectId = d.EffectId, VictimSeat = d.VictimSeat, VictimInstanceId = d.VictimInstanceId,
                GuardInstanceId = d.GuardInstanceId, Kind = d.Kind, ByBattleKo = d.ByBattleKo,
            }).ToList();
            g.ActivatedEventIds = new List<string>(s.ActivatedEventIds);
            g.ActivatedTriggerIds = new List<string>(s.ActivatedTriggerIds);
            g.NoPlayCharBaseCostAtLeast = new Dictionary<string, int>(s.NoPlayCharBaseCostAtLeast);
            g.CharRestedByEffectThisTurn = new HashSet<string>(s.CharRestedByEffectThisTurn);
            g.StartStageDoneSeats = new HashSet<string>(s.StartStageDoneSeats);
            g.BattledOppCharThisTurn = new HashSet<string>(s.BattledOppCharThisTurn);
            g.NoPlayFromHandThisTurn = new HashSet<string>(s.NoPlayFromHandThisTurn);
            g.CannotAttackLeaderThisTurn = new HashSet<string>(s.CannotAttackLeaderThisTurn);
            g.NoAddLifeToHandThisTurn = new HashSet<string>(s.NoAddLifeToHandThisTurn);
            g.NoSetDonActiveViaCharThisTurn = new HashSet<string>(s.NoSetDonActiveViaCharThisTurn);
            g.RestedKoProtectionPaid = new HashSet<string>(s.RestedKoProtectionPaid);
            g.BattleKoTrashSaveSeats = new HashSet<string>(s.BattleKoTrashSaveSeats);
            // A 13th, found because the reflective check reports fields it could NOT populate
            // instead of skipping them quietly. "unchecked" is not "passing".
            g.LastPowerBuffTargetId = s.LastPowerBuffTargetId;
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
            // Both ban CEILINGS were dropped, so a searching bot believed it could Blocker
            // with cards the battle had already excluded.
            BlockerPowerBanMax = b.BlockerPowerBanMax, BlockerCostBanMax = b.BlockerCostBanMax,
            BattlePowerBonus = new Dictionary<string, int>(b.BattlePowerBonus),
        };

        private static PendingEffect ClonePE(PendingEffect e) => new PendingEffect
        {
            EffectId = e.EffectId, Seat = e.Seat, QueuedBatch = e.QueuedBatch, SourceInstanceId = e.SourceInstanceId, SourceCardId = e.SourceCardId,
            Timing = e.Timing, Text = e.Text, Optional = e.Optional, Scope = e.Scope, TargetZone = e.TargetZone,
            DonPaymentRemaining = e.DonPaymentRemaining, SelectionsRemaining = e.SelectionsRemaining,
            PlayedPickIds = e.PlayedPickIds == null ? null : new List<string>(e.PlayedPickIds),
            RemainingBudget = e.RemainingBudget, FirstPickId = e.FirstPickId, PendingContinuation = e.PendingContinuation,
            FinalizesActivatedTrigger = e.FinalizesActivatedTrigger,
        
            // Four the shipped cloner already carried and this one did not. OnceKey is the one
            // with teeth: it records that a once-per-turn ability has been spent, so a planner
            // searching on a copy without it can spend the same ability twice. The other three
            // are the progress ledger the pending-effect panel reads.
            OriginalText = e.OriginalText, OnceKey = e.OnceKey,
            DeclineContinuation = e.DeclineContinuation, DeclineSeat = e.DeclineSeat,
            // Mid-pick bookkeeping. A clone that drops these restarts the pick with a clean slate,
            // so a search rollout can re-choose a card the real game already spent or reach one the
            // real game froze out. PickedInstanceIds and CostPaidRefs were ALREADY missing before
            // EligibleInstanceIds existed — same class as the 20 fields dropped earlier here.
            PickedInstanceIds = e.PickedInstanceIds == null ? null : new List<string>(e.PickedInstanceIds),
            CostPaidRefs = e.CostPaidRefs == null ? null : new List<string>(e.CostPaidRefs),
            EligibleInstanceIds = e.EligibleInstanceIds == null ? null : new List<string>(e.EligibleInstanceIds),
            DoneParts = e.DoneParts == null ? null : new List<string>(e.DoneParts),
            SkippedParts = e.SkippedParts == null ? null : new List<string>(e.SkippedParts),
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

        // ---- slow JSON clone (correctness cross-check) ----
        private static readonly JsonSerializerOptions Opts = new JsonSerializerOptions { IncludeFields = true };
        public static GameState CloneJson(GameState state) =>
            JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state, Opts), Opts);
    }
}
