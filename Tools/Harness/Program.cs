using System;
using System.Collections.Generic;
using System.Linq;
using OnePieceTcg.Engine;
using OnePieceTcg.Engine.Bot;

// Headless bot-vs-bot battle harness for the OPTCG engine.
// Loops every starter-deck pairing over several seeds, running IntermediateBot.PlayFullMatch,
// and flags: crashes (exceptions), non-finishing games (hit the command cap = likely deadlock/
// infinite loop), and basic end-state invariant violations. Prints a summary + the worst cases.

class Program
{
    const string CardJsonPath = @"C:\Users\Nperr\One Piece TCG Simulator\Assets\StreamingAssets\Cards\official-card-library.json";

    static int Main(string[] args)
    {
        int loaded = CardLibraryLoader.Load(CardJsonPath);
        Console.WriteLine($"Loaded {loaded} card definitions from JSON.\n");
        if (args.Length > 0 && args[0] == "scenario") return Scenarios.Run();
        if (args.Length > 0 && args[0] == "diag") { Diag(args[1], args[2], int.Parse(args[3])); return 0; }
        if (args.Length > 0 && args[0] == "trace") { Trace(args.Length > 1 ? args[1] : "st01", args.Length > 2 ? args[2] : "st02"); return 0; }
        int seedsPer = args.Length > 0 && int.TryParse(args[0], out var s) ? s : 3;
        var decks = CardData.StarterDecks.Keys.OrderBy(k => k).ToList();
        Console.WriteLine($"Starter decks ({decks.Count}): {string.Join(", ", decks)}");
        Console.WriteLine($"Seeds per pairing: {seedsPer}  →  {decks.Count * decks.Count * seedsPer} games\n");

        int games = 0, crashes = 0, stalls = 0, invariantFails = 0;
        var crashDetail = new List<string>();
        var stallDetail = new List<string>();
        var invDetail = new List<string>();
        long totalTurns = 0;
        int endDeckout = 0, endLife = 0, endOther = 0;
        var turnBuckets = new int[6]; // <=10, <=20, <=30, <=50, <=80, >80
        var deckGames = new Dictionary<string, int>();
        var deckDeckout = new Dictionary<string, int>();
        foreach (var d in decks) { deckGames[d] = 0; deckDeckout[d] = 0; }

        foreach (var sd in decks)
        foreach (var nd in decks)
        for (int seed = 0; seed < seedsPer; seed++)
        {
            games++;
            string tag = $"{sd} vs {nd} #{seed}";
            GameState state = null;
            try
            {
                var cfg = new MatchConfig { SouthDeck = sd, NorthDeck = nd, Seed = $"{sd}:{nd}:{seed}" };
                state = GameEngine.CreateMatch(cfg);
                IntermediateBot.PlayFullMatch(state, 20000);

                totalTurns += state.TurnNumber;
                int tn = state.TurnNumber;
                turnBuckets[tn <= 10 ? 0 : tn <= 20 ? 1 : tn <= 30 ? 2 : tn <= 50 ? 3 : tn <= 80 ? 4 : 5]++;
                string tail = LogTail(state).ToLowerInvariant();
                bool isDeckout = tail.Contains("no cards in deck") || tail.Contains("deck reached 0");
                if (isDeckout) endDeckout++;
                else if (tail.Contains("wins")) endLife++;
                else endOther++;
                deckGames[sd]++; deckGames[nd]++;
                if (isDeckout) { deckDeckout[sd]++; deckDeckout[nd]++; }

                if (state.Status != "finished")
                {
                    stalls++;
                    if (stallDetail.Count < 15) stallDetail.Add($"{tag}: status='{state.Status}' phase='{state.Phase}' turn={state.TurnNumber} active={state.ActiveSeat} {LogTail(state)}");
                    continue;
                }

                // ---- End-state invariants ----
                var problems = CheckInvariants(state);
                if (problems.Count > 0)
                {
                    invariantFails++;
                    if (invDetail.Count < 20) invDetail.Add($"{tag}: {string.Join("; ", problems)}");
                }
            }
            catch (Exception ex)
            {
                crashes++;
                string where = state == null ? "CreateMatch" : $"turn {state.TurnNumber}/{state.Phase}";
                if (crashDetail.Count < 25) crashDetail.Add($"{tag} @ {where}: {ex.GetType().Name}: {ex.Message}{LogTail(state)}");
            }
        }

        Console.WriteLine($"\n===== SUMMARY =====");
        Console.WriteLine($"games={games}  crashes={crashes}  stalls(non-finishing)={stalls}  invariantFails={invariantFails}");
        Console.WriteLine($"avg turns/finished game = {(games - crashes - stalls > 0 ? (double)totalTurns / (games - crashes - stalls) : 0):0.0}");
        Console.WriteLine($"end reason: lifeOut={endLife}  deckOut={endDeckout}  other={endOther}");
        Console.WriteLine($"turns: <=10:{turnBuckets[0]}  <=20:{turnBuckets[1]}  <=30:{turnBuckets[2]}  <=50:{turnBuckets[3]}  <=80:{turnBuckets[4]}  >80:{turnBuckets[5]}");

        Console.WriteLine("\n----- per-deck deck-out rate (deck : deckouts/games) sorted worst-first -----");
        foreach (var kv in deckDeckout.OrderByDescending(k => deckGames[k.Key] == 0 ? 0 : (double)k.Value / deckGames[k.Key]))
        {
            double rate = deckGames[kv.Key] == 0 ? 0 : 100.0 * kv.Value / deckGames[kv.Key];
            Console.WriteLine($"  {kv.Key,-10} {rate,5:0}%  ({kv.Value}/{deckGames[kv.Key]})");
        }

        Print("CRASHES", crashDetail);
        Print("STALLS (never reached 'finished' — deadlock/loop suspect)", stallDetail);
        Print("INVARIANT VIOLATIONS", invDetail);

        return (crashes + stalls + invariantFails) > 0 ? 1 : 0;
    }

