using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OnePieceTcg.Engine;

// LAYER 0 — per-card effect COVERAGE sweep.
//
// For every card in CardData.Library, this drives the card's effect in a minimal, auto-resolved
// scenario (one per applicable timing) and classifies the outcome:
//   OK            — the effect resolved with no "manual resolution" fallback and left nothing stuck.
//   NOT_AUTOMATED — the engine logged "acknowledged for manual resolution" => wording no handler
//                   recognizes (unimplemented / partially-implemented effect). The actionable list.
//   STUCK         — a pending effect / choice / deck-look for the actor could not be driven to
//                   completion within the budget (targeting or handler gap; possible deadlock).
//   CRASH         — the effect threw.
//   INVARIANT     — a structural rule invariant broke while resolving (dup ids, >5 board, dup zone).
// Cards whose only effects use timings this harness can't stage yet (On Block, On K.O., passive
// auras, End/Start of turn, opponent's-attack reactions) are reported UNEXERCISED, by timing, so
// nothing is silently skipped.
//
// This is a COVERAGE + not-silently-broken oracle, not a full rulebook oracle: it proves an effect
// is wired up and rules-invariant, not that a wired effect matches its text exactly (that is the job
// of the golden snapshots + invariants layers).
static class EffectCoverage
{
    const string ManualMarker = "acknowledged for manual resolution";
    // Distinct condition wordings EvaluateCondition couldn't parse (→ treated as not met), with an
    // example card. Surfaces "conditions we may be missing" across the whole library.
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> UnknownConditions = new();

    enum Verdict { OK, NotAutomated, Stuck, Crash, Invariant, Unexercised }

    sealed class Result
    {
        public string Id, Name, Timing, Detail;
        public Verdict Verdict;
        public List<string> Suspects; // compound sub-clauses that left no trace in the resolution log
        public string Snapshot;       // normalized outcome for golden-regression comparison
    }

    public static int Run()
    {
        var results = new List<Result>();
        var cards = CardData.Library.Values
            .Where(c => c != null && (c.Type == "leader" || c.Type == "character" || c.Type == "event" || c.Type == "stage"))
            .OrderBy(c => c.Id, StringComparer.Ordinal)
            .ToList();

        Console.WriteLine($"Coverage sweep over {cards.Count} cards…");
        foreach (var def in cards)
        {
            var timings = ApplicableTimings(def);
            if (timings.Count == 0)
            {
                results.Add(new Result { Id = def.Id, Name = def.Name, Timing = UnexercisedLabel(def), Detail = "", Verdict = Verdict.Unexercised });
                continue;
            }
            foreach (var t in timings)
                results.Add(RunOne(def, t));
        }

        WriteReport(results);
        PrintSummary(results);
        // Coverage is a report, not a gate — return 0 unless something actually threw the harness.
        return 0;
    }

    // ---- Which timings can we stage for this card? -------------------------------------------
    static List<string> ApplicableTimings(CardDef def)
    {
        var e = def.Effect ?? "";
        var list = new List<string>();
        if (Has(e, "[On Play]") && (def.Type == "character" || def.Type == "stage")) list.Add("OnPlay");
        if (Has(e, "[Activate: Main]")) list.Add("ActivateMain");
        if (Has(e, "[When Attacking]")) list.Add("WhenAttacking");
        if (!string.IsNullOrWhiteSpace(def.Trigger) || Has(e, "[Trigger]")) list.Add("Trigger");
        // Events with a [Main] effect resolve when played.
        if (def.Type == "event" && Has(e, "[Main]")) list.Add("PlayEvent");
        // Reactive/timed timings staged by driving the trigger condition.
        if ((Has(e, "[On K.O.]") || Has(e, "[On KO]")) && def.Type == "character") list.Add("OnKo");
        if (Has(e, "[On Block]") && def.Type == "character") list.Add("OnBlock");
        if (Has(e, "[On Your Opponent's Attack]") && (def.Type == "character" || def.Type == "leader")) list.Add("OnOpponentAttack");
        if (Has(e, "[End of Your Turn]") && (def.Type == "character" || def.Type == "leader" || def.Type == "stage")) list.Add("EndOfTurn");
        if (def.Type == "event" && Has(e, "[Counter]")) list.Add("PlayCounter");
        return list;
    }

    static string UnexercisedLabel(CardDef def)
    {
        var e = def.Effect ?? "";
        var tags = new[] { "[On Block]", "[On K.O.]", "[On Your Opponent's Attack]",
            "[End of Your Turn]", "[Start of Your Turn]", "[End of Opponent's Turn]",
            "[Blocker]", "[DON!! x1]", "[DON!! x2]", "[Rush]", "[Double Attack]", "[Counter]" };
        var present = tags.Where(t => Has(e, t)).ToList();
        if (string.IsNullOrWhiteSpace(e)) return "(no effect text)";
        return present.Count > 0 ? string.Join("+", present) : "(other/passive)";
    }

