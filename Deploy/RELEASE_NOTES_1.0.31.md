## Targeting arrows are back

Every targeting arrow in the game was missing — dragging to attack, hovering a target, and the arrow drawn between two cards once an attack was declared. None of them were broken individually: the shader they are all drawn with was being dropped from the packaged build, so the whole system switched itself off. It only ever worked when running from the editor, which is why it survived testing.

## The coin flip

- The toss has a sound. It never played once — the sound was asked for in the same instant the game started loading it, so it was always silently discarded.
- Your opponent sees the coin now. Anything arriving from the other player mid-toss — their name, their deck, a chat message — rebuilt the board and took the coin with it, leaving them on "Flipping the coin…" until the result appeared.
- The first sound of a session is no longer swallowed. This also affected Life cards turning face-up, which is why that one seemed to work sometimes.

## DON!! costs

- Paying a `DON!! −N` cost lights up every DON!! you can return — active, rested, and DON!! attached to your Leader or Characters. They were drawn with a thin outline that sat behind the neighbouring DON!!, so nothing appeared to be selectable.
- You can decline a cost. An effect with a cost is never forced on you, even when the effect itself is mandatory — you simply choose not to pay. Eighteen cards were resolving their cost with no way to say no.
- The cost is paid first. Marineford lit up your hand for the card it wanted you to trash before you had paid the DON!!, so the payment only happened once you clicked something that was not highlighted.

## Cards

- **Emporio.Ivankov** — made you play one of the three cards you had just looked at, filtered to cost 2 or less. It should reveal an {Impel Down} card to your hand, and *then* let you play a cost-2-or-less Character from your **hand**.
- **Shirahoshi** (the 1-cost searcher) — nothing in the search was selectable. It reveals a {Neptunian} **or** {Fish-Man Island} card, and the second type was being ignored, so no card in the five ever qualified.
- **Let's Go!! To the Navy Headquarters!!** — rested six DON!! and did nothing. Setting your Leader and all your Characters active was never implemented, so the cost was paid for no effect.
- **Mamaragan** — gave the +1000 to whichever card was in the battle without asking, then made you select a Character before it would draw, and lost the draw entirely if you skipped that prompt. You now choose who gets the power, and the draw resolves on its own afterwards whether you take the boost or not.
- **Borsalino** — the end-of-turn `DON!! −2` gave you no way to decline.
- Counter events that say "up to 1 of your Leader or Character cards" now let you choose, and only cards that actually qualify light up — a `{Minks}` counter no longer offers your Admirals.

## Elsewhere

- Casual matches show your opponent's name instead of "Player 2". Their name could arrive a moment after the match had already started, and whatever was known at that instant was kept for the whole game.
- Your personal DON!! deck no longer follows you into other accounts on the same PC. It was saved without an account attached.
- The scroll bar in the card preview on the left sat on top of the text instead of beside it.
- The A.I. could not pick from a search that accepted two types, and would silently take nothing.
