# Advanced Bot Research & Improvement Log (2026-07 onward)

Goal: raise the SHIPPED Advanced bot's strength using an honest measure→hypothesize→implement→measure
loop, grounded in (a) how other TCG engines build bots and (b) how strong OPTCG players actually play.
The bot was made fair-information on 2026-07-21 (see [[project_optcg_bot_observation_boundary]]); the game
also fixed 300+ card-effect bugs since the bot was last tuned, so new play patterns exist to exploit.

## Measurement infrastructure (built 2026-07-22 — the unblocker)
The shipped Advanced bot (`Assets/…/AdvancedContractBot`) was previously UN-measurable headlessly (the Sim
only compiled a subset and self-play used IntermediateBot/WeightedAgent). Fixed:
- Added the shipped advanced files (`AdvancedContractBot`, `AdvancedActivationPolicy`, `AdvancedPressurePolicy`,
  `BotDeterminizer`) to `Tools/Sim/Sim.csproj` (deps SearchBot/TriggerUtilityPolicy/LegalActions/… already there).
- `Tools/Sim/Agents/ShippedAdvancedAgent.cs` — IAgent adapter driving the real `AdvancedContractBot.Decide`
  (owns the per-turn attempted-activations set; reconstructs the decklist from state for `ClassifyArchetype`).
