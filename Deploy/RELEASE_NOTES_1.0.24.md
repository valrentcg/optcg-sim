# OPTCG Sim v1.0.24

## Fixes
- **The Advanced A.I. no longer freezes on searcher cards.** Playing a card that looks at the top of its deck (OP16-091 Nami and others) could leave the bot thinking for many seconds before it moved. Its practice playouts were spending almost their entire budget re-attempting one attack the rules never allow — the trigger was a Leader that prints "This Leader cannot attack" (OP15-039 Rebecca), but the stall could surface in any matchup. That decision now takes a fraction of the time, and because those playouts finally run to a result, the bot's choice on searches and effect targets is based on completed simulations instead of a cut-off guess.
- **The Advanced A.I. no longer locks up on a [Trigger].** When a Trigger's own text queued a follow-up decision — K.O. a Character, hand out +1000 power — the bot kept re-activating the Trigger instead of resolving what it had just queued, and the match stopped advancing.
- **A.I. matches against an Imu deck no longer hang before the first turn.** Imu (OP13-079) plays a Stage from the deck at the very start of the game; on every difficulty, the bot never answered that prompt and the match never began.
- **You are no longer asked to make the opponent's decisions.** In a game against the A.I. you could be shown live buttons belonging to the bot — "Take None" on the bot's deck search, a Choose A / Choose B branch, and Go First / Go Second after losing the coin flip. Answering one of those decided it on the bot's behalf and could leave the game stuck. Each now shows a "waiting for opponent" message instead. Hotseat and Versus Self are unchanged, since one player controls both sides there.

## Under the hood
- New headless bot-stall sweep across every starter and meta deck, checking each difficulty for decisions that are too slow or that can never complete. All three difficulties are clean; it runs as a gate before future releases.
