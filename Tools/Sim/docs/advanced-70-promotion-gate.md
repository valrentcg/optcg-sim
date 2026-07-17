# Advanced honest bot: 70% promotion gate

## Target

The shipped advanced honest bot must win at least 70% against the frozen `IntermediateBot` benchmark on a
pre-registered, paired, all-clean-deck field. Reaching parity with `IntermediateBot` is the competence floor,
not the advanced target.

## Required gates

1. **Honesty:** dedicated observation, boundary, determinizer, ledger, K-world, and hidden-permutation
   noninterference checks pass. No policy may inspect the referee's hidden assignment.
2. **Primary strength:** at least 70% outright wins on the advanced arm in a fresh, pre-registered paired
   all-deck run. Adjudicated, stuck, thrown, and invalid games are reported separately and never counted as wins.
3. **Sample size:** screening may use 16–40 games, but promotion requires at least 300 advanced games on an
   untouched seed block. No rerun or parameter change after viewing the block.
4. **Uncertainty:** report the Wilson 95% interval. The point estimate must be at least 70%; the interval and
   paired better/worse counts remain visible so 70.1% from noise is not presented as solved.
5. **Coverage:** report results by deck identity and leader. A global win rate cannot hide an identity that
   consistently regresses. The initial minimum floor is 55% for every identity with adequate samples.
6. **Robustness:** also test against at least one non-`IntermediateBot` fixed policy or policy snapshot. A
   best response that reaches 70% only by overfitting one deterministic opponent is not generally advanced.
7. **Validity:** the candidate may not introduce additional invalid/stuck games versus the competent routed
   floor.

## Current anchors

- Independent routed-policy replications: approximately 44.6% combined (125/280), establishing the honest
  competent floor around parity rather than 70%.
- Current low-budget honest search: approximately 7.1% combined (19/267 decided), rejected as a playable
  default.

## First advanced candidate: K-world greedy rollouts

The first candidate performed one-step policy improvement: enumerate legal root actions, preserve the greedy
action as incumbent, roll each candidate to completion with `IntermediateBot`, and aggregate across four
honest determinized worlds. It remained behind an explicit `useAdvancedPolicy` experiment flag and never
changed the default.

Results:

| Block | Greedy floor | Rollout candidate | Paired result | Verdict |
|---|---:|---:|---:|---|
| Viability screen, all field, seed 1020000 | 8/16 | 13/16 = 81.2% | 5 better / 0 worse | Screen passed |
| Independent validation, all field, seed 1030000 | 20/40 | 19/40 = 47.5% | 6 better / 7 worse | Rejected |
| Aggro diagnostic, seed 1040000 | 7/8 | 5/8 = 62.5% | 0 better / 2 worse | Rejected |
| Control diagnostic, seed 1050000 | 4/8 | 5/8 = 62.5% | 1 better / 0 worse | Below target |

The 81% screen was a small-sample false positive. The independent block is authoritative: the universal
rollout candidate did not improve the floor and did not reach 70%. This result must remain in the record.

## Next build direction

Do not run more global weight mutations or another universal policy switch. Build deck-identity/leader-level
strategy contracts and learn only the override decisions that consistently beat the greedy incumbent across
many determinized worlds. Track which root actions produce paired improvements, distill those into bounded
policy modules, and validate each module before composing them. The 70% goal then applies to the composed
advanced bot on a new all-deck seed block.

## Contract v2 result (2026-07-16)

Contract v2 composes three bounded, deck-identity-aware modules over the honest determinized greedy floor:

- resolution-only K=4 Pareto rollout overrides for every identity;
- leader-pressure redirects for control decks;
- conservative, once-per-turn guarded `[Activate: Main]` usage for midrange decks.

The activation work uncovered a command-shape defect: generated `activateMain` commands put the card id in
`InstanceId`, while `GameEngine` reads `Target`. That made all generated activation actions silent no-ops.
Both legal-action copies and the observed-root translator now emit the engine's real command shape.

### Primary pre-registered block

All 37 clean meta decks, same-deck mirrors, both seats, 150 pairs / 300 scheduled games, untouched seed block
1180000, against the frozen `IntermediateBot` benchmark:

| Arm | Outright wins | Scheduled | Rate | Invalid |
|---|---:|---:|---:|---:|
| Honest greedy floor | 154 | 300 | 51.3% | 0 |
| Honest Advanced contract v2 | 211 | 300 | **70.3%** | 0 after engine fix |

Wilson 95% interval for 211/300: **64.9%–75.2%**. The point estimate clears the requested 70% target; the
interval remains visible and does not establish that the true rate is above 70%.

Identity coverage after counting the reproduced invalid as its verified post-fix loss:

| Identity | Wins | Scheduled | Rate |
|---|---:|---:|---:|
| Aggro | 22 | 40 | 55.0% |
| Combo | 26 | 34 | 76.5% |
| Control | 38 | 50 | 76.0% |
| Midrange | 125 | 176 | 71.0% |

The original block reported 79 paired improvements / 22 regressions plus one candidate invalid. The invalid
was reproduced exactly as `op16-kalgara`, scenario 96, south: a card played from a deck look opened a nested
`[On Play]` deck look before the parent returned its four unselected cards, orphaning those cards. The engine
now queues the nested effect, blocks pending-effect advancement during an active deck-look/choice, and the
bot resolves deck-look sub-decisions before queued effects. A dedicated nested-look fixture preserves all 50
cards through the sequence. The exact failed game now finishes as an outright loss, and a full post-fix replay
of all 300 Advanced games completed with **0 invalid** and the same **211 wins / 89 losses**.

### Robustness block

Against the fixed evolved Champion snapshot on a fresh 74-game all-deck mirror block (seed 1190000):

| Arm | Wins | Rate | Paired delta | Invalid |
|---|---:|---:|---:|---:|
| Honest greedy floor | 39/74 | 52.7% | — | 0 |
| Honest Advanced contract v2 | 45/74 | 60.8% | 10 better / 4 worse = +6 | 0 |

Contract v2 does not reach 70% against Champion, so 70.3% must be described specifically as the result versus
the frozen `IntermediateBot` baseline. It nevertheless retains an 8.1-point and +6 paired improvement against
a materially different fixed policy, so the upgrade is not only a brittle best response.

### Promotion status

**Promoted for the headless OpenList research path. Not yet shipped as Unity's playable Advanced tier.**

`GameManager.AdvancedAiTick()` still calls the historical `Engine/Bot/Search/SearchBot`, which receives the
authoritative referee state and is not the policy measured above. Shipping contract v2 requires moving the
determinizer/ledger and bounded identity modules into the Unity-compiled engine path (or driving them through
the strict observed-agent seam), then re-running the same gates on that exact binary. Until then, do not cite
211/300 as the strength of the in-game Advanced button.
