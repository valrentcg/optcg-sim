# Brain Teasers / Lethal Puzzles — concept and architecture

Status (2026-07-23): SOLVER + HINTS + RUNTIME BUILT (shipped engine), 8/8 on `dotnet run -c Release lethaltest`.
The solver, hint generator, and the player-facing runtime state machine now live in `Assets/Scripts/Engine/
Puzzles/` (namespace `OnePieceTcg.Engine.Puzzles`) so BOTH Tools/Sim (tested there via Sim.csproj includes)
and the Unity game can use them. Remaining: the Unity screen (renders the board + hint glows on top of the
runtime), and puzzle authoring/harvesting.

## The idea

A daily "brain teaser" / warm-up mode, later a separate puzzle-solving ranked ladder, in the spirit of chess
puzzles: one board, one turn, find the winning line. This is deliberately SEPARATE from the AI opponent.

Prior art referenced during research:
- real_joshua's Lab "Lethal Calculator" — a strong combat-math solver (enumerates DON!! spreads, attack
  orders, counter/take-life/block branches, reports guaranteed lethal + probability). Its own author notes it
  does NOT model active DON!!/Counter Events or many card effects — it is a combat solver, not a full
  card-game solver.
- "Lethal Line" — a daily OPTCG puzzle game (one board, one turn) whose puzzles already involve effects,
  blocker disabling, bouncing, exact DON!! allocation, and known counters, with an authored canonical
  solution plus a bounded guaranteed-win search in its shipped code.
- OPCG "Lethal Thinking Vol. 1" — the clearest human framework: required hits = opponent Life + 1; defensive
  resources are counters, Life, and blockers; distribute DON!! so requirements are even when a blocker is up;
  attack weak-to-strong while Life remains (taking Life hands them a card); find the "boundary X" (max counter
  capacity a line beats) and compare to the opponent's likely hand; under the 5K-leader/all-2K-counter model
  the 7K/9K/11K thresholds are the efficient ones.

## Why not just use the bot

The bot (`TurnPlanner`) beam-prunes and models the defender with ONE greedy policy. It can *find* a winning
line, but it cannot *prove* lethal: a beam can drop the alternative that mattered, and a single defender
policy never checks that EVERY legal defense loses. A puzzle certificate needs an exact AND/OR search.

## The four pieces

1. **Puzzle snapshot** — the complete position (both boards, hands, Life-card order, DON!!), active player,
   card/rules version, win condition, and — critically — BOTH a materialized state AND the deterministic
   `{Seed, decks, CommandHistory}` that reconstructs it exactly (the existing `Puzzle` record in
   `Runner/PuzzleSuite.cs` already stores positions this way; the lethal puzzle format extends it).

2. **Exact lethal verifier** — BUILT: `LethalSolver`. Full AND/OR search over the real engine:
   - Attacker decisions = OR nodes (lethal if ANY action forces a win).
   - Defender decisions = AND nodes (lethal only if ALL defenses still lose) — enumerated exhaustively, never
     pruned, so a WIN is a real proof against optimal defense.
   - Outcomes are `Win` / `NoLethal` / `Unknown`. `Unknown` (budget exhausted) is NEVER conflated with
     `NoLethal` — that distinction is the whole point of a certificate. The engine is the legality authority
     (`LegalActions.Validate` = clone + apply + state-diff), so no rule is re-encoded. Sound transposition
     table keyed on a complete position string. Returns a principal variation (the winning line) for
     authoring and hints.

3. **Player-facing runtime** (designed) — the player controls the attacking side; the system auto-plays the
   STRONGEST SURVIVING defense (drive the AND branch that keeps the defender alive longest). Accept ANY
   verified winning line, not only the author's — after each player action, re-run `LethalSolver` from the
   resulting state; if it is still `Win`, the line is on track; if the move dropped it to `NoLethal`, flag the
   misstep. This lives in the shipped engine (like the bot was ported from here) so Unity can call it.

4. **Authoring + harvesting** (designed) — hand-author puzzles from interesting matches first; later scan
   match replays (we already store them, and now bug reports carry `{Seed, decks, CommandHistory}` too) for
   positions where a forced one-turn lethal EXISTS, the player/bot MISSED it, the proof finished without
   truncation (`!BudgetHit`), and the line is short enough to teach one identifiable idea (DON!! allocation,
   attack order, play-before-attack, blocker removal, restand/Rush, Double Attack, Counter awareness,
   trigger-safe sequencing).

## Hints — chess-puzzle style, three graduated levels

BUILT: `HintGenerator` derives hints from the solver's proven line, so a hint can never point at a
non-solution. Levels escalate:
- **L1 (obscure)**: names the theme/category + the human framing ("you need Life + 1 hits"), no card named.
- **L2 (targeted)**: highlights the single key card with a blurb about what to consider, without the move.
- **L3 (explicit)**: spells out the first one or two moves.
Each hint carries `HighlightInstanceIds` for the UI to glow the relevant card(s). Category is inferred from
the winning line (ability-first / play-before-attack / DON allocation / counter-aware / attack-order).

## Rollout order (matches the research recommendation)

1. One-turn puzzles, fully known hands/Life, no randomness.  <- solver + hints proven here now
2. Multiple-solution verification + graduated hints.          <- hint layer built; runtime accept-any next
3. Automated harvesting from match/replay/bug-report histories.
4. Random triggers / unknown Life: every possible outcome must still win (AND over chance nodes too).
5. A public-information probability analyzer (0K/1K/2K counter distribution + tracked cards) — the "lethal
   probability" number, separate from the exact certificate.
6. Later: "mate in two" (a whole opponent turn in between).

## Files

- `Puzzles/LethalSolver.cs`     — the exact AND/OR verifier (Win/NoLethal/Unknown + principal variation).
- `Puzzles/HintGenerator.cs`    — three-level graduated hints from the proven line.
- `Puzzles/LethalSolverTest.cs` — validation suite (`lethaltest`): terminal, hit-count = Life+1, and the
                                   AND node (one counter flips a position from Win to NoLethal, more attackers
                                   flip it back).
- `Runner/PuzzleSuite.cs`       — existing position/replay preservation to extend for the puzzle format.
