# Discovered heuristics — living digest

Snapshot of the strongest, **holdout-validated** heuristics the simulation platform has distilled so
far. All rules are **deck-agnostic**: conditions are generic hand-shape / deck-vs-deck features, never
leader names — the advanced bot applies them by computing the same features from its deck vs the
opponent's. Regenerate any section with:

```
python analyze_trials.py Results/<experiment> --min 5000 --holdout 0.2 [--json policy.json]
```

Methodology: each rule is a **matched-seed counterfactual** — the same game is played under choice A
and choice B on an identical seed, so the reported effect `P(win|A) − P(win|B)` is causal, not
correlational (§10.5). A rule is listed only if it clears the promotion gate (n ≥ 5,000 paired trials,
95% CI excludes 0) **and** its sign re-confirms on a 20% holdout split it never trained on (§11.3).

---

## Turn order — go first vs go second  (`overnight-turnorder`, ✅ COMPLETE)

Choice A = go **first**. Positive effect ⇒ prefer going first. **1,089,000 games, 544,499 paired
trials, 1 no-result (0.0002%).** 34 holdout-validated rules → `policy.json`.

- **Overall: +7.2 pp** to go first across the field, 95% CI **[+7.0, +7.3]** — definitive.
- **`my_low_curve = 0.1` → +10.9 pp** (n=66,000, CI [+10.4,+11.3]): decks with almost no ≤2-cost
  cards want the first turn most.
- **`opp_low_curve = 0.1` → +10.7 pp**; **`i_am_faster = false` → ~+8 pp**: going first matters more
  when your deck is the slower / higher-curve side and needs the tempo.
- **Every one of the 34 promoted rules re-validated on a 20% holdout** it never trained on.

_Interpretation for the bot: essentially always choose first; the edge is largest for slower /
low-curve-poor decks. Full ranked list in `Results/overnight-turnorder/heuristics.md`._

## Mulligan — keep vs mulligan  (`overnight-mulligan`, queued)
## Counter economy — conserve vs defend  (`overnight-counter-economy`, queued, vs all styles)
## Aggression — face-rush vs mixed targeting  (`overnight-aggression`, queued, vs all styles)

_Pending — these run after turn-order/mulligan free the CPU. Counter-economy and aggression run
against the full opponent-style pool (baseline / aggro / conservative) so each rule is checked for
robustness across playstyles._

---

_This digest is refreshed periodically from the live trial data; the authoritative machine-readable
form is the per-family `policy.json` exported by `analyze_trials.py --json`._
