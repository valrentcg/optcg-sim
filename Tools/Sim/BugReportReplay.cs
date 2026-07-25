using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OnePieceTcg.Engine;
using OnePieceTcg.Engine.Bot;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// Replays an in-game bug report (Seed + both decklists + the exact CommandHistory) headlessly, then
    /// drives the reported seat with the shipped bot tier to reproduce a hang/stall outside Unity.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- bugreplay &lt;bugs.jsonl&gt; [reportId|last] [tier]
    /// tier: advanced (default) | intermediate
    /// </summary>
    public static class BugReportReplay
    {
        /// <summary>Rebuild the exact GameState a bug report was filed from (seed + both decklists +
        /// the recorded CommandHistory). Shared by the replay driver and the rollout profiler.</summary>
        public static GameState LoadState(string path, string want, out string seat, out MatchConfig cfg)
        {
            seat = "north"; cfg = null;
            path ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "AppData", "LocalLow", "DefaultCompany", "One Piece TCG Simulator", "BugReports", "bugs.jsonl");
            if (!File.Exists(path)) { Console.WriteLine("No bug file at " + path); return null; }
            var lines = File.ReadAllLines(path).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
            if (lines.Count == 0) { Console.WriteLine("empty"); return null; }
            string line = string.IsNullOrEmpty(want) || want == "last" ? lines[lines.Count - 1]
                : lines.FirstOrDefault(l => l.Contains("\"Id\":\"" + want + "\"")) ?? lines[lines.Count - 1];

            using var doc = JsonDocument.Parse(line);
            var r = doc.RootElement;
            cfg = new MatchConfig
            {
                Seed = Str(r, "Seed"),
                SouthDeckDef = Deck("south", Str(r, "SouthLeaderId"), r.GetProperty("SouthDeck")),
                NorthDeckDef = Deck("north", Str(r, "NorthLeaderId"), r.GetProperty("NorthDeck")),
            };
            var st = GameEngine.CreateMatch(cfg);
            foreach (var c in r.GetProperty("CommandHistory").EnumerateArray())
                GameEngine.ApplyCommand(st, ToCommand(c));
            seat = string.IsNullOrEmpty(Str(r, "ActiveSeat")) ? "north" : Str(r, "ActiveSeat");
            return st;
        }

        public static int Run(string[] args)
        {
            string path = args.Length > 1 ? args[1]
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "AppData", "LocalLow", "DefaultCompany", "One Piece TCG Simulator", "BugReports", "bugs.jsonl");
            string want = args.Length > 2 ? args[2] : "last";
            string tier = args.Length > 3 ? args[3] : "advanced";

            if (!File.Exists(path)) { Console.WriteLine("No bug file at " + path); return 1; }
            var lines = File.ReadAllLines(path).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
            if (lines.Count == 0) { Console.WriteLine("empty"); return 1; }
            string line = want == "last" ? lines[lines.Count - 1]
                : lines.FirstOrDefault(l => l.Contains("\"Id\":\"" + want + "\"")) ?? lines[lines.Count - 1];

            using var doc = JsonDocument.Parse(line);
            var r = doc.RootElement;
            Console.WriteLine($"=== bug {Str(r, "Id")} — {Str(r, "Description")}");
            Console.WriteLine($"    card={Str(r, "CardId")} {Str(r, "CardName")} turn={r.GetProperty("Turn").GetInt32()} active={Str(r, "ActiveSeat")} app={Str(r, "AppVersion")}");

            var cfg = new MatchConfig
            {
                Seed = Str(r, "Seed"),
                SouthDeckDef = Deck("south", Str(r, "SouthLeaderId"), r.GetProperty("SouthDeck")),
                NorthDeckDef = Deck("north", Str(r, "NorthLeaderId"), r.GetProperty("NorthDeck")),
            };
            var st = GameEngine.CreateMatch(cfg);

            int applied = 0;
            foreach (var c in r.GetProperty("CommandHistory").EnumerateArray())
            {
                GameEngine.ApplyCommand(st, ToCommand(c));
                applied++;
            }
            Console.WriteLine($"    replayed {applied} command(s)");
            Console.WriteLine();
            Dump(st, "state at report time");

            string seat = Str(r, "ActiveSeat");
            if (string.IsNullOrEmpty(seat)) seat = "north";
            string archetype = OnePieceTcg.Engine.Bot.Search.AdvancedContractBot.ClassifyArchetype(
                seat == "south" ? cfg.SouthDeckDef : cfg.NorthDeckDef);
            Console.WriteLine($"driving seat={seat} tier={tier} archetype={archetype}");

            // Mirror GameManager's per-tick adapter: one command per tick, host-owned no-op blacklist
            // that persists for the turn, cleared on turn change.
            var blacklist = new HashSet<string>();
            var activated = new HashSet<string>();
            int lastTurn = st.TurnNumber;
            var seen = new Dictionary<string, int>();
            for (int tick = 0; tick < 60; tick++)
            {
                if (st.TurnNumber != lastTurn) { lastTurn = st.TurnNumber; blacklist.Clear(); activated.Clear(); }
                var snapshot = OnePieceTcg.Engine.Bot.Search.GameClone.Clone(st);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var cmd = tier == "advanced"
                    ? OnePieceTcg.Engine.Bot.Search.AdvancedContractBot.Decide(snapshot, seat, new HashSet<string>(blacklist), activated, archetype)
                    : IntermediateBot.DecideOneCommand(st, seat, blacklist);
                sw.Stop();
                Console.WriteLine($"    [think {sw.ElapsedMilliseconds} ms]");
                if (cmd == null)
                {
                    // NULL is only a stall when the seat actually owns the open decision. Waiting on the
                    // OPPONENT (they must block/counter, or it's their turn) is correct, not a hang.
                    bool ownsDecision =
                        (st.DeckLook != null && st.DeckLook.Seat == seat)
                        || (st.ActiveChoice != null && st.ActiveChoice.Seat == seat)
                        || st.PendingEffects.Any(e => e.Seat == seat)
                        || (st.Battle != null && st.Battle.PrioritySeat == seat)
                        || (st.Battle == null && st.ActiveSeat == seat && st.Phase == "main");
                    Console.WriteLine(ownsDecision
                        ? $"  tick {tick}: bot returned NULL while it OWNS the open decision — STALLED"
                        : $"  tick {tick}: bot returned NULL — waiting on the opponent (expected, not a stall)");
                    break;
                }
                string sig = IntermediateBot.Signature(cmd);
                seen[sig] = seen.TryGetValue(sig, out var k) ? k + 1 : 1;
                object before = IntermediateBot.SnapshotFor(st, cmd);
                GameEngine.ApplyCommand(st, cmd);
                bool ok = IntermediateBot.Succeeded(st, cmd, before);
                Console.WriteLine($"  tick {tick}: {sig} -> {(ok ? "ok" : "NO-OP")}{(seen[sig] > 1 ? $"  [repeat #{seen[sig]}]" : "")}");
                if (!ok) blacklist.Add(sig);
                if (seen[sig] >= 4) { Console.WriteLine("  LOOP: same command re-issued 4x — STALLED"); break; }
                if (st.Status == "finished") { Console.WriteLine("  match finished"); break; }
                if (st.ActiveSeat != seat && st.Battle == null && st.DeckLook == null
                    && st.PendingEffects.Count == 0 && st.ActiveChoice == null)
                { Console.WriteLine("  turn passed to the opponent — bot is unstuck"); break; }
            }
            Console.WriteLine();
            Dump(st, "final");
            return 0;
        }

        private static string Str(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : "";

        private static DeckDef Deck(string seat, string leader, JsonElement list)
        {
            var entries = new List<(string cardId, int qty)>();
            foreach (var s in list.EnumerateArray())
            {
                var parts = (s.GetString() ?? "").Split(':');
                if (parts.Length == 2 && int.TryParse(parts[1], out var q)) entries.Add((parts[0], q));
            }
            return new DeckDef { Id = seat + "-report", Name = seat, Leader = leader, List = entries };
        }

        private static GameCommand ToCommand(JsonElement c)
        {
            var cmd = new GameCommand
            {
                Type = Str(c, "Type"),
                Seat = Str(c, "Seat"),
                InstanceId = Str(c, "InstanceId"),
                Target = Str(c, "Target"),
                Attacker = Str(c, "Attacker"),
                Blocker = Str(c, "Blocker"),
                EffectId = Str(c, "EffectId"),
            };
            if (c.TryGetProperty("HasAmount", out var ha) && ha.GetBoolean()) cmd.Amount = c.GetProperty("Amount").GetInt32();
            if (c.TryGetProperty("HasSlotIndex", out var hs) && hs.GetBoolean()) cmd.SlotIndex = c.GetProperty("SlotIndex").GetInt32();
            if (c.TryGetProperty("HasGoingFirst", out var hg) && hg.GetBoolean()) cmd.GoingFirst = c.GetProperty("GoingFirst").GetBoolean();
            if (c.TryGetProperty("HasMulligan", out var hm) && hm.GetBoolean()) cmd.Mulligan = c.GetProperty("Mulligan").GetBoolean();
            if (c.TryGetProperty("OrderedInstanceIds", out var oi) && oi.ValueKind == JsonValueKind.Array)
                cmd.OrderedInstanceIds = oi.EnumerateArray().Select(x => x.GetString()).ToList();
            if (c.TryGetProperty("DonInstanceIds", out var di) && di.ValueKind == JsonValueKind.Array)
                cmd.DonInstanceIds = di.EnumerateArray().Select(x => x.GetString()).ToList();
            return cmd;
        }

        private static void Dump(GameState st, string label)
        {
            Console.WriteLine($"--- {label} ---");
            Console.WriteLine($"  status={st.Status} turn={st.TurnNumber} active={st.ActiveSeat} phase={st.Phase} battle={(st.Battle == null ? "none" : st.Battle.Step)}");
            var dl = st.DeckLook;
            Console.WriteLine(dl == null
                ? "  DeckLook: null"
                : $"  DeckLook: seat={dl.Seat} src={dl.SourceName} step={dl.Step} cards={dl.Cards.Count} feature='{dl.FeatureFilter}' type='{dl.CardTypeFilter}' named='{dl.NamedCardFilter}' exclude='{dl.ExcludeName}' trashRest={dl.TrashRest} selectCount={dl.SelectCount} searchMode={dl.SearchMode} playMode={dl.PlayMode} postLook='{dl.PostLookClause}'");
            if (dl != null)
                foreach (var c in dl.Cards)
                    Console.WriteLine($"      {c.CardId} '{CardData.GetCard(c.CardId)?.Name}' features='{string.Join("/", CardData.GetCard(c.CardId)?.Features ?? new List<string>())}'");
            foreach (var e in st.PendingEffects)
                Console.WriteLine($"  PendingEffect: seat={e.Seat} id={e.EffectId} optional={e.Optional} scope={e.Scope} zone={e.TargetZone} text='{e.Text}' cont='{e.PendingContinuation}'");
            if (st.ActiveChoice != null) Console.WriteLine($"  ActiveChoice: seat={st.ActiveChoice.Seat}");
            if (st.PendingCharReplace != null) Console.WriteLine($"  PendingCharReplace: seat={st.PendingCharReplace.Seat}");
            foreach (var kv in st.Players)
                Console.WriteLine($"  [{kv.Key}] hand={kv.Value.Hand.Count} deck={kv.Value.Deck.Count} trash={kv.Value.Trash.Count} life={kv.Value.Life.Count} don={kv.Value.CostArea.Count}");
            foreach (var l in st.EventLog.Skip(Math.Max(0, st.EventLog.Count - 10)))
                Console.WriteLine("   log: " + l.Message);
        }
    }
}
