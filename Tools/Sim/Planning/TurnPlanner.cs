using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OnePieceTcg.Engine;
using OnePieceTcg.Sim.Search;   // GameClone, LegalActions

namespace OnePieceTcg.Sim.Planning
{
    /// <summary>
    /// Plans a whole turn as a SEQUENCE of actions, not one move at a time — this is what lets the bot
    /// find lethal and execute combos (blueprint §9 / §5.2). It beam-searches over the turn's legal
    /// action sequences on cloned state, resolves the opponent's defence with a policy along the way,
    /// and scores each candidate line at END-OF-TURN with the <see cref="ValueFunction"/>. A lethal line
    /// evaluates as a terminal WIN, so it's chosen automatically; a combo line evaluates as the strong
    /// board it builds, so the search starts the chain even though the intermediate steps look neutral.
    /// Everything runs on the existing clone + legal-action enumerator.
    /// </summary>
    public static class TurnPlanner
    {
        public sealed class Options
        {
            // THE KNEE, measured 2026-07-15 by a proper sweep (aggro mirrors, n=300 PER POINT):
            //   nodes  400 = 33.3%  |  1600 = 53.7%  |  3200 = 64.3%  |  6400 = 65.0% (+0.7 ⇒ saturated)
            // The previous 6/10/400 was NOT the knee: it came from +3.3pp on n=60 measured on the MIXED
            // pool (24/41 midrange), and starved cheap decks catastrophically — branching scales with cheap
            // cards (a 2.1-cost deck plays ~5/turn vs ~1 for a 5.5-cost deck). Raising to the real knee is
            // worth +31pp on aggro and +11pp on control (56.7→67.7, n=300, p≈0.005), and collapsed a 40pp
            // archetype spread to ~2pp. Every "the eval is the ceiling" conclusion predating this was
            // measured on a bot thinking at 1/8 the useful budget.
            public int BeamWidth = 16;
            public int MaxDepth = 16;     // max actions planned in a turn line
            public int NodeBudget = 3200; // hard cap on positions expanded (bounds combo depth)
            public int ResolveCap = 300;  // engine commands to auto-resolve opponent defence per step

            /// <summary>Hard cap on clone+apply operations per planned turn (0 = unlimited).
            /// NodeBudget bounds how many nodes we expand but NOT what a node costs: each node runs
            /// Validate (one clone per candidate) plus a Resolve that can itself enumerate repeatedly,
            /// and candidates grow as attackers x targets. So on a cluttered board a "120-node" turn can
            /// cost tens of thousands of clones — the source of the 100x latency tail. This bounds the
            /// real work. Deterministic, so seeded games stay reproducible.</summary>
            public long WorkBudget = 0;

            /// <summary>Score mid-turn beam states left over when the search is cut off. Needed when a
            /// budget can truncate the search (otherwise `best` stays null and the bot passively ends its
            /// turn), but it lets a PARTIAL line — one that never plays endTurn — win the comparison
            /// against complete lines. Off by default so it cannot alter full-budget play.</summary>
            public bool ConsiderTruncatedLines = false;

            /// <summary>Play the OPPONENT'S REPLY TURN before scoring a line, so a line is judged by the
            /// position it actually leaves us in rather than by the board at the instant our turn ends.
            ///
            /// Without this the search is effectively ONE PLY: Resolve() returns the moment the opponent's
            /// turn starts, so a line that attacks with everything and taps out is scored on its lovely
            /// board with the counter-swing invisible. The bot cannot represent "if I do this, they kill me
            /// next turn" at all — LifeDanger is a crude static stand-in for a whole ply of search. That is
            /// 1-ply versus the IntermediateBot's 0-ply, which is the shape of a bot that thinks ~25x
            /// harder and still only wins ~45%.
            ///
            /// The reply is driven by ValuePolicy (greedy, cheap) rather than a nested TurnPlanner: it is
            /// an approximation, but a greedy reply that ATTACKS is enormously more honest than no reply at
            /// all. Bounded by the same deterministic WorkDeadline as everything else.
            ///
            /// Applied as a RE-RANK of the top <see cref="RerankK"/> finished lines, never inline at every
            /// leaf. Inline was the original design and it was unusable for two reasons: (1) a reply per
            /// leaf drew from the SEARCH's deadline, so roll-outs ate the budget node expansion needed and
            /// the bot went passive; (2) it was guarded by `!OutOfWork`, so once the budget died the
            /// remaining leaves were scored at end-of-turn while earlier ones were scored after the
            /// counter-swing — two incomparable scales feeding one `>` comparison, biased toward whichever
            /// lines happened to be considered after the budget ran dry.</summary>
            public bool OpponentReplyPly = false;

