using System;
using System.Collections.Generic;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// The heal path was leaving cards in Life FACE-UP that should have been face-down. That was one
    /// clause family; `FaceUp` is meaningful for every Life card, so this asks the same question of
    /// every clause in the pool that touches Life at all:
    ///
    ///     after this clause resolves, is any Life card face-up that the text never asked to be?
    ///
    /// It is the purest hidden-information check available headlessly. Rule 3-10-2 makes the Life
    /// area secret and face-down "unless otherwise specified", and 3-10-2-1 makes face-up something
    /// an effect MAY specify — so a face-up Life card with no wording behind it is the opponent
    /// seeing a card they are not entitled to, and knowing a [Trigger] before it is dealt.
    ///
    /// A leak here is invisible twice over: nothing in the log mentions facing, and the Life COUNT
    /// is unchanged, so every other check in this suite reads normal.
    ///
    /// Both seats are inspected — several clauses move the OPPONENT's Life, and a leak there is
    /// worse, since it exposes cards to the player who caused it.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- lifefacing
    /// </summary>
    public static class LifeFacingSweep
    {
        private const int Baseline = 0;

        public static int Run()
        {
            Console.WriteLine("=== Life facing: does any clause leave a card face-up unasked? ===");

            var seen = new HashSet<string>();
            var leaks = new List<string>();
            int driven = 0, touched = 0;

            foreach (var def in CardData.Library.Values
                        .Where(d => d != null)
                        .GroupBy(d => d.Id).Select(g => g.First())
                        .OrderBy(d => d.Id, StringComparer.Ordinal))
            {
                foreach (var src in new[] { def.Effect, def.Trigger })
                {
                    if (string.IsNullOrWhiteSpace(src)) continue;
                    foreach (var raw in src.Split((char)10))
                    {
                        var clause = raw.Trim();
                        if (clause.Length == 0) continue;
                        if (clause.IndexOf("Life", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        if (!seen.Add(Normalize(clause))) continue;

                        Board b;
                        try { b = new Board(); b.Drive(clause); }
                        catch (Exception) { continue; }
                        driven++;

                        // Did this clause say anything about turning cards face-up?
                        bool allowsFaceUp = clause.IndexOf("face-up", StringComparison.OrdinalIgnoreCase) >= 0;
                        var exposed = b.AllLife().Where(c => c.FaceUp).ToList();
                        if (exposed.Count > 0) touched++;
                        if (!allowsFaceUp && exposed.Count > 0)
                            leaks.Add($"{def.Id}  {exposed.Count} face-up  :: {Trim(clause, 74)}");
                    }
                }
            }

            Console.WriteLine($"  drove {driven} distinct Life-touching clauses; {touched} left at least one card face-up");
            Console.WriteLine($"  face-up with NO wording asking for it: {leaks.Count}");
            foreach (var s in leaks.Take(10)) Console.WriteLine("    " + s);

            SweepRatchet.Reset();
            // Guard the guard: if nothing was ever driven, zero leaks is not a result.
            SweepRatchet.AtMost("Life clauses driven (floor check)", Math.Max(0, 100 - driven), 0);
            SweepRatchet.AtMost("Life cards left face-up with no wording for it", leaks.Count, Baseline);
            return SweepRatchet.Result();
        }

        private static string Normalize(string s) =>
            System.Text.RegularExpressions.Regex.Replace(
                System.Text.RegularExpressions.Regex.Replace(s, "[0-9]+", "N"), @"\[[^\]]*\]", "[T]").Trim();

        private static string Trim(string s, int n) =>
            string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s.Substring(0, n) + "…");

        private sealed class Board
        {
            public GameState St;
            public PlayerState S => St.Players["south"];
            public PlayerState N => St.Players["north"];
            private int serial;

            public Board()
            {
                St = GameEngine.CreateMatch(new MatchConfig
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "life-facing" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "south"; St.TurnNumber = 8;
                foreach (var p in St.Players.Values)
                {
                    p.TurnsStarted = 4;
                    for (int i = 0; i < 5; i++) p.CharacterArea[i] = null;
                    p.Hand.Clear(); p.Life.Clear(); p.CostArea.Clear();
                    foreach (var id in new[] { "ST29-004", "ST29-009", "ST01-005" })
                        p.Hand.Add(Make(id, p.Seat, "hand"));
                    // Every Life card starts FACE-DOWN, which is the state rule 3-10-2 describes.
                    foreach (var id in new[] { "ST01-005", "ST01-006", "EB01-004", "EB01-005" })
                        p.Life.Add(Make(id, p.Seat, "life"));
                    for (int i = 0; i < 10; i++)
                        p.CostArea.Add(new DonInstance { InstanceId = $"{p.Seat}-lf-don-{serial++}", Rested = false });
                    p.DonDeck = 2;
                }
                S.CharacterArea[0] = Make("ST29-010", "south", "character");
                N.CharacterArea[0] = Make("OP15-040", "north", "character");
                St.PendingEffects.Clear();
            }

            public IEnumerable<CardInstance> AllLife() => S.Life.Concat(N.Life);

            public void Drive(string clause)
            {
                GameEngine.QueueClauseForTest(St, "south", S.CharacterArea[0], "main", clause);
                for (int i = 0; i < 10; i++)
                {
                    if (St.DeckLook != null)
                    {
                        int before = St.EventLog.Count;
                        var order = St.DeckLook.Cards.Select(x => x.InstanceId).ToList();
                        St = GameEngine.ApplyCommand(St, new GameCommand
                        { Type = "deckLookConfirmOrder", Seat = "south", OrderedInstanceIds = order });
                        if (St.EventLog.Count == before)
                            St = GameEngine.ApplyCommand(St, new GameCommand
                            { Type = "deckLookScryConfirm", Seat = "south", OrderedInstanceIds = order });
                        if (St.EventLog.Count == before) break;
                        continue;
                    }
                    var pe = St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
                    if (pe == null) break;
                    string target = Everything()
                        .FirstOrDefault(x => GameEngine.IsValidEffectTarget(St, pe, x))?.InstanceId;
                    int peBefore = St.PendingEffects.Count, logBefore = St.EventLog.Count;
                    St = GameEngine.ApplyCommand(St, new GameCommand
                    { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId, Target = target });
                    if (St.PendingEffects.Count == peBefore && St.EventLog.Count == logBefore) break;
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
                InstanceId = $"{owner}-{id}-lf-{serial++}",
                CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
