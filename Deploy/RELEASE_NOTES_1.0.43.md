# One Piece TCG Simulator v1.0.43

## Gameplay fixes

- Reveal-from-hand costs now remain synchronized until the opposing player confirms the shown card, then continue the paid effect correctly instead of silently stopping.
- Advanced AI planning, Sandbox undo and redo, and puzzle simulations now preserve deferred replacements, match winner and outcome data, reveal progress, modal selection progress, and turn-level match facts when cloning a position.

## Verification

- Expanded regression coverage verifies reveal costs, Life-damage privacy, optional once-per-turn effects, full-board replacement choices, and all recorded playtest cases.
