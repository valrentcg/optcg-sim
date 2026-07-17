# Deck-mechanic workstreams — 2026-07-16

## Framing

For every archetype below the **engine already implements the rules** (verified off-disk). The bot loses
these decks because the **planner / value model is generic** — it has no concept of the resource or removal
mechanic the deck is built around. So each workstream is a *bot-side* value/targeting model, keyed off
printed card text, applied to every deck that exposes the mechanic — never a card-id patch.

Shared principle: the substrate is fine; teach the pilot the mechanic.

---

## WS-1 — DON-minus payment + DON-threshold engines

**Scope by MECHANIC, not color.** Purple is the DON-manipulation color, but `DON!! −N` also appears
off-color — e.g. **ST03 Crocodile's leader** (`[Activate: Main] DON!! −4: return a Character cost ≤5 to hand`)
is a DON-minus bounce on a non-purple deck. Keying this off printed text unbreaks Crocodile's leader bounce
*and* purple with one rule. **Enel is unique:** it is the ONLY deck that caps at 6 DON (a 6-card DON deck),
so the "sit in a low-DON band" strategy is Enel-specific — but the rule that enables it (detect `N or
(more|less) DON` conditionals on the piloted deck) stays general and simply lights up hardest for Enel.

**Decks:** any card with `DON!! −N` (ST03 Crocodile leader, purple staples) or a `N or less/more DON`
conditional (Enel most of all).

**Mechanic (engine-supported):**
- `DON!! −N` payment = return N DON from field to the DON deck (`ParseDonMinusCost`, `PayDonMinus`).
- `N or less/more DON!! cards on your field` conditional bonuses (`GameEngine.cs:3702`).
- `When N DON!! returned to your DON!! deck` triggers (OP09-061 line ~3193).
- Enel leader (OP15-058): 6-card DON deck; re-ramp each turn and dump up to 4 DON onto a Character.

**Bot gap:** `ValueFunction` rewards DON monotonically (`MyActiveDon +0.1`, `MyAttachedDon +0.15`) and has
**no threshold term.** Consequences:
- Over-ramps past the ≤6 band, turning off the deck's own bonuses (Shura Rush, unremovable Enels, extra draws).
- Scores `DON!! −N` plays as pure losses → refuses Crocodile's leader bounce and the whole Enel event suite.
  (This is exactly the observed "Crocodile kept DON up but never bounced" behaviour.)

**Broad rules to build:**
1. **DON-threshold-band awareness — driven by the DETECTED DON-deck cap, never an Enel profile.** Read the
   player's actual DON-deck size (`PlayerState.DonDeck`; Enel's leader sets it to 6, normal decks are 10) and
   reason about the threshold *relative to that real cap*. Then scan the piloted deck's own cards for
   `N or (more|less) DON` conditions; when several share a threshold, make that DON count a preferred band in
   the eval instead of "more is better." Enel's ≤6 behaviour falls out of "cap is 6", with zero card-id logic.
2. **DON-minus valued net-of-effect** — treat `DON!! −N` as a payment; value the play by (effect bought) −
   (DON returned), and *credit* the returned DON when it re-enters a beneficial band or feeds a re-ramp leader.
3. **Activation DON routing** — when an activation distributes DON to a Character, target the one that converts
   it to damage/a payoff this turn (an unrested attacker that connects), not an arbitrary body.

**Measure:** paired `trigger-policy-ab`-style A/B, Enel vs Lucy, ON vs OFF the DON model.

**RESULT 2026-07-16 — premise largely refuted.** The gated DON-engine eval change (stop rewarding parked
DON) moved **0/24** games (`don-engine-ab`). A direct play-trace (`enel-diagnostic`, 8 games) then showed the
bot ALREADY executes the engine: **6.5 activateMain/game** (leader+characters, ~124% of turns), **6.6
DON-minus events PLAYED/game with ~0.1 left in hand** (it does not hoard them), and it routes ~5 DON/turn
(2.6 leader / 2.4 characters). So "the bot over-ramps / won't pay DON-minus / hoards DON" is FALSE for the
shipped advanced bot — there is no gross DON-mechanic bug left. Detection (`IsDonEngine`, cap, band) is
retained as infrastructure; the inert eval adjustment is not a win-rate lever. The residual Enel gap vs the
66.9% ranked reference is therefore fine-grained decision quality (event/target selection, sequencing) or a
simulator opponent-strength mismatch (already flagged in `lucy-enel-directional-matchup`), NOT engine execution.