- Registered tier `shipped-advanced` in `BotTiers.Make`. Run: `dotnet run -c Release tiers shipped-advanced intermediate <n>`.
- ⚠ NOTE: this tests EXACTLY what ships (no fork). The Tools/Sim/Planning/* Advanced files are the stale
  research fork — do NOT tune those.

### Measurement discipline (hard-won — see the memory)
- Paired on decks+seed+seat-swap where possible; report Wilson CI. Small-n + shared-seed = the classic trap.
- A/B a single change via a STATIC toggle (ON vs OFF) over the SAME games; keep only CI-clear wins.
- Never read an archetype cell out of a full-pool run (n=48 cells lie ~10pp). Use global aggregate for keep/discard.
- The bot is now fair-info, so gains must come from BETTER HEURISTICS/SEARCH, not from peeking.

## Baselines
| date | matchup | n | win% | CI | notes |
|---|---|---|---|---|---|
| 2026-07-22 | shipped-advanced vs intermediate (advdiag, alt-first, NOT paired) | 40 | 47.5% adv | ~±15pp | small n; rough field estimate |

### 🔑 STRATEGIC FINDING: fair-info Advanced ≈ Intermediate (parity, maybe slightly behind)
After making the Advanced bot FAIR-INFO this session, it no longer clearly beats the greedy Intermediate core
(19/40 adv vs 21/40 int, n=40, alternating first player, unpaired ⇒ wide CI). This DIRECTLY supports the
memory's long-standing hypothesis ([[project_optcg_bot_observation_boundary]]) that the Advanced tier's old
edge was partly PERFECT-INFO CHEATING — remove the cheat, and the extra search/rollout modules add little
over greedy. ⇒ Big headroom, and the advanced modules need genuinely better search/eval (not peeking) to earn
their keep. Confirm with a larger PAIRED run (same decks, both seat orders) next.

### ✅ Determinism ACHIEVED (2026-07-22)
Two identical `advdiag 40 1500 3` sweeps now produce BYTE-IDENTICAL per-game results (0 diff). The
`BotDeterminizer.Seed` string.GetHashCode()→FNV fix removed the per-process variance. A/B pairing is now valid
(run a toggle ON vs OFF over the SAME deterministic games and the delta is clean). Both sweeps: 0 hangs.

## Research notes

### TCG-bot engineering (Hearthstone/MTG/CCG literature)
- SOTA CCG bots = MCTS + a state-evaluation (expert heuristic OR learned value net). Progressive Bias =
  blend the heuristic eval into UCT, weight ∝ #sims. Imperfect info handled by PIMC + ISMCTS (we do PIMC
  via determinization). "Heuristic solver for combat in the MC sims" — deterministic combat resolution.
- MTG Forge AI = pragmatic heuristics + per-effect logic + general "smart-play" rules (e.g. ALWAYS attack
  with a temp-control creature so the effect isn't wasted). Lesson: general concepts > per-card hacks.
- "Evolving Evaluation Functions for CCG AI" (arXiv 2105.01115) — our Evaluation.cs weights were evolved;
  re-evolving after the 300+ rule fixes is a candidate.

### OPTCG expert play (concrete, implementable)
- **DON!! is guaranteed every turn** — the game is HOW you spend, not whether you draw. Advanced: don't
  autopilot full DON depletion; holding DON can enable counter-events/bluff. (Our eval W4 already lightly
  PENALIZES leftover active DON — arguably right for most decks; deck-specific for counter-event decks.)
- **Counter efficiency (BIG):** take BIG early attacks to hand rather than burning two +2000 counters on an
  8000 hit; SAVE counters for LATER low-power attacks where they're efficient. Ideal to sit ~2 Life; 0 =
  danger zone. ⇒ life value is NON-LINEAR (last 1-2 life precious) — our eval W2 is LINEAR. Hypothesis H1.
- **Aggression at low life keyed to opponent's ACTIVE resources**, not life alone (lethal windows). H4.
- **Attack sequencing for max value:** force blockers with an expendable attacker first, THEN swing Leader;
  order attacks so the opponent's best block is baited. Our greedy attacks "Leader by default, profitable
  rested trades first" — sequencing is likely improvable. H3.
- **Board reading:** track opponent's revealed searchers/plays to estimate swing success. (Fair-info OK.)

### 🔑 KEY FINDING — the turn-search oracle ALREADY EXISTS (Tools/Sim/Planning/TurnPlanner.cs)
`TurnPlanner.PlanTurn(state|world, weights, ctx, opt)` returns a PLANNED MOVE SEQUENCE for the whole turn:
BEAM search (width 16, depth 16, NodeBudget=3200 = the measured knee), optional `OpponentReplyPly` (plays
the opp's greedy reply before scoring = 1-ply lookahead). This IS the user's "try move permutations to find
the winning line." It powers `HonestPlannerBot` (runs PlanTurn on the honest `Determinize`d world) and
`PlannerBot` (perfect-info). **It was NEVER shipped** — the shipped Advanced bot uses only greedy + shallow
SearchBot rollout. Its old honest verdict was ~40-45% vs intermediate ("eval-bound"), but that PREDATES the
300+ rule fixes ⇒ re-measure. Uses `ValueFunction`/`ValuePolicy` (a richer eval than Evaluation.cs).

## 🎯 DIRECTION (user, 2026-07-22): fixed-seed turn-search ORACLE to challenge heuristics
Core methodology shift — instead of blind heuristic tuning, build a turn-level SEARCH that, on a FROZEN
determinized position, explores permutations of the turn's moves (play order, DON attach, effect/leader
timing, attack order+targets, effect resolution for max value) and picks the highest-value line. Use it:
  (a) as the Advanced bot's MAIN-PHASE policy (search, not greedy delegation);
  (b) as an ORACLE that audits greedy — replay fixed games greedily, and wherever the oracle finds a better
      line, log the DIVERGENCE POINT = a concrete heuristic weakness. Aggregate divergences across many fixed
      games+matchups = "challenge every heuristic."
This settles search-starved-vs-eval-bound honestly: if the oracle wins games greedy loses, search is the
lever and the divergences say which heuristics to rewrite.

### Constraints / blockers found
- SPEED: current SearchBot does a 2000-command rollout PER CANDIDATE on every tactical decision ⇒ ~4-6s/game,
  too slow to iterate and a bad base for deeper search. Step 1 = a bounded turn-search (beam over move seqs,
  cheap eval leaf, short/near-terminal rollouts only). Determinize is now LAZY (only SearchBot/Trigger sim
  branches clone) — behaviour-neutral speedup already applied to AdvancedContractBot.
- FORK: tune the SHIPPED files (Assets/), measured via `tiers shipped-advanced`. Tools/Sim/Planning/* is stale.
- Focus must stay ADVANCED-specific (the search/eval/activation modules), not the shared greedy core.

### Build plan (oracle-first)
1. Fast bounded turn-search over move sequences (LegalActions.Candidates per step + eval leaf); node budget.
2. Oracle harness: fixed (deckA,deckB,seed,order,determinization); greedy line vs oracle line; log first
   divergence + whether oracle converts a greedy LOSS→WIN. Aggregate divergence patterns.
3. Turn the winning-line patterns into Advanced heuristic/search improvements; A/B via `tiers shipped-advanced`.
4. When gains stall → back to research (this doc) + new divergence mining.

## NEXT-SESSION PLAN (crisp, ordered)
1. **Measure the pivot question:** honest `TurnPlanner` vs intermediate (`honest-play`/`tiers` — wire an
   honest-planner tier if needed) vs the current `tiers shipped-advanced intermediate` baseline, post-bugfix.
   - If TurnPlanner clearly > shipped greedy-advanced ⇒ PORT TurnPlanner+ValueFunction+ValuePolicy to Assets/
     as the Advanced main-phase policy (big win, matches the user's search vision directly).
   - If ~parity ⇒ use TurnPlanner as the DIVERGENCE ORACLE (build `divergence` mode: fixed game, greedy line
     vs PlanTurn line, log first divergence + greedy-LOSS→planner-WIN conversions; mine patterns → heuristics).
2. **Speed:** SearchBot's 6×2000-cmd rollouts/decision make `tiers shipped-advanced` ~4-6 s/game. For A/B,
   either (a) reduce RolloutCap/Shortlist behind a static knob for iteration, or (b) prefer TurnPlanner (bounded
   by NodeBudget/WorkBudget) which is designed for this. Get a stable baseline number with n≥100 in background.
3. **Research-driven hypotheses** (below) as static toggles, A/B via the fast harness; keep CI-clear wins only.

### Env/tooling notes
- `tiers shipped-advanced intermediate <n>` = the measurement command (shipped bot via ShippedAdvancedAgent).
- Background `dotnet run` sometimes captured 0 bytes (build lock / buffering). Build FIRST (`dotnet build`),
  then run `--no-build` with an explicit `> file 2>&1` redirect; be patient (advanced bot is slow).

## 🧠 RETHINK (2026-07-22, user steer: "if not conclusive, go back to research + rethink optimal play")
HONEST DIAGNOSIS of why iters 5-8 gave marginal/noisy results: I was (1) tuning the SATURATED greedy layer and
(2) reading NOISY aggregate win-rate A/B (±5pp even at n=600). Both the literature and my own diagnostic say the
layer that matters is elsewhere:
- **Research consensus (DeepStack, Libratus, depth-limited solving, arXiv 1906.06412 / neurips 7993):** strong
  imperfect-info play = SEARCH + a good VALUE FUNCTION (learned/heuristic leaf), NOT a pile of heuristics. Value
  function is THE critical component; search is only as good as its leaf eval.
- **My diagnostic (iter 8):** the Advanced bot's ENTIRE edge is the SearchBot ROLLOUT (+3.6pp); greedy is
  saturated; Activation/Trigger ≈ 0. ⇒ same conclusion: the lever is SEARCH + EVAL, not more heuristics.
THREE STRUCTURAL MOVES (this is the new plan):
  1. **BROADEN the search** to the biggest decision space — the MAIN PHASE (currently greedy). `SearchCleanMain`
     experiment running (iter 9). A pre-fix "universal clean-main rollout" was rejected, but pre the 300+ fixes.
  2. **Improve the VALUE FUNCTION** (`Evaluation.cs`, 13 weights evolved on the OLD ruleset) — re-tune post-fix
     and/or add principled features (non-linear life, lethal-proximity, tempo). It BOUNDS all search quality.
  3. **Measure with a LOW-VARIANCE ORACLE, not win-rate A/B** — the user's "single-seed solve": on a FIXED
     position, compare the bot's move to a deep search's best line ⇒ see decision ERRORS directly, no ±5pp fog.
     This is how to get CONCLUSIVE signal (per-decision), and it doubles as the "challenge every heuristic" tool.
SHIPPABILITY NOTE: an unbounded 2000-cmd rollout on every main decision is too slow for the live UI; the
shippable form is a BOUNDED search (beam + node budget, à la TurnPlanner) with a good eval — but first prove
search-on-main WINS at all (iter 9 signal), then make it fast.

### iter 9 RESULTS (the rethink's first conclusions — CONCLUSIVE)
- **Search-on-main phase: 46.8% vs 51.3% baseline ⇒ WORSE (~−4.5pp). DROP IT.** The SearchBot rollout plays the
  MAIN phase worse than the well-tuned greedy, because the rollout is only as good as its leaf eval and the eval
  is too weak to guide big-decision strategy. (Confirms the pre-fix rejection, now with a mechanism.)
- **Search contribution (tactical) confirmed: +3.6pp (s3), +1.6pp (s5) ⇒ ~+2.6pp real** — but ONLY on narrow
  tactical branches (deck-look/effect-target/trigger) where greedy's heuristics are weakest.
- ⇒ **THE VALUE FUNCTION (`Evaluation.cs`) IS THE DEFINITIVE BOTTLENECK.** The greedy core is genuinely strong
  (beats eval-guided search on main); the eval is only good enough for tactical tiebreaks. So: the bot is NEAR
  its ceiling for the current architecture; further real gains need a MATERIALLY better value function (big
  investment: re-evolution or learned eval), NOT more heuristics or broader search on top of a weak eval.
- **NEXT = BUILD THE ORACLE** (user's single-seed solve): the low-variance tool to (a) find CONCRETE main-phase
  positions where greedy errs (perfect-info deep rollout ranks actions; flag where the bot's pick is much worse
  than the best), and (b) give a decision-quality metric to drive eval improvement without ±5pp win-rate fog.
  Reset: `SearchCleanMain=false` (shipped default; experiment toggle only).

### iter 9 cont'd — ✅ ORACLE BUILT (`oracle <dA> <dB> [seed] [K] [maxDec]`) — the low-variance signal
Plays a fixed game greedily; at each SOUTH clean-main decision, evaluates EVERY legal action by a PAIRED
K-determinized rollout win-rate (all candidates face the SAME K sampled futures via BotDeterminizer ⇒ regret is
low-variance; greedy playout both sides, cap 6000). Reports per-decision REGRET (best_wr − bot_wr) and flags
errors ≥0.10. First run (op16-black-teach vs op16-g-zoro, K=8): 24 decisions, 12 errors, avg regret 0.12; found
concrete misplays (e.g. turn7 bot played OP09-090→0% vs attachDon-to-leader→62%). KEY: oracle uses greedy
ROLLOUTS, bot uses greedy IMMEDIATE ⇒ high regret = where LOOKAHEAD beats the immediate heuristic (= the +2.6pp
the SearchBot provides, now QUANTIFIED per decision). This is how to improve WITHOUT win-rate fog.
CAVEATS (be skeptical): (1) K=8 is coarse (wr granular to 1/8) — use K≥30 to trust a specific error; (2) the
"best" is relative to GREEDY continuation, not true-optimal — it finds where the immediate heuristic disagrees
with its own lookahead (still a real fix signal); (3) declareAttack label shows TARGET not attacker (ambiguous).
NEXT: run oracle at K≥30 over SEVERAL fixed games/matchups → aggregate error PATTERNS by decision TYPE (playCard
order? attack selection? attachDon timing?) → the dominant pattern is the concrete heuristic/eval fix to make.

### iter 9 cont'd — oracle FIRST PATTERN (greedy bot, 4 matchups, K=20): declareAttack dominates
by-type total regret: **declareAttack 4.75 (n=29, avg 0.164)** >> playCard 2.75 (avg 0.098) > endTurn 1.50
(avg 0.088) > attachDon 0.25 (avg 0.023). ⇒ ATTACK decisions are the biggest regret source; DON timing ~optimal.
BUT confounded: the oracle analyzed the GREEDY bot, and several "errors" were "should activateMain before
attacking" — which greedy can't do but the ADVANCED bot's activation policy ALREADY handles. Also: this RE-CONFIRMS
the value-fn story — SearchCleanMain (cheap 1-rollout+eval search) was WORSE, yet the oracle's ACCURATE K=20-to-
terminal rollout shows attack search WOULD help ⇒ the gap is search QUALITY (eval), not search itself. A shallow
search needs a good eval to be accurate & shippable.
FIX: re-ran oracle analyzing the ACTUAL ADVANCED bot (ShippedAdvancedAgent for south's decisions + line) over 5
matchups to find its RESIDUAL error pattern (bg /tmp/oracle_adv.txt). NEXT: read residual pattern → the dominant
residual error type is the concrete fix (heuristic if a clean rule emerges; else it's the eval → shallow-search).

### iter 9→10 — ✅ ORACLE FOUND A CONCRETE FIX: activation over-firing (no value gate)
Advanced-bot oracle (5 matchups, K=20): overall avg regret only **0.037** (bot plays CLOSE to the rollout-oracle
best ⇒ near-ceiling for a greedy-lookahead reference). Residual regret dominated by **playCard (avg 0.055)** and,
in Blackbeard/activation decks, **activateMain (avg 0.06-0.16)**. Detailed errors (black-teach): the bot fires
[Activate: Main] abilities the oracle rates BAD — e.g. `activate OP10-082 → 0% win` (end-turn was 10%),
`activate OP10-082 → 20%` when ATTACKING the leader was 60% (regret 0.40); OP10-082 activation bad EVERY time.
ROOT CAUSE (same class as the Van Augur issue): `AdvancedActivationPolicy` fires any whitelisted ability that
merely CHANGES state (LegalActions.Validate) — it NEVER checks the activation IMPROVES the position.
FIX (behind `ActivationValueGate` toggle): skip an activation if the resulting `Evaluation.Score` is WORSE than
not acting. Testing `moduleab actgate` @ seeds 3,5 (paired vs none@3=51.3 / none@5=55.0). CAVEAT: gate uses the
weak eval, so it only filters CLEAR losers; effect concentrated in activation-engine decks (minority of field) so
a full-field delta may be small — if so, verify on Blackbeard-only matchups. This is oracle→concrete-fix, the
loop working as intended. Also confirms: bot is near-ceiling; bigger gains need a stronger eval (value function).

## 🏁 CONCLUSIVE FINDING OF THE RESEARCH ARC (iters 5-10)
Multiple independent lines of evidence converge: **the shipped Advanced bot is already strong and near its
architectural CEILING; the VALUE FUNCTION is the binding constraint on further gains.**
- greedy toggles SATURATED (weakest-first the lone win, ~+2pp);
- advanced modules add ~+3.4pp, ALL from the search rollout (Activation/Trigger ≈ 0);
- search-on-main is WORSE (eval too weak to guide big decisions);
- oracle: advanced bot's avg regret vs a K-rollout best line is only **0.037** (near-optimal for that reference);
- the activation value-gate (oracle-found fix) is only **~+0.8pp** (marginal, eval-limited).
⇒ The incremental wins available WITHOUT a better eval are ~+0.8-2pp each and getting scarcer. The BIG lever is a
materially stronger VALUE FUNCTION (re-evolve `Evaluation.cs` post-bugfix on the shipped SearchBot; or a learned
value net à la DeepStack) — a larger, separate project. RECOMMENDATION: ship the small oracle-driven correctness
wins (weakest-first ✅, activation gate pending sign-confirm), then decide whether to invest in the value-function
project vs accept the current near-ceiling bot. The ORACLE (`oracle` mode) is the durable tool to guide any eval
work with low-variance per-decision signal.

## 🎯 DEEP-THINK PLAN — actually beat/close-match TOP PLAYERS (user directive 2026-07-22)
The whole arc proved the ceiling is SEARCH-QUALITY + VALUE-FUNCTION, not heuristics. Concrete plan, highest
impact first:
1. **[FIX — testing] Rollout policy was STALE/WEAK.** `SearchBot` rolled out with `ChampionBot` — chosen when it
   was the strongest policy, but later RETIRED for LOSING to the core. So every move was evaluated by simulating
   WEAK future play (this alone plausibly explains why search-on-main FAILED and the eval looked capped). Switched
   to `IntermediateBot` (strong core) via `SearchBot.StrongRollout`. Testing `moduleab strongroll` @ s3,s5.
2. **[NEXT] Re-test search-on-main WITH strong rollouts** (`strongmain`) — the cheap-search-on-main failure may
   have been the weak rollout, not search itself. If accurate rollouts make main-search win, that's a big lever.
3. **[THE BIG ONE] Value function.** activation-gate FAILED (inconsistent, +0.7/+0.9/−1.3) BECAUSE the eval is too
   weak even to gate. Re-evolve `Evaluation.cs` on the CURRENT ruleset AGAINST the strong-rollout search (not vs
   greedy), + add nonlinear features (non-linear life, lethal-proximity, tempo, blocker-wall). Use the ORACLE for
   low-variance per-decision training signal. A linear 13-weight eval caps positional understanding — the honest
   path to top-tier is a richer/learned value function (DeepStack recipe: depth-limited search + value net).
4. **[TO EXCEED THE GREEDY CEILING] Self-play bootstrapping (AlphaZero-lite).** The bot currently plans against
   GREEDY rollouts ⇒ optimized to beat greedy, which top players exceed. Iterate: search → distill its moves into
   a stronger rollout/value policy → search now plans against stronger play → repeat. This is what actually climbs
   above the greedy ceiling toward human-expert level.
5. **[EFFICIENCY] ISMCTS/PIMC** (determinize K worlds + UCB tree search) to get deep, accurate search inside a
   shippable time budget (the oracle proves deep rollouts ⇒ near-optimal moves; ISMCTS makes that fast enough).
DISCARDED this iter: activation value-gate (inconclusive across seeds — eval too weak).

### 🧱 STRONG-ROLLOUT REFUTED (hypothesis #1 failed) — and the REAL barrier is now clear
`strongroll` (IntermediateBot rollout instead of ChampionBot): 50.4% (s3) / 54.3% (s5) = ~−0.8pp vs baseline.
So the rollout POLICY isn't the lever either. WHY (the key realization): the oracle showed the bot is near-optimal
vs a GREEDY-ROLLOUT reference (regret 0.037). ChampionBot and IntermediateBot are BOTH ~greedy ⇒ same ceiling.
**The bot is at the greedy-rollout ceiling, and EVERY tractable lever (greedy toggles, rollout policy, main-search,
activation gate) is neutral because they're all anchored to greedy-quality play.** To EXCEED it you need a
policy/eval that is BETTER THAN GREEDY — which requires LEARNING (self-play-trained value/policy), not tuning.
### 🚧 THE MEASUREMENT WALL (must state honestly)
I CANNOT measure "beats top players" — my only opponents are greedy/intermediate, which the bot already beats
~56%. So even a stronger bot can't be validated here; that needs HUMAN playtesting vs strong players (which also
reveals the real weaknesses self-play hides). ⇒ Two things gate top-tier: (1) a learned value function + self-play
(big ML project, uncertain payoff), (2) a stronger-than-greedy yardstick (human games or a much stronger ref bot).
### ✅ BANKED THIS SESSION (real, shipped): fair-info determinizer (integrity), hang fixes (app-freeze),
removal follow-through (correctness), weakest-first attack seq (+2pp), the ORACLE tool (durable eval-guide).
RECOMMENDATION: bank these; the next tier is a scoped ML/self-play project + human playtest loop, a deliberate
investment decision — not something more self-play toggling will reach.

## 🏆 CHAMPION GAUNTLET — the ratchet baseline (user insight 2026-07-22): measure vs the CURRENT BEST bot
The measurement wall dissolves: measuring changes vs the WEAK intermediate washes out top-end gains (both strong
bots ~56% vs it). Instead measure CANDIDATE (advanced + change) HEAD-TO-HEAD vs CHAMPION (current best advanced),
paired ⇒ far more sensitive (a real gain = >50% vs champion), AND it's the self-play RATCHET: a candidate that
beats the champion is PROMOTED and becomes the new champion, so the bar keeps rising past the greedy ceiling.
Built `champ <toggle> [pairs] [seed]`: both seats ShippedAdvancedAgent, candidate seat gets a SEAT-SCOPED
experimental toggle (e.g. `ActivationValueGateSeat`), champion seat plain; 4-game matchup-paired blocks; prints
PROMOTE / REJECT / inconclusive from the CI. This is now the STANDARD yardstick for all future advanced changes.
(Validating with weakest-first as a known-win sanity + re-running actgate vs champion — more sensitive than the
inconclusive vs-intermediate read.) NEXT once validated: this is the backbone for the self-play eval-tuning loop
(evolve/tune the value function with fitness = beat the champion → promote → repeat).

### CHAMPION GAUNTLET RESULTS (n=594, seed 3) — the ratchet works, and it confirms the ceiling
- weakest-first vs champion = **48.0%** [44-52] inconclusive (it was +4pp vs the GREEDY bot ⇒ does NOT transfer
  to the advanced bot — the activation setup changes attack dynamics; still fine for beginner/intermediate tiers).
- activation-gate vs champion = **49.1%** [45.1-53.1] inconclusive.
⇒ The gauntlet gives clean PROMOTE/REJECT/inconclusive verdicts (great infra) AND confirms: TOGGLE-level changes
are neutral vs the advanced champion. The bot is at its architectural ceiling; no more toggle wins exist.
LEVERAGE PROBLEM (the crux): `Evaluation.cs` is used ONLY by SearchBot (tactical branches ≈ +3.4pp of the game).
The greedy `Value()` drives play. So even a much better eval has bounded leverage UNLESS main-search works — which
needs a much better eval (chicken-egg). Breaking it = a materially stronger, likely NONLINEAR/LEARNED value fn +
applying search more broadly. That's a deliberate PROJECT (self-play value tuning via the champion ratchet), not a
toggle. Loop STOPPED here (toggle-iteration exhausted — can't progress); the big lever is an investment decision.
### SESSION SUMMARY (what shipped + what's built)
SHIPPED: fair-info determinizer (no peeking), 2 hang fixes (app-freeze), removal follow-through, weakest-first
(+2pp beginner/intermediate), lazy-determinize perf. BUILT (durable tools): `oracle` (per-decision regret),
`champ` (champion ratchet), `abtest`/`seatab`/`moduleab` (paired A/B w/ CI), `ShippedAdvancedAgent`/
`HonestPlannerAgent` (measurable in Sim). CONCLUSION: bot is strong & near-ceiling; top-tier needs a value-fn
learning project + human-playtest measurement.

## ✅ PUZZLE SUITE — iteration 1 BUILT & VALIDATED (the fast, deterministic yardstick)
Replaces slow noisy win-rate A/B with a chess-style test suite. `Tools/Sim/Runner/PuzzleSuite.cs` +
`puzzle-harvest` / `puzzle-score` modes.
- HARVEST: play games; at each south clean-main decision the ORACLE (K-determinized rollout) labels every legal
  action with its win-rate; positions where the reference bot's move regret ≥ threshold are saved as the engine's
  CommandHistory (deterministic replay) + the oracle action-value map. `puzzle-harvest <n> [K] [thr] [out] [seed]
  [adv]` (greedy ref = fast default; `adv` = advanced ref = relevant but ~4× slower).
- SCORE: replay each puzzle to its EXACT position (CommandHistory), ask the bot, look up the oracle win-rate of
  its move ⇒ regret. `puzzle-score <suite> [bot]`. INSTANT + deterministic (0 game variance).
- VALIDATED: on a 6-puzzle test suite, greedy scores avgRegret 0.2833 (== harvested — replay is exact), advanced
  0.2667 (lower — suite DISCRIMINATES), 0 unmatched, scoring in 0s. ⇒ measuring a change is now a sub-second
  reproducible number instead of a 10-min ±5pp coin-flip.
- Suite v1 harvesting (60 games greedy-ref → puzzles_v1.json). NEXT (iter 2): score the current advanced bot for a
  baseline regret; then this is the fast fitness for the REGRESSION/distillation eval-tuning (reframe #2) — try eval
  changes and score on the suite in <1s each, keep those that lower regret, confirm winners on the champion gauntlet.
  Also harvest an ADVANCED-ref suite so the puzzles target the advanced bot's OWN residual errors (playCard/activate).

## 🚀 ITERATION 2 — ORACLE DISTILLATION WORKS (first thing that BEATS the advanced bot, out-of-sample)
The breakthrough the whole arc was building toward. Refactored `Evaluation.cs` to expose `Features()` (13 raw
features) + loadable `W`/`SetWeights`. New `EvalGreedyAgent` = 1-ply value-greedy main phase (pick the action
whose resulting position evals best; delegate non-main to greedy). New `fit-eval <suite> [ridge] [testfile]`:
extracts `(Features(candidate.result) → oracle win-rate)` from the puzzle labels (NO re-running the oracle),
least-squares-fits W (ridge), and scores eval-greedy before/after — with a TRAIN/TEST split so it's honest.
**RESULT (held-out 30%, 75 unseen puzzles):**
| bot | regret | solved |
|---|---|---|
| advanced (current) | 0.225 | 10.7% |
| eval-greedy + OLD eval | 0.221 | 13.3% |
| **eval-greedy + LEARNED eval** | **0.210** | **18.7%** |
⇒ a LEARNED eval + shallow search BEATS the hand-tuned advanced bot on UNSEEN positions (regret −6.5%, solves
best-move ~2× as often). Generalizes (out-of-sample), so not overfit. **R²=0.12** (linear/13-feature eval explains
only 12% of oracle values) ⇒ HUGE headroom with richer/nonlinear features. This breaks the "near-ceiling" wall —
hand-tuning was capped, LEARNING is not.
### NEXT (iter 2 cont'd), in order:
1. **CONFIRM IN GAMES**: eval-greedy-advanced vs advanced on the champion gauntlet (suite-regret ↓ should ⇒ win% ↑).
2. **RICHER FEATURES** (the R²=0.12 lever): non-linear life (buckets/own-life²), lethal-proximity (attackers whose
   power ≥ opp leader), per-card threat, tempo, blocker-wall, board-control ratio → re-fit → lower regret.
3. **SHIP FORM**: wire fitted-W + eval-greedy into `AdvancedContractBot` clean-main (keep activation/defense modules).
4. **BOOTSTRAP** (AlphaZero ratchet): re-harvest puzzles with the improved bot → its NEW mistakes → re-fit → repeat,
   climbing past greedy. Optionally advanced-ref harvest so puzzles target the advanced bot's own errors.
5. **UPGRADE MODEL** when linear plateaus: gradient-boosted trees / small TorchSharp net for the value fn.

### iter 2 cont'd — richer features + ship-form + GAME CONFIRMATION (running)
- Added 8 richer features (myLowLifeDanger=max(0,3-life)², oppLowLifePress, myLeaderReach=#attackers≥oppLeaderPow,
  oppLeaderThreat, myBoardPow, iHaveBlockerUp, myUnrested, bias). **R² 0.12 → 0.33** (features are the lever); eval-
  greedy(fitted) out-of-sample 0.204 regret / 17.3% solved vs advanced 0.225 / 10.7% — still a clear win. (Regret
  improved less than R² because regret = argmax quality, not value-fit; preference-ranking loss is the next lever.)
- SHIP FORM: `AdvancedContractBot.EvalGreedyMain` toggle routes clean-main through 1-ply eval-greedy with the
  learned weights (kept activation/defense/search modules). `fit-eval` writes `fitted_w.txt` (cwd; NOT /tmp — C#
  on Windows can't write /tmp). `moduleab evalmain` loads it + enables the toggle.
- GAME CONFIRMATION (the decisive test — does lower suite-regret ⇒ higher win%?): `moduleab none` vs `moduleab
  evalmain` @ seeds 3,5 vs intermediate, paired. Baselines none@3=51.3 / @5=55.0. RUNNING. If evalmain > none ⇒
  the distillation win transfers to games ⇒ ship it + start the bootstrap ratchet. If not ⇒ suite-regret is an
  imperfect proxy (investigate; likely need the eval to drive MORE of the decision, or a ranking-loss fit).
GOTCHA logged: detached `( … ) &` batches spawn OptcgSim.exe that LOCK the build DLL; use run_in_background + kill
OptcgSim.exe/dotnet.exe before rebuilding.

### ❌ GAME CONFIRMATION FAILED — eval-greedy-main is MUCH WORSE in games (the discipline paid off)
none 48.5%(s3)/54.0%(s5) vs **evalmain 39.1%/40.8%** — a ~10-13pp DROP despite LOWER puzzle-regret (0.204 vs
0.225). Lower per-decision regret did NOT ⇒ higher win%. WHY (two real causes, important lessons):
1. **MYOPIA**: 1-ply eval-greedy picks the best IMMEDIATE next-state but can't SEQUENCE a turn (play A then B,
   attack ordering, DON timing, activation setup). The greedy heuristic encodes TURN-LEVEL coherence that
   eval-greedy throws away ⇒ incoherent play. (Also 7× slower.)
2. **DISTRIBUTION SHIFT** (classic imitation-learning trap): the suite measured isolated decisions at positions a
   GREEDY reference reached, labelled by a GREEDY-rollout oracle. Optimizing that ≠ a coherent player — when
   eval-greedy DRIVES the game it reaches DIFFERENT/worse positions and errors compound. (My "bootstrap re-harvest"
   idea = DAgger, which addresses distribution shift — but NOT myopia.)
⇒ **The puzzle suite is a useful DIAGNOSTIC but NOT a sufficient game-strength proxy on its own**; and the greedy
main-phase heuristic is genuinely strong (hard to beat with a myopic eval). CONFIRMING IN GAMES CAUGHT THIS — the
discipline worked (didn't ship a suite-number mirage).
PIVOT: the eval improvement is real per-decision, so use it in its PROPER place — the leaf of the EXISTING tactical
search (SearchBot), NOT as a main-phase policy. Testing `moduleab learnedw` (learned weights, normal greedy main).
If it helps ⇒ the distillation value lives in the tactical branches (bounded). If the goal is main-phase strength,
it needs DEEPER search with the good eval (not 1-ply) — expensive, and the honest hard problem.

### ❌ learnedw NEUTRAL too — the distillation path is blocked (honest conclusion)
learned weights in the tactical search (proper use, greedy main kept): 46.9%(s3)/55.2%(s5) vs 48.5/54.0 = NEUTRAL.
So the distilled eval doesn't help games as a MAIN policy (−13pp) NOR as the tactical leaf (neutral).
**WHY the whole distillation loop is BLOCKED (the deep reason):** the ORACLE reference uses GREEDY rollouts, so
its "best move" = best-vs-greedy-continuation ⇒ distilling it can only reach the GREEDY ceiling. The AlphaZero
escape is BOOTSTRAP (distill → stronger policy → use as new rollout → repeat), but bootstrap CAN'T START because
round-1 distillation (eval-greedy) went BACKWARD in games (myopia + weak eval). Round 1 must produce a policy ≥
greedy in GAMES to bootstrap, and a myopic 1-ply eval isn't that.
⇒ **HONEST CEILING:** the bot is a strong hand-tuned HEURISTIC turn-level player (carrying all the playtest wins).
Beating it needs a COHERENT multi-step policy stronger than greedy — i.e. deep search with a good eval, or a
learned SEQUENCE policy + self-play (AlphaZero/ReBeL). That's a RESEARCH-GRADE project, not incremental tuning.
And "beats TOP HUMANS" can't be measured here at all (only greedy opponents) — needs HUMAN playtesting.
### 🧰 BANKED (real, shipped this session): fair-info determinizer, 2 hang fixes, removal follow-through,
weakest-first (+2pp), lazy-determinize perf. DURABLE TOOLS: oracle (per-decision regret), puzzle suite
(harvest/score/fit-eval — fast deterministic yardstick + distillation rig), champion gauntlet, paired A/B harnesses.
### RECOMMENDATION (for the user): two real paths, both deliberate —
1. **Human playtest loop** (highest value/effort): play strong humans vs the current bot, log where it loses, fix
   THOSE concretely. Self-play hid these; this is how to make it "close matches vs top players" fastest.
2. **Research project**: learned policy+value net (TorchSharp) + deep PIMC/ISMCTS search + self-play, bootstrapped
   & measured on the champion gauntlet. Big, uncertain, but the only path to "top players LOSE".

## 🔬 PATH 2 (user chose it): learned value/policy + self-play. PIECE 1 = OUTCOME-trained value
The distillation failed because it trained on the ORACLE's greedy-rollout values (greedy-anchored ceiling). The
fix: train the value on REAL GAME OUTCOMES (did this position actually win?) — a stronger, non-greedy-anchored
signal, and the AlphaZero value target. Built `fit-value <nGames> [seed] [ridge] [adv]` (`TrainValueOnOutcomes`):
self-play, record (Features(state) → 1 if that seat won else 0) at each clean-main state, least-squares fit →
fitted_w.txt. First run: 5626 states / 400 greedy games, R²=0.289; weights are intuitive (lifeDiff 0.125,
myLeaderReach 0.099, oppLeaderThreat −0.074, bias 0.432).
TESTING NOW (the moment of truth — does an OUTCOME value fix what the ORACLE value broke?): `moduleab evalmain`
(eval-greedy-main w/ outcome-value) + `learnedw` (outcome-value in tactical) @ s3,s5 vs intermediate. Baselines
none 48.5(s3)/54.0(s5). PREDICTION: eval-greedy-main may STILL lose (MYOPIA is independent of value quality — 1-ply
can't sequence a turn). If so ⇒ the value is fine but needs MULTI-PLY search (MCTS/PIMC), which is PIECE 2. If
eval-greedy-main is now ≥ baseline ⇒ outcome-value + 1-ply is enough and we bootstrap (re-gen data w/ new policy).
### PATH-2 BUILD PLAN (multi-session, honest):
1. ✅ Outcome-value trainer (fit-value). 2. If 1-ply insufficient → PIMC/MCTS search using the value at leaves
(fixes myopia). 3. Self-play LOOP: gen data w/ current best → refit value (+ later a POLICY head to guide search)
→ gate on champion gauntlet → promote → repeat (bootstrap). 4. Richer STATE ENCODER (more features / per-card).
5. Upgrade linear→MLP (TorchSharp) when linear plateaus. Measure everything on the champion gauntlet (user ratchet).

## Hypothesis queue (testable — from research + to-be-mined oracle divergences)
- **H1 Non-linear life eval:** replace linear `W2*(myLife-opLife)` with a convex penalty for own low life +
  convex reward for pushing opp low (last 1-2 life precious; ~2 ideal, 0 danger). (Eval → tactical branches.)
- **H2 Counter efficiency:** in greedy `DecideDefense`, prefer TAKING big early hits (high Life) and spend
  counters on efficient low-power blocks; don't overspend premiums early. (Partly present — measure tightening.)
- **H3 Attack sequencing:** bait/force blockers with an expendable attacker before swinging Leader; order by
  forcing the opponent's worst block. (Greedy attack order.)
- **H4 Lethal-window aggression:** at low life, key aggression to opp's ACTIVE DON / counter capacity, not
  life alone. (Eval aggro term W9 currently DAMPENS low-opp-life reward — suspicious; investigate/re-evolve.)
- **H5 Re-evolve Evaluation.cs weights** post-bugfix (the weights were evolved on the old ruleset).
- **H6 Explicit LETHAL DETECTION (near-universal in competitive CCG bots):** before ending the main phase,
  check if an attack sequence kills the opponent THIS turn (Leader ≤ 0 Life after unblockable/forced damage)
  and take it. Greedy may leave lethal on the table. High value, testable as an Advanced main-phase pre-check.
- **H7 Survival / opponent-reply pruning:** the LOCM #1 agent pruned on survival probability; TurnPlanner has
  `OpponentReplyPly` (OFF) that plays the opp's greedy reply before scoring — turn it ON for the ported planner
  and measure ("if I tap out they kill me next turn" awareness). Pairs with H1 (non-linear life).

## Loop log (self-paced, /loop)
- **iter 1 (2026-07-22):** kicked off n=120 baseline (firm up parity); research pull → LOCM competition patterns
  (eval=global+per-card diff; beam+survival-pruning = the #1 of 2300 agents; lethal detection near-universal).
  Added H6 (lethal detection), H7 (reply-ply/survival).
- **iter 2 (2026-07-22):** BASELINE FIRMED: shipped-advanced 57/120 = **47.5% vs intermediate** (65s; fast now
  that hangs are fixed) ⇒ robustly at parity/slightly behind. Launched the PIVOT: `honest-play 50 1 1200`
  (honest TurnPlanner vs intermediate, Wilson CI) — running in background. Research pull → COMPETITIVE OPTCG
  SEQUENCING (high value):
  • **Attack WEAKEST relevant attacker FIRST to force out Blockers/Counters, THEN send the key attacker into a
    depleted defense** — the #1 sequencing rule (concretizes H3). Our greedy attacks "Leader-default / profitable
    rested trades first", NOT bait-weakest-first → clear testable gap.
  • Attach DON only directly BEFORE each attack; attack before deploying to keep DON open for plays = flexibility.
  • If the goal is removal, attack the target Character first. Plan the WHOLE turn as a sequence (← the core
    argument FOR turn-level planning over greedy; validates the TurnPlanner direction).
  H3 refined → **H3a: weakest-attacker-first blocker/counter bait** (greedy attack-order toggle, A/B-able).
  **PIVOT RESULT (the big one):** honest `TurnPlanner` (fair-info, K=1) vs intermediate = **56% (167/300, CI
  [50-61%])**, stable at 58% (n=50). Budget 1200 vs 3200 = IDENTICAL 167/300; K=3 = 55% ⇒ strength is the
  turn-search+value-fn, NOT compute (cheap: 300 games/15s). vs shipped-advanced's 47.5% (same intermediate
  baseline) = **~+8.5pp**. Overturns the old "eval-bound ~45%" verdict (the 300+ effect fixes made the search
  accurate). ⇒ **DECISION: PORT TurnPlanner to the shipped Advanced bot** (matches the user's plan-the-whole-turn
  vision). Files to port: TurnPlanner + ValueFunction + ValuePolicy + DeckContext, wired into AdvancedContractBot's
  clean-main branches on the fair view (reuse BotDeterminizer). CAVEAT: honest-play uses GameRunner, shipped uses
  MatchDriver — CONFIRM same-driver first (wrap honest planner as a `tiers` agent) so the ~8.5pp isn't driver noise.
- **iter 3 (2026-07-22) — ⚠ SELF-CORRECTION, the port signal was a MIRAGE:** wrapped the honest planner as a
  `tiers` agent (`HonestPlannerAgent`, decklists reconstructed from state — fair, decklist-knowledge allowed).
  SAME-DRIVER, SAME-DECK-PAIRS paired comparison (both via MatchDriver/tiers):
  | seed | honest-planner | shipped-adv | Δ (shipped−honest) |
  |---|---|---|---|
  | 3 | 42.4% (50/118) | 47.5% (57/120) | +5.1 |
  | 5 | 51.0% (50/98) | 55.1% (54/98) | +4.1 |
  | 7 | 59.6% (59/99) | (timed out) | — |
  ⇒ Through the identical driver+pairs, the honest planner is ~4-5pp WORSE than shipped-advanced, NOT +8.5pp
  better. The earlier `honest-play` 56% was CONFOUNDED: different runner (GameRunner, with ledger/ObserveApplied)
  AND different deck pairs (rng seed 7 vs 3). **DO NOT PORT on that number.** Classic small-n/cross-config trap —
  exactly what the memory warns about. Also: honest-planner has ~1-2% UNFINISHED games via MatchDriver (stalls —
  no ObserveApplied ledger, or a planner loop) — a real gap, and part of why its MatchDriver showing is handicapped.
  BIGGER LESSON: absolute win% swings ~18pp by seed at n=100 (random unpaired deck pairs) ⇒ **measurement is
  VARIANCE-DOMINATED. The #1 blocker is the lack of a PAIRED harness.** Chasing heuristic deltas without it =
  chasing noise. NEXT ITER = build a proper PAIRED A/B harness (same deck pairs × both seat orders × baseline;
  report Δ + CI), reuse it for the port question AND every H1-H7 toggle. (Note existing `donab`/`counterab` A/B
  modes may be the paired template to copy.)

## 🐞 BUG FIXES (found via measurement infra)
### 2026-07-22 — Advanced bot HANG on deck-look (SearchBot ignored the blacklist) [FIXED]
Symptom: certain matchups froze to the command cap (would freeze the SHIPPED app too). Found via new
`advdiag`/`advtrace` modes: game 9 of `tiers` seed=3 = advanced(op16-luffy-n-ace) vs int(op16-blue-lucy)
looped forever issuing `deckLookSelect tgt=OP16-021` while `deckLook=select/5` never changed — a no-op the
engine rejects. `Succeeded` DID flag it and the host DID blacklist it, but `SearchBot.DecideOneCommand`'s
MAIN search path never consulted `blacklist` (only its greedy fallback did), so it re-picked the same no-op
every tick. FIX (`SearchBot.cs`): filter `legal` by `!blacklist.Contains(Signature(kv.Key))`; if none survive,
defer to the greedy fallback (whose deck-look path confirms/ends once every select is blacklisted). Verified:
the exact game now finishes in 96 commands. Applies to the shipped game too (GameManager passes aiTriedThisTurn
as the blacklist). Diagnostic modes added to Program.cs: `advdiag [n] [cap] [seed]`, `advtrace <A> <B> <first> [seed] [dumpAfter]`.
NOTE also observed: minor Advanced-bot NONDETERMINISM (same game varied 128↔204 commands) — likely HashSet
iteration order somewhere in the decision path; logged for later (not a correctness bug, but hurts A/B pairing).

- **iter 4 (2026-07-22) — PAIRED HARNESS BUILT (`abtest`) + corrected baseline:** new `abtest <tierA> <tierB>
  [pairs] [seed]` mode plays each random deck pair in BOTH seat orientations (cancels deck+seat bias),
  first-player alternated, Wilson CI. This is the trustworthy harness for the port question AND all H1-H7 toggles.
  **Corrected baseline: shipped-advanced vs intermediate = 50.9% (59/116, CI [42-60%])** — DEAD EVEN, not 47.5%
  (that was a seat bias from advanced-always-south in `tiers`). ⇒ the advanced MODULES are ~NEUTRAL (neither help
  nor hurt) over the greedy core, fair-info. To improve the ADVANCED bot they must add value greedy misses.
  Flagged: 4/116 games UNFINISHED (hit the 20000 cap — residual slow-grind or stall; investigate). Launched a
  tight n=600 paired baseline in bg. NEXT: implement H3a (weakest-attacker-first blocker/counter bait) as a
  per-seat toggle + A/B via a donab-style advanced per-seat test; also glance at the unfinished games.

## Experiment ledger
(one row per A/B run: toggle, n, A%, B%, delta, CI, verdict KEEP/DISCARD)
| iter | change | harness | n | result | CI | verdict |
|---|---|---|---|---|---|---|
| 4 | (baseline) shipped-adv vs int | abtest paired | 116 | 50.9% | [42,60] | too-small-n |
| 5 | (baseline) shipped-adv vs int | abtest paired | 594 | **56.2%** | [52,60] | REFERENCE — modules help ~+6pp |
| 5 | **weakest-first attack seq** (H3a) | seatab head-to-head | 3516 | **~54% / +4pp** | clear of 50 | ✅ SHIPPED |
| 6 | racedrop (commit-all-DON on lethal) | seatab | 3904 | 51.4% (49.9/52.9) | incl. 50 | ❌ inconclusive/discard |
| 6 | attackfirst (attack before deploy) | seatab | 3931 | **41.0%** (40.9/41.1) | clear BELOW 50 | ❌ STRONG NEGATIVE — discard |
| 6 | surplusoverload (force 2nd counter) | seatab | 3906 | 51.8% (50.5/53.1) | =NULL@s31 | ❌ NEUTRAL (seed artifact) |
| 6 | doubleattackfirst | seatab | 3904 | 51.6% (50.2/53.1) | =NULL@s31 | ❌ NEUTRAL (seed artifact) |
| 6 | **NULLCONTROL (identical bots)** | seatab OLD | 5860 | 50.2/**53.1**/50.2 @ s11/s31/s23 | s31 biased | 🔧 exposed a ±3pp harness bias |
| 6 | NULLCONTROL — FIXED harness | seatab NEW | 4000 | 51.3/49.8 @ s31/s37 | ~50, within noise | ✅ harness now ~unbiased |
| 6 | weakest-first — net of fixed null | seatab NEW | 2000 | 53.1 vs 51.3 null = +1.8 @ s31 | — | ✅ real ~+2pp (ship confirmed) |
| 7 | holdblockers / widedeploy / onkoaversion / richdeploy | seatab FIXED | 2000 ea | 46 / 32.5 / 50.5 / 49.6 | seeds agree | ❌ all discard (greedy SATURATED) |
| 7 | removal follow-through (exec-aware cost-KO) | correctness | — | smoke clean, scenarios 38pass | — | ✅ SHIPPED (user-requested) |