            /// <summary>How many of the best finished lines get re-scored after the opponent's reply.
            /// Bounds the reply cost at K turns instead of one per leaf.</summary>
            public int RerankK = 8;

            /// <summary>Work reserved for the reply-ply re-rank, spent ON TOP of <see cref="WorkBudget"/>
            /// rather than out of it, so enabling the reply ply cannot shrink the search. This is free at
            /// the knee: NodeBudget binds there, not WorkBudget (work 4000 vs 16000 was measured
            /// BIT-IDENTICAL), so the search cannot spend the extra work even if handed it. Keeping the
            /// budgets separate is also what makes reply-ply OFF provably identical to the old build.</summary>
            public long ReplyBudget = 0;
        }

        /// <summary>THE PRODUCTION ENTRY. Plan a turn over a <see cref="Search.SearchWorld"/> — the sampled
        /// world the search is entitled to run on. This is the signature every shipped caller should use:
        /// it takes a world whose provenance (honest determinization vs the unsafe legacy clone) is an
        /// explicit, auditable property of the type rather than something a call site decides ad hoc.
        ///
        /// ⚠ The boundary is not yet compile-enforced: the raw <see cref="PlanTurn(GameState,string,double[],DeckContext,Options)"/>
        /// core below is still reachable in-assembly (the perfect-info control in privacy-test needs it).
        /// Making a raw-state plan a COMPILE error is the encapsulation step (design §14.7), after
        /// migration — naming the surface is the honest interim.</summary>
        public static List<GameCommand> PlanTurn(Search.SearchWorld world, double[] w, DeckContext ctx,
            Options opt = null)
        {
            if (world == null) throw new System.ArgumentNullException(nameof(world));
            return PlanTurn(world.State, world.Seat, w, ctx, opt);
        }

        /// <summary>THE RAW CORE — plans directly on a <see cref="GameState"/>, i.e. with whatever
        /// information that state carries. Kept accessible only for (a) the <see cref="PlanTurn(Search.SearchWorld,double[],DeckContext,Options)"/>
        /// overload and (b) the explicit perfect-info CONTROL in privacy-test. Production planners must not
        /// call this directly; they go through a SearchWorld so the information they searched is auditable.
        /// <paramref name="seat"/> is the planning seat; the opponent's defence is resolved along the way.</summary>
        public static List<GameCommand> PlanTurn(GameState state, string seat, double[] w, DeckContext ctx,
            Options opt = null)
        {
            opt ??= new Options();
            // Publish the budget as a shared deadline so it bounds the WHOLE turn, not just node expansion:
            // ValuePolicy (this search's opponent model, called at every node) reads it and drops its battle
            // lookahead once the turn's budget is spent, instead of running an unbounded roll-out per node.
            // The reply-ply re-rank gets its slice ADDED here and fenced off below, so the search phase still
            // sees exactly opt.WorkBudget whether the reply ply is on or off. (0 = unlimited, so a 0 search
            // budget stays unlimited rather than becoming a ReplyBudget-sized cap.)
            long total = opt.WorkBudget > 0 && opt.OpponentReplyPly
                ? opt.WorkBudget + opt.ReplyBudget
                : opt.WorkBudget;
            long prevDeadline = LegalActions.PushDeadline(total);
            try { return PlanTurnCore(state, seat, w, ctx, opt); }
            finally { LegalActions.PopDeadline(prevDeadline); }
        }

