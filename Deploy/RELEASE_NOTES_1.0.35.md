# One Piece TCG Simulator 1.0.35

## New cards

- Added the complete 119-card OP17 set, all 30 new cards from ST31 through ST36, and 38 newly announced promotional cards.
- New cards include their playable effects, triggers, traits, stats, and compressed card art. Where only Japanese artwork was available, an English gameplay text block was added in the style of an official card.
- Corrected several preview images to use the regular base artwork instead of alternate-art versions, including green Shanks and Lead Performers.
- Filled in missing artwork for pre-existing Starter Deck cards and added additional promotional releases, including the live-action Straw Hats, Baroque Works, and Navy-themed Ace, Sabo, and Luffy cards.

## OP17 Pre-Release

- OP17 is available in Sealed / Pre-Release with randomized six-pack pools, its own pack wrapper, Leader selection, deck building, and solo or Custom play.
- OP17 pack collation follows the same Leader, Rare, Super Rare, and Secret Rare rules as the other supported booster sets.

## Formats

- OP17 and ST31 through ST36 are classified as Block 5 and are legal in Standard.
- The new cards can be used in Ranked, Casual, and Standard Custom matches. Extra Regulation Custom matches continue to allow the full card pool.

## Card mechanics

- Added support for OP17's new shared-cost plays, replacement effects, conditional Counters, reveal conditions, cost-12 interactions, and opponent-controlled choices.
- Player-choice effects now wait for the correct player to select cards and payment resources instead of choosing automatically.
- An independent rules pass compared all 119 OP17 records with the currently published card information and exercised the new high-risk mechanics through the gameplay engine.

## Scrollable update notes

- The launch-time update screen now shows the complete patch notes inside a larger scrollable panel while the update downloads.
- Use the mouse wheel or drag the visible scrollbar to read every section without moving the version, progress bar, or download percentage.
- Release-note headings and bullet points are formatted for the game instead of showing raw Markdown symbols.

## Player names

- Ranked, Casual, Custom, and Custom Sealed matches use both players' real display names again instead of falling back to Player 1 and Player 2.
- Both names now travel inside the authoritative match-start message instead of depending on separate lobby-message timing.

## Friends

- The sidebar now shows friends who are online as soon as the menu loads instead of reporting 0 online until the Friends list is opened.
- Friend relationships and presence perform a background server resync after the account is restored, then continue updating live.

## Player profiles

- Human players now use their selected profile picture in the in-match player plate instead of the generic Leader-colour hex.
- Each player's profile picture travels with the match-start data in Ranked, Casual, Custom, and Custom Sealed. Bots keep their existing hex icons.
- Added 27 new profile-picture choices from ST21, ST22, OP12, OP13, and OP14.

## Sealed rematch flow

- Change Deck now keeps both players in the same Custom lobby instead of returning either player to the main menu.
- Both players leave the finished match together, their Ready checks reset, and the lobby owner is taken straight to the pack carousel to choose the next set.
- Fixed the set carousel reporting that no booster sets were available after a match rewrote the card library without its pack-rarity data.
- Solo Play now marks Sealed as Ready and keeps its Open Packs action visibly selectable.
- Casual and Ranked now wait for a finished Custom lobby to close completely before creating the next matchmaking session.
