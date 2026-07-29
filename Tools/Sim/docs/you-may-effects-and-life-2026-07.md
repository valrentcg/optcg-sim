# "You may" effects and Life mechanics — 2026-07-28/29

Worked from one brief, repeated over many iterations:

> work on resolving you may effects so nami, kalgara, and a host of other cards resolve
> properly. The user should be prompted to make a decision on if they'd like to use effect
> and IF they do use it, the effects should all resolve properly. I also want you to
> double/triple check that flip life mechanics are working properly, heal, and life
> re-arranging mechanics as well

## Start here

```
dotnet run --project Tools/Sim/Sim.csproj -c Release -- gate
```

**55 suites, ~6s, exit 1 on any failure.** Run it after any engine change. It deliberately
excludes `smoke` (statistical, not pass/fail) and the pure reporting sweeps.

## Engine defects found and fixed

| # | Defect | Reach |
|---|---|---|
| 1 | Cost-prefix effects offered **Skip alone, nothing clickable** — the panel asked `EffectHasValidTarget`, which is the wrong question while a cost is still unpaid | 512 clauses |
| 2 | **Top-of-Life is literal**: "turn 1 card from the TOP face-up" was scanning *past* a face-up top card to find a face-down one lower down | Wyper, Nami, 15+ |
| 3 | Auto-skip gate counted a **DON!! cost against the Character area**, retiring the effect before the player was asked (OP14-049 Jinbe) | 117 cards |
| 4 | Counter events granted their **power bonus for free** — the `+N` was grepped out and applied flat, ignoring the cost, and the clause was then blocked from queueing *because* it contains "gains +N" | 13 cards |
| 5 | Reveal-from-hand costs **auto-selected** the card, **logged only a count** (a reveal nobody can see is not a cost), and read `{A} or {B}` as `{A}` | 58 disjunction clauses |
| 6 | **"up to N" read as "exactly N"** — the clause was retired whenever fewer than N targets existed, so OP12-038 rested 2 DON!! and did nothing against one legal victim | 917 cards carry the wording |
| 7 | A **compound cost** reached across "and" to borrow the word *Character* from the other conjunct (OP10-028: "rest 2 of your DON!! cards AND trash this Character") | — |
| 8 | **PvP: the opponent could resolve or skip YOUR pending effect** by omitting the effect id — `FindPendingEffect`'s no-id fallback ended in `PendingEffects[0]`, whoever queued first | every optional effect |
| 9 | `GameClone` silently dropped **20 fields** across two hand-written copies, incl. `DeferredRemovals` and `OnceKey`. Measured: turn-state was non-empty in **12.9%** of search clones | bot search, Sandbox undo, puzzle solver |
| 10 | Protection discards took `Hand[Count-1]` — the engine chose which card you paid with | 6 cards |
| 11 | **"Your opponent may X. If they do not, Y" never asked the opponent** — the clause fell through to whichever generic handler matched Y, so the penalty fired unconditionally and the "may" was inert, always in the controller's favour | OP05-099, OP15-059 |
| 12 | "Return N of your **active** DON!! cards to your DON!! deck" existed only as a *cost*; as an effect body it resolved to nothing | same 2 cards + any future body use |
| 13 | **"Your opponent chooses 1 card from your hand" auto-picked** `Hand[Count-1]` and logged "opponent chose …" — the engine deciding on a player's behalf, same shape as #10 | OP01-038 |
| 14 | **The opponent's own discards were auto-picked too** — "your opponent trashes 1 card from their hand" / "… places 1 card from their hand at the bottom of their deck" took `Hand[Count-1]` at 5 sites. The controller cannot legally choose (3-4-3: you cannot view the other player's hand), so the owner must — and a discard is only ever as bad as the card you give up | **25 cards** |
| 19 | **My own "if they do not" fix never fired when the opponent COULDN'T pay** — I argued the retire sweep would catch it. It only detects missing CHARACTER targets, so a clause wanting a Life card or DON!! is never unresolvable to it. The opponent held a prompt they could only Skip, and a controller who had rested a Character got nothing unless they pressed it | OP05-099, OP15-059 |
| 17 | **My own trigger fix let a DRAWN card pay the cost** — it plays the card then queues the discard, so "Play this card. Then, draw 1 card." put a fresh card in hand before the pick. Rule 8-4-1-3 pays costs first. Fixed with an eligibility snapshot enforced in BOTH the glow filter and the resolver | OP08-104 + the top-or-bottom-Life bodies |
| 18 | `GameClone` dropped `PickedInstanceIds` and `CostPaidRefs` (pre-existing, found while adding the new field) — a rollout restarts a pick with a clean slate and can re-spend a card the real game already spent | bot search, Sandbox undo, puzzle solver |
| 16 | **`[Trigger]` costs auto-trashed `Hand[0]`** — "[Trigger] You may trash 1 card from your hand: Play this card." The Trigger press answers *whether*; nothing ever asked *which*. Invisible to every sweep here, which all enumerate `effect` while these clauses live in the separate `trigger` field | **44 cards** |
| 15 | The place-at-deck-bottom half had **no skip enforcement** — once it became a prompt, declining it was a free escape (found by testing the fix, not the code) | 8 of those 25 |