    // ---- Run one (card, timing) scenario ------------------------------------------------------
    static Result RunOne(CardDef def, string timing)
    {
        var r = new Result { Id = def.Id, Name = def.Name, Timing = timing };
        GameState st = null;
        int mark = 0;
        try
        {
            st = BaseState(def.Id);
            var S = st.Players["south"];
            switch (timing)
            {
                case "OnPlay":
                case "PlayEvent":
                {
                    var c = Instance(def.Id, "south", "hand");
                    S.Hand.Add(c);
                    mark = st.EventLog.Count;
                    Apply(st, new GameCommand { Type = "playCard", Seat = "south", InstanceId = c.InstanceId, SlotIndex = 0 });
                    break;
                }
                case "ActivateMain":
                {
                    CardInstance c = PlaceSource(st, def);
                    mark = st.EventLog.Count;
                    Apply(st, new GameCommand { Type = "activateMain", Seat = "south", Target = c.InstanceId });
                    break;
                }
                case "WhenAttacking":
                {
                    var N = st.Players["north"];
                    CardInstance atk = def.Type == "leader" ? SetLeader(st, "south", def.Id) : null;
                    if (atk == null) { atk = Instance(def.Id, "south", "character"); atk.PlayedOnTurn = 0; atk.Rested = false; S.CharacterArea[0] = atk; }
                    mark = st.EventLog.Count;
                    Apply(st, new GameCommand { Type = "declareAttack", Seat = "south", Attacker = atk.InstanceId, Target = N.Leader.InstanceId });
                    break;
                }
                case "Trigger":
                {
                    var N = st.Players["north"];
                    var t = Instance(def.Id, "south", "life");
                    S.Life.Add(t); // last element = TOP of Life (revealed first)
                    var natk = Instance("ST08-005", "north", "character"); natk.PlayedOnTurn = 0; natk.Rested = false; N.CharacterArea[0] = natk; // Shanks 10000
                    st.ActiveSeat = "north"; st.Phase = "main"; st.TurnNumber = 6;
                    mark = st.EventLog.Count;
                    Apply(st, new GameCommand { Type = "declareAttack", Seat = "north", Attacker = natk.InstanceId, Target = S.Leader.InstanceId });
                    Apply(st, new GameCommand { Type = "passBlock", Seat = "south" });
                    Apply(st, new GameCommand { Type = "passCounter", Seat = "south" });
                    Apply(st, new GameCommand { Type = "resolveAttack", Seat = "south" });
                    Apply(st, new GameCommand { Type = "useTrigger", Seat = "south" });
                    break;
                }
                case "OnKo":
                {
                    // Put the Character on the board, then K.O. it (trash) to fire [On K.O.].
                    var c = Instance(def.Id, "south", "character"); c.PlayedOnTurn = 0; c.Rested = false;
                    S.CharacterArea[0] = c;
                    // Respect the printed turn gate: [Opponent's Turn] On K.O. fires on north's turn.
                    bool oppTurn = Has(def.Effect, "[Opponent's Turn]") && !Has(def.Effect, "[Your Turn]");
                    st.ActiveSeat = oppTurn ? "north" : "south";
                    mark = st.EventLog.Count;
                    Apply(st, new GameCommand { Type = "trash", Seat = "south", InstanceId = c.InstanceId });
                    break;
                }
                case "OnBlock":
                {
                    // North attacks south's Leader; south blocks with this [Blocker]+[On Block] card.
                    var N = st.Players["north"];
                    var blocker = Instance(def.Id, "south", "character"); blocker.PlayedOnTurn = 0; blocker.Rested = false; S.CharacterArea[0] = blocker;
                    var natk = Instance("ST08-005", "north", "character"); natk.PlayedOnTurn = 0; natk.Rested = false; N.CharacterArea[0] = natk;
                    st.ActiveSeat = "north"; st.Phase = "main"; st.TurnNumber = 6;
                    mark = st.EventLog.Count;
                    Apply(st, new GameCommand { Type = "declareAttack", Seat = "north", Attacker = natk.InstanceId, Target = S.Leader.InstanceId });
                    Apply(st, new GameCommand { Type = "blockAttack", Seat = "south", Blocker = blocker.InstanceId });
                    break;
                }
                case "OnOpponentAttack":
                {
                    // North declares an attack; south's [On Your Opponent's Attack] fires on defense.
                    var N = st.Players["north"];
                    CardInstance holder = def.Type == "leader" ? SetLeader(st, "south", def.Id) : null;
                    if (holder == null) { holder = Instance(def.Id, "south", "character"); holder.PlayedOnTurn = 0; holder.Rested = false; S.CharacterArea[0] = holder; }
                    var natk = Instance("ST08-005", "north", "character"); natk.PlayedOnTurn = 0; natk.Rested = false; N.CharacterArea[0] = natk;
                    st.ActiveSeat = "north"; st.Phase = "main"; st.TurnNumber = 6;
                    mark = st.EventLog.Count;
                    Apply(st, new GameCommand { Type = "declareAttack", Seat = "north", Attacker = natk.InstanceId, Target = S.Leader.InstanceId });
                    break;
                }
                case "PlayCounter":
                {
                    // North attacks south's Leader; south plays this [Counter] event in the Counter Step.
                    var N = st.Players["north"];
                    var ce = Instance(def.Id, "south", "hand"); S.Hand.Add(ce);
                    var natk = Instance("ST08-005", "north", "character"); natk.PlayedOnTurn = 0; natk.Rested = false; N.CharacterArea[0] = natk;
                    st.ActiveSeat = "north"; st.Phase = "main"; st.TurnNumber = 6;
                    mark = st.EventLog.Count;
                    Apply(st, new GameCommand { Type = "declareAttack", Seat = "north", Attacker = natk.InstanceId, Target = S.Leader.InstanceId });
                    Apply(st, new GameCommand { Type = "passBlock", Seat = "south" });
                    Apply(st, new GameCommand { Type = "counterWithCard", Seat = "south", InstanceId = ce.InstanceId });
                    break;
                }
                case "EndOfTurn":
                {
                    // South (turn player) ends their turn → [End of Your Turn] fires.
                    CardInstance holder = def.Type == "leader" ? SetLeader(st, "south", def.Id)
                        : def.Type == "stage" ? (st.Players["south"].Stage = Instance(def.Id, "south", "stage"))
                        : null;
                    if (holder == null) { holder = Instance(def.Id, "south", "character"); holder.PlayedOnTurn = 0; holder.Rested = false; S.CharacterArea[0] = holder; }
                    st.ActiveSeat = "south"; st.Phase = "main";
                    mark = st.EventLog.Count;
                    Apply(st, new GameCommand { Type = "endTurn", Seat = "south" });
                    break;
                }
            }

            string actor = timing == "Trigger" ? "south" : "south";
            DriveToQuiescence(st, actor, 400);

            // Classify.
            var newLog = st.EventLog.Skip(mark).Select(l => l.Message ?? "").ToList();
            foreach (var m in newLog)
            {
                var uc = System.Text.RegularExpressions.Regex.Match(m, @"Unknown condition '([^']*)'");
                if (uc.Success) UnknownConditions.TryAdd(uc.Groups[1].Value, $"{def.Id} {def.Name} [{timing}]");
            }
            r.Snapshot = Snapshot(st, newLog);
            if (newLog.Any(m => m.Contains(ManualMarker)))
            {
                r.Verdict = Verdict.NotAutomated;
                r.Detail = newLog.First(m => m.Contains(ManualMarker));
                return r;
            }
            var inv = Invariants.Structural(st);
            inv.AddRange(Invariants.Conservation(st));
            if (inv.Count > 0) { r.Verdict = Verdict.Invariant; r.Detail = string.Join("; ", inv); return r; }
            if (st.PendingEffects.Any(e => e.Seat == actor) || (st.DeckLook != null && st.DeckLook.Seat == actor) || (st.ActiveChoice != null && st.ActiveChoice.Seat == actor))
            {
                r.Verdict = Verdict.Stuck;
                var pe = st.PendingEffects.FirstOrDefault(e => e.Seat == actor);
                r.Detail = pe != null ? $"pending: {(pe.Text ?? "").Replace("\n", " / ")}"
                          : st.DeckLook != null ? $"deckLook step={st.DeckLook.Step} left={st.DeckLook.Cards.Count}"
                          : $"choice pending";
                return r;
            }
            r.Verdict = Verdict.OK;
            r.Suspects = CompoundSuspects(def, timing, newLog);
            return r;
        }
        catch (Exception ex)
        {
            r.Verdict = Verdict.Crash;
            r.Detail = $"{ex.GetType().Name}: {ex.Message}";
            return r;
        }
    }

