# v1.0.16 — Timed play, Sandbox, and a counter-step redesign

The biggest feature drop yet: timed games, a full Solo/Sandbox testing toolkit, replay position restore, and a top-to-bottom redesign of the counter step.

## ⏱ Blitz — timed play (new)
- **Personal chess clocks** for each player, with **+5 seconds banked at the end of every turn** (a green “+5s” pops beside your clock).
- **Custom timing** in the Custom Room: set each player’s starting clock, the per-turn increment, and the response-window limits.
- **Presets**: Bullet 5:00, Blitz 7:30, Rapid 12:00, or Custom.
- Prominent clock HUD with the active player’s clock highlighted.
- **Ranked timing** option: a single shared match clock with rulebook-style overtime.
- Response-window countdown for defender decisions (block / counter / trigger) — your own on-turn actions run off your personal clock, not a response timer.

## 🧪 Sandbox — free-form testing board (new)
- Boot a blank active board and **spawn or edit any card in any zone** for either seat.
- **Undo/redo**, plus **save/load** board snapshots to disk slots.
- Attach DON, buff power, grant keywords (Rush / Blocker / Double Attack / Banish), set Life, force the phase or active seat.
- **Deck-builder-style card picker**: card-art grid with search, colour filters, and hover preview.
- Draggable tool tab that remembers where you put it.

## ↺ Restore Code (new)
- Export a position from a replay as a shareable code, then **play it out yourself** from that exact spot (pick your POV or hotseat). Available in the Custom Room and Sandbox.

## ⚔️ Counter step — redesigned
- Pick counters **right in the actions panel** from a grid of your hand — usable cards are highlighted, the rest dimmed.
- **Matchup read-out**: mini card art for attacker and defender with their power, and a clear **Safe ✓ / Hits ⚠** line telling you exactly how much more counter you need.
- **Hover a card** to float it up in your hand and preview it; hover a combatant to preview it beside the panel.
- **Resolve Attack in one step** — the separate damage window is gone, and the button stays put even with a big hand.
- **Counter events** only light up green when you have the DON to play them.

## ✨ Polish & fixes
- Card power / cost / status indicators restyled as **sleek pills** with bright colour-coded text.
- **Summoning-sick** characters now read as a subtle grey instead of washed out.
- **Multiplayer shows player names** instead of “North/South” throughout the match.
- The **win/lose screen is always reachable** again after pressing View Board.
- New **Patch Notes** tab on the main menu.
- Turn-sequence pill display no longer double-shows during transitions; in-match menu reordered (Main Menu on top).
