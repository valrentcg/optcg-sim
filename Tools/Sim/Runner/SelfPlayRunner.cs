using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// Runs a whole experiment: crosses the deck list south×north, runs the opponent population, and
    /// (optionally) swaps starting order over matched seeds — in parallel, deterministically, at a
    /// scale of millions of games. Every game is reconstructable from its seed. Output is sharded
    /// JSONL (one file per worker, zero lock contention on the hot path) plus a merged summary with
    /// Wilson confidence intervals and a first-vs-second table (the raw input to the coin-flip
    /// estimator of §6).
    /// </summary>
    public static class SelfPlayRunner
    {
        // ---- policy population -------------------------------------------------
        public static IAgent MakeAgent(string name)
        {
            switch (name)
            {
                case "baseline":
                case "baseline-intermediate":
                    return new BaselineAgent();
                case "random":
                case "random-legal":
                    // Placeholder identity until a true RandomLegalAgent lands (see README).
                    return new BaselineNamedAgent("random-legal", new BaselineAgent());
                case "aggro":
                    return new AggroAgent();
                case "conservative":
                    return new ConservativeAgent();
                default:
                    throw new ArgumentException($"Unknown policy '{name}'. Known: baseline, random, aggro, conservative.");
            }
        }

        public static void Run(ExperimentConfig cfg, DeckRegistry registry = null)
        {
            registry ??= new DeckRegistry();

            var decks = (cfg.decks != null && cfg.decks.Count > 0)
                ? cfg.decks
                : registry.Ids.OrderBy(k => k).ToList();
            foreach (var d in decks)
                if (!registry.Has(d))
                    throw new ArgumentException($"Unknown deck id '{d}'. Import it first (see 'deckcheck').");
            var deckDefs = decks.ToDictionary(d => d, d => registry.Resolve(d));

            var pool = (cfg.opponentPolicyPool != null && cfg.opponentPolicyPool.Count > 0)
                ? cfg.opponentPolicyPool : new List<string> { "baseline" };

            int D = decks.Count, P = pool.Count, O = cfg.swapStartingPlayer ? 2 : 1, G = Math.Max(1, cfg.gamesPerCondition);
            long total = (long)D * D * P * O * G;
            string[] orders = O == 2 ? new[] { "south", "north" } : new[] { "south" };

            // Pre-build one agent instance per (seat-role, policy). Agents are stateless here, so a
            // single shared instance per policy is safe to read concurrently across all games.
            var southAgent = MakeAgent(cfg.southPolicy);
            var northAgents = pool.ToDictionary(n => n, n => MakeAgent(n));

            string outDir = Path.GetFullPath(Path.Combine(cfg.outputDir, cfg.experimentId));
            Directory.CreateDirectory(outDir);

            Console.WriteLine($"Experiment '{cfg.experimentId}'");
            Console.WriteLine($"  decks={D}  opponents={P}  orders={O}  gamesPerCondition={G}");
            Console.WriteLine($"  TOTAL GAMES = {total:N0}");
            Console.WriteLine($"  output → {outDir}");
            Console.WriteLine($"  decision logs: {(cfg.saveDecisionLogs ? $"ON (sample {cfg.decisionSampleRate:P1})" : "off")}");

            int dop = cfg.maxDegreeOfParallelism > 0 ? cfg.maxDegreeOfParallelism : Environment.ProcessorCount;
            var global = new Accumulator();
            var mergeLock = new object();
            long done = 0;
            int shardCounter = 0;
            var sw = Stopwatch.StartNew();

            // Split [0,total) into a bounded set of contiguous chunks — one output shard each, so the
            // file count stays small at any scale. More chunks than cores gives load balancing (a
            // slow chunk can't stall a whole core) without a file-per-range explosion.
            int chunks = (int)Math.Min(total, (long)dop * 4);
            var ranges = new List<(long start, long end)>(chunks);
            for (int c = 0; c < chunks; c++)
            {
                long start = total * c / chunks;
                long end = total * (c + 1) / chunks;
                if (end > start) ranges.Add((start, end));
            }

            var po = new ParallelOptions { MaxDegreeOfParallelism = dop };
            Parallel.ForEach(
                ranges,
                po,
                () => (Shard)null,
                (range, _, shard) =>
                {
                    shard ??= new Shard(outDir, Interlocked.Increment(ref shardCounter), cfg);
                    for (long gi = range.start; gi < range.end; gi++)
                    {
                        long idx = gi;
                        long i = idx % G; idx /= G;
                        long o = idx % O; idx /= O;
                        long p = idx % P; idx /= P;
                        long nd = idx % D; idx /= D;
                        long sd = idx;      // < D

                        string sDeck = decks[(int)sd], nDeck = decks[(int)nd];
                        string npName = pool[(int)p];
                        string first = orders[(int)o];

                        // Paired seeds: identical seed across the order swap ⇒ matched hidden-state family.
                        string baseSeed = $"{cfg.seedPrefix}:{sDeck}:{nDeck}:{npName}:{i}";
                        string seed = cfg.pairedSeeds ? baseSeed : $"{baseSeed}:{first}";

                        var opts = new MatchDriver.Options { CommandCap = cfg.commandCap };
                        var decisions = new List<DecisionRecord>();
                        bool logDecisions = cfg.saveDecisionLogs && SampleForDecisions(gi, cfg.decisionSampleRate);
                        if (logDecisions) opts.Sink = new RecordingDecisionSink(gi, decisions.Add);

                        var rec = MatchDriver.Play(southAgent, northAgents[npName], deckDefs[sDeck], deckDefs[nDeck], seed, first, opts);
                        rec.exp = cfg.experimentId;
                        rec.g = gi;

                        shard.Games.WriteLine(Json.Line(rec));
                        if (logDecisions)
                            foreach (var dr in decisions) shard.Decisions.WriteLine(Json.Line(dr));

                        shard.Acc.Add(rec);

                        long n = Interlocked.Increment(ref done);
                        if (n % 100_000 == 0)
                            Console.WriteLine($"  {n:N0}/{total:N0}  ({n / sw.Elapsed.TotalSeconds:N0} games/s)");
                    }
                    return shard;
                },
                shard =>
                {
                    if (shard == null) return; // task received no work
                    shard.Dispose();
                    lock (mergeLock) global.Merge(shard.Acc);
                });

            sw.Stop();
            Console.WriteLine($"Done: {total:N0} games in {sw.Elapsed.TotalSeconds:N1}s " +
                              $"({total / Math.Max(0.001, sw.Elapsed.TotalSeconds):N0} games/s).");

            WriteSummary(cfg, outDir, decks, global, sw.Elapsed);
            Console.WriteLine($"Summary → {Path.Combine(outDir, "summary.json")}");
            Console.WriteLine($"Game shards → {outDir}\\games.part*.jsonl");
        }

        private static bool SampleForDecisions(long g, double rate)
        {
            if (rate >= 1.0) return true;
            if (rate <= 0.0) return false;
            // Deterministic per-game hash so a rerun logs the same sampled games.
            ulong h = (ulong)g * 2654435761UL;
            double u = (h & 0xFFFFFF) / (double)0x1000000;
            return u < rate;
        }

        // ---- summary -----------------------------------------------------------
        private static void WriteSummary(ExperimentConfig cfg, string outDir, List<string> decks,
            Accumulator acc, TimeSpan elapsed)
        {
            var overall = new Dictionary<string, object>
            {
                ["experimentId"] = cfg.experimentId,
                ["totalGames"] = acc.Games,
                ["finished"] = acc.Games - acc.Crash - acc.Stall - acc.Cap,
                ["endReasons"] = new Dictionary<string, long>
                {
                    ["life"] = acc.Life, ["deckout"] = acc.Deckout,
                    ["cap"] = acc.Cap, ["stall"] = acc.Stall, ["crash"] = acc.Crash,
                },
                ["avgTurns"] = acc.Games == 0 ? 0 : (double)acc.TurnSum / acc.Games,
                ["avgCommands"] = acc.Games == 0 ? 0 : (double)acc.CmdSum / acc.Games,
                ["firstPlayerWinRate"] = Rate(acc.FirstWon),
                ["elapsedSeconds"] = Math.Round(elapsed.TotalSeconds, 2),
            };

            // Per-deck overall win rate (either seat).
            var perDeck = new Dictionary<string, object>();
            foreach (var d in decks.Where(acc.DeckWins.ContainsKey))
                perDeck[d] = Rate(acc.DeckWins[d]);

            // First-vs-second table: for each (myDeck vs oppDeck), south's win rate when it goes
            // first vs when it goes second — the empirical input to the coin-flip estimator (§6.2).
            var firstSecond = new List<object>();
            foreach (var sd in decks)
            foreach (var nd in decks)
            {
                acc.Matchup.TryGetValue(Key(sd, nd, "south"), out var whenFirst);
                acc.Matchup.TryGetValue(Key(sd, nd, "north"), out var whenSecond);
                if (whenFirst.Games == 0 && whenSecond.Games == 0) continue;
                firstSecond.Add(new Dictionary<string, object>
                {
                    ["south"] = sd, ["north"] = nd,
                    ["southWinRate_goingFirst"] = Rate(whenFirst),
                    ["southWinRate_goingSecond"] = Rate(whenSecond),
                });
            }

            var summary = new Dictionary<string, object>
            {
                ["overall"] = overall,
                ["perDeck"] = perDeck,
                ["firstVsSecond"] = firstSecond,
            };
            File.WriteAllText(Path.Combine(outDir, "summary.json"), Json.Pretty(summary));

            // Console headline.
            Console.WriteLine($"  P(first-player wins) = {acc.FirstWon.Rate:P2} " +
                              $"[{acc.FirstWon.Wilson95().low:P1}, {acc.FirstWon.Wilson95().high:P1}]  n={acc.FirstWon.Games:N0}");
            if (acc.Crash > 0) Console.WriteLine($"  ⚠ {acc.Crash:N0} crashes — inspect games.part*.jsonl (end=\"crash\").");
            if (acc.Cap + acc.Stall > 0) Console.WriteLine($"  ⚠ {acc.Cap + acc.Stall:N0} non-finishing games (cap/stall).");
        }

        private static Dictionary<string, object> Rate(WinTally t)
        {
            var (lo, hi) = t.Wilson95();
            return new Dictionary<string, object>
            {
                ["rate"] = Math.Round(t.Rate, 4),
                ["wins"] = t.Wins,
                ["games"] = t.Games,
                ["ci95"] = new[] { Math.Round(lo, 4), Math.Round(hi, 4) },
            };
        }

        internal static string Key(string sd, string nd, string first) => $"{sd}|{nd}|{first}";

        // ---- per-worker output shard ------------------------------------------
        private sealed class Shard : IDisposable
        {
            public readonly StreamWriter Games;
            public readonly StreamWriter Decisions;
            public readonly Accumulator Acc = new Accumulator();

            public Shard(string outDir, int id, ExperimentConfig cfg)
            {
                Games = new StreamWriter(Path.Combine(outDir, $"games.part{id:D3}.jsonl"), append: false)
                { AutoFlush = false };
                Decisions = cfg.saveDecisionLogs
                    ? new StreamWriter(Path.Combine(outDir, $"decisions.part{id:D3}.jsonl"), append: false) { AutoFlush = false }
                    : StreamWriter.Null;
            }

            public void Dispose()
            {
                Games.Flush(); Games.Dispose();
                Decisions.Flush(); Decisions.Dispose();
            }
        }

        // ---- aggregate ---------------------------------------------------------
        private sealed class Accumulator
        {
            public long Games, Life, Deckout, Cap, Stall, Crash;
            public long TurnSum, CmdSum;
            public WinTally FirstWon;
            public readonly Dictionary<string, WinTally> Matchup = new Dictionary<string, WinTally>();
            public readonly Dictionary<string, WinTally> DeckWins = new Dictionary<string, WinTally>();

            public void Add(GameRecord r)
            {
                Games++;
                TurnSum += r.turns; CmdSum += r.commands;
                switch (r.end)
                {
                    case "life": Life++; break;
                    case "deckout": Deckout++; break;
                    case "cap": Cap++; break;
                    case "stall": Stall++; break;
                    case "crash": Crash++; break;
                }
                if (r.winner != null)
                {
                    FirstWon.Add(r.firstWon);
                    Bump(Matchup, Key(r.sDeck, r.nDeck, r.first), r.winner == "south");
                    Bump(DeckWins, r.sDeck, r.winner == "south");
                    Bump(DeckWins, r.nDeck, r.winner == "north");
                }
            }

            private static void Bump(Dictionary<string, WinTally> map, string key, bool win)
            {
                map.TryGetValue(key, out var t);
                t.Add(win);
                map[key] = t;
            }

            public void Merge(Accumulator o)
            {
                Games += o.Games; Life += o.Life; Deckout += o.Deckout;
                Cap += o.Cap; Stall += o.Stall; Crash += o.Crash;
                TurnSum += o.TurnSum; CmdSum += o.CmdSum;
                FirstWon.Merge(o.FirstWon);
                foreach (var kv in o.Matchup) { Matchup.TryGetValue(kv.Key, out var t); t.Merge(kv.Value); Matchup[kv.Key] = t; }
                foreach (var kv in o.DeckWins) { DeckWins.TryGetValue(kv.Key, out var t); t.Merge(kv.Value); DeckWins[kv.Key] = t; }
            }
        }
    }
}
