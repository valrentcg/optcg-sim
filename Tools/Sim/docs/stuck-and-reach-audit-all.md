# Stuck / unreachable sweep — every card

- Generated: 2026-07-27 00:35
- Cards swept: **2636**, tagged clauses: **2135**, clause texts reach-tested: **161**

**STUCK** = after the real queue path (including the engine's retire-unresolvable sweep) a pending effect remains that is NOT `Optional` and has nothing clickable anywhere. The pending panel only enables Skip for optional effects, so this is a hard freeze for the player.

**UNREACHABLE** = the clause wants a target, but not one of the game's printed cards satisfies its description (measured with `IsValidEffectTarget`, the same predicate that decides what glows). The effect is recognized and resolves — it just can never do anything.

## STUCK — 0

_none_

## UNCLICKABLE — 1

| Card | Name | Board | Detail | Clause |
|---|---|---|---|---|
| P-029 | Bartolomeo | full-board | the resolver accepts P-029 (own character) [south/character/True/0/0 -> south/character/False/0/0] but the glow lights nothing | You may rest this Character: Set up to 1 of your {FILM} type Characters other than [Bartolomeo] as active. |

## UNREACHABLE — 4

| Card | Name | Board | Detail | Clause |
|---|---|---|---|---|
| OP04-031 | Donquixote Doflamingo | library-wide | wants a card target, but NO card in the library satisfies the description | Up to a total of 3 of your opponent's rested Leader and Character cards will not become active in your opponent's next Refresh Phase. |
| OP06-033 | Vander Decken IX | library-wide | wants a card target, but NO card in the library satisfies the description | You may trash 1 {Fish-Man} type card from your hand or 1 [The Ark Noah] from your hand or field: K.O. up to 1 of your opponent's rested Characters. |
| OP07-091 | Monkey.D.Luffy | library-wide | wants a card target, but NO card in the library satisfies the description | Trash up to 1 of your opponent's Characters with a cost of 2 or less. Then, place any number of Character cards with a cost of 4 or more from your trash at the bottom of your deck in any order. This Character gains +1000 power during this turn for every 3 cards placed at the bottom of your deck. |
| OP11-022 | Shirahoshi | library-wide | wants a card target, but NO card in the library satisfies the description | You may rest 1 of your DON!! cards and turn 1 card from the top of your Life cards face-up: Play up to 1 {Neptunian} type Character card or [Megalo] with a cost equal to or less than the number of DON!! cards on your field from your hand. |

## THREW — 0

_none_

