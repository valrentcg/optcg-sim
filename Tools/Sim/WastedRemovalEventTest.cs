using System;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// Reported from playtest: the Advanced bot, piloting Imu, repeatedly played OP13-098
    /// "Never Existed... in the First Place..." while the opponent had NO Stage.
    ///
    ///   [Main] You may rest 1 of your DON!! cards: If your Leader is [Imu], K.O. up to 1 of your
    ///          opponent's Stages with a cost of 7.
    ///   [Counter] If your Leader is [Imu], up to 1 of your Leader or Character cards gains +4000
    ///          power during this battle.
    ///
    /// With no Stage on the board that play is pure loss three times over: it spends a DON to play,
    /// rests a second DON as the cost, and throws away a 4000-power [Counter]. GameEngine.
    /// RemovalEventHasTarget already exists to stop the bot wasting a targeted removal event with no
    /// legal target (the Gum-Gum Jet Pistol fix), but it missed this card for two independent reasons:
    /// it only looked for "opponent's Character" (this hits STAGES), and it required the clause to
    /// START with the removal verb (this starts with a cost prefix and a condition).
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- wastedremoval
    /// </summary>
    public static class WastedRemovalEventTest
    {
        private static int passed;
        private static int failed;

        public static int Run()
        {
            Console.WriteLine("=== Bot: don't play a removal event with nothing to remove ===");
            NeverExistedIsPrunedWithNoStage();
            NeverExistedIsOfferedWhenThereIsAStage();
            StillOfferedWhenTheRemovalCanHit();
            GuardNeverPrunesWhenTargetsExist();
            Console.WriteLine($"wastedremoval: {passed}/{passed + failed} passed ({failed} failed)");
            return failed == 0 ? 0 : 1;
        }

        private static void NeverExistedIsPrunedWithNoStage()
        {
            var b = new Board();
            b.SetLeader("south", "OP13-079");            // Imu — the condition on the clause holds
            b.Don("south", 5);
            var card = b.Hand("south", "OP13-098");
            // north has NO Stage at all.
            bool hasTarget = GameEngine.RemovalEventHasTarget(b.St, "south", CardData.GetCard("OP13-098"));
            Check("with no Stage on the board, the event is seen as having no target",
                !hasTarget, "RemovalEventHasTarget said there IS a target");

            var acts = OnePieceTcg.Engine.Bot.Search.LegalActions.Candidates(b.St, "south");
            bool offered = acts.Any(a => a.Type == "playCard" && a.InstanceId == card.InstanceId);
            Check("…so the bot is never offered the play", !offered,
                "playCard for OP13-098 was still a candidate action");
        }

        private static void NeverExistedIsOfferedWhenThereIsAStage()
        {
            var b = new Board();
            b.SetLeader("south", "OP13-079");
            b.Don("south", 5);
            var card = b.Hand("south", "OP13-098");
            b.SetStage("north", "OP13-099");             // The Empty Throne — a cost-7 Stage, a legal target

            Check("with a cost-7 Stage out, the event has a target",
                GameEngine.RemovalEventHasTarget(b.St, "south", CardData.GetCard("OP13-098")),
                "the guard would wrongly prune a play that does something");

            var acts = OnePieceTcg.Engine.Bot.Search.LegalActions.Candidates(b.St, "south");
            Check("…and the play is offered to the bot",
                acts.Any(a => a.Type == "playCard" && a.InstanceId == card.InstanceId),
                "the play was pruned even though a legal Stage target exists");
        }

        // The guard must stay conservative: it is only allowed to prune a play that genuinely does
        // nothing. A plain removal event with a live target must always survive.
        private static void StillOfferedWhenTheRemovalCanHit()
        {
            var b = new Board();
            b.Don("south", 5);
            var jet = b.Hand("south", "ST01-015");       // Gum-Gum Jet Pistol — K.O. a Character
            b.Character("north", "ST29-009");            // a legal, non-immune victim
            Check("an ordinary removal event with a live target is still offered",
                GameEngine.RemovalEventHasTarget(b.St, "south", CardData.GetCard("ST01-015")),
                "the original Jet Pistol case regressed");
        }

        // The guard is only ever allowed to say "no target" when there genuinely is none. Its dangerous
        // direction is the false NEGATIVE: prune a play that would have done something, and the bot
        // silently loses a line with no error anywhere. So every Event in the pool is asked against a
        // board that is full on both axes — five Characters across the cost/power range plus a Stage.
        // Nothing may be pruned there. The empty-board count is reported for scale, not asserted.
        private static void GuardNeverPrunesWhenTargetsExist()
        {
            var events = CardData.Library.Values
                .Where(c => c != null && c.Type == "event" && !string.IsNullOrEmpty(c.Effect))
                .GroupBy(c => c.Id).Select(g => g.First())
                .OrderBy(c => c.Id, StringComparer.Ordinal).ToList();

            var wronglyPruned = new System.Collections.Generic.List<string>();
            int prunedOnEmptyBoard = 0;

            foreach (var def in events)
            {
                // The board has to span the COST axis as well as power, or a "cost of 2 or less" removal
                // is pruned for the honest reason that the fixture had nothing that cheap — which reads as
                // a false negative and is how the first cut of this sweep produced 8 phantom findings.
                // Stage cost is a single value per board, so both a cheap and an expensive Stage are tried.
                bool anyPass = false;
                foreach (var stage in new[] { "EB01-011", "OP13-099" })   // cost 1 and cost 7
                {
                    var full = new Board();
                    full.SetLeader("south", "OP13-079");
                    full.Don("south", 10);
                    full.Character("north", "EB01-005");    // cost 1, 3000
                    full.Character("north", "EB01-004");    // cost 2, 3000
                    full.Character("north", "ST29-008");    // cost 3, 1000
                    full.Character("north", "ST29-010");    // cost 5, 6000
                    full.Character("north", "ST29-006");    // cost 6, 7000
                    full.SetStage("north", stage);
                    if (GameEngine.RemovalEventHasTarget(full.St, "south", def)) { anyPass = true; break; }
                }
                if (!anyPass) wronglyPruned.Add($"{def.Id} {def.Name}");

                var empty = new Board();
                empty.SetLeader("south", "OP13-079");
                empty.Don("south", 10);
                if (!GameEngine.RemovalEventHasTarget(empty.St, "south", def)) prunedOnEmptyBoard++;
            }

            Console.WriteLine($"    {events.Count} Events swept; {prunedOnEmptyBoard} correctly pruned on an empty board");
            Check("no Event is pruned while the opponent has a full board and a Stage",
                wronglyPruned.Count == 0,
                wronglyPruned.Count == 0 ? null : string.Join(", ", wronglyPruned.Take(8)));
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
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "wasted-removal" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "south"; St.TurnNumber = 8;
                S.TurnsStarted = 4; N.TurnsStarted = 4;
                for (int i = 0; i < 5; i++) { S.CharacterArea[i] = null; N.CharacterArea[i] = null; }
                S.Hand.Clear(); N.Hand.Clear();
                S.Life.Clear(); N.Life.Clear();
                S.CostArea.Clear(); N.CostArea.Clear();
                S.Stage = null; N.Stage = null;
                S.DonDeck = 10; N.DonDeck = 10;
                S.Leader.Rested = false; N.Leader.Rested = false;
                S.Leader.PlayedOnTurn = 0; N.Leader.PlayedOnTurn = 0;
                St.PendingEffects.Clear();
                for (int i = 0; i < 3; i++) { S.Life.Add(Card("ST01-005", "south", "life")); N.Life.Add(Card("ST01-005", "north", "life")); }
            }

            public void SetLeader(string seat, string id)
            {
                var l = seat == "south" ? S.Leader : N.Leader;
                l.CardId = id; l.Rested = false; l.PlayedOnTurn = 0; l.AttachedDonIds.Clear();
            }

            public void SetStage(string seat, string id)
            {
                var p = seat == "south" ? S : N;
                p.Stage = Card(id, seat, "stage");
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

            public void Don(string seat, int count)
            {
                var p = seat == "south" ? S : N;
                for (int i = 0; i < count; i++)
                    p.CostArea.Add(new DonInstance { InstanceId = $"{seat}-wr-don-{serial++}", Rested = false });
                p.DonDeck = Math.Max(0, p.DonDeck - count);
            }

            private CardInstance Card(string id, string owner, string zone) => new CardInstance
            {
                InstanceId = $"{owner}-{id}-wr-{serial++}",
                CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
