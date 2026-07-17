using System;
using System.Collections.Generic;
using System.Linq;
using OnePieceTcg.Engine;
using OnePieceTcg.Engine.Bot;
using OnePieceTcg.Engine.Bot.Search;

namespace OnePieceTcg.Sim.Planning
{
    /// <summary>Deterministic regressions for deck-identity and owner-agnostic bounce decisions. These
    /// fixtures are intentionally card-specific witnesses of generic policy rules; they do not simulate
    /// a whole match or substitute for the paired strength gates.</summary>
    public static class StrategyFixtureTest
    {
        public static int Run(DeckRegistry registry)
        {
            int ok = 0, fail = 0;
            Check("Sables main outranks Boa when it can bounce an expensive enemy", TestMainEvent(), ref ok, ref fail);
            Check("Sables targets the opponent, never own Boa", TestPendingOpponent(), ref ok, ref fail);
            Check("Sables pending effect declines instead of self-bouncing", TestPendingNoOpponent(), ref ok, ref fail);
            Check("Sables Trigger is held when no opponent target exists", TestTriggerNoOpponent(), ref ok, ref fail);
            Check("Sables Trigger fires when an opponent target exists", TestTriggerWithOpponent(), ref ok, ref fail);
            Check("leader activation identity reaches ST03 Crocodile and Enel", TestActivationIdentity(registry), ref ok, ref fail);
            Check("target-dependent Main Trigger is held on an empty opposing board", TestMolePistolNoTarget(), ref ok, ref fail);
            Check("non-targeted end-of-next-turn Trigger resolves for real value", TestKongGatlingTrigger(), ref ok, ref fail);
            Check("end-of-next-turn power survives through the owner's next turn", TestEndOfNextTurnDuration(), ref ok, ref fail);
            Check("rested-DON Activate Main is recognized generically", TestRestedDonActivation(), ref ok, ref fail);
            Check("unreserved active DON is committed before attacks", TestDonCommitment(withCounterEvent: false), ref ok, ref fail);
            Check("active DON is reserved only for a real Counter Event", TestDonCommitment(withCounterEvent: true), ref ok, ref fail);
            // Advanced TriggerUtilityPolicy: general "resolve now vs. take into hand" mechanics.
            Check("advanced fires a buff Trigger that stops the current attack", TestUtilBeneficialBuffFires(), ref ok, ref fail);
            Check("advanced holds a debuff Trigger aimed at a spent target", TestUtilSpentDebuffHeld(), ref ok, ref fail);
            Check("advanced fires a removal Trigger against an expensive body", TestUtilRemovalFires(), ref ok, ref fail);
            Check("advanced fires a free-blocker Trigger with board room", TestUtilFreeBodyFires(), ref ok, ref fail);
            Check("advanced fires any surviving Trigger to prevent lethal", TestUtilLethalOverrideFires(), ref ok, ref fail);
            // WS-3 removal-capability foundation.
            Check("removal model classifies kinds from printed text", TestWs3ClassifyKinds(), ref ok, ref fail);
            Check("soft debuff prefers a live threat over a spent body", TestWs3SoftDebuffPrefersLiveThreat(), ref ok, ref fail);
            Check("permanent removal still takes the biggest body", TestWs3PermanentRemovalTakesBiggest(), ref ok, ref fail);
            Check("Enel detected as a DON-engine with a cap-6 always-on band", TestWs1EnelDonEngine(registry), ref ok, ref fail);
            Check("black Yamato detected as a trash-recursion deck", TestTrashRecursionDetected(registry), ref ok, ref fail);
            Check("trash bodies are valued as recur fuel for a recursion deck", TestTrashFuelValued(registry), ref ok, ref fail);
            Check("cost-down targets the KO-able body only when a finisher exists", TestWs2CostDownEnablesKo(), ref ok, ref fail);
            Check("DON-minus Main is used when its effect lands", TestOppUseMainWhenEffectLands(), ref ok, ref fail);
            Check("DON-minus card is held for Counter when threatened with no target", TestOppHoldForCounter(), ref ok, ref fail);
            Check("Lightning Dragon (no Counter) is HELD when its freeze has no rested target", TestLightningDragonHeld(), ref ok, ref fail);
            Check("Lightning Dragon is PLAYED when a rested freeze target exists", TestLightningDragonPlayed(), ref ok, ref fail);
            Check("Crocodile DON-4 bounce clears its cost for a real target", TestCrocBounceWorthCost(bigTarget: true), ref ok, ref fail);
            Check("Crocodile DON-4 bounce is NOT worth its cost on a cheap body", TestCrocBounceWorthCost(bigTarget: false), ref ok, ref fail);
            Check("DON-cost rule generalizes by DETECTED recovery, not card id", TestDonRecoveryGeneralizes(), ref ok, ref fail);
            Check("DON recovery is detected from CHARACTERS, not just the leader", TestCharacterDonRecovery(), ref ok, ref fail);
            Check("lethal pivot detects near-certain death next turn", TestDyingNextTurn(dying: true), ref ok, ref fail);
            Check("lethal pivot does NOT fire when defence can survive", TestDyingNextTurn(dying: false), ref ok, ref fail);
            Check("stall kit (bounce/freeze/life) declines the desperation pivot", TestStallDeclinesDesperation(), ref ok, ref fail);
            Check("restand gate is detected generically from leader text", TestLeaderRestandGate(), ref ok, ref fail);
            Check("restand leader loads its [DON!! xN] gate before attacking", TestRestandLeaderLoadsGate(), ref ok, ref fail);
            Check("threat model values a repeatable engine beyond its stats", TestEffectThreatDetected(), ref ok, ref fail);
            Check("removal targets a small engine over a bigger vanilla body", TestThreatTargetsEngine(), ref ok, ref fail);
            Check("lt01 Luffy ([DON!!x1]) leader activation probe", TestLt1LuffyLeaderProbe(), ref ok, ref fail);
            Check("advanced proactively sets up + fires the [DON!!x1] leader engine", TestLt1LuffyProactiveActivation(), ref ok, ref fail);
            Check("sacrifice-to-recur unlocks Yamato 6→8 trash upgrade", TestSacRecurYamato(), ref ok, ref fail);
            Check("sacrifice-to-recur reads the RECUR cost, not a trash-requirement cost", TestSacRecurMomoCostParse(), ref ok, ref fail);
            Check("sacrifice-to-recur rejects a downgrade (big body → small recur)", TestSacRecurRejectsDowngrade(), ref ok, ref fail);
            Check("sacrifice-to-recur rejects marginal self-sacrifice (no recur)", TestSacRecurRejectsSelfSac(), ref ok, ref fail);
            Check("no-cost recur is allowed only when sacrificing a spent (rested) body", TestSacRecurNoCostNeedsRested(), ref ok, ref fail);
            Check("countering with a Character does NOT fire its non-[Counter] \"Then,\" clause", TestCounterThenGuardBlocks(), ref ok, ref fail);
            Check("countering with a real [Counter] event STILL fires its \"Then,\" clause", TestCounterThenGuardAllows(), ref ok, ref fail);
            Check("a \"your Characters\" buff with an opponent-duration does NOT target opponent cards", TestBuffTargetOwnershipDuration(), ref ok, ref fail);
            Check("leader passive +power obeys its embedded cost condition", TestLeaderConditionalPowerGated(), ref ok, ref fail);
            Check("\"+1 cost to all\" aura is not suppressed by a later sentence's cost number", TestCostAuraNotSuppressed(), ref ok, ref fail);
            Check("compound self-buff hits its own source and does NOT leak to others", TestSelfBuffDoesNotLeak(), ref ok, ref fail);
            Console.WriteLine($"strategy fixture test: {ok} ok, {fail} fail");
            return fail == 0 ? 0 : 1;
        }

