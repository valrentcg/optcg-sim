using System;
using System.Collections.Generic;
using System.Linq;
using OnePieceTcg.Engine;
using OnePieceTcg.Sim.Search;   // LegalActions.StateFingerprint, KnowledgeState, DerivedSeed
using OnePieceTcg.Sim.Runner;   // ObservedSeam

namespace OnePieceTcg.Sim.Planning
{
    /// <summary>
    /// Drives two agents to the end of a game with NO IntermediateBot dependency: no-op commands are
    /// detected generically via the real-state fingerprint (and blacklisted so a bot can't spin on one),
    /// and a stuck-game guard caps total commands and total turns so a pathological matchup aborts as a
    /// no-result instead of grinding forever. Used by the planner tests and tournament.
    /// </summary>
    public static class GameRunner
    {
        /// <summary>Set when a game ABORTED because neither seat could produce a legal command — i.e. the
        /// game did not end, it got STUCK. This is not a slow game or a long game: it is a forfeit.
        /// Cause seen 2026-07-15: an engine effect raises a prompt that LegalActions cannot generate a
        /// command for (e.g. Gecko Moria's "play a {Thriller Bark Pirates} from your trash"), so Decide
        /// returns null, `did == 0`, and Play() silently returns a half-finished game which rules-8.2
        /// adjudication then awards to whoever happens to be ahead. The bot loses games it never played —
        /// often by playing its OWN card. Utterly invisible in win rate, which is why it survived this long.
        /// [ThreadStatic] because games run in parallel.</summary>
        [ThreadStatic] public static bool Stuck;
        public static bool EnforceCardConservation;

        /// <summary>⚠ UNSAFE-LEGACY ENTRY. Any plain <see cref="IAgent"/> passed here is AUTOMATICALLY
        /// treated as an <see cref="ILegacyAgent"/> and handed the raw referee <see cref="GameState"/> — this
        /// overload is perfect-information by construction. It exists for the historical tournament/duel
        /// callers; it is NOT an honest path. Honest agents implement <see cref="IObservedAgent"/> and go
        /// through the object overload, which projects. This overload cannot receive an observed agent (type
        /// mismatch), so it never silently downgrades one — but a caller using it opts every agent into
        /// perfect information whether or not the agent marked itself <see cref="ILegacyAgent"/>.</summary>
        public static GameState Play(IAgent south, IAgent north, DeckDef sDef, DeckDef nDef,
            string seed, string first, int cmdCap = 20000, int maxTurns = 60)
            => Play(AsLegacy(south), AsLegacy(north), sDef, nDef, seed, first, ListAssumption.UnknownList, cmdCap, maxTurns);

        /// <summary>Wrap a plain <see cref="IAgent"/> as an EXPLICIT <see cref="ILegacyAgent"/> so it enters
        /// the object-typed loop through the legacy (perfect-info) branch on purpose. The object overload
        /// rejects anything that is neither <see cref="ILegacyAgent"/> nor <see cref="IObservedAgent"/>, so an
        /// agent can only get the raw GameState by being marked legacy — never by accident.</summary>
        private static ILegacyAgent AsLegacy(IAgent a) => a as ILegacyAgent ?? new LegacyAgentAdapter(a);

        private sealed class LegacyAgentAdapter : ILegacyAgent
        {
            private readonly IAgent _inner;
            public LegacyAgentAdapter(IAgent inner) { _inner = inner; }
            public string Name => _inner.Name;
            public GameCommand Decide(GameState state, string seat, HashSet<string> blacklist) => _inner.Decide(state, seat, blacklist);
        }

