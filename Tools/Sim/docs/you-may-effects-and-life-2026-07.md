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

**66 suites, ~6s, exit 1 on any failure.** Run it after any engine change. It deliberately
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
| 28 | **"Look at all of your OPPONENT'S Life cards and place them back in any order" had no handler** — EB01-052's Choose-one offered a real option and a no-op. Found by the choice differential; my own `liferearrange` test had passed it vacuously, because "north's cards intact and hidden" is exactly what an unimplemented effect produces | EB01-052 |
| 27 | **"Place 1 at the top of your deck and place the rest back" dropped its first half** — ST13-016 and ST13-004 look at all your Life, send one card to the deck top and reorder the rest. The engine did the reorder and silently skipped the deck placement: its only matching handler wanted "place **them** at the top of your deck", which never matches "place **1**". The clause also uses "Look at all **your** Life cards" (no "of"), which the rearrange handler itself did not accept | ST13-016, ST13-004 |
| 26 | **Cards returned from hand to Life landed FACE-UP** — the hand→Life site defaulted to face-up unless the text said "face-down". Rule 3-10-2 is the opposite: Life cards are face-down *unless otherwise specified*. All 12 such clauses in the pool say neither, so every one showed the opponent a card they may not see and pre-revealed its `[Trigger]`. The sibling site for the same move already defaulted face-down — one rule, two implementations, inverted | 12 clauses |
| 25 | **6 of the engine's 28 tag-strippers could not handle slash-combined tags** — `[On Play]/[When Attacking]` (39 cards, 68 clause lines, incl. OP02-036 Nami). Those 6 stop after the first tag and leave `/[When Attacking]…`, so the anchored match each one runs next fails and that path silently skips the card | 39 cards |
| 24 | **13 compound DON!!-rest costs lost their payment affordance** — the UI parser required the colon straight after "cards", so "rest 1 of your DON!! cards **and trash 1 card from your hand**:" scored 0. Those cards kept a Use button that stayed ENABLED with too few DON!! and did nothing when pressed, and lost the click-a-glowing-DON!! affordance the other 46 have | 13 cards |
| 23 | **The progress ledger showed DON!! boilerplate verbatim** — the cleaner stripped only the *return*-to-deck reminder; the pool also prints a *rest*-in-cost-area one (92) and a period-less variant (2), so ~94 clause instances displayed "(You may rest the specified number of DON!! cards in your cost area.)" in the text the player watches fill in | 44 distinct clauses |
| 22 | **5 prompts pointed at the wrong ZONE** — "Select a card in your hand" while the only legal target sat on the board or in Life. The wording read `TargetZone`, inferred from the whole clause: the same over-reading behind defect #1. It now describes where the legal targets actually ARE | OP03-058, OP05-089, OP08-046, OP11-108, OP12-048 |
| 21 | **`OP01-031` says "You CAN trash 1 …", not "may"** — the panel's routing predicate matched only `You may`, so the pool's lone "You can \<cost\>:" card showed a board prompt with nothing clickable. Defect #1's exact symptom, surviving in one card because the LABEL half of the feature accepted "can" and the ROUTING half did not | OP01-031 |
| 20 | **My own self-disposal fix broke PARTIAL payment** — turning it into a prompt meant "trash 2" against a 1-card hand queued an unpayable pick, which `HandDiscardCannotBePaid` retired outright. The opponent paid NOTHING, where the auto-pick it replaced had correctly taken the one card (rule 8-4-4-1: choose as many as you can) | the 17 trash-from-hand cards |
| 19 | **My own "if they do not" fix never fired when the opponent COULDN'T pay** — I argued the retire sweep would catch it. It only detects missing CHARACTER targets, so a clause wanting a Life card or DON!! is never unresolvable to it. The opponent held a prompt they could only Skip, and a controller who had rested a Character got nothing unless they pressed it | OP05-099, OP15-059 |
| 17 | **My own trigger fix let a DRAWN card pay the cost** — it plays the card then queues the discard, so "Play this card. Then, draw 1 card." put a fresh card in hand before the pick. Rule 8-4-1-3 pays costs first. Fixed with an eligibility snapshot enforced in BOTH the glow filter and the resolver | OP08-104 + the top-or-bottom-Life bodies |
| 18 | `GameClone` dropped `PickedInstanceIds` and `CostPaidRefs` (pre-existing, found while adding the new field) — a rollout restarts a pick with a clean slate and can re-spend a card the real game already spent | bot search, Sandbox undo, puzzle solver |
| 16 | **`[Trigger]` costs auto-trashed `Hand[0]`** — "[Trigger] You may trash 1 card from your hand: Play this card." The Trigger press answers *whether*; nothing ever asked *which*. Invisible to every sweep here, which all enumerate `effect` while these clauses live in the separate `trigger` field | **44 cards** |
| 15 | The place-at-deck-bottom half had **no skip enforcement** — once it became a prompt, declining it was a free escape (found by testing the fix, not the code) | 8 of those 25 |