    static void Diag(string sd, string nd, int seed)
    {
        var cfg = new MatchConfig { SouthDeck = sd, NorthDeck = nd, Seed = $"{sd}:{nd}:{seed}" };
        var st = GameEngine.CreateMatch(cfg);
        IntermediateBot.PlayFullMatch(st, 20000);
        Console.WriteLine($"{sd} vs {nd} #{seed}: status={st.Status} phase={st.Phase} turn={st.TurnNumber} active={st.ActiveSeat}");
        Console.WriteLine($"battle: {(st.Battle == null ? "null" : $"step={st.Battle.Step} attacker={st.Battle.AttackerId} target={st.Battle.TargetId} pendingDmg={st.Battle.PendingLifeDamage}")}");
        Console.WriteLine($"pending effects: {st.PendingEffects.Count}");
        foreach (var e in st.PendingEffects)
            Console.WriteLine($"  [{e.EffectId}] seat={e.Seat} timing={e.Timing} optional={e.Optional} sel={e.SelectionsRemaining} zone={e.TargetZone}\n     text='{(e.Text ?? "").Replace("\n", " / ")}'");
        Console.WriteLine("--- last 18 log lines ---");
        foreach (var l in st.EventLog.Skip(Math.Max(0, st.EventLog.Count - 18))) Console.WriteLine("  " + l.Message);
    }

