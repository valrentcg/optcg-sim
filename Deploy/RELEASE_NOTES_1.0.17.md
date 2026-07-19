# v1.0.17 — Board feel: push/pull life bar, shadows, smoother motion

A polish-focused update that makes the board read and move a lot better, plus meaningful bot fixes.

## 🎯 New center Push/Pull life bar
- A **tug-of-war bar across the middle of the board**, each half painted in that player's **deck colours**, with a **gold balance point that slides** toward whoever's behind on life.
- **Deck-coloured life totals** at each end, and a **turn chevron** that points to whose turn it is.
- The active player's side carries a **living glow and smoky shimmer**; the idle side sits back.
- Replaces the old center turn pill.

## 🃏 Card presentation
- **Drop shadows** under every card for real depth off the board.
- **Smoother motion**: characters **glide** when the row re-centres (a character leaves/enters) and **tap/untap rotation animates** instead of snapping.
- **Rested / summoning-sick characters darken** while keeping their colours (matching the official app), instead of turning grey.

## 🟡 DON polish
- DON in the **cost area render at full card size** (they were shrinking slightly).
- DON **tuck under rested characters the same way** as un-rested ones.
- Attached DON show **ドン!!×N**.

## 🔎 Quality of life
- The **left card preview scrolls** for cards with lots of rules text.
- **Attacks play a declaration sound.**

## 🤖 Bot fixes
- The bot now uses the **6th-character rule** — when its board is full it **plays over its weakest Character** (trashing it) instead of spamming “no open character slot”.
- **Smarter removal targeting**: it prioritises the real threat (**Blockers / bigger bodies**) rather than grabbing a random legal target — so it clears the wall that's stopping lethal.

## 🛠 Rules fix
- **DON −N** effects (e.g. R/P Law's -3) can now return **attached** DON as valid targets — in the engine, the UI, and the bots.
