using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OnePieceTcg.Engine;
using OnePieceTcg.Engine.Bot;

namespace OnePieceTcg.Sim.Runner
{
    /// <summary>
    /// CHESS-STYLE PUZZLE SUITE (iterative-improvement reframe #1). Instead of measuring a bot change by noisy
    /// win-rate over hundreds of games (±4pp noise, minutes per test), we harvest a FROZEN set of positions where
    /// the bot misplays — each labelled by the ORACLE (deep K-determinized rollout) with the win-rate of every
    /// legal action — and then SCORE any bot by its total regret on that fixed suite. Deterministic, sub-second,
    /// interpretable (you can read the exact positions it now gets right). Positions are stored as the engine's
    /// CommandHistory (deterministic replay), so scoring reconstructs the EXACT position independent of the bot.
    /// </summary>
    public sealed class Puzzle
    {
        public string Seed { get; set; }
        public string DeckA { get; set; }   // south deck
        public string DeckB { get; set; }   // north deck
        public string First { get; set; }
        public string Seat { get; set; }     // the decision-maker (south)
        public int Turn { get; set; }
        public List<GameCommand> History { get; set; }             // replay to reach the position
        public Dictionary<string, double> ActionValues { get; set; } // action signature -> oracle win-rate
        public string BestSig { get; set; }
        public double BestValue { get; set; }
        public double RefRegret { get; set; }  // regret of the reference bot that harvested it (for context)
    }

    public static class PuzzleSuite
    {
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true, WriteIndented = false };