    // Drive every pending choice/deck-look/effect for `seat` to completion (or budget out).
    // Where an effect needs a target we actually SUPPLY plausible targets (opponent/own board,
    // hand, trash) so the resolution body executes — that's what lets invariants/crashes surface —
    // and only skip when no candidate makes progress.
    static void DriveToQuiescence(GameState st, string seat, int budget)
    {
        for (int step = 0; step < budget; step++)
        {
            if (st.DeckLook != null && st.DeckLook.Seat == seat)
            {
                var dl = st.DeckLook; object before = dl;
                string stepBefore = dl.Step; int cardsBefore = dl.Cards.Count;
                if (dl.Step == "select")
                {
                    // Try to actually take the first card (exercises the add/play/trash path); if
                    // that doesn't progress (ineligible), take nothing, which always resolves.
                    string pick = dl.Cards.FirstOrDefault()?.InstanceId ?? "";
                    int leftBefore = dl.Cards.Count;
                    Apply(st, new GameCommand { Type = "deckLookSelect", Seat = seat, Target = pick });
                    if (st.DeckLook == dl && dl.Cards.Count == leftBefore)
                        Apply(st, new GameCommand { Type = "deckLookSelect", Seat = seat, Target = "" });
                }
                else if (dl.Step == "rearrange")
                    Apply(st, new GameCommand { Type = "deckLookConfirmOrder", Seat = seat, OrderedInstanceIds = dl.Cards.Select(c => c.InstanceId).ToList() });
                else
                    Apply(st, new GameCommand { Type = "deckLookScryConfirm", Seat = seat, OrderedInstanceIds = new List<string>() });
                // Safety: only bail if the look is genuinely STUCK — same object, same step, same
                // card count (no progress). A select→rearrange transition keeps the same object but
                // changes the step, so it must NOT be treated as stuck (that aborted deck-looks
                // before completion, so a post-look tail like "Then, trash 1 from your hand" — see
                // DeckLookState.PostLookClause — never fired).
                if (ReferenceEquals(st.DeckLook, before) && st.DeckLook.Step == stepBefore && st.DeckLook.Cards.Count == cardsBefore)
                    st.DeckLook = null;
                continue;
            }
            if (st.ActiveChoice != null && st.ActiveChoice.Seat == seat)
            {
                Apply(st, new GameCommand { Type = "resolveChoice", Seat = seat, Target = "A" });
                continue;
            }
            var pe = st.PendingEffects.FirstOrDefault(e => e.Seat == seat);
            if (pe != null)
            {
                if (TryAdvanceEffect(st, seat, pe)) continue;
                Apply(st, new GameCommand { Type = "passEffect", Seat = seat, EffectId = pe.EffectId });
                continue;
            }
            break;
        }
    }

