using System;
using System.Collections.Generic;
using System.Linq;
using OnePieceTcg.Engine;
using OnePieceTcg.Engine.Bot;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// Every prompt added this session has to be answerable by the BOT, not just the player. A pending
    /// effect the bot owns and cannot answer is a hung solo game - the exact STUCK condition botstall
    /// hunts, except botstall plays random matchups and may never draw the handful of cards whose
    /// prompts changed.
    ///
    /// So this drops the bot straight into each new decision state and asks for one command:
    ///
    ///   protection discard   the guard survives, but WHICH card pays is now a queued pick
    ///   reveal-from-hand     a real choice now waits instead of auto-selecting
    ///   counter cost-prefix  the +N boost is now queued behind its cost rather than free
    ///
    /// Each is a state the engine could not previously produce, so no existing coverage reaches them.
    /// Returning null while owning the decision is the failure; any legal command is a pass, because
    /// the question here is liveness, not play quality.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- botprompts
    /// </summary>
    public static class BotAnswersPromptsTest
    {
        private static int passed, failed;

        public static int Run()
        {
            Console.WriteLine("=== Can the BOT answer the prompts added this session? ===");
            BotAnswersProtectionDiscard();
            BotAnswersRevealCost();
            BotAnswersCounterCost();
            BotAlwaysHasAFallbackAnswer();
            Console.WriteLine($"botprompts: {passed}/{passed + failed} passed ({failed} failed)");
            return failed == 0 ? 0 : 1;
        }

        private static void Check(string label, bool ok, string detail = "")
        {
            if (ok) { passed++; Console.WriteLine("  PASS  " + label); }
            else { failed++; Console.WriteLine("  FAIL  " + label + (detail.Length > 0 ? "  -- " + detail : "")); }
        }

        /// <summary>Ask the bot for one command and drive it, then report whether the pending decision
        /// it owned actually went away. A command that is rejected leaves the state unchanged and hangs
        /// the game just as surely as returning null, so both count as failure.</summary>
        private static bool BotClearsItsDecision(GameState st, string seat, out string why)
        {
            for (int step = 0; step < 12; step++)
            {
                if (!st.PendingEffects.Any(e => e != null && e.Seat == seat)) { why = null; return true; }
                var cmd = IntermediateBot.DecideOneCommand(st, seat, new HashSet<string>());
                if (cmd == null)
                {
                    why = $"the bot returned NO command while owning a pending effect "
                        + $"(\"{st.PendingEffects.First(e => e != null && e.Seat == seat).Text}\")";
                    return false;
                }
                int before = st.EventLog.Count;
                int pendingBefore = st.PendingEffects.Count;
                st = GameEngine.ApplyCommand(st, cmd);
                if (st.EventLog.Count == before && st.PendingEffects.Count == pendingBefore)
                {
                    why = $"the bot's command ({cmd.Type}) changed nothing - it will re-issue it forever";
                    return false;
                }
            }
            why = "the bot never cleared its decision within 12 commands";
            return false;
        }

        private static void BotAnswersProtectionDiscard()
        {
            // EB03-001 Nefeltari Vivi (Leader): "If your Character with a base cost of 4 or more would
            // be K.O.'d, you may trash 1 card from your hand instead." With 3 cards in hand the discard
            // is a queued pick - a state that did not exist before this session.
            var b = new Board("north");
            b.Leader("north", "EB03-001");
            b.Hand("north", "ST29-004"); b.Hand("north", "ST29-009"); b.Hand("north", "ST29-010");
            var victim = b.Character("north", "ST29-009");
            GameEngine.AuditKoByEffectAsking(b.St, "north", victim.InstanceId);

            if (!b.St.PendingEffects.Any(e => e != null && e.Seat == "north"))
            { Check("bot answers the protection discard", false, "fixture: no decision was offered"); return; }
            Check("bot answers the protection discard prompt",
                  BotClearsItsDecision(b.St, "north", out string why), why);
        }

        private static void BotAnswersRevealCost()
        {
            string film = null;
            foreach (var d in CardData.Library.Values)
            {
                if (d == null || !string.Equals(d.Type, "character", StringComparison.OrdinalIgnoreCase)) continue;
                if (d.Features != null && d.Features.Any(f => (f ?? "").IndexOf("FILM", StringComparison.OrdinalIgnoreCase) >= 0))
                { film = d.Id; break; }
            }
            if (film == null) { Check("bot answers the reveal cost", false, "fixture: no {FILM} Character"); return; }

            var b = new Board("north");
            b.Hand("north", film); b.Hand("north", film);   // two legal reveals => a real choice
            GameEngine.QueueClauseForTest(b.St, "north", b.Character("north", "ST29-010"), "main",
                "You may reveal 1 {FILM} type card from your hand: Draw 1 card.");
            Check("bot answers the reveal-from-hand cost prompt",
                  BotClearsItsDecision(b.St, "north", out string why), why);
        }

        private static void BotAnswersCounterCost()
        {
            // "[Counter] You may trash 1 card from your hand: ... gains +3000 power during this battle."
            // Previously the boost applied flat and nothing was queued; now it is a decision the
            // defending bot has to answer mid-battle.
            var b = new Board("north");
            b.Hand("north", "ST29-004"); b.Hand("north", "ST29-009");
            GameEngine.QueueClauseForTest(b.St, "north", b.Character("north", "ST29-010"), "counter",
                "You may trash 1 card from your hand: Up to 1 of your Leader or Character cards gains +3000 power during this battle.");
            Check("bot answers the counter cost prompt",
                  BotClearsItsDecision(b.St, "north", out string why), why);
        }

        /// <summary>What actually guarantees the bot cannot hang on these prompts.
        ///
        /// Two obvious controls both turned out to be dead ends worth recording. A nonsense clause
        /// never queues - the engine disposes of unresolvable text upstream, even with
        /// RetireUnresolvablePendingEffects disabled - so the bot never sees it. And blacklisting
        /// every answer the bot gives does NOT starve it: it returns passEffect regardless, even
        /// with that exact signature already in the blacklist.
        ///
        /// That second dead end IS the answer. DecideOneCommand keeps an unconditional Skip
        /// fallback, so it structurally cannot return null on a pending effect, and the "no
        /// command" branch of the detector is unreachable by design rather than by luck. The
        /// meaningful failure is a LIVELOCK - commands that never clear the decision - which the
        /// three checks above do catch (they loop until the effect is gone) and which botstall
        /// covers at scale.
        ///
        /// So this pins the fallback itself. Remove it and solo games can hang; this goes red.</summary>
        private static void BotAlwaysHasAFallbackAnswer()
        {
            var b = new Board("north");
            b.Hand("north", "ST29-004"); b.Hand("north", "ST29-009");
            GameEngine.QueueClauseForTest(b.St, "north", b.Character("north", "ST29-010"), "main",
                "You may trash 1 card from your hand: Draw 1 card.");
            if (!b.St.PendingEffects.Any(e => e != null && e.Seat == "north"))
            { Check("bot always has a fallback answer", false, "fixture: nothing queued"); return; }

            // Blacklist every answer it offers, then ask once more.
            var tried = new HashSet<string>();
            for (int i = 0; i < 8; i++)
            {
                var cmd = IntermediateBot.DecideOneCommand(b.St, "north", tried);
                if (cmd == null) break;
                if (!tried.Add(IntermediateBot.Signature(cmd))) break;   // repeating itself
            }
            var fallback = IntermediateBot.DecideOneCommand(b.St, "north", tried);
            Check("bot always has a fallback answer, so it cannot hang on a prompt",
                  fallback != null,
                  "with every answer blacklisted the bot returned nothing - a solo game would hang here");
        }

        private sealed class Board
        {
            public GameState St;
            private readonly string seat;
            private int slot, serial;

            public Board(string botSeat)
            {
                seat = botSeat;
                St = GameEngine.CreateMatch(new MatchConfig
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "bot-prompts" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = botSeat; St.TurnNumber = 8;
                foreach (var p in St.Players.Values)
                {
                    p.TurnsStarted = 4;
                    for (int i = 0; i < 5; i++) p.CharacterArea[i] = null;
                    p.Hand.Clear(); p.Life.Clear(); p.CostArea.Clear();
                    for (int i = 0; i < 4; i++) p.Life.Add(Card("ST01-005", "life"));
                    for (int i = 0; i < 10; i++)
                        p.CostArea.Add(new DonInstance { InstanceId = $"{p.Name}-bp-don-{serial++}", Rested = false });
                    p.DonDeck = 0;
                }
                St.PendingEffects.Clear();
            }

            public void Leader(string s, string id) => St.Players[s].Leader = Card(id, "leader");
            public CardInstance Hand(string s, string id)
            { var c = Card(id, "hand"); St.Players[s].Hand.Add(c); return c; }
            public CardInstance Character(string s, string id)
            { var c = Card(id, "character"); St.Players[s].CharacterArea[slot++] = c; return c; }

            private CardInstance Card(string id, string zone) => new CardInstance
            {
                InstanceId = $"{seat}-{id}-bp-{serial++}",
                CardId = id, Owner = seat, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
