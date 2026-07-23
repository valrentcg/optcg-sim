# v1.0.19 — Five Elders deck, smarter bots, connectivity fixes, and in-game bug reports

This release adds full support for the Imu / Five Elders deck, a proper game-start experience (coin flip and
an animated mulligan), a round of Advanced-bot fixes, several lobby/connectivity fixes, working password
recovery, and a new right-click "Report a Bug" tool so issues can be sent in from inside a match.

## New: Imu and the Five Elders deck

- **Five Elders (OP13-082)** now fully works. Its Activate: Main pays its cost (rest 1 DON and trash 1 card
  from your hand), trashes all of your Characters, then lets you play up to 5 different-named Five Elders
  Characters back from your trash. The trash selection greys out a name once you have played it, so you can
  only pick unique ones.
- The Five Elders trash-selection screen now scales to fit the window when many cards are eligible, instead
  of overflowing off the bottom of the screen (this also fixes full-deck search screens).
- **Fixing the cost was any card, not just Celestial Dragons.** The "trash 1 card from your hand" cost for
  Five Elders now correctly lets you trash any card, not only Celestial Dragons.
- **St. Marcus Mars (OP13-091)** now resolves its On Play correctly when played by an effect (for example
  from The Empty Throne). It asks to trash a card and K.O. an opponent Character, instead of mistakenly
  offering a "gain Blocker" prompt.
- **St. Shepherd Ju Peter (OP13-084)** now applies its aura: with 10 or more cards in your trash, all of your
  Five Elders Characters have their base power set to 7000 on your turn.
- **The Empty Throne (OP13-099)** and other effects that play a Character now handle a full board: instead of
  doing nothing, you are asked which of your Characters to play over (or skip).
- **Imu leader** now opens a Stage-selection screen at the start of the game, letting you place a Stage from
  your deck before your opening hand is drawn.

## New: game-start experience

- A coin-flip animation now plays at the start of a match, and the winner chooses whether to go first or
  second.
- The keep/mulligan screen now shows whether you are going first or second.
- Choosing to mulligan now animates your hand returning to the deck and the deck shuffling before the new
  hand is dealt, and the board is no longer greyed out while that plays.

## Cards and rules

- The 6th-character rule now works for effect-driven plays. When an effect plays a Character onto a full
  board, you choose which of your Characters it replaces, with a Skip option so it can never get stuck.
- Multiple On Play effects that resolve at once (for example several Five Elders played together) now resolve
  one card fully before the next, instead of interleaving their steps.
- "If a Character is rested by your effect" abilities (OP07-031, OP10-036) now work instead of being silently
  dead and logging an unknown-condition warning.

## Bots

- The Advanced bot no longer wastes a targeted removal event (such as Gum-Gum Jet Pistol) when it has no
  legal target — for example against a board of Five Elders that are immune to removal.
- Additional Advanced-bot improvements: a fair-information view for its search, richer board evaluation,
  a more accurate removal model, and better use of Activate: Main abilities.

## Connectivity and lobbies

- The "Connecting..." screen now has a 45-second timeout and a Cancel button, so a stalled connection no
  longer leaves you on a frozen spinner.
- Custom lobbies now wait for both players to be ready before starting the match, fixing a race that could
  leave a custom game unable to connect.
- Casual matches are no longer mislabeled as ranked.
- If two players are on different game versions, the match now aborts with a clear message instead of
  silently desyncing.

## Account

- Password recovery now works: the reset step was failing for everyone and has been fixed. The reset screen
  also adds a confirm-password field and a cleaner button layout.

## Interface

- Valid targets keep their green highlight on hover across all selection steps (effect targeting, life
  viewing, deck viewing, counters), instead of flipping to a gold tint.
- Deck import now accepts two common list formats.

## New: report a bug from inside a match

- Right-click any card to open a "Report a Bug" window. It captures the exact game state along with your
  description and sends it in, so problems can be reproduced and fixed. Reports are also saved locally.
