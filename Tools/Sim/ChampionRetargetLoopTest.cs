using System;
using System.Collections.Generic;
using System.Linq;
using OnePieceTcg.Engine;
using OnePieceTcg.Engine.Bot;
using OnePieceTcg.Engine.Bot.Search;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// Regression for report 20260725-045539-685 ("Advanced bot getting stuck on searcher decision making").
    ///
    /// Two defects compounded into a rollout LIVELOCK, reproduced from the reported position:
    ///   1. IntermediateBot's attacker list honoured the dynamic "cannotAttack" modifier but not the
    ///      PRINTED one ("This Leader cannot attack." — OP15-039 Rebecca), so it kept proposing an
    ///      attacker the engine can never accept.
    ///   2. ChampionBot (the Advanced bot's ROLLOUT policy) then rewrote that attack's Target to the
    ///      opponent's Leader AFTER IntermediateBot had checked the caller's no-op blacklist. Every
    ///      re-target collapsed onto one rejected signature while IntermediateBot kept offering fresh,
    ///      un-blacklisted CHARACTER targets for the same attacker — so an identical rejected command was
    ///      re-issued until the rollout's command budget ran out.
    ///
    /// Measured before the fix, on the reported position: 9,403 identical no-ops, ~94% of every playout,
    /// not one playout reaching a terminal state, ~8s per decision on CoreCLR (worse on the player's Mono).
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- retargetlooptest
    /// </summary>
    public static class ChampionRetargetLoopTest
    {
        private static int passed, failed;

        public static int Run()
        {
            Console.WriteLine("=== ChampionBot / IntermediateBot attack-loop regression ===");
            BotDoesNotProposeAPrintedCannotAttackAttacker();
            RetargetDoesNotReviveABlacklistedFaceAttack();
            RetargetStillPrefersTheFace();
            RolloutsTerminateInsteadOfBurningTheCommandCap();
            Console.WriteLine($"retargetlooptest: {passed}/{passed + failed} passed ({failed} failed)");
            return failed == 0 ? 0 : 1;
        }

        // Defect 1. Rebecca's Leader carries "This Leader cannot attack." — she must never be proposed.
        private static void BotDoesNotProposeAPrintedCannotAttackAttacker()
        {
            var st = RebeccaPosition(out string seat, "rebecca-propose");
            var leaderId = st.Players[seat].Leader?.InstanceId;
            bool proposed = false;
            var bl = new HashSet<string>();
            for (int i = 0; i < 300 && st.Status != "finished"; i++)
            {
                var cmd = IntermediateBot.DecideOneCommand(st, seat, bl);
                if (cmd == null) { seat = GameEngine.OtherSeat(seat); leaderId = st.Players[seat].Leader?.InstanceId; continue; }
                if (cmd.Type == "declareAttack" && cmd.Attacker == leaderId
                    && GameEngine.HasPrintedCannotAttack(st, st.Players[seat].Leader))
                    proposed = true;
                object before = IntermediateBot.SnapshotFor(st, cmd);
                GameEngine.ApplyCommand(st, cmd);
                if (!IntermediateBot.Succeeded(st, cmd, before)) bl.Add(IntermediateBot.Signature(cmd));
            }
            Check("never proposes an attacker with a printed \"cannot attack\"", !proposed);
        }

        // Defect 2, isolated from defect 1: pre-blacklist the FACE version of whatever attack the bot wants,
        // then assert the re-target declines to revive it rather than handing back a known no-op.
        private static void RetargetDoesNotReviveABlacklistedFaceAttack()
        {
            // The attacker filter above removes the ONE trigger we know of, so to test the amplifier on its
            // own we recreate the state it produces directly: POISON the blacklist with the face version of
            // every possible attack, exactly as a rollout would after the engine rejected them. A re-target
            // that ignores the blacklist must then hand back a command it was just told is a no-op.
            int violations = 0, exercised = 0;
            for (int t = 0; t < 40; t++)
            {
                var st = RebeccaPosition(out string seat, "rebecca-retarget-" + t);
                // Need a proposal aimed at a CHARACTER: that is the shape where the face re-target
                // produces a different signature from the one IntermediateBot blacklist-checked.
                var probe = FindNonFaceAttackProposal(st, ref seat);
                if (probe == null) continue;
                string oppLeader = st.Players[GameEngine.OtherSeat(seat)].Leader.InstanceId;
                var bl = new HashSet<string>
                {
                    IntermediateBot.Signature(new GameCommand
                    { Type = "declareAttack", Seat = seat, Attacker = probe.Attacker, Target = oppLeader }),
                };
                var cmd = ChampionBot.DecideOneCommand(st, seat, bl);
                if (cmd == null || cmd.Type != "declareAttack") continue;
                exercised++;
                if (bl.Contains(IntermediateBot.Signature(cmd))) violations++;
            }
            Check($"re-target respects the blacklist ({violations} violations over {exercised} attack decisions)",
                exercised > 0 && violations == 0);
        }

        // The guard must not disable FaceBias: with an empty blacklist, attacks still go at the Leader.
        private static void RetargetStillPrefersTheFace()
        {
            var st = RebeccaPosition(out string seat, "rebecca-face");
            var probe = FindAnAttackProposal(st, ref seat);
            if (probe == null) { Check("(setup) reached a position with an attack proposal", false); return; }
            string oppLeader = st.Players[GameEngine.OtherSeat(seat)].Leader.InstanceId;
            var cmd = ChampionBot.DecideOneCommand(st, seat, new HashSet<string>());
            Check("FaceBias still sends attacks at the opponent's Leader",
                cmd != null && cmd.Type == "declareAttack" && cmd.Target == oppLeader);
        }

        // End-to-end: SearchBot's rollout must reach a decision, not grind to the command cap on rejects.
        private static void RolloutsTerminateInsteadOfBurningTheCommandCap()
        {
            int worstApplied = 0; double worstNoop = 0; int finished = 0; const int trials = 8;
            for (int t = 0; t < trials; t++)
            {
                var st = RebeccaPosition(out _, "rebecca-rollout-" + t);
                int applied = 0, noop = 0, total = 0;
                while (st.Status != "finished" && total < SearchBot.RolloutCap)
                {
                    int moved = 0;
                    foreach (var s in new[] { "south", "north" })
                    {
                        var bl = new HashSet<string>();
                        for (int i = 0; i < SearchBot.RolloutCap - total; i++)
                        {
                            var cmd = ChampionBot.DecideOneCommand(st, s, bl);
                            if (cmd == null) break;
                            object before = IntermediateBot.SnapshotFor(st, cmd);
                            GameEngine.ApplyCommand(st, cmd);
                            applied++; moved++; total++;
                            if (!IntermediateBot.Succeeded(st, cmd, before)) { noop++; bl.Add(IntermediateBot.Signature(cmd)); }
                        }
                    }
                    if (moved == 0) break;
                }
                if (st.Status == "finished") finished++;
                worstApplied = Math.Max(worstApplied, applied);
                worstNoop = Math.Max(worstNoop, applied == 0 ? 0 : (double)noop / applied);
            }
            Console.WriteLine($"    worst playout: {worstApplied} commands, {worstNoop:P0} no-ops, {finished}/{trials} reached a terminal state");
            Check($"no playout burns the command cap (worst {worstApplied} < {SearchBot.RolloutCap})", worstApplied < SearchBot.RolloutCap);
            Check($"no-op rate stays sane (worst {worstNoop:P0} < 50%)", worstNoop < 0.50);
            Check($"playouts reach a terminal state ({finished}/{trials})", finished == trials);
        }

        /// <summary>Advance until some seat wants to attack a CHARACTER (not the Leader) — the only shape
        /// where the FaceBias re-target changes the command's signature after the blacklist check.</summary>
        private static GameCommand FindNonFaceAttackProposal(GameState st, ref string seat)
        {
            var bl = new HashSet<string>();
            for (int i = 0; i < 600 && st.Status != "finished"; i++)
            {
                string acting = st.ActiveSeat;
                var probe = IntermediateBot.DecideOneCommand(st, acting, bl)
                            ?? IntermediateBot.DecideOneCommand(st, acting = GameEngine.OtherSeat(acting), bl);
                if (probe == null) return null;
                if (probe.Type == "declareAttack"
                    && probe.Target != st.Players[GameEngine.OtherSeat(acting)].Leader?.InstanceId)
                { seat = acting; return probe; }
                object before = IntermediateBot.SnapshotFor(st, probe);
                GameEngine.ApplyCommand(st, probe);
                if (!IntermediateBot.Succeeded(st, probe, before)) bl.Add(IntermediateBot.Signature(probe));
            }
            return null;
        }

        /// <summary>Advance until some seat wants to declare an attack; returns that proposal without
        /// applying it, leaving <paramref name="seat"/> set to the seat that made it.</summary>
        private static GameCommand FindAnAttackProposal(GameState st, ref string seat)
        {
            var bl = new HashSet<string>();
            for (int i = 0; i < 400 && st.Status != "finished"; i++)
            {
                foreach (var s in new[] { st.ActiveSeat, GameEngine.OtherSeat(st.ActiveSeat) })
                {
                    var probe = IntermediateBot.DecideOneCommand(st, s, bl);
                    if (probe == null) continue;
                    if (probe.Type == "declareAttack") { seat = s; return probe; }
                    object before = IntermediateBot.SnapshotFor(st, probe);
                    GameEngine.ApplyCommand(st, probe);
                    if (!IntermediateBot.Succeeded(st, probe, before)) bl.Add(IntermediateBot.Signature(probe));
                    break;
                }
            }
            return null;
        }

        /// <summary>The reported matchup's shape: a Dressrosa deck led by OP15-039 Rebecca, whose Leader
        /// carries the printed "This Leader cannot attack." — the restriction that triggered the livelock.
        /// Played forward a few turns so rest state and board are whatever the engine really produces.</summary>
        private static GameState RebeccaPosition(out string seat, string seed)
        {
            var dressrosa = new DeckDef
            {
                Id = "rebecca", Name = "Dressrosa", Leader = "OP15-039",
                List = new List<(string cardId, int qty)>
                {
                    ("OP15-039", 1), ("OP15-040", 4), ("OP15-042", 4), ("OP15-047", 4), ("OP15-051", 4),
                    ("OP15-052", 4), ("OP15-053", 4), ("OP10-045", 4), ("OP10-049", 4), ("OP10-054", 4),
                    ("OP16-056", 4), ("OP07-051", 4), ("OP14-049", 3), ("OP06-058", 4),
                },
            };
            var wano = new DeckDef
            {
                Id = "wano", Name = "Land of Wano", Leader = "OP16-079",
                List = new List<(string cardId, int qty)>
                {
                    ("OP16-079", 1), ("OP16-091", 4), ("OP16-092", 4), ("OP16-081", 2), ("OP16-087", 4),
                    ("OP16-088", 2), ("OP13-093", 3), ("OP16-082", 4), ("OP16-084", 4), ("OP16-098", 4),
                    ("OP15-092", 2), ("OP16-096", 4), ("OP16-097", 4), ("OP16-085", 4), ("OP16-099", 3),
                },
            };
            var st = GameEngine.CreateMatch(new MatchConfig
            { Seed = seed, SouthDeckDef = dressrosa, NorthDeckDef = wano });

            var bl = new HashSet<string>();
            int guard = 0;
            while (st.Status != "finished" && st.TurnNumber < 7 && guard++ < 800)
            {
                var cmd = IntermediateBot.DecideOneCommand(st, "south", bl)
                          ?? IntermediateBot.DecideOneCommand(st, "north", bl);
                if (cmd == null) break;
                object before = IntermediateBot.SnapshotFor(st, cmd);
                GameEngine.ApplyCommand(st, cmd);
                if (!IntermediateBot.Succeeded(st, cmd, before)) bl.Add(IntermediateBot.Signature(cmd));
            }
            seat = st.ActiveSeat;
            return st;
        }

        private static void Check(string name, bool ok)
        {
            if (ok) { passed++; Console.WriteLine("  PASS  " + name); }
            else { failed++; Console.WriteLine("  FAIL  " + name); }
        }
    }
}