        // --- Sacrifice-to-recur unlock (generalized "trash this Character: play a bigger body from trash") ---
        // BeneficialActivateMain blanket-rejects "trash this character"; these witness the value-gated path
        // that restores the deliberate trash-to-recur burst engine (Yamato 6→8, Momonosuke 5→9, …).

        private static (bool ok, string detail) TestSacRecurYamato()
        {
            // OP16-098: trash the 6-cost to deploy an 8-cost from trash — a clear upgrade.
            const string e = "[On Play] Draw 1 card and trash 1 card from your hand. [Activate: Main] You may " +
                "trash this Character: Play up to 1 black [Yamato] with a cost of 8 from your trash.";
            bool pass = AdvancedActivationPolicy.IsSacrificeToRecur(e, ownCost: 6, cardRested: false);
            return (pass, pass ? "unlocked" : "still rejected");
        }

        private static (bool ok, string detail) TestSacRecurMomoCostParse()
        {
            // OP16-084: a leading "cost of 20 or more" is a trash REQUIREMENT, not the recur target (cost 9).
            const string e = "[Activate: Main] You may trash this Character with a cost of 20 or more: If you " +
                "have 9 or more DON!! cards on your field, play up to 1 [Kouzuki Momonosuke] with a cost of 9 " +
                "from your trash.";
            bool pass = AdvancedActivationPolicy.IsSacrificeToRecur(e, ownCost: 5, cardRested: false);
            return (pass, pass ? "recur cost 9 ≥ 5" : "misparsed trash-requirement cost");
        }

        private static (bool ok, string detail) TestSacRecurRejectsDowngrade()
        {
            // Trashing an 8-cost body to recur a 2-cost is card-negative — reject even if the body is spent.
            const string e = "[Activate: Main] You may trash this Character: Play up to 1 Character with a " +
                "cost of 2 from your trash.";
            bool pass = !AdvancedActivationPolicy.IsSacrificeToRecur(e, ownCost: 8, cardRested: true);
            return (pass, pass ? "downgrade rejected" : "fired a downgrade");
        }

        private static (bool ok, string detail) TestSacRecurRejectsSelfSac()
        {
            // Pure self-sacrifice for a marginal buff (no from-trash deploy) stays rejected.
            const string e = "[Activate: Main] You may trash this Character: This Leader gains +1000 power " +
                "during this turn.";
            bool pass = !AdvancedActivationPolicy.IsSacrificeToRecur(e, ownCost: 3, cardRested: false);
            return (pass, pass ? "self-sac rejected" : "fired a self-sac");
        }

        private static (bool ok, string detail) TestSacRecurNoCostNeedsRested()
        {
            // A recur with no printed target cost: allowed only when trashing an already-spent (rested) body.
            const string e = "[Activate: Main] You may trash this Character: Play up to 1 Character from your trash.";
            bool spent  = AdvancedActivationPolicy.IsSacrificeToRecur(e, ownCost: 4, cardRested: true);
            bool fresh  = AdvancedActivationPolicy.IsSacrificeToRecur(e, ownCost: 4, cardRested: false);
            bool pass = spent && !fresh;
            return (pass, $"spent={spent} fresh={fresh}");
        }

        // --- Bugs #2/#3/#4: multi-sentence aura strings must not cross-contaminate ----------------

        private static (bool ok, string detail) TestLeaderConditionalPowerGated()
        {
            // ST14-001: "[DON!! x1] … If you have a Character with a cost of 8 or more, this Leader gains
            // +1000 power." The +1000 must be OFF with no 8-cost Character, ON with one (delta == 1000).
            GameState Build(bool bigChar)
            {
                var st = BaseState();
                var lead = Card("S-LEADER", "ST14-001", "south", "leader");
                lead.AttachedDonIds.Add("LD0");                 // meet the [DON!! x1] gate
                st.Players["south"].Leader = lead;
                if (bigChar) st.Players["south"].CharacterArea[0] = Card("BIG", "EB02-004", "south", "character");
                return st;
            }
            var a = Build(false); int pNo  = GameEngine.GetPower(a, a.Players["south"].Leader);
            var b = Build(true);  int pYes = GameEngine.GetPower(b, b.Players["south"].Leader);
            return (pYes - pNo == 1000, $"noBig={pNo} withBig={pYes} delta={pYes - pNo}");
        }

        private static (bool ok, string detail) TestCostAuraNotSuppressed()
        {
            // ST14-001's "All of your Characters gain +1 cost" must apply to a low-cost Character; the
            // later sentence's "cost of 8 or more" must NOT be read as a recipient filter.
            var st = BaseState();
            var lead = Card("S-LEADER", "ST14-001", "south", "leader");
            lead.AttachedDonIds.Add("LD0");
            st.Players["south"].Leader = lead;
            var low = Card("LOW", "ST03-013", "south", "character");     // Boa, low cost
            st.Players["south"].CharacterArea[0] = low;
            int baseCost = GameEngine.GetCard(low).Cost;
            int eff = GameEngine.GetCost(st, low);
            return (eff == baseCost + 1, $"base={baseCost} effective={eff}");
        }

        private static (bool ok, string detail) TestSelfBuffDoesNotLeak()
        {
            // ST14-009 Franky: "[DON!! x1] [Opponent's Turn] If you have a Character with a cost of 6 or
            // more, this Character cannot be K.O.'d … and gains +2000 power." The +2000 must land on Franky
            // and NOT leak to other Characters.
            var st = BaseState();
            st.ActiveSeat = "north";                                     // opponent's turn → [Opponent's Turn] live for south
            var franky = Card("FRANKY", "ST14-009", "south", "character");
            franky.AttachedDonIds.Add("FD0");                            // meet the [DON!! x1] gate
            var other  = Card("OTHER", "EB02-004", "south", "character");// 8-cost vanilla: satisfies cost≥6 AND is the leak canary
            st.Players["south"].CharacterArea[0] = franky;
            st.Players["south"].CharacterArea[1] = other;
            int frankySelf = GameEngine.GetPower(st, franky) - GameEngine.GetCard(franky).Power;
            int otherLeak  = GameEngine.GetPower(st, other)  - GameEngine.GetCard(other).Power;
            return (frankySelf >= 2000 && otherLeak == 0, $"frankySelf=+{frankySelf} otherLeak=+{otherLeak}");
        }

        // --- Bug #1: an opponent-DURATION phrase must not flip target ownership -------------------

