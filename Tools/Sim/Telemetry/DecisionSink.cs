using System;
using System.Linq;
using System.Text.Json;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>Receives one callback per applied decision, BEFORE the command mutates state, so it
    /// can record the state the choice was made in. Implementations must be cheap and allocation-light
    /// — this runs on the hot path of every simulated game.</summary>
    public interface IDecisionSink
    {
        void OnDecision(GameState state, string seat, string agentName, GameCommand cmd);
    }

    /// <summary>No-op sink — the default for raw self-play generation where per-game outcome rows
    /// are all we keep. Millions of games at full decision fidelity is intentionally opt-in.</summary>
    public sealed class NullDecisionSink : IDecisionSink
    {
        public static readonly NullDecisionSink Instance = new NullDecisionSink();
        public void OnDecision(GameState state, string seat, string agentName, GameCommand cmd) { }
    }

    /// <summary>Extracts the cheap, PUBLIC state features into a DecisionRecord and hands it to a
    /// writer. Deliberately reads only legally observable quantities — sizes and counts, never the
    /// contents of the opponent's hidden hand/deck (§13 integrity boundary).</summary>
    public sealed class RecordingDecisionSink : IDecisionSink
    {
        private readonly long _gameIndex;
        private readonly Action<DecisionRecord> _emit;

        public RecordingDecisionSink(long gameIndex, Action<DecisionRecord> emit)
        {
            _gameIndex = gameIndex;
            _emit = emit;
        }

        public void OnDecision(GameState state, string seat, string agentName, GameCommand cmd)
        {
            var s = state.Players.TryGetValue("south", out var sp) ? sp : null;
            var n = state.Players.TryGetValue("north", out var np) ? np : null;
            _emit(new DecisionRecord
            {
                g = _gameIndex,
                turn = state.TurnNumber,
                phase = state.Phase,
                seat = seat,
                agent = agentName,
                cmdType = cmd.Type,
                cmdInstance = cmd.InstanceId,
                cmdTarget = cmd.Target,
                sLife = s?.Life.Count ?? 0,
                nLife = n?.Life.Count ?? 0,
                sHand = s?.Hand.Count ?? 0,
                nHand = n?.Hand.Count ?? 0,
                sBoard = s?.CharacterArea.Count(c => c != null) ?? 0,
                nBoard = n?.CharacterArea.Count(c => c != null) ?? 0,
                sDon = s != null ? GameEngine.ActiveDonCount(s) : 0,
                nDon = n != null ? GameEngine.ActiveDonCount(n) : 0,
                sDeck = s?.Deck.Count ?? 0,
                nDeck = n?.Deck.Count ?? 0,
            });
        }
    }

    /// <summary>Compact single-line JSON used for all JSONL output.</summary>
    public static class Json
    {
        // IncludeFields is essential: GameRecord/DecisionRecord expose public FIELDS (compact), and
        // System.Text.Json serializes only properties unless told otherwise.
        private static readonly JsonSerializerOptions Opts = new JsonSerializerOptions
        {
            WriteIndented = false,
            IncludeFields = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };
        private static readonly JsonSerializerOptions PrettyOpts = new JsonSerializerOptions
        {
            WriteIndented = true,
            IncludeFields = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };
        public static string Line<T>(T value) => JsonSerializer.Serialize(value, Opts);
        public static string Pretty<T>(T value) => JsonSerializer.Serialize(value, PrettyOpts);
    }
}