Also: `"You may"` protections now prompt unconditionally (a decision, not a setting — the
per-seat flag and its UI toggle were deleted), and the Use button **names the cost** rather
than saying "Use Effect".

## What is now asserted, pool-wide

- **Prompted** — `optionalfires`: 704 optional clauses, **0** fire without asking. Deleting the
  opt-in guard turns it red with 127.
- **Resolves** — `paidfornothing`: if the engine takes payment it must give an effect or a
  prompt. **0** across 451 unconditional-body clauses.
- **DON!! gates** — `donthreshold`: [DON!! xN] is a gate, not a trigger — 42 you-may clauses
  sit behind one. Shut on N-1, open on N, both directions.
- **Once-per-turn gates** — `onceperturn`: the other gate hiding behind a timing tag, 27 you-may
  clauses. Runs the first time, refuses a second use in the same turn, reopens next turn. The
  third case matters on its own: without it, a gate that closed *permanently* would still pass,
  quietly turning the card once-per-game.
- **Saying no** — `optionalonce`: declining a `[Once Per Turn] You may ...` must NOT burn the
  turn's use, and using it must. This is a *second* implementation of once-per-turn (a deferred
  `OnceKey` committed only on resolution) alongside ActivateMain's immediate one — two
  implementations of one rule is the recurring bug class here, so both are now pinned.
- **Who is asked** — `opponentdecides`: seven cards hand the decision to the OTHER player
  ("Your opponent chooses one:", "Your opponent may ..."). The prompt must route to them, the
  controller must not be able to answer it — and the options still resolve in the CONTROLLER's
  frame, because the texts are written from their side. Resolving in the chooser's frame inverts
  the card (ST07-010's option A eats the controller's own Life) and logs plausibly either way.
- **Nested opt-ins** — `opponentbranch`: south opts in, then NORTH gets their own optional
  decision, and declining costs them. The two branches must be mutually exclusive — paying AND
  taking the penalty is the card doing double duty; taking neither means the controller rested a
  Character for nothing. Also pins the active/rested qualifier on the DON!! return, which the
  shared DON!!-paying helper gets backwards by design (it prefers rested).
- **They pick, you pay** — `opponentpicks`: OP01-038's opponent chooses, but the card leaves the
  CONTROLLER's hand. Now a real pick on the chooser's seat, routed into the existing
  click-a-card-in-the-opponent's-hand path rather than a second implementation of it. Blind by
  rule (3-4-2 hand is a secret area; 8-4-4-2 no guaranteed information), which costs nothing to
  honour since the UI already renders the opponent's hand as face-down holders by index.
  Mandatory: skipping still costs the controller a card.
- **Your own discard is your choice** — `selfdisposal`: the 25-card mirror of `opponentpicks`.
  The player who owns the cards is asked which one goes, for both wordings; mandatory, so a skip
  still costs them. One shared helper feeds both call sites — two implementations of "the
  opponent disposes of their own card" is the drift this engine keeps producing.
- **Nobody chose that** — `autopick`: the class-level instrument for the five auto-pick defects
  above, which were all found one at a time by reading. Drives 1031 distinct selective clauses and
  asks whether any took cards from a zone holding MORE candidates than it took, without raising a
  prompt. **0.** Restoring any one of the fixed auto-picks turns it red (4), which is the only
  reason the zero means anything — a sweep that cannot fail reads as coverage and is worse than
  none. Ratcheted, not gated on zero: some auto-picks are legitimate.
  Covers hand, trash and the **board** for both seats. The board zone was added after the first
  clean run and controlled separately (forcing the K.O. resolver to auto-pick a victim reports 16)
  — a widened check that matches nothing looks identical to a widened check that found nothing.