        private static List<GameCommand> PlanTurnCore(GameState state, string seat, double[] w, DeckContext ctx,
            Options opt)
        {
            bool OutOfWork() => LegalActions.OutOfWork;

            // Fence the search inside exactly opt.WorkBudget. PlanTurn's deadline is WorkBudget+ReplyBudget
            // when the reply ply is on, so without this fence the search would simply spend the reply's slice
            // and starve it — the original bug, inverted. With the reply ply off this is PushDeadline(0) = a
            // no-op, which is what keeps that path bit-identical to the pre-reply-ply build.
            long searchPrev = LegalActions.PushDeadline(opt.OpponentReplyPly ? opt.WorkBudget : 0);

            var start = GameClone.Clone(state);
            Resolve(start, seat, w, ctx, opt.ResolveCap);      // advance to my first real decision

            List<GameCommand> best = null;
            double bestVal = double.NegativeInfinity;
            int nodes = 0;

            // beam entries: (state at my decision, actions taken to get here)
            var beam = new List<(GameState s, List<GameCommand> acts)> { (start, new List<GameCommand>()) };

            // Finished lines kept for the reply-ply re-rank, trimmed to the best few by end-of-turn score.
            // Holding every leaf would pin NodeBudget states per thread; the re-rank only ever reads the top K.
            var cands = new List<(GameState s, List<GameCommand> acts, double myopic)>();
            // The "just end the turn" floor is held OUT of that list so the trim can never evict it. The
            // shortlist is ranked by the end-of-turn score, which is board-dominated, so it is systematically
            // the most AGGRESSIVE lines — exactly the ones a reply ply exists to punish. If the safe line is
            // not on the shortlist the re-rank can only pick the least-bad way to tap out, which is not the
            // question being asked. The floor is the one line guaranteed to be conservative, so it is always
            // a comparator.
            (GameState s, List<GameCommand> acts, double myopic)? floorCand = null;

            void Consider(GameState leaf, List<GameCommand> acts, bool isFloor = false)
            {
                double val = ValueFunction.Score(leaf, seat, w, ctx);
                if (val > bestVal) { bestVal = val; best = acts; }
                if (!opt.OpponentReplyPly) return;
                if (isFloor) { floorCand = (leaf, acts, val); return; }
                cands.Add((leaf, acts, val));
                if (cands.Count >= opt.RerankK * 4)
                {
                    cands.Sort((x, y) => y.myopic.CompareTo(x.myopic));
                    cands.RemoveRange(opt.RerankK, cands.Count - opt.RerankK);
                }
            }

            for (int depth = 0; depth < opt.MaxDepth && beam.Count > 0 && nodes < opt.NodeBudget; depth++)
            {
                if (OutOfWork()) break;
                var next = new List<(GameState s, List<GameCommand> acts, double h)>();
                foreach (var (s, acts) in beam)
                {
                    if (!IsMyDecision(s, seat)) { Consider(s, acts); continue; }   // line already ended my turn
                    var legal = LegalActions.Validate(s, seat, LegalActions.Candidates(s, seat));
                    if (legal.Count == 0) { Consider(s, acts); continue; }
                    foreach (var kv in legal)
                    {
                        if (++nodes > opt.NodeBudget || OutOfWork()) break;
                        var result = kv.result;                    // already a clone
                        Resolve(result, seat, w, ctx, opt.ResolveCap);  // let opponent defend / auto-progress
                        var line = new List<GameCommand>(acts) { kv.cmd };
                        if (kv.cmd.Type == "endTurn" || !IsMyDecision(result, seat) || result.Status == "finished")
                            Consider(result, line);                // this line ends my turn → score it
                        else
                            next.Add((result, line, ValueFunction.Score(result, seat, w, ctx)));
                    }
                    if (nodes > opt.NodeBudget || OutOfWork()) break;
                }
                beam = next.OrderByDescending(x => x.h).Take(opt.BeamWidth).Select(x => (x.s, x.acts)).ToList();
            }

            // A search truncated by the work/node budget must still return the best line it FOUND. Without
            // this, a cut-off search leaves `best` null and the bot falls back to the endTurn floor below —
            // i.e. it stops attacking exactly on the cluttered boards where attacking matters most. That is
            // the passivity bug this rebuild exists to fix, so budgets must never be able to reintroduce it.
            if (opt.ConsiderTruncatedLines) foreach (var (s, acts) in beam) if (acts.Count > 0) Consider(s, acts);
            // make sure "just end the turn" is always in the running as a floor. (Scoring `start` needs no
            // clone — Consider only reads.)
            Consider(start, new List<GameCommand> { new GameCommand { Type = "endTurn", Seat = seat } }, isFloor: true);

            LegalActions.PopDeadline(searchPrev);               // search done; the reply's slice is now live
            var replyBest = RerankByReply(cands, floorCand, seat, w, ctx, opt);
            return replyBest ?? best ?? new List<GameCommand> { new GameCommand { Type = "endTurn", Seat = seat } };
        }

