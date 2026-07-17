# Observer Knowledge State — Design Specification (REVISION 3, DRAFT FOR REVIEW, NOT IMPLEMENTED)

**Status:** revision 3. Four layers approved (rev 1). Two-representation model approved (rev 2), with
corrections below. **No implementation. Implementation is gated on the mutation-site audit (§14).**
**Date:** 2026-07-15

### What changed in revision 3

| rejected in rev 2 | why it was wrong | rev 3 |
|---|---|---|
| **"Life damage ⇒ identity to all"** | **FLATLY WRONG.** CR §10-1-5 and `GameEngine.cs:2501-2504`: *"the card is NOT revealed unless its owner chooses to use its [Trigger]"* | full damage flow, §4.5; `Battle.RevealedLife` masked from the attacker |
| `KnowledgeEvent` carries `InstanceId`+`CardId` always, `IdentityViewers` tells the ledger not to look | **discipline-based privacy inside the ledger** — the exact thing this design claims to abolish | two types: `RefereeEvent` (truth) → per-viewer `ObservedKnowledgeEvent` (unobserved fields **physically absent**) |
| "decrement every `AtLeast`" as MVP | privacy-safe but **loses correlations** and injects artificial memory loss ⇒ confounds the strength measurement | grouped **ambiguity constraints** (§4.4); blanket forgetting demoted to a **counted, reported failsafe** |
| surrogate ids stable within a decision | leaves a correlation channel across decisions | **intentionally unrelated** across decisions (§5.1) |
| own hidden zones need no facts | the decklist fixes the **combined** unseen pool, **not** its division between deck and facedown Life | own-zone facts required (§2.4) |
| `Apply` is O(1) | it touches every fact the transition affects | **O(facts affected)** |
| audit runs alongside | an event funnel **cannot protect sites that bypass it** | **the audit GATES implementation** (§14) |

⚠ **The recurring failure mode, now four for four.** Every rejection this cycle has been the same shape:
*the claim was stronger than the mechanism.* `ValidatesAgainstList` claimed equality, checked `<=`. The
"private" log set no `PrivateSeat`. `KnownIdentities` claimed self-sufficiency, needed the referee.
`KnowledgeEvent` claimed type-enforced privacy, enforced it by convention. **I keep type-enforcing the
boundary at one end and leaving the other end to discipline.** Rev 3's rule: every privacy claim names
the type or the test that makes it impossible to violate — or it is not a claim.

---

## 0. Why this exists

`GameClone.ClonePlayer` copies `Deck`, `Hand` and `Life` verbatim ⇒ the PlannerBot searches with perfect
information. Only the planner cheats (`IntermediateBot` never clones) ⇒ contamination is
one-directional and **inflates the planner**. Every published win rate, incl. 61.3%, measured that bot.

Hidden information is **observer-specific knowledge, not a fixed list of hidden zones**:
> **OP10-119** — *"Reveal up to 1 {Supernovas} type Character card from your hand and add it to the top of
> your Life cards face-down."* → revealed to **both**, then facedown. Any `(zone, FaceUp)` model inverts it.

Conversely, naive reshuffling **destroys legally-held knowledge** — and `op16-rayleigh` (`searchers=25`)
is both the extreme case and this project's unexplained outlier. That failure presents as a weak
evaluator: the diagnosis this project has already wasted months on.

**Rules anchors:** facedown Life is secret to **both** players (CR §3-10-2). Damage does **not** reveal
(CR §10-1-5). **Prior art:** `LogEntry.PrivateSeat`/`PublicMessage` (`GameState.cs:190-194`).

### 0.1 🚨 The defence-lookahead result is INVALID, and the mechanism itself is illegitimate