        /// <summary>Drive two agents to the end of a game. Each agent is EITHER an <see cref="IAgent"/> (the
        /// unsafe legacy control — it receives the referee <see cref="GameState"/>) OR an
        /// <see cref="IObservedAgent"/> (honest — the RUNNER projects its <see cref="Search.PlayerObservation"/>,
        /// enumerates its legal actions as opaque tokens, and translates its choice back). An observed agent
        /// never touches the GameState here; the projection and the token→command map live in this loop only.
        /// <paramref name="assumption"/> is the experiment's list assumption (OpenList exposes the opponent's
        /// list to the projection; UnknownList does not).</summary>
        public static GameState Play(object south, object north, DeckDef sDef, DeckDef nDef,
            string seed, string first, ListAssumption assumption = ListAssumption.UnknownList,
            int cmdCap = 20000, int maxTurns = 60)
        {
            Stuck = false;
            var st = GameEngine.CreateMatch(new MatchConfig { SouthDeckDef = sDef, NorthDeckDef = nDef, Seed = seed });
            GameEngine.ApplyCommand(st, new GameCommand { Type = "chooseTurnOrder", Seat = st.CoinFlipWinner, GoingFirst = st.CoinFlipWinner == first });

            // Per-seat MONOTONIC decision sequence, owned by the runner (not any agent's local counter), so an
            // observed agent's derived world seeds are stable and comparable across configurations.
            var seq = new Dictionary<string, int> { ["south"] = 0, ["north"] = 0 };

            // Agents that maintain a persistent side-record (e.g. a knowledge ledger) get every applied command
            // reported with before/after states. Only clone-per-command when at least one such observer exists.
            var observers = new[] { south, north }.OfType<ICommandObserver>().ToArray();

            int total = 0;
            while (st.Status != "finished" && total < cmdCap && st.TurnNumber <= maxTurns)
            {
                int did = 0;
                foreach (var (ag, seat, ownDef, oppDef) in new[]
                    { (south, "south", sDef, nDef), (north, "north", nDef, sDef) })
                {
                    var bl = new HashSet<string>();
                    for (int i = 0; i < 2000; i++)
                    {
                        var cmd = NextCommand(ag, st, seat, bl, ownDef, oppDef, assumption, seq, seed);
                        if (cmd == null) break;
                        string sig = Sig(cmd);
                        if (bl.Contains(sig)) break;                  // bot keeps offering a known no-op → stop it
                        long before = LegalActions.StateFingerprint(st);
                        var snap = observers.Length > 0 ? GameClone.Clone(st) : null;   // before-image for observers
                        GameEngine.ApplyCommand(st, cmd);
                        if (EnforceCardConservation)
                        {
                            ValidateCardConservation(st, "south", sDef, cmd);
                            ValidateCardConservation(st, "north", nDef, cmd);
                        }
                        total++; did++;
                        foreach (var obs in observers) obs.ObserveApplied(cmd, snap, st);
                        if (LegalActions.StateFingerprint(st) == before) bl.Add(sig);   // no-op → blacklist
                        if (st.Status == "finished") break;
                    }
                    if (st.Status == "finished") break;
                }
                if (did == 0) { Stuck = true; break; }   // neither seat could act — see Stuck
            }
            return st;
        }

        private static void ValidateCardConservation(GameState st, string owner, DeckDef list, GameCommand cmd)
        {
            var p = st.Players[owner];
            int expected = list.List.Where(e => e.cardId != list.Leader).Sum(e => e.qty);
            int dl = st.DeckLook?.Seat == owner
                ? (st.DeckLook.Cards?.Count ?? 0) + (st.DeckLook.Ordered?.Count ?? 0) : 0;
            int revealed = st.Battle?.TargetSeat == owner && st.Battle.RevealedLife != null ? 1 : 0;
            int chars = p.CharacterArea.Count(c => c != null);
            int actual = p.Deck.Count + p.Hand.Count + p.Life.Count + chars + (p.Stage == null ? 0 : 1)
                + p.Trash.Count + dl + revealed;
            if (actual != expected)
                throw new InvalidOperationException(
                    $"card conservation failed after {cmd.Type} seat={cmd.Seat} instance={cmd.InstanceId} " +
                    $"target={cmd.Target}: owner={owner} actual={actual} expected={expected}; " +
                    $"deck={p.Deck.Count} hand={p.Hand.Count} life={p.Life.Count} chars={chars} " +
                    $"stage={(p.Stage == null ? 0 : 1)} trash={p.Trash.Count} deckLook={dl} revealed={revealed}; " +
                    $"turn={st.TurnNumber} phase={st.Phase}");
        }

