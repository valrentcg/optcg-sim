# Stuck / unreachable sweep — every card

- Generated: 2026-07-27 00:13
- Cards swept: **2636**, tagged clauses: **1962**, clause texts reach-tested: **287**

**STUCK** = after the real queue path (including the engine's retire-unresolvable sweep) a pending effect remains that is NOT `Optional` and has nothing clickable anywhere. The pending panel only enables Skip for optional effects, so this is a hard freeze for the player.

**UNREACHABLE** = the clause wants a target, but not one of the game's printed cards satisfies its description (measured with `IsValidEffectTarget`, the same predicate that decides what glows). The effect is recognized and resolves — it just can never do anything.

## STUCK — 0

_none_

## UNCLICKABLE — 2

| Card | Name | Board | Detail | Clause |
|---|---|---|---|---|
| OP06-107 | Kouzuki Momonosuke | full-board | the resolver accepts OP06-107 (own character) [south/character/False/0/0 -> south/life/False/0/0] but the glow lights nothing | Add up to 1 of your {Land of Wano} type Characters other than [Kouzuki Momonosuke] to the top or bottom of the owner's Life cards face-up. |
| OP16-035 | Roronoa Zoro | full-board | the resolver accepts ST01-005 (opponent character) [north/character/False/0/0 -> north/character/True/0/0] but the glow lights nothing | Rest up to 1 of your opponent's cards. Then, you may trash 1 card from your hand. If you do, give up to 3 rested DON!! cards to your Leader. |

## UNREACHABLE — 3

| Card | Name | Board | Detail | Clause |
|---|---|---|---|---|
| OP07-059 | Foxy | library-wide | wants a card target, but NO card in the library satisfies the description | DON!! −3 (You may return the specified number of DON!! cards from your field to your DON!! deck.): If you have 3 or more {Foxy Pirates} type Characters, select your opponent's rested Leader and up to 1 Character card. The selected cards will not become active in your opponent's next Refresh Phase. |
| OP09-007 | Heat | library-wide | wants a card target, but NO card in the library satisfies the description | Up to 1 of your Leader with 4000 power or less gains +1000 power during this turn. |
| ST30-012 | Monkey.D.Luffy | library-wide | wants a card target, but NO card in the library satisfies the description | Rest up to 1 of your opponent's [Blocker] Characters. |

## THREW — 0

_none_

