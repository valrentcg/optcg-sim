using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OnePieceTcg.Engine;
using OnePieceTcg.Engine.Bot.Search;   // GameClone, LegalActions
using OnePieceTcg.Engine.Puzzles;      // LethalSolver

namespace OnePieceTcg.Sim.Puzzles
{
    /// <summary>
    /// Harvests real-game puzzles from saved replays. Each replay is reconstructed EXACTLY
    /// (GameEngine.CreateMatch(Seed, deck defs) + replaying its CommandHistory — the same deterministic
    /// path the in-game replay viewer uses), and at every point where the active player is deciding in
    /// their main phase we run the LethalSolver. Every position it proves forced-lethal becomes a puzzle
    /// candidate — including MID-TURN states (some DON already spent, a Character already played) as well
    /// as full-resource turn starts, so the harvested set covers the resource variety real games produce.
    ///
    /// Because reconstruction is exact, the defender's face-down Life cards and deck order are the real
    /// ones, so the solver proves lethal against the actual disruptive [Trigger]s (worst-case, adversarial
    /// via the AND/OR search) rather than assuming Life is inert.
    ///
    /// Candidates are scored by <see cref="DifficultyEvaluator"/> and the hardest fill the Hard/Expert
    /// tiers. Output is a self-contained JSON recipe (seed + both deck lists + the command prefix) that
    /// the engine reloads via <see cref="HarvestedPuzzle"/> — no re-solving needed at load.
    /// </summary>
    public static class PuzzleHarvester
    {
        // ── Replay + deck JSON DTOs (match ReplayStore.ReplayRecord / DeckStore decks.json) ──
        private sealed class ReplayDto
        {
            public string Id { get; set; }
            public string Seed { get; set; }
            public string FirstPlayer { get; set; }
            public string SouthDeckId { get; set; }
            public string NorthDeckId { get; set; }
            public string SouthLeaderId { get; set; }
            public string NorthLeaderId { get; set; }
            public string WinnerName { get; set; }
            public int TurnCount { get; set; }
            public List<CmdDto> CommandHistory { get; set; }
        }
        private sealed class CmdDto
        {
            public string Type { get; set; }
            public string Seat { get; set; }
            public string InstanceId { get; set; }
            public string Target { get; set; }
            public string Attacker { get; set; }
            public string Blocker { get; set; }
            public int Amount { get; set; }
            public bool HasAmount { get; set; }
            public int SlotIndex { get; set; }
            public bool HasSlotIndex { get; set; }
            public string EffectId { get; set; }
            public bool GoingFirst { get; set; }
            public bool HasGoingFirst { get; set; }
            public bool Mulligan { get; set; }
            public bool HasMulligan { get; set; }
            public List<string> OrderedInstanceIds { get; set; }
            public List<string> DonInstanceIds { get; set; }

            public GameCommand ToCommand() => new GameCommand
            {
                Type = Type, Seat = Seat, InstanceId = InstanceId, Target = Target, Attacker = Attacker,
                Blocker = Blocker, Amount = HasAmount ? (int?)Amount : null,
                SlotIndex = HasSlotIndex ? (int?)SlotIndex : null, EffectId = EffectId,
                GoingFirst = HasGoingFirst ? (bool?)GoingFirst : null,
                Mulligan = HasMulligan ? (bool?)Mulligan : null,
                OrderedInstanceIds = OrderedInstanceIds, DonInstanceIds = DonInstanceIds,
            };
        }
        private sealed class DecksFileDto { public List<DeckDto> decks { get; set; } }
        private sealed class DeckDto
        {
            public string id { get; set; }
            public string name { get; set; }
            public string leaderId { get; set; }
            public List<CardEntryDto> cards { get; set; }
        }
        private sealed class CardEntryDto { public string id { get; set; } public int count { get; set; } }

        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        public sealed class Candidate
        {
            public GameState State;
            public string Attacker;
            public string ReplayId;
            public int Turn;
            public int CmdIndex;          // command-prefix length that reaches this position
            public string Seed;
            public string FirstPlayer;
            public DeckDef South, North;
            public List<GameCommand> Prefix;
            public LethalSolver.Result Solved;
            public PuzzleQualityAnalyzer.Report Quality;
            public double Score;
            public int Difficulty;
        }

