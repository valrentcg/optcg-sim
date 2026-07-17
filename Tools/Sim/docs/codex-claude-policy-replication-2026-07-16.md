# Honest policy replication: Claude vs Codex — 2026-07-16

## Pre-registered protocols

Both runs used the direct paired `honest-policy-check`, all clean deck identities, varied field opponents,
node budget 100, and did not rerun after observing the result.

| Run | Pairs / games per arm | Seed offset |
|---|---:|---:|
| Claude | 60 / 120 | 700000 |
| Codex | 80 / 160 | 900000 |

The seed blocks do not overlap. Claude's counts below are transcribed from its completed-run report; Codex's
counts are retained by the progress board and the command output.

## Results

| Run | Honest search | Routed honest policy | Paired comparison |
|---|---:|---:|---:|
| Claude | 9/112 decided = 8.0% | 49/120 = 40.8%, Wilson 95% CI 32.5–49.8% | +35 net; 37 better / 2 worse |
| Codex | 10/155 decided = 6.5%, Wilson 95% CI 3.5–11.5% | 76/160 = 47.5%, Wilson 95% CI 39.9–55.2% | +64 net; 65 better / 1 worse |

The routed confidence intervals overlap substantially, and both paired comparisons strongly favor the route.
The runs independently replicate the same conclusion; the difference between 40.8% and 47.5% is compatible
with ordinary sample variation at these sizes.

For a directional pooled read across the two independent seed blocks (not a replacement for a larger final
protocol): routed = 125/280 = 44.6% (Wilson 95% CI 38.9–50.5%); search = 19/267 decided = 7.1%
(Wilson 95% CI 4.6–10.8%). Paired discordances total 102 better versus 3 worse.

## Dashboard

`ProgressBoard` writes one JSON file per arm and regenerates `Results/progress/dashboard.html` after every
completed game. The policy A/B now starts both arms together under the requested total worker budget, so both
bars can advance during the same run. Each bar includes the seed offset, progress, wins, losses, invalids,
elapsed time, and estimated time remaining. A completed arm is now marked done immediately instead of waiting
for the other arm.

Current local page: `http://127.0.0.1:8765/dashboard.html`

Standalone file: `Tools/Sim/Results/progress/dashboard.html`

## Interpretation

This independently confirms the same-day competence upgrade. It does not establish a 70% bot. The routed bot
is approximately baseline-parity, while the current low-budget honest search/evaluation path is materially
worse. Keep the route as the playable default and improve identity-specific policy behavior from that floor.
