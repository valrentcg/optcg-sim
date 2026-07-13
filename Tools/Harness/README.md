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
dotnet run -c Release -- trace st01 st02   # trace one matchup over 10 seeds + a battle log
```

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
