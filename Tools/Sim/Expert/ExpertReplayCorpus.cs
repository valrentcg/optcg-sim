using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim.Expert
{
    /// <summary>
    /// Downloads the public OPBounty replay feed and turns it into a privacy-safe research corpus.
    /// Raw logs are retained only as ignored, regenerable evidence. The committed/runtime-facing model
    /// contains aggregate action counts and replay-matched local deck ids; it never contains player names,
    /// true hidden ordering, hands, life cards, or authoritative instance ids.
    /// </summary>
    public static class ExpertReplayCorpus
    {
        public const string DefaultOutputDir = "Results/expert-replays";
        public const string CatalogFile = "catalog.json";
        public const string ModelFile = "expert-policy.json";
        private const string SessionUrl = "https://stats.tcgmatchmaking.com/auth/session";
        private const string ListUrl = "https://stats.tcgmatchmaking.com/api/replays/list?gameMode=0&limit=20";
        private const string LogUrl = "https://stats.tcgmatchmaking.com/api/replays/log?id=";

        // Public high-bounty examples found during the 2026-07-16 audit. Keeping their public replay ids
        // bootstraps a stable corpus even though the anonymous feed is a rolling latest-20 window.
        private static readonly ReplayMeta[] Bootstrap =
        {
            new ReplayMeta { Id = "HjBoM17yWjAsohHA4tfS", WinnerLeader = "OP11-041", WinnerBounty = 1713,
                LoserLeader = "OP15-058", LoserBounty = 3171, Timestamp = "bootstrap-2026-07-16" },
            new ReplayMeta { Id = "yu0lTmtuuBWelEEtzIOl", WinnerLeader = "OP12-061", WinnerBounty = 2025,
                LoserLeader = "OP07-059", LoserBounty = 1786, Timestamp = "bootstrap-2026-07-16" },
            new ReplayMeta { Id = "GRVrO7yIr1M5eVOYpZ7f", WinnerLeader = "OP12-061", WinnerBounty = 1765,
                LoserLeader = "OP11-041", LoserBounty = 1029, Timestamp = "bootstrap-2026-07-16" },
        };

        private static readonly Regex PlayerLeader = new Regex(
            @"^\[(.+?)\] Leader is .+?<link=""([A-Z0-9-]+)""", RegexOptions.Compiled);
        private static readonly Regex PlayerPrefix = new Regex(@"^\[(.+?)\] (.+)$", RegexOptions.Compiled);
        private static readonly Regex Ply = new Regex(
            @"^RZ1\|PLY\|(\d+)\|(.+)\|([A-Z0-9-]+)$", RegexOptions.Compiled);
        private static readonly Regex RzCard = new Regex(
            @"^RZ1\|\d+\|(\d+)\|([A-Z0-9-]+)\|", RegexOptions.Compiled);
        private static readonly Regex Link = new Regex(@"<link=""([A-Z0-9-]+)""", RegexOptions.Compiled);

        public static int Sync(DeckRegistry registry, double minimumBounty = 1000,
            string outputDir = DefaultOutputDir)
        {
            Directory.CreateDirectory(outputDir);
            string rawDir = Path.Combine(outputDir, "raw");
            Directory.CreateDirectory(rawDir);

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            string token = GetToken(http);
            var latest = GetLatest(http, token);
            var selected = Bootstrap.Concat(latest)
                .GroupBy(r => r.Id, StringComparer.Ordinal)
                .Select(g => g.Last())
                .Where(r => Math.Max(r.WinnerBounty, r.LoserBounty) >= minimumBounty)
                .OrderBy(r => r.Timestamp).ThenBy(r => r.Id).ToList();

            var catalog = new ReplayCatalog
            {
                GeneratedUtc = DateTime.UtcNow.ToString("O"),
                MinimumBounty = minimumBounty,
                Source = "https://stats.tcgmatchmaking.com/replays",
            };

            string sessionToken = token;
            foreach (var meta in selected)
            {
                string rawPath = Path.Combine(rawDir, meta.Id + ".log");
                string raw;
                if (File.Exists(rawPath)) raw = File.ReadAllText(rawPath);
                else
                {
                    // The public endpoint rate-limits bursts. A cached corpus is fast; first sync is polite.
                    if (catalog.Replays.Count > 0) Thread.Sleep(2200);
                    try { raw = GetLog(http, sessionToken, meta.Id); }
                    catch (HttpRequestException)
                    {
                        sessionToken = GetToken(http);
                        Thread.Sleep(2200);
                        raw = GetLog(http, sessionToken, meta.Id);
                    }
                    File.WriteAllText(rawPath, raw, new UTF8Encoding(false));
                }

                var replay = Parse(meta, raw, registry, minimumBounty);
                replay.RawFile = "raw/" + meta.Id + ".log";
                catalog.Replays.Add(replay);
                Console.WriteLine($"  {meta.Id}: {string.Join(" vs ", replay.Players.Select(p =>
                    $"{p.Leader} {p.Bounty:F0} -> {p.MatchedDeckId ?? "no local proxy"}"))}");
            }

            var model = ExpertPolicyModel.FromCatalog(catalog);
            WriteJson(Path.Combine(outputDir, CatalogFile), catalog);
            WriteJson(Path.Combine(outputDir, ModelFile), model);
            Console.WriteLine($"\nExpert corpus: {catalog.Replays.Count} replay(s), {model.ExpertPlayers} high-bounty player-games, " +
                              $"{model.Global.Attacks} attacks ({model.Global.LeaderAttackRate:P1} at leader).");
            Console.WriteLine($"catalog: {Path.GetFullPath(Path.Combine(outputDir, CatalogFile))}");
            Console.WriteLine($"model:   {Path.GetFullPath(Path.Combine(outputDir, ModelFile))}");
            return catalog.Replays.Count > 0 && model.ExpertPlayers > 0 ? 0 : 1;
        }

        public static ReplayCatalog LoadCatalog(string outputDir = DefaultOutputDir) =>
            ReadJson<ReplayCatalog>(Path.Combine(outputDir, CatalogFile));

        public static ExpertPolicyModel LoadModel(string outputDir = DefaultOutputDir) =>
            ReadJson<ExpertPolicyModel>(Path.Combine(outputDir, ModelFile));

        public static List<DeckDef> MatchedDecks(DeckRegistry registry, ReplayCatalog catalog) => catalog.Replays
            .SelectMany(r => r.Players)
            .Where(p => p.Expert && !string.IsNullOrEmpty(p.MatchedDeckId) && registry.Has(p.MatchedDeckId))
            .Select(p => registry.Resolve(p.MatchedDeckId))
            .GroupBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First()).OrderBy(d => d.Id).ToList();

        private static ReplayRecord Parse(ReplayMeta meta, string raw, DeckRegistry registry, double threshold)
        {
            string[] lines = raw.Replace("\r", "").Split('\n');
            var seatByName = new Dictionary<string, int>(StringComparer.Ordinal);
            var leaderBySeat = new Dictionary<int, string>();
            var nameBySeat = new Dictionary<int, string>();

            foreach (string line in lines)
            {
                var m = Ply.Match(line);
                if (!m.Success) continue;
                int seat = int.Parse(m.Groups[1].Value);
                string name = m.Groups[2].Value;
                seatByName[name] = seat; nameBySeat[seat] = name; leaderBySeat[seat] = m.Groups[3].Value;
            }
            if (leaderBySeat.Count == 0)
            {
                int seat = 1;
                foreach (string line in lines)
                {
                    var m = PlayerLeader.Match(line);
                    if (!m.Success || seatByName.ContainsKey(m.Groups[1].Value)) continue;
                    string name = m.Groups[1].Value;
                    seatByName[name] = seat; nameBySeat[seat] = name; leaderBySeat[seat] = m.Groups[2].Value;
                    seat++;
                }
            }

            var observed = new Dictionary<int, HashSet<string>>();
            foreach (string line in lines)
            {
                var m = RzCard.Match(line);
                if (!m.Success) continue;
                int seat = int.Parse(m.Groups[1].Value);
                if (!observed.TryGetValue(seat, out var cards)) observed[seat] = cards = new HashSet<string>();
                cards.Add(m.Groups[2].Value);
            }

            var stats = leaderBySeat.Keys.ToDictionary(s => s, _ => new ExpertActionStats());
            foreach (string line in lines)
            {
                var m = PlayerPrefix.Match(line);
                if (!m.Success || !seatByName.TryGetValue(m.Groups[1].Value, out int seat)
                    || !stats.TryGetValue(seat, out var st)) continue;
                string action = m.Groups[2].Value;
                if (action.Contains("Chose to go First", StringComparison.OrdinalIgnoreCase)) st.ChoseFirst++;
                if (action.Equals("Mulligan", StringComparison.OrdinalIgnoreCase)
                    || action.StartsWith("Hand after Mulligan", StringComparison.OrdinalIgnoreCase)) st.Mulligans = 1;
                if (action.StartsWith("Hand before Mulligan", StringComparison.OrdinalIgnoreCase)) st.OpeningHands++;
                if (action.StartsWith("Deploy ", StringComparison.OrdinalIgnoreCase)) st.Deploys++;
                if (action.IndexOf(" for Counter", StringComparison.OrdinalIgnoreCase) >= 0) st.Counters++;
                if (action.StartsWith("Block", StringComparison.OrdinalIgnoreCase)
                    || action.IndexOf(": Block", StringComparison.OrdinalIgnoreCase) >= 0) st.Blocks++;
                if (action.IndexOf(" attacking ", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var links = Link.Matches(action);
                    if (links.Count >= 2)
                    {
                        st.Attacks++;
                        int other = seat == 1 ? 2 : 1;
                        string target = links[links.Count - 1].Groups[1].Value;
                        if (leaderBySeat.TryGetValue(other, out string otherLeader) && target == otherLeader)
                            st.LeaderAttacks++;
                    }
                }
            }

            string concededName = lines.Select(line => PlayerPrefix.Match(line))
                .FirstOrDefault(m => m.Success && m.Groups[2].Value.StartsWith("Concedes", StringComparison.OrdinalIgnoreCase))
                ?.Groups[1].Value;
            int concededSeat = concededName != null && seatByName.TryGetValue(concededName, out int cs) ? cs : 0;

            var record = new ReplayRecord { Id = meta.Id, Timestamp = meta.Timestamp };
            foreach (int seat in leaderBySeat.Keys.OrderBy(x => x))
            {
                string leader = leaderBySeat[seat];
                bool won;
                double bounty;
                if (concededSeat != 0)
                {
                    won = seat != concededSeat;
                    bounty = won ? meta.WinnerBounty : meta.LoserBounty;
                }
                else if (!string.Equals(meta.WinnerLeader, meta.LoserLeader, StringComparison.OrdinalIgnoreCase))
                {
                    won = string.Equals(leader, meta.WinnerLeader, StringComparison.OrdinalIgnoreCase);
                    bounty = won ? meta.WinnerBounty : meta.LoserBounty;
                }
                else { won = false; bounty = Math.Max(meta.WinnerBounty, meta.LoserBounty); }

                var proxy = MatchDeck(registry, leader, observed.GetValueOrDefault(seat));
                record.Players.Add(new ReplayPlayer
                {
                    Seat = seat, Leader = leader, Bounty = bounty, Won = won, Expert = bounty >= threshold,
                    MatchedDeckId = proxy.deckId, ObservedDistinctCards = observed.GetValueOrDefault(seat)?.Count ?? 0,
                    ProxyOverlap = proxy.overlap, ProxyCoverage = proxy.coverage,
                    Actions = stats.GetValueOrDefault(seat) ?? new ExpertActionStats(),
                });
            }
            return record;
        }

        private static (string deckId, int overlap, double coverage) MatchDeck(
            DeckRegistry registry, string leader, HashSet<string> observed)
        {
            if (observed == null) return (null, 0, 0);
            return registry.Ids.Select(registry.Resolve)
                .Where(d => d != null && string.Equals(d.Leader, leader, StringComparison.OrdinalIgnoreCase))
                .Select(d =>
                {
                    var ids = d.List.Select(x => x.cardId).Where(x => x != leader).ToHashSet();
                    int overlap = ids.Count(observed.Contains);
                    return (deckId: d.Id, overlap, coverage: ids.Count == 0 ? 0 : (double)overlap / ids.Count);
                })
                .OrderByDescending(x => x.overlap).ThenByDescending(x => x.coverage).ThenBy(x => x.deckId)
                .FirstOrDefault();
        }

        private static string GetToken(HttpClient http)
        {
            using var response = http.PostAsync(SessionUrl, new StringContent("", Encoding.UTF8, "application/json"))
                .GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            return doc.RootElement.GetProperty("token").GetString();
        }

        private static List<ReplayMeta> GetLatest(HttpClient http, string token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ListUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = http.Send(request);
            response.EnsureSuccessStatusCode();
            var feed = JsonSerializer.Deserialize<ReplayFeed>(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            return feed?.Replays ?? new List<ReplayMeta>();
        }

        private static string GetLog(HttpClient http, string token, string id)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LogUrl + Uri.EscapeDataString(id));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
            using var response = http.Send(request);
            response.EnsureSuccessStatusCode();
            return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        }

        private static void WriteJson<T>(string path, T value) => File.WriteAllText(path,
            JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
            new UTF8Encoding(false));

        private static T ReadJson<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    public sealed class ReplayFeed
    {
        [JsonPropertyName("replays")] public List<ReplayMeta> Replays { get; set; } = new List<ReplayMeta>();
    }

    public sealed class ReplayMeta
    {
        [JsonPropertyName("id")] public string Id { get; set; }
        [JsonPropertyName("timestamp")] public string Timestamp { get; set; }
        [JsonPropertyName("winner_bounty")] public double WinnerBounty { get; set; }
        [JsonPropertyName("winner_leader")] public string WinnerLeader { get; set; }
        [JsonPropertyName("loser_bounty")] public double LoserBounty { get; set; }
        [JsonPropertyName("loser_leader")] public string LoserLeader { get; set; }
    }

    public sealed class ReplayCatalog
    {
        public string GeneratedUtc { get; set; }
        public double MinimumBounty { get; set; }
        public string Source { get; set; }
        public List<ReplayRecord> Replays { get; set; } = new List<ReplayRecord>();
    }

    public sealed class ReplayRecord
    {
        public string Id { get; set; }
        public string Timestamp { get; set; }
        public string RawFile { get; set; }
        public List<ReplayPlayer> Players { get; set; } = new List<ReplayPlayer>();
    }

    public sealed class ReplayPlayer
    {
        public int Seat { get; set; }
        public string Leader { get; set; }
        public double Bounty { get; set; }
        public bool Won { get; set; }
        public bool Expert { get; set; }
        public string MatchedDeckId { get; set; }
        public int ObservedDistinctCards { get; set; }
        public int ProxyOverlap { get; set; }
        public double ProxyCoverage { get; set; }
        public ExpertActionStats Actions { get; set; } = new ExpertActionStats();
    }

    public sealed class ExpertActionStats
    {
        public int PlayerGames { get; set; }
        public int Attacks { get; set; }
        public int LeaderAttacks { get; set; }
        public int Deploys { get; set; }
        public int Counters { get; set; }
        public int Blocks { get; set; }
        public int ChoseFirst { get; set; }
        public int OpeningHands { get; set; }
        public int Mulligans { get; set; }
        [JsonIgnore] public double LeaderAttackRate => Attacks == 0 ? 0 : (double)LeaderAttacks / Attacks;

        public void Add(ExpertActionStats other)
        {
            PlayerGames += Math.Max(1, other.PlayerGames);
            Attacks += other.Attacks; LeaderAttacks += other.LeaderAttacks; Deploys += other.Deploys;
            Counters += other.Counters; Blocks += other.Blocks; ChoseFirst += other.ChoseFirst;
            OpeningHands += other.OpeningHands; Mulligans += other.Mulligans;
        }
    }

    public sealed class ExpertPolicyModel
    {
        public string Version { get; set; } = "expert-replay-v1";
        public string TrainedUtc { get; set; }
        public double MinimumBounty { get; set; }
        public int ReplayCount { get; set; }
        public int ExpertPlayers { get; set; }
        public ExpertActionStats Global { get; set; } = new ExpertActionStats();
        public Dictionary<string, ExpertActionStats> Leaders { get; set; } =
            new Dictionary<string, ExpertActionStats>(StringComparer.OrdinalIgnoreCase);

        public ExpertActionStats Preference(string leader, int minimumLeaderAttacks = 6, int minimumGlobalAttacks = 24)
        {
            if (leader != null && Leaders.TryGetValue(leader, out var local) && local.Attacks >= minimumLeaderAttacks)
                return local;
            return Global.Attacks >= minimumGlobalAttacks ? Global : null;
        }

        public static ExpertPolicyModel FromCatalog(ReplayCatalog catalog)
        {
            var model = new ExpertPolicyModel
            {
                TrainedUtc = DateTime.UtcNow.ToString("O"), MinimumBounty = catalog.MinimumBounty,
                ReplayCount = catalog.Replays.Count,
            };
            foreach (var player in catalog.Replays.SelectMany(r => r.Players).Where(p => p.Expert))
            {
                model.ExpertPlayers++;
                model.Global.Add(player.Actions);
                if (!model.Leaders.TryGetValue(player.Leader, out var local))
                    model.Leaders[player.Leader] = local = new ExpertActionStats();
                local.Add(player.Actions);
            }
            return model;
        }
    }
}
