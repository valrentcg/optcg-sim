using System;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// Reported from playtest: OP15-114 Wyper's [On Play] "needs some love".
    ///
    ///   "[On Play] You may turn 1 card from the top of your Life cards face-up: Give all of your
    ///    opponent's Characters −2000 power during this turn. Then, K.O. all of your opponent's
    ///    Characters with 0 power or less."
    ///
    /// Three separate mechanisms, each with its own way of failing silently, so each is checked on its
    /// own: the optional Life-face-up COST, the give-ALL debuff (a "give all" resolved as a single-target
    /// pick hits one Character and looks like the card is broken), and the ". Then," K.O.-all sweep whose
    /// power filter only makes sense AFTER the debuff has been applied.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- wypertest
    /// </summary>
    public static class WyperOnPlayTest
    {
        private static int passed;
        private static int failed;

        public static int Run()
        {
            Console.WriteLine("=== OP15-114 Wyper [On Play] ===");
            WyperDebuffsEveryoneAndSweepsTheDead();
            WyperCanBeDeclined();
            EveryKoAllSweepInThePoolAutoResolves();
            Console.WriteLine($"wypertest: {passed}/{passed + failed} passed ({failed} failed)");
            return failed == 0 ? 0 : 1;
        }

        private static void WyperDebuffsEveryoneAndSweepsTheDead()
        {
            var b = new Board();
            b.Life("south", 3); b.Life("north", 3);
            b.Don("south", 5);
            // Powers are read from the library rather than assumed — the first cut of this test guessed
            // ST01-005 was 2000 (it is 5000), which turned a passing sweep into a phantom failure.
            // Both victims must be free of K.O.-replacement text or they protect themselves and the sweep
            // reads as broken when it is not: the first cut used ST29-008 Nami, whose "you may turn 1 Life
            // face-up instead" saved BOTH bodies. Blocker/On-Play text cannot interfere with a K.O.
            var weak = b.Character("north", "ST29-009");    // 2000 → 0 → swept ([Blocker] only)
            var tiny = b.Character("north", "OP15-040");    // 2000 → 0 → swept ([On Play] only)
            var big = b.Character("north", "ST29-010");     // 6000 → 4000 → lives (vanilla)
            int weakP = GameEngine.GetPower(b.St, weak), tinyP = GameEngine.GetPower(b.St, tiny),
                bigP = GameEngine.GetPower(b.St, big);
            Console.WriteLine($"    board powers: weak={weakP} tiny={tinyP} big={bigP}");
            Check("fixture is valid: two Characters fall to 0 or less, one does not",
                weakP - 2000 <= 0 && tinyP - 2000 <= 0 && bigP - 2000 > 0,
                $"{weakP}/{tinyP}/{bigP} after −2000 → {weakP - 2000}/{tinyP - 2000}/{bigP - 2000}");
            var wyper = b.Hand("south", "OP15-114");
            int faceUpBefore = b.S.Life.Count(l => l.FaceUp);

            b.Apply(new GameCommand { Type = "playCard", Seat = "south", InstanceId = wyper.InstanceId });

            var pe = b.St.PendingEffects.FirstOrDefault(e => e.SourceCardId == "OP15-114");
            Console.WriteLine($"    pending={b.St.PendingEffects.Count} :: {pe?.Text}");
            Check("Wyper's [On Play] is offered", pe != null,
                $"pending={b.St.PendingEffects.Count}");
            if (pe == null) return;

            b.Resolve(pe, null);        // "Use Effect" — pay the cost, run the body
            foreach (var l in b.St.EventLog.Skip(1)) Console.WriteLine("    log| " + l.Message);

            int faceUpAfter = b.S.Life.Count(l => l.FaceUp);
            Check("the cost turns exactly 1 of MY Life cards face-up",
                faceUpAfter == faceUpBefore + 1 && b.S.Life.Count == 3,
                $"faceUp {faceUpBefore}->{faceUpAfter}, life={b.S.Life.Count}");

            // The sweep must land on the SAME "Use Effect" that paid the cost — no second click. It is a
            // sweep, so there is no target to pick; a leftover pending here is the reported bug.
            Check("the sweep does not leave a stray pending effect behind",
                b.St.PendingEffects.Count == 0,
                $"leftover :: {b.St.PendingEffects.FirstOrDefault()?.Text}");

            bool weakGone = !b.N.CharacterArea.Any(c => c != null && c.InstanceId == weak.InstanceId);
            bool tinyGone = !b.N.CharacterArea.Any(c => c != null && c.InstanceId == tiny.InstanceId);
            var bigLeft = b.N.CharacterArea.FirstOrDefault(c => c != null && c.InstanceId == big.InstanceId);

            Check("every opponent Character takes the −2000, not just one",
                bigLeft != null && GameEngine.GetPower(b.St, bigLeft) == bigP - 2000,
                bigLeft == null ? "the surviving Character was wrongly K.O.'d"
                                : $"survivor power={GameEngine.GetPower(b.St, bigLeft)} want {bigP - 2000}");
            Check("the ». Then« sweep K.O.s everything the debuff left at 0 or less",
                weakGone && tinyGone, $"victim1 gone={weakGone} victim2 gone={tinyGone}");
            Check("…and spares the one still above 0", bigLeft != null);
        }

        // It is a "You may" cost: declining must cost no Life and leave the board untouched.
        private static void WyperCanBeDeclined()
        {
            var b = new Board();
            b.Life("south", 3); b.Life("north", 3);
            b.Don("south", 5);
            var victim = b.Character("north", "ST01-005");
            int victimP = GameEngine.GetPower(b.St, victim);
            var wyper = b.Hand("south", "OP15-114");
            int faceUpBefore = b.S.Life.Count(l => l.FaceUp);

            b.Apply(new GameCommand { Type = "playCard", Seat = "south", InstanceId = wyper.InstanceId });
            var pe = b.St.PendingEffects.FirstOrDefault(e => e.SourceCardId == "OP15-114");
            if (pe == null) { Check("Wyper's [On Play] can be declined", false, "no pending effect"); return; }
            b.Apply(new GameCommand { Type = "passEffect", Seat = "south", EffectId = pe.EffectId });

            var still = b.N.CharacterArea.FirstOrDefault(c => c != null && c.InstanceId == victim.InstanceId);
            Check("declining costs no Life and leaves the board alone",
                b.S.Life.Count(l => l.FaceUp) == faceUpBefore && still != null
                    && GameEngine.GetPower(b.St, still) == victimP,
                $"faceUp={b.S.Life.Count(l => l.FaceUp)} (was {faceUpBefore}), "
                + $"victim={(still == null ? "K.O.'d" : GameEngine.GetPower(b.St, still).ToString())}");
        }

        // The fix was to the recognition gate, not to Wyper, so every card carrying a K.O.-all sweep is
        // checked. Five of the six are real board wipes (Kaido, Birdcage, The Ark Maxim, Kaido & Linlin,
        // Shanks) — the same silent stall on any of those is a game-deciding effect quietly not happening.
        private static void EveryKoAllSweepInThePoolAutoResolves()
        {
            var koAll = new System.Text.RegularExpressions.Regex(@"K\.O\. all\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var carriers = CardData.Library.Values
                .Where(c => c != null && !string.IsNullOrEmpty(c.Effect) && koAll.IsMatch(c.Effect))
                .GroupBy(c => c.Id).Select(g => g.First())
                .OrderBy(c => c.Id, StringComparer.Ordinal).ToList();

            var stalled = new System.Collections.Generic.List<string>();
            int checkedCount = 0;
            foreach (var def in carriers)
            {
                string clause = def.Effect
                    .Split('\n')
                    .SelectMany(l => System.Text.RegularExpressions.Regex.Split(l, @"(?<=\.)\s*(?:Then|After that),\s*"))
                    .FirstOrDefault(x => koAll.IsMatch(x));
                if (string.IsNullOrWhiteSpace(clause)) continue;
                // Only the sweep itself is under test — a leading "You may <cost>:" is a separate decision
                // that is SUPPOSED to wait, so strip any cost prefix and skip clauses that stay optional.
                clause = System.Text.RegularExpressions.Regex.Replace(clause, @"^.*?:\s*", "",
                    System.Text.RegularExpressions.RegexOptions.Singleline).Trim();
                if (clause.IndexOf("you may", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                checkedCount++;

                var b = new Board();
                b.Life("south", 3); b.Life("north", 3);
                var src = b.Character("south", "ST29-010");
                b.Character("north", "ST29-009"); b.Character("north", "OP15-040"); b.Character("north", "ST29-010");
                GameEngine.QueueClauseForTest(b.St, "south", src, "main", clause);
                if (b.St.PendingEffects.Count > 0)
                    stalled.Add($"{def.Id} {def.Name} :: {clause.Substring(0, Math.Min(70, clause.Length))}");
            }

            Console.WriteLine($"    {carriers.Count} cards carry a K.O.-all sweep; {checkedCount} testable without a cost prompt");
            Check("every K.O.-all sweep in the pool resolves without waiting for a click",
                stalled.Count == 0, stalled.Count == 0 ? null : string.Join(" | ", stalled));
        }

        // ---- plumbing ---------------------------------------------------------------------------

        private static void Check(string name, bool ok, string detail = null)
        {
            if (ok) { passed++; Console.WriteLine("  PASS  " + name); }
            else { failed++; Console.WriteLine("  FAIL  " + name + (string.IsNullOrEmpty(detail) ? "" : " — " + detail)); }
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
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "wyper-onplay" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "south"; St.TurnNumber = 8;
                S.TurnsStarted = 4; N.TurnsStarted = 4;
                for (int i = 0; i < 5; i++) { S.CharacterArea[i] = null; N.CharacterArea[i] = null; }
                S.Hand.Clear(); N.Hand.Clear();
                S.Life.Clear(); N.Life.Clear();
                S.CostArea.Clear(); N.CostArea.Clear();
                S.DonDeck = 10; N.DonDeck = 10;
                S.Leader.Rested = false; N.Leader.Rested = false;
                S.Leader.PlayedOnTurn = 0; N.Leader.PlayedOnTurn = 0;
                S.Leader.AttachedDonIds.Clear(); N.Leader.AttachedDonIds.Clear();
                St.PendingEffects.Clear();
            }

            public CardInstance Character(string seat, string id)
            {
                var p = seat == "south" ? S : N;
                var c = Card(id, seat, "character");
                p.CharacterArea[seat == "south" ? southSlot++ : northSlot++] = c;
                return c;
            }

            public CardInstance Hand(string seat, string id)
            {
                var c = Card(id, seat, "hand");
                (seat == "south" ? S : N).Hand.Add(c);
                return c;
            }

            public void Life(string seat, int n)
            {
                var p = seat == "south" ? S : N;
                for (int i = 0; i < n; i++) p.Life.Add(Card("ST01-005", seat, "life"));
            }

            public void Don(string seat, int count)
            {
                var p = seat == "south" ? S : N;
                for (int i = 0; i < count; i++)
                    p.CostArea.Add(new DonInstance { InstanceId = $"{seat}-wy-don-{serial++}", Rested = false });
                p.DonDeck = Math.Max(0, p.DonDeck - count);
            }

            public void Resolve(PendingEffect e, string target) => Apply(new GameCommand
            { Type = "resolveEffect", Seat = e.Seat, EffectId = e.EffectId, Target = target });

            public void Apply(GameCommand c) => St = GameEngine.ApplyCommand(St, c);

            private CardInstance Card(string id, string owner, string zone) => new CardInstance
            {
                InstanceId = $"{owner}-{id}-wy-{serial++}",
                CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
