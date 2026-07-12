// One Piece TCG - Replay viewer: per-command enrichment.
//
// ReplayStore already gives us everything needed to deterministically reproduce a match
// (Seed + deck ids + CommandHistory — see ReplayStore.cs's own header comment), but nothing
// about the replay's *content* — which turn/seat/phase a command belongs to, what actually
// happened as readable text, or how to categorize it for a timeline marker or action-log
// icon. This file computes all of that in ONE pass: a full step-by-step resimulation from
// GameEngine.CreateMatch, applying CommandHistory one command at a time (never bulk-jumping),
// capturing state right after each ApplyCommand call.
//
// This is cheap to do once per replay-viewing session: GameEngine is pure, I/O-free data
// mutation (confirmed by audit — no Resources.Load/File/Instantiate/UnityEngine anywhere in
// GameEngine.cs), so resimulating a few hundred commands is on the order of milliseconds.
// The real cost in this codebase is Unity UI reconstruction (GameManager.Render()'s
// destroy-and-rebuild), which this file never touches — it only builds plain C# data.

using System.Collections.Generic;
using OnePieceTcg.Engine;

/// <summary>Everything about ONE command's place in the match: whose turn, what phase, a
/// human-readable category for markers/icons, the narrative EventLog lines it produced, and
/// (if known) when it was really dispatched live.</summary>
public sealed class ReplayAction
{
    /// Matches GameManager.ResimulateReplayTo's cursor convention: the number of commands
    /// applied so far (1-based) to reach the state this action describes — i.e. cursor N
    /// means "the state right after CommandHistory[N-1] was applied."
    public int Cursor;
    public GameCommand Command;
    public int Turn;
    public string ActiveSeat;
    public string Phase;
    /// One of: draw, play, attack, block, counter, don, rest, trigger, effect, search,
    /// choice, zoneMove, life, endTurn, setup, cosmetic, event (fallback).
    public string Category;
    /// Every EventLog message that appeared as a direct result of this command (a single
    /// command — e.g. playCard triggering an On-Play effect chain — can produce several).
    public List<string> LogLines = new List<string>();
    /// Wall-clock seconds since match start when this command was dispatched live, or 0 if
    /// unknown (older replay, or an imported file saved before this field existed).
    public float ElapsedSeconds;

    public string Summary => LogLines.Count > 0 ? string.Join(" ", LogLines) : (Command?.Type ?? "");
}

public static class ReplayIndex
{
    /// <summary>Resimulates the whole match once, one command at a time, building the full
    /// per-command action list. Does not touch any live GameManager state — builds its own
    /// throwaway GameState via GameEngine.CreateMatch.</summary>
    public static List<ReplayAction> Build(ReplayRecord record, MatchConfig config)
    {
        var actions = new List<ReplayAction>();
        if (record?.CommandHistory == null || config == null) return actions;

        var state = GameEngine.CreateMatch(config);
        int prevLogCount = state.EventLog.Count;
        for (int i = 0; i < record.CommandHistory.Count; i++)
        {
            var cmd = record.CommandHistory[i].ToCommand();
            state = GameEngine.ApplyCommand(state, cmd);

            var action = new ReplayAction
            {
                Cursor = i + 1,
                Command = cmd,
                Turn = state.TurnNumber,
                ActiveSeat = state.ActiveSeat,
                Phase = state.Phase,
                Category = CategoryFor(cmd.Type),
                ElapsedSeconds = (record.CommandElapsedSeconds != null && i < record.CommandElapsedSeconds.Count)
                    ? record.CommandElapsedSeconds[i] : 0f,
            };
            for (int j = prevLogCount; j < state.EventLog.Count; j++)
                action.LogLines.Add(state.EventLog[j].Message);
            prevLogCount = state.EventLog.Count;

            actions.Add(action);
        }
        return actions;
    }

    /// <summary>First cursor value belonging to each turn number — the jump target for
    /// "start of turn N" navigation and turn-heading clicks in the side panel.</summary>
    public static Dictionary<int, int> TurnStartCursors(List<ReplayAction> actions)
    {
        var map = new Dictionary<int, int>();
        foreach (var a in actions)
            if (!map.ContainsKey(a.Turn)) map[a.Turn] = a.Cursor;
        return map;
    }

    /// <summary>Cursor values where a new turn begins, in order — same data as
    /// TurnStartCursors but as a sorted list, convenient for prev/next-turn stepping.</summary>
    public static List<int> TurnBoundaryCursors(List<ReplayAction> actions)
    {
        var list = new List<int>();
        int lastTurn = -1;
        foreach (var a in actions)
        {
            if (a.Turn != lastTurn) { list.Add(a.Cursor); lastTurn = a.Turn; }
        }
        return list;
    }

    // Classifies by the actual GameCommand.Type — far more reliable than sniffing log text
    // (see MatchHistoryStore.ClassifyLogMessage, which classifies narrative text for the
    // cloud match-history screen and stays as-is for that use; this is the structured
    // counterpart for the replay viewer, which has the real command available).
    private static string CategoryFor(string commandType)
    {
        switch (commandType)
        {
            case "draw":
            case "drawDon":
                return "draw";
            case "playCard":
                return "play";
            case "declareAttack":
            case "resolveAttack":
            case "clearBattle":
                return "attack";
            case "blockAttack":
            case "passBlock":
                return "block";
            case "counterWithCard":
            case "passCounter":
                return "counter";
            case "attachDon":
                return "don";
            case "rest":
            case "unrest":
                return "rest";
            case "useTrigger":
            case "passTrigger":
                return "trigger";
            case "resolveEffect":
            case "passEffect":
                return "effect";
            case "deckLookSelect":
            case "deckLookConfirmOrder":
            case "deckLookScryConfirm":
                return "search";
            case "resolveChoice":
                return "choice";
            case "trash":
                return "zoneMove";
            case "takeLife":
                return "life";
            case "endTurn":
                return "endTurn";
            case "chooseTurnOrder":
            case "mulliganDecision":
            case "startGame":
                return "setup";
            case "reorderHand":
            case "reorderTrash":
            case "sortTrash":
                return "cosmetic";
            default:
                return "event";
        }
    }
}
