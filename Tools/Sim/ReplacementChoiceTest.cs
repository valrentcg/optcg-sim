using System;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// 63 cards carry a "you may &lt;pay something&gt; instead" protection, and the engine has always paid
    /// the first payable one automatically. Usually that is what you want; sometimes it is not, because
    /// the price is a Life card turned face-up, a card out of hand, DON!! rested, or the guard itself.
    /// The card says "you may" and the player was never asked.
    ///
    /// The mechanism here lets a seat OPT IN to being asked. It is off by default, so the behaviour
    /// everyone has today is untouched, and the bot is never asked.
    ///
    /// Design notes worth keeping:
    ///  · The question is an ordinary optional PendingEffect owned by the VICTIM'S seat, so it renders
    ///    with the existing Use/Skip panel and LegalActions already offers a bot both answers — no new
    ///    decision type, no new client surface, no risk of a seat being handed a decision it cannot make.
    ///  · Scoped to K.O.s. A postponed removal is replayed with MoveToTrash, which is faithful for a
    ///    K.O. and NOT for a bounce or a deck-place, so those callers keep paying immediately.
    ///  · A postponement is a delay, never an escape: if the question disappears unanswered, the removal
    ///    still happens.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- replacementchoice
    /// </summary>
    public static class ReplacementChoiceTest
    {
        private static int passed;
        private static int failed;

        public static int Run()
        {
            Console.WriteLine("=== \"You may … instead\": ask before spending my resources ===");
            OffByDefaultTheProtectionStillAutoPays();
            OptedInThePlayerIsAskedAndCanDecline();
            OptedInThePlayerCanAcceptAndKeepTheCard();
            AnUnansweredQuestionStillKillsTheCard();
            Console.WriteLine($"replacementchoice: {passed}/{passed + failed} passed ({failed} failed)");
            return failed == 0 ? 0 : 1;
        }

        // ST29-008 Nami: "If your {Egghead} type Character would be K.O.'d by your opponent's effect,
        // you may turn 1 card from the top of your Life cards face-up instead."
        private static Board Fixture(bool optIn)
        {
            var b = new Board();
            b.N.PromptForReplacements = optIn;          // north is the victim's side
            b.Character("north", "ST29-008");           // Nami, the guard ({Egghead})
            b.Character("north", "ST29-009");           // Nico Robin, {Egghead} — the victim
            b.Life("north", 3); b.Life("south", 3);
            b.Character("south", "ST29-010");
            return b;
        }

        private static void OffByDefaultTheProtectionStillAutoPays()
        {
            var b = Fixture(optIn: false);
            var victim = b.N.CharacterArea.First(c => c != null && c.CardId == "ST29-009");
            int faceUpBefore = b.N.Life.Count(l => l.FaceUp);
            b.KoViaEffect(victim);

            // Nami guards every {Egghead} Character INCLUDING herself, so the sweep produces two
            // protected victims and two payments. That is the honest shape of the mechanic, so the test
            // asserts it rather than contriving a single victim.
            Check("off by default: the protection still pays itself, exactly as before",
                b.N.CharacterArea.Any(c => c != null && c.InstanceId == victim.InstanceId)
                    && b.N.Life.Count(l => l.FaceUp) == faceUpBefore + 2
                    && b.St.PendingEffects.Count == 0,
                $"onBoard={b.N.CharacterArea.Any(c => c != null && c.InstanceId == victim.InstanceId)} "
                + $"faceUp {faceUpBefore}->{b.N.Life.Count(l => l.FaceUp)} pending={b.St.PendingEffects.Count}");
        }

        private static void OptedInThePlayerIsAskedAndCanDecline()
        {
            var b = Fixture(optIn: true);
            var victim = b.N.CharacterArea.First(c => c != null && c.CardId == "ST29-009");
            int faceUpBefore = b.N.Life.Count(l => l.FaceUp);
            b.KoViaEffect(victim);

            var q = b.St.PendingEffects.FirstOrDefault();
            Check("opted in: the victim's own seat is asked", q != null && q.Seat == "north" && q.Optional,
                $"pending={b.St.PendingEffects.Count} seat={q?.Seat} optional={q?.Optional}");
            if (q == null) return;
            Check("nothing is spent while the question is open",
                b.N.Life.Count(l => l.FaceUp) == faceUpBefore
                    && b.N.CharacterArea.Any(c => c != null && c.InstanceId == victim.InstanceId));

            // One question per protected victim — answer them all.
            while (b.St.PendingEffects.FirstOrDefault(e => e.Timing == "removalChoice") is PendingEffect open)
                b.Apply(new GameCommand { Type = "passEffect", Seat = open.Seat, EffectId = open.EffectId });
            Check("declining lets the K.O. happen and costs no Life",
                !b.N.CharacterArea.Any(c => c != null && c.InstanceId == victim.InstanceId)
                    && b.N.Trash.Any(c => c.InstanceId == victim.InstanceId)
                    && b.N.Life.Count(l => l.FaceUp) == faceUpBefore,
                $"trashed={b.N.Trash.Any(c => c.InstanceId == victim.InstanceId)} "
                + $"faceUp {faceUpBefore}->{b.N.Life.Count(l => l.FaceUp)}");
        }

        private static void OptedInThePlayerCanAcceptAndKeepTheCard()
        {
            var b = Fixture(optIn: true);
            var victim = b.N.CharacterArea.First(c => c != null && c.CardId == "ST29-009");
            int faceUpBefore = b.N.Life.Count(l => l.FaceUp);
            b.KoViaEffect(victim);

            var q = b.St.PendingEffects.FirstOrDefault();
            if (q == null) { Check("accepting pays and saves the Character", false, "no question was asked"); return; }
            while (b.St.PendingEffects.FirstOrDefault(e => e.Timing == "removalChoice") is PendingEffect open)
                b.Apply(new GameCommand { Type = "resolveEffect", Seat = open.Seat, EffectId = open.EffectId });

            Check("accepting pays the price and the Character survives",
                b.N.CharacterArea.Any(c => c != null && c.InstanceId == victim.InstanceId)
                    && b.N.Life.Count(l => l.FaceUp) == faceUpBefore + 2
                    && b.St.PendingEffects.Count == 0,
                $"onBoard={b.N.CharacterArea.Any(c => c != null && c.InstanceId == victim.InstanceId)} "
                + $"faceUp {faceUpBefore}->{b.N.Life.Count(l => l.FaceUp)} pending={b.St.PendingEffects.Count}");
        }

        // The failure mode that would make opting in strictly better than not: the question disappears
        // and the card quietly survives having paid nothing.
        private static void AnUnansweredQuestionStillKillsTheCard()
        {
            var b = Fixture(optIn: true);
            var victim = b.N.CharacterArea.First(c => c != null && c.CardId == "ST29-009");
            b.KoViaEffect(victim);
            if (b.St.PendingEffects.Count == 0) { Check("an unanswered question still kills the card", false, "no question"); return; }

            b.St.PendingEffects.Clear();                 // the question is lost, however that happens
            b.Apply(new GameCommand { Type = "endTurn", Seat = "south" });

            Check("an unanswered question still kills the card — a delay, not an escape",
                !b.N.CharacterArea.Any(c => c != null && c.InstanceId == victim.InstanceId),
                "the Character survived without anyone paying for it");
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
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "replacement-choice" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "south"; St.TurnNumber = 8;
                S.TurnsStarted = 4; N.TurnsStarted = 4;
                for (int i = 0; i < 5; i++) { S.CharacterArea[i] = null; N.CharacterArea[i] = null; }
                S.Hand.Clear(); N.Hand.Clear();
                S.Life.Clear(); N.Life.Clear();
                S.CostArea.Clear(); N.CostArea.Clear();
                S.DonDeck = 10; N.DonDeck = 10;
                S.Leader.Rested = false; N.Leader.Rested = false;
                St.PendingEffects.Clear();
                for (int i = 0; i < 8; i++)
                    S.CostArea.Add(new DonInstance { InstanceId = $"s-rc-don-{serial++}", Rested = false });
            }

            /// <summary>K.O. a Character by EFFECT (not battle) — the trigger these protections are
            /// written against — through the real resolver, via a K.O.-all sweep that hits only it.</summary>
            public void KoViaEffect(CardInstance victim)
            {
                var src = S.CharacterArea.FirstOrDefault(c => c != null) ?? S.Leader;
                GameEngine.QueueClauseForTest(St, "south", src, "main",
                    "K.O. all of your opponent's Characters with 3000 power or less.");
            }

            public CardInstance Character(string seat, string id)
            {
                var p = seat == "south" ? S : N;
                var c = Card(id, seat, "character");
                p.CharacterArea[seat == "south" ? southSlot++ : northSlot++] = c;
                return c;
            }

            public void Life(string seat, int n)
            {
                var p = seat == "south" ? S : N;
                for (int i = 0; i < n; i++) p.Life.Add(Card("ST01-005", seat, "life"));
            }

            public void Apply(GameCommand c) => St = GameEngine.ApplyCommand(St, c);

            private CardInstance Card(string id, string owner, string zone) => new CardInstance
            {
                InstanceId = $"{owner}-{id}-rc-{serial++}",
                CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