        /// <summary>Get the next command from either agent kind. For an <see cref="IObservedAgent"/> the
        /// runner projects, enumerates+tokenizes the legal actions, calls the agent with the sanitized
        /// context, and translates the choice back (fail-closed). The GameState is never handed across.</summary>
        private static GameCommand NextCommand(object agent, GameState st, string seat, HashSet<string> bl,
            DeckDef ownDef, DeckDef oppDef, ListAssumption assumption, Dictionary<string, int> seq, string matchSeed)
        {
            if (agent is IObservedAgent obs)
            {
                var knowledge = new KnowledgeState
                {
                    Seat = seat,
                    OwnList = ownDef,
                    OpponentList = assumption == ListAssumption.OpenList ? oppDef : null,
                    Assumption = assumption,
                };
                int s = seq[seat]++;
                int seedBasis = DerivedSeed.For(matchSeed, seat, st.TurnNumber, s, worldIndex: 0);
                var decision = ObservedSeam.Build(st, seat, knowledge, matchSeed, s, seedBasis);
                if (decision.ActionCount == 0) return null;
                return decision.Translate(obs.Decide(decision.Context));   // fail-closed on unknown token
            }
            // FAIL CLOSED: only an EXPLICIT legacy agent may receive the raw GameState. An arbitrary object
            // (or a plain IAgent that never opted into ILegacyAgent) is rejected rather than silently handed
            // the truth — the boundary is not a default, it is a marked choice.
            if (agent is ILegacyAgent legacy) return legacy.Decide(st, seat, bl);
            throw new InvalidOperationException(
                $"agent '{agent?.GetType().Name ?? "null"}' is neither ILegacyAgent nor IObservedAgent — refusing to hand it a GameState.");
        }

        /// <summary>The seat that won OUTRIGHT, or null if the game was cut short by the turn/command cap.
        /// Distinct from <see cref="Result"/> on purpose: telling a real win from an adjudicated one is how
        /// we measure what fraction of games actually finish.</summary>
        public static string Winner(GameState st)
        {
            for (int i = st.EventLog.Count - 1; i >= 0; i--)
            {
                var m = st.EventLog[i].Message;
                if (m != null && m.Contains("wins")) return m.StartsWith("South") ? "south" : "north";
            }
            return null;   // no-result (aborted by the stuck guard)
        }

        /// <summary>The game's result: an outright win, else the official time-limit adjudication.
        /// This is what a tournament should score, because a game stopped at the cap is not "no data" —
        /// it is a real position with a real rules-defined winner.</summary>
        public static string Result(GameState st) => Winner(st) ?? Adjudicate(st);

        /// <summary>Comprehensive Rules 8.2: when a match hits its time limit unfinished, the winner is
        /// decided by (1) most Life cards, (2) most cards left in deck, (3) most Characters in the
        /// Character area, (4) whoever last drew from their Life area. Null only if all four tie — the
        /// rulebook's own "null match without a winner".
        ///
        /// The turn cap stands in for the clock here. We do NOT play the rule's "additional three turns"
        /// first: that exists so a match interrupted mid-round gives both players equal turns, whereas the
        /// cap already stops on an even boundary (turns 1..maxTurns with maxTurns even = the same number
        /// each), so the extra turns would only move the cap, not make it fairer.
        ///
        /// NOTE (deliberate, see 2026-07-14 decision): procedure 1 is RAW life, not a life differential.
        /// That rewards preserving your own life over pressuring theirs, which is a genuine turtle
        /// incentive — it is imported knowingly because it is the real game's incentive. The held-out
        /// IntermediateBot benchmark is what guards against it: if the population learns to turtle for the
        /// cap, its absolute win rate against a fixed opponent falls even as ladder Elo rises.</summary>
        public static string Adjudicate(GameState st)
        {
            if (!st.Players.TryGetValue("south", out var s) || !st.Players.TryGetValue("north", out var n))
                return null;

            int cmp = s.Life.Count.CompareTo(n.Life.Count);                                  // 1. life
            if (cmp == 0) cmp = s.Deck.Count.CompareTo(n.Deck.Count);                        // 2. deck
            if (cmp == 0) cmp = s.CharacterArea.Count(c => c != null)
                                .CompareTo(n.CharacterArea.Count(c => c != null));           // 3. characters
            if (cmp != 0) return cmp > 0 ? "south" : "north";

            // 4. whoever most recently drew a Life card to hand — i.e. last took damage. LogEntry.Actor is
            // the damaged seat (GameEngine logs it as defenderSeat), so read that rather than the prose.
            for (int i = st.EventLog.Count - 1; i >= 0; i--)
            {
                var e = st.EventLog[i];
                if (e.Message != null && e.Message.Contains("takes 1 damage")
                    && (e.Actor == "south" || e.Actor == "north")) return e.Actor;
            }
            return null;   // nobody ever took damage and all else tied → null match
        }

        private static string Sig(GameCommand c) =>
            string.Join("|", c.Type, c.Seat, c.InstanceId, c.Target, c.Attacker, c.Blocker, c.EffectId, c.Amount);
    }
}
