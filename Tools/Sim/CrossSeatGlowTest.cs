using System;
using System.Collections.Generic;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// Every prompt this workstream added lives on a seat that does NOT control the source, and
    /// glowsweep cannot reach any of them: it drives each card's text on the controller's seat, so
    /// a decision handed to the opponent is a state it never constructs.
    ///
    /// That matters because the failure mode is the one actually reported against this engine —
    /// "offered with nothing clickable" (OP15-002 Lucy, OP15-114 Wyper). The UI lights a card when
    /// GameEngine.IsValidEffectTarget says so and the resolver accepts a click only if the same call
    /// agrees, so glow == clickable is engine code and testable headlessly even though the symptom
    /// is visual.
    ///
    /// Three of the new prompts are MANDATORY — the player chooses which, never whether — so Skip is
    /// not an escape and nothing clickable means the game is frozen, not merely awkward. Those are
    /// the ones checked here, on the seat that owns the decision, against BOTH players' zones since
    /// half of them deliberately point at the other player's hand.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- crossglow
    /// </summary>
    public static class CrossSeatGlowTest
    {
        private static int passed, failed;

        public static int Run()
        {
            Console.WriteLine("=== Prompts on the OPPONENT's seat: is anything actually clickable? ===");

            // The CARD TEXT is queued on south, exactly as a real play would; the engine is what
            // re-queues the second-person half onto north. Queuing that internal half directly (my
            // first version) puts it on the wrong seat and tests nothing.
            //
            // (label, card text queued on south, whose cards the click must land on)
            Case("self-disposal: trash from your own hand",
                 "Your opponent trashes 1 card from their hand.", "north");
            Case("self-disposal: place from your own hand at the deck bottom",
                 "Your opponent places 1 card from their hand at the bottom of their deck.", "north");
            Case("opponent-picks: choose from the OTHER player's hand",
                 "Your opponent chooses 1 card from your hand; trash that card.", "south");
            // The nested "Your opponent may X. If they do not, Y" halves. These are OPTIONAL, so
            // nothing clickable is not a freeze — but it would make the opt-in unusable, leaving
            // Skip as the only move and the decline branch as the only outcome, which is the
            // pre-fix behaviour wearing a prompt.
            Case("nested opt-in: opponent may pay a Life card",
                 "Your opponent may trash 1 card from the top of their Life cards. "
                 + "If they do not, give up to 1 of your opponent's Leader or Character cards -2000 power during this turn.",
                 "north");

            Console.WriteLine($"crossglow: {passed}/{passed + failed} passed ({failed} failed)");
            return failed == 0 ? 0 : 1;
        }

        private static void Check(string label, bool ok, string detail = "")
        {
            if (ok) { passed++; Console.WriteLine("  PASS  " + label); }
            else { failed++; Console.WriteLine("  FAIL  " + label + (detail.Length > 0 ? "  -- " + detail : "")); }
        }

        private static void Case(string label, string clause, string expectZoneOwner)
        {
            var b = new Board();
            GameEngine.QueueClauseForTest(b.St, "south", b.S.CharacterArea[0], "main", clause);

            var pe = b.St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "north");
            if (pe == null)
            {
                Check(label, false, "no prompt was raised on north at all");
                return;
            }

            // Exactly what the UI asks, card by card, over everything on the table.
            var lit = b.Everything()
                       .Where(c => GameEngine.IsValidEffectTarget(b.St, pe, c))
                       .ToList();

            if (lit.Count == 0)
            {
                Check(label, false,
                      pe.Optional
                          ? "nothing clickable (Skip still available, but the effect is unusable)"
                          : "nothing clickable and the prompt is MANDATORY — the game is frozen here");
                return;
            }

            // Lighting *something* is not enough: it has to be the right player's cards, or the
            // prompt sends them clicking at a zone the resolver will refuse.
            var owners = lit.Select(c => c.Owner).Distinct().ToList();
            bool rightZone = owners.Contains(expectZoneOwner);
            Check(label, rightZone,
                  $"{lit.Count} card(s) lit but owned by [{string.Join(",", owners)}], expected {expectZoneOwner}");
        }

        private sealed class Board
        {
            public GameState St;
            public PlayerState S => St.Players["south"];
            public PlayerState N => St.Players["north"];
            private int serial;

            public Board()
            {
                St = GameEngine.CreateMatch(new MatchConfig
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "cross-glow" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "south"; St.TurnNumber = 8;
                foreach (var p in St.Players.Values)
                {
                    p.TurnsStarted = 4;
                    for (int i = 0; i < 5; i++) p.CharacterArea[i] = null;
                    p.Hand.Clear(); p.Life.Clear(); p.CostArea.Clear(); p.Trash.Clear();
                    foreach (var id in new[] { "ST29-004", "ST29-009", "ST01-005" })
                        p.Hand.Add(Make(id, p.Seat, "hand"));
                    for (int i = 0; i < 4; i++) p.Life.Add(Make("ST01-005", p.Seat, "life"));
                    for (int i = 0; i < 10; i++)
                        p.CostArea.Add(new DonInstance { InstanceId = $"{p.Seat}-cg-don-{serial++}", Rested = false });
                    p.DonDeck = 0;
                }
                S.CharacterArea[0] = Make("ST29-010", "south", "character");
                N.CharacterArea[0] = Make("OP15-040", "north", "character");
                St.PendingEffects.Clear();
            }

            /// <summary>Every card the UI could put a glow on.</summary>
            public IEnumerable<CardInstance> Everything()
            {
                foreach (var p in St.Players.Values)
                {
                    foreach (var x in p.Hand) yield return x;
                    foreach (var x in p.Life.AsEnumerable().Reverse()) yield return x;   // Life top is LAST
                    foreach (var x in p.CharacterArea.Where(y => y != null)) yield return x;
                    foreach (var x in p.Trash) yield return x;
                    if (p.Leader != null) yield return p.Leader;
                    if (p.Stage != null) yield return p.Stage;
                }
            }

            private CardInstance Make(string id, string owner, string zone) => new CardInstance
            {
                InstanceId = $"{owner}-{id}-cg-{serial++}",
                CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
