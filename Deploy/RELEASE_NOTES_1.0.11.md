# v1.0.11 — AI Opponent Overhaul + Card Fixes

_Bug Testing Season. Auto-updates from any recent build._

## Smarter AI opponent
- **No more whiffing.** The bot only declares an attack when the hit actually connects — it won't swing a 3000 into your 5000 for nothing.
- **Spreads its DON!!** across multiple attackers to land efficient hits, instead of dumping the whole pool on the Leader for one oversized swing.
- **Counters intelligently.** It only counters when it can *fully* stop the attack, and it accounts for whether it can actually afford the Counter (DON!! for Counter events). No more countering 6000 into 6000 and taking the hit anyway, and no more wasted partial counters.
- **Blocks/counters must exceed, not tie** — matching the rules, an equal value no longer "survives."
- Added a lethal / race read, and fixed a case that could stall the turn.

## Card & effect fixes
- **Crocodile & Mihawk (ST25-003)** — "Draw 2, trash 1, then play up to 1…" now resolves **in order**. It no longer plays the card you were trying to trash; each step gets its own prompt.
- **Sabo / Ace / Luffy (ST13-007 / 010 / 014)** — "Reveal the top of your Life; if it's the named 5-cost, play it. If you do, your Leader gets +2000" now only grants the **+2000 when a card is actually played**. Previously the buff fired even when nothing was revealed to play.
- **Multi-step effects now show one action at a time** on the action buttons — only the part currently being resolved, in sequence.
- Many more effect conditions now resolve correctly: **DON!! ×2 thresholds**, power/cost checks, **attribute-icon** effects (Slash/Strike/Ranged/Special/Wisdom), original-vs-current power/cost, K.O.-timed effects, and "instead"/**replacement** effects (K.O. replacement, removal immunity against bounce/deck-placement).
- Restored missing **attribute icons** on 19 cards used by those effects.

## Visual
- The **Blocker shield** badge now renders on active (un-rested) Blockers.

---
_Full validation: 38 scenario tests, engine coverage across all timings, and a bot-vs-bot sweep of every starter deck (0 crashes / stalls / rule violations)._
