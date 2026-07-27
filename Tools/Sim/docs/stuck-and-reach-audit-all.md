# Stuck / unreachable sweep — every card

- Generated: 2026-07-27 00:38
- Cards swept: **2636**, tagged clauses: **2135**, clause texts reach-tested: **294**

**STUCK** = after the real queue path (including the engine's retire-unresolvable sweep) a pending effect remains that is NOT `Optional` and has nothing clickable anywhere. The pending panel only enables Skip for optional effects, so this is a hard freeze for the player.

**UNREACHABLE** = the clause wants a target, but not one of the game's printed cards satisfies its description (measured with `IsValidEffectTarget`, the same predicate that decides what glows). The effect is recognized and resolves — it just can never do anything.

## STUCK — 0

_none_

## UNCLICKABLE — 0

_none_

## UNREACHABLE — 0

_none_

## THREW — 0

_none_