        private static (bool ok, string detail) TestBuffTargetOwnershipDuration()
        {
            // "… your Characters gains +2 cost until the end of your opponent's next turn" — the
            // duration's "opponent's" must not make the opponent's cards valid targets (Haredas class).
            var st = BaseState();
            var mine  = Card("MINE",   "ST03-013", "south", "character");
            var yours = Card("THEIRS", "ST03-009", "north", "character");
            st.Players["south"].CharacterArea[0] = mine;
            st.Players["north"].CharacterArea[0] = yours;
            var eff = new PendingEffect
            {
                Seat = "south", SourceInstanceId = "S-LEADER", TargetZone = EffectTargetZone.Play,
                Text = "Up to 1 of your Characters gains +2 cost until the end of your opponent's next turn.",
            };
            bool ownValid = GameEngine.IsValidEffectTarget(st, eff, mine);
            bool oppValid = GameEngine.IsValidEffectTarget(st, eff, yours);
            return (ownValid && !oppValid, $"ownChar={ownValid} oppChar={oppValid}");
        }

        // --- Bug #5: countering reads the [Counter] clause ONLY (228-card class) ------------------

        private static GameState CounterState(CardInstance counterCard)
        {
            var st = BaseState();
            st.Phase = "battle";
            st.Players["south"].Hand.Add(counterCard);
            for (int i = 0; i < 4; i++) st.Players["south"].CostArea.Add(new DonInstance { InstanceId = "SD" + i });
            st.Battle = new BattleState
            {
                Id = "B", Step = "counter", PrioritySeat = "south", TargetSeat = "south",
                AttackerSeat = "north", AttackerId = "N-LEADER", TargetId = "S-LEADER",
                AttackPower = 6000, DefensePower = 5000,
            };
            return st;
        }

        private static (bool ok, string detail) TestCounterThenGuardBlocks()
        {
            // ST14-008 Haredas: printed Counter 2000, but its "Then, … draw/trash" lives in the
            // [Activate: Main] text with NO [Counter] clause. Countering must add +2000 and NOT queue
            // the Activate:Main "Then," clause (the bug fired it via ExtractTimedClause's whole-text fallback).
            var st = CounterState(Card("HAREDAS", "ST14-008", "south", "hand"));
            int before = st.PendingEffects.Count;
            GameEngine.ApplyCommand(st, new GameCommand { Type = "counterWithCard", Seat = "south", InstanceId = "HAREDAS" });
            bool applied = st.Battle != null && st.Battle.CounterPower >= 2000;
            bool leaked  = st.PendingEffects.Skip(before).Any(e => e.Timing == "counter");
            return (applied && !leaked, $"counter+2000={applied} thenLeaked={leaked}");
        }

        private static (bool ok, string detail) TestCounterThenGuardAllows()
        {
            // EB01-019: a real "[Counter] … gains +4000 … Then, look at 3 cards …" event. The guard
            // must NOT suppress a genuine [Counter] "Then," clause — it should still queue.
            var st = CounterState(Card("CTR-EVENT", "EB01-019", "south", "hand"));
            int before = st.PendingEffects.Count;
            GameEngine.ApplyCommand(st, new GameCommand { Type = "counterWithCard", Seat = "south", InstanceId = "CTR-EVENT" });
            bool applied = st.Battle != null && st.Battle.CounterPower >= 4000;
            bool fired   = st.PendingEffects.Skip(before).Any(e => e.Timing == "counter");
            return (applied && fired, $"counter+4000={applied} thenFired={fired}");
        }

        private static (bool ok, string detail) TestMainEvent()
        {
            var st = BaseState();
            var sables = Card("SABLES-H", "ST03-015", "south", "hand");
            var boa = Card("BOA-H", "ST03-013", "south", "hand");
            st.Players["south"].Hand.Add(sables);
            st.Players["south"].Hand.Add(boa);
            for (int i = 0; i < 4; i++) st.Players["south"].CostArea.Add(new DonInstance { InstanceId = "D" + i });
            st.Players["north"].CharacterArea[0] = Card("ENEMY-7", "ST03-009", "north", "character");
            var cmd = IntermediateBot.DecideOneCommand(st, "south", new HashSet<string>());
            bool pass = cmd?.Type == "playCard" && cmd.InstanceId == sables.InstanceId;
            return (pass, cmd == null ? "null" : $"{cmd.Type}:{cmd.InstanceId}");
        }

        private static (bool ok, string detail) TestPendingOpponent()
        {
            var st = PendingState(withOpponent: true);
            var cmd = IntermediateBot.DecideOneCommand(st, "south", new HashSet<string>());
            bool pass = cmd?.Type == "resolveEffect" && cmd.Target == "ENEMY-7";
            return (pass, cmd == null ? "null" : $"{cmd.Type}:{cmd.Target}");
        }

        private static (bool ok, string detail) TestPendingNoOpponent()
        {
            var st = PendingState(withOpponent: false);
            var cmd = IntermediateBot.DecideOneCommand(st, "south", new HashSet<string>());
            bool pass = cmd?.Type == "passEffect";
            return (pass, cmd == null ? "null" : $"{cmd.Type}:{cmd.Target}");
        }

        private static (bool ok, string detail) TestTriggerNoOpponent()
        {
            var st = TriggerState(withOpponent: false);
            var cmd = IntermediateBot.DecideOneCommand(st, "south", new HashSet<string>());
            return (cmd?.Type == "passTrigger", cmd?.Type ?? "null");
        }

        private static (bool ok, string detail) TestTriggerWithOpponent()
        {
            var st = TriggerState(withOpponent: true);
            var cmd = IntermediateBot.DecideOneCommand(st, "south", new HashSet<string>());
            return (cmd?.Type == "useTrigger", cmd?.Type ?? "null");
        }

        private static (bool ok, string detail) TestActivationIdentity(DeckRegistry registry)
        {
            var croc = DeckFingerprint.Analyze(registry.Resolve("st03"));
            var enel = registry.Has("op16-p-enel") ? DeckFingerprint.Analyze(registry.Resolve("op16-p-enel")) : null;
            bool pass = croc.RequiresMainActivation && enel?.RequiresMainActivation == true;
            return (pass, $"croc={DeckFingerprint.Describe(croc)}; enel={(enel == null ? "missing" : DeckFingerprint.Describe(enel))}");
        }

        private static (bool ok, string detail) TestMolePistolNoTarget()
        {
            var st = TriggerState("ST21-017", withOpponent: false);
            var cmd = IntermediateBot.DecideOneCommand(st, "south", new HashSet<string>());
            return (cmd?.Type == "passTrigger", cmd?.Type ?? "null");
        }

        private static (bool ok, string detail) TestEndOfNextTurnDuration()
        {
            var st = BaseState();
            st.ActiveSeat = "north";
            st.TurnNumber = 6;
            st.TimedPowerBonuses.Add(new TimedPowerBonus
            {
                TargetInstanceId = st.Players["south"].Leader.InstanceId,
                Delta = 1000, OwnerSeat = "south", Duration = "endOfNextTurn",
            });
            int before = GameEngine.GetPower(st, st.Players["south"].Leader);
            GameEngine.ApplyCommand(st, new GameCommand { Type = "endTurn", Seat = "north" });
            int during = GameEngine.GetPower(st, st.Players["south"].Leader);
            GameEngine.ApplyCommand(st, new GameCommand { Type = "endTurn", Seat = "south" });
            int after = GameEngine.GetPower(st, st.Players["south"].Leader);
            bool pass = before == 6000 && during == 6000 && after == 5000;
            return (pass, $"before={before} during-own-turn={during} after={after}");
        }