        private static string WinnerOf(GameState g)
        {
            for (int i = g.EventLog.Count - 1; i >= 0; i--)
            {
                var m = g.EventLog[i].Message;
                if (string.IsNullOrEmpty(m) || m.IndexOf("wins", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (m.StartsWith("South", StringComparison.Ordinal)) return "south";
                if (m.StartsWith("North", StringComparison.Ordinal)) return "north";
            }
            return null;
        }

        /// <summary>Oracle: K-determinized rollout win-rate of a position for `seat` (greedy playout both sides).
        /// All candidate positions at one decision share the SAME K futures ⇒ the ranking is low-variance.</summary>
        public static double Evaluate(GameState pos, string seat, int K, int baseSeed)
        {
            int wins = 0, done = 0;
            for (int k = 0; k < K; k++)
            {
                var world = OnePieceTcg.Engine.Bot.Search.BotDeterminizer.FairView(pos, seat, baseSeed * 131 + k);
                MatchDriver.Playout(world, new BaselineAgent(), new BaselineAgent(), 6000);
                var w = WinnerOf(world);
                if (w != null) { done++; if (w == seat) wins++; }
            }
            return done > 0 ? (double)wins / done : 0.5;
        }

        /// <summary>Harvest high-regret south main-phase positions into a suite. Reference play: south = advanced,
        /// north = greedy (so positions are ADVANCED-bot-relevant).</summary>
        public static void Harvest(DeckRegistry reg, int nGames, int K, double regretThreshold, string outfile, int seed, bool advancedRef = false)
        {
            var pool = reg.Ids.Where(i => !CardData.StarterDecks.ContainsKey(i))
                .Where(id => { var d = reg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; })
                .OrderBy(x => x).ToList();
            var rng = new Random(seed);
            var puzzles = new List<Puzzle>();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int g = 0; g < nGames; g++)
            {
                string dA = pool[rng.Next(pool.Count)], dB = pool[rng.Next(pool.Count)];
                string first = g % 2 == 0 ? "south" : "north";
                var st = GameEngine.CreateMatch(new MatchConfig { SouthDeckDef = reg.Resolve(dA), NorthDeckDef = reg.Resolve(dB), Seed = $"pz:{seed}:{g}" });
                if (st.Status == "coinflip") { string w = st.CoinFlipWinner; GameEngine.ApplyCommand(st, new GameCommand { Type = "chooseTurnOrder", Seat = w, GoingFirst = w == first }); }
                IAgent south = advancedRef ? new ShippedAdvancedAgent() : new BaselineAgent();
                var north = new BaselineAgent();
                int total = 0;
                while (st.Status != "finished" && total < 3000)
                {
                    bool southCleanMain = st.ActiveSeat == "south" && st.Phase == "main" && st.Battle == null
                        && st.PendingEffects.Count == 0 && st.ActiveChoice == null && st.DeckLook == null;
                    if (southCleanMain)
                    {
                        var cands = OnePieceTcg.Sim.Search.LegalActions.Validate(st, "south", OnePieceTcg.Sim.Search.LegalActions.Candidates(st, "south"));
                        var botCmd = south.Decide(st, "south", new HashSet<string>());
                        if (botCmd != null && cands.Count > 1)
                        {
                            var vals = new Dictionary<string, double>();
                            double best = -1; string bestSig = null; double botVal = -1;
                            string bSig = IntermediateBot.Signature(botCmd);
                            foreach (var kv in cands)
                            {
                                string sig = IntermediateBot.Signature(kv.cmd);
                                double v = Evaluate(kv.result, "south", K, total + 1);
                                vals[sig] = v;
                                if (v > best) { best = v; bestSig = sig; }
                                if (sig == bSig) botVal = v;
                            }
                            if (botVal < 0) { var bc = OnePieceTcg.Engine.Bot.Search.GameClone.Clone(st); GameEngine.ApplyCommand(bc, botCmd); botVal = Evaluate(bc, "south", K, total + 1); vals[bSig] = botVal; }
                            double regret = best - botVal;
                            if (regret >= regretThreshold)
                            {
                                puzzles.Add(new Puzzle
                                {
                                    Seed = $"pz:{seed}:{g}", DeckA = dA, DeckB = dB, First = first, Seat = "south", Turn = st.TurnNumber,
                                    History = st.CommandHistory.Select(CloneCmd).ToList(),
                                    ActionValues = vals, BestSig = bestSig, BestValue = best, RefRegret = regret,
                                });
                            }
                        }
                        if (botCmd == null) break;
                        GameEngine.ApplyCommand(st, botCmd); total++;
                        continue;
                    }
                    foreach (var seat in new[] { "south", "north" })
                    {
                        var c = (seat == "south" ? (IAgent)south : north).Decide(st, seat, new HashSet<string>());
                        if (c != null) { GameEngine.ApplyCommand(st, c); total++; }
                    }
                }
                Console.WriteLine($"  game {g + 1}/{nGames}: {puzzles.Count} puzzles so far ({sw.Elapsed.TotalSeconds:F0}s)");
                Console.Out.Flush();
                if (puzzles.Count > 0 && (g + 1) % 5 == 0) File.WriteAllText(outfile, JsonSerializer.Serialize(puzzles, Json)); // checkpoint
            }
            File.WriteAllText(outfile, JsonSerializer.Serialize(puzzles, Json));
            Console.WriteLine($"HARVEST done: {puzzles.Count} puzzles → {outfile}  (avg refRegret={(puzzles.Count > 0 ? puzzles.Average(p => p.RefRegret) : 0):F3}, {sw.Elapsed.TotalSeconds:F0}s)");
        }

        /// <summary>Score a bot on a frozen suite: replay each puzzle to its position, ask the bot, look up the
        /// oracle win-rate of the bot's move ⇒ regret. Reports avg regret + accuracy (fraction near-best).</summary>
        public static void Score(DeckRegistry reg, string suitefile, Func<IAgent> makeBot, string label)
            => ScoreList(reg, JsonSerializer.Deserialize<List<Puzzle>>(File.ReadAllText(suitefile), Json), makeBot, label);

        public static void ScoreList(DeckRegistry reg, List<Puzzle> puzzles, Func<IAgent> makeBot, string label)
        {
            double regretSum = 0; int solved = 0, scored = 0, unmatched = 0; var sw = System.Diagnostics.Stopwatch.StartNew();
            foreach (var p in puzzles)
            {
                var st = GameEngine.CreateMatch(new MatchConfig { SouthDeckDef = reg.Resolve(p.DeckA), NorthDeckDef = reg.Resolve(p.DeckB), Seed = p.Seed });
                foreach (var cmd in p.History) GameEngine.ApplyCommand(st, cmd);   // deterministic replay to the position
                var bot = makeBot();
                var move = bot.Decide(st, p.Seat, new HashSet<string>());
                if (move == null) { unmatched++; continue; }
                string sig = IntermediateBot.Signature(move);
                if (!p.ActionValues.TryGetValue(sig, out double v)) { unmatched++; continue; }  // bot chose an unlabelled action
                double regret = p.BestValue - v;
                regretSum += regret; scored++;
                if (regret <= 0.03) solved++;   // "solved" = picked a near-best move
            }
            Console.WriteLine($"PUZZLE-SCORE [{label}]: {puzzles.Count} puzzles, scored={scored} unmatched={unmatched}  " +
                              $"avgRegret={(scored > 0 ? regretSum / scored : 0):F4}  solved(≤0.03)={100.0 * solved / Math.Max(1, scored):F1}%  {sw.Elapsed.TotalSeconds:F0}s");
        }

        /// <summary>DISTILLATION: fit the eval weights to predict the oracle's win-rate for each candidate, using
        /// the labels already stored in the suite (no re-running the oracle). Reports the fit quality and scores
        /// eval-greedy BEFORE vs AFTER the fit on the suite — the test of whether a learned eval + 1-ply search
        /// beats the hand-coded bot. `ridge` regularizes. Prints the fitted weights (paste into Evaluation.W to ship).</summary>
        public static void FitEval(DeckRegistry reg, string suitefile, double ridge, Func<IAgent> makeEvalGreedy, string testfile = null)
        {
            var all = JsonSerializer.Deserialize<List<Puzzle>>(File.ReadAllText(suitefile), Json);
            // Out-of-sample: fit on TRAIN (index%10<7), hold out TEST (index%10>=7). If a separate testfile is given,
            // fit on ALL of `suitefile` and score on `testfile` instead.
            List<Puzzle> puzzles; List<Puzzle> testSet; string testTag;
            if (testfile != null) { puzzles = all; testSet = JsonSerializer.Deserialize<List<Puzzle>>(File.ReadAllText(testfile), Json); testTag = "separate-test"; }
            else { puzzles = all.Where((p, i) => i % 10 < 7).ToList(); testSet = all.Where((p, i) => i % 10 >= 7).ToList(); testTag = "held-out 30%"; }
            int F = OnePieceTcg.Engine.Bot.Search.Evaluation.FeatureCount;
            var X = new List<double[]>(); var Y = new List<double>();
            foreach (var p in puzzles)
            {
                var st = GameEngine.CreateMatch(new MatchConfig { SouthDeckDef = reg.Resolve(p.DeckA), NorthDeckDef = reg.Resolve(p.DeckB), Seed = p.Seed });
                foreach (var cmd in p.History) GameEngine.ApplyCommand(st, cmd);
                var cands = OnePieceTcg.Sim.Search.LegalActions.Validate(st, p.Seat, OnePieceTcg.Sim.Search.LegalActions.Candidates(st, p.Seat));
                foreach (var kv in cands)
                {
                    if (!p.ActionValues.TryGetValue(IntermediateBot.Signature(kv.cmd), out double v)) continue;
                    if (kv.result.Status == "finished") continue;   // terminal: no feature vector
                    X.Add(OnePieceTcg.Engine.Bot.Search.Evaluation.Features(kv.result, p.Seat));
                    Y.Add(v);
                }
            }
            int n = X.Count;
            // Normal equations with ridge: (X^T X + ridge*I) w = X^T y.  A is F×F (small), solve by Gaussian elim.
            var A = new double[F, F]; var b = new double[F];
            for (int r = 0; r < n; r++)
            {
                var x = X[r]; double y = Y[r];
                for (int i = 0; i < F; i++) { b[i] += x[i] * y; for (int j = 0; j < F; j++) A[i, j] += x[i] * x[j]; }
            }
            for (int i = 0; i < F; i++) A[i, i] += ridge;
            var w = Solve(A, b, F);
            try { File.WriteAllText("fitted_w.txt", string.Join(" ", w.Select(x => x.ToString("R")))); } catch { }
            // Fit quality (in-sample MSE vs a constant-mean baseline).
            double mean = Y.Count > 0 ? Y.Average() : 0.5, ssRes = 0, ssTot = 0;
            for (int r = 0; r < n; r++) { double pred = 0; for (int i = 0; i < F; i++) pred += w[i] * X[r][i]; ssRes += (Y[r] - pred) * (Y[r] - pred); ssTot += (Y[r] - mean) * (Y[r] - mean); }
            Console.WriteLine($"FIT: {n} training rows from {puzzles.Count} train puzzles, ridge={ridge}, R²={(ssTot > 0 ? 1 - ssRes / ssTot : 0):F3}, RMSE={System.Math.Sqrt(ssRes / System.Math.Max(1, n)):F3}");
            Console.WriteLine("  fitted W = { " + string.Join(", ", w.Select(x => x.ToString("F3"))) + " }");
            Console.WriteLine($"=== OUT-OF-SAMPLE ({testTag}, {testSet.Count} puzzles) — the honest test ===");
            Console.WriteLine("  advanced (current bot) on test:");
            ScoreList(reg, testSet, () => new ShippedAdvancedAgent(), "advanced[test]");
            Console.WriteLine("  eval-greedy BEFORE fit (current W) on test:");
            ScoreList(reg, testSet, makeEvalGreedy, "eval-greedy(current)[test]");
            OnePieceTcg.Engine.Bot.Search.Evaluation.SetWeights(w);
            Console.WriteLine("  eval-greedy AFTER fit (learned W) on test:");
            ScoreList(reg, testSet, makeEvalGreedy, "eval-greedy(fitted)[test]");
        }

        /// <summary>PATH 2, PIECE 1: train the VALUE on REAL GAME OUTCOMES (not oracle-rollout values — the fix
        /// for the greedy-anchored ceiling). Self-play `nGames`; record (Features(state) → 1 if that seat WON the
        /// game else 0) at every clean-main state; least-squares fit → value that predicts actual win. Writes
        /// fitted_w.txt so `moduleab evalmain/learnedw` can test it. `policy`: greedy (fast) | advanced (on-dist).</summary>
        public static void TrainValueOnOutcomes(DeckRegistry reg, int nGames, int seed, double ridge, bool advancedPolicy)
        {
            var pool = reg.Ids.Where(i => !CardData.StarterDecks.ContainsKey(i))
                .Where(id => { var d = reg.Resolve(id); int t = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty); int mx = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max(); return t == 50 && mx <= 4; })
                .OrderBy(x => x).ToList();
            var rng = new Random(seed);
            int F = OnePieceTcg.Engine.Bot.Search.Evaluation.FeatureCount;
            var X = new List<double[]>(); var Y = new List<double>(); var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int g = 0; g < nGames; g++)
            {
                string dA = pool[rng.Next(pool.Count)], dB = pool[rng.Next(pool.Count)];
                string first = g % 2 == 0 ? "south" : "north";
                var st = GameEngine.CreateMatch(new MatchConfig { SouthDeckDef = reg.Resolve(dA), NorthDeckDef = reg.Resolve(dB), Seed = $"vg:{seed}:{g}" });
                if (st.Status == "coinflip") { string cw = st.CoinFlipWinner; GameEngine.ApplyCommand(st, new GameCommand { Type = "chooseTurnOrder", Seat = cw, GoingFirst = cw == first }); }
                IAgent sAg = advancedPolicy ? new ShippedAdvancedAgent() : new BaselineAgent();
                IAgent nAg = advancedPolicy ? new ShippedAdvancedAgent() : new BaselineAgent();
                // record (seat, features) at each clean-main state; label with the game outcome at the end.
                var pending = new List<(string seat, double[] f)>();
                int total = 0;
                while (st.Status != "finished" && total < 3000)
                {
                    foreach (var seat in new[] { "south", "north" })
                    {
                        bool cleanMain = st.ActiveSeat == seat && st.Phase == "main" && st.Battle == null
                            && st.PendingEffects.Count == 0 && st.ActiveChoice == null && st.DeckLook == null;
                        if (cleanMain && (rng.NextDouble() < 0.5))   // subsample to decorrelate
                            pending.Add((seat, OnePieceTcg.Engine.Bot.Search.Evaluation.Features(st, seat)));
                        var c = (seat == "south" ? sAg : nAg).Decide(st, seat, new HashSet<string>());
                        if (c != null) { GameEngine.ApplyCommand(st, c); total++; }
                    }
                }
                string winner = WinnerOf(st);
                if (winner == null) continue;
                foreach (var (seat, f) in pending) { X.Add(f); Y.Add(seat == winner ? 1.0 : 0.0); }
                if ((g + 1) % 50 == 0) Console.WriteLine($"  {g + 1}/{nGames} games, {X.Count} labelled states ({sw.Elapsed.TotalSeconds:F0}s)");
            }
            int n = X.Count;
            var A = new double[F, F]; var b = new double[F];
            for (int r = 0; r < n; r++) { var x = X[r]; double y = Y[r]; for (int i = 0; i < F; i++) { b[i] += x[i] * y; for (int j = 0; j < F; j++) A[i, j] += x[i] * x[j]; } }
            for (int i = 0; i < F; i++) A[i, i] += ridge;
            var w = Solve(A, b, F);
            try { File.WriteAllText("fitted_w.txt", string.Join(" ", w.Select(x => x.ToString("R")))); } catch { }
            double mean = Y.Count > 0 ? Y.Average() : 0.5, ssRes = 0, ssTot = 0;
            for (int r = 0; r < n; r++) { double p = 0; for (int i = 0; i < F; i++) p += w[i] * X[r][i]; ssRes += (Y[r] - p) * (Y[r] - p); ssTot += (Y[r] - mean) * (Y[r] - mean); }
            Console.WriteLine($"VALUE-FIT (real outcomes): {n} states from {nGames} games, ridge={ridge}, R²={(ssTot > 0 ? 1 - ssRes / ssTot : 0):F3}, {sw.Elapsed.TotalSeconds:F0}s");
            Console.WriteLine("  W → fitted_w.txt = { " + string.Join(", ", w.Select(x => x.ToString("F3"))) + " }");
        }

        // Gaussian elimination with partial pivoting for the small normal-equation system.
        private static double[] Solve(double[,] A, double[] b, int F)
        {
            var M = new double[F, F + 1];
            for (int i = 0; i < F; i++) { for (int j = 0; j < F; j++) M[i, j] = A[i, j]; M[i, F] = b[i]; }
            for (int col = 0; col < F; col++)
            {
                int piv = col; for (int r = col + 1; r < F; r++) if (System.Math.Abs(M[r, col]) > System.Math.Abs(M[piv, col])) piv = r;
                if (System.Math.Abs(M[piv, col]) < 1e-12) continue;
                for (int j = 0; j <= F; j++) { var t = M[col, j]; M[col, j] = M[piv, j]; M[piv, j] = t; }
                for (int r = 0; r < F; r++) { if (r == col) continue; double f = M[r, col] / M[col, col]; for (int j = col; j <= F; j++) M[r, j] -= f * M[col, j]; }
            }
            var w = new double[F];
            for (int i = 0; i < F; i++) w[i] = System.Math.Abs(M[i, i]) < 1e-12 ? 0 : M[i, F] / M[i, i];
            return w;
        }

        private static GameCommand CloneCmd(GameCommand c) => new GameCommand
        {
            Type = c.Type, Seat = c.Seat, InstanceId = c.InstanceId, Target = c.Target, Attacker = c.Attacker,
            Blocker = c.Blocker, Amount = c.Amount, SlotIndex = c.SlotIndex, EffectId = c.EffectId,
            GoingFirst = c.GoingFirst, Mulligan = c.Mulligan,
            OrderedInstanceIds = c.OrderedInstanceIds != null ? new List<string>(c.OrderedInstanceIds) : null,
            DonInstanceIds = c.DonInstanceIds != null ? new List<string>(c.DonInstanceIds) : null,
        };
    }
}