### iter 5 note (baseline re-established): **shipped-advanced = 56.2% vs intermediate, CI [52-60], n=594 (297s).**
Corrects iter 4's "neutral" (n=116 was too small — its CI contained 56%). The advanced modules DO add ~+6pp
over the greedy core, fair-info. So they earn their keep; the goal is to push higher. USE n≥600 for every A/B
(±4pp CI); n=116 lies. Baseline unfinished 6/594 = ~1% (down from the 3.4% n=116 read — also small-n noise).
Standard A/B command: `abtest shipped-advanced intermediate 300 3` (repeat with a 2nd seed to guard seed luck).
Port question (honest-planner) still open but DEPRIORITIZED (handicapped via MatchDriver: no ledger + stalls);
focus on improving the current 56.2% bot via H-toggles measured on `abtest`.

### iter 5 cont'd — ✅ FIRST SHIPPED WIN: weakest-first attacker sequencing
`WeakestFirstSeat` knob already existed (default OFF); the prior "unconditional weakest-first was null" verdict
PREDATED the 300+ effect fixes. Re-measured post-fix via new `seatab weakestfirst` (per-seat head-to-head, both
seats greedy, NEW=weakest vs OLD=strongest, SAME game): 51.9% (n780,s11), 53.5% (s23), 55.1% (n1956,s31) ⇒
**pooled ~54% / +4pp, CI clear of 50%.** SHIPPED as the IntermediateBot DEFAULT (`weakestFirst =
LegacyStrongestFirstSeat != seat`); flows to the Advanced bot (delegates attacks to greedy). Smoke clean (P(first)
53.67%, no crash). New harness `seatab <toggle> [games] [seed]`.
**NEXT (iter 6): H6 explicit LETHAL DETECTION** — does greedy take guaranteed lethal? Near-universal in
competitive bots; leaving lethal on the table = big win. Then H1 (non-linear life eval), H4 (lethal-window aggro).
- **iter 6 (2026-07-22):** H6 — greedy DOES have a lethal-ish detector (`race = leaderReachers >= oppLife +
  oppBlockers`, ~L1352, pushes Leader). Tested the gap "commit ALL DON on lethal" (`racedrop`): 49.9% (s11) /
  52.9% (s31), pooled 51.4% CI incl. 50 ⇒ INCONCLUSIVE, discard. Pivoted to a SYSTEMATIC survey: ~35 default-off
  A/B knobs exist (many rejected PRE-bugfix — weakest-first proved some are wins now). Batch-testing the most
  research-aligned untested ones via `seatab`: `attackfirst` (attack before deploying = keep DON flexible — direct
  guide match), `surplusoverload` (force a 2nd counter with spare DON), `doubleattackfirst`. Batch (3×2 seeds)
  running in bg → /tmp/knobbatch.txt. KEEP rule: ship only if >50% at BOTH seeds AND pooled CI clear of 50 (the
  weakest-first bar); racedrop-style seed-disagreement = discard. NEXT: read batch, ship any clear win, then if
  the knob survey saturates → RESEARCH PIVOT (advanced-specific: re-evolve Evaluation.cs post-bugfix / expand
  where search applies, since greedy toggles are near-saturated).
