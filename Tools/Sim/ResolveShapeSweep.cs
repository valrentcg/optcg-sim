using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// Generalisation check for the two fixes that came out of the OP15-002 Lucy playtest. Neither was
    /// a card patch, so neither is allowed to be verified only on the card that reported it.
    ///
    /// PART 1 — the Lucy shape ("trash any number of X from your hand … +N power for every card
    /// trashed"). The invariant swept is the one that actually broke: for EVERY card carrying this
    /// shape, and for every card that could sit in hand, the GLOW and the RESOLVER must agree. A card
    /// that lights must be trashable; a card that does not light must be refused. Deliberately does not
    /// re-implement the filter — that would just re-test my own copy of it.
    ///
    /// PART 2 — the auto-resolve gate now reads the lead clause instead of the whole text, which makes
    /// MORE clauses resolve without a click. That is a widening, so the blast radius is enumerated
    /// exactly and every affected clause is run under BOTH gates and diffed
    /// (GameEngine.AuditLegacyWholeTextAutoResolveGate).
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- resolvesweep
    /// </summary>
    public static class ResolveShapeSweep
    {
        private static int passed;
        private static int failed;

        public static int Run()
        {
            Console.WriteLine("=== Resolve-shape sweep — the two Lucy fixes vs the whole card pool ===");
            SweepTrashAnyNumberForPower();
            SweepAutoResolveLeadClause();
            Console.WriteLine($"resolvesweep: {passed}/{passed + failed} passed ({failed} failed)");
            return failed == 0 ? 0 : 1;
        }

        // ---- PART 1: the Lucy shape ------------------------------------------------------------

        private static readonly Regex TrashAnyNumber = new Regex(
            @"trash any number of (.+?) from your hand\b.*?gains? \+(\d{3,5}) power during this battle for every card trashed",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        private static void SweepTrashAnyNumberForPower()
        {
            var carriers = CardData.Library.Values
                .Where(c => c != null && !string.IsNullOrEmpty(c.Effect) && TrashAnyNumber.IsMatch(c.Effect))
                .GroupBy(c => c.Id).Select(g => g.First())
                .OrderBy(c => c.Id).ToList();

            Console.WriteLine($"\n-- Part 1: \"trash any number … for every card trashed\" — {carriers.Count} card(s) --");

            // A deliberately mixed hand: Events, Stages, Characters, and different {type} tags, so a
            // tag-filtered carrier and an "Event or Stage" carrier are both exercised against cards
            // that should and should not pay.
            var probes = ProbeHandCards();
            int agreements = 0, disagreements = 0;

            foreach (var carrier in carriers)
            {
                string clause = CarrierClause(carrier);
                int lit = 0, trashable = 0;
                var mismatches = new List<string>();
                // A carrier that filters on a {type} tag (ST16-002 Gordon wants {Music}) needs at least
                // one card carrying that tag in the probe hand, or the sweep would only ever prove the
                // filter rejects things — never that it accepts the right things.
                var carrierProbes = probes.Concat(TaggedProbes(clause)).ToList();

                foreach (var probe in carrierProbes)
                {
                    // Fresh board per probe — resolving mutates hand, trash and battle power.
                    var b = new Board();
                    var src = PlaceCarrier(b, carrier);
                    if (src == null) continue;
                    var hand = b.Hand("south", probe.Id);
                    // The buff lands in a live battle, so the battle has to still BE live when the clause
                    // resolves — and it is the ATTACKING seat that may declare one, so south swings.
                    // (Attacking with north while ActiveSeat is south is simply rejected, leaving no
                    // battle at all — which is what the first version of this sweep was measuring.)
                    // A [Blocker] on the defending board then halts the battle at the block step;
                    // without one the engine runs the whole battle inside declareAttack and Battle is
                    // already null by the time the clause resolves.
                    b.Character("north", "EB01-017");           // vanilla [Blocker], halts the battle
                    b.Life("north", 3); b.Life("south", 3);     // nobody dies mid-fixture
                    b.Attack(b.S.Leader, b.N.Leader);
                    b.St.PendingEffects.Clear();
                    if (b.St.Battle == null)
                    {
                        mismatches.Add($"{probe.Id}: fixture failed — no live battle (status={b.St.Status})");
                        continue;
                    }
                    GameEngine.QueueClauseForTest(b.St, "south", src, "whenAttacking", clause);
                    var pe = b.St.PendingEffects.FirstOrDefault();
                    if (pe == null) { mismatches.Add($"{probe.Id}: clause did not queue"); continue; }

                    bool glows = GameEngine.IsValidEffectTarget(b.St, pe, hand);
                    bool battleAtQueue = b.St.Battle != null;
                    int logBefore = b.St.EventLog.Count;
                    b.Resolve(pe, hand.InstanceId);
                    bool wentToTrash = b.S.Trash.Any(c => c.InstanceId == hand.InstanceId);

                    if (glows) lit++;
                    if (wentToTrash) trashable++;
                    if (glows != wentToTrash)
                    {
                        string said = string.Join(" / ", b.St.EventLog.Skip(logBefore).Select(l => l.Message).Take(2));
                        mismatches.Add($"{probe.Id} ({probe.Type}): glow={glows} resolver={wentToTrash} "
                            + $"battleAtQueue={battleAtQueue} battleNow={(b.St.Battle != null)} pend={b.St.PendingEffects.Count} [{said}]");
                    }
                }

                if (mismatches.Count == 0) agreements++; else disagreements++;
                Check($"{carrier.Id} {carrier.Name}: glow and resolver agree on all {carrierProbes.Count} probe cards "
                      + $"({lit} payable)",
                    mismatches.Count == 0 && lit > 0,
                    mismatches.Count > 0 ? string.Join("; ", mismatches.Take(4))
                                         : "nothing in the probe hand was payable — filter may be inverted");
            }

            Console.WriteLine($"   carriers where glow==resolver on every probe: {agreements}/{carriers.Count}"
                              + (disagreements > 0 ? $"  ({disagreements} DISAGREE)" : ""));
        }

        // The single line of the card that carries the shape (these cards pair it with unrelated
        // clauses on other lines — Lucy's [Activate: Main] draw, for instance).
        private static string CarrierClause(CardDef def) =>
            (def.Effect ?? "").Split('\n').FirstOrDefault(l => TrashAnyNumber.IsMatch(l)) ?? def.Effect;

        private static CardInstance PlaceCarrier(Board b, CardDef def)
        {
            if (def.Type == "leader") { b.SetLeader("south", def.Id); return b.S.Leader; }
            return b.Character("south", def.Id);
        }

        // Cards carrying the {type} tag this carrier's filter names — one of each card type it could
        // legally accept, so an "Event or Stage"-plus-tag filter is exercised on both halves.
        private static List<CardDef> TaggedProbes(string clause)
        {
            var tag = Regex.Match(clause ?? "", @"\{([^}]+)\}");
            if (!tag.Success) return new List<CardDef>();
            string feature = tag.Groups[1].Value;
            var picks = new List<CardDef>();
            foreach (var type in new[] { "event", "stage", "character" })
            {
                var hit = CardData.Library.Values
                    .Where(c => c != null && c.Type == type && c.HasFeature(feature))
                    .OrderBy(c => c.Id, StringComparer.Ordinal).FirstOrDefault();
                if (hit != null) picks.Add(hit);
            }
            return picks;
        }

        // 20 library cards spread across type and {type} tag, chosen deterministically by id order so
        // the sweep is reproducible.
        private static List<CardDef> ProbeHandCards()
        {
            var picks = new List<CardDef>();
            foreach (var type in new[] { "event", "stage", "character" })
            {
                var ofType = CardData.Library.Values
                    .Where(c => c != null && c.Type == type && !string.IsNullOrEmpty(c.Id))
                    .GroupBy(c => c.Id).Select(g => g.First())
                    .OrderBy(c => c.Id, StringComparer.Ordinal).ToList();
                if (ofType.Count == 0) continue;
                // Spread across the whole set rather than taking the first N, so several different
                // {type} tags come along for the ride.
                int want = type == "character" ? 8 : 6;
                for (int i = 0; i < want && ofType.Count > 0; i++)
                    picks.Add(ofType[(int)((long)i * ofType.Count / want)]);
            }
            return picks;
        }

        // ---- PART 2: the auto-resolve gate widening --------------------------------------------

        private static void SweepAutoResolveLeadClause()
        {
            // The exact set the change can touch: a clause whose "you may" lives ONLY in the ". Then, …"
            // rider, whose lead is a pattern the resolver recognises. Computed from the engine's own
            // helpers (AuditThenSplit = FindThenClause, AuditEffectRecognized = IsAutomatedEffectPattern)
            // rather than a paraphrase of them.
            var affected = new List<(string Id, string Name, string Clause)>();
            foreach (var def in CardData.Library.Values.GroupBy(c => c?.Id).Select(g => g.First()).OrderBy(c => c?.Id))
            {
                if (def == null || string.IsNullOrEmpty(def.Effect)) continue;
                foreach (var line in def.Effect.Split('\n'))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    int thenAt = GameEngine.AuditThenSplit(line);
                    if (thenAt <= 0 || thenAt > line.Length) continue;      // no rider → gate unchanged
                    string lead = line.Substring(0, thenAt);
                    bool wholeSaysMay = line.IndexOf("you may", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool leadSaysMay = lead.IndexOf("you may", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!wholeSaysMay || leadSaysMay) continue;            // the rider's opt-in is the whole point
                    if (!GameEngine.AuditEffectRecognized(lead)) continue; // still would not auto-resolve
                    affected.Add((def.Id, def.Name, line.Trim()));
                }
            }

            Console.WriteLine($"\n-- Part 2: clauses whose auto-resolve decision CHANGED — {affected.Count} --");
            foreach (var a in affected.Take(40))
                Console.WriteLine($"   {a.Id}  {a.Name}");
            if (affected.Count > 40) Console.WriteLine($"   … and {affected.Count - 40} more");

            // Every one of them, run under both gates. The change is only ever allowed to move work
            // from "waiting on a Use click" to "already done"; it must never lose a decision, throw,
            // or strand a mandatory effect with nothing to click.
            int identical = 0, movedForward = 0, regressions = 0;
            foreach (var a in affected)
            {
                var legacy = RunClause(a.Id, a.Clause, legacyGate: true);
                var current = RunClause(a.Id, a.Clause, legacyGate: false);

                if (current.Threw != null)
                {
                    regressions++;
                    Check($"{a.Id} auto-resolve is safe", false, "threw: " + current.Threw);
                    continue;
                }
                // A decision the player still owns must survive: if the legacy run left a pending
                // effect that the player could act on, the new run must still leave one (the lead
                // resolving may replace it with the rider, which is the intended behaviour).
                bool lostTheDecision = legacy.PendingCount > 0 && current.PendingCount == 0
                                       && current.LogCount == legacy.LogCount;
                if (lostTheDecision)
                {
                    regressions++;
                    Check($"{a.Id} keeps the player's decision", false,
                        $"legacy pending={legacy.PendingCount} → now {current.PendingCount} with no new log line");
                    continue;
                }
                if (legacy.PendingCount == current.PendingCount && legacy.LogCount == current.LogCount) identical++;
                else movedForward++;
            }

            Console.WriteLine($"   unchanged in effect: {identical}   lead now resolves up front: {movedForward}"
                              + $"   regressions: {regressions}");
            Check("no affected clause throws, and none loses the player's decision", regressions == 0,
                $"{regressions} regression(s)");
            Check("the widening is bounded and accounted for", identical + movedForward == affected.Count,
                $"{identical}+{movedForward} != {affected.Count}");
        }

        private sealed class ClauseRun
        {
            public int PendingCount, LogCount;
            public string Threw;
        }

        private static ClauseRun RunClause(string cardId, string clause, bool legacyGate)
        {
            var r = new ClauseRun();
            try
            {
                GameEngine.AuditLegacyWholeTextAutoResolveGate = legacyGate;
                var b = new Board();
                b.Don("south", 10);
                var src = CardData.GetCard(cardId)?.Type == "leader"
                    ? Leader(b, cardId)
                    : b.Character("south", cardId);
                // A populated board on both sides, so target-needing leads have something to find and
                // are not retired as unsatisfiable for the wrong reason.
                b.Character("south", "ST01-005");
                b.Character("north", "ST01-005");
                b.Character("north", "ST01-006");
                b.Hand("south", "ST01-015");
                b.Hand("south", "ST01-005");
                int logBefore = b.St.EventLog.Count;
                GameEngine.QueueClauseForTest(b.St, "south", src, "main", clause);
                r.PendingCount = b.St.PendingEffects.Count;
                r.LogCount = b.St.EventLog.Count - logBefore;
            }
            catch (Exception ex) { r.Threw = ex.GetType().Name + ": " + ex.Message; }
            finally { GameEngine.AuditLegacyWholeTextAutoResolveGate = false; }
            return r;
        }

        private static CardInstance Leader(Board b, string cardId) { b.SetLeader("south", cardId); return b.S.Leader; }

        // ---- plumbing --------------------------------------------------------------------------

        private static void Check(string name, bool ok, string detail = null)
        {
            if (ok) { passed++; Console.WriteLine("  PASS  " + name); }
            else { failed++; Console.WriteLine("  FAIL  " + name + (string.IsNullOrEmpty(detail) ? "" : " — " + detail)); }
        }

        private sealed class Board
        {
            public GameState St;
            public PlayerState S => St.Players["south"];
            public PlayerState N => St.Players["north"];
            private int southSlot, northSlot, serial;

            public Board()
            {
                St = GameEngine.CreateMatch(new MatchConfig
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "resolve-shape-sweep" });
                St.Status = "active";
                St.Phase = "main";
                St.ActiveSeat = "south";
                St.TurnNumber = 8;
                S.TurnsStarted = 4; N.TurnsStarted = 4;
                for (int i = 0; i < 5; i++) { S.CharacterArea[i] = null; N.CharacterArea[i] = null; }
                S.Hand.Clear(); N.Hand.Clear();
                S.Life.Clear(); N.Life.Clear();
                S.CostArea.Clear(); N.CostArea.Clear();
                S.DonDeck = 10; N.DonDeck = 10;
                S.Leader.Rested = false; N.Leader.Rested = false;
                S.Leader.PlayedOnTurn = 0; N.Leader.PlayedOnTurn = 0;
                S.Leader.AttachedDonIds.Clear(); N.Leader.AttachedDonIds.Clear();
                St.PendingEffects.Clear();
            }

            public void SetLeader(string seat, string id)
            {
                var l = seat == "south" ? S.Leader : N.Leader;
                l.CardId = id; l.Rested = false; l.PlayedOnTurn = 0; l.AttachedDonIds.Clear();
            }

            public CardInstance Character(string seat, string id)
            {
                var p = seat == "south" ? S : N;
                int slot = seat == "south" ? southSlot++ : northSlot++;
                if (slot > 4) return null;
                var c = Card(id, seat, "character");
                p.CharacterArea[slot] = c;
                return c;
            }

            public CardInstance Hand(string seat, string id)
            {
                var c = Card(id, seat, "hand");
                (seat == "south" ? S : N).Hand.Add(c);
                return c;
            }

            public void Life(string seat, int count)
            {
                var p = seat == "south" ? S : N;
                for (int i = 0; i < count; i++) p.Life.Add(Card("ST01-005", seat, "life"));
            }

            public void Don(string seat, int count)
            {
                var p = seat == "south" ? S : N;
                for (int i = 0; i < count; i++)
                    p.CostArea.Add(new DonInstance { InstanceId = $"{seat}-sweep-don-{serial++}", Rested = false });
                p.DonDeck = Math.Max(0, p.DonDeck - count);
            }

            public void Attack(CardInstance attacker, CardInstance target) => Apply(new GameCommand
            {
                Type = "declareAttack", Seat = attacker.Owner,
                Attacker = attacker.InstanceId, Target = target.InstanceId,
            });

            public void Resolve(PendingEffect e, string target) => Apply(new GameCommand
            { Type = "resolveEffect", Seat = e.Seat, EffectId = e.EffectId, Target = target });

            public void Apply(GameCommand c) => St = GameEngine.ApplyCommand(St, c);

            private CardInstance Card(string id, string owner, string zone) => new CardInstance
            {
                InstanceId = $"{owner}-{id}-sweep-{serial++}",
                CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