        /// <summary>Runs the whole harvest. baseDir = the game's persistentDataPath (holds Replays/ and Decks/).</summary>
        public static List<Candidate> Run(string baseDir, string outPath, int perTierCap = 40, bool verbose = true)
        {
            var custom = LoadCustomDecks(baseDir);
            var replayFiles = FindReplays(baseDir);
            if (verbose) Console.WriteLine($"Found {replayFiles.Count} replay(s), {custom.Count} custom deck(s) in the store.");

            var cands = new List<Candidate>();
            int reconstructed = 0, skippedDeck = 0, replayErrors = 0, gameNo = 0;
            int rejectedQuality = 0;
            // Real end-game boards are far branchier than synthetic puzzles, so each solve is dear. The
            // near-death pre-filter (defLife<=3) keeps solves to the handful of moments where a lethal actually
            // lives, so a single moderate budget stays fast enough while still resolving most of them.
            var solveOpts = new LethalSolver.Options { WorkBudget = 35_000 };

            foreach (var file in replayFiles)
            {
                ReplayDto dto;
                try { dto = JsonSerializer.Deserialize<ReplayDto>(File.ReadAllText(file), JsonOpts); }
                catch { continue; }
                if (dto?.CommandHistory == null || dto.CommandHistory.Count == 0) continue;
                if (!TryResolveConfig(dto, custom, out var cfg, out var south, out var north)) { skippedDeck++; continue; }

                GameState sim;
                try { sim = GameEngine.CreateMatch(cfg); }
                catch { replayErrors++; continue; }

                gameNo++;
                int before = cands.Count;
                // Lethal lives in the end-game — only scan the last few turns (huge solve-count cut).
                int endWindowStart = Math.Max(2, dto.TurnCount - 6);
                var cmds = dto.CommandHistory.Select(c => c.ToCommand()).ToList();
                bool broke = false;
                int solves = 0, wins = 0, noLethal = 0, unknown = 0, qualityFails = 0, minDefLife = 99;
                var perTurnKept = new Dictionary<int, int>();   // turn -> #candidates kept (cap solves per turn)
                for (int i = 0; i < cmds.Count && solves < 12; i++)   // bound per-game solve time (branchy boards)
                {
                    try { sim = GameEngine.ApplyCommand(sim, cmds[i]); }
                    catch { broke = true; break; }

                    if (sim.TurnNumber < endWindowStart) continue;
                    if (!PlausibleLethalDecisionPoint(sim)) continue;
                    string atk = sim.ActiveSeat;
                    // At most 2 solves per (game, turn): the turn start (full resources) plus one mid-turn
                    // (limited-resources) variant — enough variety without exploding the solve count.
                    if (perTurnKept.TryGetValue(sim.TurnNumber, out var kept) && kept >= 2) continue;
                    perTurnKept[sim.TurnNumber] = kept + 1;
                    minDefLife = Math.Min(minDefLife, sim.Players[GameEngine.OtherSeat(atk)].Life.Count);

                    var r = LethalSolver.Solve(GameClone.Clone(sim), atk, solveOpts);
                    solves++;
                    if (r.Outcome == LethalSolver.Lethal.Win) wins++;
                    else if (r.Outcome == LethalSolver.Lethal.NoLethal) { noLethal++; continue; }
                    else { unknown++; continue; }

                    var candidate = new Candidate
                    {
                        State = GameClone.Clone(sim), Attacker = atk, ReplayId = dto.Id, Turn = sim.TurnNumber,
                        CmdIndex = i + 1, Seed = dto.Seed, FirstPlayer = cfg.FirstPlayer, South = south, North = north,
                        Prefix = cmds.Take(i + 1).ToList(), Solved = r,
                    };

                    // "Forced lethal exists" is necessary but nowhere near sufficient. Audit the first several
                    // attacker decisions and reject any position where too many choices still win (the old
                    // harvested set's "attack with everybody in any order" failure).
                    candidate.Quality = PuzzleQualityAnalyzer.Analyze(
                        candidate.State, candidate.Attacker, candidate.Solved,
                        PuzzleQualityAnalyzer.StrictHarvest());
                    if (!candidate.Quality.Passed)
                    {
                        qualityFails++;
                        rejectedQuality++;
                        if (verbose)
                            Console.WriteLine($"      reject-quality T{candidate.Turn} cmd{candidate.CmdIndex}: {candidate.Quality.Reason} " +
                                              $"first={candidate.Quality.WinningFirstMoves}/{candidate.Quality.LegalFirstMoves} " +
                                              $"critical={candidate.Quality.CriticalDecisions}");
                        continue;
                    }

                    candidate.Score = DifficultyEvaluator.Score(candidate.State, candidate.Attacker, candidate.Solved);
                    candidate.Difficulty = Math.Max(3, DifficultyEvaluator.Bucket(candidate.Score));
                    cands.Add(candidate);
                }
                if (!broke) reconstructed++;
                if (verbose) Console.WriteLine($"  [{gameNo}] {dto.Id} turns={dto.TurnCount} " +
                    $"{(broke ? "REPLAY-BROKE" : "ok")} solves={solves}(W{wins}/N{noLethal}/U{unknown}) " +
                    $"qualityReject={qualityFails} minDefLife={(minDefLife == 99 ? -1 : minDefLife)} " +
                    $"+{cands.Count - before} publishable");

                // Checkpoint after every replay. If a branchy tail is interrupted, all quality-approved work
                // from earlier games is already present in the output asset and the run is safely resumable.
                if (!string.IsNullOrEmpty(outPath) && cands.Count > 0)
                    Emit(cands, outPath, perTierCap, verbose: false);
            }

            if (verbose)
                Console.WriteLine($"Reconstructed {reconstructed}/{replayFiles.Count} replays cleanly " +
                                  $"(skipped {skippedDeck} unresolved decks, {replayErrors} build errors). " +
                                  $"Found {cands.Count} publishable puzzle(s); rejected {rejectedQuality} shallow lethal(s).");

            if (verbose)
            {
                var byDiff = cands.GroupBy(c => c.Difficulty).OrderBy(g => g.Key)
                    .Select(g => $"D{g.Key}={g.Count()}");
                Console.WriteLine("Difficulty spread: " + string.Join("  ", byDiff));
                foreach (var c in cands.OrderByDescending(c => c.Score).Take(12))
                    Console.WriteLine($"  score {c.Score,6:F1}  D{c.Difficulty}  {c.ReplayId} T{c.Turn} " +
                                      $"({c.Attacker}) pv={c.Solved.PrincipalVariation.Count} work={c.Solved.Work}");
            }

            if (!string.IsNullOrEmpty(outPath))
                Emit(cands, outPath, perTierCap, verbose);
            return cands;
        }

