// Leader-effect operability sweep — and a general "can the player get STUCK?" sweep.
//
// The existing effect-coverage-audit answers "does a handler recognize this body?". Every gap found
// today was invisible to that question: the body IS recognized, it just never accepts a target, or it
// waits forever for a choice that cannot be made. This audit asks the two questions that actually
// match what a player experiences.
//
// 1. STUCK. The pending-effect panel only ENABLES its Skip button when effect.Optional is true
//    (GameManager ~9356). So an effect that is mandatory AND has nothing clickable anywhere on the
//    board is a hard freeze: no target to click, Skip disabled, and "Use Effect" just re-enters the
//    same wait. That is exactly the Gum-Gum Champion Rifle report. Every clause is queued through the
//    REAL command path (so the engine's retire-unresolvable sweep gets its chance) against several
//    deliberately hostile boards.
//
// 2. UNREACHABLE TARGET. A clause that wants a target but which NO CARD IN THE LIBRARY can satisfy is
//    dead — it resolves as far as the resolver is concerned and does nothing forever. This is the
//    OP16-001 Ace bug (a CHOICE of target descriptions was ANDed, so every card failed one half).
//    Reach is measured with the engine's own IsValidEffectTarget, the same predicate that decides
//    what glows, so a "reachable" verdict means genuinely clickable.
//
// Leaders first, because they carry the game's most unusual wording — but nothing here is
// leader-specific, and `leaderaudit all` runs the identical sweep over every card in the game.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using OnePieceTcg.Engine;
using OnePieceTcg.Engine.Bot.Search;

namespace OnePieceTcg.Sim
{
    public static class LeaderEffectAudit
    {
        static readonly HashSet<string> ResolverEventTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "On Play", "On K.O.", "When this Character is K.O.'d", "When Attacking", "On Block",
            "Activate: Main", "Main", "On Your Opponent's Attack", "End of Your Turn", "End of Your Opponent's Turn",
            // [Counter] belongs here for one blunt reason: Gum-Gum Champion Rifle, the freeze report that
            // started all of this, IS a Counter. Leaving the tag out meant the sweep covered every path
            // except the one the original bug came from. 173 clauses.
            "Counter",
        };

        /// <summary>The timing a clause is queued under. A [Counter] clause queued as "main" would not be
        /// the thing the engine actually runs during a block, and the counter path is exactly where the
        /// known freeze lived, so it is queued as itself.</summary>
        static string TimingFor(List<string> tags) =>
            tags.Any(t => t.Equals("Trigger", StringComparison.OrdinalIgnoreCase)) ? "trigger"
            : tags.Any(t => t.Equals("Counter", StringComparison.OrdinalIgnoreCase)) ? "counter"
            : "main";
        static readonly string[] KeywordOnly = { "Rush", "Blocker", "Double Attack", "Banish", "Unblockable" };
        static readonly Regex LeadingTags = new Regex(@"^\s*((?:\[[^\]]+\]\s*/?\s*)+)");
        static readonly Regex TagInner = new Regex(@"\[([^\]]+)\]");

        sealed class Finding
        {
            public string CardId, CardName, Kind, Scenario, Clause, Detail;
        }

