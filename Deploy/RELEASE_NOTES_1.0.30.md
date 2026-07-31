## Whose turn it is

The turn announcement is rebuilt. A tide line lifts off your edge of the board, your name is revealed in its wake, and the line settles clear of the letters before sinking back the way it came.

- It uses the player's actual name in every mode now, not just hotseat — "VALREN'S TURN" rather than "YOUR TURN".
- The side it rises from is what tells you whose turn it is, so it still reads if the two colours are hard to tell apart. Amber for you, violet for your opponent.

## Triggers

- Using a [Trigger] now costs you visibly: the card flies out of your Life stack, burns at centre screen the way an Event does, and reforms in the trash. Characters too — using a trigger spends the card whatever it is.
- A Life card with no [Trigger] no longer offers a choice you cannot make. It just says **Draw the Card**.
- The Trigger step is always entered, even on a card with no [Trigger]. Resolving instantly used to tell your opponent exactly which of your Life cards were Triggers.

## Chat

- Several friends can be open at once, docked along the bottom right, each collapsed to its title bar or expanded with its own history and unread count.
- Opening a second conversation used to throw the first one away.

## In-match options

- Match Options moved into the in-game menu and opens as a window you can close again, instead of a tab tucked against the edge of the board.
- You are no longer asked to confirm ending your turn or playing a counter by default. Both are still toggles.

## Sound

- A Life card turning face-up has a sound — taking damage, or an effect like Wyper or Shirahoshi turning one over.
- Playing a Character or Stage lands with a sound as it hits the board.

## Deck builder

- Deck lists import correctly. A line like `4x Nami (OP01-016)` matched nothing, so a 50-card list came in as one of everything, and codes the game does not know yet arrived as phantom copies instead of being reported.
- Searching is far more forgiving. Punctuation, accents and apostrophes are ignored, so "youre" finds "You're" and "monkey d luffy" finds "Monkey.D.Luffy", and several words can match across a card's name and its text at once.
- Deck legality is enforced when you save or pick a deck. Wrong card count, more than four copies, off-colour cards and leader restrictions only tinted a badge before.
- The in-deck count bubble and the profile tier badge are readable against any card art.

## Fixes underneath

- A deck file that failed to read could be overwritten on the next save, taking every deck and sealed run with it. Saves are now written atomically with a backup, and refuse to overwrite anything they could not read.
- A networked match aborts with a clear message if the two players are on different versions or different card data, instead of quietly drifting apart mid-game.
- Restore codes are bounded, so a malformed one can no longer lock up the client.
- Timed matches cannot hang forever when an opponent's clock stops reporting.
- Quitting a ranked match by force now settles as a loss instead of leaving the result unrecorded.
- In timed matches the opponent's clock now sits in their own Leader-to-Life gap, mirroring yours, instead of being crowded onto the same side of the board as your own clock.
- The updater's "What's New" panel actually lists what changed while it downloads. It has been blank on every update until now — the notes were never embedded into the release being packaged.
