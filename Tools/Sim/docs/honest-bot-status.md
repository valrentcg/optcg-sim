# Honest advanced bot — EOD checkpoint status

The research bot searches HONESTLY: it never searches the opponent's true hidden cards. This is the
observation-boundary work made real. Everything here is in `Tools/Sim` (out of the shipped Unity build).

## What works today (verified)

- **Observation boundary** — the planner is confined to a legal `PlayerObservation`; the enforced
  `IObservedAgent` seam means an honest agent cannot receive or reach a referee `GameState` (opaque action
  tokens, runner-side token→command map, fail-closed translation).
- **K=1 determinizer** (`Runner/RootWorldSampler.cs`) — samples every zone hidden from the acting seat
  (opp hand, both decks, both face-down Life, the life card in flight) from the DECKLIST pool, discarding
  the referee's true hidden CardIds AND instance ids. Public/own-hand ids are preserved, so a plan found on
  the sampled world applies to the real state. Fails loud on any illegality.
- **Knowledge ledger** (`Runner/KnowledgeLedger.cs`) — tracks the cards a seat legally knows are on TOP of
  its own deck after a to-top deck look. Owner-scoped; gains only from legal events; reconciles by
  INSTANCE ID after every command (a coincidental same-CardId top does NOT relaunder knowledge). The
  determinizer PLACES these instead of reshuffling them — so the honest bot stops forgetting what it
  legitimately searched (the rayleigh problem).
- **`HonestPlannerBot`** — determinizes + searches per command (receding horizon), completes real games.
- **K-world** (voting) — on its own turn it searches K determinized worlds and takes the plurality first move
  (reduces K=1 strategy fusion). Reviewed: no correctness break; `kworld-test` 6/6 incl. "aggregation bites".
  `K=1` reproduces the single-world path. Whether it raises win rate is a measurement question (K=3 ≈ 2.5× cost).

## How to run / verify

| command | what it checks |
|---|---|
| `dotnet run -c Release -- observed-seam-test` | the enforced agent boundary (7 checks) |
| `dotnet run -c Release -- boundary-test` | deterministic per-decision fixtures (secrecy/entitlement/action-fidelity) |
| `dotnet run -c Release -- determinizer-test` | the K=1 sampler: secrecy, legality, runnable, determinism, known-top placement (6) |
| `dotnet run -c Release -- ledger-test` | the ledger: gain/draw/multi-draw/shuffle/coincidence/owner-scope (9) |
| `dotnet run -c Release -- honest-play [n]` | honest bot vs held-out IntermediateBot, Wilson 95% CI |
| `dotnet run -c Release -- contamination [n]` | PAIRED perfect-info-vs-baseline & honest-vs-baseline ⇒ contamination delta |

Current test state: observed-seam-test 7/7, determinizer-test 6/6, ledger-test 9/9, clonetest 19/0.

## Numbers so far (NOT trustworthy — modest budget=600, small n, deck-matchup variance dominates)

honest K=1 vs the held-out IntermediateBot, across runs (all budget 600, random meta decks):
- n=20 → 9/20 = 45%; n=40 → 16/38 = 42%; n=12 → 4/12 = 33% (K=3 identical: 4/12 — K-world inert here).
- **Pooled ≈ 29/70 = 41%** (95% CI ≈ [30%, 53%]). Directional read: the honest bot is CLEARLY playing (not
  random), COMPETITIVE with the perfect-info bot at equal small compute, but NOT strong at this budget.
- **Contamination (paired): n=14 ⇒ 7pp; n=30 ⇒ perfect 47% vs honest 30% = ~17pp.** Direction consistent
  both runs, magnitude grows with n. Not yet significant at n=30 (delta CI ≈ [−7, +41]pp) but a REAL
  directional signal: perfect information inflated the bot ~10–17pp ⇒ the project premise holds (the old
  ~61% was contaminated). Budget is weak (600); the original hypothesis is that perfect info helps MORE at
  deeper search, so true contamination may be larger at strength. Nail it down with paired n≥300.

A trustworthy verdict needs the PAIRED n≥300 experiment (deck + turn-order swaps) at a REAL budget, which is
slow (honest games determinize + search per command). The numbers above bound honesty/correctness, not
strength — strength is a separate measurement-and-tuning effort.

