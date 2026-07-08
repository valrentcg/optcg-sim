# v1.0.4 — First Playtest Update

Everything in this release comes straight from our first real online playtest. Thanks for the feedback!

## Card Effects & Rules Engine
- **[When Attacking] leader effects now work.** Leader effects like Uta's were being acknowledged without resolving — the effect resolver only understood one narrow text wording. It now handles the general patterns (targeted power buffs, self-buffs, "other than this card", DON!! costs paid at resolve time), fixing the same class of bug across all starter-deck leaders.
- **Cost-reduction effects now apply.** There was no cost-modifier pathway at all — "-N cost" effects (e.g. black Luffy's) now genuinely reduce a card's effective cost, and all "cost N or less" checks use the modified cost.
- **On-play "play from hand" effects now find their targets.** Robin (and any similar card) now correctly offers valid characters — the target search was looking in the wrong zone and only understood one card's exact wording. Feature-type filters like {FILM} now work for all types.
- **Cost & power modifiers are visible.** Cards under an active effect now show badges: "+1000 POWER" (green), "-5000 POWER" (red), "-2 COST" (blue) for as long as the effect lasts.
- **Valid targets highlight in green.** Event/effect targeting (e.g. Backlight) now shows legal targets with a green glow instead of incorrectly flagging them red; the target-validity rules for exact-cost and multi-clause effects were also fixed.

## Duel Flow
- **The defender owns the battle.** After an attack is declared, the block, counter, and final attack resolution are all the defender's decisions. The attacker sees "Waiting on opponent..." — no more resolving your own attacks.
- **Trigger privacy.** Only the defender sees their revealed life card and the Trigger decision. The attacker only gets a card preview after a trigger is actually activated.
- **No dead buttons.** Action prompts only appear for the player who actually has a decision to make. If it's not your call, you see a status line, not clickable buttons.
- **New mulligan flow.** After first/second is decided, your 5 cards are dealt to your hand and KEEP / MULLIGAN bubbles appear beneath it.
- **Descriptive effect buttons.** "Resolve Effect" now tells you what it does — e.g. "UTA: +2000 POWER" or "ROBIN: PLAY FROM HAND".
- **Player names.** The center turn indicator now shows player names instead of NORTH/SOUTH.

## Multiplayer
- **In-match chat.** A chat tab on the left edge of the board — click to expand, Enter to send.
- **Live opponent presence.** You now see what your opponent is doing while you wait: a gold glow on the card they're hovering, and their (still face-down) hand cards lift as they look through them.
- **Win on disconnect.** If your opponent leaves mid-match, you get an "OPPONENT LEFT — YOU WIN!" popup instead of a frozen board.
- **Friends online status.** The friends list now shows a green dot and "online" for friends who have the client open.

## Visual Fixes
- **Attached DON!! size.** DON!! cards attached to a leader/character now render at full card size instead of shrunken.

## Compatibility
- This build changes the match-start protocol and battle priority rules. **v1.0.4 clients cannot play against older versions** — both players must update (the client auto-updates on launch).