        // Pre-filter: only solve when the active player is genuinely on-decision in their main phase with
        // enough attackers to plausibly kill — keeps the per-game solve count small.
        private static bool PlausibleLethalDecisionPoint(GameState s)
        {
            if (s.Status != "active" || s.Phase != "main") return false;
            if (s.Battle != null || s.PendingEffects.Count > 0 || s.ActiveChoice != null
                || s.DeckLook != null || s.PendingCharReplace != null) return false;
            string atk = s.ActiveSeat;
            if (LethalSolver.DecidingSeat(s, atk) != atk) return false;
            var me = s.Players[atk];
            if (me.TurnsStarted <= 1) return false;                       // can't battle first turn
            var opp = s.Players[GameEngine.OtherSeat(atk)];
            int defLife = opp.Life.Count;
            if (defLife > 4) return false;   // lethal lives near death; keeps the expensive solve count small
            // loose necessary condition: each ready attacker (+leader) tops out at 2 Life (Double Attack)
            int readyAttackers = (me.Leader != null && !me.Leader.Rested ? 1 : 0)
                + me.CharacterArea.Count(c => c != null && !c.Rested);
            // account for characters we could still PLAY with Rush this turn very loosely: +hand size
            int loose = readyAttackers * 2 + Math.Min(3, me.Hand.Count);
            return defLife >= 1 && loose >= defLife + 1;
        }