## Known gaps / next increments (each gated on the review harness — see `review-harness.md`)

1. **K-world** — the bot is K=1 (one sampled world ⇒ strategy fusion: it optimizes confidently against one
   false world). Aggregating root-action values across K worlds is the next strength lever.
2. **Honest-by-type — DEFERRED (considered decision).** `HonestPlannerBot` is honest by CONSTRUCTION
   (multiple fresh-context reviews confirmed it never searches a true hidden card: the determinizer discards
   the referee's hidden assignment, and every move it commits references only preserved own/public ids). It
   is still an `ILegacyAgent` that RECEIVES the referee state, so it is not honest by TYPE. Folding it under
   the strict `IObservedAgent` seam is NOT a small change: a full-turn search needs a concrete `GameState` to
   simulate, so honest-by-type requires moving the SEARCH runner-side and reducing the "agent" to a weight
   vector — a real architecture shift. Judgment: this is FUTURE HARDENING (it prevents a future mistake, not a
   current leak), so it is deferred rather than rushed into the loop. Do it when the boundary needs to be
   enforced against untrusted agent code; skip it while the honest planner is the only consumer and is
   already verified honest. (The ENFORCED seam already exists and is tested for observed agents generally —
   `observed-seam-test`; this gap is only that `HonestPlannerBot` doesn't yet route through it.)
3. **Ledger breadth** — only own-deck-top today. Opponent-hand count facts (from revealed cards), known
   deck positions, grouped ambiguity constraints are later slices (knowledge-state-design.md §2/§4).
4. **Trustworthy measurement** — paired n≥300, honest-vs-perfect-info for the contamination delta.

## Next increment: K-world sampling (implementation plan)

Goal: reduce K=1 strategy fusion — instead of committing to the best move on ONE sampled world, evaluate the
SAME root actions across K worlds and pick the move with the best aggregate value.

Scope (own-turn MAIN decision only, first; reactive stays K=1 for now):
1. Enumerate the legal first-moves `M` from the REAL state via `LegalActions.Validate(LegalActions.Candidates(...))`
   — these reference preserved-id cards, so they apply to any determinized world and the real state.
2. Determinize `K` worlds: `RootWorldSampler.Determinize(state, knowledge, DerivedSeed.For(seed,seat,turn,decision,w))`
   for `w in 0..K-1` (world index is the ONLY varying coordinate ⇒ distinct legal worlds, all honest).
3. For each `m in M`: `V(m) = mean over worlds w of` [clone `w`, apply `m`, greedy roll-out the rest of my
   turn + one opponent reply via `ValuePolicy`, then `ValueFunction.Score`]. Greedy roll-out (not full beam)
   keeps cost at `O(K·|M|·rollout)`.
4. Return `argmax_m V(m)` (tie-break deterministically by the enumerated order).

Config: `K` and a per-eval budget as `HonestPlannerBot` options; `K=1` must reproduce today's behaviour path
(guard it). Keep the receding-horizon replan (call this each command).

Falsification-first tests (`kworld-test` or extend `determinizer-test`):
- DETERMINISM: same (state, seeds) ⇒ identical chosen move; different world-seed base ⇒ the K worlds differ.
- LEGALITY: the chosen move is legal on the REAL state (Validate).
- AGGREGATION BITES: construct/reach a decision where world-0's best move differs from the K-aggregate best,
  and assert K-world does NOT just echo world-0 (else K is inert — the exact "feature does nothing" trap).
- SECRECY: each of the K worlds carries no true hidden identity (reuse the determinizer poison scan) — K-world
  must not reintroduce a leak by, e.g., reading across worlds.
- COST BOUND: assert clone/eval count ≤ K·|M|·cap (no runaway).

Then: fresh-context reviewer (harness) — falsify that aggregation is correct (not secretly K=1), that
roll-outs never commit a resampled-deck action back to the real state, and that the per-world seeds are stable.

## Development loop

This bot is being built via a solo review loop (`review-harness.md`): each increment is built with
falsification-first tests, then a fresh-context reviewer subagent red-teams it, findings are fixed, and it
is re-verified before "done". That loop has already caught (and fixed) a real apply-back desync, an honesty
leak in the ledger, and several false-confidence tests — the kind of thing self-review alone misses.
