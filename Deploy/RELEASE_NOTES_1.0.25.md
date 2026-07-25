# OPTCG Sim v1.0.25

## New
- **Suggest a bot improvement.** Right-click a card during a game against the A.I. (or in Puzzles) and the report window now offers a **Bot Suggestion** toggle alongside the usual bug report — tell us what the bot should have done instead and why it's better. It records the exact position, both decks, and which A.I. difficulty was playing, so the line you suggest can be replayed in that position and measured against how the bot plays today. The toggle only appears when a bot is actually playing; PvP and hotseat games see the normal bug form.

## Fixes
- **Cards no longer render behind the board zones.** The deck, DON!! deck, trash and Life panels are drawn after everything else on the board, so any card that overflowed its own slot could be painted over by them — most visibly a rested (rotated) Character at the outer end of the row, where the character area deliberately reaches out toward the deck and trash. Character cards and cost-area DON!! now always draw above those panels.
