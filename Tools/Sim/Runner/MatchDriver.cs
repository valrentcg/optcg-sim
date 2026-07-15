using System;
using System.Collections.Generic;
using System.Linq;
using OnePieceTcg.Engine;
using OnePieceTcg.Engine.Bot;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// Plays one full headless game by routing every decision through the <see cref="IAgent"/> seam,
    /// while reusing IntermediateBot's proven blacklist/success protocol verbatim
    /// (SnapshotFor / Succeeded / Signature are public exactly so a host can drive one command at a
    /// time). This is the generalized, instrumentable form of IntermediateBot.PlayFullMatch:
    ///   • starting player is FORCED deterministically so paired experiments can swap turn order
    ///     over a matched seed family (§6.3, §11.1);
    ///   • each applied decision is offered to a telemetry sink BEFORE it mutates state (§12.1);
    ///   • the winner and terminal reason are parsed from the engine's own event log.
    /// The engine remains the sole authority on legality and resolution (§1.2).
    /// </summary>
    public static class MatchDriver
    {
        public sealed class Options
        {
            public int CommandCap = 20000;   // deadlock guard, mirrors the harness default
            public IDecisionSink Sink = NullDecisionSink.Instance;
            // When true, the coin-flip winner's AGENT chooses turn order (via a chooseTurnOrder
            // command in its Decide) instead of the driver forcing `firstSeat`. Lets a PolicyAgent
            // apply the discovered turn-order heuristic (§6). Off by default → paired experiments
            // keep their deterministic forced order.
            public bool AgentChoosesTurnOrder = false;
        }

        public static GameRecord Play(
            IAgent south, IAgent north,
            DeckDef southDeck, DeckDef northDeck,
            string seed, string firstSeat,
            Options opts = null)
        {
            opts ??= new Options();
            var rec = new GameRecord
            {
                sDeck = southDeck.Id, nDeck = northDeck.Id,
                sAgent = south.Name, nAgent = north.Name,
                seed = seed, first = firstSeat,
            };

            GameState state = null;
            try
            {
                state = GameEngine.CreateMatch(new MatchConfig
                {
                    SouthDeckDef = southDeck, NorthDeckDef = northDeck, Seed = seed,
                });

                if (!opts.AgentChoosesTurnOrder) ForceTurnOrder(state, firstSeat);

                int total = 0;
                while (state.Status != "finished" && total < opts.CommandCap)
                {
                    int s = DriveSeat(state, south, "south", opts.CommandCap - total, opts.Sink);
                    total += s;
                    int n = DriveSeat(state, north, "north", opts.CommandCap - total, opts.Sink);
                    total += n;
                    if (s == 0 && n == 0) break; // both stuck / match ended between checks
                }

                rec.turns = state.TurnNumber;
                rec.commands = total;

                // Who actually went first: the forced seat, or (agent-chosen mode) whatever the
                // winner picked — read back off the resolved state.
                string effectiveFirst = opts.AgentChoosesTurnOrder ? state.FirstPlayer : firstSeat;
                rec.first = effectiveFirst;

                if (state.Status == "finished")
                {
                    var (winner, deckout) = ReadResult(state);
                    rec.winner = winner;
                    rec.end = deckout ? "deckout" : "life";
                    rec.winnerAgent = winner == "south" ? south.Name : winner == "north" ? north.Name : null;
                    rec.firstWon = winner != null && winner == effectiveFirst;
                }
                else
                {
                    rec.end = total >= opts.CommandCap ? "cap" : "stall";
                }
            }
            catch (Exception ex)
            {
                rec.end = "crash";
                rec.turns = state?.TurnNumber ?? 0;
                rec.error = $"{ex.GetType().Name}: {ex.Message}";
            }

            return rec;
        }

        /// <summary>Continues an ALREADY-STARTED game from its current mid-game state to terminal,
        /// driving both seats with the given policies. Used for search rollouts — "play this position
        /// out and see who wins" — with a chosen playout policy (a strong policy gives accurate
        /// evaluations against strong opponents; baseline rollouts mis-evaluate vs stronger play).</summary>
        public static void Playout(GameState state, IAgent south, IAgent north, int cap)
        {
            int total = 0;
            while (state.Status != "finished" && total < cap)
            {
                int s = DriveSeat(state, south, "south", cap - total, NullDecisionSink.Instance);
                total += s;
                int n = DriveSeat(state, north, "north", cap - total, NullDecisionSink.Instance);
                total += n;
                if (s == 0 && n == 0) break;
            }
        }

        /// <summary>Forces <paramref name="firstSeat"/> to take the first turn regardless of the coin
        /// flip, by issuing the winner's turn-order choice deterministically. This is what lets the
        /// runner measure P(win | go first) vs P(win | go second) on the SAME seed (§6.2). An
        /// advanced agent choosing first/second for itself is a separate concern layered on later.</summary>
        private static void ForceTurnOrder(GameState state, string firstSeat)
        {
            if (state.Status != "coinflip") return;
            string winner = state.CoinFlipWinner; // deterministic from seed
            bool goingFirst = winner == firstSeat; // winner picks so that firstSeat ends up first
            GameEngine.ApplyCommand(state, new GameCommand
            {
                Type = "chooseTurnOrder", Seat = winner, GoingFirst = goingFirst,
            });
        }

        /// <summary>Drains one seat's available actions through its agent, one command at a time,
        /// using the exact success check IntermediateBot uses so no-ops get blacklisted instead of
        /// looping. Returns the number of commands actually applied.</summary>
        private static int DriveSeat(GameState state, IAgent agent, string seat, int budget, IDecisionSink sink)
        {
            int applied = 0;
            var blacklist = new HashSet<string>();
            for (int i = 0; i < budget; i++)
            {
                var cmd = agent.Decide(state, seat, blacklist);
                if (cmd == null) break;

                object before = IntermediateBot.SnapshotFor(state, cmd);
                sink.OnDecision(state, seat, agent.Name, cmd); // record the pre-decision state
                GameEngine.ApplyCommand(state, cmd);
                applied++;
                if (!IntermediateBot.Succeeded(state, cmd, before))
                    blacklist.Add(IntermediateBot.Signature(cmd));
            }
            return applied;
        }

        /// <summary>The engine records terminal outcomes only as event-log lines ("South wins.",
        /// "... has no cards in deck", "... deck reached 0 — they win instead"). Parse the last such
        /// line: winner seat, and whether it was a deck-out finish vs a life/leader finish.</summary>
        private static (string winner, bool deckout) ReadResult(GameState state)
        {
            for (int i = state.EventLog.Count - 1; i >= 0; i--)
            {
                string m = state.EventLog[i].Message;
                if (string.IsNullOrEmpty(m) || m.IndexOf("wins", StringComparison.OrdinalIgnoreCase) < 0) continue;

                string winner =
                    m.StartsWith("South", StringComparison.Ordinal) ? "south" :
                    m.StartsWith("North", StringComparison.Ordinal) ? "north" : null;
                if (winner == null) continue;

                bool deckout = m.IndexOf("no cards in deck", StringComparison.OrdinalIgnoreCase) >= 0
                            || m.IndexOf("deck reached 0", StringComparison.OrdinalIgnoreCase) >= 0;
                return (winner, deckout);
            }
            return (null, false);
        }
    }
}
