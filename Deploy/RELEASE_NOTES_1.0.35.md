# One Piece TCG Simulator 1.0.35

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

## Sealed rematch flow

- Change Deck now keeps both players in the same Custom lobby instead of returning either player to the main menu.
- Both players leave the finished match together, their Ready checks reset, and the lobby owner is taken straight to the pack carousel to choose the next set.
- Fixed the set carousel reporting that no booster sets were available after a match rewrote the card library without its pack-rarity data.
- Solo Play now marks Sealed as Ready and keeps its Open Packs action visibly selectable.
- Casual and Ranked now wait for a finished Custom lobby to close completely before creating the next matchmaking session.
