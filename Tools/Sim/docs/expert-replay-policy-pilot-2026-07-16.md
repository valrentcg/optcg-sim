# High-bounty replay learning pilot — 2026-07-16

## Outcome

The retained honest `contract-v2` Advanced policy cleared the requested 55% point-estimate target on
two independent seed blocks against the stronger local Champion opponent, using the validated local
meta lists that best match the public high-bounty replays:

| Block | Seeds | Result | Invalid | 95% Wilson interval |
|---|---:|---:|---:|---:|
| Policy A/B screen | 0 | 23/40 = 57.5% | 0 | 42.2%–71.5% |
| Untouched confirmation | 900000 | 25/40 = 62.5% | 0 | 47.0%–75.8% |
| Pooled descriptive result | both | **48/80 = 60.0%** | **0** | **49.0%–70.0%** |

This supports “55% as a repeated point estimate on this local benchmark.” It does **not** support
“the true win rate is above 55% with 95% confidence”; the pooled lower bound is still 49.0%.

## What was learned from the replay logs

`expert-sync` collected five public games containing ten 1,000+ bounty player-games. The privacy-safe
model contains 143 attacks, of which 113 (79.0%) targeted the opposing Leader. It stores aggregate
counts per Leader and globally. It does not store player names, hands, life cards, hidden ordering, or
authoritative instance ids.

The raw logs cannot be treated as exact deck dumps: their `RZ1` rows are state-transition/shuffle
events and repeatedly mention the same physical cards. Instead, each replay player is matched to the
validated local meta list with the same Leader and the highest overlap with the cards observed in the
log. These are explicitly called **replay-matched deck proxies**, never reconstructed exact lists.

The current proxy suite is:

- `op15-by-nami`
- `op15-foxy`
- `op16-gb-luffy`
- `op16-luffy-n-ace`
- `op16-p-enel`
- `op16-py-rosinante`
- `op16-yb-teach`

## Promotion decision

The opt-in `expert-v1` policy uses the replay-derived, Leader-conditioned attack-target preference on
an honestly determinized world, with engine validation and the existing greedy policy as its fail-closed
fallback. In the paired 40-game screen it changed 26 attack targets, but its match outcomes were exactly
the same as `contract-v2`: 23/40 for both, 0 paired improvements, 0 paired regressions.

Therefore `expert-v1` is **not promoted**. The ingestion/model pipeline is useful and remains available
for a larger corpus, but changing behavior without changing outcomes is not a strength upgrade. The
retained `contract-v2` policy produced the 25/40 untouched-seed confirmation.

## Scope and limitations

- This tests an honest Advanced bot against the local Champion agent while both use replay-matched high-
  bounty deck proxies. It does not simulate the decision quality of the human high-bounty players.
- The seven-Leader breakdown is unstable at this sample size (for example, Foxy and Teach reverse sharply
  between blocks). Use the pooled suite result; do not claim per-Leader strength from 4–6 games.
- Public anonymous access exposes a rolling latest-20 feed. `expert-sync` also retains three stable public
  bootstrap replay ids found during the audit. Re-running it can add newly visible 1,000+ bounty games.
- Raw logs are ignored by git because they contain player handles and hidden-state evidence. Only the
  privacy-safe aggregate catalog/model should be shared.

## Reproduction

From `Tools/Sim`:

```text
dotnet run -c Release -- expert-sync 1000
dotnet run -c Release -- honest-expert-check 20 8 0
dotnet run -c Release -- honest-expert-gate 20 8 900000
```

The existing live dashboard is updated by both benchmark commands.

## Verification

- Release build: clean, 0 warnings / 0 errors.
- `clonetest`: 19/19.
- `observed-seam-test`: 7/7.
- `boundary-test`: all deterministic boundary fixtures pass.
- `determinizer-test`: 7/7.
- `privacy-test`: still exits red on its deliberately cheating `FromLegacy` Layer-2 baseline; its own
  output explicitly says this is expected and points to the green honest-path suites above.
