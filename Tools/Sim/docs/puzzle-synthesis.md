# Solution-first puzzle synthesis

## Why this replaced the old pipeline

A forced-lethal proof answers only one question: “does at least one winning line exist?” It does not distinguish
a puzzle from an ordinary winning board where almost every attack order succeeds. Live-game harvesting made that
problem worse because most real lethal turns are intentionally engineered to be redundant alpha strikes.

The player-facing library therefore no longer uses the old random-generator bank or unstamped harvested positions.
Harvesting remains an optional source of candidates, but it must pass the same quality oracle as synthesized content.

## Architecture

1. `PuzzleSynthesizer` starts with a tactical solution schema and composes the minimum state needed to force it.
2. `LethalSolver` proves the attacker wins against every legal defender response.
3. `PuzzleQualityAnalyzer` solves every alternative at the first four attacker decisions.
4. A candidate is admitted only when:
   - the result is a complete proof, never a truncated/unknown search;
   - the winning line has at least three meaningful attacker actions;
   - at least two decisions contain both proven-winning and proven-losing choices;
   - winning choices are a minority at each counted decision;
   - a plain attack-only line does not win, unless the opening attack order itself is selective.
5. `PuzzleLibrary.All()` contains only a small, manually curated seed manifest. More seeds are not content unless
   they add a genuinely different decision structure.

## Current tactical families

- `ExactSplit`: exact DON!! distribution across multiple attackers; stacking or attacking early loses.
- `WallAndSplit`: a blocker must be neutralized while preserving the exact DON!! split.
- `LifeCounterOrder`: the known Life card becomes counter after the first hit, so attack size and order interact.
- `RushCounterGate`: exact play/attach sequencing must overcome a blocker-disable threshold and a Counter Event.

These are solution schemas, not fixed board templates. Card identities can vary within curated, color-legal role
pools while the tactical constraints remain stable.

## Validation commands

From the repository root:

```powershell
dotnet build "Tools/Sim/Sim.csproj" -c Release
dotnet run --project "Tools/Sim/Sim.csproj" -c Release --no-build -- lethaltest
dotnet run --project "Tools/Sim/Sim.csproj" -c Release --no-build -- puzzlesynth 24
dotnet run --project "Tools/Sim/Sim.csproj" -c Release --no-build -- puzzlecheck
dotnet build "Assembly-CSharp.csproj" -c Release
```

`puzzlesynth` explores candidates. `puzzlecheck` is the shipping guard: every library entry must pass strict
decision quality and then solve end-to-end through `PuzzleRuntime` against optimal defense.

## Information model

Deterministic lethal puzzles reveal the opponent's hand and Life because the verifier uses those exact cards.
Hidden-information probability analysis is a separate problem and should become a separate mode, not a source of
guesswork inside a deterministic puzzle.

## Adding another family

Start from a tactical dependency graph, not a random board:

1. Define the required decisions and the resource thresholds that make each one necessary.
2. Add at least one tempting legal alternative that provably loses.
3. Compose the smallest legal state that expresses those constraints.
4. Add the family to `PuzzleSynthesizer`.
5. Run `puzzlesynth`, inspect accepted seeds for structural duplicates, and curate only novel examples.
6. Add a focused regression test for any new engine behavior the family depends on.
7. Run `puzzlecheck`; never add a seed by inspection alone.

Counter Events and rich `[When Attacking]` resolution commands are included in legal search. The attack-only
counterfactual must retain mandatory resolution commands or it will falsely classify ordinary alpha strikes as
setup puzzles.