    // Attempt one step of progress on a pending effect. Returns true if state moved (effect gone,
    // a selection consumed, or a new choice/deck-look opened) — so the caller keeps driving.
    static bool TryAdvanceEffect(GameState st, string seat, PendingEffect pe)
    {
        string id = pe.EffectId;
        foreach (var target in EffectTargets(st, seat, pe))
        {
            long sig = Progress(st, id);
            Apply(st, new GameCommand { Type = "resolveEffect", Seat = seat, EffectId = id, Target = target });
            if (Progress(st, id) != sig) return true;
        }
        return false;
    }

    // A progress signature over real STATE (never the log length — an invalid-target attempt logs
    // "not a valid target" without progressing, and counting that as progress would churn forever).
    // Tracks: is the effect still present, its remaining selections/DON payment, total pending
    // count (chained follow-ups), and whether a choice/deck-look opened.
    static long Progress(GameState st, string effectId)
    {
        var e = st.PendingEffects.FirstOrDefault(x => x.EffectId == effectId);
        long present = e == null ? 0 : 1;
        long sel = (e?.SelectionsRemaining ?? 0) + (e?.DonPaymentRemaining ?? 0);
        long pend = st.PendingEffects.Count;
        long extra = (st.ActiveChoice != null ? 2 : 0) + (st.DeckLook != null ? 4 : 0);
        return present * 1_000_000_000 + sel * 1_000_000 + pend * 1000 + extra;
    }

    // Candidate targets to try, in order: null (auto/enter-selection), opponent board, own board,
    // own hand, own trash. Kept small so a multi-select effect converges within the step budget.
    static IEnumerable<string> EffectTargets(GameState st, string seat, PendingEffect pe)
    {
        yield return null;
        var me = st.Players[seat];
        var opp = st.Players[seat == "south" ? "north" : "south"];
        if (opp.Leader != null) yield return opp.Leader.InstanceId;
        foreach (var c in opp.CharacterArea) if (c != null) yield return c.InstanceId;
        foreach (var c in me.CharacterArea) if (c != null) yield return c.InstanceId;
        if (me.Leader != null) yield return me.Leader.InstanceId;
        if (me.Stage != null) yield return me.Stage.InstanceId;
        foreach (var c in me.Hand.Take(3)) yield return c.InstanceId;
        foreach (var c in me.Trash.Take(3)) yield return c.InstanceId;
        foreach (var d in me.CostArea.Take(3)) yield return d.InstanceId;
    }

