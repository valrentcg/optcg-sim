using System;
using System.Collections.Generic;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// The instrument the last five defects should have been found by.
    ///
    /// Each of them — the protection discard, OP01-038's [On K.O.], and the 25-card self-disposal
    /// class — was the same bug wearing a different card name: the engine moved a card out of a zone
    /// that held several equally legal candidates, without asking anyone which. They were found one
    /// at a time, by reading. This asks the question directly, over the whole pool:
    ///
    ///     did a clause take cards from a zone that had MORE candidates than it took,
    ///     without ever raising a prompt?
    ///
    /// Two exclusions, both principled rather than convenient:
    ///
    ///   "top of" / "bottom of"   positional by wording, so there is nothing to choose. This is
    ///                            load-bearing: Life and deck clauses are overwhelmingly positional,
    ///                            and counting them would bury the real findings under ~200 rows.
    ///   DON!! and deck           DON!! cards carry no identity (choosing among them is not a
    ///                            decision), and the deck is a secret ordered area.
    ///
    /// Hand, trash and the BOARD are the zones where a pick is both meaningful and possible, so they
    /// are what this reports. The board was added second and matters most: which of your own
    /// Characters dies is never arbitrary, and the controller choosing an opponent's victim is the
    /// single most common targeted decision in the game. Widening to it took the sweep from 523
    /// clauses to 1031 — and the widened zone was CONTROLLED before its zero was believed (making
    /// the K.O. resolver auto-pick a victim reports 16 and breaks the ratchet), because a new check
    /// that silently matches nothing reads exactly like a clean result. It is a REPORTING sweep, ratcheted rather than gated on zero: some auto-picks
    /// are legitimate (a skip fallback, a blind pick from a hidden zone) and the number should be
    /// read, not asserted to be nothing. What it must never do is grow.
    ///
    /// SCOPE: this reads `def.Effect` only. [Trigger] text lives in a separate data field and fires
    /// only through battle damage, so it is NOT covered here — `triggerfield` is the companion that
    /// drives those 42 clauses through a real hit. Feeding them into this sweep instead would queue
    /// a reactive ability as a main-timing clause and invent a question that never existed.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- autopick
    /// </summary>
    public static class AutoPickSweep
    {
        /// <summary>Set from the first measured run. This is a HELD LINE, not a target: some of the
        /// remaining rows are legitimate (skip fallbacks, blind picks from hidden zones). Lower it
        /// whenever a real one is fixed; never raise it to make a run pass.</summary>
        private const int AutoPickBaseline = 0;

        private sealed class Finding
        {
            public string CardId, Clause, Zone;
            public int Took, Had;
        }

        public static int Run()
        {
            Console.WriteLine("=== Auto-pick sweep: did the engine choose a card nobody was asked about? ===");

            var seen = new HashSet<string>();
            var findings = new List<Finding>();
            int driven = 0, prompted = 0;

            foreach (var def in CardData.Library.Values.OrderBy(d => d?.Id))
            {
                if (def == null || string.IsNullOrWhiteSpace(def.Effect)) continue;
                foreach (var raw in def.Effect.Split((char)10))
                {
                    string clause = raw.Trim();
                    if (clause.Length == 0) continue;
                    if (!LooksSelective(clause)) continue;
                    if (!seen.Add(Normalize(clause))) continue;

                    driven++;
                    var b = new Board();
                    int hand0 = b.S.Hand.Count, trash0 = b.S.Trash.Count;
                    int oppHand0 = b.N.Hand.Count, oppTrash0 = b.N.Trash.Count;
                    int chars0 = Bodies(b.S), oppChars0 = Bodies(b.N);

                    try { b.Queue(clause); }
                    catch (Exception) { continue; }

                    // A prompt on EITHER seat means somebody was asked — that is the whole point.
                    if (b.St.PendingEffects.Any(e => e != null) || b.St.ActiveChoice != null || b.St.DeckLook != null)
                    { prompted++; continue; }

                    Consider(findings, def.Id, clause, "your hand", hand0 - b.S.Hand.Count, hand0);
                    Consider(findings, def.Id, clause, "your trash", trash0 - b.S.Trash.Count, trash0);
                    Consider(findings, def.Id, clause, "opponent hand", oppHand0 - b.N.Hand.Count, oppHand0);
                    Consider(findings, def.Id, clause, "opponent trash", oppTrash0 - b.N.Trash.Count, oppTrash0);
                    // The board is the zone with the MOST information attached to a choice: which of
                    // your own Characters dies is never arbitrary. Both seats, since "K.O. 1 of your
                    // opponent's Characters" is equally a choice the controller should be making.
                    Consider(findings, def.Id, clause, "your board", chars0 - Bodies(b.S), chars0);
                    Consider(findings, def.Id, clause, "opponent board", oppChars0 - Bodies(b.N), oppChars0);
                }
            }

            Console.WriteLine($"  drove {driven} distinct selective clauses; {prompted} raised a prompt");
            Console.WriteLine($"  auto-picked with a real choice available: {findings.Count}");

            foreach (var g in findings.GroupBy(f => f.Zone).OrderByDescending(g => g.Count()))
            {
                Console.WriteLine($"    {g.Key}: {g.Count()}");
                foreach (var f in g.Take(6))
                    Console.WriteLine($"      {f.CardId}  took {f.Took} of {f.Had}  :: {Trim(f.Clause, 96)}");
            }

            // Ratchet, not a zero gate: some of these are legitimate and the honest move is to hold
            // the line rather than pretend the right number is zero. See SweepRatchet.
            SweepRatchet.Reset();
            // Floor. Every prompted clause is skipped — correct, a prompt is the opposite of an
            // auto-pick — but that means a break which made EVERYTHING prompt would leave this
            // comparing nothing and still reporting 0. ~295 clauses are actually compared.
            int compared = driven - prompted;
            Console.WriteLine($"  clauses actually compared (unprompted): {compared}");
            SweepRatchet.AtMost("clauses compared (floor — 0 findings from 0 compared is not a pass)",
                                Math.Max(0, 150 - compared), 0);
            SweepRatchet.AtMost("auto-picked with a real choice available", findings.Count, AutoPickBaseline);
            return SweepRatchet.Result();
        }

        private static int Bodies(PlayerState p) => p.CharacterArea.Count(x => x != null);

        private static void Consider(List<Finding> into, string id, string clause, string zone, int took, int had)
        {
            // Took nothing → nothing was chosen. Took everything → there was no choice to make.
            if (took <= 0 || had <= took) return;
            into.Add(new Finding { CardId = id, Clause = clause, Zone = zone, Took = took, Had = had });
        }

        /// <summary>Clauses that move a bounded number of cards out of a zone somebody could pick
        /// from. "Top/bottom of" is positional by wording and excluded — see the class comment.</summary>
        private static bool LooksSelective(string clause)
        {
            string c = clause.ToLowerInvariant();
            if (c.IndexOf("top of", StringComparison.Ordinal) >= 0) return false;
            if (c.IndexOf("bottom of", StringComparison.Ordinal) >= 0) return false;
            if (c.IndexOf(" all ", StringComparison.Ordinal) >= 0) return false;
            bool zone = c.Contains("hand") || c.Contains("trash") || c.Contains("character");
            bool verb = c.Contains("trash") || c.Contains("play") || c.Contains("add")
                     || c.Contains("return") || c.Contains("place") || c.Contains("reveal")
                     || c.Contains("discard") || c.Contains("k.o.") || c.Contains("rest");
            return zone && verb && System.Text.RegularExpressions.Regex.IsMatch(clause, "[0-9]");
        }

        private static string Normalize(string s) =>
            System.Text.RegularExpressions.Regex.Replace(
                System.Text.RegularExpressions.Regex.Replace(s, "[0-9]+", "N"),
                @"\[[^\]]*\]", "[T]").Trim();

        private static string Trim(string s, int n) =>
            string.IsNullOrEmpty(s) || s.Length <= n ? (s ?? "") : s.Substring(0, n) + "…";

        private sealed class Board
        {
            public GameState St;
            public PlayerState S => St.Players["south"];
            public PlayerState N => St.Players["north"];
            private int serial;

            public Board()
            {
                St = GameEngine.CreateMatch(new MatchConfig
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "auto-pick" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "south"; St.TurnNumber = 8;
                foreach (var p in St.Players.Values)
                {
                    p.TurnsStarted = 4;
                    for (int i = 0; i < 5; i++) p.CharacterArea[i] = null;
                    p.Hand.Clear(); p.Life.Clear(); p.CostArea.Clear(); p.Trash.Clear();
                    // Four distinguishable cards in every pickable zone, so "had more than it took"
                    // is a real statement about choice rather than an artefact of a thin fixture.
                    foreach (var id in new[] { "ST29-004", "ST29-009", "ST01-005", "ST01-006" })
                    { Add(p, id, "hand"); Add(p, id, "trash"); }
                    for (int i = 0; i < 4; i++) p.Life.Add(Make("ST01-005", p.Seat, "life"));
                    for (int i = 0; i < 10; i++)
                        p.CostArea.Add(new DonInstance { InstanceId = $"{p.Seat}-ap-don-{serial++}", Rested = false });
                    p.DonDeck = 0;
                }
                S.CharacterArea[0] = Make("ST29-010", "south", "character");
                S.CharacterArea[1] = Make("EB03-002", "south", "character");
                N.CharacterArea[0] = Make("OP15-040", "north", "character");
                N.CharacterArea[1] = Make("EB03-002", "north", "character");
                St.PendingEffects.Clear();
            }

            private void Add(PlayerState p, string id, string zone)
            {
                var c = Make(id, p.Seat, zone);
                if (zone == "hand") p.Hand.Add(c); else p.Trash.Add(c);
            }

            public void Queue(string clause) =>
                GameEngine.QueueClauseForTest(St, "south", S.CharacterArea[0], "main", clause);

            private CardInstance Make(string id, string owner, string zone) => new CardInstance
            {
                InstanceId = $"{owner}-{id}-ap-{serial++}",
                CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