- **Which end of Life** — `lifeend`: 46 cards read "from the top **or bottom** of your Life
  cards", and this is the one selective wording `autopick` cannot see — it filters "top of"/
  "bottom of" as positional, which is right for the ~200 clauses naming ONE end and wrong for
  these. A real decision: Life damage comes off the top, so the end chosen decides whether the
  player keeps their next `[Trigger]`. Both the cost form and the body form are driven, because
  they are separate handlers, and each one's cases go red independently under control.
- **Is it clickable?** — `crossglow`: every prompt this workstream added sits on a seat that does
  not control the source, and `glowsweep` cannot reach any of them — it drives each card's text on
  the CONTROLLER's seat, so an opponent-owned decision is a state it never constructs. Three of the
  new prompts are mandatory, where nothing clickable is a frozen game rather than an annoyance.
  Asserts not just that something lights up but that it belongs to the right player, since half of
  them deliberately point at the other player's hand. Restricting glow to the acting seat's own
  cards reddens exactly the cross-seat case.
- **The cards actually named** — `namedcards`: six of the nine distinct "you may" Nami cards plus
  Kalgara, driven one at a time through a real play. Everything else here is shape-driven, which
  has a known blind spot: a class fix is proven by the cards it touched, and only two Namis had
  ever been looked at individually. Picked for being awkward in different ways — a SELF-HARM cost
  (give your own Leader -5000), a VARIABLE cost ("1 or more DON!!"), a keyword-filtered cost
  (trash a card *with a [Trigger]*, asserted in BOTH directions), a compound rest cost, and
  Kalgara's deck-look. All six pass.
- **Every `[Trigger]` "you may", swept** — `triggerfield`: the `autopick` oracle applied to the
  population `autopick` structurally cannot reach. All **42** cards are put on top of a real Life
  stack, actually damaged, and their Trigger actually pressed — 42 fired, 34 raised a decision,
  **0** took hand cards without asking. The 8 silent ones are DON!!-cost triggers, where the cards
  are fungible. Restoring the `Hand[0]` auto-pick reports **14** and breaks the ratchet.
  The shortcut of feeding `def.Trigger` into the existing sweeps was rejected: they queue clauses
  as main-timing, and that invents a question that never existed (their own comment says so).
  Now runs BOTH oracles: took-without-asking **0**, and paid-a-cost-for-no-payoff **0**. Each is
  separately controlled — restoring the auto-pick reports 14 on the first, suppressing the card
  placement reports 14 on the second.
- **`[Trigger]` costs** — `triggercost`: 42 "you may" clauses live in the `trigger` DATA FIELD,
  which **no sweep here reads** — all five enumerate `def.Effect` (checked, not assumed). This is
  where the brief's two halves meet: a [Trigger] fires only when a Life card is dealt as damage, so
  each is a "you may" decision taken *during* the life mechanics. Six cases covering both answers
  to the Trigger itself and both answers to the cost pick, incl. that the card the player NAMES is
  the one trashed.
- **Dispatch** — `timingsweep`: 10 timings, ~600 clauses driven on their *real* trigger.
- **Retire predicate** — `retiresweep`: diffs it against itself (retirement on vs off).
- **Seat** — `wrongseat`: 20 commands incl. every battle step; none accept the wrong seat.
- **Targets** — `illegaltarget`: 6,790 illegal-target attempts, none touched the card.
- **Life** — flip (both directions, and *all* at once), heal, take, trash, reveal,
  top-or-bottom, re-arranging, battle damage, `[Trigger]` both answers, `[Double Attack]`,
  `[Banish]`, the **opponent's** Life (trash from it; a card taken goes to *their* hand),
  "trash until you have N", life-count conditions in both directions, and the boundaries
  (empty deck, empty Life, single card).

  This line originally claimed less carefully. Enumerating every Life-bearing sentence in the
  pool — 236 distinct shapes — showed four the suites had never touched, which `lifeshapes`
  now covers. Auditing a coverage claim is not the same as making one.
- **Costs** — `costshapes`: the four shapes the suites never drove, found by enumerating all
  121 distinct cost shapes in the pool — place-from-trash-to-deck-bottom (15 cards), mill as a
  cost (7), return-DON!!-to-deck (6), Life-trash as a cost (5). Each asserts the specific zone
  moved by the specific amount, with an unpayable control.