        /// <summary>Re-score the best finished lines by the position they leave us in AFTER the opponent's
        /// reply turn, and return the winner — or null to keep the end-of-turn ranking.
        ///
        /// UNIFORM SEMANTICS IS THE WHOLE POINT. A line scored after the reply and a line scored at the
        /// instant our turn ends are on different scales (the second cannot see the counter-swing), so they
        /// must never meet in one comparison. A candidate whose reply does not COMPLETE inside its slice is
        /// therefore dropped from the ranking rather than mixed in with an optimistic score, and if fewer
        /// than two survive there is nothing to compare and we keep the end-of-turn ranking untouched.
        /// Each candidate gets an EQUAL slice so the ranking cannot depend on evaluation order.</summary>
        private static List<GameCommand> RerankByReply(
            List<(GameState s, List<GameCommand> acts, double myopic)> cands,
            (GameState s, List<GameCommand> acts, double myopic)? floorCand,
            string seat, double[] w, DeckContext ctx, Options opt)
        {
            if (!opt.OpponentReplyPly) return null;
            cands.Sort((x, y) => y.myopic.CompareTo(x.myopic));
            var shortlist = cands.Take(opt.RerankK).ToList();
            if (floorCand.HasValue) shortlist.Add(floorCand.Value);   // always a comparator — see floorCand
            if (shortlist.Count == 0) return null;
            long slice = opt.ReplyBudget > 0 ? Math.Max(1, opt.ReplyBudget / shortlist.Count) : 0;

            var scored = new List<(List<GameCommand> acts, double val)>();
            for (int i = 0; i < shortlist.Count; i++)
            {
                var c = shortlist[i];
                if (c.s.Status != "active")
                {
                    // Already terminal: there is no reply to play, and the score is the true end of the game.
                    // Comparable with replied lines by construction, so it belongs in the ranking.
                    scored.Add((c.acts, ValueFunction.Score(c.s, seat, w, ctx)));
                    continue;
                }
                long prev = LegalActions.PushDeadline(slice);
                try
                {
                    var eval = GameClone.Clone(c.s, searchMode: true);   // never mutate a candidate
                    if (PlayOpponentTurn(eval, seat, w, ctx))
                        scored.Add((c.acts, ValueFunction.Score(eval, seat, w, ctx)));
                }
                finally { LegalActions.PopDeadline(prev); }
            }

            if (scored.Count < 2) { Interlocked.Increment(ref RerankAbstained); return null; }
            Interlocked.Increment(ref RerankApplied);
            scored.Sort((x, y) => y.val.CompareTo(x.val));
            // Did seeing the reply actually change our mind? If the re-rank almost always re-elects the line
            // the end-of-turn score already liked, the feature is INERT and its win rate is measuring nothing
            // — the likeliest cause being that the greedy ValuePolicy opponent does not punish a tap-out, so
            // every line's reply looks equally harmless. That is indistinguishable from "the idea does not
            // help" in a win rate alone, which is why it is counted rather than inferred.
            if (!ReferenceEquals(scored[0].acts, shortlist[0].acts)) Interlocked.Increment(ref RerankChangedMind);
            // How hard the reply BITES: end-of-turn score minus post-reply score for the chosen line. ~0 means
            // the modelled opponent is doing nothing to us and the ply carries no information.
            double bite = shortlist[0].myopic - scored[0].val;
            lock (BiteLock) { BiteSum += bite; BiteN++; }
            return scored[0].acts;
        }

        /// <summary>Is the current decision one that WE choose (and therefore search)? Main-phase actions,
        /// our pending-effect targets, our A/B choices, our deck-look picks. Battle defence during our
        /// attack belongs to the opponent, so it's not "ours".</summary>
        public static bool IsMyDecision(GameState s, string seat)
        {
            if (s.Status != "active") return false;
            if (s.ActiveChoice != null) return s.ActiveChoice.Seat == seat;
            if (s.DeckLook != null) return s.DeckLook.Seat == seat;
            if (s.PendingEffects.Count > 0) return s.PendingEffects[0].Seat == seat;
            if (s.Battle != null) return false;
            return s.ActiveSeat == seat && s.Phase == "main";
        }

