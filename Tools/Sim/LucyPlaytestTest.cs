using System;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// Regressions for the OP15-002 Lucy playtest reports (2026-07-27, v1.0.27):
    ///  • the Leader's "[When Attacking]/[On Your Opponent's Attack] You may trash any number of Event or
    ///    Stage cards from your hand" never flagged the hand as payable — on offence or on defence;
    ///  • OP15-020 Fire Fist asked for a hand trash before granting the Leader +3000 / picking the −8000
    ///    target, and lit the Leader as a target for the debuff.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- lucytest
    /// </summary>
    public static class LucyPlaytestTest
    {
        private static int passed;
        private static int failed;

        public static int Run()
        {
            Console.WriteLine("=== OP15-002 Lucy playtest regressions ===");

            LucyOffersTheTrashForPowerWhenAttacking();
            LucyOffersTheTrashForPowerOnDefence();
            LucyLightsExactlyTheEventsAndStagesInMyHand();
            FireFistBuffsTheLeaderAndDebuffsAnOpponent();
            EveryActivationPathMarksTheEventAsPlayed();

            Console.WriteLine($"lucytest: {passed}/{passed + failed} passed ({failed} failed)");
            return failed == 0 ? 0 : 1;
        }

        // "[When Attacking]/[On Your Opponent's Attack] You may trash any number of Event or Stage cards
        // from your hand. This Leader gains +1000 power during this battle for every card trashed."
        // The report is about the GLOW: the effect may queue, but if IsValidEffectTarget refuses the hand
        // there is nothing to click and the ability is unusable.
        private static void LucyOffersTheTrashForPowerWhenAttacking()
        {
            var b = new Board();
            b.SetLeader("south", "OP15-002");
            var ev1 = b.Hand("south", "OP15-020");        // Event (Fire Fist)
            var ev2 = b.Hand("south", "OP15-021");        // Event
            var chr = b.Hand("south", "OP15-040");        // Character — must NOT pay
            string leaderId = b.S.Leader.InstanceId;

            b.Attack(b.S.Leader, b.N.Leader);

            var pe = b.St.PendingEffects.FirstOrDefault(e => e.SourceCardId == "OP15-002");
            Check("Lucy offers her trash-for-power when attacking", pe != null,
                $"pending={b.St.PendingEffects.Count} [{string.Join(", ", b.St.PendingEffects.Select(p => p.SourceCardId + ":" + p.Text))}]");
            if (pe == null) return;

            Check("an Event in hand is flagged as payable", GameEngine.IsValidEffectTarget(b.St, pe, ev1),
                $"zone={pe.TargetZone} text={pe.Text}");
            Check("a Character in hand is NOT flagged as payable", !GameEngine.IsValidEffectTarget(b.St, pe, chr));

            int Bonus() => b.St.Battle != null && b.St.Battle.BattlePowerBonus.TryGetValue(leaderId, out var v) ? v : 0;
            b.Resolve(pe, ev1.InstanceId);
            int one = Bonus();
            b.Resolve(pe, ev2.InstanceId);
            int two = Bonus();
            Check("the buff scales +1000 per card trashed", one == 1000 && two == 2000,
                $"1 card={one}, 2 cards={two}");
            Check("Lucy's live power reflects the trashes", GameEngine.GetPower(b.St, b.S.Leader) == 5000 + 2000,
                $"power={GameEngine.GetPower(b.St, b.S.Leader)}");
        }

        // The defensive half of the same line — the one a Lucy player actually leans on, and the one the
        // 19:30 report was filed against (north attacking, south holding a hand full of Events).
        private static void LucyOffersTheTrashForPowerOnDefence()
        {
            var b = new Board();
            b.St.ActiveSeat = "north";
            b.SetLeader("south", "OP15-002");
            var ev = b.Hand("south", "OP15-020");
            string leaderId = b.S.Leader.InstanceId;

            b.Attack(b.N.Leader, b.S.Leader);

            var pe = b.St.PendingEffects.FirstOrDefault(e => e.SourceCardId == "OP15-002");
            Check("Lucy offers her trash-for-power on defence", pe != null,
                $"pending={b.St.PendingEffects.Count} battleStep={b.St.Battle?.Step}");
            if (pe == null) return;
            Check("an Event in hand is flagged as payable on defence",
                GameEngine.IsValidEffectTarget(b.St, pe, ev), $"zone={pe.TargetZone}");

            b.Resolve(pe, ev.InstanceId);
            int bonus = b.St.Battle != null && b.St.Battle.BattlePowerBonus.TryGetValue(leaderId, out var v) ? v : 0;
            Check("the defensive buff lands", bonus == 1000, $"bonus={bonus}");
        }

        // The client does not just glow the valid cards — it decides its whole panel from whether ANY card
        // anywhere is valid (EffectHasValidTarget scans both seats' leader/board/hand/trash/life). With the
        // hand refused, that scan came back empty, so the panel fell back to a bare "Use Effect" button —
        // and pressing it resolved the effect with a null target, which the resolver treats as "done, nothing
        // trashed". Both buttons on screen threw the ability away. So the whole lit SET is what matters, on
        // both sides of the battle: exactly the Events and Stages in my own hand, and nothing else anywhere.
        private static void LucyLightsExactlyTheEventsAndStagesInMyHand()
        {
            foreach (bool onDefence in new[] { false, true })
            {
                var b = new Board();
                if (onDefence) b.St.ActiveSeat = "north";
                b.SetLeader("south", "OP15-002");
                var ev = b.Hand("south", "OP15-020");        // Event   — payable
                var st = b.Hand("south", "OP15-057");        // Stage   — payable ("Event or Stage")
                b.Hand("south", "OP15-040");                 // Character in hand — not payable
                b.Hand("north", "OP15-021");                 // opponent's hand   — never mine to trash
                b.Character("south", "OP15-046");            // my board
                b.Character("north", "OP15-046");            // their board
                b.Trash("south", "OP15-055");                // an Event, but in the TRASH, not the hand

                if (onDefence) b.Attack(b.N.Leader, b.S.Leader);
                else b.Attack(b.S.Leader, b.N.Leader);

                string where = onDefence ? "on defence" : "when attacking";
                var pe = b.St.PendingEffects.FirstOrDefault(e => e.SourceCardId == "OP15-002");
                if (pe == null) { Check($"Lucy queues her ability {where}", false, "no pending effect"); continue; }

                var lit = AllCards(b.St).Where(c => GameEngine.IsValidEffectTarget(b.St, pe, c))
                                        .Select(c => c.InstanceId).OrderBy(x => x).ToList();
                var want = new[] { ev.InstanceId, st.InstanceId }.OrderBy(x => x).ToList();
                Check($"exactly the Events and Stages in my hand light up {where}",
                    lit.SequenceEqual(want),
                    $"lit=[{string.Join(", ", lit.Select(id => Label(b.St, id)))}] want=[{string.Join(", ", want.Select(id => Label(b.St, id)))}]");
            }
        }

        // Every card the client's own scan would visit, in the same zones.
        private static System.Collections.Generic.IEnumerable<CardInstance> AllCards(GameState st)
        {
            foreach (var p in st.Players.Values)
            {
                if (p == null) continue;
                if (p.Leader != null) yield return p.Leader;
                foreach (var c in p.CharacterArea) if (c != null) yield return c;
                if (p.Stage != null) yield return p.Stage;
                foreach (var c in p.Hand) yield return c;
                foreach (var c in p.Trash) yield return c;
                foreach (var c in p.Life) yield return c;
            }
        }

        private static string Label(GameState st, string instanceId)
        {
            var c = AllCards(st).FirstOrDefault(x => x.InstanceId == instanceId);
            return c == null ? instanceId : $"{c.CardId}/{c.Owner}/{c.Zone}";
        }

        // OP15-020 Fire Fist: "[Main] Your Leader gains +3000 power during this turn and give up to 1 of your
        // opponent's Characters −8000 power until the end of your opponent's next End Phase. Then, you may
        // trash 2 cards from your hand. If you do, K.O. up to 1 of your opponent's Characters with 0 power
        // or less." The report: it asked for the hand trash FIRST, gave no +3000, and lit the Leader.
        private static void FireFistBuffsTheLeaderAndDebuffsAnOpponent()
        {
            var b = new Board();
            b.SetLeader("south", "OP15-002");
            b.Don("south", 7);
            var fist = b.Hand("south", "OP15-020");
            b.Hand("south", "OP15-021");                 // spare cards for the optional "trash 2"
            b.Hand("south", "OP15-021");
            var victim = b.Character("north", "OP15-046");   // 9000-power Sabo

            b.Apply(new GameCommand { Type = "playCard", Seat = "south", InstanceId = fist.InstanceId });

            int leaderPower = GameEngine.GetPower(b.St, b.S.Leader);
            Check("Fire Fist gives the Leader +3000 without being asked",
                leaderPower == 5000 + 3000, $"leader power={leaderPower} want 8000");

            var pe = b.St.PendingEffects.FirstOrDefault();
            Check("Fire Fist then asks for the −8000 target", pe != null, "no pending effect");
            if (pe == null) return;
            Check("the −8000 step does not offer my own Leader",
                !GameEngine.IsValidEffectTarget(b.St, pe, b.S.Leader), $"text={pe.Text}");
            Check("the −8000 step offers the opponent's Character",
                GameEngine.IsValidEffectTarget(b.St, pe, victim), $"text={pe.Text} zone={pe.TargetZone}");

            b.Resolve(pe, victim.InstanceId);
            int victimPower = GameEngine.GetPower(b.St, victim);
            Check("the opponent's Character actually loses 8000",
                victimPower == 9000 - 8000, $"victim power={victimPower} want 1000");
        }

        // The Event/Counter burn is triggered by the view from a hand→trash zone change, but only for a
        // card the engine has recorded in `ActivatedEventIds` — that list is what separates an Event
        // that was PLAYED from one discarded to pay a cost. So the engine half of "does it burn?" is
        // exactly: did this path record the activation, and did the card actually land in the trash.
        // Checked on every path that can activate an Event, because they are four different code paths
        // and only two of them were recording it.
        private static void EveryActivationPathMarksTheEventAsPlayed()
        {
            // 1. My own [Main] Event, played from hand.
            {
                var b = new Board();
                b.SetLeader("south", "OP15-002");
                b.Don("south", 7);
                var fist = b.Hand("south", "OP15-020");
                b.Apply(new GameCommand { Type = "playCard", Seat = "south", InstanceId = fist.InstanceId });
                CheckBurnable(b, "south", fist, "my own [Main] Event");
            }

            // 2. The OPPONENT's [Main] Event. The view diffs both seats' zones, so the engine simply has
            //    to record it the same way for north.
            {
                var b = new Board();
                b.St.ActiveSeat = "north";
                b.SetLeader("north", "OP15-002");
                b.Don("north", 7);
                var fist = b.Hand("north", "OP15-020");
                b.Apply(new GameCommand { Type = "playCard", Seat = "north", InstanceId = fist.InstanceId });
                CheckBurnable(b, "north", fist, "my opponent's [Main] Event");
            }

            // 3. An Event activated BY ANOTHER CARD — OP15-046 Sabo's "[On Play] activate up to 1
            //    {Dressrosa} type Event from your hand" into Fire Fist. This is the path the user asked
            //    about, and the one that recorded nothing.
            {
                var b = new Board();
                b.SetLeader("south", "OP15-002");            // {Dressrosa} Leader — Sabo's gate
                b.Don("south", 7);
                var fist = b.Hand("south", "OP15-020");
                var sabo = b.Character("south", "OP15-046");
                GameEngine.QueueClauseForTest(b.St, "south", sabo, "onPlay",
                    "If your Leader has the {Dressrosa} type, activate up to 1 {Dressrosa} type Event from your hand.");
                var pe = b.St.PendingEffects.FirstOrDefault();
                if (pe == null) { Check("Sabo offers to activate an Event", false, "no pending effect"); return; }
                Check("Sabo's activate step flags the Event in my hand",
                    GameEngine.IsValidEffectTarget(b.St, pe, fist), $"zone={pe.TargetZone}");
                b.Resolve(pe, fist.InstanceId);
                CheckBurnable(b, "south", fist, "an Event activated by Sabo's [On Play]");
            }

            // 4. A [Counter] Event, played during the opponent's attack.
            {
                var b = new Board();
                b.St.ActiveSeat = "north";
                b.Don("south", 4);
                var ctr = b.Hand("south", "OP15-021");       // [Main]/[Counter] give −3000
                b.Character("north", "OP15-046");
                b.Attack(b.N.Leader, b.S.Leader);
                b.Apply(new GameCommand { Type = "counterWithCard", Seat = "south", InstanceId = ctr.InstanceId });
                CheckBurnable(b, "south", ctr, "a [Counter] Event");
            }
        }

        private static void CheckBurnable(Board b, string seat, CardInstance card, string what)
        {
            var p = b.St.Players[seat];
            bool inTrash = p.Trash.Any(c => c.InstanceId == card.InstanceId);
            bool recorded = b.St.ActivatedEventIds.Contains(card.InstanceId);
            Check($"{what} is recorded as played, and lands in the trash",
                inTrash && recorded, $"inTrash={inTrash} recordedAsActivated={recorded}");
        }

        // ---- plumbing -------------------------------------------------------------------------

        private static void Check(string name, bool ok, string detail = null)
        {
            if (ok) { passed++; Console.WriteLine("  PASS  " + name); }
            else
            {
                failed++;
                Console.WriteLine("  FAIL  " + name + (string.IsNullOrEmpty(detail) ? "" : " — " + detail));
            }
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
                {
                    SouthDeck = "st01", NorthDeck = "st01", Seed = "lucy-playtest",
                });
                St.Status = "active";
                St.Phase = "main";
                St.ActiveSeat = "south";
                St.TurnNumber = 8;
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
                var leader = seat == "south" ? S.Leader : N.Leader;
                leader.CardId = id;
                leader.Rested = false;
                leader.PlayedOnTurn = 0;
                leader.AttachedDonIds.Clear();
            }

            public CardInstance Character(string seat, string id, bool rested = false)
            {
                var c = Card(id, seat, "character");
                c.Rested = rested;
                var p = seat == "south" ? S : N;
                p.CharacterArea[seat == "south" ? southSlot++ : northSlot++] = c;
                return c;
            }

            public CardInstance Hand(string seat, string id)
            {
                var c = Card(id, seat, "hand");
                (seat == "south" ? S : N).Hand.Add(c);
                return c;
            }

            public CardInstance Trash(string seat, string id)
            {
                var c = Card(id, seat, "trash");
                (seat == "south" ? S : N).Trash.Add(c);
                return c;
            }

            public void Don(string seat, int count)
            {
                var p = seat == "south" ? S : N;
                for (int i = 0; i < count; i++)
                    p.CostArea.Add(new DonInstance { InstanceId = $"{seat}-lucy-don-{serial++}", Rested = false });
                p.DonDeck = Math.Max(0, p.DonDeck - count);
            }

            public void Attack(CardInstance attacker, CardInstance target) => Apply(new GameCommand
            {
                Type = "declareAttack", Seat = attacker.Owner,
                Attacker = attacker.InstanceId, Target = target.InstanceId,
            });

            public void Resolve(PendingEffect effect, string target = null)
            {
                if (effect == null) return;
                Apply(new GameCommand
                {
                    Type = "resolveEffect", Seat = effect.Seat,
                    EffectId = effect.EffectId, Target = target,
                });
            }

            public void Apply(GameCommand command) => St = GameEngine.ApplyCommand(St, command);

            private CardInstance Card(string id, string owner, string zone) => new CardInstance
            {
                InstanceId = $"{owner}-{id}-lucy-{serial++}",
                CardId = id,
                Owner = owner,
                Zone = zone,
                Rested = false,
                PlayedOnTurn = 0,
            };
        }
    }
}
