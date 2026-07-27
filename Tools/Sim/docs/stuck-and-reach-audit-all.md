# Stuck / unreachable sweep — every card

- Generated: 2026-07-27 00:09
- Cards swept: **2636**, tagged clauses: **1962**, clause texts reach-tested: **289**

**STUCK** = after the real queue path (including the engine's retire-unresolvable sweep) a pending effect remains that is NOT `Optional` and has nothing clickable anywhere. The pending panel only enables Skip for optional effects, so this is a hard freeze for the player.

**UNREACHABLE** = the clause wants a target, but not one of the game's printed cards satisfies its description (measured with `IsValidEffectTarget`, the same predicate that decides what glows). The effect is recognized and resolves — it just can never do anything.

## STUCK — 0

_none_

## UNCLICKABLE — 4

| Card | Name | Board | Detail | Clause |
|---|---|---|---|---|
| OP05-089 | Saint Mjosgard | full-board | the resolver accepts ST01-005 (own character) [south/character/False/0/0 -> south/character/True/0/0] but the glow lights nothing | ➀ (You may rest the specified number of DON!! cards in your cost area.) You may rest this Character and 1 of your Characters: Add up to 1 black Character card with a cost of 1 from your trash to your hand. |
| OP06-107 | Kouzuki Momonosuke | full-board | the resolver accepts OP06-107 (own character) [south/character/False/0/0 -> south/life/False/0/0] but the glow lights nothing | Add up to 1 of your {Land of Wano} type Characters other than [Kouzuki Momonosuke] to the top or bottom of the owner's Life cards face-up. |
| OP16-035 | Roronoa Zoro | full-board | the resolver accepts ST01-005 (opponent character) [north/character/False/2/0 -> north/character/True/2/0] but the glow lights nothing | Rest up to 1 of your opponent's cards. Then, you may trash 1 card from your hand. If you do, give up to 3 rested DON!! cards to your Leader. |
| ST07-017 | Queen Mama Chanter | full-board | the resolver accepts ST01-005 (own life) [south/life/False/0/0 -> south/hand/False/0/0] but the glow lights nothing | You may rest this Stage and add 1 card from the top or bottom of your Life cards to your hand: Add up to 1 of your Characters with a cost of 3 to the top of the owner's Life cards face-up. |

## UNREACHABLE — 12

| Card | Name | Board | Detail | Clause |
|---|---|---|---|---|
| OP01-008 | Cavendish | library-wide | wants a card target, but NO card in the library satisfies the description | You may add 1 card from your Life area to your hand: This Character gains [Rush] during this turn. (This card can attack on the turn in which it is played.) |
| OP01-013 | Sanji | library-wide | wants a card target, but NO card in the library satisfies the description | You may add 1 card from your Life area to your hand: This Character gains +2000 power during this turn. Then, give this Character up to 2 rested DON!! cards. |
| OP04-073 | Mr.13 & Ms.Friday | library-wide | wants a card target, but NO card in the library satisfies the description | You may trash this Character and 1 of your Characters with a type including "Baroque Works": Add up to 1 DON!! card from your DON!! deck and set it as active. |
| OP07-059 | Foxy | library-wide | wants a card target, but NO card in the library satisfies the description | DON!! −3 (You may return the specified number of DON!! cards from your field to your DON!! deck.): If you have 3 or more {Foxy Pirates} type Characters, select your opponent's rested Leader and up to 1 Character card. The selected cards will not become active in your opponent's next Refresh Phase. |
| OP09-007 | Heat | library-wide | wants a card target, but NO card in the library satisfies the description | Up to 1 of your Leader with 4000 power or less gains +1000 power during this turn. |
| OP14-114 | Ran | library-wide | wants a card target, but NO card in the library satisfies the description | Give up to 1 rested DON!! card to 1 of your {Kuja Pirates} type Leader or Character cards. |
| OP15-038 | It's an Order! Do Not Defy Me!!! | library-wide | wants a card target, but NO card in the library satisfies the description | Up to 1 of your opponent's rested Characters with a cost of 8 or less that has 2 or more DON!! cards given will not become active in your opponent's next Refresh Phase. |
| OP15-114 | Wyper | library-wide | wants a card target, but NO card in the library satisfies the description | Give up to 1 rested DON!! card to 1 of your {Sky Island} type Leader or Character cards. |
| OP15-117 | Heso!! | library-wide | wants a card target, but NO card in the library satisfies the description | Draw 1 card. Then, give up to 1 rested DON!! card to 1 of your {Sky Island} type Leader or Character cards. |
| OP16-094 | Portgas.D.Ace | library-wide | wants a card target, but NO card in the library satisfies the description | Give up to 1 rested DON!! card to 1 of your {Land of Wano} type Leader or Character cards. |
| ST21-009 | Nami | library-wide | wants a card target, but NO card in the library satisfies the description | Give up to 2 rested DON!! cards to 1 of your {Straw Hat Crew} type Leader or Character cards. |
| ST30-012 | Monkey.D.Luffy | library-wide | wants a card target, but NO card in the library satisfies the description | Rest up to 1 of your opponent's [Blocker] Characters. |

## THREW — 0

_none_

