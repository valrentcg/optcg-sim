using System;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// "[DON!! xN]" is a GATE, not a trigger: the ability exists only while N or more DON!! cards are
    /// attached to that card. 42 "you may" clauses sit behind one ([DON!! x1] 34, [DON!! x2] 8), and
    /// timingsweep never drove either — it dispatches on the timing tag that FOLLOWS the gate, so a
    /// card whose gate is shut looks identical to one with no effect at all.
    ///
    /// Both directions matter and neither is sufficient alone. A gate stuck OPEN hands the player an
    /// ability they have not paid the DON!! for; a gate stuck SHUT means the card they invested DON!!
    /// in does nothing. Testing only the open case would pass against an engine that ignores the gate
    /// entirely, which is the more likely failure for a substring interpreter.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- donthreshold
    /// </summary>
    public static class DonThresholdTest
    {
        private static int passed, failed;

        // OP01-015 Tony Tony.Chopper: "[DON!! x1] [When Attacking] You may trash 1 card from your
        // hand: Add up to 1 {Straw Hat Crew} type Character card ... to your hand."
        private const string GatedCard = "OP01-015";

        public static int Run()
        {
            Console.WriteLine("=== [DON!! xN]: the gate must open only when the DON!! are there ===");
            ClosedGateOffersNothing();
            OpenGateOffersTheDecision();
            TwoDonGateNeedsTwo();
            Console.WriteLine($"donthreshold: {passed}/{passed + failed} passed ({failed} failed)");
            return failed == 0 ? 0 : 1;
        }

        private static void Check(string label, bool ok, string detail = "")
        {
            if (ok) { passed++; Console.WriteLine("  PASS  " + label); }
            else { failed++; Console.WriteLine("  FAIL  " + label + (detail.Length > 0 ? "  -- " + detail : "")); }
        }

        /// <summary>Attack with the gated card carrying <paramref name="attached"/> DON!!, and report
        /// whether the player ended up with a decision.</summary>
        private static bool OffersWith(int attached, string cardId = GatedCard)
        {
            var b = new Board();
            var atk = b.Character("south", cardId);
            atk.Rested = false; atk.PlayedOnTurn = 0;
            for (int i = 0; i < attached; i++)
            {
                var don = b.S.CostArea.FirstOrDefault(d => !d.Rested && !b.Attached.Contains(d.InstanceId));
                if (don == null) break;
                atk.AttachedDonIds.Add(don.InstanceId);
                b.Attached.Add(don.InstanceId);
            }
            b.Hand("south", "ST29-004");          // something to pay the from-hand cost with
            b.St.ActiveSeat = "south";
            b.Apply(new GameCommand
            { Type = "declareAttack", Seat = "south", Attacker = atk.InstanceId, Target = b.N.Leader?.InstanceId });
            return b.St.PendingEffects.Any(e => e != null && e.Seat == "south");
        }

        private static void ClosedGateOffersNothing()
        {
            Check("[DON!! x1] with NO DON!! attached offers nothing",
                  !OffersWith(0),
                  "the ability was offered without the DON!! it is gated behind");
        }

        private static void OpenGateOffersTheDecision()
        {
            Check("[DON!! x1] with 1 DON!! attached offers the decision",
                  OffersWith(1),
                  "the DON!! were attached and the ability still did not appear");
        }

        /// <summary>A [DON!! x2] card must not open on one. Without this, "attached > 0" would pass
        /// both cases above while ignoring the actual number.</summary>
        private static void TwoDonGateNeedsTwo()
        {
            string two = null;
            foreach (var d in CardData.Library.Values)
            {
                if (d == null || string.IsNullOrEmpty(d.Effect)) continue;
                if (!string.Equals(d.Type, "character", StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var line in d.Effect.Split((char)10))
                {
                    var s = line.Trim();
                    if (s.StartsWith("[DON!! x2]") && s.IndexOf("When Attacking", StringComparison.OrdinalIgnoreCase) >= 0
                        && s.IndexOf("you may", StringComparison.OrdinalIgnoreCase) >= 0)
                    { two = d.Id; break; }
                }
                if (two != null) break;
            }
            if (two == null) { Check("[DON!! x2] needs two", false, "no [DON!! x2] When-Attacking you-may card in the pool"); return; }

            bool one = OffersWith(1, two);
            bool both = OffersWith(2, two);
            Check($"[DON!! x2] ({two}) stays shut on 1 DON!! and opens on 2",
                  !one && both,
                  $"offeredWith1={one} offeredWith2={both}");
        }

        private sealed class Board
        {
            public GameState St;
            public PlayerState S => St.Players["south"];
            public PlayerState N => St.Players["north"];
            public readonly System.Collections.Generic.HashSet<string> Attached = new System.Collections.Generic.HashSet<string>();
            private int southSlot, northSlot, serial;

            public Board()
            {
                St = GameEngine.CreateMatch(new MatchConfig
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "don-threshold" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "south"; St.TurnNumber = 8;
                foreach (var p in St.Players.Values)
                {
                    p.TurnsStarted = 4;
                    for (int i = 0; i < 5; i++) p.CharacterArea[i] = null;
                    p.Hand.Clear(); p.Life.Clear(); p.CostArea.Clear();
                    for (int i = 0; i < 4; i++) p.Life.Add(Card("ST01-005", p.Seat, "life"));
                    for (int i = 0; i < 10; i++)
                        p.CostArea.Add(new DonInstance { InstanceId = $"{p.Seat}-dt-don-{serial++}", Rested = false });
                    p.DonDeck = 0;
                }
                St.PendingEffects.Clear();
            }

            public CardInstance Hand(string seat, string id)
            { var c = Card(id, seat, "hand"); St.Players[seat].Hand.Add(c); return c; }

            public CardInstance Character(string seat, string id)
            {
                var p = St.Players[seat];
                int slot = seat == "south" ? southSlot++ : northSlot++;
                var c = Card(id, seat, "character");
                p.CharacterArea[slot] = c;
                return c;
            }

            public void Apply(GameCommand c) => St = GameEngine.ApplyCommand(St, c);

            private CardInstance Card(string id, string owner, string zone) => new CardInstance
            {
                InstanceId = $"{owner}-{id}-dt-{serial++}",
                CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
