// One Piece TCG - Puzzle / brain-teaser mode (Solo Play option).
//
// Reuses GameManager's board rendering, but instead of the normal live/AI/networked loop it drives the
// position through the shipped-engine PuzzleRuntime: the player controls the ATTACKER, every move is applied
// through the runtime (which auto-plays the defender's strongest surviving defense and re-verifies lethal),
// and the side panel shows status + graduated hints. Winning is defined solely by the engine reaching a
// finished state with the player as the winner - the runtime accepts ANY verified lethal line, not an
// author's sequence. See Assets/Scripts/Engine/Puzzles/ for the solver / hints / runtime.

using System.Collections.Generic;
using System.Linq;
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
    // Three strikes: each failed attempt at the CURRENT puzzle is a strike; on the 3rd, the winning line is
    // revealed. Strikes persist across "Try Again" (same puzzle) and reset when you move to a different puzzle.
    private const int PuzzleMaxStrikes = 3;
    private int puzzleStrikes;
    private bool puzzleFailCounted;      // guards one strike per failed attempt
    private string puzzleSolutionText;   // the revealed winning line (set once you strike out)

    private void NewPuzzle()
    {
        isPuzzle = true;
        isSandbox = false;
        isReplayMode = false;
        aiSeat = null;
        isNetworked = false;

        puzzleSet = BuildShuffledPuzzleSet();
        puzzleIndex = 0;
        if (!string.IsNullOrEmpty(PendingPuzzleId))
        {
            int idx = puzzleSet.FindIndex(p => p.Id == PendingPuzzleId);
            if (idx >= 0) puzzleIndex = idx;
        }
        LoadPuzzle(puzzleIndex);
    }

    // The full verified set (100), fully shuffled across ALL difficulties so "Next" jumps to a random puzzle of
    // any difficulty (not a long run of Easy then Medium ...). Random per session (not deterministic).
    private List<AuthoredPuzzle> BuildShuffledPuzzleSet()
    {
        var rng = new System.Random();
        // Generated puzzles (Easy/Medium after the relabel) + real-game HARVESTED puzzles (Hard/Expert),
        // fully shuffled so "Next" lands on any difficulty.
        var all = new List<AuthoredPuzzle>(PuzzleLibrary.All());
        all.AddRange(HarvestedPuzzleStore.All());
        return all.OrderBy(_ => rng.Next()).ToList();
    }

    private void LoadPuzzle(int idx)
    {
        if (puzzleSet == null || puzzleSet.Count == 0) return;
        int newIndex = Mathf.Clamp(idx, 0, puzzleSet.Count - 1);
        if (newIndex != puzzleIndex) { puzzleStrikes = 0; puzzleSolutionText = null; }   // strikes reset on a NEW puzzle
        puzzleFailCounted = false;                                                        // a fresh attempt can strike again
        puzzleIndex = newIndex;
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
        // Clear any finished-match result overlay left over from the previous (solved) puzzle so Next/Replay
        // return straight to a live board.
        finishedResultText = null;
        matchResultHidden = false;

        // Fresh-board animation bookkeeping (mirror NewSandbox) so the first render doesn't fling ghosts.
        mulliganAnimShownKey = null;
        mulliganRedrawSeat = null;
        handDealAnimating = false;
        mulliganDealAnimating = 0;
        lastCardPoses.Clear();
        suppressMoveAnim.Clear();

        Render();
    }

    // Advance to the next puzzle; after the last one, reshuffle the whole set for a fresh run.
    private void NextPuzzle()
    {
        if (puzzleSet == null || puzzleSet.Count == 0) return;
        if (puzzleIndex + 1 >= puzzleSet.Count) { puzzleSet = BuildShuffledPuzzleSet(); LoadPuzzle(0); }
        else LoadPuzzle(puzzleIndex + 1);
    }

    private static string DifficultyWord(int d) => d switch { 1 => "Easy", 2 => "Medium", 3 => "Hard", _ => "Expert" };

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
        // Strike: the attempt failed (turn ended without lethal). Count it once; on the 3rd, reveal the line.
        if (puzzleRuntime.Status == PuzzleRuntime.PuzzleStatus.Failed && !puzzleFailCounted)
        {
            puzzleFailCounted = true;
            puzzleStrikes++;
            if (puzzleStrikes >= PuzzleMaxStrikes && puzzleSolutionText == null && puzzleSet != null && puzzleIndex < puzzleSet.Count)
                puzzleSolutionText = HintGenerator.DescribeSolution(puzzleSet[puzzleIndex].Build(), puzzleAttacker);
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
        AddInfo(body, "EARLY DEV — Puzzles is still being built; expect rough edges. Right-click a card to report a bug.");
        var pz = (puzzleSet != null && puzzleIndex < puzzleSet.Count) ? puzzleSet[puzzleIndex] : null;
        if (pz != null)
        {
            AddInfo(body, $"Puzzle {puzzleIndex + 1}/{puzzleSet.Count}  ·  {DifficultyWord(pz.Difficulty)}");
            AddInfo(body, pz.Title);
        }

        var status = puzzleRuntime != null ? puzzleRuntime.Status : PuzzleRuntime.PuzzleStatus.NotStarted;
        if (status == PuzzleRuntime.PuzzleStatus.Solved)
        {
            AddInfo(body, "Solved — that's lethal!");
            if (pz != null && !string.IsNullOrEmpty(pz.Teaches)) AddInfo(body, pz.Teaches);
            AddButton(body, "Next Puzzle", NextPuzzle, true, false);
            AddButton(body, "Replay", () => LoadPuzzle(puzzleIndex), true, false);
        }
        else if (status == PuzzleRuntime.PuzzleStatus.Failed)
        {
            AddInfo(body, puzzleRuntime != null ? puzzleRuntime.Message : "That line doesn't win.");
            AddButton(body, "Try Again", () => LoadPuzzle(puzzleIndex), true, false);
            AddButton(body, "Next Puzzle", NextPuzzle, true, false);
        }
        else
        {
            // No "you're off track" tell mid-solve — that would hand over the answer. Just the neutral prompt.
            AddInfo(body, "There is lethal here. Find the line.");
            AddInfo(body, "Attack, spread your DON!!, and play cards to close it out.");
            if (!string.IsNullOrEmpty(puzzleHintText)) AddInfo(body, "Hint: " + puzzleHintText);
            if (puzzleHintLevel < 3)
                AddButton(body, puzzleHintLevel == 0 ? "Hint" : "Reveal more", () => RevealHint(puzzleHintLevel + 1), true, false);
            // End the attempt on purpose (e.g. you don't see the line): ends the turn, which fails the puzzle and
            // costs a strike — after 3 the winning line is revealed. Restart, by contrast, is a free do-over.
            AddButton(body, "Give Up (End Turn)", () => Dispatch(new GameCommand { Type = "endTurn", Seat = state.ActiveSeat }), CanEndTurn(), false);
            AddButton(body, "Restart", () => LoadPuzzle(puzzleIndex), true, false);
            AddButton(body, "Next Puzzle", NextPuzzle, true, false);
        }
        AddButton(body, "Exit to Menu", ReturnToMenu, true, false);
    }
}
