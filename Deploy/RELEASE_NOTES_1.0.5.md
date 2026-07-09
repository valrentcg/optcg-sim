# v1.0.5 — 100% Card Automation + Trash/Mulligan/DON!! Overhaul

## Card Engine
- **Every card ability now auto-resolves.** A full sweep of the 4,572-card library brought
  automated-effect coverage from ~56% of cards to **100%** — the engine now recognizes and plays
  out every ability line in the game (reminder-only text aside). Verified by an automated fuzz
  test that played 316+ full games across every starter matchup plus random decks from the whole
  card pool: zero crashes, zero stuck effects, zero deadlocks.
- **Specific playtest bugs fixed:** Sengoku's [On Play] not triggering, Smoker's effect missing
  its character-selection step, Uta's [On Block] never firing, Gild Tesoro not paying its DON!! −2
  cost, Hina's dead activate button, and the full Shanks-vs-Smoker matchup — all now play correctly.
- **New timing hooks:** [On Block] and [On Your Opponent's Attack] effects (e.g. Nami, Teach's
  redirect) now fire at the right moment.
- **Fixed soft-locks:** mandatory effects with no legal target could freeze the game — these now
  resolve or dismiss cleanly instead of getting stuck.
- **Fixed multi-line effects losing text.** "Choose one:" and "Apply each…" bullet lines were
  being dropped when queued, silently breaking every multi-option card (Backlight, Soul Pocus,
  Jango, and others). Choice text is now parsed correctly before anything else runs.
- Dozens of smaller resolver fixes: mandatory-vs-optional effect labeling, K.O.-immunity and
  removal-replacement effects, continuous power/cost auras, DON!! cost variants, and more.

## New Features
- **Trash viewer.** "View Trash" (and any effect that needs a trash pick) now opens a searchable,
  scrollable overlay instead of a plain text list — drag to reorder, click to select.
- **Animated, reorderable mulligan.** Your opening hand is dealt with an animation; drag cards to
  reorder before deciding KEEP or MULLIGAN, and toggle away to peek at the board mid-decision.
- **DON!! grouping.** Organize your DON!! into groups on your own field — your opponent sees your
  groupings live during a match.
- **Deck picking moved into the Duel portal.** Pick your deck right on the main menu before
  queuing — no separate launch bar. Your last-used decks are now remembered per account between
  launches. The "Private" lobby tab is renamed "Custom."

## Visual Polish
- **Consistent rounded card corners everywhere.** Deck builder tiles, leader slots, showcases, and
  lobby thumbnails now use the same anti-aliased shader rounding that in-match cards already had.
- **Less flicker on opponent presence.** Hover glow and raised-hand indicators during a match
  update more smoothly.

## Compatibility
This build changes match presence data (DON!! grouping sync). **v1.0.5 clients cannot play against
older versions** — both players must be on the latest build (the client auto-updates on launch).