        private static (bool ok, string detail) TestKongGatlingTrigger()
        {
            var st = TriggerState("ST10-016", withOpponent: false);
            st.ActiveSeat = "north"; // south is taking damage during north's turn
            var use = IntermediateBot.DecideOneCommand(st, "south", new HashSet<string>());
            if (use?.Type != "useTrigger") return (false, use?.Type ?? "null");
            GameEngine.ApplyCommand(st, use);
            var resolve = IntermediateBot.DecideOneCommand(st, "south", new HashSet<string>());
            if (resolve?.Type != "resolveEffect") return (false, $"after-use={resolve?.Type ?? "null"}");
            GameEngine.ApplyCommand(st, resolve);
            var timed = st.TimedPowerBonuses.FirstOrDefault(b =>
                b.TargetInstanceId == st.Players["south"].Leader.InstanceId && b.Delta == 1000);
            int power = GameEngine.GetPower(st, st.Players["south"].Leader);
            return (timed?.Duration == "endOfNextTurn" && power == 6000,
                $"duration={timed?.Duration ?? "missing"} power={power}");
        }

        private static (bool ok, string detail) TestRestedDonActivation()
        {
            var st = BaseState();
            st.Players["south"].Leader = Card("S-LUFFY", "ST01-001", "south", "leader");
            st.Players["south"].CostArea.Add(new DonInstance { InstanceId = "RESTED-DON", Rested = true });
            var cmd = AdvancedActivationPolicy.Decide(st, "south",
                new HashSet<string>(), new HashSet<string>());
            return (cmd?.Type == "activateMain" && cmd.Target == "S-LUFFY",
                cmd == null ? "null" : $"{cmd.Type}:{cmd.Target}");
        }

        private static (bool ok, string detail) TestDonCommitment(bool withCounterEvent)
        {
            var st = BaseState();
            if (withCounterEvent)
                st.Players["south"].Hand.Add(Card("GUARD", "ST01-014", "south", "hand"));
            for (int i = 0; i < 5; i++)
                st.Players["south"].CostArea.Add(new DonInstance { InstanceId = "COMMIT-D" + i });

            GameCommand next = null;
            int attached = 0;
            for (int i = 0; i < 6; i++)
            {
                next = IntermediateBot.DecideOneCommand(st, "south", new HashSet<string>());
                if (next?.Type != "attachDon") break;
                GameEngine.ApplyCommand(st, next);
                attached += next.Amount ?? 0;
            }
            int active = GameEngine.ActiveDonCount(st.Players["south"]);
            int expectedReserve = withCounterEvent ? 1 : 0;
            bool pass = attached == 5 - expectedReserve && active == expectedReserve
                && next?.Type == "declareAttack";
            return (pass, $"attached={attached} active={active} next={next?.Type ?? "null"}");
        }

        // ---- Advanced TriggerUtilityPolicy: general use/hold mechanics (not card-id witnesses) ----

        // A +power Trigger that raises the target above the incoming attack (so it no longer connects) is
        // beneficial and must fire, even though the revealed card would otherwise enter hand.
        private static (bool ok, string detail) TestUtilBeneficialBuffFires()
        {
            var st = BattleTrigger("ST10-016", withOpponent: false, life: 3); // Kong Gatling: +1000 to Leader
            bool use = TriggerUtilityPolicy.ShouldUse(st, "south");
            return (use, use ? "useTrigger" : "passTrigger");
        }

        // A "-power during this turn" Trigger aimed at a Character that has ALREADY attacked (rested) with no
        // K.O. payoff resolves for zero value; taking the card into hand is strictly better.
        private static (bool ok, string detail) TestUtilSpentDebuffHeld()
        {
            var st = BattleTrigger("ST21-017", withOpponent: true, life: 3); // Mole Pistol: -5000, conditional K.O.
            st.Players["north"].CharacterArea[0].Rested = true;               // the enemy body already attacked
            bool use = TriggerUtilityPolicy.ShouldUse(st, "south");
            return (!use, use ? "useTrigger" : "passTrigger");
        }

        // Removing an expensive opposing body (bounce/K.O.) beats holding the card.
        private static (bool ok, string detail) TestUtilRemovalFires()
        {
            var st = BattleTrigger("ST03-015", withOpponent: true, life: 3); // Sables bounces the 7-cost Doflamingo
            bool use = TriggerUtilityPolicy.ShouldUse(st, "south");
            return (use, use ? "useTrigger" : "passTrigger");
        }

        // A "Play this card" Trigger that develops a free (blocker) body while attacks remain should fire.
        private static (bool ok, string detail) TestUtilFreeBodyFires()
        {
            var st = BattleTrigger("ST03-013", withOpponent: true, life: 3); // Boa: Play this card / Blocker
            st.Players["south"].CharacterArea[0] = null;                     // clear a slot so there is board room
            bool use = TriggerUtilityPolicy.ShouldUse(st, "south");
            return (use, use ? "useTrigger" : "passTrigger");
        }

        // At lethal, a Trigger that prevents the killing hit must fire regardless of the held card's value.
        private static (bool ok, string detail) TestUtilLethalOverrideFires()
        {
            var st = BattleTrigger("ST10-016", withOpponent: false, life: 1); // last Life; buff flips the lethal hit
            bool use = TriggerUtilityPolicy.ShouldUse(st, "south");
            return (use, use ? "useTrigger" : "passTrigger");
        }

        // End-to-end: from a Luffy turn with NO DON on the leader (so the raw activation would no-op), the
        // advanced activation layer should attach a DON to meet [DON!! x1] and then fire the leader ability —
        // exactly the DON→leader→give-to-attacker line. Proves the proactive setup, not just legality.
        private static (bool ok, string detail) TestLt1LuffyProactiveActivation()
        {
            var st = BaseState();
            var leader = Card("S-LTLUFFY", "ST21-001", "south", "leader");
            st.Players["south"].Leader = leader;
            st.Players["south"].TurnsStarted = 3;
            st.Players["south"].CharacterArea[0] = Card("MY-CHAR", "ST03-013", "south", "character"); // recipient
            st.Players["south"].CostArea.Add(new DonInstance { InstanceId = "A0", Rested = false });     // 1 active
            for (int i = 0; i < 3; i++) st.Players["south"].CostArea.Add(new DonInstance { InstanceId = "R" + i, Rested = true });
            var attempted = new HashSet<string>(); var bl = new HashSet<string>();
            string trail = "";
            for (int i = 0; i < 10; i++)
            {
                var cmd = AdvancedActivationPolicy.Decide(st, "south", bl, attempted);
                if (cmd == null || cmd.Type == "endTurn") break;
                trail += $"{cmd.Type}:{cmd.Target};";
                object snap = IntermediateBot.SnapshotFor(st, cmd);
                GameEngine.ApplyCommand(st, cmd);
                if (!IntermediateBot.Succeeded(st, cmd, snap)) bl.Add(IntermediateBot.Signature(cmd));
                if (st.Players["south"].AbilityUsedThisTurn.Contains(leader.InstanceId)) break;
            }
            bool used = st.Players["south"].AbilityUsedThisTurn.Contains(leader.InstanceId);
            return (used, $"leaderUsed={used} trail=[{trail}]");
        }

