# Puzzle Mode V3: gold-standard strategic curriculum

## Why 500 became 30

V2 shipped 500 certified positions, but 475 came from seven generated combat structures and the other 25 were
five authored mechanic graphs repeated through safe card substitutions. The exact solver proved the positions
were winnable and the decision audit found losing alternatives, but neither test proved that two positions
taught different strategic ideas.

V3 deliberately removes the generated manifest from the player rotation. `CertifiedPuzzleCatalog` now contains
30 authored entries with 30 board signatures and 30 categories:

- 21 hand-authored combat-policy positions;
- 4 solution-first constraint positions;
- 5 full mechanic dependency graphs, including 2 cross-turn forced wins.

There are no card-identity variants in the rotation. The old generators and harvesters remain available as
offline candidate tools only.

## Combat model

The combat curriculum is built around the decisions competitive players actually make:

- count the opponent's current hand and the cards future Life damage will add;
- distinguish total counter power from the number of counter cards an attack consumes;
- use the 7K threshold against a 5K Leader to turn a single 2K counter into a two-card response;
- decide when DON!! should be concentrated and when separate natural 6K attackers must each become 7K;
- equalize pressure so a Blocker cannot erase the only expensive attack;
- sequence weak attacks before strong attacks when taking Life adds a counter card;
- price active DON!! and Counter Events into the attack ceiling;
- combine Life, Blockers, hidden counters, Rush, restand, removal, Triggers, and multi-turn crackback.

The hidden-hand arithmetic exercises use the all-2K worst case. The player sees hand quantity, not hand
identities, and the side panel states the public assumption. A winning line therefore never depends on secret
information the player could not infer. Face-up Life is used when the future counter value must be public.

`AuthoredPuzzle.PublicInformation` carries this fair clue. Puzzle Mode displays both `Objective` and
`PublicInformation` before play; the teaching explanation remains hidden until the puzzle is solved.

## Quality gates

Every player-facing entry must pass all of the following:

1. The exact one-turn AND/OR solver proves lethal, or the cross-turn solver proves the fixed turn-limit win.
2. `PuzzleQualityAnalyzer` finds at least two decisions with both winning and losing continuations. Symmetric
   DON!! micro-actions do not qualify unless the aggregate allocation is genuinely load-bearing.
3. `PuzzleRuntime` follows the principal policy against its strongest defense and reaches a solved state.
4. Every catalog ID, category, and complete board signature is unique.
5. Difficulty metadata agrees with the four score bands.

Current validation:

- `puzzlecheck`: 30/30 pass strict quality and solve end-to-end.
- `catalogcheck`: 30 entries, 30 signatures, 30 families, 10/10 sampled exact proofs.
- `puzzlegradecheck`: Easy 2, Medium 2, Hard 13, Expert 13.
- `dotnet build Assembly-CSharp.csproj`: succeeds.

## Difficulty intent

- **Easy:** one visible threshold with no hidden-composition dependency.
- **Medium:** one card-consumption breakpoint plus a tempting inefficient allocation.
- **Hard:** multiple interacting hand, Life, attack-power, or DON!! thresholds.
- **Expert:** layered defensive assignment, Counter Events or card effects, or a response-dependent/multi-turn
  policy.

The distribution is intentionally top-heavy. V3 is a brain-teaser curriculum, not a bulk daily-challenge
feed. New entries should be added only when they introduce a new strategic dependency graph and pass the same
gates.
