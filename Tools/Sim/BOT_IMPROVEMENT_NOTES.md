# Improving the OPTCG bot — where it stands and where to go next

Written 2026-07-14 after building the 3-tier bot system (Beginner / Intermediate / Advanced) and the
out-of-ship discovery platform (`Tools/Sim`). This is an honest technical assessment, not a sales pitch.

---

## Where it is now

**Three tiers, wired into the game** (`GameManager` difficulty router):
- **Beginner** — the original hand-coded `IntermediateBot`.
- **Intermediate** — an evolved `ChampionBot` (6 tuned "style" genes: mulligan bar, attack targeting,
  counter/block thresholds, turn order). Beats Beginner ~54%.
- **Advanced** — `SearchBot`: at every decision it enumerates *every legal action*, shortlists the
  top few by a weighted position eval, plays each out to the end (rollout) with the champion policy,
  and picks the highest-win-rate move. Beats Intermediate ~87%, Beginner ~75%.

**What we learned building it:**
1. **The rollout policy is everything.** With *baseline* rollouts the search bot *lost* to the
   champion (25%); switching rollouts to the *champion* policy flipped it to 87%. A search is only as
   good as the policy it imagines the game being played out with.
2. **A static eval alone is shallow.** The 1-ply eval caps at ~25–32% vs baseline no matter how its
   weights are tuned. The rollouts — not the eval — are what make the Advanced bot strong.
3. **Population tournaments converge fast.** The 6-gene style space is small; the champion re-emerges
   as a stable optimum. More identical tournament cycles validate it but don't advance it.

---

## The biggest limitation (and the biggest opportunity)

**The Advanced bot rolls out with PERFECT INFORMATION.** It clones the full game state — including the
opponent's hand and deck order — and plays it out. Real games are *hidden-information*: you don't know
the opponent's hand, their counters, or their draws. So the bot currently:
- never reasons about what the opponent *might* be holding (counters, blockers, removal),
- can't value bluffs, baiting, or playing around a counter it can't see,
- evaluates every line as if the future were known.

This is why its self-play win rates are strong but its *real-play* skill has a ceiling — and it's
exactly the belief-state modeling the blueprint calls for (§8). **Closing this gap is the single
highest-value improvement.**

---

## Recommended roadmap (in priority order)

### 1. Belief-state / imperfect-information search  ⭐ highest value
Sample plausible opponent hands/deck-orders consistent with everything public (cards played, counters
spent, searches, trash, remaining copy counts — blueprint §8.4/§8.6), and evaluate each candidate move
across *many* sampled worlds. This is **determinized / information-set MCTS (ISMCTS)**. It makes the
bot reason under uncertainty like a real player — respecting counters it can't see, baiting, holding
back — and it's the change most likely to translate to beating humans, not just other bots.

### 2. Proper tree search (MCTS or shallow minimax)
Current search is 1-ply + rollout. A real MCTS tree (UCB selection, node reuse across ticks) or a
2-ply minimax with the rollout as the leaf value would let it reason about the opponent's *best
response*, not just the immediate result. Combine with #1 (ISMCTS) for the full picture.

### 3. A learned value/policy network (AlphaZero-lite)
Rollouts are expensive (~hundreds of ms/decision). Train a value net on the outcomes the search
already generates, and a policy net to rank moves — then use the nets instead of full rollouts. This
makes the bot both **stronger** (better-than-champion rollout guidance) and **faster** (no full
playouts), which in turn makes #1 and #2 affordable at game speed. Bootstrap iteratively: search
generates data → train nets → nets improve search → repeat.

### 4. Calibrate against real ranked data
The whole platform's win rates are **bot-relative** — the bot pilots every deck the same way, so they
reflect the bot's skill × deck power, not true competitive deck tiers (this is why the user's real
ranked matchup matrix, saved in `reference/ranked-matchups-op15-may28.csv`, is better ground truth).
Use that real data to (a) validate the bot's matchup predictions, (b) tune it toward reproducing real
matchup win rates, and (c) score deck strength honestly.

### 5. Richer rollout/heuristic policy
The 6-gene champion caps out because it delegates main-phase play (which cards to play, DON allocation,
attack sequencing) to a fixed heuristic. Parameterizing those decisions gives the tournament new room
and yields a stronger rollout policy — which, per finding #1, lifts the whole Advanced bot.

### 6. Deeper effect / combo reasoning
The legal-action enumerator covers effect targets, A/B choices, and deck-look picks via the
engine-as-oracle trick, but long multi-step combos (search → play → trigger chains) aren't explored to
full depth. Combo-heavy leaders would benefit from deeper effect-resolution search.

### 7. Engineering to make it shippable at scale
- Run the Advanced search **off the main thread** (compute the move async, apply on the next tick) so
  it never hitches the UI. (Noted in `AdvancedAiTick`.)
- Faster/pooled cloning, incremental search (reuse the tree between ticks).
- Verify deck-list ingestion (the WebFetch-scraped meta lists have transcription errors — ~37 of 41
  were clean; a few had wrong copy counts).

---

## My honest one-line recommendation

The Advanced bot is already a strong *perfect-information* player. The next real leap is **teaching it
to reason about hidden information (belief-state sampling / ISMCTS)** — that's where "advanced tactical
skill vs a human" actually lives, and everything else (tree search, value nets, real-data calibration)
compounds on top of it. I'd start there.