The Use button's label is now derived by `GameEngine.DescribeCostPrefix` rather than inside
GameManager. It is pure text logic, and while it was private to a MonoBehaviour nothing headless
could call it — so a feature asked for by name shipped untested. `costlabel` now drives it over all
**544** cost-prefixed clauses in the pool: every one produces a label, it names the COST and never
the body, no label exceeds the bubble width, and none leaks a timing tag. Controls: disabling
truncation reports 98 over-width; a naive tag-stripper drops the 4 cards using slash-combined tags
(`[On Play]/[When Attacking]`) back to "Use Effect".

`promptzone` covers the other half of a prompt — WHERE to click — over 792 displayed prompts:
**0** name a zone holding no legal target. No engine suite can see this, because the engine
resolves clicks and never words them. Note the first run reported 27 and most were not real: the
panel shows a target prompt only when the effect is NOT an unpaid cost prefix, and the rest were
wordings computed for a branch the UI never takes. Filtering to the actual display condition left
5 genuine ones.

`costlabel` also covers the circled DON!! cost glyph (`GameEngine.ParseCircledDonCost`), which the
UI held a character-for-character duplicate of. **Both** Unicode series are load-bearing: the pool
uses the dingbat run (➀-➉) for 32 of its 46 circled-cost clauses, so a parser handling only ①-⑩
reads those costs as free. Deleting one series reddens the check with 32.

`costlabel` also drives the panel's ROUTING predicate (`GameEngine.IsUnpaidCostPrefix`), moved out
of GameManager for the same reason: an unpaid cost prefix must route to the Use button, not to a
board prompt. That is the predicate behind defect #1, and inline in the UI its anchored `^You may`
was dead code for every tagged clause. Driving it pool-wide immediately found `OP01-031`.

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
  Now runs THREE oracles: took-without-asking **0**, paid-a-cost-for-no-payoff **0**, and the
  differential — does USING the Trigger differ from PASSING it? **42 driven, 0 indistinguishable.**
  The third is the one the first two cannot cover: a Trigger that takes nothing and owes nothing
  passes both while being completely inert. Each is separately controlled — restoring the auto-pick
  reports 14, suppressing the card placement reports 14, and making `useTrigger` behave like
  `passTrigger` reports 42.
- **`[Trigger]` costs** — `triggercost`: 42 "you may" clauses live in the `trigger` DATA FIELD,
  which **no sweep here reads** — all five enumerate `def.Effect` (checked, not assumed). This is
  where the brief's two halves meet: a [Trigger] fires only when a Life card is dealt as damage, so
  each is a "you may" decision taken *during* the life mechanics. Six cases covering both answers
  to the Trigger itself and both answers to the cost pick, incl. that the card the player NAMES is
  the one trashed.
- **A latent trap, guarded** — `countercost` also asserts that every cost-prefixed `[Counter] +N`
  card is an **Event**. The counter-cost split made `AutomatedCounterPower` return 0 for those
  clauses, and GameEngine gates counter LEGALITY on `AutomatedCounterPower <= 0 && !counterEvent`.
  All 19 such cards are Events today, so nothing breaks; the day a **Character** prints this shape
  it becomes unplayable outright — not weakened, unusable. The check is self-verifying: it first
  asserts the detector still finds the known cards, because a shape-detector that matches nothing
  passes the real assertion perfectly.
