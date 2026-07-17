# v1.0.12

**New: pick your AI difficulty**

- Solo matches now have a **Beginner / Intermediate / Advanced** difficulty selector (your choice is remembered).
- **Advanced** runs a much stronger planning bot — it plays out full turns, uses activated abilities, spreads DON!! for efficient attacks, and applies real pressure instead of drifting. Intermediate is the Elo-tournament champion bot; Beginner is the original heuristic bot.

**Ranked & match fixes**

- **Fixed getting stuck on "Connecting"** when you requeue right after a match — rooms now close properly, so back-to-back queues find a fresh match instead of hanging.
- **Added a Surrender button** to the in-match menu. Leaving to the main menu mid-match now counts as a surrender too — your opponent gets the win, and it's recorded.
- **Blocker shield redesigned:** bigger, centered above the card with a soft glow, and it now shows a **red X (glow off)** when a card's block is cancelled (e.g. Limejuice) or when an **unblockable** attack is coming.

**Card fixes**

- **Uta** and the other *"costs less in your hand when a condition is met"* cards now actually get their discount (e.g. Uta with a 10,000-power Character on the board). Handled generally now, so the whole family works.
- **Block-cancel** (e.g. Limejuice) no longer wrongly switches off the *rest* of a character's effects — it only stops the block.
- **Removed the manual rest option** — characters only rest from attacking or card effects now.
- Plus additional under-the-hood AI and card-effect improvements.