**Refined diagnosis (rev 4 — rev 3's was too shallow).** Masking `Battle.RevealedLife` from the attacker
is **necessary but nowhere near sufficient**. The illegal conditioning happens one level deeper:

`ValuePolicy.ResolveBattle` (`ValuePolicy.cs:117`) loops `Decide` → `GameEngine.ApplyCommand` until the
battle ends, and `ShouldLookAhead` (`:105-112`) fires at `Step == "block" || "counter"`. So **the
defender's own simulated block/counter decision is scored on an outcome computed by rolling forward
through the TRUE future Life card** — which the defender cannot legally know at that step (Life is secret,
CR §3-10-2). They learn it only at the Trigger step, and only if they activate (CR §10-1-5). The
attacker's outer planner then scores that future-informed defence.

⇒ **Root determinization alone would leave this leak entirely intact**, and `privacy-test` would go green
with it in place. **Every simulated decision must receive the ACTING SEAT's legal observation.** That is a
strictly stronger requirement than a root boundary — and it is the classic PIMC failure: sample one world,
then let every player see it.

**The irony is load-bearing, not decorative.** `ShouldLookAhead`'s docstring justifies lookahead precisely
because *"the payoff is INVISIBLE at decision time... scoring them in place always ranks them below
passing"*. The fix for invisibility **was to make the future visible** — which is exactly the illegal act.
An honest defender must reason over the **distribution** of Life cards, not the actual one. This is a
**redesign of `ValuePolicy`**, not a re-measurement of it.

⚠ **What is and is NOT established (rev 5 correction — rev 4 overclaimed).**
- **ESTABLISHED:** the +15pp measurement (5.0% → 20.0%, p≈0.013) is **INVALID** and is **discarded**,
  because the policy illegally conditions block/counter on the true future Life card.
- **NOT ESTABLISHED:** that *all 15 points* came from cheating. **Honest, expectation-based battle
  lookahead may retain an unknown portion of the gain** — the core insight (score defence on its
  OUTCOME, not mid-battle) is sound and legal; only the *implementation* reaches through the boundary to
  get it. "The mechanism works by cheating" (rev 4) asserted a decomposition nobody has measured.
- ⇒ The honest statement: **the number is void; the idea is unrefuted.** Re-measure after the redesign.
⇒ Dedicated tests for both levels in §7.

---

## 1. Four layers (approved)

| layer | type | who reads it | referee IDs? |
|---|---|---|---|
| Referee truth | `GameState` | engine only | yes |
| Internal ledger | `InternalKnowledgeLedger` | engine only | **yes — never leaves** |
| Observation | `PublicObservation` (embeds `ObservationKnowledge`) | the planner | **no** |
| Sampled world | `GameState` | the search | freshly minted |

```
GameState ──RefereeEvent──> Project per viewer ──ObservedKnowledgeEvent──> InternalKnowledgeLedger[seat]
                                                                                   │
                                    Project(GameState, seat, ledger) ──> PublicObservation
                                    Determinize(PublicObservation, seed) ──> SampledWorld ──> search
```

**HARD RULE:** `Determinize` accepts **only** `PublicObservation`. The signature is the enforcement.

---

## 2. Representations

### 2.1 `InternalKnowledgeLedger` (engine-private)

```csharp
sealed class InternalKnowledgeLedger          // one per VIEWER seat
{
    Dictionary<string, string> IdentityOf;                          // instanceId -> cardId
    Dictionary<(string owner, Zone z), List<string>> KnownTop;      // instanceIds, 0 = nearest TOP
    Dictionary<(string owner, Zone z), List<string>> KnownBottom;   // instanceIds, 0 = nearest BOTTOM
    Dictionary<(string owner, Zone z, string cardId), int> AtLeast;
    List<AmbiguityGroup> Groups;                                    // §4.4
}
```

**`Apply` accepts ONLY `ObservedKnowledgeEvent` (§3).** A South ledger processing a private North draw
never receives North's `CardId` or `InstanceId` **at all** — not as a field it is trusted to ignore.
Complexity: **O(facts affected by the transition)**, not O(1).

### 2.2 `ObservationKnowledge` (sanitized — no referee IDs, ever)

```csharp
sealed class ObservationKnowledge
{
    List<string> OwnHand;                     // EXACT CardIds
    int OppHandSize;                          // public
    List<CountFact>     Counts;               // { Seat, Zone, CardId, AtLeast }
    List<SegmentFact>   Segments;             // { Seat, Zone, Anchor: Top|Bottom, CardIds[] }
    List<AmbiguityGroup> Groups;              // §4.4
    ListAssumption Assumption;                // OpenList | UnknownList
}
```

**Counts, not instances.** A revealed opponent-hand card is knowledge of a *card*. The opponent
rearranges their hand freely and moves cards privately; tracking an `InstanceId` through that is not
knowledge a player could hold — it is a leak with a knowledge-shaped label.

### 2.3 Semantic positions

`Top(0)`/`Bottom(0)`. **Never raw indexes.** Encodings differ per zone and the engine says so:

| zone | `Top(0)` is | source |
|---|---|---|
| Deck | `Deck[0]` | `Deck.Insert(0, …)` (`1936`, `7445`) |
| **Life** | **`Life[Count-1]`** | **`GameEngine.cs:1920`** *"the Life list stores TOP at the END"*; damage does `Life.RemoveAt(Life.Count-1)` |

One adapter owns this mapping. Rev 1 wrote `Life[0]` and was wrong; raw indexes are how that happens.

### 2.4 Own hidden zones (rev 2 got this wrong)

A player knows their **constructed decklist**, which fixes the **combined** unseen pool
`deck ∪ facedown Life` — but **not how identities divide between them**. So:

- model the **combined own unseen pool** as the primitive;
- apply known own Deck/Life `Segments` and `Counts` on top;
- **never infer exact per-zone composition from the decklist.** "My deck still holds 3 Nami" is *not*
  derivable — some may be sitting in Life.

Same treatment for the opponent under `OpenList`.

---

## 3. Two event types (rev 2's architecture correction)

```csharp
readonly struct RefereeEvent          // engine-internal: truth + visibility metadata
{
    Kind Kind;                        // Move | Reveal | Shuffle | Setup
    string InstanceId, CardId, OwnerSeat;
    ZoneRef From, To;                 // semantic
    HashSet<string> IdentityViewers, MovementViewers, PositionViewers;
}

// PROJECTED PER VIEWER — DISCRIMINATED VARIANTS. Each carries ONLY legally visible data.
// ⚠ rev 3 used ONE struct holding SurrogateId/CardId/From/To with comments saying fields were "absent
// unless…". A comment is not an absence. That was COMMENT-ENFORCED privacy inside the very type whose
// job is to make privacy structural — the FIFTH instance of this cycle's recurring error.
abstract record ObservedKnowledgeEvent;

sealed record ObservedCountChange(ZoneRef Zone, int Delta)                    : ObservedKnowledgeEvent;
sealed record ObservedUnidentifiedMove(ZoneRef From, ZoneRef To)             : ObservedKnowledgeEvent;
sealed record ObservedIdentifiedMove(LedgerToken Tok, string CardId,
                                     ZoneRef From, ZoneRef To)               : ObservedKnowledgeEvent;
sealed record ObservedReveal(string CardId, ZoneRef At)                      : ObservedKnowledgeEvent;
sealed record ObservedPositionChange(ZoneRef Zone, Anchor A, int Count)      : ObservedKnowledgeEvent;
sealed record ObservedShuffle(ZoneRef Zone)                                  : ObservedKnowledgeEvent;
```

**There is no variant that carries a `CardId` the viewer may not see.** An unidentified private draw is
an `ObservedUnidentifiedMove` — the CardId is not omitted, it is *unrepresentable*.

`Project(RefereeEvent, viewer) → ObservedKnowledgeEvent` is the **only** path into a ledger.

**The three visibility axes are load-bearing, not a refinement:**
- *private search to hand* — movement public, identity private, position public.
- *OP10-119* — identity public, movement public, position public, **despite ending facedown**.
- *damage, no Trigger* — movement public (hand +1, Life −1), **identity private to the defender**, position public.
- *shuffle* — movement public, identity irrelevant, position **destroyed for all**.

Knowledge must never be parsed from `EventLog` strings. It is more load-bearing than rules-8.2
adjudication, which already reads `LogEntry.Actor` (`GameRunner.cs:106`); it must not inherit a weaker
mechanism.

---

## 4. Update and invalidation rules

### 4.1 General

| transition | effect |
|---|---|
| **setup** | Life dealt facedown: **no identity to anyone, incl. the owner** (CR §3-10-2). Deck order unknown to all. Opening hand → owner's `IdentityOf` + `OwnHand`. |
| **draw** | → drawer's `IdentityOf`. If it was `KnownTop[owner, Deck][0]` for a viewer, **pop** it and convert to `AtLeast[(owner, Hand, cardId)] += 1` for that viewer. |
| **public reveal** | `IdentityViewers = all`. Survives later facedown placement. |
| **private reveal / search to hand** | `IdentityViewers = {searcher}`. Others learn only the count change. |
| **hand → public zone** | identity → all; decrement source-hand facts per §4.4. |
| **public → hand** | identity persists for all who saw it ⇒ `AtLeast[(owner, Hand, cardId)] += 1`. |
| **public → deck top/bottom** (`7445`) | identity persists; write a `Segment` at the correct anchor for `PositionViewers`. |
| **shuffle** (`ShuffleInPlace`, `9618`) | §4.3. |
| **Life damage** | **§4.5 — the rev 2 rule here was wrong.** |
| **Life gain, revealed** (OP10-119) | identity → all; `Segment{Life, Top, [cardId]}` for all. |
| **face-down flip** | does **not** erase knowledge already held. |

### 4.2 Deck-look completion, split by mode (`FinishDeckLook`, `GameEngine.cs:1915`)

| mode | engine behaviour | knowledge effect |
|---|---|---|
| `LifeMode` | `Life.Add` reversed ⇒ arranged left→right = top→bottom of Life (`1920-1925`) | `Segment{Life, Top, [ordered]}` for `PositionViewers` |
| `ToTop` | `Deck.Insert(0,…)` reversed ⇒ first arranged is topmost (`1933-1937`) | `Segment{Deck, Top, [ordered]}` |
| default | `Ordered` is documented **"bottom-of-deck order"** | `Segment{Deck, Bottom, [ordered]}` |
| `SearchMode` | full-deck search, **remainder shuffled back** | identity of the **taken** card to `IdentityViewers`; **shuffle invalidation for the rest** — knowledge-*destroying*, which rev 1 recorded as knowledge-gaining |
| `TrashRest` | rest → trash | those become public; identity to all |
| `CompleteDeckLook` → `DeckLook = null` (`1896`) | — | ⚠ the engine **destroys** known order here; the ledger must capture it **before** this fires |

### 4.3 Shuffle invalidation

For the shuffled zone, **for every viewer**: clear `KnownTop`/`KnownBottom`; **drop `IdentityOf` for every
instance in the zone**; **retain** `AtLeast` counts. Rationale: instances keep their slot in the shuffled
list, so retained `IdentityOf` would let a determinizer pin that instance's CardId — **a leak wearing the
costume of knowledge**. The viewer still legally knows *"the deck contains an X"*; that is the count fact.

### 4.4 Grouped ambiguity constraints (replaces "decrement everything")

Blanket decrementing is privacy-safe but **loses correlations** and injects artificial memory loss —
which would confound the very strength measurement this work exists to produce.

```csharp
sealed class AmbiguityGroup
{
    ZoneRef  Source;             // where the departure happened
    Multiset<string> Candidates; // known CardIds that MIGHT have left
    int HiddenDepartures;        // how many cards left unobserved
    int MinRemaining;            // >= this many of Candidates must still be in Source
}
```

**Example.** South knows X and Y are in North's hand. One unidentified card leaves.
⇒ `Group{ Source=(north,Hand), Candidates={X,Y}, HiddenDepartures=1, MinRemaining=1 }`
⇒ *"at least one of {X,Y} remains"* — **the correlation is preserved**, where rev 2 forgot both.

**Blanket conservative forgetting survives only as a FAILSAFE** for effects the model cannot express.

**Failsafe policy — no threshold.** Any benchmark match that invokes blanket forgetting **even once** is
**EXCLUDED from headline "honest" results** and reported separately. There is no arbitrary `k`: the
fallback injects artificial memory loss, so a match containing it is not measuring honest strength, it is
measuring honest strength *plus an amnesia bug*. **If fallback incidence is material, the experiment is
INCOMPLETE, not merely noisy** — the correct response is to extend the model to cover the effect, not to
average the damage away. An unflagged fallback is exactly how "the eval is weak" gets concluded from a
memory bug.

### 4.5 🚨 Life damage — the corrected flow (CR §10-1-5; `GameEngine.cs:2499-2515`)

**A Life card taken as damage is NOT automatically revealed.** The defender reveals it only by
activating its Trigger.

⚠ **Wording corrected (rev 5).** Rev 4 said "no Trigger ⇒ never revealed to **anyone**". Wrong: the
**defender always privately learns the card** at the Trigger decision — whether they activate or decline —
because it goes to their hand and they must check it to decide at all. **Only the ATTACKER's knowledge
depends on activation.**

| step | engine | knowledge |
|---|---|---|
| **before damage** | — | **neither** player knows the facedown Life identity (absent prior knowledge). |
| damage check / Trigger decision | `Battle.RevealedLife = cardFromLife; Step = "trigger"` (`2499-2500`) | **defender knows it; attacker does not.** `IdentityViewers = {defender}`. |
| **no Trigger** | `FinalizeTrigger` fires immediately (`2504`) | → defender's hand. Defender knows it (it is in their hand). **Attacker never learns it.** |
| **PassTrigger** | `FinalizeTrigger` → `Hand.Add` (`2513-2514`) | same: defender knows, **attacker never learns it**. |
| **UseTrigger** | trigger activates | identity becomes **public at activation** ⇒ `IdentityViewers = all`. |
| **Banish** | card → public trash | identity **public** (the trash is public). |

⇒ **`Battle.RevealedLife` MUST be masked from the attacker's `PublicObservation`** until activation. The
field name is misleading: it is the life card *in flight*, not a revealed card. §7 requires a dedicated
privacy test for this field.
⇒ The engine's own comment already states the rule (`2501-2502`); the leak is in the **clone/projection**
path, not in the rules implementation.

---

## 5. Projection (`Project(GameState, seat, ledger) → PublicObservation`)

1. **Unknown CardIds unreachable** — an instance neither identified by this viewer nor in a public zone
   carries no CardId.
2. **True InstanceIds unreachable** — hidden instances get **surrogates** (§5.1).
3. **`Battle.RevealedLife` masked** from any viewer who is not an `IdentityViewer` (§4.5).
4. **Private logs redacted** — `PrivateSeat != seat` ⇒ `PublicMessage`, else drop. Never `Message`.
5. **Raw `CommandHistory` removed** or projected to public commands only. (`searchMode`, `GameClone.cs:21`,
   already empties it — but only at `TurnPlanner.cs:225`, not `TurnPlanner.cs:116` or `LegalActions.cs:210`.)
6. **Legally known information preserved** — via `ObservationKnowledge`. The direction a secrecy-only test
   cannot see, and the direction that protects rayleigh.
7. **Counts preserved** — hand size, deck size, Life count are public.

### 5.1 Two token lifetimes — surrogates are NOT ledger keys

Rev 3 conflated them, which does not typecheck against its own rules: a per-decision surrogate
**intentionally changes between decisions**, so it cannot key a **persistent** ledger.

| token | minted by | lifetime | reaches planner? | keys the ledger? |
|---|---|---|---|---|
| `SurrogateId` | `Project` | **one decision**, fresh keyspace, deliberately unrelated across decisions | **yes** | **no** |
| `LedgerToken` | engine/ledger | persistent | **never** | yes |

- `LedgerToken` is opaque, unrelated to referee `InstanceId`, never in `PublicObservation`, and is
  **supplied only when the observer may legally track continuity**.
- **Many hidden movements carry NO trackable token at all** — an unidentified private draw or an ambiguous
  hidden movement is an `ObservedUnidentifiedMove`/`ObservedCountChange`, which have no token field. If a
  viewer cannot legally follow the card, there is nothing to follow it *with*.
- Persistence across decisions in the planner-facing keyspace would be a correlation channel, and legal
  memory does not need it: it lives in `Counts`/`Segments`/`Groups`.
- `Project` builds a bijection `surrogate ↔ authoritative` for instances the viewer may legally
  **target**: public cards and own hand.
- Hidden instances get surrogates **outside** the bijection.
- **A root command referencing a non-bijective surrogate is illegal by construction and must throw** —
  never be silently remapped. This makes "the planner cannot act on what it cannot know" a type-level
  fact rather than a hope.
- The bijection is per-decision and dies with the observation.

---

## 6. Determinization invariants

`Determinize(PublicObservation, seed) → GameState`

1. **Exact decklist equality** — every CardId count equals the list in both directions; totals match.
2. **All facts satisfied** — `Segments` at their semantic anchors in order; `Counts` as lower bounds;
   `Groups` per §6.1; `OwnHand` exact.
3. **Purity** — same `(observation, seed)` ⇒ bit-identical world. Seed derived from
   `(game seed, decision index)`, **never** a shared RNG. Prior bug of exactly that shape:
   `planner-bench --perturb` draws from a locked RNG inside a `Parallel.For`.
4. **Impossible constraints fail loudly — throw.** A silently violated constraint is indistinguishable
   from a weak eval, this project's most expensive historical misdiagnosis.
5. **Shared root worlds** — every candidate in a decision is evaluated against the *same* world set.

### 6.1 Satisfying grouped ambiguity constraints

**Groups must be solved JOINTLY as ONE GLOBAL CONSTRAINT SYSTEM.** Satisfying each group independently is
**incorrect**: overlapping groups constrain each other, and a flat list is acceptable *only* if the solver
treats the whole list as a single system. Physical merging is not required; independent satisfaction is
forbidden.

1. place `Segments` (exact positions);
2. solve `Counts` **and** `Groups` **together** — `AtLeast` is exactly a `Group` with
   `Candidates={cardId}`, `MinRemaining=n`, so **unify the representation** and hand the solver one system;
3. fill the remainder from the unseen pool under `seed`.

Fact counts are single-digit, so **greedy with backtracking over the joint system** suffices; if no
assignment exists, **throw**. Canonicalize identical/subsumed groups first for efficiency.

⚠ Throwing on a *satisfiable* instance is a **bug**, not a safety property — the feasibility check must be
exact. §7 therefore requires (a) a satisfiable-but-greedy-hostile instance that still solves, and (b) an
**overlapping-group case where each group is individually satisfiable but the combination is not**, which
independent satisfaction would silently accept.

---

## 7. Required tests

**Secrecy**
- poison `CardId` **and** `InstanceId` across every zone the viewer is not entitled to.
- genuinely private `EventLog` entry (`PrivateSeat` set, poison-free `PublicMessage`).
- **`Battle.RevealedLife` masked from the attacker** — dedicated test (§4.5). This is the field implicated
  in the defence-lookahead result.
- **a real private-history flow, not the synthetic `playCard`** (an *impossible private action* — 
  `playCard` is public). Use a genuine deck-look selection (`GameEngine.cs:1645`), **or** specify that
  search receives no raw history at all and test that.

**Per-decision information sets (§0.1) — the level a root boundary does NOT reach**
- **THE KEY TEST: before damage, a defender's block/counter action must be INVARIANT across two states
  differing only in their unknown top Life card.** This is the one that fails today and that root
  determinization alone would not fix.
- at the **Trigger** step, the defender's action **may** depend on that card (they now legally know it).
- at that same Trigger state, the **attacker's** observation must **not** contain its identity before
  activation.
- after **UseTrigger**, **both** observations may contain it.
- **PassTrigger / no Trigger** ⇒ moves to hand **without informing the attacker**.

**Life flow (§4.5)**
- damage, card has **no Trigger** ⇒ attacker gains no CardId fact.
- damage, **PassTrigger** ⇒ attacker gains no CardId fact.
- damage, **UseTrigger** ⇒ identity public at activation.
- **Banish** ⇒ identity public via trash.
- **attacker vs defender projections differ** on the same battle state.

**Preservation** (legally known info MUST survive — rev 1 had no test for this direction)
- **OP10-119**: both seats still know it; determinizer places it at Life `Top(0)` = `Life[Count-1]`.
- deck-look `ToTop` vs default-bottom: correct anchor, correct order.
- searched-to-hand: only the searcher knows.

**Invalidation**
- shuffle drops position + instance identity, **retains** `AtLeast`.
- `SearchMode`: taken card known, remainder invalidated.
- known top-deck card drawn ⇒ becomes an opponent-hand count fact.
- **ambiguity group**: X,Y known, one hidden departure ⇒ *"≥1 of {X,Y} remains"*, **not** both forgotten.
- **failsafe accounting**: blanket forgetting increments a counter and flags the match.

**Validity controls**
- exact decklist equality. *(Implemented 2026-07-15: equality both directions + totals. Supersedes the
  stale `actual <= expected` note.)*
- perfect-info discrimination control (`PlanDirect` must differ, else BROKEN ⇒ fail) vs boundary
  blindness (view must match).
- determinizer solves a satisfiable-but-greedy-hostile constraint set (§6.1).
- ⚠ present witness worlds are **multiset-preserving, not observation-equivalent**. Only this model closes it.

---

## 8. Minimal viable version

`ObservationKnowledge` = `OwnHand` + `Counts` + `Segments` + `Groups`; **OpenList**; **K=1**; combined
own unseen pool (§2.4). Covers setup, draw, reveal, search, all deck-look modes, shuffle, the full Life
damage flow, OP10-119.

**Later:** inferential/negative constraints ("they didn't counter ⇒ likely no counter"); `UnknownList`
belief seeded by leader; K>1 shared world sets; recursive belief — **explicitly out of scope**.

### The decklist assumption, and UnknownList's scope
`OpenList` = the opponent's list is known ⇒ unseen multiset = `list − public − facts`. A **generous
information assumption**, **not a bound**. Rev 1 claimed it upper-bounds honest strength; that holds only
for an *optimal* agent. Extra legal information can degrade a flawed search, so OpenList strength may land
either side of UnknownList strength and **must be measured, not assumed**. *(My second
information-monotonicity error in one design cycle — the first was "K=1 is a lower bound".)*

| phase | assumption |
|---|---|
| **1 — research** | `OpenList` is acceptable for validating the observation boundary and re-measuring contamination, **clearly labelled as an assumption**. It does **not** block phase 1. |
| **2 — before human play** | **`UnknownList` is REQUIRED.** Humans do not show you their list. |

⇒ **Never describe the OpenList planner as deployable against humans.** `DeckFingerprint` already asserts
*"Both deck lists are known pre-match (§8.1)"* — true of the harness, **false of the shipped game**.

⇒ **`UnknownList` is not a chore — it is the foundation of the original deck-identity objective.** The
shape is the same one the audit asked for at match start: a **leader/color prior → archetype distribution
→ Bayesian update from observed cards during the match**. That is precisely "deck identity at match
start" and "deck-conditioned strategy", arrived at from the privacy side. Phase 2 should build it as the
deck-belief model, not as a privacy patch bolted onto it.

---

## 9. Performance — **estimates pending measurement**

Determinization runs **once per decision at the search root**; the fixed cost amortizes over the turn.
`Apply` = O(facts affected). `Project` ≈ O(cards) ≈ 100. `Determinize` ≈ O(deck) ≈ 50 + fact placement +
backtracking (bounded by single-digit fact counts).

**Estimated** well under 1% against a 3200-node search — **not measured; `clone-bench` must confirm before
this number is quoted.** The last confident cost inference here ("the ~5ms clone is the bottleneck") was
wrong by ~150x and aimed weeks of work at a 14µs operation.

**The real cost is K**: K worlds multiplies search cost by K or divides the per-world budget by K. That
trade is the experiment, not an implementation detail.

---

## 10. Migration

**Research planner (`Tools/Sim`) — first**
1. Complete the §14 audit. **Zero unclassified mutation sites before honest mode may be enabled.**
2. Land ledger + projection + determinization behind `--perfect-info` (**default ON**, bit-identical to
   today, so every historical measurement stays reproducible).
3. Turn `privacy-test` green.
4. Flip the default; run the 2×2×K: perfect-info @400/@3200; honest K=1 @400/@3200 total; honest K=8
   @3200 total (fixed-compute ⇒ shipping economics) **and** @3200 per world (fixed-depth ⇒ K=8 not
   penalised by 1/8 the depth). Report `(D−C)−(B−A)` with a CI; outright vs adjudicated separately;
   n≥300/cell; never read a single deck row out of it.
5. **Re-measure the defence lookahead** (§0.1) — its +15pp is contaminated by `RevealedLife`.

**Shipped Advanced bot (`Assets`) — after, separate job**
- The Unity clone (`Assets/.../Search/GameClone.cs:52`) has no `searchMode` and copies everything.
- The shipped Advanced tier is **still the old passive `SearchBot`** — players have never faced the
  planner. Replacement is gated on honest strength.
- ⚠ Against a **human**, `OpenList` is unavailable. `DeckFingerprint` already asserts *"Both deck lists
  are known pre-match (§8.1)"* — true of the harness, **false of the shipped game**.

---

## 11. Worked examples

**1. OP10-119 — revealed card into facedown Life.** `Reveal{IdentityViewers=all}` then
`Move{From=(south,Hand), To=(south,Life,Top(0)), Position/MovementViewers=all}`. Both observations get
`Segment{south, Life, Top, [OP04-031]}`; determinizer places it at `Life[Count-1]`. **No facedown-Life
special case exists.**

**2. Privately drawn card.** `Move{From=(north,Deck,Top(0)), To=(north,Hand), IdentityViewers={north},
MovementViewers=all}`. South's `ObservedKnowledgeEvent` has **no `CardId` field at all** — not a field it
is trusted to ignore. South records `OppHandSize += 1`.

**3. Publicly revealed search card into opponent's hand.** `SearchMode` deck-look, revealed on take ⇒
`IdentityViewers=all`. South gains `AtLeast[(north, Hand, X)] = 1`; remainder shuffled ⇒ §4.3 fires.
South knows one card in North's hand **as a count fact**, never as an instance.

**4. Known top-deck card drawn.** South holds `Segment{north, Deck, Top, [A,B]}`. North draws ⇒ pop `A`;
segment becomes `[B]`; South gains `AtLeast[(north, Hand, A)] += 1`. Deck knowledge **converts into** hand
knowledge; nothing invented, nothing lost.

**5. Deck-look, top vs bottom.** `ToTop` ⇒ `Segment{Deck, Top, [ordered]}`. Default ⇒
`Segment{Deck, Bottom, [ordered]}`. Same UI flow, **opposite anchors**.

**6. Shuffle invalidation.** `Shuffle{(north,Deck)}` ⇒ all viewers: `KnownTop/Bottom` cleared;
`IdentityOf` dropped for every instance in the zone; `AtLeast` **retained**. South still knows "north's
deck contains an X"; not where, not which instance.

**7. Ambiguous hidden hand movement ⇒ grouped constraint.** South holds `AtLeast[(north,Hand,X)]=1` and
`AtLeast[(north,Hand,Y)]=1`. North places 1 card from hand on the bottom of their deck; South sees the
movement, not the identity.
⇒ `Group{ Source=(north,Hand), Candidates={X,Y}, HiddenDepartures=1, MinRemaining=1 }`.
South still knows **at least one of X or Y remains** — rev 2 forgot both. South does **not** add a
compensating deck fact; the referee is not consultable.

**8. 🚨 Damage with no Trigger.** North attacks; South takes 1 damage; the life card has no Trigger.
`Move{From=(south,Life,Top(0)), To=(south,Hand), IdentityViewers={south}, MovementViewers=all,
PositionViewers=all}`. North's observation: Life −1, hand +1, **`Battle.RevealedLife` masked, no CardId**.
North gains **no** count fact. **This is the channel that inflated the defence-lookahead result.**

---

## 12. Question log

### ✅ RESOLVED (rev 4) — kept so they are not re-litigated
| question | ruling |
|---|---|
| Group merging | **Solve overlapping groups JOINTLY as one global constraint system** (§6.1). Physical merging optional; independent satisfaction forbidden. Canonicalize identical/subsumed groups. |
| Failsafe budget | **No `k` threshold.** Any match invoking blanket forgetting **even once** is excluded from headline honest results and reported separately. Material incidence ⇒ the experiment is **incomplete**, not noisy (§4.4). |
| `UnknownList` scope | **Phase 2, required before human play; does NOT block the phase-1 OpenList validity experiment.** And it is the foundation of the deck-identity objective, not a privacy patch (§8). |
| Own facedown Life at setup | **Unknown to both players**, including the owner — CR §3-10-2 (§4.1). |
| Surrogate lifetime | **Per-decision, deliberately unrelated across decisions**; a separate engine-private `LedgerToken` keys the ledger (§5.1). |
| Audit sequencing | **The audit GATES coding.** A funnel cannot protect sites that bypass it (§13). |

### ⏳ STILL OPEN
1. **Transition grouping** — one card movement is typically *source removal + `Zone` assignment +
   destination insertion*: three syntax sites, **one** `KnowledgeEvent`. The `transition_id` layer (§14.6)
   is specified but **not implemented**; without it the implementation may emit the same knowledge event
   three times. What is the right grouping key — the enclosing statement, or an explicit engine helper?
2. **Classification authority** — 308 sites need identity/movement/position rulings. Which are decidable
   from code + CR, and which need a human ruling? (The two rules errors this cycle, §3-10-2 and §10-1-5,
   were both cases where the engine was right and the spec guessed.)
3. **`.Clear()` semantics** — **8 sites, not 6** (`102` Hand, `1355` Trash, `1724` Deck, **`1863` and
   `1887` `DeckLookState.Cards`**, `5315` Hand, `6327` Life, `6427` Hand). ⚠ *Rev 4 said 6 — that was v1's
   number written into a section describing v2's output; the two `DeckLook` sites were invisible to v1.*
   **Codex's ruling: a `.Clear()` is normally SUPPORTING implementation detail inside the surrounding
   move/search/shuffle transaction, not its own knowledge event.** It therefore inherits the enclosing
   transition's ruling and must not be classified independently.

---

## 13. Audit-first sequencing

Rev 2 proposed the funnel as the safety net and the audit alongside. **That is backwards.** The
`KnowledgeEvent` funnel protects only sites routed *through* it; a legacy direct list mutation that never
learned about the funnel is invisible to it and leaks silently. **The audit gates coding.**

---

## 14. Mutation-site audit plan (deliverable before implementation)

### 14.0 STATUS: EXPANDED INVENTORY — COMPLETENESS UNPROVEN

**Not "inventory complete".** Three analyzer versions, two of which shipped a completeness claim while
blind:

| ver | blind to | symptom |
|---|---|---|
| v1 | helper-parameter mutation | `ShuffleInPlace`/`Shift`/`Pop` = **0**, while a grep in the same session said 7 |
| v2 | **object initializers** | **`GameClone.cs` = 0 rows** — the file whose verbatim `Deck`/`Hand`/`Life` copy IS the leak |
| v3 | *(unknown — that is the point)* | alias tracking is **global and flow-insensitive**; helper summaries handle **bare parameters only** |

**v3 adds:** object-initializer detection, **15 executable analyzer fixtures** (`--test`), and **probe
ASSERTIONS with non-zero exit** (`--check`) — v2 only *printed* its probes, so it could regress to zero
and still exit 0. **`GameClone.cs` is itself a probe now.** Completeness is claimed only when the
fixtures pass *and* every known privacy carrier appears — and even then it is a claim about the forms
tested, not a proof.

### 14.0.1 ⛔ THE A/B INVENTORY SPLIT IS DEAD — privacy is not a property of a filename

**Removed 2026-07-15.** The `A:zone-transition` / `B:identity-reachability` table (334 + 18) assigned
privacy relevance **from the filename**, which is architecturally wrong: *gameplay-transition* and
*privacy-reachability* are **overlapping properties of a SITE**, not mutually exclusive buckets of a file.
It mislabelled every `GameEngine` write to `PendingEffect`/`ChoiceState`/`Selected` as a "zone
transition", so **"A = 334 gameplay movements" was false**. Do not reintroduce it.

**The replacement is a division of INSTRUMENTS, not of files:**

| tool | question | mechanism | enumeration? |
|---|---|---|---|
| `Tools/Audit` | which **authoritative transitions** must move onto the ledger funnel? | static, emits only from `GameEngine.cs` | **bounded migration aid; completeness enforced only after encapsulation** (§14.7) |
| `PrivacyTest` | is any secret **reachable / inferable** from what planning receives? | **runtime** poison scan + paired-world noninterference + typed two-layer boundary | **no static privacy-field whitelist** — the reachability walk enumerates no fields, but the poison and decision fixtures still enumerate the threat model |

⚠ Emitting only from `GameEngine.cs` is **source scoping, not filename privacy**: the analyzer still parses
every file for symbols and call-graph summaries, but a `GameClone` copy is clone plumbing, not a semantic
transition, and must never enter the classification table. **`PrivacyTest` proves the `GameClone` copies'
reachability at RUNTIME** — no static "signature assertions" were moved out of `Tools/Audit` (none exist);
`PrivacyTest.ReachabilityNote` is an orientation comment, and the poison scan is the actual proof.

**Current artifact: 324 transition candidates, all from `GameEngine.cs`, `GameClone` = 0.**
Best-effort, for migration. **Not a privacy proof and not claimed complete** — completeness becomes
durable only by **encapsulation** (§14.7), never by a cleverer analyzer.

### 14.7 The durable fix: encapsulation, not detection

Four analyzer versions were spent trying to **detect** what should be made **impossible**. After
migration, **make the zone-mutation surface `private`/`internal` and route it through the funnel, so a
future bypass FAILS COMPILATION.** That is what turns completeness from a permanent analyzer-maintenance
burden into a property of the type system.

### 14.1 Historical counts (the cautionary record, not history)

| target | v1 | **v2** | |
|---|---|---|---|
| *(`Zone` string)* | 95 | 95 | |
| Trash | 51 | 51 | |
| Hand | 46 | 46 | |
| **Deck** | 26 | **41** | +15 via `Shift`/`ShuffleInPlace` |
| **Life** | 13 | **24** | +11 via `Pop` |
| CharacterArea | 21 | 21 | slot array — indexer only |
| CostArea | 14 | 14 | |
| **`DeckLookState.Cards`** | **0** | **7** | v1 could not see it |
| Stage | 5 | 5 | |
| **`BattleState.RevealedLife`** | **0** | **2** | **the headline leak** |
| **`DeckLookState.Ordered`** | **0** | **1** | |
| **`GameState.Selected`** | **0** | **1** | |
| **TOTAL** | 271 | **308** | |

**Mutating helpers discovered by fixpoint:** `Shift(param 0)`, `Pop(param 0)`, `ShuffleInPlace(param 0)`
⇒ **6 `ShuffleInPlace` call sites** — consistent with the earlier grep's 7 occurrences minus the
declaration. **That consistency check is the one v1 failed to run**, and v2 prints these counts every run
as a standing regression probe.

### 14.2 The method is NOT regex — and here is the receipt

**`CharacterArea: 0` was the finding, not a result. RESOLVED: `CharacterArea` is a fixed-size SLOT ARRAY**,
mutated exclusively by **indexer assignment** — `p.CharacterArea[openSlot] = instance` (`1402`),
`pDef.CharacterArea[di] = null` (`2707`). **There is no `.Add()` to find.** 21 sites, every one
structurally invisible to a mutator-call sweep. A regex inventory would have shipped a boundary with an
entire zone blind — and the board is where the game is played.

**Honest accounting of the regex-vs-semantic delta** (the method deserves credit only for what it earned):
- **21 `CharacterArea` + 7 whole-list replacements = 28 sites a mutator-call regex CANNOT see**, ever.
- `CostArea` (14) and `Stage` (5) were missed because I never grepped for them — **my sampling error, not
  the method's limit**. A tool that enumerates zones from the type cannot make that mistake.
- Deck 26 / Hand 46 / Life 13 / Trash 51 reproduce the regex counts **exactly**, which is the control that
  says the tool is not inventing sites.

**Verified clean:** all 7 whole-list replacements are `Stage` single-card assignment and `CostArea` DON
filtering — **no unrouted shuffle**, which was the live risk.
**Flagged for ruling:** the 6 `.Clear()` sites are high-information events — `p.Deck.Clear()` (`1724`,
inside the search flow), `owner.Hand.Clear()` (`5315`), `oppRs.Hand.Clear()` (`6427`),
`owner.Life.Clear()` (`6327`). Clearing and refilling a hand is a large knowledge transition and none of
them is obviously classifiable from code alone.

⇒ The audit must be **reference-driven and REPRODUCIBLE** — a **checked-in Roslyn tool**, not a one-time
manual IDE pass. A manual pass cannot be re-run against future card-effect work, and this boundary's whole
value is that it keeps holding.

**`Tools/Audit` (read-only)** — Roslyn over `Assets/Scripts/Engine`, with **interprocedural mutation
summaries**: find methods that mutate their collection parameters, propagate to a fixpoint through the
call graph, then attribute at call sites.

⚠ **A semantic model alone is NOT alias-complete, and v1 proved it.** v1 matched only
`receiver.Mutator(...)` and reported `ShuffleInPlace: 0`, `Shift: 0`, `Pop: 0`,
`Battle.RevealedLife: 0` — all false, because those pass the zone as an **argument**
(`GameEngine.cs:9618/9657/9665`). It was shipped claiming to reach mutations "through any path" **while a
grep in the same session had already printed `ShuffleInPlace calls: 7`**. The cross-check was applied only
to the four zones that happened to pass. **Do not restore any "any path" claim without a probe that
fails when the tool is blind.** v2 prints the v1 blind spots as a standing regression check.

What it must catch:

| must catch | why regex misses it |
|---|---|
| method calls that mutate (`.Add/.Insert/.Remove*/.Clear/.AddRange/.Sort/.Reverse`) | only the easy case |
| **indexer assignment** (`p.Hand[i] = c`) | no `.Add(` token |
| **helper aliases** (`var z = p.Deck; z.Insert(…)`) | the mutation names no zone — **this is the likely `CharacterArea: 0` explanation** |
| **whole-list replacement** (`p.Deck = newList`) | not a list method at all |
| **zone-field assignment** (`c.Zone = "hand"`) — 95 sites | the cross-check |
| **moves that do NOT change the `Zone` string** (reorder within a zone, shuffle) | invisible to a `Zone =` sweep |

**Cross-check, both directions:** every `Zone = "…"` must correspond to a classified movement, **and**
every classified movement must be reachable from a real mutation site. A discrepancy either way is an
unrouted mutation. Neither sweep alone is sufficient — the reorder/shuffle case has no `Zone` write, and
the alias case has no zone name.

### 14.3 Classification schema — one row per site

| column | values |
|---|---|
| site | `file:line` |
| zone / direction | e.g. `Life → Hand` |
| `Kind` | Move / Reveal / Shuffle / Setup |
| `IdentityViewers` | all / {owner} / {searcher} / none |
| `MovementViewers` | all / … |
| `PositionViewers` | all / … |
| semantic anchor | `Top(n)` / `Bottom(n)` / Unordered |
| routed? | does it go through the funnel after implementation |
| rules cite | CR § where visibility is non-obvious |

### 14.4 Gating criteria

- **Zero unclassified zone-mutation sites** before `--perfect-info` may be defaulted OFF.
- Every `Zone = "…"` assignment maps to a classified site.
- `CharacterArea` accounted for (14.2).
- Every row with non-obvious visibility carries a CR citation — **the two rules errors this cycle
  (§3-10-2, §10-1-5) were both cases where the engine was right and the spec guessed.**

### 14.6 Raw sites ≠ semantic transitions (specified; NOT implemented)

**One card movement is typically three syntax sites and ONE `KnowledgeEvent`:** source removal + `Zone`
assignment + destination insertion. Without a grouping layer the implementation will emit the same
knowledge event up to three times, and the 95 `Zone` assignments will double-count against the 213 zone
mutations.

Keep the **raw inventory** as ground truth; add a `transition_id` layer over it:

| field | meaning |
|---|---|
| `transition_id` | groups the syntax sites of one semantic movement |
| source → destination | semantic zones |
| supporting sites | every `file:line` that implements it |
| event kind | `Move` / `Reveal` / `Shuffle` / `Setup` |
| identity / movement / position viewers | the three axes (§3) |
| semantic anchor | `Top(n)` / `Bottom(n)` / Unordered |
| rules cite | CR § where visibility is non-obvious |
| routed | goes through the funnel post-implementation |

**Current state: every CSV row reads `transition_id = UNGROUPED`.** This is open question §12.1.

### 14.5 Deliverable

`Tools/Sim/docs/zone-mutation-audit.md` — the table above, plus an explicit list of sites whose
visibility could not be determined from code + rules and that need a ruling.
