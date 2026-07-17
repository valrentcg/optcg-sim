# Advanced contract v2 — verified checkpoint

## Outcome

The honest OpenList headless Advanced candidate reached **211/300 outright wins (70.3%)** against the frozen
`IntermediateBot` baseline on the pre-registered all-clean-deck mirror block. The frozen honest greedy floor
was 154/300 (51.3%). Wilson 95% for Advanced is 64.9%–75.2%.

Against the evolved Champion snapshot, Advanced scored 45/74 (60.8%) versus its paired greedy floor's 39/74
(52.7%), with 10 improvements / 4 regressions and no invalids.

## What changed

- Added `AdvancedRolloutPolicy`: K=4, resolution-only, Pareto loss-to-win overrides.
- Added `AdvancedPressurePolicy`: control-only profitable leader-pressure redirect.
- Added `AdvancedActivationPolicy`: midrange-only, engine-validated `[Activate: Main]`, once per card/turn.
- Corrected `activateMain` command translation to use `Target` in both legal-action copies and the observed seam.
- Added live progress/identity/leader/Wilson/failure reporting to `honest-advanced-check`.
- Added fixed-opponent robustness modes (`baseline`, `champion`, `aggro`, `conservative`).
- Added diagnostic invalid replay commands and full card-conservation evidence.

## Engine defect found and fixed

The original 300-game block contained one invalid. Exact replay showed an `op16-kalgara` nested deck look
orphaned four cards. A card played from the parent look immediately opened its own `[On Play]` look, overwriting
the parent transient zone. The engine now queues that `[On Play]`, refuses to resolve/pass queued effects while
a deck-look or choice is active, and `IntermediateBot` resolves the active deck look before pending effects.

Verification:

- exact scenario 96 completes normally;
- nested-decklook regression preserves 50 cards at all three transitions;
- post-fix Advanced-only replay: 300/300 complete, 0 invalid, 211/89;
- boundary, observed-seam, determinizer, ledger, K-world, and clone suites pass;
- `Assembly-CSharp.csproj` builds successfully.

## Critical remaining integration boundary

The result belongs to `Tools/Sim/Planning/HonestPlannerBot` with `contract-v2`. Unity's
`GameManager.AdvancedAiTick()` still invokes the old referee-state `SearchBot`. The next implementation step is
to port the exact determinizer/ledger/identity modules into the Unity-compiled observed path and validate that
binary. Do not relabel the current in-game Advanced button with the 70.3% result before that work.

## Reproduction commands

From `Tools/Sim`:

```text
dotnet run --project Sim.csproj -c Release --no-build -- honest-advanced-check 150 all 8 1180000 mirror contract-v2 baseline
dotnet run --project Sim.csproj -c Release --no-build -- honest-advanced-invalid-replay 150 8 1180000 contract-v2
dotnet run --project Sim.csproj -c Release --no-build -- honest-advanced-check 37 all 8 1190000 mirror contract-v2 champion
dotnet run --project Sim.csproj -c Release --no-build -- nested-decklook-test
```
