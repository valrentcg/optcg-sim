using System;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// Exact outcomes for the two largest body shapes in the "you may" pool — the third enumeration,
    /// after Life sentences (236 shapes) and costs (121). Bodies come to 161 shapes, and these two lead:
    ///
    ///   K.O. up to N of your opponent's Characters      54 clauses
    ///   Give up to N of your opponent's Characters -N   37 clauses
    ///
    /// Neither had exact verification. drawoutcome checks bodies that draw; endtoend checks two cards in
    /// full; everything else only asks whether *something* happened. A K.O. that removes the wrong
    /// Character, or a -N that applies the wrong number, produces an entirely plausible log.
    ///
    /// So each case names the victim and checks the specific card: the one targeted leaves, the one not
    /// targeted stays, and the power drop is exactly N — with a threshold case, since "K.O. with a cost
    /// of 4 or less" hitting a cost-5 body is the difference between a legal effect and a broken one.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- bodyoutcome
    /// </summary>
    public static class BodyOutcomeTest
    {
        private static int passed, failed;

        public static int Run()
        {
            Console.WriteLine("=== Exact outcomes: K.O. up to N, and give -N power ===");
            KoRemovesTheTargetedCharacterOnly();
            KoRespectsACostCeiling();
            MinusPowerAppliesExactlyN();
            MinusPowerHitsOnlyTheNamedTarget();
            KoUpToNeedsNoTargetToBeLegal();
            Console.WriteLine($"bodyoutcome: {passed}/{passed + failed} passed ({failed} failed)");
            return failed == 0 ? 0 : 1;
        }

        private static void Check(string label, bool ok, string detail = "")
        {
            if (ok) { passed++; Console.WriteLine("  PASS  " + label); }
            else { failed++; Console.WriteLine("  FAIL  " + label + (detail.Length > 0 ? "  -- " + detail : "")); }
        }

        private static void Resolve(Board b, string clause, string target)
        {
            GameEngine.QueueClauseForTest(b.St, "south", b.Character("south", "ST29-009"), "main", clause);
            for (int i = 0; i < 5; i++)
            {
                var pe = b.St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
                if (pe == null) break;
                b.Apply(new GameCommand
                { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId, Target = target });
            }
        }

        private static bool OnField(Board b, CardInstance c) =>
            b.N.CharacterArea.Any(x => x != null && x.InstanceId == c.InstanceId)
            || b.S.CharacterArea.Any(x => x != null && x.InstanceId == c.InstanceId);

        private static void KoRemovesTheTargetedCharacterOnly()
        {
            var b = new Board();
            var victim = b.Character("north", "OP15-040");    // cost 1
            var bystander = b.Character("north", "EB03-002"); // cost 5, also legal for an uncapped K.O.

            Resolve(b, "K.O. up to 1 of your opponent's Characters.", victim.InstanceId);

            bool victimGone = !OnField(b, victim) && b.N.Trash.Any(c => c.InstanceId == victim.InstanceId);
            bool bystanderSafe = OnField(b, bystander);
            Check("K.O. removes the NAMED Character and leaves the other alone",
                  victimGone && bystanderSafe,
                  $"victimGone={victimGone} bystanderStillThere={bystanderSafe} "
                  + "— \"up to 1\" must take one, not everything legal");
        }

        /// <summary>"with a cost of N or less" is the qualifier most of the 54 carry. A K.O. that ignores
        /// it removes bodies the card was never allowed to touch.</summary>
        /// <summary>"with a cost of N or less" is the qualifier most of the 54 carry. A K.O. that
        /// ignores it removes bodies the card was never allowed to touch.
        ///
        /// Both halves are needed. Checking only that the big Character survives would pass just as
        /// well against an engine whose K.O. does nothing at all, so the same capped clause is also
        /// pointed at a legal victim and must take it.</summary>
        private static void KoRespectsACostCeiling()
        {
            const string CAPPED = "K.O. up to 1 of your opponent's Characters with a cost of 2 or less.";

            var over = new Board();
            var tooBig = over.Character("north", "EB03-002");   // cost 5, above the ceiling
            Resolve(over, CAPPED, tooBig.InstanceId);
            bool survived = OnField(over, tooBig);

            var under = new Board();
            var small = under.Character("north", "OP15-040");   // cost 1, under the ceiling
            Resolve(under, CAPPED, small.InstanceId);
            bool taken = !OnField(under, small);

            Check("a cost ceiling is enforced: cost-5 survives it, cost-1 does not",
                  survived && taken,
                  $"cost5Survived={survived} cost1Taken={taken}"
                  + (survived && !taken ? " — the clause K.O.s nothing at all, so the ceiling proves nothing" : ""));
        }

        private static void MinusPowerAppliesExactlyN()
        {
            var b = new Board();
            var target = b.Character("north", "EB03-002");    // vanilla 6000
            int before = GameEngine.GetPower(b.St, target);

            Resolve(b, "Give up to 1 of your opponent's Characters -2000 power during this turn.", target.InstanceId);

            int after = GameEngine.GetPower(b.St, target);
            Check("-2000 power drops the target by EXACTLY 2000",
                  before - after == 2000,
                  $"power {before} -> {after} (want -2000, got -{before - after})");
        }

        private static void MinusPowerHitsOnlyTheNamedTarget()
        {
            var b = new Board();
            var target = b.Character("north", "EB03-002");
            var other = b.Character("north", "OP15-040");
            int otherBefore = GameEngine.GetPower(b.St, other);

            Resolve(b, "Give up to 1 of your opponent's Characters -2000 power during this turn.", target.InstanceId);

            Check("-N power touches only the named Character",
                  GameEngine.GetPower(b.St, other) == otherBefore,
                  $"the bystander moved {otherBefore} -> {GameEngine.GetPower(b.St, other)}");
        }

        /// <summary>"Up to N" with nothing legal must be a no-op, not a crash and not a retirement that
        /// swallows a cost. This is the counterpart to the up-to-N fix made earlier this session.</summary>
        private static void KoUpToNeedsNoTargetToBeLegal()
        {
            var b = new Board();          // north has no Characters at all
            bool threw = false;
            try { Resolve(b, "K.O. up to 1 of your opponent's Characters.", null); }
            catch (Exception) { threw = true; }

            Check("\"K.O. up to 1\" with no opponent Characters is a clean no-op",
                  !threw && b.N.CharacterArea.All(x => x == null),
                  threw ? "it threw" : "something appeared on an empty board");
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
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "body-outcome" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "south"; St.TurnNumber = 8;
                foreach (var p in St.Players.Values)
                {
                    p.TurnsStarted = 4;
                    for (int i = 0; i < 5; i++) p.CharacterArea[i] = null;
                    p.Hand.Clear(); p.Life.Clear(); p.CostArea.Clear();
                    for (int i = 0; i < 4; i++) p.Life.Add(Card("ST01-005", p.Seat, "life"));
                    for (int i = 0; i < 10; i++)
                        p.CostArea.Add(new DonInstance { InstanceId = $"{p.Seat}-bo-don-{serial++}", Rested = false });
                    p.DonDeck = 0;
                }
                St.PendingEffects.Clear();
            }

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
                InstanceId = $"{owner}-{id}-bo-{serial++}",
                CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