    // ---- Scenario scaffolding -----------------------------------------------------------------
    static GameState BaseState(string cardId)
    {
        var st = GameEngine.CreateMatch(new MatchConfig { SouthDeck = "st01", NorthDeck = "st01", Seed = "cov:" + cardId });
        st.Status = "active"; st.Phase = "main"; st.ActiveSeat = "south"; st.TurnNumber = 5;
        var S = st.Players["south"]; var N = st.Players["north"];
        S.TurnsStarted = 3; N.TurnsStarted = 3;
        S.Hand.Clear();
        for (int i = 0; i < 5; i++) { S.CharacterArea[i] = null; N.CharacterArea[i] = null; }
        S.CostArea.Clear();
        for (int i = 0; i < 10; i++) S.CostArea.Add(new DonInstance { InstanceId = $"scd{i}", Rested = false });
        S.DonDeck = 0; // 10 in cost area + 0 in deck = 10 total, so DON-conservation stays valid
        // A cheap opponent Character so removal/target effects have something to touch.
        var nc = Instance("ST01-006", "north", "character"); nc.Rested = false; N.CharacterArea[0] = nc;
        // Spare cheap Characters in hand and trash so "play up to 1 … from your hand/trash" riders
        // have a legal target (otherwise they correctly no-op and read as false dropped-clause hits).
        var spareHand = Instance("ST01-006", "south", "hand"); S.Hand.Add(spareHand);
        var spareTrash = Instance("ST01-006", "south", "trash"); S.Trash.Add(spareTrash);
        // Give south some Life cards so Life-zone effects (trash-from-Life, add-to-Life, Life scry,
        // "N or less Life" gates) are actually exercised instead of no-op'ing on an empty Life area.
        if (S.Life.Count == 0)
            for (int i = 0; i < 4; i++) { var lc = Instance("ST01-006", "south", "life"); S.Life.Add(lc); }
        return st;
    }

    static CardInstance PlaceSource(GameState st, CardDef def)
    {
        var S = st.Players["south"];
        if (def.Type == "leader") return SetLeader(st, "south", def.Id);
        if (def.Type == "stage") { var s = Instance(def.Id, "south", "stage"); S.Stage = s; return s; }
        var c = Instance(def.Id, "south", "character"); c.PlayedOnTurn = 0; c.Rested = false;
        int slot = S.CharacterArea.FindIndex(x => x == null); if (slot < 0) slot = 0;
        S.CharacterArea[slot] = c; return c;
    }

    static CardInstance SetLeader(GameState st, string seat, string cardId)
    {
        var p = st.Players[seat];
        p.Leader.CardId = cardId; p.Leader.Owner = seat; p.Leader.Zone = "leader";
        return p.Leader;
    }

    static int counter;
    static CardInstance Instance(string cardId, string owner, string zone) => new CardInstance
    {
        InstanceId = $"{owner}-{cardId}-cov{counter++}",
        CardId = cardId, Owner = owner, Zone = zone, Rested = false,
    };

    static void Apply(GameState st, GameCommand cmd)
    {
        GameEngine.ApplyCommand(st, cmd);
    }

    static bool Has(string s, string tag) => (s ?? "").IndexOf(tag, StringComparison.OrdinalIgnoreCase) >= 0;

    // ---- Compound-clause "dropped clause" detector (the Law-class bug) ------------------------
    // Split the resolved timing body into action sub-clauses; each recognized action verb implies
    // an evidence keyword that should appear in the resolution log. A sub-clause whose evidence is
    // missing is a SUSPECT dropped clause. Heuristic (condition-gated clauses that legitimately
    // no-op in this scenario can show up) — a prioritized review list, not a hard failure.
    // High-signal cues only. "play up to … from hand/trash" leaving no "play" log is the exact
    // Law-class dropped-clause signature; draw/K.O. drops are similarly unambiguous. Deck-look
    // riders ("place the rest at the bottom", "reveal … add to hand") are deliberately excluded —
    // the DeckLook flow handles them without emitting a matching keyword, so they read as noise.
    static readonly (string cue, string evidence)[] ActionCues =
    {
        ("play up to", "play"), ("play 1 ", "play"),
        ("k.o. up to", "k.o."), ("draw 1 card", "draw"), ("draw 2 card", "draw"),
        // Self-hand disposal clauses in a compound ("Draw N and trash/place M from your hand …")
        // were silently dropped by the greedy Draw/Play handlers (ST25-003 Crocodile & Mihawk).
        // A dropped trash leaves no "from hand"/"placed" trace — flag it so it can't regress.
        ("trash 1 card from your hand", "from hand"), ("trash 2 cards from your hand", "from hand"),
        ("place 1 card from your hand", "placed"), ("place 2 cards from your hand", "placed"),
    };