---

## WS-2 — Black: cost manipulation / cost-based removal

**Decks:** black (Moria, Teach, etc.).

**Mechanic (engine-supported):**
- `Give … −N cost during this turn`, floored at 0 (`GameEngine.cs:8121`).
- KO / selection gated on `with a cost of N or less`.
- Conditionals on `opponent has a Character with a cost of 0 / N or less` (line ~3682).

**Bot gap:** no model of the two-step **"lower the target's cost, then KO by cost"** line; cost-reduction on the
opponent reads as a weak generic debuff, and cost-down on own cards reads as noise. The bot won't set up the
KO it is built to land.

**Broad rules to build:**
1. Model `-cost → KO-by-cost` as one combined removal line (value the setup by the KO it unlocks).
2. Value opponent cost-reduction by whether it drops a real threat under a KO/effect threshold this turn.
3. Value own cost-reduction as ramp/enabler toward a concrete play, not flat.

**Assessment 2026-07-16 (post WS-3):** most black removal is direct **KO-by-cost** or "cost ≤N cannot attack"
(Moria/Thriller Bark: Zeus KO ≤5, Perona ≤6 freeze) — already covered by WS-3 (legality enforces the
threshold; RemovalModel classifies Ko/Freeze and targets correctly). The genuinely-NEW piece is narrow: the
Blackbeard **−cost→KO combo** (op16-black-teach: Stronger −2, Van Augur −3), where −cost is near-inert unless
a KO-by-cost follows. That is a targeted combo-recogniser for ONE archetype.

**BUILT + measured 2026-07-16.** `RemovalModel.CostDownAmount`/`BestEffectiveCostKoThreshold` (excludes
"base cost" KOs — −cost doesn't change base cost) + `DecideEffect` values a −cost as a removal SETUP when a
KO-by-cost finisher unlocks it. Fixture PROVES the line flips (withFinisher→big body, without→live body),
22/22. `cost-combo-ab` on black-teach vs Lucy moved **0/24** — the decision is correct (fixture) but the full
combo situation is rare in that matchup, so it is a correctness fix, not a win-rate lever there. Gated
`IntermediateBot.CostComboAware`.

## Meta-finding (all workstreams, 2026-07-16)

Every discrete tactical fix this session — TriggerUtilityPolicy, WS-3 removal targeting, WS-2 cost-combo — is
provably CORRECT (fixtures) yet fires RARELY, so none moves win rate alone (trigger A/B 1/24, removal 7/24
neutral, cost-combo 0/24; WS-1 flat weight 0/24). The per-decision play is mostly already sound. The remaining
strength gap is therefore NOT another discrete rule but decision-QUALITY in aggregate (codex's opportunity-cost
DON model: use-vs-hold-for-Counter, sequencing, DON routing) and/or sim opponent-model calibration — pursue
via the deeper model, validated FIXTURES-FIRST (prove lines change) before any win-rate A/B.

---

## WS-3 — Removal fidelity: cost-KO vs power-KO vs −power (shared foundation)

**Decks:** all — this is the targeting substrate WS-1 and WS-2 both rely on.

**Mechanic:** removal comes in distinct flavours the bot currently conflates —
- `K.O. … with a cost of N or less` (cost threshold),
- `K.O. … with a power of N or less` (power threshold),
- `give −power` (shrink: stops an attack / enables a KO / nothing if the body already acted),
- `give −cost` (enables a cost-KO; otherwise inert).

**Bot gap:** `IntermediateBot.IsNegativeEffectText` lumps all of these together and `Value()` ranks every target
by `cost*1000 + power`. So the bot can pick a target the effect *cannot actually remove* (wrong threshold), and
cannot tell a permanent KO from a temporary −power. (The battle-Trigger policy shipped this session fixes one
narrow case — a −power debuff is not credited as removal — but only at the Trigger step.)

**Broad rules to build (generalise the Trigger-policy fix to ALL targeting):**
1. A **removal-capability model**: for a given removal effect, compute the set of targets it can *actually*
   remove/affect by the correct threshold (cost vs power), and rank only within that set.
2. Value a real KO at the removed body's full investment; value `−power` only as mitigation/enabler (never as
   removal); value `−cost` only by the KO it unlocks.
