# OPTCG Heuristic-Discovery Simulation Platform

Runs **millions of seeded, reproducible self-play games** on the real game engine to generate the
telemetry an advanced bot's heuristics are discovered and validated from. Implements the foundation
of the *Simulation & Heuristic Discovery Blueprint* (Milestone 6 core: league runner, paired
experiments, telemetry, anomaly capture).

## This never ships to users

There are **two independent guarantees** that nothing here reaches the Velopack `.exe`:

1. **Unity never compiles it.** `Tools/` is outside `Assets/`, so it's not part of any player build
   — same as `Tools/Harness/`.
2. **Velopack never packs it.** `Deploy/publish_release.ps1` packs `--packDir
   C:\Users\Nperr\Builds\optcg-windows` (the Unity Standalone output only). This repo folder is
   never copied there.

Like the Harness, this project **compiles the real, pure-C# shipping engine straight from
`Assets/Scripts/Engine/`** (`GameEngine.cs`, `GameState.cs`, `CardData.cs`, `Bot/IntermediateBot.cs`)
so simulations always reflect exactly what players run. Only *engine* fixes this work drives out get
shipped — never this tooling.

## Run

```
cd Tools/Sim
dotnet run -c Release -- smoke                        # quick self-check: 3 decks, both orders, decisions on
dotnet run -c Release -- deckcheck                    # validate imported meta decks (Decks/imported/*.deck)
dotnet run -c Release -- run configs/starter-league.json          # full 33-deck self-play league (~1.09M games)
dotnet run -c Release -- heuristics configs/overnight-turnorder.json   # discover turn-order heuristics
dotnet run -c Release -- heuristics configs/overnight-mulligan.json    # discover mulligan heuristics
dotnet run -c Release -- heuristics-quick mulligan 60 # quick preset over the starter field
```

### Feeding it a new deck (e.g. OP16 meta)

1. Paste each list as a file in **`Decks/imported/<name>.deck`** (format in that folder's README —
   tolerant of onepiecetopdecks / sim exports; mixed sets are fine, the whole card pool is covered).
2. `dotnet run -c Release -- deckcheck` to validate.
3. Reference the deck ids in a config's `decks`/`opponents` (or leave them `[]` to cross everything,
   imported decks included), then `run` (self-play) or `heuristics` (discovery). Nothing else changes.

Output lands in `Results/<experimentId>/`:

- `games.part###.jsonl` — one compact row per game (matchup, seed, forced first player, winner,
  end reason, turns, commands). Sharded per worker for lock-free writes; `cat` them together to
  analyze. Every game is fully reproducible from its `seed`.
- `decisions.part###.jsonl` — per-decision log (only when `saveDecisionLogs`, typically sampled).
  Public-state features only — never opponent hidden hand/deck contents (blueprint §13).
- `summary.json` — merged aggregates: end-reason mix, avg turns, **per-deck win rate with Wilson
  95% CIs**, and a **first-vs-second table** (the empirical input to the coin-flip estimator, §6).

## Architecture (maps to the blueprint)

```
ExperimentConfig (§15.1)
   → SelfPlayRunner        parallel, deterministic, sharded output, Wilson-CI summary   (§11, §6.3)
        → MatchDriver      forces starting order, drives one game, emits decision telemetry (§12.1)
             → IAgent      the policy seam — baseline today, advanced bot tomorrow      (§2, §10.2)
                  → GameEngine.ApplyCommand   engine is the sole authority on legality  (§1.2)

HeuristicConfig → HeuristicRunner   paired counterfactual: choice A vs B on MATCHED seeds  (§10.5)
   → DecisionFamily (mulligan | turn-order)   the A/B decision + which features to condition on
   → Distiller   online paired-difference stats → §12.3 conditional heuristics with a promotion gate

DeckLoader → DeckRegistry   paste any decklist → validated DeckDef, crossed against the whole field
```

### How heuristics stay deck-agnostic (no hardcoded leader rules)

Every discovered rule is conditioned on **generic features** — hand shape (`hand_curve_ok`,
`hand_has_searcher`, `hand_flooded`, …) and *deck-vs-deck* comparison (`i_am_faster`,
`my_low_curve`, `opp_leader_life`, …) — never a leader name. Because choice A and choice B run on
the **same seed**, the measured `P(win|A) − P(win|B)` is the causal effect of the decision in that
matched world (§10.5), not a correlation. The advanced bot then applies a rule by computing the
same features from *its* deck vs *its* opponent — so a brand-new deck is handled by comparison, with
no per-leader script. Add a decision family (counter, attack-sizing, target choice) by implementing
one `DecisionFamily`; add a playstyle by adding an `IAgent` to the opponent pool.

- **`IAgent`** (`Agents/`) is the single drop-in point for an advanced bot (shallow search, tuned
  weights, learned policy/value). It only *ranks/chooses* among what the engine already permits.
  `BaselineAgent` delegates to the shipping `IntermediateBot`.
- **`MatchDriver`** (`Runner/`) reuses `IntermediateBot`'s public `SnapshotFor`/`Succeeded`/
  `Signature` no-op protocol verbatim, so routing decisions through an arbitrary agent stays
  faithful to how the game actually resolves. Starting player is *forced* per condition so paired
  experiments measure P(win | first) vs P(win | second) over a matched seed family.
- **Telemetry** (`Telemetry/`) — flat `GameRecord`/`DecisionRecord` rows + a Wilson-CI `WinTally`.

## Built

- **Self-play generation + telemetry** (§6, §11, §12.1): parallel deterministic league, sharded
  JSONL, Wilson-CI summary, first-vs-second table.
- **Constructed-deck ingestion** (§10.3): paste any decklist → validated `DeckDef` → registry.
- **Counterfactual heuristic discovery** (§10.5, §12): matched-seed A/B over the whole field, online
  paired-difference distiller, §12.3 conditional-heuristic reports with a promotion gate (§11.3).
  Families: **mulligan** (keep vs mulligan) and **turn-order** (first vs second).

## Not built yet (next blueprint milestones)

- **Mid-game decision families** (counter, attack-sizing, target/search choice) — the high-value
  tactical heuristics. Each needs to enumerate that decision's alternatives from state; localized
  per-family, but more than the start-of-game families above. Plug in as new `DecisionFamily`s.
- **Full-state `Clone()` + general legal-action enumerator** — for belief-state search and the
  lethal/survival/DON solvers (§9). The *offline* counterfactual mining here does not need them
  (matched seeds + a decision-controlling agent substitute for mid-game cloning).
- **Playstyle population** (§10.2/§10.4): aggro / control / fortress / deck-out `IAgent`s in the
  opponent pool, so heuristics are validated across styles, not just the baseline.
- **Deck fingerprinting / combo graph** (§5), **opponent belief state** (§8), **Strategic Director**
  (§4.4) — the layers the advanced `IAgent` consumes at runtime.
