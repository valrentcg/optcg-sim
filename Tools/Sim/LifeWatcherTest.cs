using System;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// The Life-removal reactive now has TWO producers, and this file exists to prove they do not
    /// both fire for one Life card.
    ///
    ///   the OLD one   the battle-damage path calls FireOnOpponentLifeRemoved directly, for the
    ///                 attacking seat, mid-command
    ///   the NEW one   a watcher at the ApplyCommand boundary, which fires whenever either seat's
    ///                 Life count shrank during the command — battle damage INCLUDED
    ///
    /// So a battle-damage command runs both. On a [Once Per Turn] card the key hides it; the second
    /// fire is refused and the card looks correct. **OP12-099 Kalgara has no [Once Per Turn]**, so
    /// on Kalgara a double-fire is a double DRAW — the card reads "draw 1 card" and you would take
    /// two, every single attack, for the rest of the game.
    ///
    /// That is the sharpest instrument in the pool for this bug, and it is also the second card the
    /// brief names, so it has to be right on its own merits.
    ///
    ///   ONCE       battle damage fires the reactive exactly once, not twice
    ///   OWN LIFE   paying your OWN Life as a cost fires it too — the half that never worked, checked
    ///              here on the card WITHOUT the once-key, so the pass cannot come from the key
    ///   OTHER      OP08-105 Bonney reads "your opponent's Life" only, and must NOT fire when the
    ///              controller's own Life is what left
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- lifewatcher
    /// </summary>
    public static class LifeWatcherTest
    {
        private static int passed, failed;

        public static int Run()
        {
            Console.WriteLine("=== Life-removal reactive: fires once, fires for BOTH sides, and only when it should ===");
            BattleDamageFiresTheReactiveExactlyOnce();
            PayingYourOwnLifeFiresItOnACardWithoutTheOnceKey();
            AnOpponentOnlyWatcherIgnoresYourOwnLifeLoss();
            AProhibitionClauseIsNotExecutedAsAnInstruction();
            ARearrangeDoesNotPayTheWatcher();
            AddingLifeToHandDoesFireIt();
            Console.WriteLine($"lifewatcher: {passed}/{passed + failed} passed ({failed} failed)");
            return failed == 0 ? 0 : 1;
        }

        private static void Check(string label, bool ok, string detail = "")
        {
            if (ok) { passed++; Console.WriteLine("  PASS  " + label); }
            else { failed++; Console.WriteLine("  FAIL  " + label + (detail.Length > 0 ? "  -- " + detail : "")); }
        }

        /// <summary>The double-fire detector. Two producers, one Life card, one draw.</summary>
        private static void BattleDamageFiresTheReactiveExactlyOnce()
        {
            var b = new Board("OP12-099");
            int hand0 = b.S.Hand.Count, nLife0 = b.N.Life.Count;

            int fires = b.AttackLeaderAndCountReactiveFires();

            int drew = b.S.Hand.Count - hand0;
            int lifeLost = nLife0 - b.N.Life.Count;
            Check("battle damage fires the Life-removal reactive EXACTLY ONCE",
                  lifeLost == 1 && fires == 1,
                  $"opponent lost {lifeLost} Life, reactive fired {fires}x, Kalgara drew {drew} "
                  + "(2 means the battle path and the command-boundary watcher BOTH fired)");
        }

        /// <summary>The half the fix added, checked on the card that has no once-key — so a pass
        /// here cannot be an artifact of the key being consumed.</summary>
        private static void PayingYourOwnLifeFiresItOnACardWithoutTheOnceKey()
        {
            var b = new Board("OP12-099");
            int hand0 = b.S.Hand.Count, life0 = b.S.Life.Count;

            b.PayALifeCostThroughACommand();

            int drew = b.S.Hand.Count - hand0, lost = life0 - b.S.Life.Count;
            // The cost trashes a Life card and the draw adds one to hand, so hand nets +1.
            Check("paying your OWN Life as a cost fires the reactive (no once-key involved)",
                  lost == 1 && drew == 1,
                  $"own Life -{lost} (want 1), hand +{drew} (want 1) — Kalgara reads \"your OR your "
                  + "opponent's Life\", and this is the \"your\" half");
        }

        /// <summary>OP08-105 Bonney says "your opponent's Life cards" with no "your or". A watcher
        /// that fires on any Life loss anywhere would hand her a draw when SHE takes damage — the
        /// obvious way to over-correct the fix.</summary>
        private static void AnOpponentOnlyWatcherIgnoresYourOwnLifeLoss()
        {
            var b = new Board("OP08-105");
            foreach (var d in b.S.CostArea.Take(2)) d.Rested = false;   // [DON!! x1] threshold
            b.AttachDon(b.S.CharacterArea[0], 2);
            int hand0 = b.S.Hand.Count;

            b.PayALifeCostThroughACommand();

            int drew = b.S.Hand.Count - hand0;
            Check("an \"opponent's Life\" watcher does NOT fire when your own Life leaves",
                  drew <= 0,
                  $"hand +{drew} — Bonney's text has no \"your or\", so losing your own Life must "
                  + "not pay her out");
        }

        /// <summary>Kalgara's own rider is "Then, you cannot draw cards using your own effects during
        /// this turn." A substring interpreter sees "draw cards" in it and DRAWS — the sentence that
        /// forbids drawing is executed as a draw. That is why Kalgara drew twice.
        ///
        /// General class, not a Kalgara quirk: any negated sentence whose object happens to contain
        /// an action verb is at risk of being run as that action.</summary>
        private static void AProhibitionClauseIsNotExecutedAsAnInstruction()
        {
            var b = new Board("OP12-099");
            int hand0 = b.S.Hand.Count;

            GameEngine.QueueClauseForTest(b.St, "south", b.S.CharacterArea[0], "main",
                "You cannot draw cards using your own effects during this turn.");
            b.DrainAll();

            int drew = b.S.Hand.Count - hand0;
            Check("a \"you cannot draw\" clause does NOT draw",
                  drew <= 0,
                  $"hand +{drew} — the sentence FORBIDDING a draw was executed as one");
        }

        /// <summary>The member the fix did NOT touch. A rearrange LIFTS Life cards out to show them
        /// and puts them back; if the command ends with the look still open, the Life count is
        /// momentarily lower and the boundary watcher would read that as a REMOVAL — paying Kalgara
        /// a free card every time anyone reorders their own Life. Nothing was removed, so nothing
        /// may fire.</summary>
        private static void ARearrangeDoesNotPayTheWatcher()
        {
            var b = new Board("OP12-099");
            int hand0 = b.S.Hand.Count, life0 = b.S.Life.Count;

            b.DriveClause("Look at all of your Life cards and place them back in your Life area in any order.");

            int drew = b.S.Hand.Count - hand0;
            Check("a Life REARRANGE does not fire the removal watcher",
                  drew == 0 && b.S.Life.Count == life0,
                  $"hand +{drew} (want 0), life {life0} -> {b.S.Life.Count} — a reorder removes "
                  + "nothing, so a card that reacts to removal must not be paid");
        }

        /// <summary>The opposite direction, so the case above cannot pass by the watcher being dead.
        /// "Add 1 card from the top of your Life cards to your hand" genuinely REMOVES a Life card,
        /// and is a different removal route from both battle damage and a paid cost.</summary>
        private static void AddingLifeToHandDoesFireIt()
        {
            var b = new Board("OP12-099");
            int life0 = b.S.Life.Count, hand0 = b.S.Hand.Count;

            // Behind a "You may" cost, so the ADD resolves inside a resolveEffect COMMAND. A bare
            // mandatory clause is auto-resolved by QueueClauseForTest outside ApplyCommand, where no
            // boundary exists to observe it — in real play the queue always happens inside a command,
            // so testing the bare form measures the harness rather than the engine.
            b.DriveClause("You may rest this Character: Add 1 card from the top of your Life cards to your hand.");

            int lost = life0 - b.S.Life.Count, gained = b.S.Hand.Count - hand0;
            // +1 for the Life card itself, +1 for Kalgara's draw.
            Check("adding a Life card to hand DOES fire the removal watcher",
                  lost == 1 && gained == 2,
                  $"life -{lost} (want 1), hand +{gained} (want 2: the Life card AND Kalgara's draw)");
        }

        private sealed class Board
        {
            public GameState St;
            public PlayerState S => St.Players["south"];
            public PlayerState N => St.Players["north"];
            public System.Collections.Generic.List<DonInstance> CostArea => S.CostArea;
            private int serial;

            public Board(string subjectId)
            {
                St = GameEngine.CreateMatch(new MatchConfig
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "life-watcher" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "south"; St.TurnNumber = 8;
                foreach (var p in St.Players.Values)
                {
                    p.TurnsStarted = 4;
                    for (int i = 0; i < 5; i++) p.CharacterArea[i] = null;
                    p.Hand.Clear(); p.Life.Clear(); p.CostArea.Clear(); p.Trash.Clear();
                    foreach (var id in new[] { "ST29-004", "ST29-009", "ST01-005" })
                        p.Hand.Add(Make(id, p.Seat, "hand"));
                    for (int i = 0; i < 4; i++) p.Life.Add(Make("ST01-005", p.Seat, "life"));
                    foreach (var c in p.Life) c.FaceUp = false;
                    for (int i = 0; i < 10; i++)
                        p.CostArea.Add(new DonInstance { InstanceId = $"{p.Seat}-lw-don-{serial++}", Rested = false });
                    p.DonDeck = 4;
                    p.AbilityUsedThisTurn.Clear();
                }
                S.CharacterArea[0] = Make(subjectId, "south", "character");
                St.PendingEffects.Clear();
            }

            public void AttachDon(CardInstance c, int n)
            {
                for (int i = 0; i < n && i < S.CostArea.Count; i++)
                    c.AttachedDonIds.Add(S.CostArea[i].InstanceId);
            }

            /// <summary>A real attack on the opponent's Leader, driven to damage, counting how many
            /// times the reactive queued. The DEFENDER owns every battle decision including
            /// resolveAttack, so those commands are issued as north.</summary>
            public int AttackLeaderAndCountReactiveFires()
            {
                var atk = Make("ST01-006", "south", "character");
                atk.Rested = false; atk.PlayedOnTurn = 0;
                S.CharacterArea[1] = atk;
                AttachDon(atk, 4);                       // enough power to get through the Leader
                int log0 = St.EventLog.Count;

                Apply(new GameCommand
                { Type = "declareAttack", Seat = "south", Attacker = atk.InstanceId, Target = N.Leader?.InstanceId });
                foreach (var cmd in new[] { "passBlock", "passCounter", "resolveAttack", "passTrigger" })
                    for (int i = 0; i < 3; i++)
                    {
                        int before = St.EventLog.Count;
                        Apply(new GameCommand { Type = cmd, Seat = "north" });
                        if (St.EventLog.Count == before) break;
                    }
                DrainPending();

                if (Environment.GetEnvironmentVariable("OPT_DIAG") == "1")
                    foreach (var e in St.EventLog.Skip(log0)) Console.WriteLine("      [atk] " + e.Message);
                return St.EventLog.Skip(log0)
                    .Count(e => (e.Message ?? "").IndexOf("onOppLifeRemoved", StringComparison.OrdinalIgnoreCase) >= 0);
            }

            /// <summary>Remove one of MY Life cards through a real resolveEffect command, which is
            /// how a Life cost is paid in play — and, critically, inside ApplyCommand, where the
            /// boundary watcher can see it.</summary>
            public void PayALifeCostThroughACommand()
            {
                GameEngine.QueueClauseForTest(St, "south", S.CharacterArea[0], "main",
                    "You may trash 1 card from the top of your Life cards: This Character gains +1000 power during this turn.");
                DrainPending();
                if (Environment.GetEnvironmentVariable("OPT_DIAG") == "1")
                    foreach (var e in St.EventLog.TakeLast(8)) Console.WriteLine("      [cost] " + e.Message);
            }

            public void DrainAll() => DrainPending();

            /// <summary>Queue a clause and answer it, including any look it opens.</summary>
            public void DriveClause(string clause)
            {
                GameEngine.QueueClauseForTest(St, "south", S.CharacterArea[0], "main", clause);
                for (int i = 0; i < 10; i++)
                {
                    if (St.DeckLook != null)
                    {
                        var order = St.DeckLook.Cards.Select(x => x.InstanceId).ToList();
                        int b0 = St.EventLog.Count;
                        Apply(new GameCommand
                        { Type = "deckLookConfirmOrder", Seat = "south", OrderedInstanceIds = order });
                        if (St.EventLog.Count == b0) break;
                        continue;
                    }
                    var pe = St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
                    if (pe == null) break;
                    string target = Everything()
                        .FirstOrDefault(x => GameEngine.IsValidEffectTarget(St, pe, x))?.InstanceId;
                    int before = St.EventLog.Count;
                    Apply(new GameCommand
                    { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId, Target = target });
                    if (St.EventLog.Count == before) break;
                }
                if (Environment.GetEnvironmentVariable("OPT_DIAG") == "1")
                    foreach (var e in St.EventLog.TakeLast(8)) Console.WriteLine("      [drive] " + e.Message);
            }

            private void DrainPending()
            {
                for (int i = 0; i < 10; i++)
                {
                    var pe = St.PendingEffects.FirstOrDefault(e => e != null && e.Seat == "south");
                    if (pe == null) break;
                    string target = Everything()
                        .FirstOrDefault(x => GameEngine.IsValidEffectTarget(St, pe, x))?.InstanceId;
                    int before = St.EventLog.Count;
                    Apply(new GameCommand
                    { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId, Target = target });
                    if (St.EventLog.Count == before) break;
                }
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

            public void Apply(GameCommand c) => St = GameEngine.ApplyCommand(St, c);

            private CardInstance Make(string id, string owner, string zone) => new CardInstance
            {
                InstanceId = $"{owner}-{id}-lw-{serial++}",
                CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
