using System;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// "life re-arranging mechanics" — the third Life keyword the brief names, enumerated the way
    /// heal was. 10 distinct wordings across the pool, and they split three ways:
    ///
    ///   reorder YOUR OWN Life        "Look at all of your Life cards and place them back ... in any order"
    ///   reorder the OPPONENT'S Life  EB01-052, same sentence pointed at them
    ///   reorder AND remove           ST13-016 "place 1 at the top of your deck and place the rest back"
    ///
    /// Three properties, and the middle one is why this file exists at all. A reorder is a LOOK: the
    /// player sees their Life cards and puts them back. If the engine leaves them face-up afterwards
    /// the entire Life stack becomes public — a strictly worse version of the face-up defect just
    /// fixed in the heal path, and equally invisible in a log that only counts Life.
    ///
    ///   COUNT    a reorder is not a heal and not a loss; the count is unchanged
    ///   FACING   looking at your own Life does not reveal it to the opponent
    ///   OWNER    "your opponent's Life cards" must move THEIR stack, not yours
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- liferearrange
    /// </summary>
    public static class LifeRearrangeTest
    {
        private static int passed, failed;

        public static int Run()
        {
            Console.WriteLine("=== Life re-arranging: count, facing, and whose stack ===");
            ReorderKeepsTheCountAndTheCards();
            ReorderLeavesLifeFaceDown();
            OpponentReorderTouchesTheirStack();
            ReorderWithRemovalMovesExactlyTheStatedNumber();
            Console.WriteLine($"liferearrange: {passed}/{passed + failed} passed ({failed} failed)");
            return failed == 0 ? 0 : 1;
        }

        private static void Check(string label, bool ok, string detail = "")
        {
            if (ok) { passed++; Console.WriteLine("  PASS  " + label); }
            else { failed++; Console.WriteLine("  FAIL  " + label + (detail.Length > 0 ? "  -- " + detail : "")); }
        }

        private const string Reorder =
            "Look at all of your Life cards and place them back in your Life area in any order.";

        private static void ReorderKeepsTheCountAndTheCards()
        {
            var b = new Board();
            int n0 = b.S.Life.Count;
            var before = b.S.Life.Select(x => x.InstanceId).OrderBy(x => x).ToList();

            b.Drive(Reorder);

            var after = b.S.Life.Select(x => x.InstanceId).OrderBy(x => x).ToList();
            Check("a reorder keeps every Life card and the count",
                  b.S.Life.Count == n0 && before.SequenceEqual(after),
                  $"life {n0}->{b.S.Life.Count}; a reorder is neither a heal nor a loss");
        }

        /// <summary>The one that matters. Looking at your own Life is not revealing it.</summary>
        private static void ReorderLeavesLifeFaceDown()
        {
            var b = new Board();
            foreach (var c in b.S.Life) c.FaceUp = false;

            b.Drive(Reorder);

            var exposed = b.S.Life.Where(c => c.FaceUp).ToList();
            Check("a reorder leaves the Life cards FACE-DOWN",
                  exposed.Count == 0,
                  $"{exposed.Count} of {b.S.Life.Count} Life cards ended face-up — looking at your own "
                  + "Life would have made the whole stack public");
        }

        /// <summary>"Look at all of your OPPONENT'S Life cards ..." must move their stack. Getting
        /// the owner backwards here would let a card shuffle its controller's own Life while
        /// claiming to disrupt the opponent — and the Life counts would look identical.</summary>
        private static void OpponentReorderTouchesTheirStack()
        {
            var b = new Board();
            var southBefore = b.S.Life.Select(x => x.InstanceId).ToList();
            var northBefore = b.N.Life.Select(x => x.InstanceId).ToList();

            b.Drive("Look at all of your opponent's Life cards and place them back in their Life area in any order.");

            bool southIntact = b.S.Life.Select(x => x.InstanceId).SequenceEqual(southBefore);
            bool northSameCards = b.N.Life.Select(x => x.InstanceId).OrderBy(x => x)
                                   .SequenceEqual(northBefore.OrderBy(x => x));
            bool northFaceDown = b.N.Life.All(c => !c.FaceUp);
            Check("an opponent-facing reorder leaves MY stack alone and keeps theirs intact and hidden",
                  southIntact && northSameCards && northFaceDown,
                  $"southUnchanged={southIntact} northSameCards={northSameCards} northFaceDown={northFaceDown}");
        }

        /// <summary>ST13-016: "place 1 at the top of your deck and place the rest back in your Life
        /// area in any order." Exactly one leaves — not zero, not the whole stack.</summary>
        private static void ReorderWithRemovalMovesExactlyTheStatedNumber()
        {
            var b = new Board();
            int life0 = b.S.Life.Count, deck0 = b.S.Deck.Count;

            b.Drive("Look at all your Life cards; place 1 at the top of your deck and place the rest back in your Life area in any order.");

            int lost = life0 - b.S.Life.Count, gained = b.S.Deck.Count - deck0;
            Check("reorder-with-removal moves exactly the stated number to the deck",
                  lost == 1 && gained == 1,
                  $"life -{lost} (want 1), deck +{gained} (want 1)");
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
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "life-rearrange" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "south"; St.TurnNumber = 8;
                foreach (var p in St.Players.Values)
                {
                    p.TurnsStarted = 4;
                    for (int i = 0; i < 5; i++) p.CharacterArea[i] = null;
                    p.Hand.Clear(); p.Life.Clear(); p.CostArea.Clear();
                    foreach (var id in new[] { "ST29-004", "ST29-009" }) p.Hand.Add(Make(id, p.Seat, "hand"));
                    // Distinguishable Life cards, so a reorder can be told from a replacement.
                    foreach (var id in new[] { "ST01-005", "ST01-006", "EB01-004", "EB01-005" })
                        p.Life.Add(Make(id, p.Seat, "life"));
                    for (int i = 0; i < 10; i++)
                        p.CostArea.Add(new DonInstance { InstanceId = $"{p.Seat}-lr-don-{serial++}", Rested = false });
                    p.DonDeck = 0;
                }
                S.CharacterArea[0] = Make("ST29-010", "south", "character");
                St.PendingEffects.Clear();
            }

            /// <summary>Queue the clause and answer whatever it raises, including the deck-look style
            /// re-arrange UI, which is how the engine surfaces "place them back in any order".</summary>
            public void Drive(string clause)
            {
                GameEngine.QueueClauseForTest(St, "south", S.CharacterArea[0], "main", clause);
                for (int i = 0; i < 10; i++)
                {
                    if (St.DeckLook != null)
                    {
                        // deckLookConfirmOrder is the real command — "deckLookDone" does not exist
                        // and was silently a no-op. Submit the order the look is already showing.
                        int before = St.EventLog.Count;
                        var order = St.DeckLook.Cards.Select(x => x.InstanceId).ToList();
                        St = GameEngine.ApplyCommand(St, new GameCommand
                        { Type = "deckLookConfirmOrder", Seat = "south", OrderedInstanceIds = order });
                        if (St.EventLog.Count == before)
                        {
                            St = GameEngine.ApplyCommand(St, new GameCommand
                            { Type = "deckLookScryConfirm", Seat = "south", OrderedInstanceIds = order });
                            if (St.EventLog.Count == before) break;
                        }
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
                if (Environment.GetEnvironmentVariable("OPT_DIAG") == "1")
                    foreach (var e in St.EventLog.TakeLast(6)) Console.WriteLine("      log: " + e.Message);
            }

            public System.Collections.Generic.IEnumerable<CardInstance> Everything()
            {
                foreach (var p in St.Players.Values)
                {
                    foreach (var x in p.Hand) yield return x;
                    foreach (var x in p.Life.AsEnumerable().Reverse()) yield return x;
                    foreach (var x in p.CharacterArea.Where(y => y != null)) yield return x;
                    if (p.Leader != null) yield return p.Leader;
                }
            }

            private CardInstance Make(string id, string owner, string zone) => new CardInstance
            {
                InstanceId = $"{owner}-{id}-lr-{serial++}",
                CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
