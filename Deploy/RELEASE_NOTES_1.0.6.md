# v1.0.6 — Versus A.I., Attack Animations & Sound

## New Features
- **Versus A.I. is live.** Play solo against "Basic Bot," a heuristic opponent that mulligans for
  playable curve, plays its biggest affordable character, spends DON!!, attacks for profitable
  trades, blocks to protect a low-life leader, and uses triggers/counters intelligently. Pick your
  deck and the bot's deck right from the Solo Play portal.
- **Sound effects.** Card draws and DON!! placement now play sound, with a volume slider in both
  the in-game pause menu and the main menu's account settings (synced, with an instant preview on
  drag).
- **Animated card movement.** Every card now flies smoothly between zones — hand to trash, life to
  hand, board to life, DON!! attaching — instead of popping in place. Covers the opening hand deal
  and mulligan too.
- **Turn-start presentation.** A "TURN N · [PLAYER]'S TURN" banner now announces each turn, with
  the phase tracker (REFRESH → DRAW → DON) stepping in sync, plus ambient particle effects drifting
  around the active player's side of the mat.
- **Targeting arrow for character drags.** The solid targeting arrow (previously attack-only) now
  also shows while dragging characters and event cards, with slot-snap highlighting on hover.

## Fixes
- Block step now auto-skips when you have no legal Blocker — no more clicking "Pass Blockers" for
  nothing.
- Life-trigger fix: triggering a Character or Stage from Life now correctly goes to **hand**
  afterward instead of always trashing (that was only ever correct for Events).
- Fixed a bug where a custom (non-starter) deck pick could silently get cleared before the deck
  store finished loading it async — your saved pick now survives.
- Your last-selected game mode (Versus A.I., Private Room, etc.) is now remembered across app
  restarts.
- Mandatory "If [condition], ..." effects and "up to N" targeting effects now auto-resolve instead
  of stalling, matching how optional ("you may") effects already worked.
- Added support for "look at all Life cards, place back in any order" effects (e.g. Makino).

## Compatibility
No match-presence protocol changes — compatible with v1.0.5 clients.
