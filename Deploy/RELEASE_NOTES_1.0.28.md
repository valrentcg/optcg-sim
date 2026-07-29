# v1.0.28 — Stages, Life cards, and a Blocker lock that finally locks

All of this came out of player bug reports filed in-game.

## Stages

- **You can play a Stage from your hand again.** Dropping one onto an empty Stage zone did nothing at
  all. Because dragging is the only way to put your *first* Stage down, no Stage could ever be played
  by hand — for either player, on any leader. (The A.I. was unaffected, which is why Stages appeared
  to work when the bot played them.)
- The Stage zone now lights up as a valid drop target while you drag one, the same as a character slot.

## Life cards

- **A Life card an effect turns face up now shows its face.** With six or more Life the flipped card
  was never drawn at all, so the combat log said the card had been turned up while the board still
  showed a card back. Wyper and Kalgara both looked broken because of it. At exactly five Life it
  happened to work, which is why it hid for so long.
- **Your opponent's Life stack reads the right way round.** Their top Life card now sits nearest the
  middle of the board, matching your own side.
- Effects that let you choose the **top or bottom** of your Life now highlight the correct card. Above
  five Life they offered — and played — the wrong one.

## Effects that offer a choice of card

- Abilities worded **"play a {Type} Character card _or_ [Name]"** now offer both. Shirahoshi's ability
  lit nothing in your hand and her Use button did nothing, because the two halves of the choice were
  being required *together* rather than either one on its own.
- **An effect can no longer strand you.** A mandatory ability with nothing legal to pick used to show a
  button that did nothing next to a greyed-out Skip, leaving no way to continue the game.
- A pending decision belonging to your opponent no longer swallows clicks on your own hand.

## Blocker locks

- **"Your opponent cannot activate a [Blocker]…" now does something.** On Shanks, Adio and the
  starter-deck cards sharing this wording the restriction was being read as a note and discarded, so
  weak Blockers could still block freely.
- **Attacking with Shanks no longer crashes the game.**

---

### Known issues

- The card-burn flourish does not play for Fire Fist, and the Five Elders search does not animate the
  cards travelling from deck to the search window. Both are cosmetic; the effects themselves resolve
  correctly.
