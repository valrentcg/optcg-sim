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

**41 suites, ~6s, exit 1 on any failure.** Run it after any engine change. It deliberately
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

Also: `"You may"` protections now prompt unconditionally (a decision, not a setting — the
per-seat flag and its UI toggle were deleted), and the Use button **names the cost** rather
than saying "Use Effect".

## What is now asserted, pool-wide

- **Prompted** — `optionalfires`: 704 optional clauses, **0** fire without asking. Deleting the
  opt-in guard turns it red with 127.
- **Resolves** — `paidfornothing`: if the engine takes payment it must give an effect or a
  prompt. **0** across 451 unconditional-body clauses.
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

## Do the fixes matter in real games?

Measured, not assumed. Temporary counters on the fixed paths, over 2,160 bot-vs-bot games
using six META decks (starter decks contain none of the affected cards and report zero):

| path | firings | per game |
|---|---|---|
| optional effect queued for a decision | 45,688 | ~21 |
| **"up to N" rescued from retirement** | **17,330** | **~8** |
| counter cost-prefix queued | 0 -> 2,723 | see below |
| reveal cost handed to the player | 0 | — |

The second row is the one that matters. Before that fix, each of those 17,330 clauses was
being **retired** — the card did nothing, with a rule citation in the log to make it look
deliberate. Roughly eight times a game, in decks people actually play.

**The first zero was a regression I had introduced.** Chasing it: the bots pick counters with
`.Where(GetCounterPower(c) > 0)`, and making the flat path return 0 for a cost-prefixed counter —
correct, and the whole point of fix #4 — made all 15 of those cards invisible to the AI. Measured
at **0 played across 44,143 counters** in decks that contain them. `GetCounterPower` (what a card
is WORTH to a player who can pay) is now separate from the flat boost the engine applies
automatically (still 0). After the split: **2,723 played**. A rules fix that silently removes
cards from the AI's repertoire is not finished, and no unit test would have noticed — the rules
assertion passes either way.

The remaining zero is an honest gap rather than good news: the bots never played a reveal cost in 2,160 games, so that fix is correct by test but
unmeasured in play. Do not read 0 as "does not happen" — read it as "this harness did
not reach it".

Instrumentation was reverted; these numbers are a snapshot, not a standing check.

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

## NOT verified — needs a Play-test

Everything above is headless. Two changes are Unity UI and only type-checked:

1. The **Use button label** now names the cost ("Turn 1 card from the top of your Life cards
   face-up"). Never rendered.
2. The **protection discard** now raises a card-pick prompt *mid-K.O.* — a new interaction
   with no headless coverage of its presentation.

The reported bugs came from build 1.0.27, which contains none of these fixes.
