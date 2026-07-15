using System;
using System.Collections.Generic;
using System.Linq;
using OnePieceTcg.Engine;
using OnePieceTcg.Sim.Search;   // LegalActions.StateFingerprint

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

        public static GameState Play(IAgent south, IAgent north, DeckDef sDef, DeckDef nDef,
            string seed, string first, int cmdCap = 20000, int maxTurns = 60)
        {
            Stuck = false;
            var st = GameEngine.CreateMatch(new MatchConfig { SouthDeckDef = sDef, NorthDeckDef = nDef, Seed = seed });
            GameEngine.ApplyCommand(st, new GameCommand { Type = "chooseTurnOrder", Seat = st.CoinFlipWinner, GoingFirst = st.CoinFlipWinner == first });

            int total = 0;
            while (st.Status != "finished" && total < cmdCap && st.TurnNumber <= maxTurns)
            {
                int did = 0;
                foreach (var (ag, seat) in new[] { (south, "south"), (north, "north") })
                {
                    var bl = new HashSet<string>();
                    for (int i = 0; i < 2000; i++)
                    {
                        var cmd = ag.Decide(st, seat, bl);
                        if (cmd == null) break;
                        string sig = Sig(cmd);
                        if (bl.Contains(sig)) break;                  // bot keeps offering a known no-op → stop it
                        long before = LegalActions.StateFingerprint(st);
                        GameEngine.ApplyCommand(st, cmd);
                        total++; did++;
                        if (LegalActions.StateFingerprint(st) == before) bl.Add(sig);   // no-op → blacklist
                        if (st.Status == "finished") break;
                    }
                    if (st.Status == "finished") break;
                }
                if (did == 0) { Stuck = true; break; }   // neither seat could act — see Stuck
            }
            return st;
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
