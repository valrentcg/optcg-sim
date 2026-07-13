# v1.0.10 — Gameplay fixes + match flow (Bug Testing Season)

A big engine + match-flow pass, verified with a new headless battle harness (`Tools/Harness/`).

## Match flow
- **Win / Lose result screen** at the end of Ranked & Casual matches — the only exit is Main Menu, which tears down the connection cleanly.
- **Back-to-back matches connect.** Netcode is now shut down when a match ends, so the 2nd match of a session no longer hangs on "Connecting to your opponent…".
- **On a loss, the opponent's hand and Life are revealed** (all modes).

## Card / rules fixes
- **[Double Attack]** now deals **2 Life damage in one hit** (and can win at 1 Life) instead of a mis-modeled second attack.
- **Counter events** (e.g. Blaze Slice) can be used to counter again — they were wrongly treated as non-counters.
- **Black Uta** (ST08-002) is K.O.'d by Character attacks (only immune to Leaders).
- **Gum-Gum Bell** trigger adds a black *Character* (not any card) from trash.
- **Black Luffy's** leader effect (rested DON!! on a K.O.) and **Shanks'** defensive leader effect now fire.
- **Battle-effect hangs fixed** — attacks whose target is bounced mid-battle no longer freeze the game.
- Battle-K.O.-immunity now respects attribute / condition qualifiers instead of being immune to everything.

## Quality of life
- Summoning-sick characters (played this turn, no [Rush]) are **greyed out**.
- A small **shield** marks active [Blocker] characters.
- Play a **6th character over an existing one** to trash it (full-board replace).
- Your **deck searches are hidden from your opponent** (they no longer see the cards you look at).

## Notes
- Ready-check is a 15-second window — both players should Accept promptly.