        // Probe why lt01 Luffy (leader ST21-001, "[DON!! x1] [Activate: Main] give up to 2 rested DON!! to a
        // Character") never fires in play. Tests, in order: (A) engine legality with the [DON!! x1] and rested
        // DON set up; (B) whether the activation is legal WITHOUT the [DON!! x1] (should be illegal); (C)
        // whether the greedy/activation policy proposes it when fully set up. Diagnostic — always reports ok
        // so we can read the three outcomes.
        private static (bool ok, string detail) TestLt1LuffyLeaderProbe()
        {
            string EngineTakes(bool attachLeaderDon)
            {
                var st = BaseState();
                st.Players["south"].Leader = Card("S-LTLUFFY", "ST21-001", "south", "leader");
                st.Players["south"].CharacterArea[0] = Card("MY-CHAR", "ST03-013", "south", "character");
                for (int i = 0; i < 3; i++) st.Players["south"].CostArea.Add(new DonInstance { InstanceId = "R" + i, Rested = true });
                if (attachLeaderDon)
                {
                    st.Players["south"].CostArea.Add(new DonInstance { InstanceId = "LD", Rested = false });
                    st.Players["south"].Leader.AttachedDonIds.Add("LD");
                }
                var cmd = new GameCommand { Type = "activateMain", Seat = "south", Target = "S-LTLUFFY" };
                var clone = GameClone.Clone(st);
                long before = OnePieceTcg.Sim.Search.LegalActions.StateFingerprint(clone);
                GameEngine.ApplyCommand(clone, cmd);
                bool changed = OnePieceTcg.Sim.Search.LegalActions.StateFingerprint(clone) != before;
                return changed ? "engine-accepts" : "engine-noop";
            }
            string withDon = EngineTakes(true);
            string withoutDon = EngineTakes(false);
            // Policy proposal when fully set up (and greedy would otherwise attach/attack/end).
            var ps = BaseState();
            ps.Players["south"].Leader = Card("S-LTLUFFY", "ST21-001", "south", "leader");
            ps.Players["south"].CharacterArea[0] = Card("MY-CHAR", "ST03-013", "south", "character");
            for (int i = 0; i < 3; i++) ps.Players["south"].CostArea.Add(new DonInstance { InstanceId = "R" + i, Rested = true });
            ps.Players["south"].CostArea.Add(new DonInstance { InstanceId = "LD", Rested = false });
            ps.Players["south"].Leader.AttachedDonIds.Add("LD");
            var pol = AdvancedActivationPolicy.Decide(ps, "south", new HashSet<string>(), new HashSet<string>());
            // Engine + policy are correct: the ability fires WHEN set up (DON on leader + rested DON) and is
            // gated off WITHOUT the [DON!! x1]. The real-game 0 is therefore a SETUP gap, not an engine bug.
            bool pass = withDon == "engine-accepts" && withoutDon == "engine-noop" && pol?.Type == "activateMain";
            return (pass, $"engineWith[DON!!x1]={withDon}  engineWithout={withoutDon}  policyProposes={pol?.Type}:{pol?.Target}");
        }

        // Opportunity-cost model: the SAME El Thor (Main = buff + K.O. ≤3000 / [Counter] +2000) is USED when
        // its K.O. has a legal target and we are safe, but HELD for Counter when there is no target and we are
        // low on Life under a live attacker. The line flips on board+life, not on the card id.
        private static (bool ok, string detail) TestOppUseMainWhenEffectLands()
        {
            var st = DonHoldState(koTargetAvailable: true, life: 5);
            var el = st.Players["south"].Hand.First(c => c.CardId == "OP15-075");
            bool use = DonOpportunityModel.ShouldUseMain(st, "south", el);
            double mv = DonOpportunityModel.MainEffectValue(st, "south", CardData.GetCard("OP15-075").Effect);
            double cv = DonOpportunityModel.CounterHoldValue(st, "south", el);
            return (use, $"use={use} main={mv:0.00} counterHold={cv:0.00}");
        }

        private static (bool ok, string detail) TestOppHoldForCounter()
        {
            var st = DonHoldState(koTargetAvailable: false, life: 2);
            var el = st.Players["south"].Hand.First(c => c.CardId == "OP15-075");
            bool use = DonOpportunityModel.ShouldUseMain(st, "south", el);
            double mv = DonOpportunityModel.MainEffectValue(st, "south", CardData.GetCard("OP15-075").Effect);
            double cv = DonOpportunityModel.CounterHoldValue(st, "south", el);
            return (!use, $"use={use} main={mv:0.00} counterHold={cv:0.00}");
        }

        // Recovery is not leader-only: a re-ramp CHARACTER (Senor Pink sets a DON!! active) makes DON!! cheap
        // to return even under a leader that has no recovery of its own. Without it on board → not fast.
        private static (bool ok, string detail) TestCharacterDonRecovery()
        {
            var st = BaseState();
            st.Players["south"].Leader = Card("L", "ST01-001", "south", "leader"); // Luffy leader: no DON recovery
            bool without = DonResourceProfile.Build(st, "south").FastRecovery;
            st.Players["south"].CharacterArea[0] = Card("SP", "OP10-067", "south", "character"); // Senor Pink re-ramp
            bool with = DonResourceProfile.Build(st, "south").FastRecovery;
            return (!without && with, $"noCharacter={without} withReRampCharacter={with}");
        }

        // The DON-cost gate must NOT be Crocodile-specific — any deck can be bot-piloted. The behaviour is
        // driven by the DETECTED resource profile: a re-ramp leader (Enel) recovers returned DON!! so its cost
        // is cheap; a no-recovery leader (Crocodile) does not, so its DON!! −N activations must earn their cost.
        // Same rule, opposite outcome, zero card ids.
        private static (bool ok, string detail) TestDonRecoveryGeneralizes()
        {
            var enel = BaseState(); enel.Players["south"].Leader = Card("E", "OP15-058", "south", "leader");
            var croc = BaseState(); croc.Players["south"].Leader = Card("C", "ST03-001", "south", "leader");
            bool enelFast = DonResourceProfile.Build(enel, "south").FastRecovery;
            bool crocFast = DonResourceProfile.Build(croc, "south").FastRecovery;
            return (enelFast && !crocFast, $"enel(re-ramp)={enelFast} croc(no-recovery)={crocFast}");
        }

        // Threat is more than stats: a repeatable [Activate: Main] engine (Arlong's per-turn rest/freeze) scores
        // an effect-threat bonus; a body with only an On-Play (Senor Pink) scores none. Text-driven.
        private static (bool ok, string detail) TestEffectThreatDetected()
        {
            double engine = IntermediateBot.EffectThreat(CardData.GetCard("OP15-023"));  // Arlong: Activate:Main engine
            double vanilla = IntermediateBot.EffectThreat(CardData.GetCard("OP10-067")); // Senor Pink: On-Play only
            return (engine > 0 && vanilla == 0, $"engineThreat={engine} onPlayOnly={vanilla}");
        }