    static List<string> CompoundSuspects(CardDef def, string timing, List<string> newLog)
    {
        string body = TimingBody(def, timing);
        if (string.IsNullOrWhiteSpace(body)) return null;
        var parts = SplitCompound(body);
        if (parts.Count < 2) return null;                 // not a compound — nothing to cross-check
        string logAll = string.Join(" \n ", newLog).ToLowerInvariant();
        var suspects = new List<string>();
        foreach (var part in parts)
        {
            var pl = part.ToLowerInvariant().TrimStart();
            // Condition-gated clauses ("if your Leader is X", "if you have N…") legitimately no-op
            // when the condition isn't met in this generic scenario — not a dropped clause.
            if (pl.StartsWith("if ")) continue;
            foreach (var (cue, evidence) in ActionCues)
            {
                if (pl.Contains(cue) && !logAll.Contains(evidence))
                {
                    suspects.Add(part.Trim());
                    break;
                }
            }
        }
        return suspects.Count > 0 ? suspects : null;
    }

    // Body of the given timing's clause, minus the leading timing/cost tags.
    static string TimingBody(CardDef def, string timing)
    {
        if (timing == "Trigger") return def.Trigger ?? "";
        string tag = timing == "OnPlay" ? "[On Play]"
                   : timing == "ActivateMain" ? "[Activate: Main]"
                   : timing == "WhenAttacking" ? "[When Attacking]"
                   : timing == "OnKo" ? (Has(def.Effect, "[On K.O.]") ? "[On K.O.]" : "[On KO]")
                   : timing == "OnBlock" ? "[On Block]"
                   : timing == "OnOpponentAttack" ? "[On Your Opponent's Attack]"
                   : timing == "EndOfTurn" ? "[End of Your Turn]"
                   : timing == "PlayCounter" ? "[Counter]"
                   : timing == "PlayEvent" ? "[Main]" : null;
        var line = (def.Effect ?? "").Split('\n').FirstOrDefault(l => tag != null && Has(l, tag));
        if (line == null) return "";
        // Drop leading bracket tags and any "DON!! -N (...) :" / "①:" cost prefix.
        int colon = line.IndexOf(':');
        // Only treat a colon as a cost/timing separator when it's early (a real prefix), not the
        // rare mid-sentence colon.
        if (colon >= 0 && colon < 60 && (line.Contains("DON!!") || line.Contains("):") || line.IndexOf(']') > colon - 3))
            return line.Substring(colon + 1).Trim();
        // Otherwise strip only the leading [..] tags.
        var t = line.TrimStart();
        while (t.StartsWith("[")) { int e = t.IndexOf(']'); if (e < 0) break; t = t.Substring(e + 1).TrimStart(); }
        return t.Trim();
    }

