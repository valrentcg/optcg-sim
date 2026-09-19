# One Piece TCG Simulator 1.0.38

## Friends and messages

- Added a persistent Social panel inspired by modern competitive games. Open friends and conversations from the main menus, deck builder, Sealed deck builder, or during a match without leaving what you are doing.
- Search friends, filter by online status, open player profiles, and keep several conversations available. Message history, unread counts, drafts, and the active conversation survive menu changes and panel refreshes.
- The collapsed Social button shows unread messages, friend requests, and pending game invitations so new activity remains visible without occupying the screen.
- Friend messages remain available during a match alongside opponent chat. Game invitations stay queued while a match is active and can be accepted after returning to a main menu.

## Invitations and requests

- Friend requests and room invitations now live in the Social panel with compact accept and decline controls instead of covering the screen with a popup.
- Room invitations show the sender and room details, update the pending badge, and use the same join flow as the room browser. Hosts can quickly invite a friend from room creation or the waiting room.
- Removing or blocking a friend uses a clear confirmation step. Failed social requests keep their unread state so an item is not lost before it succeeds.

## Play screen

- Refined the Duel deck showcase with deck information above the illustration, full-width faded Leader art, the card's in-game corner mask, and a repositioned Change Deck action.
- Solo Play deck illustrations fill their panels vertically. Your Deck and Bot's Deck remain clear while difficulty, timing, and match actions use the available space more consistently.
- Casual and Ranked place players in queue and players in a match on either side of the queue action. Header population labels no longer collide with their values.

## Rooms and navigation

- Polished Create Room and the two-player waiting room with tighter grouping, clearer player and deck presentation, better rule spacing, and a visible scrollbar when more rule choices are below the fold.
- The public-table browser uses the standard resize-grip symbol. Restore Code sits with Back to Play, and sidebar destinations work from room creation without leaving two menu items selected.
- Back buttons use a muted magenta treatment for quick recognition, while Exit uses red.
- Windowed mode responds to its available client area. Display mode, resolution, cursor, and related settings can be opened while playing.

## Updater

- The launch-time update panel measures the complete release notes and keeps every section inside a responsive scroll view.
- A visible scrollbar appears whenever the notes overflow. Use the mouse wheel or drag the bar to read from the first heading through the final bullet without clipping.

## Online verification

- Verified Custom Constructed and Custom Sealed with two packaged Windows clients through Unity Gaming Services Sessions, Relay, and Netcode for GameObjects.
- Verified Casual and Ranked through the live matchmaking Worker, ready check, host session publication, Relay peer connection, deck and player exchange, chat transport, and match-start delivery.
