# v1.0.29 — Cards land with weight, and the board answers

## Playing a Character or Stage

- The card **lifts toward you**, hangs just long enough to read, then **slams down** into its slot
  instead of simply appearing there.
- **The board answers the landing.** The cards beside it are struck on the same frame, then a wave
  travels outward from the impact — nearest neighbours hardest, falling off with distance.
- Each card the wave passes catches a **rim light in the played card's colour**: red lands red, blue
  lands blue, using the same colour keying as the Event burn.
- Play a **Stage** and your **Leader** takes the hit instead of the character row.

## Searchers

- A Character that searches your deck when played now **finishes its landing before the search window
  opens**. The engine resolves the search on the frame the card lands, so the deck-look used to appear
  on top of its own slam.

## Sound

- **The coin toss has a coin again.**
- **Every button gives a click when you press it** — Skip, End Turn, the menus, the deck builder,
  sealed. It's hooked to the pointer rather than to each button, so it covers every control in the
  game rather than a list someone has to remember to update.
- Buttons that are **greyed out stay silent**: a disabled control must not sound available.
- The click sits deliberately below the gameplay sounds, since you hear it constantly.

## Fixes that apply to every play, not just the new effect

- **A grey panel no longer sits in a slot before its card arrives.** The summoning-sick shade lives
  beside the card rather than inside it, so hiding the card for its flight left the shade behind — a
  dark rectangle in the destination slot for the whole 0.38s. It has been doing that for as long as
  the shade has existed.
- **Dragging a Stage now previews where it will land**, the same as dragging a Character. The snap
  preview was gated to Characters only.

---

### Known issues

- The card-burn flourish still does not play for Fire Fist, and the Five Elders search does not
  animate the cards travelling from deck to the search window. Both are cosmetic; the effects
  themselves resolve correctly.
