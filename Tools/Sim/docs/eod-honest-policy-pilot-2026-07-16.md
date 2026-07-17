# EOD honest policy pilot — 2026-07-16

## Outcome

The low-budget honest search policy is not the strongest deployable honest policy currently in the
repository. An `IntermediateBot` decision made only on a freshly determinized world is hidden-information
safe and substantially stronger in the bounded paired tests below. `HonestPlannerBot` now automatically
uses that route for every currently recognized deck identity (`aggro`, `midrange`, `control`, `combo`).
Experiments can force either arm with `useGreedyPolicy: false/true`. Unknown future identities remain on
search until measured.

This is a same-day competence checkpoint, not a 70% claim and not evidence that search/evaluation work is
finished. The routed policy is approximately baseline-strength; its value is that it replaces a much weaker
honest search policy without reading the referee's hidden assignment.

## Cross-check findings

- The earlier pooled `29/70` style total is not a valid 70-game sample if it combines `honest-play` runs:
  that command uses the same fixed schedule, so larger runs contain the smaller runs.
- K=1/K=8 and archetype-weight nulls were severely underpowered; they do not establish general null effects.
- The observed perfect-information result is an empirical result for one evaluator/configuration, not a
  theoretical 65% ceiling.
- A direct hidden-permutation noninterference assertion now verifies that the routed policy returns the same
  command when only the authoritative hidden assignment changes.
- `privacy-test` is a stale expected-red legacy-adapter test. It still says the determinizer, deterministic
  fixtures, and K-world aggregation are pending even though their dedicated suites now exist and pass.

## Mutation and policy results

All games below are paired on matchup, seat/turn-order schedule, and seed. Wins are outright unless noted.
Invalid games never count as wins.

| Identity / schedule | Honest search | Honest routed policy | Paired change | Invalids |
|---|---:|---:|---:|---:|
| Aggro, mirror, first untouched block | 0/12 | 9/12 | 9 improved, 0 regressed | 0 / 0 |
| Aggro, mirror, independent block | 0/19 | 13/19 | 13 improved, 0 regressed | 1 common |
| Aggro, varied field | 1/24 | 15/24 | 14 improved, 0 regressed | 0 / 0 |
| Midrange, varied field | 2/16 | 9/16 | 7 improved, 0 regressed | 0 / 0 |
| Control, varied field | 2/12 | 6/12 | 5 improved, 1 regressed | 0 / 0 |
| Combo, varied field | 2/10 decided | 4/12 | 3 improved, 1 regressed | search 2 / route 0 |
| All identities, varied field | 4/39 decided | 18/40 | 14 improved, 1 regressed | search 1 / route 0 |

The eight aggro weight candidates (default, two race presets, five deterministic sign-preserving mutations)
showed no useful held-out gradient in the mirror pilot. The policy-family mutation separated immediately.

## Reproduction commands

Run from `Tools/Sim` so imported deck paths resolve:

```text
dotnet run -c Release --no-build -- honest-mutate 3 12 100 aggro 4 80000 field
dotnet run -c Release --no-build -- honest-policy-check 8 100 midrange 4 120000 field
dotnet run -c Release --no-build -- honest-policy-check 6 100 control 2 140000 field
dotnet run -c Release --no-build -- honest-policy-check 6 100 combo 2 160000 field
dotnet run -c Release --no-build -- honest-policy-check 20 100 all 4 200000 field
```

## Verification

- Release build: clean, zero warnings.
- `clonetest`: 19/19.
- `observed-seam-test`: 7/7.
- `boundary-test`: pass across all ten reached boundaries.
- `determinizer-test`: 7/7, including routed-policy hidden-permutation noninterference.
- `ledger-test`: 9/9.
- `kworld-test`: 6/6.
- Same unpaired 12-game smoke schedule: 0/12 before routing, 3/12 after; runtime fell from 19s to 1s.
  Do not use this smoke as the strength estimate—the paired A/B above is the valid small-sample comparison.

## Next measurement

Keep the routed policy as the competent default and treat low-budget search as the experimental arm. The next
strength work should mutate policy behavior/features by identity, not another global evaluation-weight vector.
Use a larger pre-registered paired field run before attaching a stable win-rate number to this checkpoint.