        // A small body with a per-turn engine (Arlong, cost4/5000) is a higher-priority removal target than a
        // bigger vanilla body (Senor Pink, cost5/6000) — the threat model flips the choice that raw stats miss.
        private static (bool ok, string detail) TestThreatTargetsEngine()
        {
            var st = BaseState();
            st.Players["north"].CharacterArea[0] = Card("ARLONG", "OP15-023", "north", "character");   // engine
            st.Players["north"].CharacterArea[1] = Card("BIGVANILLA", "OP10-067", "north", "character"); // bigger, no engine
            st.PendingEffects.Add(new PendingEffect
            {
                EffectId = "KO", Seat = "south", SourceCardId = "OP11-106", SourceInstanceId = "KO-SRC",
                Timing = "main", Text = "K.O. up to 1 of your opponent's Characters.",
                Optional = true, Scope = EffectScope.Instant, TargetZone = EffectTargetZone.Play,
            });
            var cmd = IntermediateBot.DecideOneCommand(st, "south", new HashSet<string>());
            bool pass = cmd?.Type == "resolveEffect" && cmd.Target == "ARLONG";
            return (pass, cmd == null ? "null" : $"{cmd.Type}:{cmd.Target}");
        }

        // The restand engine is text-driven: Zoro's leader has "[DON!! x3] … set this Leader as active" → gate 3;
        // a non-restand leader (Crocodile) → -1. No card ids.
        private static (bool ok, string detail) TestLeaderRestandGate()
        {
            var z = BaseState(); z.Players["south"].Leader = Card("Z", "OP12-020", "south", "leader");
            var n = BaseState(); n.Players["south"].Leader = Card("N", "ST03-001", "south", "leader"); // no restand
            int zg = IntermediateBot.LeaderRestandGate(z, "south");
            int ng = IntermediateBot.LeaderRestandGate(n, "south");
            return (zg == 3 && ng == -1, $"zoroGate={zg} nonRestandLeader={ng}");
        }

        // With a rested Character to K.O. and an unused restand, the bot loads the Leader's [DON!! x3] gate
        // first (so it can K.O. the body then restand for a face swing) rather than spreading DON!! elsewhere.
        private static (bool ok, string detail) TestRestandLeaderLoadsGate()
        {
            var st = BaseState();
            st.Players["south"].Leader = Card("S-ZORO", "OP12-020", "south", "leader");
            st.Players["south"].TurnsStarted = 3;
            for (int i = 0; i < 3; i++) st.Players["south"].CostArea.Add(new DonInstance { InstanceId = "D" + i });
            var opp = Card("OPP", "OP12-071", "north", "character"); opp.Rested = true; // a K.O. target
            st.Players["north"].CharacterArea[0] = opp;
            var cmd = IntermediateBot.DecideOneCommand(st, "south", new HashSet<string>());
            bool pass = cmd?.Type == "attachDon" && cmd.Target == "S-ZORO";
            return (pass, cmd == null ? "null" : $"{cmd.Type}:{cmd.Target}");
        }

        // Same near-lethal board (2 Life, four incoming attacks) triggers the all-in pivot — UNLESS a stall
        // kit is in hand. Three bounce Events + the DON!! to cast them can weather the turn, so the bot should
        // decline desperation and stall/stabilise instead of throwing the game.
        private static (bool ok, string detail) TestStallDeclinesDesperation()
        {
            GameState Build(bool withStall)
            {
                var st = BaseState();
                for (int i = 0; i < 2; i++) st.Players["south"].Life.Add(Card("SL" + i, "ST03-002", "south", "life"));
                st.Players["north"].Leader = Card("NL", "ST01-001", "north", "leader");
                for (int i = 0; i < 3; i++) st.Players["north"].CharacterArea[i] = Card("NC" + i, "ST03-009", "north", "character");
                for (int i = 0; i < 4; i++) st.Players["south"].CostArea.Add(new DonInstance { InstanceId = "D" + i });
                if (withStall)
                    for (int i = 0; i < 3; i++) st.Players["south"].Hand.Add(Card("SB" + i, "ST03-015", "south", "hand")); // Sables bounce
                return st;
            }
            bool without = IntermediateBot.DyingNextTurn(Build(false), "south");
            bool with = IntermediateBot.DyingNextTurn(Build(true), "south");
            return (without && !with, $"withoutStall={without} withStallKit={with}");
        }

        // The lethal/desperation pivot: with 1 Life and four incoming attacks and no defence, death next turn
        // is near-certain, so the bot should abandon conservative play. With 5 Life, one attacker and a blocker,
        // defence survives, so it must NOT pivot (throwing away defence would lose a winnable game).
        private static (bool ok, string detail) TestDyingNextTurn(bool dying)
        {
            var st = BaseState();
            if (dying)
            {
                st.Players["south"].Life.Add(Card("S-LIFE", "ST03-002", "south", "life")); // 1 Life
                st.Players["north"].Leader = Card("N-LEAD", "ST01-001", "north", "leader");
                for (int i = 0; i < 3; i++) st.Players["north"].CharacterArea[i] = Card("NC" + i, "ST03-009", "north", "character");
            }
            else
            {
                for (int i = 0; i < 5; i++) st.Players["south"].Life.Add(Card("S-LIFE-" + i, "ST03-002", "south", "life")); // 5 Life
                st.Players["north"].Leader = Card("N-LEAD", "ST01-001", "north", "leader");
                st.Players["north"].CharacterArea[0] = Card("NC0", "ST03-009", "north", "character");
                st.Players["south"].CharacterArea[0] = Card("S-BLOCK", "ST03-013", "south", "character"); // Boa blocker
            }
            bool d = IntermediateBot.DyingNextTurn(st, "south");
            return (d == dying, $"dying={d} want={dying}");
        }

        // Crocodile (ST03-001) bounces with "DON!! −4" and has NO re-ramp, so returning 4 DON!! is real tempo:
        // the bounce must hit a target worth it. Big target (cost-5 6000) clears the cost; a cheap body does not.
        private static (bool ok, string detail) TestCrocBounceWorthCost(bool bigTarget)
        {
            var st = BaseState();
            st.Players["south"].Leader = Card("S-CROC", "ST03-001", "south", "leader");
            var body = bigTarget
                ? Card("OPP-BIG", "OP10-067", "north", "character")    // cost 5, 6000 power → worth 4 DON
                : Card("OPP-SMALL", "OP12-071", "north", "character"); // cost 1, 2000 power → not worth 4 DON
            st.Players["north"].CharacterArea[0] = body;
            bool clears = DonOpportunityModel.ActivationClearsDonCost(st, "south", CardData.GetCard("ST03-001").Effect);
            double mv = DonOpportunityModel.MainEffectValue(st, "south", CardData.GetCard("ST03-001").Effect);
            bool want = bigTarget;   // fires for the big target, holds for the cheap one
            return (clears == want, $"clears={clears} want={want} bounceValue={mv:0.00} (cost=4 DON, no recovery)");
        }

