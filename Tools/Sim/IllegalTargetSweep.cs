using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// The glow invariant from the enforcement side: the resolver must REFUSE a target the glow filter
    /// rejects.
    ///
    /// glowsweep and disjunctionresolve ask whether the right cards light up. This asks the opposite
    /// and more dangerous question — what happens when a command arrives naming a card that never lit.
    /// The UI only offers legal targets, so an honest client cannot produce one; but the UI is not the
    /// referee. If the resolver trusts the id it is handed, a client can point "K.O. up to 1 of your
    /// opponent's Characters" at anything on the board, and IsValidEffectTarget becomes decoration.
    ///
    /// For every clause that stops for a target, this offers each card in every zone in turn and checks
    /// the two agree: a card the glow rejects must leave the board unchanged.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- illegaltarget
    /// </summary>
    public static class IllegalTargetSweep
    {
        public static int Run()
        {
            Console.WriteLine("=== Does the resolver refuse targets the glow filter rejects? ===");

            int clauses = 0, pairsChecked = 0, threw = 0, legalMoved = 0;
            var accepted = new List<(string Id, string Clause, string Target, string Before, string After)>();
            int discardedOnly = 0;

            foreach (var def in CardData.Library.Values
                        .Where(c => c != null && !string.IsNullOrEmpty(c.Effect))
                        .GroupBy(c => c.Id).Select(g => g.First())
                        .OrderBy(c => c.Id, StringComparer.Ordinal))
            {
                foreach (var raw in def.Effect.Split('\n'))
                {
                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    var clause = SweepText.StripTimingTags(raw);
                    if (clause.Length == 0 || SweepText.IsReactiveClause(clause)) continue;

                    try
                    {
                        // Does this clause stop for a target at all?
                        var probe = new Board();
                        var psrc = probe.Character("south", def.Id);
                        if (psrc == null) continue;
                        GameEngine.QueueClauseForTest(probe.St, "south", psrc, "main", clause);
                        var ppe = probe.St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
                        if (ppe == null) continue;
                        probe.Apply(new GameCommand
                        { Type = "resolveEffect", Seat = "south", EffectId = ppe.EffectId });
                        var waiting = probe.St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
                        if (waiting == null || probe.St.DeckLook != null || probe.St.ActiveChoice != null) continue;

                        // Which cards does the glow REJECT here? Those are the ones to try.
                        var illegal = probe.AllCards()
                            .Where(c => !GameEngine.IsValidEffectTarget(probe.St, waiting, c))
                            .Select(c => c.InstanceId).Take(6).ToList();
                        if (illegal.Count == 0) continue;
                        clauses++;

                        // Self-check. A sweep that reports "0 illegal targets accepted" is worthless
                        // if Describe cannot see movement at all, so try a LEGAL target too and count
                        // how often the card demonstrably moves. That number being large is what
                        // makes the zero above evidence.
                        {
                            var okId = probe.AllCards()
                                .FirstOrDefault(x => GameEngine.IsValidEffectTarget(probe.St, waiting, x))?.InstanceId;
                            if (okId != null)
                            {
                                var lb = new Board();
                                var lsrc = lb.Character("south", def.Id);
                                if (lsrc != null)
                                {
                                    GameEngine.QueueClauseForTest(lb.St, "south", lsrc, "main", clause);
                                    var lpe0 = lb.St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
                                    if (lpe0 != null)
                                    {
                                        lb.Apply(new GameCommand { Type = "resolveEffect", Seat = "south", EffectId = lpe0.EffectId });
                                        var lpe = lb.St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
                                        var lt = lb.AllCards().FirstOrDefault(x => SameSlot(x.InstanceId, okId));
                                        if (lpe != null && lt != null && GameEngine.IsValidEffectTarget(lb.St, lpe, lt))
                                        {
                                            string lb0 = Describe(lb, lt.InstanceId);
                                            lb.Apply(new GameCommand
                                            { Type = "resolveEffect", Seat = "south", EffectId = lpe.EffectId, Target = lt.InstanceId });
                                            if (Describe(lb, lt.InstanceId) != lb0) legalMoved++;
                                        }
                                    }
                                }
                            }
                        }

                        foreach (var badId in illegal)
                        {
                            // Fresh board per attempt: a rejected target must change nothing, and reusing
                            // one board would let an earlier attempt's state muddy the next.
                            var b = new Board();
                            var src = b.Character("south", def.Id);
                            if (src == null) break;
                            GameEngine.QueueClauseForTest(b.St, "south", src, "main", clause);
                            var pe0 = b.St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
                            if (pe0 == null) break;
                            b.Apply(new GameCommand
                            { Type = "resolveEffect", Seat = "south", EffectId = pe0.EffectId });
                            var pe = b.St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
                            if (pe == null) break;
                            // Ids are per-board, so map the rejected card across by position.
                            var target = b.AllCards().FirstOrDefault(c => SameSlot(c.InstanceId, badId));
                            if (target == null) continue;
                            if (GameEngine.IsValidEffectTarget(b.St, pe, target)) continue;   // legal here after all

                            pairsChecked++;
                            string before = b.Fingerprint();
                            string tgtBefore = Describe(b, target.InstanceId);
                            b.Apply(new GameCommand
                            { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId, Target = target.InstanceId });
                            string after = b.Fingerprint();
                            // The precise question is whether the ILLEGAL CARD was touched, not
                            // whether anything at all moved. OP12-037 ("rest up to 2 of your
                            // opponent's DON!! cards or Characters") rests an opponent DON!! — a
                            // legitimate part of its own effect needing no card target — while the
                            // bogus target sat untouched in hand. Counting that as "acted on an
                            // illegal target" was my detector being vague, not the engine erring.
                            string tgtAfter = Describe(b, target.InstanceId);
                            if (tgtBefore != tgtAfter)
                            { accepted.Add((def.Id, clause, target.CardId, tgtBefore, tgtAfter)); continue; }
                            if (before == after) continue;
                            // Two very different outcomes hide behind "state changed". Acting ON an
                            // illegal card moves cards and is the dangerous one. Merely DISCARDING
                            // the pending effect touches nothing but the queue - untidy, since a
                            // stray click should not eat your decision, but no rule is broken and no
                            // honest client sends it. Counted apart so the gate reflects the first.
                            discardedOnly++;   // something else moved; the illegal card did not
                        }
                    }
                    catch (Exception) { threw++; }
                }
            }

            Console.WriteLine($"  clauses that stop for a target    : {clauses}");
            Console.WriteLine($"  illegal-target attempts made      : {pairsChecked}");
            Console.WriteLine($"  other state moved, target untouched: {discardedOnly}");
            Console.WriteLine($"  self-check: LEGAL targets that moved: {legalMoved}");
            Console.WriteLine($"  ACTED ON AN ILLEGAL TARGET        : {accepted.Count}");
            if (threw > 0) Console.WriteLine($"  threw                             : {threw}");

            foreach (var a in accepted.Take(15))
            {
                Console.WriteLine();
                Console.WriteLine($"    {a.Id}  target={a.Target}  {Trim(a.Clause, 76)}");
                Console.WriteLine($"      before: {a.Before}");
                Console.WriteLine($"      after : {a.After}");
            }
            if (accepted.Count > 15) Console.WriteLine($"    ... and {accepted.Count - 15} more");

            // The glow filter is the rule. A resolver that acts on a card the filter rejects makes the
            // filter decoration, and the client the referee.
            return accepted.Count == 0 ? 0 : 1;
        }

        /// <summary>Instance ids are "{owner}-{cardId}-it-{n}"; the serial differs per board, so match on
        /// owner + card id.</summary>
        /// <summary>The zone half of the fingerprint — everything except the pending-effect and
        /// power-bonus counters, so "the effect was dropped" is not mistaken for "a card moved".</summary>
        /// <summary>Where a card is and what state it is in — zone, rest, face-up, attachments.
        /// Missing means it left the board entirely.</summary>
        private static string Describe(Board b, string instanceId)
        {
            var c = b.AllCards().FirstOrDefault(x => x.InstanceId == instanceId);
            return c == null ? "GONE" : $"{c.Zone}/r{c.Rested}/f{c.FaceUp}/a{c.AttachedDonIds.Count}";
        }

        private static string Zones(string fingerprint) => fingerprint.Split('|')[0]
            + "|" + fingerprint.Split('|')[1];

        private static bool SameSlot(string a, string b)
        {
            string Key(string s)
            {
                var parts = s.Split('-');
                return parts.Length >= 2 ? parts[0] + "-" + parts[1] : s;
            }
            return Key(a) == Key(b);
        }

        private static string Trim(string s, int n) =>
            string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s.Substring(0, n - 1) + "…");

        private sealed class Board
        {
            public GameState St;
            public PlayerState S => St.Players["south"];
            public PlayerState N => St.Players["north"];
            private int southSlot, northSlot, serial;

            public Board()
            {
                St = GameEngine.CreateMatch(new MatchConfig
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "illegal-target" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "south"; St.TurnNumber = 8;
                foreach (var p in St.Players.Values)
                {
                    p.TurnsStarted = 4;
                    for (int i = 0; i < 5; i++) p.CharacterArea[i] = null;
                    p.Hand.Clear(); p.Life.Clear(); p.CostArea.Clear();
                    for (int i = 0; i < 4; i++) p.Life.Add(Card("ST01-005", p.Seat, "life"));
                    for (int i = 0; i < 10; i++)
                        p.CostArea.Add(new DonInstance { InstanceId = $"{p.Seat}-it-don-{serial++}", Rested = false });
                    p.DonDeck = 2;
                }
                Hand("south", "ST29-004"); Hand("south", "OP15-020");
                Hand("north", "ST29-009");
                var nr = Character("north", "ST29-009"); if (nr != null) nr.Rested = true;
                Character("north", "OP15-040");
                Character("south", "EB03-002");
                S.Trash.Add(Card("ST01-005", "south", "trash"));
                St.PendingEffects.Clear();
            }

            public IEnumerable<CardInstance> AllCards()
            {
                foreach (var p in St.Players.Values)
                {
                    foreach (var c in p.Hand) yield return c;
                    foreach (var c in p.Trash) yield return c;
                    foreach (var c in p.Life) yield return c;
                    foreach (var c in p.CharacterArea.Where(x => x != null)) yield return c;
                    if (p.Leader != null) yield return p.Leader;
                    if (p.Stage != null) yield return p.Stage;
                }
            }

            public string Fingerprint()
            {
                string Z(PlayerState p) =>
                    $"h{p.Hand.Count}/d{p.Deck.Count}/t{p.Trash.Count}/l{p.Life.Count}"
                    + $"/c{p.CharacterArea.Count(x => x != null)}/r{p.CharacterArea.Count(x => x != null && x.Rested)}"
                    + $"/don{p.CostArea.Count}/dr{p.CostArea.Count(x => x.Rested)}"
                    + $"/att{p.CharacterArea.Where(x => x != null).Sum(x => x.AttachedDonIds.Count)}";
                return Z(S) + "|" + Z(N) + $"|pe{St.PendingEffects.Count}/tp{St.TemporaryPowerBonus.Count}";
            }

            public CardInstance Hand(string seat, string id)
            { var c = Card(id, seat, "hand"); St.Players[seat].Hand.Add(c); return c; }

            public CardInstance Character(string seat, string id)
            {
                var p = St.Players[seat];
                int slot = seat == "south" ? southSlot++ : northSlot++;
                if (slot > 4) return null;
                var c = Card(id, seat, "character");
                p.CharacterArea[slot] = c;
                return c;
            }

            public void Apply(GameCommand c) => St = GameEngine.ApplyCommand(St, c);

            private CardInstance Card(string id, string owner, string zone) => new CardInstance
            {
                InstanceId = $"{owner}-{id}-it-{serial++}",
                CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
