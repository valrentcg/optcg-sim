# v1.0.21 — Deeper Puzzles, multi-turn challenges, and official keyword fixes

This release rebuilds Puzzle mode around consequential sequencing instead of shallow all-out attacks,
adds genuine multi-turn forced-win challenges, grades all 500 puzzles from proof evidence, and corrects
several shared keyword and timing rules across the game engine.

## Puzzles rebuilt around real decisions

- Puzzle mode now shuffles **500 graded challenges** across **12 tactical families**, with **478 distinct
  board states** instead of repeating the same attack pattern.
- Harder positions are built backward from a required solution and reject **any-order attack lines**.
  Legacy harvested positions no longer enter the player rotation merely because a basic lethal exists.
- **Ten Expert puzzles span two of your turns:** develop or control the board, survive the opponent's
  strongest reply, then complete the forced win.
- The opponent's hand remains hidden. Puzzle titles and the neutral starting prompt no longer disclose
  the tactic before you make a decision.
- **Give Up** is now separate from **End Turn**, because ending the setup turn is a required move in
  multi-turn puzzles.

## Proof-based difficulty grading

- Every puzzle is scored from its verified decision tree: consequential choices, attack order,
  defensive branches, resource precision, counters, setup plays, and multi-turn depth.
- The final catalog is **108 Easy, 142 Medium, 165 Hard, and 85 Expert**.
- The solved screen shows the mechanics involved and explains the evidence behind the difficulty grade.
- Database-aware substitutions vary leaders, Characters, Events, and Stages only within safe,
  role-compatible card capabilities.

## Official keyword and timing fixes

- **[Rush: Character]** now permits attacks on Characters during the turn the card was played without
  granting full **[Rush]** or permission to attack active Characters.
- Activated **[Trigger]** cards move to the correct zone. A Trigger—including **“Play this card”** and
  its On Play choices or deck-look effects—finishes before **[Double Attack]** proceeds to the next Life.
- Printed **[Unblockable]** is recognized even when imported keyword metadata is absent.
- **[Once Per Turn]** usage resets when that physical card leaves and re-enters the field.
- Counter Events may increase another eligible friendly Leader or Character, matching the official Q&A.
- The exact official **[End of Your Opponent's Turn]** timing is recognized and displayed.
- Directly trashing a Character remains distinct from K.O. and does not incorrectly activate **[On K.O.]**.

## Card visibility

- Hovering a **face-up Life card** now previews that card's face instead of showing the card back.
- Puzzle Life and the opponent's hand continue to follow normal hidden-information rules even though the
  proof solver knows the configured defense.

## Verification

- Puzzle catalog: **500 entries**, **478 board signatures**, **12 families**, **10 multi-turn**.
- Difficulty grading: **500/500 classified with no grading errors**.
- Official keyword suite: **16/16 passed**.
- Puzzle/lethal regression suite: **14/14 passed**.
- Deterministic command and hidden-information boundary fixtures: **passed**.
- Unity script build: **0 errors**.
