using System.Collections.Generic;
using System.Linq;
using OnePieceTcg.Engine;
using OnePieceTcg.Engine.Bot;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// Drives the ACTUAL shipped Advanced bot (<see cref="OnePieceTcg.Engine.Bot.Search.AdvancedContractBot"/>,
    /// living in Assets/) inside the Sim self-play harness, so heuristic changes can be A/B-measured against the
    /// exact code that ships — not the divergent research fork in Tools/Sim/Planning. Mirrors the shipped
    /// GameManager.AdvancedAiTick adapter: it owns the per-turn "attempted activations" set (cleared when the
    /// turn number changes) and classifies the deck archetype once from the reconstructed decklist. The Advanced
    /// bot decides on its own fair-information view internally (BotDeterminizer), so nothing here needs to hide
    /// state — the seat gets the full referee state exactly as the shipped GameManager passes it.
    /// </summary>
    public sealed class ShippedAdvancedAgent : IAgent
    {
        public string Name { get; }
        private readonly HashSet<string> _attempted = new HashSet<string>();
        private int _lastTurn = -1;
        private string _archetype;

        public ShippedAdvancedAgent(string name = "shipped-advanced") { Name = name; }

        public GameCommand Decide(GameState state, string seat, HashSet<string> blacklist)
        {
            if (state.TurnNumber != _lastTurn) { _lastTurn = state.TurnNumber; _attempted.Clear(); }
            _archetype ??= OnePieceTcg.Engine.Bot.Search.AdvancedContractBot.ClassifyArchetype(ReconstructDeck(state, seat));
            return OnePieceTcg.Engine.Bot.Search.AdvancedContractBot.Decide(state, seat, blacklist, _attempted, _archetype);
        }

        /// <summary>Rebuild the seat's 50-card decklist from the live state (card conservation ⇒ the union of a
        /// player's zones is exactly their list), so the archetype router has the same input the shipped game
        /// gets from the deck definition. Computed once and cached; deck identity does not change mid-game.</summary>
        private static DeckDef ReconstructDeck(GameState state, string seat)
        {
            var p = state.Players[seat];
            var all = new List<CardInstance>();
            all.AddRange(p.Deck);
            all.AddRange(p.Hand);
            all.AddRange(p.Life);
            all.AddRange(p.Trash);
            all.AddRange(p.CharacterArea.Where(c => c != null));
            if (p.Stage != null) all.Add(p.Stage);
            if (state.DeckLook != null && state.DeckLook.Seat == seat)
            {
                if (state.DeckLook.Cards != null) all.AddRange(state.DeckLook.Cards);
                if (state.DeckLook.Ordered != null) all.AddRange(state.DeckLook.Ordered);
            }
            if (state.Battle?.RevealedLife != null && state.Battle.TargetSeat == seat) all.Add(state.Battle.RevealedLife);

            var list = all.Where(c => c != null)
                .GroupBy(c => c.CardId)
                .Select(g => (cardId: g.Key, qty: g.Count()))
                .ToList();
            return new DeckDef { Id = "reconstructed", Leader = p.Leader?.CardId, List = list };
        }
    }
}
