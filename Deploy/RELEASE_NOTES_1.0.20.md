# v1.0.20 — Standard & Extra Regulation, Puzzles mode, deck-builder overhaul, and rules fixes

This release adds official format legality (Standard and Extra Regulation, with the April 2026 block rotation
and ban list), a new Puzzles solo mode, a big round of deck-builder improvements, per-game custom-lobby rules
including an Ignore Bans option, display/resolution options, and several rules and card fixes.

## New: Standard & Extra Regulation formats

- Decks are now checked against the official format rules. **Standard** uses the in-rotation blocks (Blocks
  2–5). The **April 2026 rotation** moved **Block 1 — OP01–OP04 and ST01–ST09** — to **Extra Regulation** only.
- **Reprints and Manga Rares are handled correctly:** a Block-1 card reprinted into a later block, or a Manga /
  Super-Parallel Rare, stays Standard-legal (from the official block-number list).
- The shared **ban list** (banned cards and banned pairs) is enforced in both Standard and Extra.
- The **deck builder** shows each deck's Standard/Extra legality under the leader, flags the exact cards that
  keep a deck out of a format (hover the format chips), tags each card, and adds filters for format, block
  number, and legality.
- **Casual and Ranked** queues grey out decks that aren't Standard-legal, so you can't queue an illegal deck.

## New: Puzzles (Solo Play) — early access

- A new **Puzzles** tab under Solo Play: single-board lethal puzzles, each with a guaranteed winning line this
  turn. Leaders' abilities and counters are all in play — you can't just swing and win.
- **Three graduated hints** if you're stuck, plus a **three-strike system** that reveals the winning line if
  you can't find it. The opponent's hand is hidden and it defends optimally.
- Some puzzles are **built from real recorded games**. This mode is still in early development — expect rough
  edges, and right-click a card to report a bug.

## Deck builder

- Hovering a card shows a **large, match-sized preview** with the gold glow, placed on the side away from the
  card you're viewing so it never covers it.
- **Right-click a card to add a full playset (4 copies)** at once.
- Hovering the **Standard / Extra chips** marks the exact cards making a deck illegal for that format.
- The **filter pills collapse** to give the card grid the whole panel.

## Custom lobbies

- Choose the **format** (Standard or Extra Regulation) per custom game, with an optional **Ignore Bans** toggle
  to allow banned cards.
- A **Ready** button locks in your deck; the match can't start until both players are ready, and you can
  un-ready to swap decks.
- The waiting room shows the **full match rules** — format, ban-list status, rewind (Forgiveness), and timing.

## New: display options

- Pick your **resolution** and toggle **Fullscreen / Windowed** in Settings.
- Windowed mode is **freely resizable** — drag the window to any size and the UI scales to fit.

## Rules & card fixes

- **[Double Attack]** no longer wins against a Leader with exactly **1 Life** — the extra damage of a single
  hit does not defeat (official ruling Q36).
- **Nefeltari Vivi (EB03-001)** no longer fires a phantom **[When Attacking]** effect on every swing.
- **Kouzuki Oden (EB01-001)**'s leader buff now correctly requires a **DON!!** attached, and other DON!!-gated
  abilities ([On Block], [Activate: Main]) are enforced.
- **[Activate: Main]** leader abilities (Perona and others) can now be used in Puzzles.

## Bots and bug reports

- The **Advanced bot** won't rest an attacker to remove a Character that isn't a threat, or trash a live
  attacker with nothing valid to bring back, and now considers negate and sacrifice-to-recur plays.
- **Bug reports** capture the full decks in play, so custom-deck issues can be reproduced exactly.