        // ── Deck resolution ─────────────────────────────────────────────────
        private static bool TryResolveConfig(ReplayDto dto, Dictionary<string, DeckDef> custom,
            out MatchConfig cfg, out DeckDef south, out DeckDef north)
        {
            cfg = null;
            south = ResolveDeck(dto.SouthDeckId, dto.SouthLeaderId, custom);
            north = ResolveDeck(dto.NorthDeckId, dto.NorthLeaderId, custom);
            if (south == null || north == null) return false;
            cfg = new MatchConfig
            {
                Seed = dto.Seed,
                FirstPlayer = string.IsNullOrEmpty(dto.FirstPlayer) ? "south" : dto.FirstPlayer,
                SouthDeckDef = south, NorthDeckDef = north,
            };
            return true;
        }

        private static DeckDef ResolveDeck(string deckId, string leaderId, Dictionary<string, DeckDef> custom)
        {
            if (string.IsNullOrEmpty(deckId)) return null;
            const string starterPrefix = "starter:";
            string key = deckId.StartsWith(starterPrefix, StringComparison.Ordinal)
                ? deckId.Substring(starterPrefix.Length) : deckId;
            if (CardData.StarterDecks.TryGetValue(key, out var sd) && sd != null) return sd;
            if (custom.TryGetValue(deckId, out var cd) && cd != null) return cd;
            return null;
        }

        private static Dictionary<string, DeckDef> LoadCustomDecks(string baseDir)
        {
            var map = new Dictionary<string, DeckDef>();
            var decksRoot = Path.Combine(baseDir, "Decks");
            if (!Directory.Exists(decksRoot)) return map;
            foreach (var f in Directory.EnumerateFiles(decksRoot, "decks.json", SearchOption.AllDirectories))
            {
                DecksFileDto dto;
                try { dto = JsonSerializer.Deserialize<DecksFileDto>(File.ReadAllText(f), JsonOpts); }
                catch { continue; }
                if (dto?.decks == null) continue;
                foreach (var d in dto.decks)
                {
                    if (string.IsNullOrEmpty(d.id) || d.cards == null) continue;
                    map[d.id] = new DeckDef
                    {
                        Id = d.id, Name = d.name, Leader = d.leaderId,
                        List = d.cards.Where(c => c != null && !string.IsNullOrEmpty(c.id))
                                      .Select(c => (c.id, c.count)).ToList(),
                    };
                }
            }
            return map;
        }

        private static List<string> FindReplays(string baseDir)
        {
            var root = Path.Combine(baseDir, "Replays");
            return Directory.Exists(root)
                ? Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories).OrderBy(f => f).ToList()
                : new List<string>();
        }

        // ── Emit selected candidates to the bundled JSON asset ──────────────
        private static void Emit(List<Candidate> cands, string outPath, int perTierCap, bool verbose)
        {
            // Take the hardest per tier (Hard=3, Expert=4 are the fill target); keep a few Easy/Medium too.
            var chosen = new List<Candidate>();
            foreach (var tier in new[] { 1, 2, 3, 4 })
                chosen.AddRange(cands.Where(c => c.Difficulty == tier)
                                     .OrderByDescending(c => c.Score).Take(perTierCap));

            var puzzles = chosen.Select((c, idx) => HarvestedPuzzle.FromRecipe(
                id: $"harvest-{c.ReplayId}-t{c.Turn}-{idx}",
                title: DifficultyEvaluator.TitleFor(c.State, c.Attacker, c.Solved),
                teaches: DifficultyEvaluator.LessonFor(c.State, c.Attacker, c.Solved),
                difficulty: c.Difficulty, attacker: c.Attacker, seed: c.Seed, firstPlayer: c.FirstPlayer,
                south: c.South, north: c.North, prefix: c.Prefix, quality: c.Quality)).ToList();

            var set = new HarvestedPuzzleSet { puzzles = puzzles };
            // IncludeFields + verbatim names so Unity's JsonUtility reads the SAME JSON on the client side.
            var opts = new JsonSerializerOptions { IncludeFields = true, WriteIndented = true };
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            File.WriteAllText(outPath, JsonSerializer.Serialize(set, opts));
            if (verbose) Console.WriteLine($"Emitted {puzzles.Count} harvested puzzle(s) -> {outPath}");
        }
    }
}
