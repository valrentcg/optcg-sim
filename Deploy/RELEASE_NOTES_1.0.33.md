# One Piece TCG Simulator 1.0.33

## Sealed / Pre-Release

- Sealed is now a complete six-pack mode: choose a booster set, rip six randomized packs, build a 40-card deck from the cards you opened, and play it.
- The set picker is a swipeable, momentum-based pack carousel with real wrapper art, release dates, card counts, and Leader counts. OP and EB products appear whenever their complete card pool is available.
- Seeds are generated automatically. A seed and seat reproduce the same pool, while every new run receives a fresh randomized seed.
- Sealed deck construction uses exactly 40 cards from the opened pool, without the normal colour or four-copy limits.

## Opening packs

- Swipe across the highlighted top seam to open a pack. A periodic shine points to the cut line; the wrapper stays still while you swipe and its torn foil peels away only after the gesture is complete.
- The unopened packs remain stacked behind the current wrapper and the pile shrinks as each pack is opened. Revealed cards collect into a separate stack below.
- A strong pack announces itself at the tear. Super Rare hits use silver sparkles and Secret Rare hits use gold, followed by the matching rarity glow on the card reveal.
- Reveal pacing has been tuned, square-looking particles have been replaced with polished sparkles, cards retain their rounded corners, and hovering a pull opens a glowing preview on the right.

## Leaders and deck building

- Click either Leader card to open the complete roster. It can be filtered by colour and set, sorted by name, set, Life, or power in either direction, and scrolled with the mouse wheel even while a card is hovered.
- Banned Leaders remain visible but are greyed out and cannot be selected. Leader hover previews use the same large glowing overlay as an in-game card preview.
- Rainbow Luffy stays in the first roster slot and is fully defined for the special all-colours, all-types, all-attributes, and all-names Sealed variant.
- The deck builder now has notebook-style IN DECK and CARDS FROM PACKS tabs, clearer pool and deck counts, a roomier grid, and glowing right-side hover previews.
- AUTO-BUILD - ADVANCED creates a legal 40-card list using the Advanced Sealed builder's curve, Counter density, efficient-body, interaction, and Leader-synergy priorities.

## Solo Sealed

- Choose your Leader, the opponent's Leader, and Beginner, Intermediate, or Advanced before opening packs.
- The opponent opens its own derived six-pack pool and builds immediately. Advanced follows the strongest scoring pattern; Intermediate relaxes its curve and Counter discipline; Beginner adds more variance and tempting rarity-driven mistakes.

## Custom Sealed

- Custom rooms can select Sealed. The host chooses the set, both players choose Leaders, and the first Ready check confirms those choices before pack opening starts.
- Each player receives a separate deterministic six-pack pool, opens it, and builds privately. The deck checkpoint shows whether the opponent is still building or Ready, and either player can cancel Ready to continue editing.
- Once both 40-card decks are Ready, the host asks to start and the opponent can accept or cancel without losing the pool or deck. Version and card-data mismatches are rejected before the match starts.
- The network handoff acknowledges and retries entry into deck building instead of assuming one message arrived. This flow is implemented but still needs a real two-client player-build test before it is considered connectivity-verified.

## Card information

- When a card is drawn from Life, its ordinary printed effect is written in the Trigger panel even if it has no [Trigger]. Vanilla cards are identified clearly as well.