    static List<string> SplitCompound(string body)
    {
        // Split on the connectives that join independent actions the engine resolves separately.
        var parts = System.Text.RegularExpressions.Regex
            .Split(body, @"\.\s*Then,|,\s+and\s+then\s+|,\s+and\s+|\.\s+Then\s+", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            .Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        return parts;
    }

    // ---- Reporting ----------------------------------------------------------------------------
    static void PrintSummary(List<Result> results)
    {
        int total = results.Count;
        int ok = results.Count(r => r.Verdict == Verdict.OK);
        int na = results.Count(r => r.Verdict == Verdict.NotAutomated);
        int stuck = results.Count(r => r.Verdict == Verdict.Stuck);
        int crash = results.Count(r => r.Verdict == Verdict.Crash);
        int inv = results.Count(r => r.Verdict == Verdict.Invariant);
        int unex = results.Count(r => r.Verdict == Verdict.Unexercised);
        int exercised = total - unex;
        Console.WriteLine("\n===== EFFECT COVERAGE =====");
        Console.WriteLine($"card-timing scenarios: {total}  (exercised {exercised}, unexercised {unex})");
        Console.WriteLine($"  OK={ok}  NOT_AUTOMATED={na}  STUCK={stuck}  CRASH={crash}  INVARIANT={inv}");
        if (exercised > 0)
            Console.WriteLine($"  exercised pass rate: {100.0 * ok / exercised:0.0}%");
        Console.WriteLine("Full report: Tools/Harness/findings/effect-coverage.md");
        if (UnknownConditions.Count > 0)
        {
            Console.WriteLine($"\n----- UNKNOWN CONDITIONS ({UnknownConditions.Count}) — EvaluateCondition can't parse (gated as not-met) -----");
            foreach (var kv in UnknownConditions.OrderBy(k => k.Key))
                Console.WriteLine($"  '{kv.Key}'   e.g. {kv.Value}");
        }
    }

    static void WriteReport(List<Result> results)
    {
        string dir = @"C:\Users\Nperr\One Piece TCG Simulator\Tools\Harness\findings";
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "effect-coverage.md");
        using var w = new StreamWriter(path, false);
        w.WriteLine("# Effect Coverage Report");
        w.WriteLine();
        w.WriteLine($"Generated {DateTime.Now:yyyy-MM-dd HH:mm}. Auto-generated by `Tools/Harness` — NOT shipped in the game.");
        w.WriteLine();

        int total = results.Count, unex = results.Count(r => r.Verdict == Verdict.Unexercised);
        int ok = results.Count(r => r.Verdict == Verdict.OK);
        w.WriteLine("## Summary");
        w.WriteLine();
        w.WriteLine($"- scenarios: **{total}** ({total - unex} exercised, {unex} unexercised)");
        w.WriteLine($"- OK: **{ok}**");
        w.WriteLine($"- NOT_AUTOMATED: **{results.Count(r => r.Verdict == Verdict.NotAutomated)}**");
        w.WriteLine($"- STUCK: **{results.Count(r => r.Verdict == Verdict.Stuck)}**");
        w.WriteLine($"- CRASH: **{results.Count(r => r.Verdict == Verdict.Crash)}**");
        w.WriteLine($"- INVARIANT: **{results.Count(r => r.Verdict == Verdict.Invariant)}**");
        w.WriteLine();

        Section(w, "CRASH", results.Where(r => r.Verdict == Verdict.Crash));
        Section(w, "INVARIANT", results.Where(r => r.Verdict == Verdict.Invariant));
        Section(w, "NOT_AUTOMATED (unimplemented / unrecognized wording — the backlog)", results.Where(r => r.Verdict == Verdict.NotAutomated));
        Section(w, "STUCK (could not auto-resolve — targeting/handler gap)", results.Where(r => r.Verdict == Verdict.Stuck));

        // Compound "dropped clause" suspects (the Law class): OK-resolving effects where a
        // compound sub-clause left no trace in the resolution log. Heuristic — review each.
        var suspects = results.Where(r => r.Suspects != null && r.Suspects.Count > 0)
            .OrderBy(r => r.Id, StringComparer.Ordinal).ToList();
        w.WriteLine($"## COMPOUND_SUSPECT — possible dropped clause (heuristic) ({suspects.Count})");
        w.WriteLine();
        w.WriteLine("_An OK effect whose compound sub-clause left no log trace. May be a genuine dropped");
        w.WriteLine("clause (fix it) or a condition-gated clause that legitimately no-op'd in the test scenario._");
        w.WriteLine();
        if (suspects.Count == 0) { w.WriteLine("_none_"); w.WriteLine(); }
        else
        {
            w.WriteLine("| Card | Name | Timing | Sub-clause with no trace |");
            w.WriteLine("|------|------|--------|--------------------------|");
            foreach (var r in suspects)
                w.WriteLine($"| {r.Id} | {Esc(r.Name)} | {r.Timing} | {Esc(Trunc(string.Join(" ⟂ ", r.Suspects), 160))} |");
            w.WriteLine();
        }

        // Unexercised breakdown by timing label.
        w.WriteLine("## UNEXERCISED (timing not stageable by this harness yet)");
        w.WriteLine();
        foreach (var g in results.Where(r => r.Verdict == Verdict.Unexercised).GroupBy(r => r.Timing).OrderByDescending(g => g.Count()))
            w.WriteLine($"- `{g.Key}` — {g.Count()} cards");
        w.WriteLine();
    }

    static void Section(StreamWriter w, string title, IEnumerable<Result> items)
    {
        var list = items.OrderBy(r => r.Id, StringComparer.Ordinal).ToList();
        w.WriteLine($"## {title} ({list.Count})");
        w.WriteLine();
        if (list.Count == 0) { w.WriteLine("_none_"); w.WriteLine(); return; }
        w.WriteLine("| Card | Name | Timing | Detail |");
        w.WriteLine("|------|------|--------|--------|");
        foreach (var r in list)
            w.WriteLine($"| {r.Id} | {Esc(r.Name)} | {r.Timing} | {Esc(Trunc(r.Detail, 160))} |");
        w.WriteLine();
    }

