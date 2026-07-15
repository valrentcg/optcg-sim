# OPTCG bot tiers — discovery outcome

Three bots, produced by this discovery pipeline (`Tools/Sim`, out-of-ship). Defined in
`Learning/BotTiers.cs` — `BotTiers.Make("beginner"|"intermediate"|"advanced")`.

| Tier | What it is | How it was produced | Strength |
|---|---|---|---|
| **Beginner** | The game's current hand-coded `IntermediateBot` | pre-existing | baseline |
| **Intermediate** | A tuned `WeightedAgent` (6-gene style) | champion of the Elo **population tournament** (`Results/tournament`, Elo 1606) | **~54% vs Beginner** |
| **Advanced** | The **every-legal-action rollout search bot** | `Search/` — enumerate every legal move, roll each of the top-K out to the end, pick the highest-win-rate move; eval weights from `Results/search-eval` | **~75% vs baseline** (4-game); larger validation in progress |

## How the Advanced bot works (the "every legal action discovery bot")

At every decision it:
1. **Enumerates every legal action** (`LegalActions`): play/attach-DON/activate/attack in the main
   phase; block/counter/trigger on defense; effect targets (On Play / Main / trigger), A/B choices,
   deck-look picks, and passes — validated by the engine itself (clone + apply, keep state-changers),
   so Rush and every card effect are covered by the engine as the legality oracle.
2. **Rolls out the top-K candidates** (`SearchAgent`, K=6): shortlists by a fast weighted eval, then
   plays each finalist out to the end and scores by the result ("if I make this move and the game
   plays out, do I win?"). This is the "trial every action, keep the highest win %" method.
3. **Plays the best move.** Everything is on cloned state (`GameClone`, hand-written deep clone),
   so nothing touches the real game until the move is chosen.

## Settled configs (re-run discovery to update)

- Intermediate genome: `MulliganKeep=1.0 FaceBias=0.5 CounterLifeFloor=2.27 CounterCharCost=5.0
  BlockBias=0.5 TurnOrderFirst=1.0`
- Advanced eval weights: `[1.87, 0.60, 1.20, 0.50, -0.38, -0.49, 0.40, 0.38, 0.00, -0.43]`,
  rollout cap 4000, shortlist 6.

## CLI

```
dotnet run -c Release -- tiers advanced beginner 20     # head-to-head win rate between any two tiers
dotnet run -c Release -- searchtest 8 4000              # advanced (rollout) vs baseline
dotnet run -c Release -- evolve-search --gens 30        # evolve the eval weights
dotnet run -c Release -- evolve --pop 48 --gens 30      # population tournament (intermediate style)
```

## Known limits / next

- The advanced bot is **strong but slow** (~15 s/game with shortlisting). Running *all* permutations
  of discovery exhaustively is compute-bound; the settled config above is the working advanced bot.
- 1-ply eval alone caps ~30% vs baseline — rollouts are what make it strong. Deeper (multi-ply)
  search or a learned value net would push it further.
- **Game integration** (making the shipped game use Beginner/Intermediate/Advanced) touches shipping
  code (`Assets`) and is deliberately left for explicit sign-off; this platform stays out-of-ship.