        // The Lightning Dragon case codex flagged as most valuable: a no-Counter DON-minus event (draw + freeze
        // a rested Character ≤6000). With no rested target the freeze is wasted, so the card should be HELD for a
        // turn when a target appears — the old gate could not decide this at all (it only gated Counter events).
        private static (bool ok, string detail) TestLightningDragonHeld()
        {
            var st = LightningDragonState(restedTarget: false);
            var ld = st.Players["south"].Hand.First(c => c.CardId == "OP15-077");
            bool use = DonOpportunityModel.ShouldUseMain(st, "south", ld);
            double mv = DonOpportunityModel.MainEffectValue(st, "south", CardData.GetCard("OP15-077").Effect);
            double ov = DonOpportunityModel.OptionValueOfHolding(st, "south", CardData.GetCard("OP15-077").Effect);
            return (!use, $"use={use} main={mv:0.00} optionHold={ov:0.00}");
        }

        private static (bool ok, string detail) TestLightningDragonPlayed()
        {
            var st = LightningDragonState(restedTarget: true);
            var ld = st.Players["south"].Hand.First(c => c.CardId == "OP15-077");
            bool use = DonOpportunityModel.ShouldUseMain(st, "south", ld);
            double mv = DonOpportunityModel.MainEffectValue(st, "south", CardData.GetCard("OP15-077").Effect);
            return (use, $"use={use} main={mv:0.00}");
        }

        private static GameState LightningDragonState(bool restedTarget)
        {
            var st = BaseState();
            st.Players["south"].Leader = Card("S-ENEL", "OP15-058", "south", "leader");
            st.Players["south"].Hand.Add(Card("LDRAGON", "OP15-077", "south", "hand")); // DON−1: draw + freeze ≤6000
            for (int i = 0; i < 4; i++) st.Players["south"].Life.Add(Card("S-LIFE-" + i, "ST03-002", "south", "life"));
            var body = Card("OPP-BODY", "OP12-071", "north", "character"); // 2000 power (≤6000)
            body.Rested = restedTarget;                                    // rested → a legal freeze target; active → none
            st.Players["north"].CharacterArea[0] = body;
            return st;
        }

        private static GameState DonHoldState(bool koTargetAvailable, int life)
        {
            var st = BaseState();
            st.Players["south"].Leader = Card("S-ENEL", "OP15-058", "south", "leader");
            st.Players["south"].Hand.Add(Card("ELTHOR", "OP15-075", "south", "hand")); // Main: buff + K.O. ≤3000; Counter +2000
            for (int i = 0; i < life; i++)
                st.Players["south"].Life.Add(Card("S-LIFE-" + i, "ST03-002", "south", "life"));
            var body = koTargetAvailable
                ? Card("OPP-KO", "OP12-071", "north", "character")   // 2000 power, cost 1 → K.O.-able at ≤3000
                : Card("OPP-BIG", "ST03-009", "north", "character");  // 7000 power → out of K.O. range
            body.Rested = false;                                      // a live attacker
            st.Players["north"].CharacterArea[0] = body;
            return st;
        }

        // WS-2: a −cost debuff is a K.O. SETUP when a KO-by-cost finisher can remove the reduced-cost body.
        // With the finisher in hand the debuff targets the big (spent) body the combo can K.O.; without it,
        // the debuff stays soft and goes to the live body. The target FLIPS with the finisher's presence.
        private static (bool ok, string detail) TestWs2CostDownEnablesKo()
        {
            var with = CostComboState(withKoFinisher: true);
            var c1 = IntermediateBot.DecideOneCommand(with, "south", new HashSet<string>());
            var without = CostComboState(withKoFinisher: false);
            var c2 = IntermediateBot.DecideOneCommand(without, "south", new HashSet<string>());
            bool pass = c1?.Target == "OPP-BIG" && c2?.Target == "OPP-SMALL";
            return (pass, $"withFinisher={c1?.Type}:{c1?.Target} withoutFinisher={c2?.Type}:{c2?.Target}");
        }

        private static GameState CostComboState(bool withKoFinisher)
        {
            var st = BaseState();
            var big = Card("OPP-BIG", "ST03-009", "north", "character");   // cost 7; -3 → 4, K.O.-able at ≤4
            big.Rested = true;                                             // spent, so WS-3 alone would skip it
            var small = Card("OPP-SMALL", "OP12-071", "north", "character"); // cost 1, live
            small.Rested = false;
            st.Players["north"].CharacterArea[0] = big;
            st.Players["north"].CharacterArea[1] = small;
            if (withKoFinisher) // Black Hole: "…if that Character has a cost of 4 or less, K.O. it."
                st.Players["south"].Hand.Add(Card("KO-FINISHER", "OP09-098", "south", "hand"));
            st.PendingEffects.Add(new PendingEffect
            {
                EffectId = "WS2-EFFECT", Seat = "south", SourceCardId = "OP09-083",
                SourceInstanceId = "WS2-SRC", Timing = "main",
                Text = "Give up to 1 of your opponent's Characters -3 cost during this turn.",
                Optional = true, Scope = EffectScope.Instant, TargetZone = EffectTargetZone.Play,
            });
            return st;
        }

        private static (bool ok, string detail) TestTrashRecursionDetected(DeckRegistry registry)
        {
            var yam = DeckFingerprint.Analyze(registry.Resolve("op16-black-yamato"));
            var lucy = DeckFingerprint.Analyze(registry.Resolve("op16-blue-lucy"));
            return (yam.IsTrashRecursion && !lucy.IsTrashRecursion,
                $"yamato recurCards={yam.TrashRecurCards}(recursion={yam.IsTrashRecursion}) lucy={lucy.TrashRecurCards}");
        }

        // For a recursion deck, bodies in the trash are recur fuel — a stocked trash scores higher than an
        // empty one, so self-mill/trades that fill it are correctly seen as building a resource, not card loss.
        private static (bool ok, string detail) TestTrashFuelValued(DeckRegistry registry)
        {
            var ctx = DeckFingerprint.Analyze(registry.Resolve("op16-black-yamato"));
            var empty = BaseState();
            var full = BaseState();
            for (int i = 0; i < 5; i++) full.Players["south"].Trash.Add(Card("T" + i, "ST03-009", "south", "trash"));
            bool prev = ValueFunction.TrashRecursionAware;
            ValueFunction.TrashRecursionAware = true;   // measured inert so default-off; force it to test the term
            double se = ValueFunction.Score(empty, "south", ValueFunction.DefaultWeights(), ctx);
            double sf = ValueFunction.Score(full, "south", ValueFunction.DefaultWeights(), ctx);
            ValueFunction.TrashRecursionAware = prev;
            return (sf > se, $"emptyTrash={se:0.00} fullTrash={sf:0.00}");
        }

        private static (bool ok, string detail) TestWs1EnelDonEngine(DeckRegistry registry)
        {
            if (!registry.Has("op16-p-enel")) return (false, "op16-p-enel missing");
            var ctx = DeckFingerprint.Analyze(registry.Resolve("op16-p-enel"));
            // Leader shrinks the DON deck to 6; the list keys off "6 or less DON"; cap ≤ band ⇒ always on.
            bool pass = ctx.DonDeckCap == 6 && ctx.DonBandThreshold == 6 && ctx.DonBandCards >= 3
                && ctx.IsDonEngine && ctx.DonBandAlwaysOn;
            return (pass, $"cap={ctx.DonDeckCap} band={ctx.DonBandThreshold} bandCards={ctx.DonBandCards} " +
                $"engine={ctx.IsDonEngine} alwaysOn={ctx.DonBandAlwaysOn} donMinus={ctx.DonMinusEffects}");
        }

