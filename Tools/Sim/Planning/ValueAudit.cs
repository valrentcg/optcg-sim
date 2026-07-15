using System;
using System.Collections.Generic;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim.Planning
{
    /// <summary>
    /// Measures what the value function ACTUALLY weighs on real positions, rather than what the weights
    /// look like on paper. A weight is meaningless without the scale of the feature it multiplies: the
    /// contribution of a term is w[i]*a[i], so a "small" weight on a big-range feature can silently
    /// dominate a "large" weight on a 0..5 one. This dumps the real distribution of |w[i]*a[i]| per
    /// feature across states sampled from real games, which is the only way to see which terms are
    /// actually steering the bot.
    ///
    /// This exists because the bot beam-searches every legal line with a 24-feature eval and still loses
    /// ~80% to a simple heuristic bot. That gap is far too large to be search quality; it points at the
    /// OBJECTIVE. Evolution cannot report this class of bug either — it perturbs weights ±0.6 around the
    /// defaults, so a term needing w≈0.05 that starts at 0.9 is effectively unreachable, and Elo noise
    /// buries the gradient long before it gets there.
    /// </summary>
    public static class ValueAudit
    {
        /// <summary>Wraps an agent to capture the feature vector at every decision it is asked to make —
        /// i.e. exactly the positions the bot actually evaluates, not synthetic ones.</summary>
        private sealed class Sampler : IAgent
        {
            private readonly IAgent _inner;
            private readonly DeckContext _ctx;
            public readonly List<double[]> Samples = new List<double[]>();
            /// <summary>Per-feature SPREAD across the candidate actions at a single decision, averaged over
            /// decisions. This — not mean|a| — is a gene's real LEVERAGE. The bot picks argmax over
            /// candidates, so a feature that takes the SAME value on every candidate shifts all their scores
            /// equally, changes no ranking, and cannot alter play at ANY weight. Evolution therefore has no
            /// gradient on that gene: it random-walks regardless of win rate, and crossover spreads the
            /// resulting junk through the population.
            /// Suspected culprits: MyDeck/OppDeck/MyTrash barely differ between the actions available in one
            /// turn, and they were exactly the genes seen flipping sign and quadrupling while the board genes
            /// sat frozen.</summary>
            public readonly List<double[]> Spreads = new List<double[]>();
            public Sampler(IAgent inner, DeckContext ctx) { _inner = inner; _ctx = ctx; }
            public string Name => _inner.Name;
            public GameCommand Decide(GameState st, string seat, HashSet<string> bl)
            {
                if (st.Status == "active" && st.Players.ContainsKey(seat) && st.Players[seat].Leader != null)
                {
                    Samples.Add(ValueFunction.Attributes(st, seat, _ctx));

                    var legal = Search.LegalActions.Validate(st, seat, Search.LegalActions.Candidates(st, seat));
                    if (legal.Count >= 2)
                    {
                        // Resolve each candidate exactly as TurnPlanner does before scoring a node. Without
                        // this we would measure a MYOPIC scorer: a lone command does not resolve the battle,
                        // so damage/KOs have not happened yet and OppLife/OppBoardPow would look frozen —
                        // an artefact of the probe, not of the bot. (That same myopia WAS a real bug in
                        // ValuePolicy's defence, so it is a plausible-looking artefact, which is exactly why
                        // it has to be ruled out rather than reported.)
                        foreach (var l in legal)
                            TurnPlanner.Resolve(l.result, seat, ValueFunction.DefaultWeights(), _ctx, 300);
                        var vecs = legal.Select(l => ValueFunction.Attributes(l.result, seat, _ctx)).ToList();
                        var sd = new double[ValueFunction.N];
                        for (int i = 0; i < sd.Length; i++)
                        {
                            double mu = vecs.Average(v => v[i]);
                            sd[i] = Math.Sqrt(vecs.Average(v => (v[i] - mu) * (v[i] - mu)));
                        }
                        Spreads.Add(sd);
                    }
                }
                return _inner.Decide(st, seat, bl);
            }
        }

        public static void Run(List<string> decks, Dictionary<string, DeckDef> def,
            Dictionary<string, DeckContext> ctx, TurnPlanner.Options opt, int games, int maxTurns)
        {
            var w = ValueFunction.DefaultWeights();
            var all = new List<double[]>();
            var spreads = new List<double[]>();
            for (int i = 0; i < games; i++)
            {
                string dA = decks[(i * 7) % decks.Count], dB = decks[(i * 13 + 1) % decks.Count];
                var samp = new Sampler(new PlannerBot(w, ctx[dA], opt), ctx[dA]);
                GameRunner.Play(samp, new BaselineAgent(), def[dA], def[dB], $"audit:{i}", "south", 20000, maxTurns);
                all.AddRange(samp.Samples);
                spreads.AddRange(samp.Spreads);
            }
            if (all.Count == 0) { Console.WriteLine("no samples"); return; }

            Console.WriteLine($"\n=== value-audit: |w[i]*a[i]| over {all.Count} real decision states ({games} games) ===");
            Console.WriteLine($"{"feature",-18} {"weight",8} {"mean|a|",9} {"max|a|",8} {"mean|w*a|",10} {"max|w*a|",9}  share");
            var rows = new List<(string nm, double wt, double ma, double xa, double mc, double xc)>();
            double total = 0;
            for (int i = 0; i < ValueFunction.N; i++)
            {
                double ma = all.Average(v => Math.Abs(v[i]));
                double xa = all.Max(v => Math.Abs(v[i]));
                double mc = all.Average(v => Math.Abs(w[i] * v[i]));
                double xc = all.Max(v => Math.Abs(w[i] * v[i]));
                rows.Add((ValueFunction.Names[i], w[i], ma, xa, mc, xc));
                total += mc;
            }
            foreach (var r in rows.OrderByDescending(r => r.mc))
                Console.WriteLine($"{r.nm,-18} {r.wt,8:F3} {r.ma,9:F2} {r.xa,8:F2} {r.mc,10:F2} {r.xc,9:F2}  {100 * r.mc / total,5:F1}%");
            Console.WriteLine($"{"TOTAL",-18} {"",8} {"",9} {"",8} {total,10:F2}");

            // The decisive comparison: the whole win condition (drain 5 life) against one board term.
            var life = rows.First(r => r.nm == "OppLife");
            var pow = rows.First(r => r.nm == "MyBoardPow");
            Console.WriteLine($"\n  MyBoardPow mean contribution is {pow.mc / Math.Max(1e-9, life.mc):F1}x OppLife's.");

            // ---- DECISION LEVERAGE: can evolution actually select on this gene at all? ----
            if (spreads.Count > 0)
            {
                Console.WriteLine($"\n=== decision leverage over {spreads.Count} decisions (spread of a[i] ACROSS candidate actions) ===");
                Console.WriteLine("A gene can only be selected on if its feature DIFFERS between the actions being compared.");
                Console.WriteLine("Zero spread ⇒ the feature shifts every candidate equally ⇒ no ranking changes at ANY");
                Console.WriteLine("weight ⇒ evolution has no gradient and the gene random-walks forever.\n");
                Console.WriteLine($"{"feature",-18} {"weight",8} {"meanSpread",11} {"leverage",9}  {"selectable?",12}");
                var lev = new List<(string nm, double sp, double lv)>();
                for (int i = 0; i < ValueFunction.N; i++)
                {
                    double sp = spreads.Average(s => s[i]);
                    lev.Add((ValueFunction.Names[i], sp, Math.Abs(w[i]) * sp));
                }
                double maxLv = Math.Max(1e-12, lev.Max(x => x.lv));
                foreach (var r in lev.OrderByDescending(x => x.lv))
                {
                    double pct = 100 * r.lv / maxLv;
                    string verdict = r.sp < 1e-9 ? "DEAD" : pct < 1.0 ? "~inert" : pct < 10 ? "weak" : "yes";
                    Console.WriteLine($"{r.nm,-18} {w[Array.IndexOf(ValueFunction.Names, r.nm)],8:F3} {r.sp,11:F4} {r.lv,9:F3}  {verdict,12}");
                }
                int dead = lev.Count(x => x.sp < 1e-9);
                int inert = lev.Count(x => x.sp >= 1e-9 && 100 * x.lv / maxLv < 1.0);
                Console.WriteLine($"\n  {dead} gene(s) DEAD (never differ between candidates — unselectable at any weight).");
                Console.WriteLine($"  {inert} more effectively inert (<1% of the top gene's leverage).");
                Console.WriteLine($"  ⇒ evolution is really optimising ~{ValueFunction.N - dead - inert} of {ValueFunction.N} genes; the rest drift and");
                Console.WriteLine("     inject noise into crossover.");
            }
        }
    }
}
