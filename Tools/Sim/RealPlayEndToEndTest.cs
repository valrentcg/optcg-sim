using System;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// "a host of other cards" — the phrase in the brief, taken literally.
    ///
    /// endtoend drives two cards (Wyper, Kalgara) through a REAL play. Everything else in the suite
    /// queues a clause directly, which skips the play itself: paying the card's own cost, the [On Play]
    /// dispatch, and the interaction between them. Five more cards go through the whole sequence here,
    /// chosen to span different cost/body pairings rather than to be similar:
    ///
    ///   EB01-056 Charlotte Flampe  cost: add from top-or-bottom of Life   body: Draw 1
    ///   OP01-011 Gordon            cost: place 1 from hand under the deck body: Draw 1
    ///   EB04-048 Rob Lucci         cost: trash 1 of your Characters       body: Draw 1
    ///   EB04-018 Megalo            cost: rest this Character              body: K.O. a rested Character
    ///   OP02-098 Koby              cost: trash 1 card from your hand      body: K.O. a Character
    ///
    /// The draw cards assert the hand's NET change, which is the honest number when the cost also moves
    /// hand cards — a body that draws 1 behind a cost that discards 1 leaves the hand where it started,
    /// and asserting "+1" there would be wrong.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- realplay
    /// </summary>
    public static class RealPlayEndToEndTest
    {
        private static int passed, failed;

        public static int Run()
        {
            Console.WriteLine("=== Five more cards, played for real ===");
            // (id, name, net hand change expected from cost+body, needs a K.O. victim)
            // +2, not +1: her COST adds a Life card to hand, and then the body draws. A cost that
            // GIVES you a card is easy to read as neutral — I wrote +1 twice before checking.
            DrawCard("EB01-056", "Charlotte Flampe", +2);
            DrawCard("OP01-011", "Gordon", 0);              // place 1 FROM HAND, draw 1 -> net 0
            DrawCard("EB04-048", "Rob Lucci", +1);          // trashes a CHARACTER, not a hand card
            KoCard("EB04-018", "Megalo", restedVictim: true);
            KoCard("OP02-098", "Koby", restedVictim: false);
            Console.WriteLine($"realplay: {passed}/{passed + failed} passed ({failed} failed)");
            return failed == 0 ? 0 : 1;
        }

        private static void Check(string label, bool ok, string detail = "")
        {
            if (ok) { passed++; Console.WriteLine("  PASS  " + label); }
            else { failed++; Console.WriteLine("  FAIL  " + label + (detail.Length > 0 ? "  -- " + detail : "")); }
        }

        /// <summary>Play the card for real, press Use, and answer whatever it asks — the cost may want a
        /// hand card, a board card or a Life card depending on the card.</summary>
        private static bool PlayAndUse(Board b, string cardId)
        {
            var inHand = b.Hand("south", cardId);
            // Play into a FREE slot. Hardcoding 0 silently failed for every case whose fixture had
            // already put a Character there — three of five "never reached the board", which looked
            // like an engine refusal and was an occupied slot.
            int slot = 0;
            for (int s = 0; s < 5; s++) if (b.S.CharacterArea[s] == null) { slot = s; break; }
            b.Apply(new GameCommand
            { Type = "playCard", Seat = "south", InstanceId = inHand.InstanceId, SlotIndex = slot });
            if (b.S.CharacterArea.All(c => c == null || c.CardId != cardId)) return false;

            // Ask the ENGINE which card is a legal target, exactly as the UI does, instead of
            // guessing a zone or brute-forcing. Guessing picked an opponent Character first, which
            // is illegal for a cost wanting a LIFE card (Flampe) or one of YOUR OWN Characters
            // (Rob Lucci). Brute-forcing was worse: ApplyCommand mutates the state in place, so
            // "try it and roll back" silently corrupted the board and broke two cases that had
            // been passing.
            for (int i = 0; i < 10; i++)
            {
                var pe = b.St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
                if (pe == null) break;
                string target = b.AllCards()
                    .Where(x => x.CardId != cardId)
                    .FirstOrDefault(x => GameEngine.IsValidEffectTarget(b.St, pe, x))?.InstanceId;
                int peBefore = b.St.PendingEffects.Count, logBefore = b.St.EventLog.Count;
                b.Apply(new GameCommand
                { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId, Target = target });
                if (b.St.PendingEffects.Count == peBefore && b.St.EventLog.Count == logBefore) break;
            }
            return true;
        }

        private static void DrawCard(string cardId, string name, int expectedNet)
        {
            var b = new Board();
            b.Hand("south", "ST29-004"); b.Hand("south", "ST29-009");   // spare cards for from-hand costs
            b.Character("south", "EB03-002");                            // a body for "trash 1 of your Characters"
            b.Character("north", "OP15-040");
            int hand0 = b.S.Hand.Count;

            if (!PlayAndUse(b, cardId))
            { Check($"{name} ({cardId}) real play", false, "the card never reached the board"); return; }

            // The played card leaves hand as part of playing it; count from after that.
            int net = b.S.Hand.Count - hand0;
            Check($"{name} ({cardId}): net hand change is {expectedNet:+#;-#;0} after cost and body",
                  net == expectedNet,
                  $"hand {hand0} -> {b.S.Hand.Count} (net {net:+#;-#;0}, want {expectedNet:+#;-#;0})");
        }

        private static void KoCard(string cardId, string name, bool restedVictim)
        {
            var b = new Board();
            b.Hand("south", "ST29-004"); b.Hand("south", "ST29-009");
            var victim = b.Character("north", "OP15-040");   // cost 1, small — legal under any ceiling
            if (restedVictim) victim.Rested = true;
            b.Character("north", "EB03-002");

            if (!PlayAndUse(b, cardId))
            { Check($"{name} ({cardId}) real play", false, "the card never reached the board"); return; }

            bool gone = b.N.CharacterArea.All(c => c == null || c.InstanceId != victim.InstanceId);
            Check($"{name} ({cardId}): the K.O. body actually removes a Character",
                  gone,
                  "the victim is still on the board — the cost was paid and the payoff never landed");
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
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "real-play" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "south"; St.TurnNumber = 8;
                foreach (var p in St.Players.Values)
                {
                    p.TurnsStarted = 4;
                    for (int i = 0; i < 5; i++) p.CharacterArea[i] = null;
                    p.Hand.Clear(); p.Life.Clear(); p.CostArea.Clear();
                    for (int i = 0; i < 5; i++) p.Life.Add(Card("ST01-005", p.Seat, "life"));
                    for (int i = 0; i < 10; i++)
                        p.CostArea.Add(new DonInstance { InstanceId = $"{p.Seat}-rp-don-{serial++}", Rested = false });
                    p.DonDeck = 0;
                }
                S.Trash.Add(Card("ST01-005", "south", "trash"));
                St.PendingEffects.Clear();
            }

            /// <summary>Every card the glow filter could be asked about.</summary>
            public System.Collections.Generic.IEnumerable<CardInstance> AllCards()
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

            public CardInstance Hand(string seat, string id)
            { var c = Card(id, seat, "hand"); St.Players[seat].Hand.Add(c); return c; }

            public CardInstance Character(string seat, string id)
            {
                var p = St.Players[seat];
                int slot = seat == "south" ? southSlot++ : northSlot++;
                if (slot > 4) return p.CharacterArea.First(x => x != null);
                var c = Card(id, seat, "character");
                p.CharacterArea[slot] = c;
                return c;
            }

            public void Apply(GameCommand c) => St = GameEngine.ApplyCommand(St, c);

            private CardInstance Card(string id, string owner, string zone) => new CardInstance
            {
                InstanceId = $"{owner}-{id}-rp-{serial++}",
                CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