        public static int Run(string scope = "leaders", string outPath = null)
        {
            bool allCards = string.Equals(scope, "all", StringComparison.OrdinalIgnoreCase);
            var cards = CardData.Library.Values
                .Where(d => d != null && !string.IsNullOrWhiteSpace(d.Id))
                .Where(d => allCards || string.Equals(d.Type, "leader", StringComparison.OrdinalIgnoreCase))
                .OrderBy(d => d.Id, StringComparer.Ordinal).ToList();

            Console.WriteLine($"leaderaudit: sweeping {cards.Count} {(allCards ? "cards" : "leaders")}…");

            var findings = new List<Finding>();
            int clausesChecked = 0, reachChecked = 0;
            var reachCache = new Dictionary<string, int>();     // clause text -> candidate count
            var sw = System.Diagnostics.Stopwatch.StartNew();

            foreach (var d in cards)
            {
                foreach (var raw in ClausesOf(d))
                {
                    string clause = raw.Trim();
                    if (clause.Length == 0) continue;
                    var tags = TagsOf(clause);
                    if (tags.Count == 0) continue;                     // passive/continuous — a different path
                    bool isTrigger = tags.Any(t => t.Equals("Trigger", StringComparison.OrdinalIgnoreCase));
                    // [Trigger] is the one path where the engine's gate genuinely REFUSES an effect it
                    // cannot handle (the Life card diverts to hand instead), so a miss there is a true
                    // silent no-op rather than something the resolver still catches. 485 cards carry one.
                    if (!isTrigger && !tags.Any(t => ResolverEventTags.Contains(t))) continue;
                    string body = StripLeadingTags(clause);
                    if (IsKeywordOnly(body)) continue;
                    clausesChecked++;

                    foreach (var scenario in Scenarios)
                    {
                        var st = scenario.Build(d.Id);
                        // The SOURCE has to be the audited card itself, in the zone it would really speak
                        // from. Using the Leader for everything made "Return this Character to the owner's
                        // hand" look frozen when the truth was that the audit had no Character to return.
                        var south = st.Players["south"];
                        CardInstance src;
                        if (string.Equals(d.Type, "leader", StringComparison.OrdinalIgnoreCase))
                            src = south.Leader;
                        else if (string.Equals(d.Type, "stage", StringComparison.OrdinalIgnoreCase))
                        {
                            src = Inst(d.Id, "south", "stage");
                            south.Stage = src;
                        }
                        else if (string.Equals(d.Type, "event", StringComparison.OrdinalIgnoreCase))
                        {
                            src = Inst(d.Id, "south", "hand");
                            south.Hand.Add(src);
                        }
                        else
                        {
                            src = Inst(d.Id, "south", "character");
                            int slot = south.CharacterArea.FindIndex(c => c == null);
                            if (slot < 0) slot = south.CharacterArea.Count - 1;
                            south.CharacterArea[slot] = src;
                        }
                        PendingEffect pe;
                        try
                        {
                            GameEngine.QueueClauseForTest(st, "south", src, TimingFor(tags), clause);
                            pe = st.PendingEffects.FirstOrDefault();
                        }
                        catch (Exception ex)
                        {
                            findings.Add(new Finding
                            {
                                CardId = d.Id, CardName = d.Name, Kind = "THREW", Scenario = scenario.Name,
                                Clause = body, Detail = ex.GetType().Name + ": " + ex.Message,
                            });
                            continue;
                        }
                        if (pe == null) continue;                       // resolved or correctly retired

                        // WALK THE WHOLE CHAIN, not just the first step. Gum-Gum Champion Rifle — the
                        // freeze that started all of this — hides its unsatisfiable part in a ". Then, …"
                        // rider, which does not exist until the first part has resolved. Checking only the
                        // first pending effect meant the sweep could not see the very bug it was built for
                        // (verified by disabling the engine's retire guard: 14 freezes reappeared, and
                        // EB01-028 was not among them). Each step is advanced the way a player would: click
                        // a legal target if one is offered, otherwise press "Use Effect".
                        var seenEffects = new HashSet<string>();
                        for (int step = 0; step < 8 && pe != null; step++)
                        {
                            if (!seenEffects.Add(pe.EffectId + ":" + pe.SelectionsRemaining)) break;
                            if (ReportIfStuck(st, pe, d, body, scenario.Name, findings)) break;
                            if (!AdvanceOneStep(st, pe)) break;
                            pe = st.PendingEffects.FirstOrDefault();
                        }
                        // The clickable / reach questions must be asked of a FRESH board. Walking the
                        // chain consumes targets — after picking three opponent cards, "up to a total of
                        // 3 of your opponent's rested cards" legitimately has nothing left to click, and
                        // reporting that as a dead clause is nonsense (it flagged OP04-031 Doflamingo and
                        // OP07-091 Luffy, both of which were clickable at every step). The walk answers
                        // "can the player get stuck"; these two answer "was there ever anything to click",
                        // so they get the board as the player would first meet it.
                        st = scenario.Build(d.Id);
                        south = st.Players["south"];
                        if (string.Equals(d.Type, "leader", StringComparison.OrdinalIgnoreCase)) src = south.Leader;
                        else if (string.Equals(d.Type, "stage", StringComparison.OrdinalIgnoreCase)) { src = Inst(d.Id, "south", "stage"); south.Stage = src; }
                        else if (string.Equals(d.Type, "event", StringComparison.OrdinalIgnoreCase)) { src = Inst(d.Id, "south", "hand"); south.Hand.Add(src); }
                        else
                        {
                            src = Inst(d.Id, "south", "character");
                            int slot2 = south.CharacterArea.FindIndex(c => c == null);
                            south.CharacterArea[slot2 < 0 ? south.CharacterArea.Count - 1 : slot2] = src;
                        }
                        try { GameEngine.QueueClauseForTest(st, "south", src, TimingFor(tags), clause); }
                        catch { continue; }
                        pe = st.PendingEffects.FirstOrDefault();
                        if (pe == null) continue;

                        bool clickable = AnyValidTarget(st, pe);
                        // GLOW MISMATCH. "Is anything clickable at all" is too weak a question: it passes
                        // as soon as SOMETHING lights, even when the thing that lights is the wrong card.
                        // OP15-002 Lucy is exactly that — her "trash any number of Event or Stage cards
                        // from your hand" lit board cards (the clause also says "gains +1000 power") while
                        // the hand cards that actually pay lit nothing, and with the fix reverted this
                        // sweep still reported a clean bill because clickable was true. So compare the
                        // glow against the resolver PER CARD, in both directions: a card the resolver
                        // accepts must light, and a card that lights must be accepted.
                        // Only ask the glow question of steps that actually ask for a CARD. The noise this
                        // removes is not marginal: DON!!-cost reminders ("DON!! −2 (You may return …)"),
                        // delegating clauses ("Activate this card's [Main] effect", "Play this card") and
                        // self-paid costs accounted for ~1,600 of the first run's findings, none of which
                        // involve clicking a card at all.
                        bool delegating = Regex.IsMatch(body,
                            @"^\s*(?:Activate this card's \[[^\]]+\] effect|Play this card)\.?\s*$",
                            RegexOptions.IgnoreCase);
                        // A clause gated on a condition the fixture does not meet ("If your Leader has the
                        // {Water Seven} type…") legitimately does nothing when clicked, so glow-vs-resolver
                        // disagreement there says nothing about the card — it says the board was wrong.
                        // 611 findings were this one shape.
                        bool condFails = false;
                        {
                            var condM = Regex.Match(StripCostPrefix(body), @"^If ([^,]{3,80}),",
                                RegexOptions.IgnoreCase);
                            if (condM.Success)
                            {
                                try { condFails = !GameEngine.AuditConditionValue(st, "south", condM.Groups[1].Value.Trim()); }
                                catch { condFails = false; }
                            }
                        }
                        if ((scenario.Name == "full-board" || scenario.Name == "in-battle")
                            && WantsACardTarget(pe) && !delegating && !condFails)
                        {
                            foreach (var mm in GlowMismatches(st, pe))
                                findings.Add(new Finding
                                {
                                    CardId = d.Id, CardName = d.Name, Kind = "GLOW-MISMATCH",
                                    Scenario = scenario.Name, Clause = body, Detail = mm,
                                });
                        }
                        // With nothing clickable the panel still shows an enabled "Use Effect" button, so
                        // this is only a freeze if pressing that ALSO fails to move the game on. Without
                        // this second half the sweep flagged every "rest ALL of your opponent's Characters"
                        // against an empty board — nothing to click, but Use Effect resolves it fine.
                        // Nothing glows. That is only a DEFECT if the player was supposed to click
                        // something. Three cases, and only two of them are bugs:
                        //
                        //  * the resolver would accept a card sitting right there — glow and resolver
                        //    disagree, so the card is on the board but unclickable. Always a bug; this is
                        //    what the ST13-001 Sabo cost turned out to be.
                        //  * nothing on this board works and nothing in the WHOLE LIBRARY would either —
                        //    the description describes no card that exists (the OP16-001 Ace bug).
                        //  * neither — the step is driven by the panel's "Use Effect" button (rest DON!!,
                        //    turn a Life card face-up, reveal from hand). Normal, not a finding.
                        if ((scenario.Name == "full-board" || scenario.Name == "in-battle") && !clickable)
                        {
                            string accepted = ResolverAcceptsSomeBoardCard(st, pe);
                            if (accepted != null)
                            {
                                findings.Add(new Finding
                                {
                                    CardId = d.Id, CardName = d.Name, Kind = "UNCLICKABLE", Scenario = scenario.Name,
                                    Clause = body,
                                    Detail = "the resolver accepts " + accepted + " but the glow lights nothing",
                                });
                            }
                            else
                            {
                                if (!reachCache.TryGetValue(clause, out int reach))
                                {
                                    reach = CountLibraryCandidates(st, pe);
                                    reachCache[clause] = reach;
                                    reachChecked++;
                                }
                                // A POWER filter is measured against printed stats here, but power moves
                                // during a turn — OP09-007 Heat buffs "your Leader with 4000 power or
                                // less", which no Leader is when printed and any Leader can be after a
                                // −power effect. A static library scan cannot answer that, so it must not
                                // claim the clause is dead.
                                bool powerConditioned = Regex.IsMatch(pe.Text ?? "",
                                    @"\d{3,5} power or (?:more|less)", RegexOptions.IgnoreCase);
                                if (reach == 0 && !powerConditioned && WantsACardTarget(pe))
                                    findings.Add(new Finding
                                    {
                                        CardId = d.Id, CardName = d.Name, Kind = "UNREACHABLE", Scenario = "library-wide",
                                        Clause = body,
                                        Detail = "wants a card target, but NO card in the library satisfies the description",
                                    });
                            }
                        }
                    }
                }
            }
            sw.Stop();

            outPath ??= Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "docs",
                allCards ? "stuck-and-reach-audit-all.md" : "leader-effect-audit.md"));
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            File.WriteAllText(outPath, Report(cards.Count, clausesChecked, reachChecked, findings, allCards));

            int stuck = findings.Count(f => f.Kind == "STUCK");
            int dead = findings.Count(f => f.Kind == "UNREACHABLE");
            int unclickable = findings.Count(f => f.Kind == "UNCLICKABLE");
            int threw = findings.Count(f => f.Kind == "THREW");
            Console.WriteLine($"  clauses checked: {clausesChecked} across {cards.Count} cards ({sw.Elapsed.TotalSeconds:F1}s)");
            Console.WriteLine($"  STUCK (mandatory + nothing clickable + Skip disabled): {stuck} across {findings.Where(f => f.Kind == "STUCK").Select(f => f.CardId).Distinct().Count()} cards");
            Console.WriteLine($"  UNREACHABLE (no card in the library can satisfy it): {dead} across {findings.Where(f => f.Kind == "UNREACHABLE").Select(f => f.CardId).Distinct().Count()} cards");
            Console.WriteLine($"  UNCLICKABLE (resolver accepts a board card the glow never lights): {unclickable}");
            int glowMm = findings.Count(f => f.Kind == "GLOW-MISMATCH");
            Console.WriteLine($"  GLOW-MISMATCH (glow and resolver disagree on a card): {glowMm} across "
                + $"{findings.Where(f => f.Kind == "GLOW-MISMATCH").Select(f => f.CardId).Distinct().Count()} cards");
            Console.WriteLine($"  THREW: {threw}");
            Console.WriteLine($"  report: {outPath}");
            return stuck + dead + unclickable + threw == 0 ? 0 : 1;
        }

        // Press "Use Effect" (resolveEffect with no target) on a clone and see whether the effect is still
        // sitting there afterwards. If it is, and nothing is clickable, and Skip is disabled because the
        // effect is mandatory, then every control the panel offers is a no-op — the player cannot proceed.
        static bool UseEffectLeavesItPending(GameState st, PendingEffect pe)
        {
            var clone = GameClone.Clone(st);
            try
            {
                GameEngine.ApplyCommand(clone, new GameCommand
                {
                    Type = "resolveEffect", Seat = pe.Seat, EffectId = pe.EffectId,
                });
            }
            catch { return false; }          // a throw is a different defect; THREW covers it
            return clone.PendingEffects.Any(e => e.EffectId == pe.EffectId);
        }

        /// <summary>`leaderaudit why &lt;cardId&gt;` — dump what the engine actually thinks about one card:
        /// the queued clause, its inferred target zone, and every board card with the verdict the glow
        /// gives it. Guessing at why a target is refused wastes more time than printing it.</summary>
        public static int Why(string cardId)
        {
            var def = CardData.GetCard(cardId);
            if (def == null) { Console.WriteLine("unknown card " + cardId); return 1; }
            Console.WriteLine($"== {def.Id} {def.Name} [{def.Type}] {string.Join("/", def.Features ?? new List<string>())}");
            foreach (var raw in ClausesOf(def))
            {
                string clause = raw.Trim();
                var tags = TagsOf(clause);
                if (tags.Count == 0) continue;
                if (!tags.Any(t => t.Equals("Trigger", StringComparison.OrdinalIgnoreCase))
                    && !tags.Any(t => ResolverEventTags.Contains(t))) continue;

                var st = Rich(Board(def.Id, 3, 3, false, 3, 3, 3));
                var south = st.Players["south"];
                CardInstance src;
                if (string.Equals(def.Type, "leader", StringComparison.OrdinalIgnoreCase)) src = south.Leader;
                else if (string.Equals(def.Type, "stage", StringComparison.OrdinalIgnoreCase)) { src = Inst(def.Id, "south", "stage"); south.Stage = src; }
                else if (string.Equals(def.Type, "event", StringComparison.OrdinalIgnoreCase)) { src = Inst(def.Id, "south", "hand"); south.Hand.Add(src); }
                else { src = Inst(def.Id, "south", "character"); int sl = south.CharacterArea.FindIndex(c => c == null); south.CharacterArea[sl < 0 ? 0 : sl] = src; }

                GameEngine.QueueClauseForTest(st, "south", src, TimingFor(tags), clause);
                var pe = st.PendingEffects.FirstOrDefault();
                Console.WriteLine($"\n  clause: {clause}");
                if (pe == null) { Console.WriteLine("    -> resolved / retired, nothing pending"); continue; }
                Console.WriteLine($"    pending text : {pe.Text}");
                Console.WriteLine($"    targetZone   : {pe.TargetZone}   optional={pe.Optional}  selections={pe.SelectionsRemaining}");
                // Walk the chain so a rider's step is visible too, printing the log each time.
                for (int step = 0; step < 6; step++)
                {
                    var cur = st.PendingEffects.FirstOrDefault();
                    if (cur == null) { Console.WriteLine($"    step{step}: nothing pending"); break; }
                    Console.WriteLine($"    step{step}: optional={cur.Optional} clickable={AnyValidTarget(st, cur)} :: {cur.Text}");
                    if (!AdvanceOneStep(st, cur)) { Console.WriteLine($"    step{step}: NO PROGRESS"); break; }
                }
                Console.WriteLine("    log: " + string.Join(" | ",
                    st.EventLog.Skip(Math.Max(0, st.EventLog.Count - 6)).Select(e => e.Message)));
                foreach (var kv in st.Players)
                {
                    var pl = kv.Value;
                    var all = new List<CardInstance>();
                    if (pl.Leader != null) all.Add(pl.Leader);
                    if (pl.CharacterArea != null) all.AddRange(pl.CharacterArea.Where(c => c != null));
                    foreach (var l in new[] { pl.Hand, pl.Trash, pl.Life })
                        if (l != null) all.AddRange(l.Where(c => c != null).Take(1));
                    foreach (var c in all)
                        Console.WriteLine($"      {(kv.Key == "south" ? "own " : "opp ")}{c.Zone,-10} {c.CardId,-12} rested={c.Rested,-5} glow={GameEngine.IsValidEffectTarget(st, pe, c)}");
                }
            }
            return 0;
        }

        /// <summary>Record a freeze if this step is one. Mandatory + nothing clickable + "Use Effect"
        /// making no progress means every control the pending panel offers is a no-op.</summary>
        static bool ReportIfStuck(GameState st, PendingEffect pe, CardDef d, string body,
                                  string scenario, List<Finding> findings)
        {
            if (pe.Optional) return false;
            if (AnyValidTarget(st, pe)) return false;
            if (!UseEffectLeavesItPending(st, pe)) return false;
            findings.Add(new Finding
            {
                CardId = d.Id, CardName = d.Name, Kind = "STUCK", Scenario = scenario,
                Clause = body,
                Detail = "mandatory, nothing clickable anywhere, Skip disabled → the panel cannot be dismissed"
                       + " (clause: " + Esc(pe.Text ?? "") + ")",
            });
            return true;
        }

        /// <summary>Move the effect on the way a player would, and report whether anything changed.</summary>
        static bool AdvanceOneStep(GameState st, PendingEffect pe)
        {
            int beforeCount = st.PendingEffects.Count;
            string beforeId = pe.EffectId;
            int beforeSel = pe.SelectionsRemaining;

            CardInstance pick = null;
            foreach (var kv in st.Players)
            {
                var p = kv.Value;
                if (p == null) continue;
                if (p.Leader != null && GameEngine.IsValidEffectTarget(st, pe, p.Leader)) { pick = p.Leader; break; }
                if (p.CharacterArea != null)
                    foreach (var c in p.CharacterArea)
                        if (c != null && GameEngine.IsValidEffectTarget(st, pe, c)) { pick = c; break; }
                if (pick != null) break;
                foreach (var list in new[] { p.Hand, p.Trash, p.Life })
                    if (list != null)
                        foreach (var c in list)
                            if (c != null && GameEngine.IsValidEffectTarget(st, pe, c)) { pick = c; break; }
                if (pick != null) break;
            }
            try
            {
                GameEngine.ApplyCommand(st, new GameCommand
                {
                    Type = "resolveEffect", Seat = pe.Seat, EffectId = pe.EffectId,
                    Target = pick?.InstanceId,
                });
            }
            catch { return false; }

            var after = st.PendingEffects.FirstOrDefault();
            return st.PendingEffects.Count != beforeCount
                || after == null || after.EffectId != beforeId || after.SelectionsRemaining != beforeSel;
        }

        /// <summary>`leaderaudit conditions` — which printed conditions ACTUALLY reach EvaluateCondition
        /// and fail closed there?
        ///
        /// condition-audit reads printed text, so it lists every condition whose wording the parser does
        /// not know. Most of those never reach the parser at all: replacement triggers, "if you do"
        /// sequencing and reveal comparisons are resolved inline where they are printed. Claiming those
        /// are harmless from the shape of the words is a guess. This runs every clause and watches for the
        /// engine's own "Unknown condition" line, which it emits only when a condition genuinely arrived
        /// at EvaluateCondition and was treated as not met — the failure that silently disables a card.</summary>
        public static int Conditions()
        {
            var hits = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
            int clauses = 0;

            foreach (var d in CardData.Library.Values
                         .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Id))
                         .OrderBy(x => x.Id, StringComparer.Ordinal))
            {
                foreach (var raw in ClausesOf(d))
                {
                    string clause = raw.Trim();
                    if (clause.Length == 0) continue;
                    var tags = TagsOf(clause);
                    if (tags.Count == 0) continue;
                    bool isTrigger = tags.Any(t => t.Equals("Trigger", StringComparison.OrdinalIgnoreCase));
                    if (!isTrigger && !tags.Any(t => ResolverEventTags.Contains(t))) continue;
                    clauses++;

                    var st = Rich(Board(d.Id, 3, 3, false, 3, 3, 3));
                    var south = st.Players["south"];
                    CardInstance src;
                    if (string.Equals(d.Type, "leader", StringComparison.OrdinalIgnoreCase)) src = south.Leader;
                    else if (string.Equals(d.Type, "stage", StringComparison.OrdinalIgnoreCase)) { src = Inst(d.Id, "south", "stage"); south.Stage = src; }
                    else if (string.Equals(d.Type, "event", StringComparison.OrdinalIgnoreCase)) { src = Inst(d.Id, "south", "hand"); south.Hand.Add(src); }
                    else
                    {
                        src = Inst(d.Id, "south", "character");
                        int slot = south.CharacterArea.FindIndex(c => c == null);
                        south.CharacterArea[slot < 0 ? south.CharacterArea.Count - 1 : slot] = src;
                    }

                    int logFrom = st.EventLog.Count;
                    try
                    {
                        GameEngine.QueueClauseForTest(st, "south", src, TimingFor(tags), clause);
                        var pe = st.PendingEffects.FirstOrDefault();
                        for (int step = 0; step < 6 && pe != null; step++)
                        {
                            if (!AdvanceOneStep(st, pe)) break;
                            pe = st.PendingEffects.FirstOrDefault();
                        }
                    }
                    catch { }

                    foreach (var entry in st.EventLog.Skip(logFrom))
                    {
                        var m = Regex.Match(entry.Message ?? "", @"Unknown condition '(?<c>.*)' — treating as not met\.");
                        if (!m.Success) continue;
                        string cond = m.Groups["c"].Value;
                        if (!hits.TryGetValue(cond, out var set)) hits[cond] = set = new SortedSet<string>(StringComparer.Ordinal);
                        set.Add(d.Id);
                    }
                }
            }

            Console.WriteLine($"condition reach: {clauses} clauses executed");
            Console.WriteLine($"  conditions that REALLY reached EvaluateCondition and failed closed: {hits.Count}");
            foreach (var kv in hits.OrderByDescending(k => k.Value.Count))
                Console.WriteLine($"    {kv.Value.Count,3} cards  \"{kv.Key}\"  e.g. {string.Join(", ", kv.Value.Take(4))}");
            return hits.Count == 0 ? 0 : 1;
        }

        // ---- the player's own "is anything clickable?" test ----------------------------------------
        // Mirrors GameManager.EffectHasValidTarget exactly: if this is false and the effect is not
        // Optional, the pending panel has no enabled control at all.
        static bool AnyValidTarget(GameState st, PendingEffect pe)
        {
            foreach (var kv in st.Players)
            {
                var p = kv.Value;
                if (p == null) continue;
                if (p.Leader != null && GameEngine.IsValidEffectTarget(st, pe, p.Leader)) return true;
                if (p.CharacterArea != null)
                    foreach (var c in p.CharacterArea)
                        if (c != null && GameEngine.IsValidEffectTarget(st, pe, c)) return true;
                foreach (var list in new[] { p.Hand, p.Trash, p.Life })
                    if (list != null)
                        foreach (var c in list)
                            if (c != null && GameEngine.IsValidEffectTarget(st, pe, c)) return true;
            }
            return false;
        }

        // Would the RESOLVER accept some card already on the board? Each candidate is tried on a CLONE
        // (ApplyCommand mutates in place), and "accepted" means the click actually moved the game: a pick
        // consumed, the effect finished, or cards changed zones. If such a card exists while the glow
        // lights nothing, the player can SEE the card but cannot click it.
        /// <summary>Per-card glow-vs-resolver disagreement, in both directions. Reuses the same
        /// control-run subtraction as ResolverAcceptsSomeBoardCard: pressing "Use Effect" with no target
        /// changes some cards for reasons that have nothing to do with what was clicked (a self-rest, an
        /// auto-paying cost), and those must not read as acceptance.</summary>
        static List<string> GlowMismatches(GameState st, PendingEffect pe)
        {
            var found = new List<string>();
            var control = ControlRunChanges(st, pe);

            foreach (var kv in st.Players)
            {
                var p = kv.Value;
                if (p == null) continue;
                var candidates = new List<CardInstance>();
                if (p.Leader != null) candidates.Add(p.Leader);
                if (p.CharacterArea != null) candidates.AddRange(p.CharacterArea.Where(c => c != null));
                foreach (var list in new[] { p.Hand, p.Trash, p.Life })
                    if (list != null) candidates.AddRange(list.Where(c => c != null));

                foreach (var cand in candidates)
                {
                    bool glows = GameEngine.IsValidEffectTarget(st, pe, cand);
                    var clone = GameClone.Clone(st);
                    var clonePe = clone.PendingEffects.FirstOrDefault(e => e.EffectId == pe.EffectId);
                    if (clonePe == null) continue;
                    string before = CardFingerprint(clone, cand.InstanceId);
                    try
                    {
                        GameEngine.ApplyCommand(clone, new GameCommand
                        {
                            Type = "resolveEffect", Seat = pe.Seat, EffectId = pe.EffectId, Target = cand.InstanceId,
                        });
                    }
                    catch { continue; }
                    bool accepted = CardFingerprint(clone, cand.InstanceId) != before
                                    && !control.Contains(cand.InstanceId);
                    if (glows == accepted) continue;
                    string where = (kv.Key == pe.Seat ? "own " : "opponent ") + cand.Zone;
                    found.Add(glows
                        ? $"{cand.CardId} ({where}) LIGHTS UP but the resolver does nothing with it"
                        : $"{cand.CardId} ({where}) is accepted by the resolver but never lights up");
                    if (found.Count >= 4) return found;   // enough to identify the clause
                }
            }
            return found;
        }

        /// <summary>Cards changed by pressing "Use Effect" with no target — noise for acceptance tests.</summary>
        static HashSet<string> ControlRunChanges(GameState st, PendingEffect pe)
        {
            var changed = new HashSet<string>();
            var cst = GameClone.Clone(st);
            var before = new Dictionary<string, string>();
            foreach (var kv in cst.Players)
            {
                var p0 = kv.Value;
                if (p0 == null) continue;
                var pre = new List<CardInstance>();
                if (p0.Leader != null) pre.Add(p0.Leader);
                if (p0.CharacterArea != null) pre.AddRange(p0.CharacterArea.Where(c => c != null));
                foreach (var l in new[] { p0.Hand, p0.Trash, p0.Life })
                    if (l != null) pre.AddRange(l.Where(c => c != null));
                foreach (var c in pre) before[c.InstanceId] = CardFingerprint(cst, c.InstanceId);
            }
            try
            {
                GameEngine.ApplyCommand(cst, new GameCommand
                { Type = "resolveEffect", Seat = pe.Seat, EffectId = pe.EffectId });
            }
            catch { }
            foreach (var kv in before)
                if (CardFingerprint(cst, kv.Key) != kv.Value) changed.Add(kv.Key);
            return changed;
        }

        static string ResolverAcceptsSomeBoardCard(GameState st, PendingEffect pe)
        {
            // CONTROL RUN. Press "Use Effect" (no target) once and record every card it changes. Those
            // cards change for reasons that have nothing to do with what was clicked — an auto-payable
            // cost paying itself, a self-rest, a self-trash. Without subtracting them, the source card
            // sitting on the board counts as "accepted" for every clause that rests or trashes itself,
            // which is what produced 137 findings that were all the same artifact.
            var control = new Dictionary<string, string>();
            {
                var cst = GameClone.Clone(st);
                foreach (var kv in cst.Players)
                {
                    var p0 = kv.Value;
                    if (p0 == null) continue;
                    var pre = new List<CardInstance>();
                    if (p0.CharacterArea != null) pre.AddRange(p0.CharacterArea.Where(c => c != null));
                    foreach (var l in new[] { p0.Hand, p0.Trash, p0.Life })
                        if (l != null) pre.AddRange(l.Where(c => c != null));
                    foreach (var c in pre) control[c.InstanceId] = CardFingerprint(cst, c.InstanceId);
                }
                try
                {
                    GameEngine.ApplyCommand(cst, new GameCommand
                    {
                        Type = "resolveEffect", Seat = pe.Seat, EffectId = pe.EffectId,
                    });
                }
                catch { }
                foreach (var id in control.Keys.ToList())
                    if (CardFingerprint(cst, id) == control[id]) control.Remove(id);   // unchanged = not noise
            }

            foreach (var kv in st.Players)
            {
                var p = kv.Value;
                if (p == null) continue;
                var candidates = new List<CardInstance>();
                if (p.CharacterArea != null) candidates.AddRange(p.CharacterArea.Where(c => c != null));
                foreach (var list in new[] { p.Hand, p.Trash, p.Life })
                    if (list != null) candidates.AddRange(list.Where(c => c != null));

                foreach (var cand in candidates)
                {
                    var clone = GameClone.Clone(st);
                    var clonePe = clone.PendingEffects.FirstOrDefault(e => e.EffectId == pe.EffectId);
                    if (clonePe == null) continue;
                    string before = CardFingerprint(clone, cand.InstanceId);
                    try
                    {
                        GameEngine.ApplyCommand(clone, new GameCommand
                        {
                            Type = "resolveEffect", Seat = pe.Seat, EffectId = pe.EffectId, Target = cand.InstanceId,
                        });
                    }
                    catch { continue; }
                    // Acceptance has to be ATTRIBUTABLE TO THIS CARD. Comparing whole-state before/after
                    // reported every cost-stage effect as a hit, because clicking any card at all makes an
                    // auto-payable cost pay itself exactly as the "Use Effect" button would — the click was
                    // not accepted as a TARGET, the effect just moved on regardless of what was clicked.
                    // NOT PickedInstanceIds: ResolveEffect records the attempted pick centrally BEFORE the
                    // handler validates it, so a rejected click lands in that list too. The card itself
                    // having changed — moved zone, rested, gained a modifier or an attached DON!!, or left
                    // play entirely — is the only honest evidence that the click was accepted.
                    string after = CardFingerprint(clone, cand.InstanceId);
                    if (after != before && !control.ContainsKey(cand.InstanceId))
                        return cand.CardId + " (" + (kv.Key == pe.Seat ? "own " : "opponent ") + cand.Zone
                             + ") [" + before + " -> " + after + "]";
                }
            }
            return null;
        }

        /// <summary>Everything that would visibly change about ONE card if an effect took it as its
        /// target: where it is, whether it is rested, what is attached to it, what modifiers it carries.
        /// "gone" covers a K.O./trash that removes it from every zone.</summary>
        static string CardFingerprint(GameState st, string instanceId)
        {
            foreach (var kv in st.Players)
            {
                var p = kv.Value;
                if (p == null) continue;
                var all = new List<CardInstance>();
                if (p.CharacterArea != null) all.AddRange(p.CharacterArea.Where(c => c != null));
                foreach (var list in new[] { p.Hand, p.Trash, p.Life, p.Deck })
                    if (list != null) all.AddRange(list.Where(c => c != null));
                if (p.Leader != null) all.Add(p.Leader);
                if (p.Stage != null) all.Add(p.Stage);
                var c2 = all.FirstOrDefault(c => c.InstanceId == instanceId);
                if (c2 != null)
                    // The card object is only half of a card's state. Power changes — by far the most
                    // common thing an effect does to a target — live in STATE dictionaries keyed by
                    // instance id, not on the CardInstance. A fingerprint that ignores them says
                    // "nothing happened" for every buff and debuff in the game, which is what made the
                    // glow-mismatch sweep report thousands of targets as accepted-but-inert.
                    return string.Join("/", kv.Key, c2.Zone, c2.Rested,
                        c2.AttachedDonIds == null ? 0 : c2.AttachedDonIds.Count,
                        c2.Modifiers == null ? 0 : c2.Modifiers.Count,
                        st.TemporaryPowerBonus != null && st.TemporaryPowerBonus.TryGetValue(instanceId, out var tp) ? tp : 0,
                        st.Battle != null && st.Battle.BattlePowerBonus != null
                            && st.Battle.BattlePowerBonus.TryGetValue(instanceId, out var bp) ? bp : 0,
                        st.TimedPowerBonuses == null ? 0 : st.TimedPowerBonuses.Count(t => t.TargetInstanceId == instanceId),
                        st.BasePowerOverrides == null ? 0 : st.BasePowerOverrides.Count(o => o.TargetInstanceId == instanceId),
                        st.NameOverrides != null && st.NameOverrides.ContainsKey(instanceId) ? 1 : 0,
                        GameEngine.GetPower(st, c2));
            }
            return "gone";
        }

        /// <summary>Leading timing tags and any DON!!/circled cost prefix removed, so a leading
        /// "If &lt;condition&gt;," is actually at the front where it can be read.</summary>
        static string StripCostPrefix(string text)
        {
            string t = Regex.Replace(text ?? "", @"^\s*(\[[^\]]+\]\s*/?\s*)+", "");
            t = Regex.Replace(t, @"^\s*(?:[➀-➉①-⑩]|DON!!\s*[-−–‑‒—]\s*\d+)\s*(?:\([^)]*\))?\s*[:：]?\s*", "");
            return t.Trim();
        }

        // Is the step even asking for a CARD? A cost paid by resting DON!!, turning a Life card face-up
        // or trashing off the top of the deck is driven by the panel's own button, so "nothing glows" is
        // correct there and must not be reported as a dead description.
        static bool WantsACardTarget(PendingEffect pe)
        {
            // Strip the same prefixes the engine strips before it looks for a cost — leading timing tags
            // AND the DON!!-cost prefix. Without the second one this read "DON!! -1 (...) You may rest this
            // Leader: ..." as "not a cost step at all" and reported a perfectly good card as dead.
            // "Give up to N rested DON!! card(s) to 1 of your {type} Characters" — what the player clicks
            // is a rested DON!!, which the panel handles on its own path (GameManager's DonGivePickActive)
            // and which is not a CARD at all, so a card-based reach check can never see it.
            // ...unless the clause offers a RECIPIENT you choose ("to 1 of your {Sky Island} type Leader
            // or Character cards"), which is a board click after all. Singular "Character" on purpose —
            // the plural test is exactly what hid those five cards from the glow in the first place.
            if (Regex.IsMatch(pe.Text ?? "", @"give (?:up to )?[\d\w]+ rested DON!!", RegexOptions.IgnoreCase)
                && (pe.Text ?? "").IndexOf("Character", StringComparison.OrdinalIgnoreCase) < 0)
                return false;
            string t = Regex.Replace(pe.Text ?? "", @"^\s*(\[[^\]]+\]\s*/?\s*)+", "");
            t = Regex.Replace(t, @"^\s*(?:[➀-➉①-⑩]|DON!!\s*[-−–‑‒—]\s*\d+)\s*(?:\([^)]*\))?\s*[:：]?\s*", "");
            var costGate = Regex.Match(t, @"^\s*(?:\[[^\]]+\]\s*/?\s*)*You (?:may|can) (?<cost>[^:]+):",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!costGate.Success) return true;              // not a cost step — the body wants the target
            string cost = costGate.Groups["cost"].Value;
            // A SELF cost ("trash this Character", "rest this Leader") names no card the player picks — the
            // source pays it and the panel's button drives the whole thing. Stripping those phrases before
            // looking for a card noun is what separates "click one of your Characters" from "this
            // Character". Without it, 133 perfectly good cards were reported dead purely because the word
            // "Character" appeared inside a self-sacrifice.
            cost = Regex.Replace(cost, @"\bthis (?:Character|card|Leader|Stage)\b", "", RegexOptions.IgnoreCase);
            return Regex.IsMatch(cost, @"\b(?:K\.O\.|trash|rest|return|place|add|reveal)\b[^:]*\b(?:Characters?|cards?)\b",
                       RegexOptions.IgnoreCase)
                && !Regex.IsMatch(cost, @"DON!!|top of your deck|top of your Life|Life cards face-up|from your Life area",
                       RegexOptions.IgnoreCase)
                // "return N cards from your trash to the bottom of your deck" is paid automatically — the
                // engine takes the last N, the player picks nothing — so nothing glowing is correct.
                // "AT the bottom of your deck" as well as "TO" — OP05-082 Shirahoshi and OP05-088
                // Mansherry both say "place 2 cards from your trash AT the bottom of your deck in any
                // order", which is the same auto-paid cost and was being reported as a card the player
                // could not click.
                && !(Regex.IsMatch(cost, @"from your trash", RegexOptions.IgnoreCase)
                     && Regex.IsMatch(cost, @"(?:to|at) (?:the bottom of )?your deck", RegexOptions.IgnoreCase));
        }

        // ---- library-wide reach --------------------------------------------------------------------
        // Drop one instance of every printed card into the zone the effect targets and ask the engine
        // whether it would be a legal click. Zero hits across the whole library means the description
        // describes no card that exists.
        static int CountLibraryCandidates(GameState st, PendingEffect pe)
        {
            var south = st.Players["south"];
            var north = st.Players["north"];
            int hits = 0;
            int serial = 0;
            foreach (var def in CardData.Library.Values)
            {
                if (def == null || string.IsNullOrWhiteSpace(def.Id)) continue;
                // Try the card on BOTH sides and in EVERY zone rather than guessing one from the effect's
                // TargetZone. Guessing produced a wave of false "dead" verdicts: while an optional cost is
                // being paid the effect's TargetZone still describes the BODY, so a hand-trash cost was
                // being probed with cards placed on the board, which of course matched nothing.
                bool found = false;
                foreach (var owner in new[] { "south", "north" })
                {
                    var p = owner == "south" ? south : north;
                    bool isLeaderDef = string.Equals(def.Type, "leader", StringComparison.OrdinalIgnoreCase);
                    foreach (var zone in new[] { "character", "hand", "trash", "life", "leader" })
                    {
                        if (zone == "character" && isLeaderDef) continue;
                        if (zone == "leader" && !isLeaderDef) continue;
                        var inst = new CardInstance
                        {
                            InstanceId = $"reach-{serial++}", CardId = def.Id, Owner = owner, Zone = zone,
                        };
                        var list = zone == "hand" ? p.Hand : zone == "trash" ? p.Trash : zone == "life" ? p.Life : null;
                        if (list != null) list.Add(inst);
                        // A Leader is only ever reachable while it IS the seat's Leader — putting one in a
                        // list reaches nothing, so the real slot is swapped for the probe and restored after.
                        // Skipping this made every "your [Name] Leader gains …" clause look dead.
                        var savedLeader = p.Leader;
                        if (zone == "leader") p.Leader = inst;
                        bool ok = GameEngine.IsValidEffectTarget(st, pe, inst);
                        if (zone == "leader") p.Leader = savedLeader;
                        if (list != null) list.RemoveAt(list.Count - 1);
                        if (ok) { found = true; break; }
                    }
                    if (found) break;
                }
                if (found) hits++;
            }
            return hits;
        }

        static string ZoneFor(EffectTargetZone z, CardDef def)
        {
            switch (z)
            {
                case EffectTargetZone.Hand: return "hand";
                case EffectTargetZone.Trash: return "trash";
                default:
                    return string.Equals(def.Type, "leader", StringComparison.OrdinalIgnoreCase) ? "leader" : "character";
            }
        }

        // ---- hostile boards ------------------------------------------------------------------------

        sealed class Scenario
        {
            public string Name;
            public Func<string, GameState> Build;
        }

        // A spread of ordinary cards to populate boards with — deliberately plain ones, so a scenario
        // never accidentally satisfies an exotic filter and hides a stuck case.
        const string CharA = "ST01-005", CharB = "ST01-006", CharC = "ST02-004";
        // EventA was ST01-013 and TrashA ST01-007 — both of which are CHARACTERS. So no board this sweep
        // ever built contained a single Event or Stage card, and every clause that targets one ("trash 1
        // Event from your hand", "K.O. 1 Stage", "activate an Event from your hand") was compared against
        // a board that could not satisfy it: glow said no, resolver said no, they agreed, no finding.
        // That blind spot is why the sweep reported 0 UNCLICKABLE with the OP15-002 Lucy bug reverted —
        // her cost wants "Event or Stage cards from your hand" and there were none to light up.
        const string EventA = "ST01-015", StageA = "EB01-011", TrashA = "ST01-007";

        static readonly Scenario[] Scenarios =
        {
            // The Champion Rifle board: the opponent has Characters, but none ACTIVE, so a clause
            // demanding an active one can never be satisfied.
            new Scenario { Name = "opponent-all-rested", Build = id => Board(id, ownChars: 1, oppChars: 2, oppRested: true, hand: 2, trash: 2, life: 2) },
            // Nothing anywhere. The harshest case and the one most clauses have never been played against.
            new Scenario { Name = "empty-board",        Build = id => Board(id, ownChars: 0, oppChars: 0, oppRested: false, hand: 0, trash: 0, life: 0) },
            // Your side empty, theirs populated (and the reverse is covered by the clause's own side).
            new Scenario { Name = "own-board-empty",    Build = id => Board(id, ownChars: 0, oppChars: 3, oppRested: false, hand: 0, trash: 0, life: 2) },
            // Everything present — anything still unclickable here is suspicious, and this is the board
            // the library-wide reach check runs against.
            new Scenario { Name = "full-board",         Build = id => Rich(Board(id, ownChars: 3, oppChars: 3, oppRested: false, hand: 3, trash: 3, life: 3)) },
            // A LIVE BATTLE. Without one, every [When Attacking] / [On Your Opponent's Attack] /
            // [Counter] clause hits a `state.Battle == null` early return, resolves to nothing, leaves no
            // pending effect, and the sweep skips it — so those tags were being counted as swept while
            // never actually being exercised. That is precisely where the reported OP15-002 Lucy bug
            // lived (her trash-for-power lit nothing in hand), and this sweep could not see it: with the
            // fix reverted, it still reported 0 UNCLICKABLE.
            new Scenario { Name = "in-battle",          Build = id => InBattle(Rich(Board(id, ownChars: 3, oppChars: 3, oppRested: false, hand: 3, trash: 3, life: 3))) },
        };

        // Put the board into a battle that is still OPEN. A bare leader-vs-leader swing resolves fully
        // inside declareAttack and leaves Battle null again, so the defender gets a [Blocker] to hold the
        // battle at the block step — which is also where these abilities genuinely fire.
        static GameState InBattle(GameState st)
        {
            var s = st.Players["south"]; var n = st.Players["north"];
            int slot = n.CharacterArea.FindIndex(c => c == null);
            if (slot < 0) slot = n.CharacterArea.Count - 1;
            n.CharacterArea[slot] = Inst("EB01-017", "north", "character");   // vanilla [Blocker]
            s.Leader.Rested = false; s.Leader.PlayedOnTurn = 0;
            try
            {
                st = GameEngine.ApplyCommand(st, new GameCommand
                {
                    Type = "declareAttack", Seat = "south",
                    Attacker = s.Leader.InstanceId, Target = st.Players["north"].Leader.InstanceId,
                });
            }
            catch { }
            st.PendingEffects.Clear();   // the swing's own reactives are not what is under audit
            return st;
        }

        // The board the reach check runs against has to be able to satisfy the AWKWARD descriptions too,
        // or a perfectly good card gets reported as dead because the audit never built a board it could
        // match. Two real ones: OP15-001 Krieg wants an opponent Character "that has 2 or more DON!!
        // cards given", and OP07-059 Foxy wants the opponent's Leader RESTED. Neither is exotic in play.
        static GameState Rich(GameState st)
        {
            var n = st.Players["north"];
            n.Leader.Rested = true;
            // The SAME Character must carry every awkward property at once. Resting one Character and
            // hanging DON!! on a different one satisfied neither half of "opponent's RESTED Characters …
            // that HAS 2 or more DON!! cards given" (OP15-038), so a real card read as dead.
            var withDon = n.CharacterArea.LastOrDefault(c => c != null);
            if (withDon != null)
                for (int i = 0; i < 2; i++)
                {
                    string donId = $"la-attached-{serialCounter++}";
                    n.CostArea.Add(new DonInstance { InstanceId = donId, Rested = true });
                    withDon.AttachedDonIds.Add(donId);
                }
            // One rested Character on each side, so "rest up to 1 …" and "set … as active" both have
            // something to bite on.
            var ownRested = st.Players["south"].CharacterArea.LastOrDefault(c => c != null);
            if (ownRested != null) ownRested.Rested = true;
            var oppRested2 = n.CharacterArea.LastOrDefault(c => c != null);
            if (oppRested2 != null) oppRested2.Rested = true;
            return st;
        }

        static int serialCounter;

        static GameState Board(string leaderId, int ownChars, int oppChars, bool oppRested, int hand, int trash, int life)
        {
            var st = GameEngine.CreateMatch(new MatchConfig
            {
                SouthDeck = "st01", NorthDeck = "st01", Seed = "leader-audit",
            });
            st.Status = "active"; st.Phase = "main"; st.ActiveSeat = "south"; st.TurnNumber = 8;
            var s = st.Players["south"]; var n = st.Players["north"];
            s.TurnsStarted = 4; n.TurnsStarted = 4;
            foreach (var p in new[] { s, n })
            {
                for (int i = 0; i < p.CharacterArea.Count; i++) p.CharacterArea[i] = null;
                p.Hand.Clear(); p.Trash.Clear(); p.Life.Clear();
            }
            st.PendingEffects.Clear();
            var underAudit = CardData.GetCard(leaderId);
            if (underAudit != null && string.Equals(underAudit.Type, "leader", StringComparison.OrdinalIgnoreCase))
                s.Leader.CardId = leaderId;    // only a LEADER may stand in as the Leader
            s.Leader.Rested = false; n.Leader.Rested = false;

            string[] pool = { CharA, CharB, CharC };
            for (int i = 0; i < ownChars && i < s.CharacterArea.Count; i++)
                s.CharacterArea[i] = Inst(pool[i % pool.Length], "south", "character");
            for (int i = 0; i < oppChars && i < n.CharacterArea.Count; i++)
            {
                var c = Inst(pool[i % pool.Length], "north", "character");
                c.Rested = oppRested;
                n.CharacterArea[i] = c;
            }
            // Hand and trash cycle Character / Event / Stage so a clause naming any of the three has
            // something real to match. A hand of three Characters made every Event- or Stage-targeting
            // cost look correctly dead.
            string[] mixed = { CharA, EventA, StageA };
            for (int i = 0; i < hand; i++)
            {
                s.Hand.Add(Inst(mixed[i % mixed.Length], "south", "hand"));
                n.Hand.Add(Inst(mixed[i % mixed.Length], "north", "hand"));
            }
            for (int i = 0; i < trash; i++)
            {
                s.Trash.Add(Inst(i == 0 ? TrashA : mixed[i % mixed.Length], "south", "trash"));
                n.Trash.Add(Inst(i == 0 ? TrashA : mixed[i % mixed.Length], "north", "trash"));
            }
            for (int i = 0; i < life; i++)
            {
                s.Life.Add(Inst(CharA, "south", "life"));
                n.Life.Add(Inst(CharA, "north", "life"));
            }
            // DON!! so a cost-gated clause is not blocked by an empty cost area (which would hide the
            // very stuck case we are looking for behind a "cannot pay" early return).
            for (int i = 0; i < 10; i++)
            {
                // Half rested, half active. With every DON!! active, all 100+ "give up to 1 RESTED DON!!
                // card to …" clauses lit their targets while the resolver had nothing to hand over —
                // reported as a glow/resolver disagreement that was purely the fixture's doing.
                s.CostArea.Add(new DonInstance { InstanceId = $"la-don-s{serialCounter++}", Rested = i >= 5 });
                n.CostArea.Add(new DonInstance { InstanceId = $"la-don-n{serialCounter++}", Rested = i >= 5 });
            }
            return st;
        }

        static CardInstance Inst(string cardId, string owner, string zone) => new CardInstance
        {
            InstanceId = $"la-{owner}-{cardId}-{serialCounter++}",
            CardId = cardId, Owner = owner, Zone = zone, PlayedOnTurn = 0,
        };

        // ---- clause extraction (same rules as effect-coverage-audit) --------------------------------

        static IEnumerable<string> ClausesOf(CardDef d)
        {
            var merged = new List<string>();
            void Add(string block, bool asTrigger)
            {
                foreach (var line in (block ?? "").Split('\n'))
                {
                    var t = line.TrimStart();
                    if (t.Length == 0) continue;
                    bool cont = merged.Count > 0 && (t[0] == '•' || t[0] == '-' || t[0] == '‐');
                    if (cont) merged[merged.Count - 1] += "\n" + line.Trim();
                    // The Trigger field holds the body WITHOUT its tag, so tag it here or it reads as
                    // untagged passive text and is skipped.
                    else merged.Add(asTrigger && !t.StartsWith("[Trigger]") ? "[Trigger] " + line.Trim() : line);
                }
            }
            Add(d.Effect, false);
            Add(d.Trigger, true);
            return merged;
        }

        static List<string> TagsOf(string clause)
        {
            var m = LeadingTags.Match(clause);
            var list = new List<string>();
            if (!m.Success) return list;
            foreach (Match t in TagInner.Matches(m.Groups[1].Value)) list.Add(t.Groups[1].Value.Trim());
            return list;
        }

        static string StripLeadingTags(string clause) => LeadingTags.Replace(clause, "").Trim();

        static bool IsKeywordOnly(string body)
        {
            string s = TagInner.Replace(body, "$1").Trim().TrimEnd('.', ' ');
            return s.Length == 0 || KeywordOnly.Any(k => s.Equals(k, StringComparison.OrdinalIgnoreCase));
        }

        // ---- report ---------------------------------------------------------------------------------

        static string Report(int cards, int clauses, int reach, List<Finding> findings, bool allCards)
        {
            var sb = new StringBuilder();
            sb.AppendLine(allCards ? "# Stuck / unreachable sweep — every card" : "# Leader-effect operability sweep");
            sb.AppendLine();
            sb.AppendLine($"- Generated: {DateTime.Now:yyyy-MM-dd HH:mm}");
            sb.AppendLine($"- Cards swept: **{cards}**, tagged clauses: **{clauses}**, clause texts reach-tested: **{reach}**");
            sb.AppendLine();
            sb.AppendLine("**STUCK** = after the real queue path (including the engine's retire-unresolvable sweep) a "
                + "pending effect remains that is NOT `Optional` and has nothing clickable anywhere. The pending panel "
                + "only enables Skip for optional effects, so this is a hard freeze for the player.");
            sb.AppendLine();
            sb.AppendLine("**UNREACHABLE** = the clause wants a target, but not one of the game's printed cards "
                + "satisfies its description (measured with `IsValidEffectTarget`, the same predicate that decides "
                + "what glows). The effect is recognized and resolves — it just can never do anything.");
            sb.AppendLine();

            foreach (var kind in new[] { "STUCK", "UNCLICKABLE", "GLOW-MISMATCH", "UNREACHABLE", "THREW" })
            {
                var rows = findings.Where(f => f.Kind == kind).ToList();
                sb.AppendLine($"## {kind} — {rows.Count}");
                sb.AppendLine();
                if (rows.Count == 0) { sb.AppendLine("_none_"); sb.AppendLine(); continue; }
                sb.AppendLine("| Card | Name | Board | Detail | Clause |");
                sb.AppendLine("|---|---|---|---|---|");
                foreach (var f in rows.OrderBy(f => f.CardId, StringComparer.Ordinal))
                    sb.AppendLine($"| {f.CardId} | {f.CardName} | {f.Scenario} | {f.Detail} | {Esc(f.Clause)} |");
                sb.AppendLine();
            }
            return sb.ToString();
        }

        static string Esc(string s) => (s ?? "").Replace("|", "\\|").Replace("\n", " ");
    }
}
