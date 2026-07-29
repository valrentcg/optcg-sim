# Play-test checklist — "you may" effects and Life mechanics

Every item below is **engine-verified and UI-unverified**. The 66-suite gate proves what the engine
does; it cannot prove the panel renders, the button is reachable, or the highlight is visible. That
is what this list is for.

Build note: your last reported build was **1.0.27**, which predates all of it.

Ordered by **what breaks worst if the UI half is wrong**, not by how interesting the fix was.
Each item names a card, the exact action, and what you should see.

---

## 1. Blocking — a wrong result here means a stuck or unplayable game

### 1.1 Prompts that appear on the OPPONENT's side
Four decisions now route to the player who does *not* control the card. If those panels do not
render for the non-controlling seat, that player has no way to answer and the game stops.

| card | do this | expect |
|---|---|---|
| `ST07-010` / `ST07-015` | play it | **the opponent** gets the Choose-one panel, you get a "waiting" state |
| `OP01-038` | let it be K.O.'d | **the opponent** picks a card from *your* hand; your hand is what shrinks |
| `OP05-099` / `OP15-059` | rest it during their attack | **the opponent** is asked to pay a Life card / active DON!! |

Solo vs AI counts as a test here: the AI owns those seats and must answer without hanging.

### 1.2 The two new Life "look" panels
Both are new UI states. A look that cannot be confirmed is a hard stop.

| card | do this | expect |
|---|---|---|
| `EB01-052` | play it, take the first option | a rearrange panel over **the opponent's** Life; confirming writes back to **their** Life, not yours |
| `ST13-016` / `ST13-004` | play it | a rearrange panel; the **leftmost** card goes to the top of your deck, the rest return to Life |

### 1.3 Mandatory picks that must not be skippable into nothing
| card | do this | expect |
|---|---|---|
| `OP08-104` (as a Life `[Trigger]`) | take the Trigger | you are asked **which** card to trash; skipping still costs you one |
| any "your opponent trashes 1 card from their hand" | play it | **they** choose which card, not the engine |

---

## 2. Visible correctness — the game continues, but the wrong thing is shown or done

### 2.1 Facing (information leaks)
The engine now keeps these face-down. Confirm the *card backs* actually render face-down — a
face-up Life card shows the opponent a card they may not see and pre-reveals its `[Trigger]`.

- `ST29-007` / `OP06-106`: add a Life card to hand, then put one back → the returned card is **face-down**
- `ST13-012`: reorder your own Life → all cards stay **face-down** afterwards

### 2.2 The Use button names the cost
Every "You may \<cost\>:" card should label its button with the **cost**, not "Use Effect".

- `OP01-031` — reads "You **can** trash 1 …" (not "may"). This one had **no** Use button at all
  before; it is the single best card to check the label on.
  **Set-up matters:** its cost is a `{Land of Wano}` type card *in hand*. Without one the button is
  correctly unavailable, and you would be looking at correct behaviour and calling it a bug.
- Any `[Activate: Main]` DON!!-cost card: the button reads "Use Effect (rest N DON!!)" and is
  **greyed out** when you have fewer than N active DON!!.
- `OP09-042`, `EB03-018`, `OP10-028` — compound costs ("rest N DON!! **and** trash 1 card" /
  "**and** trash this Character"). These lost the greying and the clickable-DON!! affordance; both
  should be back. *(Not `OP06-080` — it uses the circled ➁ cost, which is a different path and was
  never affected.)*

### 2.3 Prompt wording points at the right zone
Five prompts named the wrong zone. Check the instruction matches where the highlight actually is.

`OP03-058`, `OP05-089`, `OP08-046`, `OP11-108`, `OP12-048`

### 2.4 Cards the brief names
- `OP15-114` **Wyper** — the card this started from. Use it; you should get a clickable Life card,
  not a Skip button alone.
- `OP02-036` **Nami** — prints `[On Play]/[When Attacking]` slash-combined tags, which six engine
  paths could not read.
- `OP15-101` **Kalgara** — pay the cost; the 5-card look should open.

---

## 3. Regression spot-checks — previously working, touched by this workstream

- A `[DON!! xN]` card with too few DON!! attached offers **nothing**; with enough, it offers.
- A `[Once Per Turn]` ability works once, refuses a second use, and works again next turn.
- Declining a `[Once Per Turn]` "you may" does **not** burn the turn's use.
- `OP12-038`: "K.O. up to 2" with only **one** legal victim still K.O.s that one.
  **Set-up matters:** the victims must be **rested** and **base cost 4 or less**, so give the
  opponent exactly one such Character — an active or expensive one is legitimately untargetable.

---

## What is already confirmed WIRED (do not spend play-test time proving these)

Static audit of the client against every engine field and command this workstream added. None of
this proves a panel is *visible* — that is still what you are testing — but it does mean a failure
below is a layout/z-order problem, not a missing code path, and it removes four things from the list
of suspects:

| checked | result |
|---|---|
| The five engine fields added this workstream (`LifeToDeckTop`, `LifeOwnerSeat`, `EligibleInstanceIds`, `DeclineContinuation`, `DeclineSeat`) | **Engine-internal by design.** No client file references any of them, and none needs to: the engine consumes them itself at confirm time (`GameEngine.cs:3825`) |
| Glow vs resolver on frozen picks | **Consistent.** The client derives glow from `IsValidEffectTarget`, which itself honours `EligibleInstanceIds` — so the two cannot disagree on this axis |
| Life re-arrange confirm command | **Correctly routed.** `GameManager.cs:10055` sends `deckLookConfirmOrder` for a rearrange and `deckLookScryConfirm` only for a scry |
| Life re-arrange panel affordance | **Present.** The `rearrange` step is drag-to-reorder plus an explicit Confirm button, not a confirm-only dialog |

So for §1.2, the useful question is not "does confirming work" but "does the panel appear, over the
right stack, and can you reach the Confirm button".

## What a failure here means

The engine behaviour is verified, so a failure is almost certainly in the **panel, the glow, or the
click routing** — not in the resolution. When reporting one, the useful detail is: which card, what
you pressed, what appeared, and whether anything was highlighted. The in-game bug reporter captures
the exact repro (seed + command history), which is worth more than a description.
