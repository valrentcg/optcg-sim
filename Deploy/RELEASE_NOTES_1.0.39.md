# One Piece TCG Simulator v1.0.39

## Card reveals and hidden information

- Cards revealed from a hand, Life, or the top of a deck remain face-up in their actual location and appear in the right-side action panel until the required player confirms the reveal.
- Search and selection effects expose only the exact cards the effect requires. Computer opponents acknowledge required reveals automatically.
- Opponent hand cards no longer glow based on the opponent's available DON!!, closing a private-information leak.

## Cards and deck selection

- Refreshed OP17 and ST31 through ST36 with higher-quality clean artwork from the OPPP library while retaining the in-game rounded-corner presentation.
- Added clean art and complete library records for P-000, P-038, P-040, P-064, P-066, P-067, P-080, P-086, P-087, P-094, P-095, P-108, P-109, P-114, P-116, and P-118.
- Replaced Rainbow Luffy with a cleaner image.
- Expanded the Starter Deck hex roster so ST31 through ST36 and their populated deck lists remain selectable.

## OP17 and gameplay fixes

- Completed another OP17 interaction pass covering shared costs, replacements, conditional effects, opponent choices, searches, and reveal requirements.
- Improved command validation, replay restoration, and bot handling for newly supported choices.
- Corrected reported edge cases involving Life, DON!! payments, full Character areas, optional effects, and effects that continue across multiple choices.

## Sealed presentation

- Build Timer now offers selectable deck-building durations when enabled.
- Centered Open 6 Packs, raised the pack stack, separated revealed cards from wrapper art, and added a reveal-speed slider.
- Replaced the old continue-seed shortcut with a fresh-run flow.
- The completion screen now focuses on Practice vs A.I., Export Decklist, New Run, and Menu.

## Social panel

- Refined the movable Social panel with saved placement, reliable Friends and Requests tabs, corrected profile and text spacing, and clipping fixes for smaller windows.
- Copy an entire conversation to the clipboard and paste text into the message composer.
- Lobby invitations follow the custom-room flow and remain queued while a match is active.
- Added normalized notification sounds for friend messages and game invitations, with pending counts visible while Social is collapsed.
