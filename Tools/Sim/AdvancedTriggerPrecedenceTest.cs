using System;
using System.Collections.Generic;
using System.Linq;
using OnePieceTcg.Engine;
using OnePieceTcg.Engine.Bot;
using OnePieceTcg.Engine.Bot.Search;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// Regression for the Advanced-tier TRIGGER LIVELOCK found by the `botstall` sweep (2026-07-25), same
    /// family as report 20260725-045539-685.
    ///
    /// A [Trigger]'s BODY can queue its own pending effect while the battle is still parked on
    /// Step == "trigger" (OP14-108 "K.O. up to 1 …", ST11-005 "+1000 power"). AdvancedContractBot's
    /// trigger-utility fast path only tested `Battle.Step == "trigger" && TargetSeat == seat`, so it
    /// outranked that open decision and handed back `useTrigger` again — which never resolves it. The bot
    /// re-issued the same command every tick and the match hung with the bot apparently frozen.
    ///
    /// Only the Advanced tier was affected: IntermediateBot.DecideNextCommand checks PendingEffects BEFORE
    /// Battle, so beginner/intermediate resolve the effect correctly. That asymmetry is the assertion below.
    ///
    /// Run: dotnet run --project Sim.csproj -c Release -- triggerprecedencetest
    /// </summary>
    public static class AdvancedTriggerPrecedenceTest
    {
        private static int passed, failed;

        public static int Run()
        {
            Console.WriteLine("=== Advanced-bot trigger-step precedence regression ===");
            ResolvesTheQueuedEffectInsteadOfRepeatingUseTrigger();
            StillDecidesTheTriggerWhenNothingElseIsOpen();
            Console.WriteLine($"triggerprecedencetest: {passed}/{passed + failed} passed ({failed} failed)");
            return failed == 0 ? 0 : 1;
        }

        // The bug: at Battle.Step == "trigger" WITH a pending effect owned by the seat, the Advanced bot must
        // resolve the effect, never re-issue useTrigger. Starter decks only, so this needs no imported decks.
        private static void ResolvesTheQueuedEffectInsteadOfRepeatingUseTrigger()
        {
            int found = 0, offences = 0;
            foreach (var st in TriggerStepWithQueuedEffect())
            {
                found++;
                var cmd = AdvancedContractBot.Decide(GameClone.Clone(st), "north", new HashSet<string>(),
                    new HashSet<string>(), "midrange");
                string owner = st.ActiveChoice?.Seat ?? st.DeckLook?.Seat
                    ?? st.PendingEffects.FirstOrDefault()?.Seat;
                string kind = st.ActiveChoice != null ? "choice" : st.DeckLook != null ? "deckLook" : "effect";
                bool isTriggerCmd = cmd != null && (cmd.Type == "useTrigger" || cmd.Type == "passTrigger");
                // Mine to resolve  → must resolve it, never repeat useTrigger (that livelocks).
                // Opponent's       → must WAIT (null); acting here fires commands out of turn.
                bool bad = owner == "north" ? (cmd == null || isTriggerCmd) : cmd != null;
                if (bad)
                {
                    offences++;
                    Console.WriteLine($"      offence: got {cmd?.Type ?? "(null)"}; queued {kind} owned by {owner}");
                }
                if (found >= 6) break;
            }
            if (found == 0) { Check("(setup) reached a trigger step with a queued effect", false); return; }
            Check($"resolves the Trigger's queued effect rather than repeating useTrigger " +
                  $"({offences} offences over {found} positions)", offences == 0);
        }

        // The guard must not disable the trigger evaluator: with nothing queued, it still decides use vs pass.
        private static void StillDecidesTheTriggerWhenNothingElseIsOpen()
        {
            int found = 0, decided = 0;
            foreach (var st in TriggerStepWithNothingQueued())
            {
                found++;
                var cmd = AdvancedContractBot.Decide(GameClone.Clone(st), "north", new HashSet<string>(),
                    new HashSet<string>(), "midrange");
                if (cmd != null && (cmd.Type == "useTrigger" || cmd.Type == "passTrigger")) decided++;
                if (found >= 6) break;
            }
            if (found == 0) { Check("(setup) reached a clean trigger step", false); return; }
            Check($"still decides use-vs-pass on a clean trigger step ({decided}/{found})", decided == found);
        }

        private static IEnumerable<GameState> TriggerStepWithQueuedEffect() =>
            ScanTriggerSteps(withQueuedEffect: true);

        private static IEnumerable<GameState> TriggerStepWithNothingQueued() =>
            ScanTriggerSteps(withQueuedEffect: false);

        /// <summary>Play starter matchups with the plain core (which never livelocks here) and yield every
        /// position where north is at the battle Trigger step, split by whether a pending effect is queued.</summary>
        private static IEnumerable<GameState> ScanTriggerSteps(bool withQueuedEffect)
        {
            var ids = CardData.StarterDecks.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
            for (int i = 0; i < 60; i++)
            {
                var st = GameEngine.CreateMatch(new MatchConfig
                {
                    SouthDeckDef = CardData.StarterDecks[ids[(i * 7) % ids.Count]],
                    NorthDeckDef = CardData.StarterDecks[ids[(i * 13 + 3) % ids.Count]],
                    Seed = "trigprec-" + i,
                });
                var bl = new HashSet<string>();
                int guard = 0;
                while (st.Status != "finished" && st.TurnNumber <= 20 && guard++ < 1500)
                {
                    bool atTrigger = st.Battle != null && st.Battle.Step == "trigger"
                                     && st.Battle.TargetSeat == "north";
                    bool queued = st.PendingEffects.Count > 0 || st.ActiveChoice != null || st.DeckLook != null;
                    if (atTrigger && queued == withQueuedEffect) yield return GameClone.Clone(st);

                    var cmd = IntermediateBot.DecideOneCommand(st, "north", bl)
                              ?? IntermediateBot.DecideOneCommand(st, "south", bl);
                    if (cmd == null) break;
                    object before = IntermediateBot.SnapshotFor(st, cmd);
                    GameEngine.ApplyCommand(st, cmd);
                    if (!IntermediateBot.Succeeded(st, cmd, before)) bl.Add(IntermediateBot.Signature(cmd));
                }
            }
        }

        private static void Check(string name, bool ok)
        {
            if (ok) { passed++; Console.WriteLine("  PASS  " + name); }
            else { failed++; Console.WriteLine("  FAIL  " + name); }
        }
    }
}
