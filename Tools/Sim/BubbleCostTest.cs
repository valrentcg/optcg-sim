using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// Database inventory plus engine regressions for circled-number DON!! activation costs.
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- bubblecostcheck
    /// </summary>
    public static class BubbleCostTest
    {
        private static int passed;
        private static int failed;

        public static int Run()
        {
            Console.WriteLine("=== Circled DON!! cost interaction audit ===");
            InventoryUsesSupportedTimings();
            ActivateMainPaysBeforeTargetSelection();
            OnPlayWaitsForPayment();
            OpponentAttackPaysOnceBeforeTargetSelection();
            EndOfTurnSelfRestandWaitsForPayment();
            EndOfTurnTargetedRestandWaitsForPayment();

            Console.WriteLine($"bubblecostcheck: {passed}/{passed + failed} passed ({failed} failed)");
            return failed == 0 ? 0 : 1;
        }

        private static void InventoryUsesSupportedTimings()
        {
            var cards = CardData.Library.Values
                // OP07-019's latest reprint spells ➀ out as "You may rest 1..." while older
                // printings use the glyph. Include that equivalent canonical runtime definition.
                .Where(c => HasBubble(c.Effect) || HasBubble(c.Trigger)
                    || (c.Id == "OP07-019" &&
                        (HasDonRestActivationCost(c.Effect) || HasDonRestActivationCost(c.Trigger))))
                .OrderBy(c => c.Id)
                .ToList();
            string[] supported =
            {
                "[Activate: Main]", "[When Attacking]", "[On Play]",
                "[On Your Opponent's Attack]", "[End of Your Turn]",
            };
            var unsupported = cards.Where(c =>
            {
                string text = (c.Effect ?? "") + "\n" + (c.Trigger ?? "");
                return !supported.Any(tag => text.IndexOf(tag, StringComparison.OrdinalIgnoreCase) >= 0);
            }).Select(c => c.Id).ToList();

            Console.WriteLine($"  inventory: {cards.Count} unique cards with circled/rest-DON costs; unsupported timings={unsupported.Count}");
            Check("all database circled costs use an audited interaction timing",
                cards.Count == 46 && unsupported.Count == 0,
                unsupported.Count == 0 ? null : string.Join(", ", unsupported));
        }

        private static void ActivateMainPaysBeforeTargetSelection()
        {
            var b = new Board();
            b.SetLeader("south", "OP01-003"); // ④: restand and buff a Character
            var target = b.Character("south", "ST01-002", rested: true);
            b.Don("south", 4);

            b.Apply(new GameCommand { Type = "activateMain", Seat = "south", Target = b.S.Leader.InstanceId });
            var pending = b.PendingFrom("OP01-003");
            Check("[Activate: Main] pays its circled cost once before choosing its body target",
                b.ActiveDon("south") == 0 && pending != null && !HasBubble(pending.Text) && target.Rested);
        }

        private static void OnPlayWaitsForPayment()
        {
            var b = new Board();
            var ulti = b.Hand("south", "OP01-093"); // cost 2, then ① to add a rested DON!!
            b.Don("south", 3);
            b.Apply(new GameCommand { Type = "playCard", Seat = "south", InstanceId = ulti.InstanceId });

            var pending = b.PendingFrom("OP01-093");
            bool waited = pending != null && pending.Optional && HasBubble(pending.Text)
                && b.ActiveDon("south") == 1;
            b.Resolve(pending);
            bool paid = b.ActiveDon("south") == 0 && b.PendingFrom("OP01-093") == null;
            Check("[On Play] remains pending until the player commits active DON!!", waited && paid);
        }

        private static void OpponentAttackPaysOnceBeforeTargetSelection()
        {
            var b = new Board();
            b.SetLeader("north", "OP07-019"); // ①: rest an opposing Leader/Character
            var attacker = b.Character("south", "ST01-002");
            b.Don("north", 1);
            b.Attack(attacker, b.N.Leader);

            var pending = b.PendingFrom("OP07-019");
            bool offered = pending != null && pending.Optional && HasDonRestActivationCost(pending.Text)
                && b.ActiveDon("north") == 1;
            b.Resolve(pending); // commits ①; body now waits for its card target
            pending = b.PendingFrom("OP07-019");
            bool transitioned = pending != null && !HasDonRestActivationCost(pending.Text) && b.ActiveDon("north") == 0;
            b.Resolve(pending, b.S.Leader.InstanceId);
            Check("[On Your Opponent's Attack] pays once, then exposes the body target",
                offered && transitioned && b.S.Leader.Rested && b.PendingFrom("OP07-019") == null,
                $"offered={offered} transitioned={transitioned} leaderRested={b.S.Leader.Rested} " +
                $"pending={(b.PendingFrom("OP07-019") != null)} activeDon={b.ActiveDon("north")} " +
                $"log={string.Join(" || ", b.St.EventLog.TakeLast(6).Select(e => e.Message))}");
        }

        private static void EndOfTurnSelfRestandWaitsForPayment()
        {
            var b = new Board();
            var pica = b.Character("south", "OP05-032", rested: true); // ①: set this active
            b.Don("south", 1);
            b.Apply(new GameCommand { Type = "endTurn", Seat = "south" });

            var pending = b.PendingFrom("OP05-032");
            bool didNotResolveFree = pending != null && pending.Optional && HasBubble(pending.Text)
                && pica.Rested && b.ActiveDon("south") == 1;
            b.Resolve(pending);
            Check("[End of Your Turn] self-restand requires an explicit circled payment",
                didNotResolveFree && !pica.Rested && b.ActiveDon("south") == 0);
        }

        private static void EndOfTurnTargetedRestandWaitsForPayment()
        {
            var b = new Board();
            b.SetLeader("south", "OP04-020"); // [DON!! x1], then ①: restand cost-5 or less
            b.S.Leader.AttachedDonIds.Add("south-attached-audit-don");
            var target = b.Character("south", "ST01-002", rested: true);
            b.Don("south", 1);
            b.Apply(new GameCommand { Type = "endTurn", Seat = "south" });

            var pending = b.PendingFrom("OP04-020");
            bool offered = pending != null && HasBubble(pending.Text) && target.Rested
                && b.ActiveDon("south") == 1;
            b.Resolve(pending);
            pending = b.PendingFrom("OP04-020");
            bool transitioned = pending != null && !HasBubble(pending.Text)
                && target.Rested && b.ActiveDon("south") == 0;
            b.Resolve(pending, target.InstanceId);
            Check("[End of Your Turn] targeted effect pays once before choosing the target",
                offered && transitioned && !target.Rested && b.PendingFrom("OP04-020") == null);
        }

        private static bool HasBubble(string text) =>
            (text ?? "").Any(c => (c >= '\u2460' && c <= '\u2469') || (c >= '\u2780' && c <= '\u2789'));

        private static bool HasDonRestActivationCost(string text)
        {
            if (HasBubble(text)) return true;
            string bare = Regex.Replace(text ?? "", @"^\s*(\[[^\]]+\]\s*/?\s*)+", "");
            return Regex.IsMatch(bare, @"^You (?:may|can) rest \d+ of your DON!! cards?\s*:",
                       RegexOptions.IgnoreCase)
                || Regex.IsMatch(bare,
                    @"^Rest \d+ of your DON!! cards? and you may rest this Character\s*:",
                    RegexOptions.IgnoreCase);
        }

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
            private int southSlot;
            private int northSlot;
            private int serial;

            public Board()
            {
                St = GameEngine.CreateMatch(new MatchConfig
                {
                    SouthDeck = "st01", NorthDeck = "st01", Seed = "bubble-cost-audit",
                });
                St.Status = "active";
                St.Phase = "main";
                St.ActiveSeat = "south";
                St.TurnNumber = 8;
                S.TurnsStarted = 4;
                N.TurnsStarted = 4;
                for (int i = 0; i < 5; i++) { S.CharacterArea[i] = null; N.CharacterArea[i] = null; }
                S.Hand.Clear();
                N.Hand.Clear();
                S.Life.Clear();
                N.Life.Clear();
                S.CostArea.Clear();
                N.CostArea.Clear();
                S.DonDeck = 10;
                N.DonDeck = 10;
                S.Leader.Rested = false;
                N.Leader.Rested = false;
                S.Leader.PlayedOnTurn = 0;
                N.Leader.PlayedOnTurn = 0;
                S.Leader.AttachedDonIds.Clear();
                N.Leader.AttachedDonIds.Clear();
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
                int slot = seat == "south" ? southSlot++ : northSlot++;
                p.CharacterArea[slot] = c;
                return c;
            }

            public CardInstance Hand(string seat, string id)
            {
                var c = Card(id, seat, "hand");
                (seat == "south" ? S : N).Hand.Add(c);
                return c;
            }

            public void Don(string seat, int count)
            {
                var p = seat == "south" ? S : N;
                for (int i = 0; i < count; i++)
                    p.CostArea.Add(new DonInstance { InstanceId = $"{seat}-bubble-don-{serial++}", Rested = false });
                p.DonDeck = Math.Max(0, p.DonDeck - count);
            }

            public int ActiveDon(string seat) =>
                (seat == "south" ? S : N).CostArea.Count(d => !d.Rested);

            public PendingEffect PendingFrom(string cardId) =>
                St.PendingEffects.FirstOrDefault(e => e.SourceCardId == cardId);

            public void Attack(CardInstance attacker, CardInstance target) =>
                Apply(new GameCommand
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
                InstanceId = $"{owner}-{id}-bubble-{serial++}",
                CardId = id,
                Owner = owner,
                Zone = zone,
                Rested = false,
                PlayedOnTurn = 0,
            };
        }
    }
}