        private static (bool ok, string detail) TestWs3ClassifyKinds()
        {
            (string text, RemovalKind expect)[] cases =
            {
                ("[Main] K.O. up to 1 of your opponent's Characters with 7000 power or less.", RemovalKind.Ko),
                ("[Main] Return up to 1 Character with a cost of 7 or less to the owner's hand.", RemovalKind.Bounce),
                // A bounce whose COST is DON-minus must still classify as Bounce (ST03 Crocodile's leader).
                ("[Activate: Main] DON!! -4 (You may return the specified number of DON!! cards from your field to your DON!! deck.): Return up to 1 Character with a cost of 5 or less to the owner's hand.", RemovalKind.Bounce),
                ("Give up to 1 of your opponent's Characters -5000 power during this turn.", RemovalKind.PowerDown),
                ("Give up to 1 of your opponent's Characters -4 cost during this turn.", RemovalKind.CostDown),
                ("[Main] DON!! -2: Draw 1 card. Then, rest up to 1 of your opponent's Characters with 5000 power or less.", RemovalKind.Rest),
                ("Up to 1 of your opponent's rested Characters will not become active in your opponent's next Refresh Phase.", RemovalKind.Freeze),
            };
            foreach (var (text, expect) in cases)
            {
                var got = RemovalModel.Classify(text);
                if (got != expect) return (false, $"'{text.Substring(0, System.Math.Min(28, text.Length))}...' => {got}, want {expect}");
            }
            return (true, $"{cases.Length} kinds classified");
        }

        private static (bool ok, string detail) TestWs3SoftDebuffPrefersLiveThreat()
        {
            var st = SoftDebuffState("Give up to 1 of your opponent's Characters -3000 power during this turn.");
            var cmd = IntermediateBot.DecideOneCommand(st, "south", new HashSet<string>());
            // OPP-SMALL is the unrested (live) low-value body; OPP-BIG is the rested (spent) high-value one.
            bool pass = cmd?.Type == "resolveEffect" && cmd.Target == "OPP-SMALL";
            return (pass, cmd == null ? "null" : $"{cmd.Type}:{cmd.Target}");
        }

        private static (bool ok, string detail) TestWs3PermanentRemovalTakesBiggest()
        {
            var st = SoftDebuffState("Return up to 1 of your opponent's Characters to the owner's hand.");
            var cmd = IntermediateBot.DecideOneCommand(st, "south", new HashSet<string>());
            // Permanent removal is worth full investment, so it takes the big body even though it is rested.
            bool pass = cmd?.Type == "resolveEffect" && cmd.Target == "OPP-BIG";
            return (pass, cmd == null ? "null" : $"{cmd.Type}:{cmd.Target}");
        }

        // South resolves a pending effect; north shows a spent high-value body and a live low-value body.
        private static GameState SoftDebuffState(string text)
        {
            var st = BaseState();
            var big = Card("OPP-BIG", "ST03-009", "north", "character");   // cost 7, 7000 power, no Blocker
            big.Rested = true;                                             // already attacked → spent
            var small = Card("OPP-SMALL", "OP12-071", "north", "character"); // cost 1, 2000 power
            small.Rested = false;                                          // live, can still attack
            st.Players["north"].CharacterArea[0] = big;
            st.Players["north"].CharacterArea[1] = small;
            st.PendingEffects.Add(new PendingEffect
            {
                EffectId = "WS3-EFFECT", Seat = "south", SourceCardId = "ST21-017",
                SourceInstanceId = "WS3-SRC", Timing = "main", Text = text,
                Optional = true, Scope = EffectScope.Instant, TargetZone = EffectTargetZone.Play,
            });
            return st;
        }

        // A live attack at the Trigger step: north's Leader is swinging at south's Leader, south is deciding.
        private static GameState BattleTrigger(string cardId, bool withOpponent, int life)
        {
            var st = TriggerState(cardId, withOpponent);
            st.ActiveSeat = "north"; // opponent's turn — south is on defence
            for (int i = 0; i < life; i++)
                st.Players["south"].Life.Add(Card("S-LIFE-" + i, "ST03-002", "south", "life"));
            return st;
        }

        private static GameState PendingState(bool withOpponent)
        {
            var st = BaseState();
            st.Players["south"].CharacterArea[0] = Card("BOA-BOARD", "ST03-013", "south", "character");
            if (withOpponent) st.Players["north"].CharacterArea[0] = Card("ENEMY-7", "ST03-009", "north", "character");
            var def = CardData.GetCard("ST03-015");
            st.PendingEffects.Add(new PendingEffect
            {
                EffectId = "SABLES-EFFECT", Seat = "south", SourceCardId = "ST03-015",
                SourceInstanceId = "SABLES-LIFE", Timing = "trigger", Text = def.Effect,
                Optional = true, Scope = EffectScope.Instant, TargetZone = EffectTargetZone.Play,
            });
            return st;
        }

        private static GameState TriggerState(bool withOpponent)
            => TriggerState("ST03-015", withOpponent);

        private static GameState TriggerState(string cardId, bool withOpponent)
        {
            var st = BaseState();
            st.Phase = "battle";
            st.Players["south"].CharacterArea[0] = Card("BOA-BOARD", "ST03-013", "south", "character");
            if (withOpponent) st.Players["north"].CharacterArea[0] = Card("ENEMY-7", "ST03-009", "north", "character");
            st.Battle = new BattleState
            {
                Id = "B", Step = "trigger", PrioritySeat = "south", TargetSeat = "south",
                AttackerSeat = "north", AttackerId = "N-LEADER", TargetId = "S-LEADER",
                RevealedLife = Card("TRIGGER-LIFE", cardId, "south", "life"),
            };
            return st;
        }

        private static GameState BaseState()
        {
            var south = new PlayerState { Seat = "south", Leader = Card("S-LEADER", "ST03-001", "south", "leader"), TurnsStarted = 3 };
            var north = new PlayerState { Seat = "north", Leader = Card("N-LEADER", "ST01-001", "north", "leader"), TurnsStarted = 3 };
            // Applying a command runs the engine's rule processing; keep these focused fixtures
            // out of the unrelated empty-deck loss path.
            for (int i = 0; i < 12; i++)
            {
                south.Deck.Add(Card("S-DECK-" + i, "ST03-002", "south", "deck"));
                north.Deck.Add(Card("N-DECK-" + i, "ST01-002", "north", "deck"));
            }
            return new GameState
            {
                Status = "active", ActiveSeat = "south", Phase = "main", TurnNumber = 5,
                Players = new Dictionary<string, PlayerState> { ["south"] = south, ["north"] = north },
            };
        }

        private static CardInstance Card(string instance, string id, string owner, string zone) => new CardInstance
        {
            InstanceId = instance, CardId = id, Owner = owner, Zone = zone,
        };

        private static void Check(string name, (bool ok, string detail) result, ref int ok, ref int fail)
        {
            if (result.ok) { ok++; Console.WriteLine($"  [ok] {name}: {result.detail}"); }
            else { fail++; Console.WriteLine($"  [FAIL] {name}: {result.detail}"); }
        }
    }
}
