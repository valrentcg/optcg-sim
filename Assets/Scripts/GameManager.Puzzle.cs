// One Piece TCG - Puzzle / brain-teaser mode (Solo Play option).
//
// Reuses GameManager's board rendering, but instead of the normal live/AI/networked loop it drives the
// position through the shipped-engine PuzzleRuntime: the player controls the ATTACKER, every move is applied
// through the runtime (which auto-plays the defender's strongest surviving defense and re-verifies lethal),
// and the side panel shows status + graduated hints. Winning is defined solely by the engine reaching a
// finished state with the player as the winner - the runtime accepts ANY verified lethal line, not an
// author's sequence. See Assets/Scripts/Engine/Puzzles/ for the solver / hints / runtime.

using System.Collections.Generic;
using UnityEngine;
using OnePieceTcg.Engine;
using OnePieceTcg.Engine.Puzzles;

public partial class GameManager
{
    // ── Entry (mirrors PendingSandbox / LaunchSandbox) ───────────────────────
    public static bool PendingPuzzle;
    public static string PendingPuzzleId;   // optional: jump straight to a specific starter

    public static void LaunchPuzzle(string puzzleId = null)
    {
        PendingPuzzle = true;
        PendingPuzzleId = puzzleId;
        EnsureBoard();
    }

    // ── Runtime state ────────────────────────────────────────────────────────
    private bool isPuzzle;
    private PuzzleRuntime puzzleRuntime;
    private string puzzleAttacker = "south";
    private List<AuthoredPuzzle> puzzleSet;
    private int puzzleIndex;
    private int puzzleHintLevel;                                   // how many hints the player has revealed (0-3)
    private string puzzleHintText;
    private readonly HashSet<string> puzzleHintGlow = new HashSet<string>();

    private void NewPuzzle()
    {
        isPuzzle = true;
        isSandbox = false;
        isReplayMode = false;
        aiSeat = null;
        isNetworked = false;

        puzzleSet = PuzzleLibrary.Starters();
        puzzleIndex = 0;
        if (!string.IsNullOrEmpty(PendingPuzzleId))
        {
            int idx = puzzleSet.FindIndex(p => p.Id == PendingPuzzleId);
            if (idx >= 0) puzzleIndex = idx;
        }
        LoadPuzzle(puzzleIndex);
    }

    private void LoadPuzzle(int idx)
    {
        if (puzzleSet == null || puzzleSet.Count == 0) return;
        puzzleIndex = Mathf.Clamp(idx, 0, puzzleSet.Count - 1);
        var pz = puzzleSet[puzzleIndex];
        puzzleAttacker = pz.Attacker;

        puzzleRuntime = new PuzzleRuntime();
        puzzleRuntime.Start(pz.Build(), pz.Attacker);
        state = puzzleRuntime.State;
        currentMatchConfig = new MatchConfig { Seed = "puzzle-" + pz.Id };

        puzzleHintLevel = 0;
        puzzleHintText = null;
        puzzleHintGlow.Clear();
        southDisplayName = "You";
        northDisplayName = "Opponent";

        // Fresh-board animation bookkeeping (mirror NewSandbox) so the first render doesn't fling ghosts.
        mulliganAnimShownKey = null;
        mulliganRedrawSeat = null;
        handDealAnimating = false;
        mulliganDealAnimating = 0;
        lastCardPoses.Clear();
        suppressMoveAnim.Clear();

        Render();
    }

    // Player moves are funnelled here from Dispatch() when isPuzzle. Only the attacker's own commands are
    // player moves; the defender's responses are auto-played inside PuzzleRuntime.ApplyMove.
    private void DispatchPuzzle(GameCommand command)
    {
        if (puzzleRuntime == null || puzzleRuntime.Status != PuzzleRuntime.PuzzleStatus.InProgress) { Render(); return; }
        if (!string.IsNullOrEmpty(command.Seat) && command.Seat != puzzleAttacker) { Render(); return; }

        bool applied = puzzleRuntime.ApplyMove(command);
        if (applied)
        {
            state = puzzleRuntime.State;
            // A fresh decision invalidates the shown hint (it was for the previous position).
            puzzleHintLevel = 0;
            puzzleHintText = null;
            puzzleHintGlow.Clear();
        }
        NormalizeSelection();
        Render();
    }

    private void RevealHint(int level)
    {
        if (puzzleRuntime == null) return;
        var h = puzzleRuntime.Hint(level);
        if (h == null) return;
        puzzleHintLevel = level;
        puzzleHintText = h.Text;
        puzzleHintGlow.Clear();
        if (level >= 2) foreach (var id in h.HighlightInstanceIds) puzzleHintGlow.Add(id);   // L1 names no card
        Render();
    }

    // True when a card should carry the hint glow (L2/L3 highlight the key card(s)).
    private bool IsPuzzleHintGlow(CardInstance card) =>
        isPuzzle && card != null && puzzleHintGlow.Contains(card.InstanceId);

    // ── Side-panel action content for puzzle mode (replaces DrawContextActions' normal body) ──
    private void DrawPuzzleActions(RectTransform body)
    {
        var pz = (puzzleSet != null && puzzleIndex < puzzleSet.Count) ? puzzleSet[puzzleIndex] : null;
        if (pz != null) AddInfo(body, $"Puzzle {puzzleIndex + 1}/{puzzleSet.Count}: {pz.Title}");

        var status = puzzleRuntime != null ? puzzleRuntime.Status : PuzzleRuntime.PuzzleStatus.NotStarted;
        if (status == PuzzleRuntime.PuzzleStatus.Solved)
        {
            AddInfo(body, "Solved - that's lethal!");
            if (pz != null && !string.IsNullOrEmpty(pz.Teaches)) AddInfo(body, pz.Teaches);
            if (puzzleSet != null && puzzleIndex + 1 < puzzleSet.Count)
                AddButton(body, "Next Puzzle", () => LoadPuzzle(puzzleIndex + 1));
            AddButton(body, "Replay", () => LoadPuzzle(puzzleIndex));
        }
        else if (status == PuzzleRuntime.PuzzleStatus.Failed)
        {
            AddInfo(body, puzzleRuntime != null ? puzzleRuntime.Message : "That line doesn't win.");
            AddButton(body, "Try Again", () => LoadPuzzle(puzzleIndex));
        }
        else
        {
            bool onTrack = puzzleRuntime == null || puzzleRuntime.StillWinning;
            AddInfo(body, onTrack ? "There is lethal here. Find the line." : "Careful - lethal is no longer forced from here.");
            AddInfo(body, "Attack, spread your DON!!, and play cards to close it out.");
            if (!string.IsNullOrEmpty(puzzleHintText)) AddInfo(body, "Hint: " + puzzleHintText);
            if (puzzleHintLevel < 3)
                AddButton(body, puzzleHintLevel == 0 ? "Hint" : "Reveal more", () => RevealHint(puzzleHintLevel + 1), true, false);
            AddButton(body, "Restart", () => LoadPuzzle(puzzleIndex));
        }
        AddButton(body, "Exit to Menu", ReturnToMenu, true, false);
    }
}