3. Prefer targets that are live threats (can still attack / block) over spent bodies.

---

## Sequencing (proposal)

WS-3 is the shared targeting foundation; WS-1 has the strongest evidence (the 60-0 pathology and codex's
directional finding) and the most visible payoff. Recommended order: **WS-3 removal-capability model → WS-1
purple DON model → WS-2 black cost model**, each gated by a paired A/B before it ships. Do not tune global
matchup weights to any single mirror result.

---

## Unmodeled engine archetypes — pool scan + guide cross-reference (2026-07-18)

Scanned all 41 meta decks for mechanical signals; four high-prevalence ENGINE archetypes are not modeled by
the bot (it plays their cards for stats/immediate value but has no concept of the engine). Guide-confirmed.

### A. Trash / mill recursion — 23/41 decks (BIGGEST gap; Yamato 35, Moria 35, Five Elders 25, Nami, Teach)
Self-mill to fuel the trash, then replay Characters from the trash (often with Rush) for value + tempo. The
trash is a RESOURCE. Guide: "recursion-aggro — send Wano characters to trash, then replay them with Rush …
the deck's core engine turn chains a draw into a large Rush attacker from trash." (shonentcg Yamato guide)
BOT GAP: no concept of trash-as-resource — self-mill reads as card LOSS to the eval; "play from trash" isn't a
plan; recursion targets in trash aren't valued. ZERO modeling.

### B. Life manipulation — 16/41 decks (uy-Nami 30, Boa 29, by-Nami 27, Crocodile, y-Luffy, Moria)
Manipulate your OWN Life: add cards to the top of Life to set up [Trigger]s, gain Life to stabilize, take
deliberate hits when the top card is known. Guide: "put a card from hand into your Life pile to set up your
triggers … taking early hits is part of the strategy … play Characters through Trigger effects." (onepiece.gg
Boa Hancock). BOT GAP: Life is only a damage counter (MyLife); HasLowLifePayoff is minimal. No add-to-top / life-
gain-as-stabilization / known-top-trigger modeling. Connects to the shipped TriggerUtilityPolicy.

### C. Green rest-lock / freeze-control — 13/41 decks (Krieg, green-Mihawk, g-Bonney, gp-Lim, g-Zoro)
Rest the opponent's board AND KEEP it rested (freeze) repeatedly, so it can never attack or block, while you
attack freely. Guide: "rest your opponent's characters … keep them rested, freezing them over and over."
(sabatcg color guide). BOT GAP: RemovalModel classifies/【values Freeze+Rest per-instance (WS-3), but there is
no LOCK-ENGINE plan — sustaining the freeze as a win condition. Partial only.

### D. DON ramp — 12/41 decks (purple-Doffy 31, Foxy 30, Sengoku 27, up-Luffy, Rosinante)
Accelerate DON (add DON from the deck as active) to deploy expensive threats ahead of curve. Guide: "efficient
DON ramping — Baby 5 adds 1 DON from your deck as active … play big things early." (onepiece.gg Doffy). BOT
GAP: HasDonRecovery detects DON-add but there is no RAMP PLAN (accelerate → play big early); distinct from
Enel's cycle-DON-minus (ramp UP vs cycle-low).

Priority by prevalence + gap severity: A (trash, 23, zero) > B (life, 16, minimal) > D (ramp, 12, partial) >
C (rest-lock, 13, partial via WS-3). All would be text-driven/card-id-free like the shipped work.