- **Real plays** — `realplay`: five more cards driven through an actual `playCard` (Flampe,
  Gordon, Rob Lucci, Megalo, Koby), spanning different cost/body pairs. Everything else queues
  a clause directly, which skips the card's own cost and the [On Play] dispatch.
- **Bodies** — `bodyoutcome`: exact outcomes for the two largest body shapes of 161 —
  "K.O. up to N of your opponent's Characters" (54 clauses) and "give -N power" (37). Names
  the victim and checks that card: the targeted one leaves, the bystander stays, the drop is
  exactly N, and a cost ceiling spares a cost-5 body while still taking a cost-1 one.
- **Bot** — every prompt added here is answerable by the AI; a hung solo game is the failure.

## How much to trust the numbers below

Two kinds of claim appear in this document and they do **not** deserve equal weight.

**Test-backed claims** come from the 55 gated suites. Each was negative-controlled — the fix was
broken and the suite confirmed to go red — and each re-runs on demand in ~6s. Counts of clauses,
cards and shapes come from enumerating the card pool, which is reproducible. Treat these as solid.

**Reasoned claims** are the most dangerous category and did not originally have a heading here.
Twice now a fix shipped with a justification in place of a test — "the outcome is identical", "the
retire sweep already covers this" — and both were wrong, found only by going back and testing the
sentence. If a fix argues that something is handled elsewhere, that argument is a test case.

**Instrumentation-backed claims** are the "does it matter in play" figures: temporary counters
added to the engine, run over bot games, then reverted. Treat these as indicative only. Across
four rounds I got three of them wrong:

1. Counted a code path being *taken* and reported it as impact (17,330 -> the real figure is 790).
2. Corrected that to **0** from a four-deck sample; all 41 decks say **790**. A narrow zero is not
   absence.
3. Inferred an engine defect from a counter reading zero; two tests written against that exact
   scenario both pass, and the defect does not exist. The counter remains unexplained.
4. Read `[Trigger]` off the `effect` string and concluded three fixture cards lacked one. It is a
   SEPARATE data field: ST29-004 and ST29-009 both print a [Trigger] their effect text never
   mentions. That made a "the cost is unpayable" fixture full of legal payers and produced a
   confident, wrong defect report for about ten minutes. Check the field the engine reads.

The engine was right in all three. The instrument was wrong in all three. Where a number here
matters to a decision, re-derive it — and prefer writing a test, which in this workstream has
been reliable in a way ad-hoc counters have not.

## Do the fixes matter in real games?

Measured, and it took three attempts to get right. The numbers below are the third and the
only ones I stand behind — arrived at over **6,724 bot-vs-bot games across all 41 imported
meta decks**, counting when a fix **CHANGED A VERDICT** rather than when its code path ran:

