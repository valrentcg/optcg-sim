# OPTCG Sim v1.0.23

## Fixes
- **Advanced AI no longer freezes the game.** The rollout search now runs on a background thread, so the window stays responsive while the bot thinks — previously a heavy decision (worst on the opening turns) could hang the app on "Not Responding" until it finished.
- **Advanced AI no longer gets stuck at "Waiting for opponent."** A regression in the threading change discarded the bot's mulligan/turn-order decision; it now dispatches correctly.
- **Coin-flip animation plays whether you win or lose.** When the bot won the flip, its turn-order choice cut the animation short, so you only ever saw the flip on your own wins. The flip itself was always a fair 50/50 (verified over 200k trials).
- **Casual/Ranked block Standard-illegal decks.** Illegal starter decks are now greyed out ("NOT LEGAL") in the picker with a disabled "USE THIS DECK" button, and the queue refuses an illegal deck at every path.
- **Replay: Main Menu button no longer clipped.** The Match Timeline panel's tools now wrap to two rows so Main Menu is fully clickable.
- **Fixed the opening hand deal animating twice when going second.** A mid-deal re-render was restarting the animation; the state (deck/hand counts) was always correct.