- **The progress ledger lines up** — `ledgerparts`: the green/done, red/skipped text drops any
  part it cannot locate in the clause SILENTLY — no error, the text just never colours. 510
  compound clauses driven through real resolution, 529 parts recorded, **0** unlocatable. A
  regression guard rather than a discovery, and labelled as such: parts are not always verbatim
  (the self-disposal clamp rewrites "trash 2" to "trash 1"; the opponent-decision fix rewrites
  "their" to "your"), so a future rewrite reaching the ledger breaks colouring invisibly.
- **Heals** — `healsweep`: "heal" is named in the brief every time and its wordings had never been
  enumerated. **63 distinct shapes**; each is driven against three fixtures (few Life / many Life /
  full trash, since the gates pull in opposite directions) and the resulting Life card inspected for
  SOURCE zone, TOP-of-Life position, and FACING. 0 wrong on all three.
  Scope: **42 of 63** shapes heal in these fixtures, up from 20. Each increase came from closing a
  fixture blindness rather than lowering a bar — building the Leader each gate names, putting a card
  of the required `{Type}` in hand, adding an empty-Life board, and detecting a heal by card
  IDENTITY rather than by Life COUNT (a cost-prefixed heal pays *from* Life and nets zero). **The
  face-up defect was only visible after those changes.** The remaining 21 need a different drive
  path — DON!! payments, `[DON!! xN]` gates, reactive timings — which is a boundary, not a gap:
  those populations belong to `triggerfield` and `timingsweep`. Two explicit probes keep the facing check
  two-sided: a "face-up" clause must land face-UP, and the SAME clause without those words face-DOWN.
- **Re-arranging** — `liferearrange`: the brief's third Life keyword, enumerated like heal (10
  distinct wordings). A reorder is a LOOK, so three things must hold: the COUNT is unchanged (it is
  neither a heal nor a loss), the FACING is unchanged (looking at your own Life must not make the
  stack public — the sharper version of the heal facing defect), and the opponent-facing wording
  moves THEIR stack, not yours. All hold. The fourth case found the missing deck-placement half.
- **Hidden information** — `lifefacing`: the facing defect found in the heal path, asked of every
  clause in the pool that touches Life. **305** distinct clauses driven, both seats inspected;
  **19** legitimately leave a card face-up (their text says so) and **0** do it unasked. Rule 3-10-2
  makes Life face-down "unless otherwise specified", so a face-up card with no wording behind it is
  the opponent seeing a card they may not, and knowing a `[Trigger]` before it is dealt — invisible
  twice over, since nothing logs facing and the Life COUNT is unchanged. Controlled against the real
  defect: reintroducing the heal facing bug makes it report 3.
- **Use actually does something** — `usevsskip`: the brief's second half as a DIFFERENTIAL.
  `paidfornothing` asks whether the engine gave something back, which a log line satisfies; this
  runs each cost-prefixed clause **twice from an identical board**, once pressing Use and once Skip,
  and requires the two states to differ. **497 clauses driven, 422 demonstrably change the board.**
  Baseline is **75, not 0** — that residual is fixture mismatch, not broken cards: a cost wanting a
  `{Navy}` card in hand is correctly inert when the hand has none, and "Use == Skip" is then the
  right answer. Four passes of fixture synthesis took it 111 → 108 → 90 → 76 → 75 **without changing a
  line of engine code**, which is what shows the residual is measurement. Its value is the ratchet.
  A second check settles the interpretation instead of arguing it: of those 75, how many moved the
  board *anyway*? **0** — all 75 leave both branches identical to an untouched board, i.e. the cost
  was simply unpayable. Making Skip resolve the effect reports 155, so the check can fail.
- **Choices are real choices** — `choicediff`: the second decision type, checked the way
  `usevsskip` checks the first. Resolves option A and option B from identical boards and requires
  them to differ; **0** produce the same board. Separates three failures because they need different
  fixes — neither option acts, exactly one acts, both act identically. It found the EB01-052 defect
  above on its first run. Triaging the "one option inert" rows then found the INSTRUMENT at fault
  three times over — the fingerprint recorded power but not **cost**, and not **keywords**, and the
  fixture could not express "set as active" (it had no rested Character to set). Fixing those took
  the inert count **5 → 4 → 3 → 2** with no engine change; **7 of 9** modals now offer two genuinely
  different outcomes. The 2 remaining are correct: a "turn all your Life face-down" option against
  an already face-down Life area, and a branch gated on the opponent holding exactly 1 Life.
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
  That now includes the two LOOK states added by this workstream (the opponent-Life reorder and the
  rearrange that sends a card to the deck top). Looks reach the bot through a different branch from
  pending effects, so the existing helper could not see them — a look the AI never closes is not a
  bad play, it is a game that sits there. Making a Life look unconfirmable reddens both.