    static string Esc(string s) => (s ?? "").Replace("|", "\\|").Replace("\n", " ");
    static string Trunc(string s, int n) => (s ?? "").Length <= n ? (s ?? "") : s.Substring(0, n) + "…";

    // ---- LAYER 2: golden snapshots ------------------------------------------------------------
    // A deterministic per-scenario snapshot: the normalized resolution log + a compact zone-count
    // summary. Volatile instance-id tokens are scrubbed so the snapshot only changes when the
    // engine's observable BEHAVIOR changes. Reviewed once; thereafter any diff is a regression.
    static string Snapshot(GameState st, List<string> newLog)
    {
        string log = string.Join(" | ", newLog);
        log = System.Text.RegularExpressions.Regex.Replace(log, @"cov\d+|scd\d+|rd\d+|-DON-\d+", "#");
        var S = st.Players["south"]; var N = st.Players["north"];
        string zones = $"S[h{S.Hand.Count} b{S.CharacterArea.Count(c => c != null)} t{S.Trash.Count} d{S.Deck.Count} L{S.Life.Count} don{S.CostArea.Count}] "
                     + $"N[h{N.Hand.Count} b{N.CharacterArea.Count(c => c != null)} t{N.Trash.Count} d{N.Deck.Count} L{N.Life.Count}]";
        return log + " || " + zones;
    }

    // Run every scenario, build id|timing -> snapshot, and compare to the committed golden file.
    // `write` (or a missing golden) rewrites the baseline; otherwise diffs are reported as
    // regressions and written to a .new file for review.
    public static int Golden(bool write)
    {
        var current = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var cards = CardData.Library.Values
            .Where(c => c != null && (c.Type == "leader" || c.Type == "character" || c.Type == "event" || c.Type == "stage"))
            .OrderBy(c => c.Id, StringComparer.Ordinal).ToList();
        foreach (var def in cards)
            foreach (var t in ApplicableTimings(def))
            {
                var r = RunOne(def, t);
                current[$"{def.Id}|{t}"] = $"{r.Verdict}\t{r.Snapshot}";
            }

        string dir = @"C:\Users\Nperr\One Piece TCG Simulator\Tools\Harness\findings";
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "golden-snapshots.tsv");

        if (write || !File.Exists(path))
        {
            using var w = new StreamWriter(path, false);
            foreach (var kv in current) w.WriteLine($"{kv.Key}\t{kv.Value}");
            Console.WriteLine($"Golden baseline written: {current.Count} scenarios → {path}");
            return 0;
        }

        var baseline = new Dictionary<string, string>();
        foreach (var line in File.ReadAllLines(path))
        {
            int tab = line.IndexOf('\t');
            if (tab < 0) continue;
            baseline[line.Substring(0, tab)] = line.Substring(tab + 1);
        }

        var changed = new List<string>(); var added = new List<string>(); var removed = new List<string>();
        foreach (var kv in current)
        {
            if (!baseline.TryGetValue(kv.Key, out var old)) added.Add(kv.Key);
            else if (old != kv.Value) changed.Add(kv.Key);
        }
        foreach (var key in baseline.Keys) if (!current.ContainsKey(key)) removed.Add(key);

        Console.WriteLine($"\n===== GOLDEN DIFF =====");
        Console.WriteLine($"baseline={baseline.Count}  current={current.Count}  CHANGED={changed.Count}  added={added.Count}  removed={removed.Count}");
        foreach (var k in changed.Take(40))
        {
            Console.WriteLine($"\n  CHANGED {k}");
            Console.WriteLine($"    - {Trunc(baseline[k], 200)}");
            Console.WriteLine($"    + {Trunc(current[k], 200)}");
        }
        if (changed.Count > 40) Console.WriteLine($"\n  … and {changed.Count - 40} more changed.");

        if (changed.Count > 0 || added.Count > 0 || removed.Count > 0)
        {
            string np = Path.Combine(dir, "golden-snapshots.new.tsv");
            using var w = new StreamWriter(np, false);
            foreach (var kv in current) w.WriteLine($"{kv.Key}\t{kv.Value}");
            Console.WriteLine($"\nCurrent snapshots written to {np}. Review diffs, then `golden write` to accept.");
        }
        else Console.WriteLine("No behavioral changes vs golden. ✅");

        return changed.Count > 0 ? 1 : 0;
    }
}
