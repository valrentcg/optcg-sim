# Lucy vs Enel directional matchup — 2026-07-16

## Method

The honest Advanced `contract-v2` policy was tested in both directions against the Champion opponent:

- Advanced `op16-blue-lucy` into Champion `op16-p-enel`
- Advanced `op16-p-enel` into Champion `op16-blue-lucy`

Every seed was played with the Advanced candidate in both seats. Results are outright wins only;
adjudicated, capped, stuck, and invalid games do not count as wins.

## Results

| Seed block | Advanced Lucy -> Enel | Advanced Enel -> Lucy | Invalid |
|---:|---:|---:|---:|
| 1200000 | 35/40 = 87.5% | 17/40 = 42.5% | 0 |
| 1300000 | 19/20 = 95.0% | 9/20 = 45.0% | 0 |
| Combined | **54/60 = 90.0%** | **26/60 = 43.3%** | **0** |

Combined 95% Wilson intervals:

- Lucy -> Enel: 79.9%–95.3%
- Enel -> Lucy: 31.6%–55.9%

The directional asymmetry replicated. The honest Advanced bot is highly competitive piloting this Lucy
list into Champion Enel, but does not meet the 55% target when piloting this Enel list into Champion Lucy.

## Symmetric Advanced-vs-Advanced correction

The Champion comparison above changes both deck and pilot. A symmetric follow-up put the same honest
Advanced `contract-v2` architecture on both sides, paired every seed, and let each deck go first once:

- Advanced Lucy: **60/60 = 100.0%**
- Advanced Enel: **0/60 = 0.0%**
- Lucy going first: 30/30; Lucy going second: 30/30
- Adjudicated/invalid: 0/0

This is not a credible real-metagame estimate. The checked-in ranked reference reports OP15-058 Enel at
66.9% into OP15-002 Lucy, so the symmetric result proves a simulator policy/model mismatch rather than a
real Lucy advantage.

## Identity finding

The current `DeckFingerprint` classifies `op16-blue-lucy` as `combo` and `op16-p-enel` as `aggro`.
That Enel classification is a concrete candidate for the weak direction: `contract-v2` routes pressure
only for control and main-ability activation only for midrange, so an Enel list classified as aggro gets
neither identity-specific clean-main layer.

That causal test was then run. Routing Enel explicitly through `activation` produced 268 validated main
activations and improved Enel from 0/60 to 11/40 (27.5%) against contract-v2 Lucy. The identity mistake is
therefore a real part of the failure, but not the whole failure: even with its activation layer Enel remains
far below the 66.9% ranked reference. The remaining gap is likely Enel-specific DON-minus/event sequencing,
target selection, and possibly card-effect interaction fidelity. Do not tune global matchup weights to the
60-0 result.

## Mechanic-routed follow-up

The shipped and honest policies were then changed so a leader with `[Activate: Main]` sets a general
`RequiresMainActivation`/`activation-engine` identity, regardless of the curve label. Rested-DON conversion,
zero-value Trigger rejection, correct end-of-next-turn duration, and unreserved-DON commitment were also
fixed as mechanic-level rules. A new paired symmetric block (seed offset 400) produced:

- Advanced Lucy: **11/20 = 55.0%**
- Advanced Enel: **9/20 = 45.0%**
- Invalid: **0**

This materially rejects the former 60-0 pathology, but the sample is too small to establish the ranked
matchup. It is evidence that Enel now executes its engine, not evidence that the simulator is fully calibrated.

## Reproduction

From `Tools/Sim`:

```text
dotnet run -c Release -- honest-matchup-check op16-blue-lucy op16-p-enel 20 8 1200000 contract-v2 champion
dotnet run -c Release -- honest-matchup-check op16-blue-lucy op16-p-enel 10 8 1300000 contract-v2 champion
dotnet run -c Release -- honest-advanced-selfplay op16-blue-lucy op16-p-enel 30 8 1400000 contract-v2 contract-v2
dotnet run -c Release -- honest-advanced-selfplay op16-blue-lucy op16-p-enel 20 8 1500000 contract-v2 activation
```

The benchmark updates both directional bars on the existing live dashboard.
