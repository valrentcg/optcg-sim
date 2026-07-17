# Effect-coverage operability sweep — v2 (resolver-oracle)

- Generated: 2026-07-17 16:43
- Cards: **2636**
- Non-trigger event bodies dry-run through the REAL resolver: **1962** (0 threw mid-execution = recognized)
- **RESOLVER GAPS (returned NotAutomated): 0** across 0 cards
- Trigger bodies: **487**  → **TRIGGER GAPS: 0** across 0 cards

Oracle: non-trigger event bodies are checked by dry-running `TryResolveKnownEffect` on a scratch state — only an explicit `NotAutomated` return (no handler matched, no throw) is a gap. [Trigger] bodies use the trigger gate plus its Play-this-card(character) and Activate-[Main] special-cases. Passive auras / keyword grants excluded (resolved elsewhere).

## A. RESOLVER GAPS — non-trigger event bodies the resolver cannot handle

## B. TRIGGER GAPS — [Trigger] life cards that silently divert to hand