| fix | path taken | actually changed the outcome | per game |
|---|---|---|---|
| "up to N" is a ceiling (#6) | 47,090 | **790** | ~0.12 |
| compound-cost / noun-phrase guard (#3, #7) | 14,600 | **4,290** | ~0.64 |
| counter valuation restored to the AI (#4) | — | **2,723 plays** (was 0) | ~1.3 |

**How I got it wrong twice, because the method matters more than the numbers.**

First I counted the up-to-N code path being *taken* — 17,330 — and reported it as "~8 rescued
per game". A counter on a code path measures the path. The ceiling only changes anything when
the candidate count falls BELOW the printed N while staying above zero, which is far narrower.

So I corrected it with a paired A/B: same seeds, fix on and off. Identical win rate to the
digit, and a precise verdict-counter reading **0**. I published that as the correction.

That was also wrong. It ran on four decks. Across all 41 the same counter reads **790** — the
fix does fire, roughly once every eight games. The four-deck zero was coverage, not absence,
which is the same trap as `disjunctionsweep` reporting clean on a path it could not see.

The order of the errors is the useful part: overstated, then over-corrected, and only a broad
sample settled it. A zero from a narrow sample is not evidence of absence, and neither is a
large number from a path counter evidence of impact.

### Two more measurements, one of them a non-result

**The bots do use optional effects.** Over the same 6,724 games: **85,823 used, 42,664 skipped**
— a 67% use rate. Worth knowing, because every prompt this session added is one the AI must
answer, and an AI that reflexively Skips would make solo play weaker while leaving every test
green.

**The top-of-Life fix (#2) is unmeasured in play, and I could not make it measurable.** Its
cost path was never reached in 6,724 games — not once — even though six of the 41 decks contain
a card that uses it, including OP15-114 Wyper (the original report) and EB03-053 Nami in five
decks. The bot plays optional effects two thirds of the time, so blanket Skipping is not the
explanation; the cards are simply never played-and-used in this sample, or the cost reaches
resolution by a route the counter did not sit on.

Recorded as a gap rather than resolved. That fix rests on `lifefaceup` (8/8) and `lifeadvanced`
(9/9), which is unit evidence and not play evidence — the same distinction that made the up-to-N
numbers wrong twice. It is also, notably, the fix closest to what was actually reported, so its
real confirmation is the Play-test, not this harness.

## Did any other fix break a consumer?

The counter regression had a shape worth generalising: a rules fix changed what a function
RETURNS, and broke a caller that meant something different by it. So the same question was
asked of every function this session touched.

Consumers of the changed internals (`TryAutoPayCost`, `ClauseHasNoLegalCharacterTarget`,
`FindPendingEffect`, `CostCardMatches`, `CounterPowerCore`) all live inside GameEngine and are
the paths already covered above.

The bots reach the engine through 19 public members. Diffing the whole session against the last
pushed commit (235 insertions, 31 deletions in GameEngine.cs) and grepping that diff for each:

**`GetCounterPower` is the only one whose lines changed** — the regression above, now fixed.
The rest of the bot-facing surface is untouched, so no other consumer can have silently
changed meaning.

## Method notes that earned their place

**Negative-control everything.** Every "0 failures" in this document was checked by breaking
the fix and confirming the number moves. Several checks *survived* their control and were
therefore demoted from gate to report (`glowsweep`, `disjunctionsweep`) — an assertion that
cannot fail is worse than none, because it reads as coverage.

**Ask which PATH the instrument touches.** `disjunctionsweep` reported 24/24 clean on
`{A} or {B}` while the reveal bug sat on the resolver path it structurally could not see.

**Command echoes are not outcomes.** `"South counters with X"` is logged whether or not the
effect fires; counting it put `[Counter]` at a fake 100%. Excluding it exposed 13 real bugs.

**Fixtures quietly test your model of the engine.** Hand-setting `Battle.PendingLifeDamage`
tested my simulation of `[Double Attack]`, not the keyword. Use a card that has it.

**Escape sequences leak as invisible control bytes.** `\b` written through a shell heredoc
becomes `0x08`, renders as nothing, and the regex silently never matches. 15 of them made an
auto-pick detector inert, and I reported its vacuous zero as a result. Gate: `-- hygiene`.

## A lead that did not survive its own test

Last round I documented an OPEN LEAD: over 2,880 games, 72 top-of-Life-flip effects were
resolved, all with a target, and a counter placed on the cost-prefix block in `ResolveEffect`
read **zero**. The inference was that a supplied target diverts the effect before its cost is
paid — plausible, precisely localised, and wrong.

Writing the test first is what caught it. Two cases now in `lifefaceup` resolve a cost-prefixed
effect **with a target supplied**, one per clause shape that matches the counter's text:

- OP15-114 Wyper `[On Play]`, resolved with an opponent Character as the target
- EB03-053 Nami `[On K.O.]`, resolved with a hand card as the target

Both pay the cost — the top Life card is turned face-up in each. The defect I inferred does not
reproduce.

What remains unexplained is the production counter reading zero while the cost is demonstrably
paid here. That is a gap in my instrumentation, not a known engine defect, and it is recorded
as such rather than left standing as a bug report against the engine.

**Both tests are kept.** They cover a state no other suite could produce — every other suite
resolves these effects with a null target, exactly as the UI does — so whatever the counter was
measuring, the target-supplied path now has explicit coverage it lacked.

## NOT verified — needs a Play-test

Everything above is headless. Two changes are Unity UI and only type-checked:

1. The **Use button label** now names the cost ("Turn 1 card from the top of your Life cards
   face-up"). Never rendered.
2. The **protection discard** now raises a card-pick prompt *mid-K.O.* — a new interaction
   with no headless coverage of its presentation.

The reported bugs came from build 1.0.27, which contains none of these fixes.