## Audit of the fixes themselves

Every engine change in this workstream was re-examined afterwards, against the specific claim it
made rather than the feature area. Six audits:

| change | result |
|---|---|
| `[Trigger]` cost deferral | **defect** — a card drawn by the body could pay the cost (8-4-1-3 pays costs first) |
| "if they do not" branch | **defect** — never fired when the opponent COULDN'T pay; the retire sweep only sees Character targets |
| self-disposal prompt | **defect** — broke PARTIAL payment; "trash 2" vs a 1-card hand paid nothing, where the auto-pick it replaced paid 1 |
| counter-cost split | clean; every `GetCounterPower` call site is bot-only. Left a **latent trap** (now guarded) |
| "up to N" ceiling | clean — but three of the four tests written for it passed with the fix REVERTED |
| reveal-cost trio | clean; the property that a reveal KEEPS the card was untested and now is |

Three of the eight engine defects in this workstream were introduced by its own fixes. That ratio
is the argument for the audit pass, and for treating "the outcome is identical" as a test case
rather than a justification.

## How much to trust the numbers below

Two kinds of claim appear in this document and they do **not** deserve equal weight.

**Test-backed claims** come from the 66 gated suites. Each was negative-controlled — the fix was
broken and the suite confirmed to go red — and each re-runs on demand in ~6s. Counts of clauses,
cards and shapes come from enumerating the card pool, which is reproducible. Treat these as solid.

**The wording-variant class, swept and closed.** Four defects here shared one shape — the pool
prints a wording the engine's literal does not accept: "You **can**" vs "You may" (OP01-031),
slash-combined `[On Play]/[When Attacking]` vs a slash-blind stripper (39 cards), "place **them** at
the top of your deck" vs "place 1" (ST13-016/ST13-004), and "Look at all **your** Life cards" vs
"all **of** your". So the shape was swept systematically: all **302** literal card-text phrases the
engine matches on, against the pool, looking for a card printing a near-variant and not the phrase.
**5 candidates, all benign** — two are the Life pair already fixed, one is a different construct
handled elsewhere, one a false positive, and one (Moby Dick's "all your Characters" buff) was
verified by MEASURING the power rather than by reading the code. `wordingvariant` keeps that last
one pinned, since a passive buff that fails to apply has no prompt and no log line.

**A divergence in the source is not a defect until it is shown to be reachable.** The engine
spells the power cap two ways, `(\d{1,5})` and `(\d{3,5}) power or less`, and three digits cannot
express "0" — which looked certain to break the reduce-then-remove archetype (OP04-008, OP11-002,
OP13-013, OP15-114 Wyper). It does not: those clauses reach the glow filter through earlier
branches that never consult the strict spelling, and both the capped and exact wordings match
correctly. `zeropowerko` is the test that refuted it. Six call sites were left alone rather than
"fixed" on the strength of a plausible reading of the source.

All three divergent pairs the intra-engine literal diff surfaced are now resolved: the
slash-combined tag stripper was a **live defect** (fixed, 39 cards); the `(\d{1,5})` vs
`(\d{3,5})` power cap and the `includes`/`including` type matcher are **benign** — the first
unreachable, the second grammatically justified (filters say "including", conditions say
"includes"). Neither was "fixed". Both now have the assumption they rest on asserted, so the day a
set breaks it, the gate says so instead of a card quietly going dead.

**A passing test is not evidence until it has been seen to fail.** The `uptonrider` suite is the
cleanest example: three cases about an empty board all passed, and all three passed *identically*
with the fix reverted — because at ZERO candidates the clause retires either way. They asserted
something true and audited nothing. Only "up to 2 with exactly ONE candidate" discriminates the
ceiling. Control every case against the specific change it claims to cover, not against the
feature area.

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