    static void Trace(string sd, string nd)
    {
        Console.WriteLine($"--- {sd} vs {nd}, seeds 0..9 ---");
        for (int seed = 0; seed < 10; seed++)
        {
            var c = new MatchConfig { SouthDeck = sd, NorthDeck = nd, Seed = $"{sd}:{nd}:{seed}" };
            var st = GameEngine.CreateMatch(c);
            IntermediateBot.PlayFullMatch(st, 20000);
            var last = st.EventLog.Count > 0 ? (st.EventLog[st.EventLog.Count - 1].Message ?? "") : "";
            string reason = last.ToLower().Contains("no cards in deck") ? "DECKOUT" : last.ToLower().Contains("wins") ? "lifeout" : "?";
            Console.WriteLine($"  seed{seed}: turns={st.TurnNumber,3} {reason,7}  Slife={st.Players["south"].Life.Count} Nlife={st.Players["north"].Life.Count}  Sdeck={st.Players["south"].Deck.Count} Ndeck={st.Players["north"].Deck.Count}  last='{last}'");
        }
        Console.WriteLine();
        var cfg = new MatchConfig { SouthDeck = sd, NorthDeck = nd, Seed = $"{sd}:{nd}:0" };
        var state = GameEngine.CreateMatch(cfg);
        IntermediateBot.PlayFullMatch(state, 20000);
        int attacks = 0, dmg = 0, blocks = 0, counters = 0, leaderAtk = 0;
        foreach (var e in state.EventLog)
        {
            var m = (e.Message ?? "").ToLowerInvariant();
            if (m.Contains("declares an attack") || m.Contains("attacks")) attacks++;
            if (m.Contains("takes 1 damage")) dmg++;
            if (m.Contains("block")) blocks++;
            if (m.Contains("counter")) counters++;
        }
        Console.WriteLine($"{sd} vs {nd}: status={state.Status} turns={state.TurnNumber}");
        Console.WriteLine($"S life={state.Players["south"].Life.Count} deck={state.Players["south"].Deck.Count}  |  N life={state.Players["north"].Life.Count} deck={state.Players["north"].Deck.Count}");
        Console.WriteLine($"log lines={state.EventLog.Count}  attacks~={attacks}  lifeDamageEvents={dmg}  blockLines={blocks}  counterLines={counters}");
        Console.WriteLine("--- first 40 attack/damage/block/counter/win log lines ---");
        int shown = 0;
        foreach (var e in state.EventLog)
        {
            var m = e.Message ?? "";
            var ml = m.ToLowerInvariant();
            if (ml.Contains("attack") || ml.Contains("damage") || ml.Contains("block") || ml.Contains("counter") || ml.Contains("wins") || ml.Contains("no cards in deck"))
            {
                Console.WriteLine("  " + m);
                if (++shown >= 40) break;
            }
        }
    }

    static List<string> CheckInvariants(GameState state)
    {
        var problems = new List<string>();
        foreach (var seat in new[] { "south", "north" })
        {
            if (!state.Players.TryGetValue(seat, out var p)) { problems.Add($"{seat} missing"); continue; }
            if (p.Life.Count < 0) problems.Add($"{seat} negative life");
            if (p.Hand.Count > 60) problems.Add($"{seat} hand={p.Hand.Count} (runaway)");
            int boardCount = p.CharacterArea?.Count(c => c != null) ?? 0;
            if (boardCount > 5) problems.Add($"{seat} board={boardCount} (>5 chars)");
            // duplicate instance ids anywhere = a card got cloned instead of moved
            var ids = AllInstanceIds(p).ToList();
            var dupes = ids.GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (dupes.Count > 0) problems.Add($"{seat} duplicate instanceIds: {string.Join(",", dupes.Take(3))}");
        }
        return problems;
    }

    static IEnumerable<string> AllInstanceIds(PlayerState p)
    {
        foreach (var c in p.Hand) yield return c.InstanceId;
        foreach (var c in p.Deck) yield return c.InstanceId;
        foreach (var c in p.Trash) yield return c.InstanceId;
        foreach (var c in p.Life) yield return c.InstanceId;
        if (p.CharacterArea != null) foreach (var c in p.CharacterArea) if (c != null) yield return c.InstanceId;
    }

    static string LogTail(GameState state)
    {
        if (state?.EventLog == null || state.EventLog.Count == 0) return "";
        var last = state.EventLog.Skip(Math.Max(0, state.EventLog.Count - 4)).Select(e => e.Message ?? e.ToString());
        return "  | log: " + string.Join(" · ", last);
    }

    static void Print(string title, List<string> items)
    {
        if (items.Count == 0) return;
        Console.WriteLine($"\n----- {title} ({items.Count} shown) -----");
        foreach (var i in items) Console.WriteLine("  " + i);
    }
}
