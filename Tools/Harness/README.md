# OPTCG Engine Test Harness

Headless QA for the game engine. The engine (`Assets/Scripts/Engine/*.cs`) is pure C# with no
UnityEngine dependencies, so this `dotnet` project compiles it directly and runs battles without
Unity. Card stats/effects load from `Assets/StreamingAssets/Cards/official-card-library.json`.

## Run

```
cd Tools/Harness
dotnet run -c Release                 # bot-vs-bot sweep across all 33 starter decks
dotnet run -c Release -- 5            #   ...with 5 seeds per pairing (default 3)
dotnet run -c Release -- scenario     # constructed-scenario regression tests (Scenarios.cs)
dotnet run -c Release -- coverage     # LAYER 0: drive EVERY card's effect, flag unimplemented/stuck/crash/invariant
dotnet run -c Release -- invariants 2 # LAYER 1: replay all games one command at a time, assert rules invariants
dotnet run -c Release -- golden       # LAYER 2: diff every card's outcome vs the committed golden baseline
dotnet run -c Release -- golden write #   ...accept current outcomes as the new baseline
dotnet run -c Release -- trace st01 st02   # trace one matchup over 10 seeds + a battle log
```

Nothing here ships with the game — `Tools/` is outside `Assets/`, so Unity never compiles it into
a build. Only the ENGINE fixes these tests drive out get shipped. Reports land in `findings/`.

## The effect-testing system (why you stop finding bugs in playtests)

The engine resolves effects by pattern-matching card TEXT, so bugs are either "wording no handler
recognizes" or "a compound clause silently dropped" or "handled but breaks a rule." Four layers
catch those without hand-writing 2636 tests:

- **Layer 0 — `coverage`** (`EffectCoverage.cs`): for every card, stages a minimal scenario per
  applicable timing (On Play / Activate:Main / When Attacking / Trigger / event Main), auto-supplies
  plausible targets so resolution bodies actually run, and classifies **OK / NOT_AUTOMATED**
  (engine logged "manual resolution" = unimplemented) **/ STUCK** (couldn't drive to completion) **/
  CRASH / INVARIANT**. It also runs a **COMPOUND_SUSPECT** heuristic: a compound effect whose
  sub-clause left no trace in the resolution log — the "dropped clause" class (found OP08-118).
  → `findings/effect-coverage.md`.
- **Layer 1 — `invariants`** (`Invariants.cs`): replays every deck pairing ONE command at a time and
  asserts, after each command, rules that must always hold — DON!! conservation (deck+cost+attached
  == 10), zone-tag integrity, no duplicate instance ids, board ≤ 5, attached-DON!! uniqueness. This
  is the correctness-by-invariant oracle (found the mulligan zone desync). → `findings/invariant-violations.md`.
- **Layer 2 — `golden`**: snapshots each card's canonical outcome (normalized log + zone counts) to
  `findings/golden-snapshots.tsv`. Review once; thereafter any behavioral change shows as a diff.
  This is the regression lock — run it after every engine change.
- **Scenario tests** (`Scenarios.cs`): hand-authored exact-outcome assertions, one per fixed bug.

**What it CAN'T do:** confirm a *handled* effect matches its text exactly with no oracle — Layers 0/1
prove an effect is wired up and rules-invariant; the golden snapshots + scenario tests are where
per-card "as written" correctness gets locked in after a human reviews it once.

## What it checks

- **Sweep** (`Program.cs`): every starter-deck pairing × N seeds via `IntermediateBot.PlayFullMatch`.
  Flags crashes (exceptions), stalls (never reach `state.Status=="finished"` → deadlock/loop),
  and end-state invariant violations (dup instance ids, >5 board, runaway hand). Reports end
  reason (life-out vs deck-out), turn distribution, and per-deck deck-out rate. A healthy run is
  ~12 avg turns, ~99% life-out, 0 crashes/stalls.
- **Scenarios** (`Scenarios.cs`): builds specific board states and drives real `GameEngine`
  commands to assert exact card-rule outcomes. Add a test per bug fixed. NOTE: the DEFENDER owns
  the final `resolveAttack` (send it with the defender's seat).

## Adding a scenario test

Construct in-play cards with `Zone = "character"/"trash"`, set `state.Phase/ActiveSeat/TurnNumber`,
drive with `GameEngine.ApplyCommand`, then `Check(name, condition, detail)`. See existing tests.
