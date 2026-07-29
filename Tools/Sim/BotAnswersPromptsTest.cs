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
            BotAnswersAnOpponentImposedDecision();
            BotAnswersLifeFaceUpCost();
            BotAnswersTheTriggerStep();
            BotStillValuesACostPrefixedCounter();
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

        /// <summary>The newest prompt is the first one the bot does not OWN by controlling the source:
        /// "Your opponent may &lt;X&gt;. If they do not, &lt;Y&gt;." queues X on the OPPONENT's seat. In a
        /// solo game that seat is the AI, and it is asked in the middle of its own attack — so a bot
        /// that cannot answer here hangs the game at the worst possible moment. Both wordings are
        /// driven, since they resolve through different handlers (Life card vs DON!!).</summary>
        private static void BotAnswersAnOpponentImposedDecision()
        {
            foreach (var clause in new[]
            {
                "Trash 1 card from the top of your Life cards.",
                "Return 1 of your active DON!! cards to your DON!! deck.",
            })
            {
                var b = new Board("north");
                GameEngine.QueueClauseForTest(b.St, "north", b.Character("north", "ST29-010"), "main", clause);
                var pe = b.St.PendingEffects.LastOrDefault(e => e != null && e.Seat == "north");
                if (pe != null) { pe.DeclineContinuation = "Give up to 1 of your opponent's Leader or Character cards -2000 power during this turn."; pe.DeclineSeat = "south"; }
                Check($"bot answers an opponent-imposed decision ({clause.Split(' ')[0].ToLowerInvariant()})",
                      BotClearsItsDecision(b.St, "north", out string why), why);
            }
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

        /// <summary>The Life half of the brief, from the bot side. OP15-114 Wyper's "You may turn 1
        /// card from the top of your Life cards face-up: ..." is the shape the whole session started
        /// from, and in a solo game the AI has to answer it too.</summary>
        private static void BotAnswersLifeFaceUpCost()
        {
            var b = new Board("north");
            b.Character("north", "OP15-040");
            GameEngine.QueueClauseForTest(b.St, "north", b.Character("north", "OP15-114"), "onPlay",
                "You may turn 1 card from the top of your Life cards face-up: "
                + "Give all of your opponent's Characters -2000 power during this turn.");
            Check("bot answers the Life face-up cost prompt",
                  BotClearsItsDecision(b.St, "north", out string why), why);
        }

        /// <summary>Every point of Life damage puts a [Trigger] decision in front of whoever took
        /// it, so the bot meets this constantly - far more often than any card-specific prompt. A
        /// bot that cannot answer it hangs the game on the first hit that reveals a Trigger.</summary>
        private static void BotAnswersTheTriggerStep()
        {
            var b = new Board("south");                     // south attacks, north defends
            var n = b.St.Players["north"];
            n.Life.Clear();
            n.Life.Add(new CardInstance
            { InstanceId = "north-trig-life", CardId = "OP01-009", Owner = "north", Zone = "life" });
            var atk = b.Character("south", "EB03-002");      // vanilla 6000, beats a 5000 Leader
            atk.Rested = false; atk.PlayedOnTurn = 0;
            b.St.ActiveSeat = "south";
            b.St = GameEngine.ApplyCommand(b.St, new GameCommand
            { Type = "declareAttack", Seat = "south", Attacker = atk.InstanceId, Target = n.Leader?.InstanceId });
            if (b.St.Battle?.Step == "block") b.St = GameEngine.ApplyCommand(b.St, new GameCommand { Type = "passBlock", Seat = "north" });
            if (b.St.Battle?.Step == "counter") b.St = GameEngine.ApplyCommand(b.St, new GameCommand { Type = "passCounter", Seat = "north" });
            if (b.St.Battle?.Step == "damage") b.St = GameEngine.ApplyCommand(b.St, new GameCommand { Type = "resolveAttack", Seat = "north" });

            if (b.St.Battle?.Step != "trigger")
            { Check("bot answers the [Trigger] step", false, $"fixture: never reached it (step={b.St.Battle?.Step ?? "no battle"})"); return; }

            var cmd = IntermediateBot.DecideOneCommand(b.St, "north", new HashSet<string>());
            if (cmd == null) { Check("bot answers the [Trigger] step", false, "the bot returned no command at the trigger step"); return; }
            var after = GameEngine.ApplyCommand(b.St, cmd);
            Check("bot answers the [Trigger] step and the battle moves on",
                  after.Battle == null || after.Battle.Step != "trigger",
                  $"issued {cmd.Type} but the battle is still parked on the trigger step");
        }

        /// <summary>Regression guard for a fix that quietly broke the AI.
        ///
        /// Making AutomatedCounterPower return 0 for a cost-prefixed "[Counter] You may &lt;cost&gt;:
        /// ... +N power" was right — the boost must be bought, not free. But the bots pick counters
        /// with .Where(GetCounterPower(c) > 0), so all 15 such cards became invisible to them:
        /// measured at 0 played across 44,143 counters in decks that contain them.
        ///
        /// The two meanings are now separate — what applies automatically (0) versus what the card
        /// is worth to a player who can pay (+N). This pins that split, because a plain unit test of
        /// the rules fix passes either way.</summary>
        private static void BotStillValuesACostPrefixedCounter()
        {
            string id = null;
            foreach (var d in CardData.Library.Values)
            {
                if (d == null || string.IsNullOrEmpty(d.Effect)) continue;
                foreach (var line in d.Effect.Split((char)10))
                {
                    if (!line.TrimStart().StartsWith("[Counter]")) continue;
                    var body = System.Text.RegularExpressions.Regex.Replace(line, @"^\s*(\[[^\]]+\]\s*/?\s*)+", "").Trim();
                    if (System.Text.RegularExpressions.Regex.IsMatch(body, @"^You may [^:]+:.*\+\d",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase)) { id = d.Id; break; }
                }
                if (id != null) break;
            }
            if (id == null) { Check("bot still values a cost-prefixed counter", false, "no such card in the pool"); return; }

            var inst = new CardInstance { InstanceId = "probe", CardId = id, Owner = "south", Zone = "hand" };
            Check("a cost-prefixed [Counter] is still WORTH something to the bot",
                  GameEngine.GetCounterPower(inst) > 0,
                  $"GetCounterPower({id}) = {GameEngine.GetCounterPower(inst)} — at 0 the bot's "
                  + "counter filter discards it and the card is never played");
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
