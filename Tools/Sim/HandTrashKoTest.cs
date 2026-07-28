using System;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// Reported from playtest: the ST29-001 Monkey.D.Luffy Leader's "[When Attacking] If you have 2 or
    /// less Life cards, draw 1 card and trash 1 card from your hand" trashed a card WITHOUT letting the
    /// player choose which — and the card it chose (EB03-053 Nami) then fired her [On K.O.], flipping a
    /// Life card, even though a card trashed from HAND is never K.O.'d.
    ///
    /// Comprehensive Rules: a K.O. is a Character leaving the FIELD. Discarding from hand is not a K.O.,
    /// so nothing printed on the discarded card should trigger.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- handtrashtest
    /// </summary>
    public static class HandTrashKoTest
    {
        private static int passed;
        private static int failed;

        public static int Run()
        {
            Console.WriteLine("=== Hand-trash: player choice, and no [On K.O.] leak ===");
            LeaderHandTrashLetsThePlayerChoose();
            TrashingFromHandDoesNotFireOnKo();
            ZoroTriggerCostRespectsTheFilterAndAsks();
            NoOnKoCardInThePoolFiresWhenDiscarded();
            Console.WriteLine($"handtrashtest: {passed}/{passed + failed} passed ({failed} failed)");
            return failed == 0 ? 0 : 1;
        }

        private static void LeaderHandTrashLetsThePlayerChoose()
        {
            var b = new Board();
            b.SetLeader("south", "ST29-001");
            b.Life("south", 2);                     // "if you have 2 or less Life cards"
            b.Life("north", 3);
            var a = b.Hand("south", "ST29-004");
            var c = b.Hand("south", "ST29-009");
            b.Character("north", "EB01-017");       // blocker: keeps the battle alive
            int handBefore = b.S.Hand.Count;

            b.Attack(b.S.Leader, b.N.Leader);

            // The draw is automatic; the TRASH is a choice. After the swing the hand should be
            // handBefore + 1 (the draw) with nothing trashed yet, and a pending effect waiting.
            var pe = b.St.PendingEffects.FirstOrDefault();
            Console.WriteLine($"    hand {handBefore} -> {b.S.Hand.Count}, trash={b.S.Trash.Count}, "
                + $"pending={b.St.PendingEffects.Count} :: {pe?.Text}");
            foreach (var l in b.St.EventLog.Skip(2)) Console.WriteLine("    log| " + l.Message);

            Check("the leader's hand-trash waits for the player to pick",
                b.S.Trash.Count == 0 && pe != null,
                $"trash={b.S.Trash.Count} pending={b.St.PendingEffects.Count}");
            if (pe == null) return;
            Check("both hand cards are offered as the trash target",
                GameEngine.IsValidEffectTarget(b.St, pe, a) && GameEngine.IsValidEffectTarget(b.St, pe, c),
                $"a={GameEngine.IsValidEffectTarget(b.St, pe, a)} c={GameEngine.IsValidEffectTarget(b.St, pe, c)}");
        }

        // The headline rules bug: a card trashed FROM HAND is not K.O.'d, so its [On K.O.] must not fire.
        private static void TrashingFromHandDoesNotFireOnKo()
        {
            var b = new Board();
            b.SetLeader("south", "ST29-001");
            b.Life("south", 2);
            b.Life("north", 3);
            var nami = b.Hand("south", "EB03-053");   // [On K.O.] turn a Life face-up: play a Character
            b.Hand("south", "ST29-004");
            b.Character("north", "EB01-017");
            int lifeBefore = b.S.Life.Count;
            int faceUpBefore = b.S.Life.Count(l => l.FaceUp);

            b.Attack(b.S.Leader, b.N.Leader);
            var pe = b.St.PendingEffects.FirstOrDefault();
            if (pe != null) b.Resolve(pe, nami.InstanceId);       // choose Nami as the discard

            bool namiInTrash = b.S.Trash.Any(x => x.InstanceId == nami.InstanceId);
            bool koPending = b.St.PendingEffects.Any(e => e.SourceCardId == "EB03-053");
            int faceUpAfter = b.S.Life.Count(l => l.FaceUp);
            foreach (var l in b.St.EventLog.Skip(2)) Console.WriteLine("    log| " + l.Message);

            Check("the chosen card is the one that goes to the trash", namiInTrash,
                $"inTrash={namiInTrash} trash=[{string.Join(",", b.S.Trash.Select(x => x.CardId))}]");
            Check("a card trashed from HAND does not fire its [On K.O.]", !koPending,
                "an [On K.O.] effect queued for a card that was discarded from hand");
            Check("…and no Life card is flipped face-up by it",
                b.S.Life.Count == lifeBefore && faceUpAfter == faceUpBefore,
                $"life {lifeBefore}->{b.S.Life.Count}, faceUp {faceUpBefore}->{faceUpAfter}");
        }

        // The other ST29 card that trashes from hand, and the one whose cost carries a FILTER:
        // ST29-014 Roronoa Zoro, "[Activate: Main] [Once Per Turn] You may trash 1 card WITH A [Trigger]
        // from your hand: Draw 1 card and give up to 1 rested DON!! card to your Leader." A cost that is
        // auto-paid ignores both the player's choice AND the filter, which is the reported shape: a card
        // vanished from hand without a prompt, and it was a card that could never have paid (EB03-053
        // Nami has no [Trigger] at all).
        private static void ZoroTriggerCostRespectsTheFilterAndAsks()
        {
            var b = new Board();
            b.SetLeader("south", "ST29-001");
            var zoro = b.Character("south", "ST29-014");
            var nami = b.Hand("south", "EB03-053");      // NO [Trigger] — must not be payable
            var trig = b.Hand("south", "EB02-018");      // HAS a [Trigger] — the only legal payment
            int handBefore = b.S.Hand.Count;

            b.Apply(new GameCommand { Type = "activateMain", Seat = "south", Target = zoro.InstanceId });

            var pe = b.St.PendingEffects.FirstOrDefault();
            Console.WriteLine($"    hand {handBefore} -> {b.S.Hand.Count}, trash=[{string.Join(",", b.S.Trash.Select(x => x.CardId))}], "
                + $"pending={b.St.PendingEffects.Count} :: {pe?.Text}");
            foreach (var l in b.St.EventLog.Skip(2)) Console.WriteLine("    log| " + l.Message);

            Check("Zoro's [Trigger] cost waits for the player instead of auto-paying",
                b.S.Trash.Count == 0, $"already trashed [{string.Join(",", b.S.Trash.Select(x => x.CardId))}]");
            if (pe == null) { Check("Zoro's cost queues a pick", false, "no pending effect"); return; }
            Check("a card with NO [Trigger] cannot pay the cost",
                !GameEngine.IsValidEffectTarget(b.St, pe, nami), "Nami (no [Trigger]) was offered as payment");
            Check("a card WITH a [Trigger] can pay the cost",
                GameEngine.IsValidEffectTarget(b.St, pe, trig), "the [Trigger] card was not offered");

            // And paying with the wrong card must be refused by the resolver too, not just the glow.
            b.Resolve(pe, nami.InstanceId);
            Check("the resolver refuses the card the filter excludes",
                b.S.Hand.Any(x => x.InstanceId == nami.InstanceId),
                "Nami was accepted as payment despite having no [Trigger]");
        }

        // Rather than keep guessing which card the report meant, sweep the INVARIANT: for EVERY card in
        // the pool carrying an [On K.O.], discarding it from hand must trigger nothing. One of them
        // firing is the reported bug regardless of which card the playtester was holding.
        private static void NoOnKoCardInThePoolFiresWhenDiscarded()
        {
            var koCards = CardData.Library.Values
                .Where(c => c != null && !string.IsNullOrEmpty(c.Effect)
                            && (c.Effect.IndexOf("[On K.O.]", StringComparison.OrdinalIgnoreCase) >= 0
                             || c.Effect.IndexOf("[On KO]", StringComparison.OrdinalIgnoreCase) >= 0))
                .GroupBy(c => c.Id).Select(g => g.First())
                .OrderBy(c => c.Id, StringComparer.Ordinal).ToList();

            var fired = new System.Collections.Generic.List<string>();
            foreach (var def in koCards)
            {
                var b = new Board();
                b.Life("south", 3); b.Life("north", 3);
                var src = b.Character("south", "ST29-014");        // any on-field source for the clause
                var victim = b.Hand("south", def.Id);
                b.Hand("south", "ST29-004");                       // a second card, so the pick is a real choice
                GameEngine.QueueClauseForTest(b.St, "south", src, "main", "Trash 1 card from your hand.");
                var pe = b.St.PendingEffects.FirstOrDefault();
                if (pe == null) continue;
                b.Resolve(pe, victim.InstanceId);
                if (!b.S.Trash.Any(x => x.InstanceId == victim.InstanceId)) continue;   // wasn't discarded; skip
                // Anything queued whose SOURCE is the discarded card is a trigger that should not exist.
                if (b.St.PendingEffects.Any(e => e.SourceInstanceId == victim.InstanceId))
                    fired.Add(def.Id + " " + def.Name);
            }

            Console.WriteLine($"    swept {koCards.Count} cards carrying an [On K.O.]");
            Check("no [On K.O.] in the pool fires when its card is discarded from hand",
                fired.Count == 0, fired.Count == 0 ? null : string.Join("; ", fired.Take(6)));
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
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "hand-trash-ko" });
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

            public void SetLeader(string seat, string id)
            {
                var l = seat == "south" ? S.Leader : N.Leader;
                l.CardId = id; l.Rested = false; l.PlayedOnTurn = 0; l.AttachedDonIds.Clear();
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

            public void Attack(CardInstance atk, CardInstance tgt) => Apply(new GameCommand
            { Type = "declareAttack", Seat = atk.Owner, Attacker = atk.InstanceId, Target = tgt.InstanceId });

            public void Resolve(PendingEffect e, string target) => Apply(new GameCommand
            { Type = "resolveEffect", Seat = e.Seat, EffectId = e.EffectId, Target = target });

            public void Apply(GameCommand c) => St = GameEngine.ApplyCommand(St, c);

            private CardInstance Card(string id, string owner, string zone) => new CardInstance
            {
                InstanceId = $"{owner}-{id}-htk-{serial++}",
                CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
