# Puzzle Mode V2: mechanic graphs and cross-turn proofs

This document supersedes the harvest-first conclusion in `puzzle-mode-process.md`.

## Why the previous method failed

The harvested and solution-first sets were correctly proven lethals, but certification proved *solvability*,
not *interestingness*. Most positions reduced to attack order plus power/counter arithmetic.

An interactive OPTCGLab comparison made the gap concrete. A configured combat state produced 93 attacking
lines and 39 defensive paths, with explicit counter and card demand. That is a useful minimum standard for
combat branching, but OPTCGLab itself remains a one-turn, power-centric calculator; it does not supply card
effect dependency graphs or multi-turn play. The simulator uses that branching model as a floor and extends it
through the complete rules engine.

## Player-facing content model

The player-facing rotation is now `CertifiedPuzzleCatalog`, not harvested positions or an unfiltered random
generator. Puzzle Mode constructs 500 lightweight catalog entries and shuffles them with a fresh session RNG
every time the mode opens. Its five authored mechanic dependency graphs are:

- cost reduction -> cost-0 K.O. -> exact attack order and DON;
- On Play Banish grant -> Double Attack -> Trigger denial -> final hit;
- two independent Zoro restands through a Blocker and a Counter Event;
- develop while reserving DON -> opponent attack -> Blocker plus protection Counter -> next-turn lethal;
- attack a Character instead of Life -> remove crackback -> develop -> counter -> next-turn lethal.

Each mechanic graph has five proof-preserving card-identity variants, so cross-turn and real-effect positions
are not vanishingly rare in the shuffled catalog. The remaining 475 are deterministic database-varied combat
trees admitted by the exact selective-decision
audit. They cover seven additional structures: exact DON distribution, single-wall removal, hidden-counter
pressure, wall-plus-counter sequencing, Double Attack, Rush from hand, and two-wall removal. Each accepted
seed has at least two decisions where a proven-winning and proven-losing continuation coexist.

`PuzzleCardCapabilities` scans every unique `CardData` ID after the official JSON loads. It reports mechanic
coverage and supplies conservative card variety: a card is substituted only inside a full gameplay-equivalence
class. Stats, color, type, cost, counter, keywords, features, effect, and Trigger must match, and effect-bearing
cards never cross IDs because the interpreter can contain ID-specific routing. This lets the catalog use the
complete database without invalidating an offline proof by casually replacing a superficially similar card.

Harvesting can still provide candidate states, but it is no longer the content strategy. A harvested state must
pass the same mechanic-diversity and selective-decision gates before entering the rotation.

## Cross-turn proof

`ForcedWinSolver.cs` is the cross-turn oracle. Player-owned main, effect, block, counter, and Trigger choices are
OR nodes; every opponent choice is an AND node. It keeps a fixed deadline measured in player turns and returns
`Unknown` on budget/depth exhaustion. Ending the setup turn is therefore a legal part of a proof, not an
automatic puzzle failure.

Multi-turn candidates must satisfy both:

1. `LethalSolver` proves `NoLethal` this turn.
2. `ForcedWinSolver` proves `Win` within the configured player-turn limit.

`PuzzleRuntime` preserves the original fixed deadline after every player move, auto-plays full opponent turns
with the strongest surviving defense, and stops whenever the player owns a block, counter, Trigger, or effect
decision.

## Difficulty and diversity gates

`PuzzleQualityAnalyzer` audits one-turn puzzles by re-solving every alternative at successive player decisions.
A puzzle needs multiple decisions where proven-winning and proven-losing actions coexist.

`ForcedWinQualityAnalyzer` follows the longest adversarial cross-turn proof and re-solves alternatives at player
decisions. It never treats `Unknown` as a loss. It requires multiple fully resolved consequential decisions,
multiple distinct player action types, a second player turn, and failure of the one-turn oracle.

Mechanic labels are descriptive metadata, not proof. Admission is based on engine outcomes and alternative-move
audits.

### Proof-derived grading

The displayed Easy/Medium/Hard/Expert label is assigned by `PuzzleDifficultyGrader`, not inherited from the
recipe family. The offline certification manifest stores, per seed:

- consequential decisions on the proven line;
- winning versus legal opening moves;
- opening branch breadth;
- interacting defensive layers represented by the family;
- for authored positions, distinct player action types, opponent branches, and player-turn depth.

Repeated micro-actions have diminishing weight: six individual DON attachments are not treated as six separate
strategic concepts. Counter/Double Attack/multi-wall families have a Medium floor, while every fixed two-turn
proof is Expert. Thresholds are stable and score-based, so two entries with equal evidence cannot land in
different tiers merely to fill a quota.

Final catalog distribution: 108 Easy, 142 Medium, 165 Hard, and 85 Expert.

## Solver substrate corrections

This work exposed two proof-system defects:

- `LegalActions` discarded effect choices whose only visible change was a modifier or a replacement
  `". Then,"` clause.
- `LethalSolver`'s transposition key merged states such as "Minotaur with Banish" and "Minotaur without Banish."

Both are fixed. Transposition entries now preserve principal variations, and the displayed proof retains the
longest defending branch rather than the first enumerated "pass" branch.

## Information model

The solver knows the configured opposing hand and Life, but the player does not. The opponent's hand stays
face-down and hover-disabled, and face-down Life stays unknown. The player must infer the defense from the
visible board, active DON, cards consumed during the line, and the puzzle's result. A Life card explicitly
marked face-up is the exception: it renders face-up and passes its actual `CardInstance` to the hover preview.

This intentionally differs from OPTCGLab's probability mode. OPTCGLab enumerates possible hand compositions;
these deterministic exercises choose one hidden defense and require the player to find a line that the exact
engine has proven succeeds against it.

## Current gates

- `mechanicpuzzlecheck`: all five mechanic-graph families certified.
- `puzzlequalityscan`: emitted only one-turn seeds that passed exact lethal plus the alternative-move audit;
  its accepted manifest is baked into `CertifiedPuzzleCatalog`.
- `catalogcheck`: verifies count, unique IDs, family distribution, database capability counts, deterministic
  reconstruction, and sampled exact/runtime solutions.
- `puzzlegradecheck`: verifies every entry has evidence, score and tier agree, all four tiers have meaningful
  populations, and every multi-turn entry remains Expert.
- The two multi-turn entries are explicitly `NoLethal` this turn and `Win` within two player turns.
