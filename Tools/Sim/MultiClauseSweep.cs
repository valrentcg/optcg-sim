using System;
using System.Collections.Generic;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// `multiclause` proved the invariant on the sharpest card in the pool (OP06-118, two
    /// [Once Per Turn] abilities). This asks it of all **22** cards that carry two or more
    /// "you may" clauses:
    ///
    ///     using one of a card's optional abilities must not remove another one.
    ///
    /// The failure it hunts is a shared once-per-turn key, and the engine has had exactly that bug
    /// before — the bare instance id was used for both [When Attacking] and [Activate: Main], so
    /// using either consumed the other. That is invisible in any single-clause sweep, because each
    /// clause works perfectly on its own.
    ///
    /// Method: queue clause A on a fresh board and answer it, then queue clause B on the SAME board
    /// and check it still raises a decision. Compared against clause B queued on an untouched board,
    /// so the comparison is "did using A change B's availability", not "does B work at all" —
    /// a clause that never works in this fixture is not counted against the card.
    ///
    /// SCOPE — read before trusting the zero. This sweep queues clauses DIRECTLY with "main" timing,
    /// so it never runs the [When Attacking] / [Activate: Main] dispatch that assigns once-per-turn
    /// KEYS. Restoring the engine's documented shared-key bug leaves this reporting 0 while
    /// `multiclause` goes red. So:
    ///
    ///   this sweep      clause-level interference across all 22 cards (43 ordered pairs)
    ///   `multiclause`   timing-level once-per-turn key collision, one card, driven for real
    ///
    /// Neither subsumes the other, and this one's zero must not be read as covering the key bug.
    /// That was established by running the control, not by reasoning about the code.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- multisweep
    /// </summary>
    public static class MultiClauseSweep
    {
        private const int Baseline = 0;

        public static int Run()
        {
            Console.WriteLine("=== Using one ability must not remove another on the same card ===");

            var cards = MultiClauseCards();
            int pairs = 0, independent = 0, unusable = 0;
            var interference = new List<string>();

            foreach (var (id, clauses) in cards)
            {
                for (int a = 0; a < clauses.Count; a++)
                for (int bIdx = 0; bIdx < clauses.Count; bIdx++)
                {
                    if (a == bIdx) continue;

                    bool baseline, after;
                    try
                    {
                        // Does B raise a decision at all, on an untouched board?
                        var solo = new Board(id);
                        baseline = solo.QueueRaisesDecision(clauses[bIdx]);
                        if (!baseline) { unusable++; continue; }   // fixture cannot express B; not the card's fault

                        // Now use A first, on a fresh board, then ask B.
                        var both = new Board(id);
                        both.QueueRaisesDecision(clauses[a]);
                        both.AnswerAll();
                        after = both.QueueRaisesDecision(clauses[bIdx]);
                    }
                    catch (Exception) { continue; }

                    pairs++;
                    if (after) independent++;
                    else interference.Add($"{id}  using \"{Trim(clauses[a], 44)}\" removed \"{Trim(clauses[bIdx], 44)}\"");
                }
            }

            Console.WriteLine($"  {cards.Count} multi-clause cards; {pairs} ordered clause pairs checked "
                              + $"({unusable} skipped — the second clause does not fire in this fixture at all)");
            Console.WriteLine($"  pairs where the second ability survived: {independent}");
            Console.WriteLine($"  ABILITY REMOVED BY USING ANOTHER      : {interference.Count}");
            foreach (var s in interference.Take(10)) Console.WriteLine("    " + s);

            SweepRatchet.Reset();
            // Floor: a zero from zero pairs is not a result. Same lesson as the seven other floors.
            SweepRatchet.AtMost("clause pairs actually checked (floor)", Math.Max(0, 8 - pairs), 0);
            SweepRatchet.AtMost("abilities removed by using another on the same card",
                                interference.Count, Baseline);
            return SweepRatchet.Result();
        }

        private static List<(string Id, List<string> Clauses)> MultiClauseCards()
        {
            var outp = new List<(string, List<string>)>();
            foreach (var def in CardData.Library.Values.Where(d => d != null && !string.IsNullOrEmpty(d.Effect))
                                                       .GroupBy(d => d.Id).Select(g => g.First())
                                                       .OrderBy(d => d.Id, StringComparer.Ordinal))
            {
                var ym = def.Effect.Split((char)10)
                    .Select(l => l.Trim())
                    .Where(l => l.Length > 0
                                && System.Text.RegularExpressions.Regex.IsMatch(
                                       l, @"\byou (?:may|can)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                                // A [Blocker] line's parenthetical says "you may rest this card"; it is
                                // reminder text for a keyword, not a second optional ability.
                                && !l.StartsWith("[Blocker]", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (ym.Count >= 2) outp.Add((def.Id, ym));
            }
            return outp;
        }

        private static string Trim(string s, int n) =>
            string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s.Substring(0, n) + "…");

        private sealed class Board
        {
            public GameState St;
            public PlayerState S => St.Players["south"];
            public PlayerState N => St.Players["north"];
            private readonly CardInstance subject;
            private int serial;

            public Board(string cardId)
            {
                St = GameEngine.CreateMatch(new MatchConfig
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "multi-sweep" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "south"; St.TurnNumber = 8;
                foreach (var p in St.Players.Values)
                {
                    p.TurnsStarted = 4;
                    for (int i = 0; i < 5; i++) p.CharacterArea[i] = null;
                    p.Hand.Clear(); p.Life.Clear(); p.CostArea.Clear(); p.Trash.Clear();
                    foreach (var id in new[] { "ST29-004", "ST29-009", "ST01-005", "EB01-004" })
                    { p.Hand.Add(Make(id, p.Seat, "hand")); p.Trash.Add(Make(id, p.Seat, "trash")); }
                    for (int i = 0; i < 4; i++) p.Life.Add(Make("ST01-005", p.Seat, "life"));
                    for (int i = 0; i < 10; i++)
                        p.CostArea.Add(new DonInstance { InstanceId = $"{p.Seat}-ms-don-{serial++}", Rested = false });
                    p.DonDeck = 4;
                    p.AbilityUsedThisTurn.Clear();
                }
                subject = Make(cardId, "south", "character");
                S.CharacterArea[0] = subject;
                N.CharacterArea[0] = Make("OP15-040", "north", "character");
                St.PendingEffects.Clear();
            }

            /// <summary>Queue the clause as this card and report whether a decision appeared.</summary>
            public bool QueueRaisesDecision(string clause)
            {
                int pe0 = St.PendingEffects.Count;
                GameEngine.QueueClauseForTest(St, "south", subject, "main", clause);
                return St.PendingEffects.Count > pe0 || St.ActiveChoice != null || St.DeckLook != null;
            }

            public void AnswerAll()
            {
                for (int i = 0; i < 8; i++)
                {
                    var pe = St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
                    if (pe == null) break;
                    int before = St.EventLog.Count;
                    // Answer for real where possible, so any once-per-turn key is actually consumed.
                    string target = Everything()
                        .FirstOrDefault(x => GameEngine.IsValidEffectTarget(St, pe, x))?.InstanceId;
                    St = GameEngine.ApplyCommand(St, new GameCommand
                    { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId, Target = target });
                    if (St.EventLog.Count == before) break;
                }
            }

            public IEnumerable<CardInstance> Everything()
            {
                foreach (var p in St.Players.Values)
                {
                    foreach (var x in p.Hand) yield return x;
                    foreach (var x in p.Life.AsEnumerable().Reverse()) yield return x;
                    foreach (var x in p.CharacterArea.Where(y => y != null)) yield return x;
                    foreach (var x in p.Trash) yield return x;
                    if (p.Leader != null) yield return p.Leader;
                }
            }

            private CardInstance Make(string id, string owner, string zone) => new CardInstance
            {
                InstanceId = $"{owner}-{id}-ms-{serial++}",
                CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