        /// <summary>Advance the state past everything that isn't our decision — the opponent's blocks /
        /// counters / triggers and any auto-progression — using the value-driven <see cref="ValuePolicy"/>
        /// (the opponent defends to maximize ITS own position = best defence against us), until it's our
        /// decision again, our turn ends, or the game ends. No IntermediateBot.</summary>
        /// <summary>Play the opponent's whole reply turn (and our defence within it) with the greedy
        /// ValuePolicy, stopping when control returns to us or the game ends. Scoring AFTER this means a
        /// line is judged by the position we actually inherit — tapping out into a lethal counter-swing
        /// finally scores as the loss it is. Bounded by the shared WorkDeadline and a hard command cap so
        /// it can never run away; if the budget is spent we score where we got to.</summary>
        /// <summary>Returns TRUE only if the reply ran to a clean end (control back with us, or the game
        /// over). A false return means the position is mid-reply — the opponent may have attacked once and
        /// not yet finished — and scoring it would be worse than not simulating at all, because it reads as
        /// a real end-of-reply board. The old void version could not express that, so every abandoned
        /// roll-out was silently scored as if complete.</summary>
        private static bool PlayOpponentTurn(GameState s, string mySeat, double[] w, DeckContext ctx)
        {
            for (int i = 0; i < 400; i++)
            {
                if (s.Status != "active") { Interlocked.Increment(ref ReplyComplete); return true; }
                if (LegalActions.OutOfWork) { Interlocked.Increment(ref ReplyOutOfBudget); return false; }
                // a clean main phase back on OUR turn ⇒ the reply is over
                if (s.ActiveSeat == mySeat && s.Phase == "main" && s.Battle == null
                    && s.PendingEffects.Count == 0 && s.ActiveChoice == null && s.DeckLook == null)
                { Interlocked.Increment(ref ReplyComplete); return true; }

                string dec = s.ActiveChoice?.Seat ?? s.DeckLook?.Seat
                             ?? (s.PendingEffects.Count > 0 ? s.PendingEffects[0].Seat : null)
                             ?? s.Battle?.PrioritySeat ?? s.ActiveSeat;
                var cmd = dec == null ? null : ValuePolicy.Decide(s, dec, w, dec == mySeat ? ctx : DeckContext.Generic);
                // No decision available ⇒ the reply is STUCK, not finished. Counted separately because a high
                // stuck rate would mean the re-rank is silently disabled rather than measured as ineffective.
                if (cmd == null) { Interlocked.Increment(ref ReplyStuck); return false; }
                GameEngine.ApplyCommand(s, cmd);
            }
            Interlocked.Increment(ref ReplyHitCap);
            return false;
        }

        /// <summary>Reply-ply instrumentation. The re-rank can silently no-op (every reply incomplete ⇒
        /// fewer than 2 comparable lines ⇒ end-of-turn ranking kept), which would look identical to "the
        /// idea does not help". These counters tell those two apart. Dumped by `reply-stats`.</summary>
        public static long ReplyComplete, ReplyOutOfBudget, ReplyStuck, ReplyHitCap, RerankApplied, RerankAbstained;
        public static long RerankChangedMind;
        public static readonly object BiteLock = new object();
        public static double BiteSum; public static long BiteN;

        public static void Resolve(GameState s, string seat, double[] w, DeckContext ctx, int cap)
        {
            var opp = GameEngine.OtherSeat(seat);
            for (int i = 0; i < cap; i++)
            {
                if (s.Status == "finished") return;
                if (IsMyDecision(s, seat)) return;
                if (s.ActiveSeat != seat && s.Battle == null && s.PendingEffects.Count == 0
                    && s.ActiveChoice == null && s.DeckLook == null) return;

                var cmd = ValuePolicy.Decide(s, opp, w, DeckContext.Generic);       // opponent's best defence
                if (cmd == null && !(s.ActiveSeat == seat && s.Phase == "main" && s.Battle == null))
                    cmd = ValuePolicy.Decide(s, seat, w, ctx);                       // rare forced non-main on our side
                if (cmd == null) return;
                GameEngine.ApplyCommand(s, cmd);
            }
        }
    }
}
