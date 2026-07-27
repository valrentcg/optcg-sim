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
        };
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
                    if (tags.Any(t => t.Equals("Trigger", StringComparison.OrdinalIgnoreCase))) continue;
                    if (!tags.Any(t => ResolverEventTags.Contains(t))) continue;
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
                            GameEngine.QueueClauseForTest(st, "south", src, "main", clause);
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

                        bool clickable = AnyValidTarget(st, pe);
                        // With nothing clickable the panel still shows an enabled "Use Effect" button, so
                        // this is only a freeze if pressing that ALSO fails to move the game on. Without
                        // this second half the sweep flagged every "rest ALL of your opponent's Characters"
                        // against an empty board — nothing to click, but Use Effect resolves it fine.
                        bool useEffectStuck = !clickable && UseEffectLeavesItPending(st, pe);
                        if (useEffectStuck && !pe.Optional)
                            findings.Add(new Finding
                            {
                                CardId = d.Id, CardName = d.Name, Kind = "STUCK", Scenario = scenario.Name,
                                Clause = body,
                                Detail = "mandatory, nothing clickable anywhere, Skip disabled → the panel cannot be dismissed",
                            });

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
                        if (scenario.Name == "full-board" && !clickable)
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
                                if (reach == 0 && WantsACardTarget(pe))
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
        static string ResolverAcceptsSomeBoardCard(GameState st, PendingEffect pe)
        {
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
                    if (after != before)
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
                    return string.Join("/", kv.Key, c2.Zone, c2.Rested,
                        c2.AttachedDonIds == null ? 0 : c2.AttachedDonIds.Count,
                        c2.Modifiers == null ? 0 : c2.Modifiers.Count);
            }
            return "gone";
        }

        // Is the step even asking for a CARD? A cost paid by resting DON!!, turning a Life card face-up
        // or trashing off the top of the deck is driven by the panel's own button, so "nothing glows" is
        // correct there and must not be reported as a dead description.
        static bool WantsACardTarget(PendingEffect pe)
        {
            // Strip the same prefixes the engine strips before it looks for a cost — leading timing tags
            // AND the DON!!-cost prefix. Without the second one this read "DON!! -1 (...) You may rest this
            // Leader: ..." as "not a cost step at all" and reported a perfectly good card as dead.
            string t = Regex.Replace(pe.Text ?? "", @"^\s*(\[[^\]]+\]\s*/?\s*)+", "");
            t = Regex.Replace(t, @"^\s*(?:[➀-➉①-⑩]|DON!!\s*[-−–‑‒—]\s*\d+)\s*(?:\([^)]*\))?\s*[:：]?\s*", "");
            var costGate = Regex.Match(t, @"^\s*(?:\[[^\]]+\]\s*/?\s*)*You (?:may|can) (?<cost>[^:]+):",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!costGate.Success) return true;              // not a cost step — the body wants the target
            string cost = costGate.Groups["cost"].Value;
            return Regex.IsMatch(cost, @"\b(?:K\.O\.|trash|rest|return|place|add|reveal)\b[^:]*\b(?:Characters?|cards?)\b",
                       RegexOptions.IgnoreCase)
                && !Regex.IsMatch(cost, @"DON!!|top of your deck|top of your Life|Life cards face-up",
                       RegexOptions.IgnoreCase);
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
                    foreach (var zone in new[] { "character", "hand", "trash", "life" })
                    {
                        if (zone == "character" && string.Equals(def.Type, "leader", StringComparison.OrdinalIgnoreCase))
                            continue;
                        var inst = new CardInstance
                        {
                            InstanceId = $"reach-{serial++}", CardId = def.Id, Owner = owner, Zone = zone,
                        };
                        var list = zone == "hand" ? p.Hand : zone == "trash" ? p.Trash : zone == "life" ? p.Life : null;
                        if (list != null) list.Add(inst);
                        bool ok = GameEngine.IsValidEffectTarget(st, pe, inst);
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
        const string EventA = "ST01-013", TrashA = "ST01-007";

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
        };

        // The board the reach check runs against has to be able to satisfy the AWKWARD descriptions too,
        // or a perfectly good card gets reported as dead because the audit never built a board it could
        // match. Two real ones: OP15-001 Krieg wants an opponent Character "that has 2 or more DON!!
        // cards given", and OP07-059 Foxy wants the opponent's Leader RESTED. Neither is exotic in play.
        static GameState Rich(GameState st)
        {
            var n = st.Players["north"];
            n.Leader.Rested = true;
            var withDon = n.CharacterArea.FirstOrDefault(c => c != null);
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
            for (int i = 0; i < hand; i++)
            {
                s.Hand.Add(Inst(i % 2 == 0 ? CharA : EventA, "south", "hand"));
                n.Hand.Add(Inst(CharA, "north", "hand"));
            }
            for (int i = 0; i < trash; i++)
            {
                s.Trash.Add(Inst(TrashA, "south", "trash"));
                n.Trash.Add(Inst(TrashA, "north", "trash"));
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
                s.CostArea.Add(new DonInstance { InstanceId = $"la-don-s{serialCounter++}", Rested = false });
                n.CostArea.Add(new DonInstance { InstanceId = $"la-don-n{serialCounter++}", Rested = false });
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
            foreach (var line in (d.Effect ?? "").Split('\n'))
            {
                var t = line.TrimStart();
                if (t.Length == 0) continue;
                bool cont = merged.Count > 0 && (t[0] == '•' || t[0] == '-' || t[0] == '‐');
                if (cont) merged[merged.Count - 1] += "\n" + line.Trim();
                else merged.Add(line);
            }
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

            foreach (var kind in new[] { "STUCK", "UNCLICKABLE", "UNREACHABLE", "THREW" })
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