- **iter 6 cont'd — 🚨 CRITICAL METHODOLOGY FIX (the most important find of the loop so far):** batch results
  were: attackfirst **41%** (STRONG NEGATIVE — a human-planner heuristic that HURTS a greedy bot, discard),
  surplusoverload 51.8%, doubleattackfirst 51.6% (both the SAME 50/53 seed-split as racedrop). A **NULLCONTROL**
  (identical bots, no toggle) exposed the cause: seed 11=50.2, seed 23=50.2, **seed 31=53.1%** — a real ±3pp
  per-seed HARNESS BIAS. The old `seatab` redrew decks per game INSIDE the 4-game balancing group, so deck
  variance didn't cancel. ⇒ surplusoverload/doubleattackfirst/racedrop "wins" were the seed-31 artifact = ALL
  NEUTRAL, discard. **FIX:** `seatab` now plays each deck pair through a fully-crossed 4-game block (NEW∈{S,N} ×
  first∈{S,N}, SAME decks) ⇒ deck+seat+first cancel PER MATCHUP; null should be ~50 at every seed (verifying).
  **weakest-first REVISED (net of the artifact): +1.7/+3.3/+2.0 @ s11/s23/s31 ⇒ real ~+2.3pp** (ship STANDS,
  effect smaller than the raw +4pp read). LESSON (bake in): single-seed raw seatab overstates by up to +3pp;
  ALWAYS use the fixed matchup-paired harness + ≥3 seeds + a NULLCONTROL sanity check. This is the same
  small-n/artifact trap the memory keeps flagging — now structurally fixed in the harness.
