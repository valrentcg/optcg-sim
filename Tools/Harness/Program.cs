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
        if (args.Length > 0 && args[0] == "coverage") return EffectCoverage.Run();
        if (args.Length > 0 && args[0] == "golden") return EffectCoverage.Golden(args.Length > 1 && args[1] == "write");
        if (args.Length > 0 && args[0] == "invariants") return InvariantSweep(args.Length > 1 && int.TryParse(args[1], out var q) ? q : 2);
        if (args.Length > 0 && args[0] == "diag") { Diag(args[1], args[2], int.Parse(args[3])); return 0; }
        if (args.Length > 0 && args[0] == "probe") { Probe(args[1]); return 0; }
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

    // LAYER 1 driver: replay every deck pairing one command at a time and assert the rules
    // invariants after EVERY command, reporting the first violation per game (with the command
    // that broke it). Catches transient rule breaks a bot-vs-bot end-state check would miss.
    static int InvariantSweep(int seedsPer)
    {
        var decks = CardData.StarterDecks.Keys.OrderBy(k => k).ToList();
        Console.WriteLine($"Invariant sweep: {decks.Count}x{decks.Count}x{seedsPer} = {decks.Count * decks.Count * seedsPer} games, checked per-command.\n");
        int games = 0, violGames = 0;
        var details = new List<string>();
        var byKind = new Dictionary<string, int>();
        foreach (var sd in decks)
        foreach (var nd in decks)
        for (int seed = 0; seed < seedsPer; seed++)
        {
            games++;
            var st = GameEngine.CreateMatch(new MatchConfig { SouthDeck = sd, NorthDeck = nd, Seed = $"{sd}:{nd}:{seed}" });
            string viol;
            try { viol = PlayWithInvariantChecks(st, 20000); }
            catch (Exception ex) { viol = $"CRASH {ex.GetType().Name}: {ex.Message}"; }
            if (viol != null)
            {
                violGames++;
                string kind = viol.Contains("::") ? viol.Substring(viol.IndexOf("::") + 2).Split(new[] { '=', '(', ':' }, 2)[0].Trim() : viol.Split(' ')[0];
                byKind[kind] = byKind.GetValueOrDefault(kind) + 1;
                if (details.Count < 40) details.Add($"{sd} vs {nd} #{seed}: {viol}");
            }
        }

        Console.WriteLine($"===== INVARIANTS =====");
        Console.WriteLine($"games={games}  gamesWithViolation={violGames}");
        foreach (var kv in byKind.OrderByDescending(k => k.Value)) Console.WriteLine($"  {kv.Value,5}x  {kv.Key}");
        Print("VIOLATIONS (first per game)", details);

        try
        {
            string dir = @"C:\Users\Nperr\One Piece TCG Simulator\Tools\Harness\findings";
            System.IO.Directory.CreateDirectory(dir);
            using var w = new System.IO.StreamWriter(System.IO.Path.Combine(dir, "invariant-violations.md"), false);
            w.WriteLine("# Invariant Violations (per-command)\n");
            w.WriteLine($"Generated {DateTime.Now:yyyy-MM-dd HH:mm}. `Tools/Harness invariants {seedsPer}` — NOT shipped.\n");
            w.WriteLine($"- games: {games}\n- games with a violation: {violGames}\n");
            foreach (var kv in byKind.OrderByDescending(k => k.Value)) w.WriteLine($"- {kv.Value}x `{kv.Key}`");
            w.WriteLine("\n## First violation per affected game\n");
            foreach (var d in details) w.WriteLine($"- {d}");
        }
        catch { }

        return violGames > 0 ? 1 : 0;
    }

    static string PlayWithInvariantChecks(GameState state, int maxTotal)
    {
        int total = 0;
        while (state.Status != "finished" && total < maxTotal)
        {
            var (aS, vS) = StepSeat(state, "south", maxTotal - total); total += aS; if (vS != null) return vS;
            var (aN, vN) = StepSeat(state, "north", maxTotal - total); total += aN; if (vN != null) return vN;
            if (aS == 0 && aN == 0) break;
        }
        return null;
    }

    // Mirrors IntermediateBot.TakeAllAvailableActions but checks invariants after every command.
    static (int applied, string violation) StepSeat(GameState state, string seat, int maxCommands)
    {
        int applied = 0;
        var blacklist = new HashSet<string>();
        for (int i = 0; i < maxCommands; i++)
        {
            var cmd = IntermediateBot.DecideOneCommand(state, seat, blacklist);
            if (cmd == null) break;
            object before = IntermediateBot.SnapshotFor(state, cmd);
            GameEngine.ApplyCommand(state, cmd);
            applied++;

            var problems = Invariants.Structural(state);
            problems.AddRange(Invariants.Conservation(state));
            if (problems.Count > 0)
                return (applied, $"turn {state.TurnNumber} after [{IntermediateBot.Signature(cmd)}] :: {string.Join("; ", problems.Take(3))}");

            if (!IntermediateBot.Succeeded(state, cmd, before))
                blacklist.Add(IntermediateBot.Signature(cmd));
        }
        return (applied, null);
    }

    // Stage a single card's [On Play] in a controlled board and step through its pending effects
    // one command at a time, printing the ORDERED sequence of prompts (buttons) the client would
    // show, plus zone counts before/after — so a dropped or mis-ordered clause is visible.
    static void Probe(string cardId)
    {
        var def = CardData.GetCard(cardId);
        Console.WriteLine($"=== PROBE {cardId} {def?.Name} ===\n{def?.Effect}\n");
        var st = GameEngine.CreateMatch(new MatchConfig { SouthDeck = "st01", NorthDeck = "st01", Seed = "probe:" + cardId });
        st.Status = "active"; st.Phase = "main"; st.ActiveSeat = "south"; st.TurnNumber = 5;
        var S = st.Players["south"]; var N = st.Players["north"];
        S.TurnsStarted = 3; N.TurnsStarted = 3;
        S.Hand.Clear();
        for (int i = 0; i < 5; i++) { S.CharacterArea[i] = null; N.CharacterArea[i] = null; }
        S.CostArea.Clear();
        // For DON!!-ramp cards ("… from your DON!! deck …") leave headroom under the 10-DON cap and stock the
        // DON!! deck, so the add actually resolves and can be verified; other probes keep the full 10 active.
        bool donRamp = (def?.Effect ?? "").IndexOf("DON!! deck", StringComparison.OrdinalIgnoreCase) >= 0;
        int startDon = donRamp ? 8 : 10;   // 8 = 6 active after 2 pre-rested (enough to play), +2 headroom to add
        for (int i = 0; i < startDon; i++) S.CostArea.Add(new DonInstance { InstanceId = $"pcd{i}", Rested = false });
        // Pre-rest 2 so "give rested DON!!" effects have material — but NOT for DON-ramp cards, which need the
        // active DON to pay their (often high) cost and create their own rested DON via the add.
        if (!donRamp) { S.CostArea[0].Rested = true; S.CostArea[1].Rested = true; }
        S.DonDeck = donRamp ? 4 : 0;
        N.CharacterArea[0] = new CardInstance { InstanceId = "n-target", CardId = "ST01-006", Owner = "north", Zone = "character", Rested = false };
        N.CharacterArea[1] = new CardInstance { InstanceId = "n-target2", CardId = "ST01-006", Owner = "north", Zone = "character", Rested = false };
        if (S.Life.Count == 0)
            for (int i = 0; i < 4; i++) S.Life.Add(new CardInstance { InstanceId = $"life{i}", CardId = "ST01-006", Owner = "south", Zone = "life" });
        // Fill the hand with recognizable, playable cheap Characters so trash/play steps have targets.
        for (int i = 0; i < 5; i++)
            S.Hand.Add(new CardInstance { InstanceId = $"hand{i}", CardId = "ST01-006", Owner = "south", Zone = "hand" });
        // Activate: Main cards (no On Play) are placed in play and activated; everything else is
        // played from hand to fire its [On Play].
        bool isActivate = (def?.Effect ?? "").Contains("[Activate: Main]") && !(def?.Effect ?? "").Contains("[On Play]");
        CardInstance src;
        int mark;
        if (isActivate)
        {
            bool isStage = (def?.Type ?? "") == "stage";
            src = new CardInstance { InstanceId = "SRC", CardId = cardId, Owner = "south", Zone = isStage ? "stage" : "character", Rested = false, PlayedOnTurn = 0 };
            if (isStage) S.Stage = src; else S.CharacterArea[1] = src;
            Console.WriteLine($"BEFORE: hand={S.Hand.Count} board={S.CharacterArea.Count(c => c != null)} trash={S.Trash.Count} deck={S.Deck.Count}\n");
            mark = st.EventLog.Count;
            GameEngine.ApplyCommand(st, new GameCommand { Type = "activateMain", Seat = "south", Target = src.InstanceId });
        }
        else
        {
            src = new CardInstance { InstanceId = "SRC", CardId = cardId, Owner = "south", Zone = "hand" };
            S.Hand.Add(src);
            Console.WriteLine($"BEFORE: hand={S.Hand.Count} board={S.CharacterArea.Count(c => c != null)} trash={S.Trash.Count} deck={S.Deck.Count}\n");
            mark = st.EventLog.Count;
            GameEngine.ApplyCommand(st, new GameCommand { Type = "playCard", Seat = "south", InstanceId = src.InstanceId, SlotIndex = 0 });
        }

        // Step through prompts: at each step print the active pending effect (button text/zone), then
        // supply the FIRST hand card as a target (so trash/play steps actually consume a card).
        int guard = 0;
        while (guard++ < 40)
        {
            if (st.DeckLook != null && st.DeckLook.Seat == "south")
            {
                var dl = st.DeckLook;
                string stepBefore = dl.Step; int cardsBefore = dl.Cards.Count;
                Console.WriteLine($"DECKLOOK step={dl.Step} cards={dl.Cards.Count} src={dl.SourceName} postLook='{dl.PostLookClause}'");
                if (dl.Step == "select")
                {
                    string dlPick = dl.Cards.FirstOrDefault()?.InstanceId ?? "";
                    int leftBefore = dl.Cards.Count;
                    GameEngine.ApplyCommand(st, new GameCommand { Type = "deckLookSelect", Seat = "south", Target = dlPick });
                    if (st.DeckLook == dl && dl.Cards.Count == leftBefore)
                        GameEngine.ApplyCommand(st, new GameCommand { Type = "deckLookSelect", Seat = "south", Target = "" });
                }
                else if (dl.Step == "rearrange")
                    GameEngine.ApplyCommand(st, new GameCommand { Type = "deckLookConfirmOrder", Seat = "south", OrderedInstanceIds = dl.Cards.Select(c => c.InstanceId).ToList() });
                else
                    GameEngine.ApplyCommand(st, new GameCommand { Type = "deckLookScryConfirm", Seat = "south", OrderedInstanceIds = new System.Collections.Generic.List<string>() });
                if (ReferenceEquals(st.DeckLook, dl) && st.DeckLook.Step == stepBefore && st.DeckLook.Cards.Count == cardsBefore) st.DeckLook = null;
                continue;
            }
            var pe = st.PendingEffects.FirstOrDefault(e => e.Seat == "south");
            if (pe == null) break;
            Console.WriteLine($"PROMPT #{guard}: zone={pe.TargetZone} sel={pe.SelectionsRemaining} optional={pe.Optional}");
            Console.WriteLine($"    text='{(pe.Text ?? "").Replace("\n", " / ")}'");
            Console.WriteLine($"    original='{(pe.OriginalText ?? "").Replace("\n", " / ")}'");
            Console.WriteLine($"    done=[{string.Join(" | ", pe.DoneParts ?? new System.Collections.Generic.List<string>())}]  skipped=[{string.Join(" | ", pe.SkippedParts ?? new System.Collections.Generic.List<string>())}]");
            Console.WriteLine($"    validTargets: {DumpValidTargets(st, pe)}");
            // "give rested DON!! to your Leader" wants a rested DON!! id; otherwise prefer an opponent
            // Character (removal / can't-attack), else a hand card.
            string peText = pe.Text ?? "";
            string pick;
            if (peText.IndexOf("rested DON!!", StringComparison.OrdinalIgnoreCase) >= 0 && peText.IndexOf("Leader", StringComparison.OrdinalIgnoreCase) >= 0)
                pick = S.CostArea.FirstOrDefault(d => d.Rested)?.InstanceId;
            else if (peText.IndexOf("top or bottom of your Life", StringComparison.OrdinalIgnoreCase) >= 0)
                pick = S.Life.LastOrDefault()?.InstanceId;   // top of Life = end of the list
            else
                pick = st.Players["north"].CharacterArea.FirstOrDefault(c => c != null)?.InstanceId
                       ?? S.Hand.FirstOrDefault(c => c.InstanceId != "SRC")?.InstanceId;
            // Progress signature spanning BOTH players' zones + the effect's own selection counter,
            // so a move that only touches the opponent's board (e.g. Gravity Blade placing an enemy
            // Character at the bottom of THEIR deck) still reads as progress and isn't falsely skipped.
            long Sig() => st.Players.Values.Sum(pl => pl.Hand.Count + pl.Trash.Count + pl.Deck.Count
                          + pl.CharacterArea.Count(c => c != null)) * 100 + pe.SelectionsRemaining;
            long before = Sig();
            GameEngine.ApplyCommand(st, new GameCommand { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId, Target = pick });
            long after = st.PendingEffects.Contains(pe) ? Sig() : -1;
            if (before == after && st.PendingEffects.Contains(pe))
            {   // target rejected — try passing/skipping so we don't spin
                GameEngine.ApplyCommand(st, new GameCommand { Type = "passEffect", Seat = "south", EffectId = pe.EffectId });
            }
        }
        Console.WriteLine($"\nAFTER: hand={S.Hand.Count} board={S.CharacterArea.Count(c => c != null)} trash={S.Trash.Count} deck={S.Deck.Count}");
        Console.WriteLine("\n--- resolution log ---");
        foreach (var l in st.EventLog.Skip(mark)) Console.WriteLine("  " + l.Message);
    }

    // Which cards in play/hand/trash/Life the client would glow green for this pending step.
    static string DumpValidTargets(GameState st, PendingEffect pe)
    {
        var outp = new System.Collections.Generic.List<string>();
        foreach (var seat in new[] { "south", "north" })
        {
            var p = st.Players[seat];
            void Chk(CardInstance c, string where)
            {
                if (c != null && GameEngine.IsValidEffectTarget(st, pe, c))
                    outp.Add($"{where}:{CardData.GetCard(c.CardId)?.Name}");
            }
            Chk(p.Leader, seat + "/leader");
            foreach (var c in p.CharacterArea) Chk(c, seat + "/char");
            foreach (var c in p.Hand) Chk(c, seat + "/hand");
            foreach (var c in p.Trash) Chk(c, seat + "/trash");
            foreach (var c in p.Life) Chk(c, seat + "/life");
            if (p.Stage != null) Chk(p.Stage, seat + "/stage");
        }
        return outp.Count == 0 ? "(none)" : string.Join(", ", outp);
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
