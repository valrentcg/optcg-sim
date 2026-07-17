# Deck-general tactical corrections — 2026-07-16

## Scope

The reported cards are regression witnesses, not card-ID exceptions. The implementation uses
printed-text/mechanical predicates so the same rule applies to every deck that exposes the mechanic.

## General rules added

1. **Leader-engine routing** — a deck whose leader has `[Activate: Main]` is routed through the
   activation layer even when the old average-cost classifier calls the list `aggro`. This reaches
   ST03 Crocodile, OP15 Enel, ST01 Luffy, and future leaders with the same engine shape.
2. **Rested-DON conversion** — beneficial activation detection recognizes effects that give rested
   DON!! to a Leader/Character. This is a text-level rule, not an ST01 card check.
3. **Zero-value Trigger guard** — optional removal/debuff Triggers, including “activate this card's
   Main effect,” are declined when there is no legal opposing target. “Play this card” Triggers are
   declined when the Character Area is full. Advanced search applies the same pre-search guard.
4. **Owner-agnostic bounce safety** — “return … to the owner's hand” is treated as removal: choose an
   opposing target or decline. It cannot select a valuable friendly blocker merely because that card
   has the highest generic value.
5. **Removal-Event tempo priority** — a playable owner-agnostic bounce Event is valued by the opposing
   board investment it removes, not only its printed cost. If it has no opposing target it is not
   proactively played.
6. **DON!! commitment with evidence-based reserve** — before the first attack, active DON!! with no
   concrete defensive job is distributed across attackers. The only baseline reserve is the cost of
   an actually-held, currently-payable `[Counter]` Event; no Counter Event means reserve zero.
7. **Correct cross-turn duration** — “until the end of your next turn” now survives through that turn.
   It is no longer collapsed into “until the start of your next turn.”

## Deterministic evidence

`strategy-fixture-test`: **12/12 pass**.

- Sables is played over Boa when an expensive opposing bounce target exists.
- Sables targets the opponent and declines when only friendly cards exist.
- Sables/Mole Pistol-style target-dependent Triggers are held on an empty opposing board.
- Kong Gatling's real Trigger resolves to a +1000 timed leader bonus with `endOfNextTurn` duration.
- The bonus exists before and throughout the owner's next turn, then expires.
- ST01 Luffy's rested-DON activation is selected.
- Five active DON!! with no Counter Event becomes five attached DON!! before the attack.
- With a one-cost Counter Event, four are attached and exactly one remains active.

Engine/platform regression:

- Unity engine build: success (warnings only, no errors).
- clone test: 20/20.
- observed seam: 7/7.
- boundary fixtures: pass across all 10 reached boundaries.
- determinizer: 7/7.
- knowledge ledger: 9/9.
- K-world aggregation: 6/6.
- mixed starter smoke: 900/900 completed at 75 games/s; first-player wins 49.1%.

## Matchup signal (not a meta claim)

Honest Advanced vs honest Advanced, Lucy against Enel, paired by turn order:

- earlier broken routing: Lucy 60/60, Enel 0/60;
- after leader-engine/resource corrections: Lucy 11/20, Enel 9/20, invalid 0.

This proves the pathological “Enel never executes its leader engine” behavior moved materially. A
20-game sample does **not** prove the real ranked matchup or the local reference's 66.9% Enel figure.

## Remaining limits

- `Archetype` is still a coarse curve label. `RequiresMainActivation` is a mechanic override, not a
  complete deck identity model.
- The Trigger guard rejects provable zero-value cases. It does not yet solve the opportunity cost of
  using a modest Trigger versus taking a high-value Event into hand.
- DON!! distribution is a safe utilization floor, not matchup-aware attack sizing. A future policy
  should learn counter thresholds, lethal windows, and deck-specific reserve targets from replays.
- Larger paired gates across aggro, control, combo, ramp, life-manipulation, and alternate-win decks
  are still required before claiming a universal strength improvement.
