# Leader-effect operability sweep

- Generated: 2026-07-27 00:07
- Cards swept: **136**, tagged clauses: **107**, clause texts reach-tested: **21**

**STUCK** = after the real queue path (including the engine's retire-unresolvable sweep) a pending effect remains that is NOT `Optional` and has nothing clickable anywhere. The pending panel only enables Skip for optional effects, so this is a hard freeze for the player.

**UNREACHABLE** = the clause wants a target, but not one of the game's printed cards satisfies its description (measured with `IsValidEffectTarget`, the same predicate that decides what glows). The effect is recognized and resolves — it just can never do anything.

## STUCK — 0

_none_

## UNCLICKABLE — 0

_none_

## UNREACHABLE — 1

| Card | Name | Board | Detail | Clause |
|---|---|---|---|---|
| OP07-059 | Foxy | library-wide | wants a card target, but NO card in the library satisfies the description | DON!! −3 (You may return the specified number of DON!! cards from your field to your DON!! deck.): If you have 3 or more {Foxy Pirates} type Characters, select your opponent's rested Leader and up to 1 Character card. The selected cards will not become active in your opponent's next Refresh Phase. |

## THREW — 0

_none_

