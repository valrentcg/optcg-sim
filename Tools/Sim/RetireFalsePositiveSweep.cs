using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// Audits the single highest-risk predicate in the engine by DIFFING it against itself.
    ///
    /// RetireUnresolvablePendingEffects deletes pending effects it believes can never resolve. That is
    /// necessary - a mandatory clause with no legal target would otherwise freeze the game - but it
    /// makes a false positive both expensive and invisible: the player's card does nothing, and the log
    /// prints a rule citation that makes it look deliberate.
    ///
    /// Three defects have been found in it this session, each only because the card ALSO charged a
    /// cost, which is what paidfornothing keys on. Most clauses charge nothing, so most false positives
    /// leave no trace at all.
    ///
    /// This needs no such luck. Every clause is run twice - once normally, once with retirement
    /// disabled - and the two outcomes compared. A clause the predicate deletes, but which resolves or
    /// asks for a target when left alone, is a false positive: the engine threw away a live effect.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- retiresweep
    /// </summary>
    public static class RetireFalsePositiveSweep
    {
        public static int Run()
        {
            Console.WriteLine("=== Does the retire sweep delete effects that would have worked? ===");

            int tested = 0, retiredBoth = 0, keptBoth = 0, threw = 0, conditional = 0;
            var falsePositives = new List<(string Id, string Name, string Clause, string Would)>();

            foreach (var def in CardData.Library.Values
                        .Where(c => c != null && !string.IsNullOrEmpty(c.Effect))
                        .GroupBy(c => c.Id).Select(g => g.First())
                        .OrderBy(c => c.Id, StringComparer.Ordinal))
            {
                foreach (var raw in def.Effect.Split('\n'))
                {
                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    var clause = Regex.Replace(raw, @"^\s*(\[[^\]]+\]\s*/?\s*)+", "").Trim();
                    if (clause.Length == 0) continue;
                    // Reactive abilities are dispatched elsewhere and never queued as a prompt.
                    if (Regex.IsMatch(clause, @"^(When|While)\b", RegexOptions.IgnoreCase)) continue;
                    // A body gated on a condition the board does not meet does nothing whether it
                    // is retired or not, so retiring it is harmless and proves no defect. Same
                    // exclusion paidfornothing makes, for the same reason.
                    if (Regex.IsMatch(clause, @"(^|:)\s*(If|Then, if)\b", RegexOptions.IgnoreCase))
                    { conditional++; continue; }
                    tested++;

                    try
                    {
                        bool retired = RunOnce(def.Id, clause, disableRetire: false, out _);
                        if (!retired) { keptBoth++; continue; }

                        // It was deleted. Would it have done anything if left alone?
                        bool alsoNothing = RunOnce(def.Id, clause, disableRetire: true, out string outcome);
                        if (alsoNothing) retiredBoth++;
                        else falsePositives.Add((def.Id, def.Name ?? "", clause, outcome));
                    }
                    catch (Exception) { threw++; }
                }
            }

            Console.WriteLine($"  clauses tested                    : {tested}");
            Console.WriteLine($"  skipped, conditional body         : {conditional}");
            Console.WriteLine($"  never retired                     : {keptBoth}");
            Console.WriteLine($"  retired, and would do nothing anyway : {retiredBoth}");
            Console.WriteLine($"  RETIRED BUT WOULD HAVE WORKED     : {falsePositives.Count}");
            if (threw > 0) Console.WriteLine($"  threw                             : {threw}");

            if (falsePositives.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("  live effects thrown away:");
                foreach (var f in falsePositives.Take(25))
                {
                    Console.WriteLine($"    {f.Id,-10} {Trim(f.Name, 16),-16} {Trim(f.Clause, 88)}");
                    Console.WriteLine($"               would have: {Trim(f.Would, 88)}");
                }
                if (falsePositives.Count > 25) Console.WriteLine($"    ... and {falsePositives.Count - 25} more");
            }

            return falsePositives.Count == 0 ? 0 : 1;
        }

        /// <summary>Queue the clause, press Use, and report whether it ended up doing NOTHING.
        /// <paramref name="outcome"/> describes what happened when it did do something.</summary>
        private static bool RunOnce(string cardId, string clause, bool disableRetire, out string outcome)
        {
            outcome = null;
            GameEngine.AuditDisableRetireSweep = disableRetire;
            try
            {
                var b = new Board();
                var src = b.Character("south", cardId);
                if (src == null) return true;
                int logBefore = b.St.EventLog.Count;

                GameEngine.QueueClauseForTest(b.St, "south", src, "main", clause);
                var pe = b.St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
                if (pe != null)
                    b.Apply(new GameCommand
                    { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId });

                // "Waiting" alone does NOT prove the retirement was wrong: an effect waiting on a
                // target that does not exist is exactly the freeze the retire sweep prevents. It
                // counts as alive only if something is genuinely CLICKABLE, or if the wait is a
                // deck-look / choice with its own UI.
                if (b.St.ActiveChoice != null || b.St.DeckLook != null)
                { outcome = "open a choice/deck-look"; return false; }
                var live = b.St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
                if (live != null)
                {
                    if (b.AnythingClickable(live)) { outcome = "wait for a target THAT EXISTS"; return false; }
                    return true;   // waiting on nothing — retiring it was right
                }

                var did = b.St.EventLog.Skip(logBefore).Select(l => l.Message ?? "")
                    .FirstOrDefault(m => !IsNoise(m));
                if (did != null) { outcome = did; return false; }
                return true;
            }
            finally { GameEngine.AuditDisableRetireSweep = false; }
        }

        private static bool IsNoise(string m) =>
            m.IndexOf("is pending", StringComparison.OrdinalIgnoreCase) >= 0
            || m.IndexOf("not carried out", StringComparison.OrdinalIgnoreCase) >= 0
            || m.IndexOf("effect skipped", StringComparison.OrdinalIgnoreCase) >= 0
            || m.IndexOf("cost cannot be paid", StringComparison.OrdinalIgnoreCase) >= 0
            || m.IndexOf("cannot pay the cost", StringComparison.OrdinalIgnoreCase) >= 0
            || m.IndexOf("Unknown condition", StringComparison.OrdinalIgnoreCase) >= 0
            || m.IndexOf("acknowledged for manual resolution", StringComparison.OrdinalIgnoreCase) >= 0
            || m.IndexOf("Click ", StringComparison.OrdinalIgnoreCase) >= 0;

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
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "retire-sweep" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "south"; St.TurnNumber = 8;
                S.TurnsStarted = 4; N.TurnsStarted = 4;
                for (int i = 0; i < 5; i++) { S.CharacterArea[i] = null; N.CharacterArea[i] = null; }
                S.Hand.Clear(); N.Hand.Clear(); S.Life.Clear(); N.Life.Clear();
                S.CostArea.Clear(); N.CostArea.Clear();
                for (int i = 0; i < 5; i++) { S.Life.Add(Card("ST01-005", "south", "life")); N.Life.Add(Card("ST01-005", "north", "life")); }
                for (int i = 0; i < 3; i++) S.Trash.Add(Card("ST01-005", "south", "trash"));
                Hand("south", "ST29-004"); Hand("south", "OP15-020"); Hand("south", "ST29-009");
                var nr = Character("north", "ST29-009"); if (nr != null) nr.Rested = true;
                Character("north", "OP15-040");
                Character("south", "EB03-002");
                for (int i = 0; i < 10; i++)
                    S.CostArea.Add(new DonInstance { InstanceId = $"south-rt-don-{serial++}", Rested = false });
                N.CostArea.Add(new DonInstance { InstanceId = $"north-rt-don-{serial++}", Rested = false });
                S.DonDeck = 2; N.DonDeck = 2;
                St.PendingEffects.Clear();
            }

            /// <summary>Exactly what the UI lights, so "waiting" can be told apart from "waiting
            /// on something that does not exist".</summary>
            public bool AnythingClickable(PendingEffect pe)
            {
                foreach (var seat in new[] { "south", "north" })
                {
                    var p = St.Players[seat];
                    var zones = new List<CardInstance>();
                    zones.AddRange(p.Hand); zones.AddRange(p.Trash); zones.AddRange(p.Life);
                    zones.AddRange(p.CharacterArea.Where(x => x != null));
                    if (p.Leader != null) zones.Add(p.Leader);
                    if (p.Stage != null) zones.Add(p.Stage);
                    foreach (var cc in zones)
                        if (cc != null && GameEngine.IsValidEffectTarget(St, pe, cc)) return true;
                }
                return false;
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
                InstanceId = $"{owner}-{id}-rt-{serial++}",
                CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