- **iter 7 (2026-07-22):** (a) FIXED-harness knob batch2 → holdblockers −4pp, widedeploy −17pp, onkoaversion/
  richdeploy neutral; seeds now AGREE (harness trustworthy) ⇒ GREEDY TOGGLES SATURATED (weakest-first was the
  lone win). (b) USER REQUEST — removal follow-through: `RemovalModel.BestEffectiveCostKoThreshold` was a
  CAPABILITY scan (deck HAS a cost-KO tool anywhere) not EXECUTION — so the bot valued/attempted a −cost as
  removal even when the KO couldn't land this turn (the Van Augur "−3 cost but the removal never happened"
  class). Made it EXECUTION-aware: a HAND finisher must be affordable with DON left after the setup; a board/
  leader [Activate: Main] finisher must be un-used + its [DON!! xN] gate payable. Compiles, smoke clean,
  scenarios 38pass. (Van Augur-class activate itself already fixed earlier via BeneficialActivateMain clause
  isolation.) NICHE (only −cost-combo/black decks) ⇒ correctness win, won't move aggregate win%.
  **STALL SIGNAL: greedy saturated + niche removal done ⇒ NEXT = RESEARCH PIVOT to ADVANCED-SPECIFIC levers**
  (the eval `Evaluation.cs` drives the search/rollout — re-evolve or add features post-bugfix; and/or expand
  where the advanced bot applies search). Per the loop's "cycle back to deep research when stalling" rule.
