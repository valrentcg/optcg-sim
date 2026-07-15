using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim.Learning
{
    public sealed class EvolveConfig
    {
        public string experimentId { get; set; } = "tournament";
        public List<string> deckPool { get; set; }         // empty ⇒ auto-select clean imported meta decks
        public int populationSize { get; set; } = 48;      // number of distinct playstyles alive at once
        public int generations { get; set; } = 15;
        public int roundsPerGeneration { get; set; } = 5;  // Swiss rounds per generation
        public int gamesPerPairing { get; set; } = 6;      // games each matched pair plays
        public int commandCap { get; set; } = 20000;
        public int maxDegreeOfParallelism { get; set; } = 0;
        public string seedPrefix { get; set; } = "tour";
        public string outputDir { get; set; } = "Results";
        public int eliteKeep { get; set; } = 8;            // top survivors carried over each generation
        public int freshBlood { get; set; } = 8;           // brand-new random styles injected each generation
    }

    internal sealed class Individual
    {
        public Genome G; public double Elo = 1500; public string Origin; public int BornGen;
        public long Games, Wins;
        public readonly Dictionary<string, long[]> ByColor = new(); // color -> [wins, games] when PILOTING that color
        public double WinRate => Games == 0 ? 0 : (double)Wins / Games;
        public void Record(string color, bool win)
        {
            Games++; if (win) Wins++;
            if (!ByColor.TryGetValue(color ?? "?", out var a)) { a = new long[2]; ByColor[color ?? "?"] = a; }
            a[1]++; if (win) a[0]++;
        }
        public (double rate, long games) Color(string c) => ByColor.TryGetValue(c, out var a) && a[1] > 0 ? ((double)a[0] / a[1], a[1]) : (0, 0);
    }

    /// <summary>
    /// Population-based tournament co-evolution (blueprint §10.4). Instead of one champion improving on
    /// one line, a POPULATION of diverse playstyles competes on an Elo ladder: fair Swiss matchmaking
    /// pairs similar-skill styles, so a new (weak) style proves itself against other newcomers before it
    /// ever meets the top of the ladder — the bracket gets harder as you climb. Each round, styles pilot
    /// randomly-drawn META decks; wins raise Elo, losses lower it. Between generations the weakest styles
    /// are culled, strong+diverse ones breed (mutate/crossover), and fresh random styles are injected.
    /// Diversity is preserved by keeping the best style PER DECK COLOR — so the result isn't one winner
    /// but a portfolio: the strongest style for red decks, for green, etc., which the in-game bot picks
    /// from by looking at the user's deck.
    /// </summary>
    public static class Evolver
    {
        public static void Run(EvolveConfig cfg, DeckRegistry registry)
        {
            var decks = (cfg.deckPool != null && cfg.deckPool.Count > 0) ? cfg.deckPool : AutoCleanMetaDecks(registry);
            if (decks.Count < 2) throw new InvalidOperationException("Need >=2 clean meta decks — import meta lists into Decks/imported and run 'deckcheck'.");
            var def = decks.ToDictionary(d => d, registry.Resolve);
            var deckColor = decks.ToDictionary(d => d, d => CardData.GetCard(def[d].Leader)?.Color ?? "?");
            var colors = deckColor.Values.Distinct().OrderBy(c => c).ToList();

            string outDir = Path.GetFullPath(Path.Combine(cfg.outputDir, cfg.experimentId));
            Directory.CreateDirectory(outDir);
            var curve = new StreamWriter(Path.Combine(outDir, "ladder-curve.csv")) { AutoFlush = true };
            curve.WriteLine("generation,top_elo,mean_elo,top_winrate," + string.Join(",", Genome.Genes.Select(g => "best_" + g.name)));

            Console.WriteLine($"Tournament '{cfg.experimentId}'  pop={cfg.populationSize}  gens={cfg.generations}  rounds/gen={cfg.roundsPerGeneration}  games/pair={cfg.gamesPerPairing}");
            Console.WriteLine($"  meta decks={decks.Count}  colors=[{string.Join(",", colors)}]");
            long gamesPerGen = (long)(cfg.populationSize / 2) * cfg.roundsPerGeneration * cfg.gamesPerPairing;
            Console.WriteLine($"  ~{gamesPerGen:N0} games/generation  (~{gamesPerGen * cfg.generations:N0} total)\n");

            var rng = new Random(20260714);
            var pop = SeedPopulation(cfg.populationSize, rng, 0);
            int dop = cfg.maxDegreeOfParallelism > 0 ? cfg.maxDegreeOfParallelism : Environment.ProcessorCount;

            for (int gen = 1; gen <= cfg.generations; gen++)
            {
                for (int round = 0; round < cfg.roundsPerGeneration; round++)
                {
                    // Swiss-style pairing: shuffle then stable-sort by Elo, pair neighbours (similar skill).
                    var order = pop.OrderBy(_ => rng.Next()).OrderByDescending(x => x.Elo).ToList();
                    var pairs = new List<(Individual a, Individual b)>();
                    for (int i = 0; i + 1 < order.Count; i += 2) pairs.Add((order[i], order[i + 1]));
                    int seedBase = (gen * 100 + round) * 100_000;

                    // Pairings are disjoint (each style in exactly one pair), so they update their own two
                    // individuals without contention.
                    Parallel.For(0, pairs.Count, new ParallelOptions { MaxDegreeOfParallelism = dop }, pi =>
                    {
                        var (a, b) = pairs[pi];
                        int aWins = 0;
                        for (int k = 0; k < cfg.gamesPerPairing; k++)
                        {
                            var r = new Random(seedBase + pi * 97 + k);
                            string dA = decks[r.Next(decks.Count)], dB = decks[r.Next(decks.Count)];
                            string first = r.Next(2) == 0 ? "south" : "north";
                            string seed = $"{cfg.seedPrefix}:{seedBase}:{pi}:{k}";
                            var rec = MatchDriver.Play(new WeightedAgent(a.G), new WeightedAgent(b.G), def[dA], def[dB], seed, first,
                                new MatchDriver.Options { CommandCap = cfg.commandCap, AgentChoosesTurnOrder = true });
                            bool aWon = rec.winner == "south";
                            if (aWon) aWins++;
                            a.Record(deckColor[dA], aWon);
                            b.Record(deckColor[dB], !aWon && rec.winner != null);
                        }
                        UpdateElo(a, b, (double)aWins / cfg.gamesPerPairing);
                    });
                }

                var ranked = pop.OrderByDescending(x => x.Elo).ToList();
                var top = ranked[0];
                double meanElo = pop.Average(x => x.Elo);
                curve.WriteLine($"{gen},{top.Elo:0},{meanElo:0},{top.WinRate:0.000}," +
                                string.Join(",", Genome.Genes.Select((g, i) => top.G.Get(i).ToString("0.000", CultureInfo.InvariantCulture))));
                Console.WriteLine($"  gen {gen,2}: topElo={top.Elo:0} meanElo={meanElo:0}  topWR={top.WinRate:P0}  [{top.G}]");

                pop = NextGeneration(pop, cfg, rng, gen, colors);
            }

            // ---- results: the per-color portfolio + the ladder ----
            var finalRanked = pop.OrderByDescending(x => x.Elo).ToList();
            Console.WriteLine("\n=== Elo ladder (top styles) ===");
            foreach (var ind in finalRanked.Take(6))
                Console.WriteLine($"  Elo {ind.Elo:0}  WR {ind.WinRate:P0}  ({ind.Origin})  [{ind.G}]");

            Console.WriteLine("\n=== Best style per deck color (the portfolio the in-game bot picks from) ===");
            var portfolio = new Dictionary<string, object>();
            foreach (var c in colors)
            {
                var best = pop.Where(x => x.Color(c).games >= 12).OrderByDescending(x => x.Color(c).rate).FirstOrDefault();
                if (best == null) { Console.WriteLine($"  {c,-8}: (insufficient games)"); continue; }
                var (rate, g) = best.Color(c);
                Console.WriteLine($"  {c,-8}: WR {rate:P0} (n={g})  [{best.G}]");
                portfolio[c] = new Dictionary<string, object>
                {
                    ["winrate"] = Math.Round(rate, 4), ["games"] = g,
                    ["genome"] = Genome.Genes.ToDictionary(x => x.name, x => Math.Round(best.G.Get(Array.IndexOf(Genome.Genes, x)), 3)),
                };
            }
            File.WriteAllText(Path.Combine(outDir, "style-portfolio.json"), Json.Pretty(portfolio));
            curve.Dispose();
            Console.WriteLine($"\nLadder curve → {Path.Combine(outDir, "ladder-curve.csv")}");
            Console.WriteLine($"Portfolio    → {Path.Combine(outDir, "style-portfolio.json")}");
        }

        private static void UpdateElo(Individual a, Individual b, double aScore, double k = 24)
        {
            double ea = 1.0 / (1 + Math.Pow(10, (b.Elo - a.Elo) / 400));
            a.Elo += k * (aScore - ea);
            b.Elo += k * ((1 - aScore) - (1 - ea));
        }

        private static List<Individual> SeedPopulation(int n, Random rng, int gen)
        {
            var pop = new List<Individual>();
            // A spread of named archetypes so the population starts diverse across styles...
            (string, Genome)[] seeds =
            {
                ("baseline",   Genome.Baseline()),
                ("aggro-first",new Genome{ MulliganKeep=1, FaceBias=0.95, CounterLifeFloor=1, CounterCharCost=7, BlockBias=0.1, TurnOrderFirst=1 }),
                ("aggro-2nd",  new Genome{ MulliganKeep=1, FaceBias=0.9,  CounterLifeFloor=1, CounterCharCost=7, BlockBias=0.2, TurnOrderFirst=0 }),
                ("control",    new Genome{ MulliganKeep=2, FaceBias=0.1,  CounterLifeFloor=4, CounterCharCost=3, BlockBias=0.9, TurnOrderFirst=0 }),
                ("defensive",  new Genome{ MulliganKeep=2, FaceBias=0.3,  CounterLifeFloor=5, CounterCharCost=4, BlockBias=0.8, TurnOrderFirst=1 }),
                ("tempo",      new Genome{ MulliganKeep=1, FaceBias=0.6,  CounterLifeFloor=2, CounterCharCost=5, BlockBias=0.4, TurnOrderFirst=1 }),
                ("removal",    new Genome{ MulliganKeep=2, FaceBias=0.05, CounterLifeFloor=3, CounterCharCost=2, BlockBias=0.6, TurnOrderFirst=0 }),
            };
            foreach (var (name, g) in seeds.Take(n)) pop.Add(new Individual { G = g, Origin = name, BornGen = gen });
            // ...and fill the rest with random styles (new blood).
            while (pop.Count < n) pop.Add(new Individual { G = RandomGenome(rng), Origin = "random", BornGen = gen });
            return pop;
        }

        private static Genome RandomGenome(Random rng)
        {
            var g = new Genome();
            for (int i = 0; i < Genome.Genes.Length; i++)
            {
                var (_, lo, hi) = Genome.Genes[i];
                g.Set(i, lo + rng.NextDouble() * (hi - lo));
            }
            return g;
        }

        private static List<Individual> NextGeneration(List<Individual> pop, EvolveConfig cfg, Random rng, int gen, List<string> colors)
        {
            var ranked = pop.OrderByDescending(x => x.Elo).ToList();
            var survivors = new List<Individual>();
            // Elitism: keep the top of the ladder.
            survivors.AddRange(ranked.Take(cfg.eliteKeep));
            // Niching: also keep the best style for each deck color (diversity, so we don't collapse to one).
            foreach (var c in colors)
            {
                var best = pop.Where(x => x.Color(c).games >= 10).OrderByDescending(x => x.Color(c).rate).FirstOrDefault();
                if (best != null && !survivors.Contains(best)) survivors.Add(best);
            }

            var next = new List<Individual>(survivors);
            // Fresh random blood — starts a little below average so it proves itself against peers first.
            for (int i = 0; i < cfg.freshBlood && next.Count < cfg.populationSize; i++)
                next.Add(new Individual { G = RandomGenome(rng), Origin = "random", BornGen = gen, Elo = 1450 });
            // Breed the rest from survivors (tournament-select parents, mutate + occasional crossover).
            while (next.Count < cfg.populationSize)
            {
                var p1 = TournamentSelect(ranked, rng);
                var child = rng.NextDouble() < 0.3 ? Crossover(p1.G, TournamentSelect(ranked, rng).G, rng) : p1.G.Clone();
                child = Mutate(child, rng);
                next.Add(new Individual { G = child, Origin = "bred", BornGen = gen, Elo = 1480 });
            }
            return next;
        }

        private static Individual TournamentSelect(List<Individual> ranked, Random rng)
        {
            Individual best = null;
            for (int i = 0; i < 3; i++) { var c = ranked[rng.Next(ranked.Count)]; if (best == null || c.Elo > best.Elo) best = c; }
            return best;
        }

        private static Genome Crossover(Genome a, Genome b, Random rng)
        {
            var c = new Genome();
            for (int i = 0; i < Genome.Genes.Length; i++) c.Set(i, rng.Next(2) == 0 ? a.Get(i) : b.Get(i));
            return c;
        }

        private static Genome Mutate(Genome parent, Random rng)
        {
            var c = parent.Clone();
            int nMut = 1 + rng.Next(2);
            for (int k = 0; k < nMut; k++)
            {
                int gi = rng.Next(Genome.Genes.Length);
                var (_, lo, hi) = Genome.Genes[gi];
                c.Set(gi, Math.Clamp(c.Get(gi) + (hi - lo) * 0.20 * (rng.NextDouble() * 2 - 1), lo, hi));
            }
            return c;
        }

        // Clean meta decks = imported (non-starter) lists that validate to exactly 50 mainboard, <=4 copies.
        private static List<string> AutoCleanMetaDecks(DeckRegistry reg)
        {
            var clean = new List<string>();
            foreach (var id in reg.Ids.Where(i => !CardData.StarterDecks.ContainsKey(i)))
            {
                var d = reg.Resolve(id);
                int total = d.List.Where(e => e.cardId != d.Leader).Sum(e => e.qty);
                int maxCopies = d.List.Where(e => e.cardId != d.Leader).Select(e => e.qty).DefaultIfEmpty(0).Max();
                if (total == 50 && maxCopies <= 4) clean.Add(id);
            }
            return clean.OrderBy(x => x).ToList();
        }
    }
}
