# One Piece TCG Simulator v1.0.42

## Gameplay fixes

- Nested On Play, On K.O., and combat decisions now finish before their parent effect continues, preventing later targets or counter steps from appearing too early.
- Effects that play a Character from the trash now offer a replacement choice when the Character area is full instead of becoming stuck or silently failing.
- Corrected Charlotte Linlin (OP17-112): her On Play draws once before offering the two Life choices, and eligible 4000-power Trigger Characters become 8000 during your turn.
- Corrected Loki (OP17-119) so its controller can finish after selecting a legal subset rather than being forced to choose another K.O. target.
- Corrected Gol D. Roger (OP13-003) so the first DON!! placed during the DON!! Phase automatically attaches to the Leader when required.
- Revealed top-deck cards, including Rocks (OP17-039) reveals, now remain visible at the deck as well as in the reveal panel until confirmation.

## Match feedback

- Deck-to-trash mills now use staggered card-movement audio feedback.
- Decision barriers reject unrelated commands while a replacement, choice, deck look, reveal, or pending effect still requires a response.