- **iter 8 (2026-07-22) — DIAGNOSTIC: which module drives the +6pp?** Added per-module skip toggles to
  `AdvancedContractBot` (SkipActivation/SkipPressure/SkipSearch/SkipTriggerUtility) + new `moduleab
  <act|pressure|search|trigger|none> [pairs] [seed]` mode (matchup-paired advanced-vs-intermediate with a module
  disabled ⇒ falls back to greedy; the DROP from the ~56% "none" baseline = that module's contribution). Running
  none/act/search/trigger @ 150 pairs seed 3 in bg → /tmp/moduleab.txt. This tells me WHERE the advanced headroom
  is before investing: if e.g. Activation carries most of the +6pp, improve it; if Search adds little, the eval
  isn't the lever. NEXT: read contributions → invest in the biggest module (or fix a module that HURTS).
- **iter 8 RESULT (seed 3, configs play IDENTICAL games ⇒ clean paired deltas):**
  | skip | win% | contribution |
  |---|---|---|
  | none | 51.3% | — |
  | Activation | 51.0% | ~0 |
  | **Search rollout** | 47.7% | **+3.6pp (carries the edge)** |
  | TriggerUtility | 51.4% | ~0 (and EXPENSIVE — skip=search ran 21s vs 300s+) |
  ⇒ **the whole Advanced edge is the SearchBot ROLLOUT on tactical/resolution branches; Activation & Trigger add
  ~nothing here.** The lever is the ROLLOUT/EVAL (`Evaluation.cs` leaf + ChampionBot playout), NOT the activation
  logic. Confirming the search delta @ seeds 5,7 (bg /tmp/moduleconf.txt). Implications: (a) invest in the eval
  (re-evolve post-bugfix / add features: non-linear life H1, lethal-proximity) OR the rollout policy; (b)
  Activation/Trigger are candidates to SIMPLIFY (Trigger sim is costly for 0 gain) — but verify per-archetype
  first (Activation may matter for activation-engine decks specifically, washed out in the mixed field).
  ⚠ single-seed; the "none"=51.3% here vs 56.2% earlier (abtest) = game-seed variance — trust the PAIRED deltas.
