using System;
using System.Collections.Generic;
using System.Linq;
using OnePieceTcg.Engine;

// Constructed-scenario tests: build a specific board state, drive real GameEngine commands,
// and assert the outcome. Used to reproduce + verify fixes for the reported card bugs.
static class Scenarios
{
    static int pass = 0, fail = 0;

    public static int Run()
    {
        UtaKoByCharacterVsLeader();
        GumGumBellTrigger();
        ImInvincibleNotACounter();
        BlackLuffyLeaderKoEffect();
        SixthCharacterReplace();
        DoubleAttackDamage();
        BlazeSliceCounter();
        ShanksDefensiveLeaderEffect();
        DoubleAttackTriggerOrder();
        AttackTargetRemovedNoHang();

        Console.WriteLine($"\nScenarios: {pass} passed, {fail} failed.");
        return fail > 0 ? 1 : 0;
    }

    // Bug: ST08-014 Gum-Gum Bell [Trigger] "Add up to 1 black Character card with a cost of 2 or
    // less from your trash" let an EVENT be chosen — type/colour filter wasn't enforced.
    static void GumGumBellTrigger()
    {
        const string text = "Add up to 1 black Character card with a cost of 2 or less from your trash to your hand.";
        string blackEvent = "EB03-049";     // black event, cost 1  → must be REJECTED (wrong type)
        string blackChar = "EB01-044";      // black character (Funkfreed), cost 1 → allowed

        // Try to add the EVENT → rejected: stays in trash, not in hand.
        {
            var st = TrashPickState(text, blackEvent, blackChar);
            Apply(st, new GameCommand { Type = "resolveEffect", Seat = "south", EffectId = "ggbell", Target = InstId(st, "south", blackEvent) });
            bool eventInHand = st.Players["south"].Hand.Any(c => c.CardId == blackEvent);
            Check("Gum-Gum Bell trigger REJECTS a black Event from trash", !eventInHand, $"eventInHand={eventInHand}  {Tail(st)}");
        }
        // Add the black CHARACTER → allowed: moves to hand.
        {
            var st = TrashPickState(text, blackEvent, blackChar);
            Apply(st, new GameCommand { Type = "resolveEffect", Seat = "south", EffectId = "ggbell", Target = InstId(st, "south", blackChar) });
            bool charInHand = st.Players["south"].Hand.Any(c => c.CardId == blackChar);
            Check("Gum-Gum Bell trigger ADDS a black Character from trash", charInHand, $"charInHand={charInHand}  {Tail(st)}");
        }
    }

    // Bug: ST11-005 "I'm Invincible!" (event, Counter=0, no [Counter] ability) was usable as a
    // hand counter for +1000 (its [Trigger] value) lasting the whole turn. It should NOT be a
    // valid counter at all.
    static void ImInvincibleNotACounter()
    {
        var st = GameEngine.CreateMatch(new MatchConfig { SouthDeck = "st01", NorthDeck = "st11", Seed = "counter" });
        st.Status = "active"; st.Phase = "main"; st.ActiveSeat = "south"; st.TurnNumber = 3;
        var S = st.Players["south"]; var N = st.Players["north"];
        S.TurnsStarted = 2; N.TurnsStarted = 2;
        for (int i = 0; i < 5; i++) { S.CharacterArea[i] = null; N.CharacterArea[i] = null; }
        var atk = MakeInPlay("ST01-005", "south"); atk.Rested = false; atk.PlayedOnTurn = 0;
        S.CharacterArea[0] = atk;
        // north holds I'm Invincible; give north active DON in case an event cost is checked.
        var inv = MakeInPlay("ST11-005", "north"); inv.Zone = "hand"; N.Hand.Add(inv);
        // attack north's LEADER, advance to the counter step.
        Apply(st, new GameCommand { Type = "declareAttack", Seat = "south", Attacker = atk.InstanceId, Target = N.Leader.InstanceId });
        Apply(st, new GameCommand { Type = "passBlock", Seat = "north" });
        int before = st.Battle?.CounterPower ?? -1;
        Apply(st, new GameCommand { Type = "counterWithCard", Seat = "north", InstanceId = inv.InstanceId });
        int after = st.Battle?.CounterPower ?? -1;
        bool stillInHand = N.Hand.Any(c => c.CardId == "ST11-005");
        Check("I'm Invincible! (ST11-005) is NOT usable as a counter", after == before && stillInHand,
              $"counterBefore={before} counterAfter={after} stillInHand={stillInHand}  {Tail(st)}");
    }

    // Bug: "Black" Luffy leader (ST08-001) "[Your Turn] When a Character is K.O.'d, give up to 1
    // rested DON!! card to this Leader" never fired — no board-watcher dispatch on Character K.O.
    static void BlackLuffyLeaderKoEffect()
    {
        var st = GameEngine.CreateMatch(new MatchConfig { SouthDeck = "st08", NorthDeck = "st01", Seed = "koeffect" });
        st.Status = "active"; st.Phase = "main"; st.ActiveSeat = "south"; st.TurnNumber = 3;
        var S = st.Players["south"]; var N = st.Players["north"];
        S.TurnsStarted = 2; N.TurnsStarted = 2;
        for (int i = 0; i < 5; i++) { S.CharacterArea[i] = null; N.CharacterArea[i] = null; }
        var atk = MakeInPlay("ST08-005", "south"); atk.Rested = false; atk.PlayedOnTurn = 0; S.CharacterArea[0] = atk; // Shanks 10000
        var vic = MakeInPlay("ST01-006", "north"); vic.Rested = true; N.CharacterArea[0] = vic;                        // Chopper 1000
        S.CostArea.Add(new DonInstance { InstanceId = "south-don-rested", Rested = true });
        int before = S.Leader.AttachedDonIds.Count;

        Apply(st, new GameCommand { Type = "declareAttack", Seat = "south", Attacker = atk.InstanceId, Target = vic.InstanceId });
        Apply(st, new GameCommand { Type = "passBlock", Seat = "north" });
        Apply(st, new GameCommand { Type = "passCounter", Seat = "north" });
        Apply(st, new GameCommand { Type = "resolveAttack", Seat = "north" });

        var pend = st.PendingEffects.FirstOrDefault(e => e.Seat == "south");
        bool fired = pend != null;
        if (pend != null)
            Apply(st, new GameCommand { Type = "resolveEffect", Seat = "south", EffectId = pend.EffectId, Target = S.Leader.InstanceId });
        int after = S.Leader.AttachedDonIds.Count;
        Check("Black Luffy leader ST08-001 K.O. effect fires (rested DON!! to leader)",
              fired && after == before + 1, $"fired={fired} donBefore={before} donAfter={after}  {Tail(st)}");
    }

    // Bug: with a full board (5 chars) you should be able to play a Character "over" one, trashing
    // it. Engine supports it via SlotIndex; verify the replace works and CanPlayFromHand allows it.
    static void SixthCharacterReplace()
    {
        var st = GameEngine.CreateMatch(new MatchConfig { SouthDeck = "st01", NorthDeck = "st02", Seed = "sixth" });
        st.Status = "active"; st.Phase = "main"; st.ActiveSeat = "south"; st.TurnNumber = 3;
        var S = st.Players["south"];
        S.TurnsStarted = 2;
        for (int i = 0; i < 5; i++) { var c = MakeInPlay("ST01-006", "south"); c.Rested = false; S.CharacterArea[i] = c; } // full board
        string victimId = S.CharacterArea[2].InstanceId;
        var hc = MakeInPlay("ST01-005", "south"); hc.Zone = "hand"; S.Hand.Add(hc);   // Jinbe in hand
        for (int i = 0; i < 8; i++) S.CostArea.Add(new DonInstance { InstanceId = $"sd{i}", Rested = false }); // plenty DON

        bool canPlay = GameEngine.CanPlayFromHand(st, "south", hc);
        Apply(st, new GameCommand { Type = "playCard", Seat = "south", InstanceId = hc.InstanceId, SlotIndex = 2 });
        bool victimTrashed = S.Trash.Any(c => c.InstanceId == victimId);
        bool newInSlot = S.CharacterArea[2]?.InstanceId == hc.InstanceId;
        Check("Full board: play 6th Character over slot 2 trashes the occupant",
              canPlay && victimTrashed && newInSlot,
              $"canPlay={canPlay} victimTrashed={victimTrashed} newInSlot={newInSlot}  {Tail(st)}");
    }

    // Bug: [Double Attack] (Yamato OP06-022 "This card deals 2 damage") was modeled as a 2nd
    // attack, so it wouldn't kill at 1 Life. It should deal 2 Life damage in one hit.
    static void DoubleAttackDamage()
    {
        // (a) 3 Life → double attack takes 2 → 1 Life left, game continues.
        {
            var st = DoubleAtkState(3);
            DriveLeaderAttack(st);
            int life = st.Players["north"].Life.Count;
            Check("Double Attack deals 2 damage (3 Life -> 1)", st.Status != "finished" && life == 1,
                  $"status={st.Status} northLife={life}  {Tail(st)}");
        }
        // (b) 1 Life → double attack finishes the game (the reported bug).
        {
            var st = DoubleAtkState(1);
            DriveLeaderAttack(st);
            Check("Double Attack at 1 Life finishes the game", st.Status == "finished",
                  $"status={st.Status} northLife={st.Players["north"].Life.Count}  {Tail(st)}");
        }
        // (c) sanity: a NORMAL attack at 1 Life takes the last card but does NOT kill.
        {
            var st = DoubleAtkState(1);
            st.Players["south"].Leader.CardId = "ST01-001"; // plain Luffy, no Double Attack
            DriveLeaderAttack(st);
            Check("Normal attack at 1 Life does NOT finish the game", st.Status != "finished",
                  $"status={st.Status} northLife={st.Players["north"].Life.Count}  {Tail(st)}");
        }
    }

    // Bug: OP07-116 Blaze Slice ("[Main]/[Counter] …gains +1000…") couldn't be used as a counter
    // even with 1 DON — AutomatedCounterPower gated on the (empty) keywords array, not the text.
    static void BlazeSliceCounter()
    {
        var st = GameEngine.CreateMatch(new MatchConfig { SouthDeck = "st01", NorthDeck = "st02", Seed = "blaze" });
        st.Status = "active"; st.Phase = "main"; st.ActiveSeat = "south"; st.TurnNumber = 3;
        var S = st.Players["south"]; var N = st.Players["north"];
        S.TurnsStarted = 2; N.TurnsStarted = 2;
        for (int i = 0; i < 5; i++) { S.CharacterArea[i] = null; N.CharacterArea[i] = null; }
        var atk = MakeInPlay("ST01-005", "south"); atk.Rested = false; atk.PlayedOnTurn = 0; S.CharacterArea[0] = atk;
        var blaze = MakeInPlay("OP07-116", "north"); blaze.Zone = "hand"; N.Hand.Add(blaze);
        N.CostArea.Add(new DonInstance { InstanceId = "n-don-active", Rested = false }); // 1 active DON
        Apply(st, new GameCommand { Type = "declareAttack", Seat = "south", Attacker = atk.InstanceId, Target = N.Leader.InstanceId });
        Apply(st, new GameCommand { Type = "passBlock", Seat = "north" });
        int before = st.Battle?.CounterPower ?? -1;
        Apply(st, new GameCommand { Type = "counterWithCard", Seat = "north", InstanceId = blaze.InstanceId });
        int after = st.Battle?.CounterPower ?? -1;
        bool leftHand = !N.Hand.Any(c => c.CardId == "OP07-116");
        Check("Blaze Slice (OP07-116) is usable as a counter (+1000)", after == before + 1000 && leftHand,
              $"counterBefore={before} counterAfter={after} leftHand={leftHand}  {Tail(st)}");
    }

    // Bug: OP09-001 Shanks leader "[Once Per Turn] This effect can be activated when your opponent
    // attacks. Give up to 1 of your opponent's Leader or Character cards -1000 power…" was never
    // offered — the engine only recognized the [On Your Opponent's Attack] tag, not this wording.
    static void ShanksDefensiveLeaderEffect()
    {
        var st = GameEngine.CreateMatch(new MatchConfig { SouthDeck = "st01", NorthDeck = "st01", Seed = "shanksdef" });
        st.Status = "active"; st.Phase = "main"; st.ActiveSeat = "north"; st.TurnNumber = 4;
        var S = st.Players["south"]; var N = st.Players["north"];
        S.TurnsStarted = 2; N.TurnsStarted = 2;
        for (int i = 0; i < 5; i++) { S.CharacterArea[i] = null; N.CharacterArea[i] = null; }
        S.Leader.CardId = "OP09-001";   // Shanks defensive leader
        var atk = MakeInPlay("ST01-005", "north"); atk.Rested = false; atk.PlayedOnTurn = 0; N.CharacterArea[0] = atk;
        Apply(st, new GameCommand { Type = "declareAttack", Seat = "north", Attacker = atk.InstanceId, Target = S.Leader.InstanceId });
        bool offered = st.PendingEffects.Any(e => e.Seat == "south" && e.Timing == "onOpponentsAttack");
        Check("Shanks OP09-001 defensive effect is offered when attacked", offered,
              $"pendingForSouth={st.PendingEffects.Count(e => e.Seat == "south")}  {Tail(st)}");
    }

    // Bug (all 11 harness deadlocks): when an attack's target leaves play mid-battle (bounced by a
    // [When Attacking] effect etc.), the battle fizzled but the phase stayed "battle" — the turn
    // player was stranded with no battle → hang. It must return to the main phase.
    static void AttackTargetRemovedNoHang()
    {
        var st = FreshBattleBoard("ST01-005", null, "ST08-002"); // south Jinbe attacks north's Uta
        var N = st.Players["north"];
        Apply(st, new GameCommand { Type = "declareAttack", Seat = "south", Attacker = st.Players["south"].CharacterArea[0].InstanceId, Target = N.CharacterArea[0].InstanceId });
        Apply(st, new GameCommand { Type = "passBlock", Seat = "north" });
        Apply(st, new GameCommand { Type = "passCounter", Seat = "north" });
        // Simulate the target being bounced out of play during the battle.
        var target = N.CharacterArea[0];
        N.CharacterArea[0] = null; target.Zone = "hand"; N.Hand.Add(target);
        Apply(st, new GameCommand { Type = "resolveAttack", Seat = "north" });
        Check("Attack whose target left play fizzles back to main phase (no hang)",
              st.Battle == null && st.Phase == "main" && st.Status == "active",
              $"battle={(st.Battle == null ? "null" : "set")} phase={st.Phase} status={st.Status}");
    }

    // Rulebook: on 2 damage from [Double Attack], the FIRST life card's [Trigger] resolves BEFORE
    // the second damage is dealt (damage repeats once per point). Verify the ordering.
    static void DoubleAttackTriggerOrder()
    {
        var st = DoubleAtkState(0);   // OP06-022 Yamato leader for south; empty north life
        var N = st.Players["north"];
        N.Life.Clear();
        N.Life.Add(new CardInstance { InstanceId = "nlife-bottom", CardId = "ST01-006", Owner = "north", Zone = "life" }); // no trigger
        N.Life.Add(new CardInstance { InstanceId = "nlife-top", CardId = "ST01-014", Owner = "north", Zone = "life" });    // TOP (Pop takes last), has a [Trigger]
        var S = st.Players["south"];
        Apply(st, new GameCommand { Type = "declareAttack", Seat = "south", Attacker = S.Leader.InstanceId, Target = N.Leader.InstanceId });
        Apply(st, new GameCommand { Type = "passBlock", Seat = "north" });
        Apply(st, new GameCommand { Type = "passCounter", Seat = "north" });
        Apply(st, new GameCommand { Type = "resolveAttack", Seat = "north" });
        // After the FIRST damage: in the trigger step for the top card, second card NOT yet taken,
        // and 1 more damage pending.
        bool midOk = st.Battle != null && st.Battle.Step == "trigger" && N.Life.Count == 1 && st.Battle.PendingLifeDamage == 1;
        Apply(st, new GameCommand { Type = "passTrigger", Seat = "north" });   // resolve first trigger → second damage lands
        bool finalOk = N.Life.Count == 0;
        Check("Double Attack: first Life trigger resolves BEFORE the second damage", midOk && finalOk,
              $"midOk={midOk} (step={st.Battle?.Step} lifeAfter1st={(midOk ? 1 : -1)}) finalLife={N.Life.Count}");
    }

    static GameState DoubleAtkState(int northLife)
    {
        var st = GameEngine.CreateMatch(new MatchConfig { SouthDeck = "st01", NorthDeck = "st01", Seed = "dbl" });
        st.Status = "active"; st.Phase = "main"; st.ActiveSeat = "south"; st.TurnNumber = 3;
        var S = st.Players["south"]; var N = st.Players["north"];
        S.TurnsStarted = 2; N.TurnsStarted = 2;
        for (int i = 0; i < 5; i++) { S.CharacterArea[i] = null; N.CharacterArea[i] = null; }
        S.Leader.CardId = "OP06-022";  // Yamato, [Double Attack]
        S.Leader.Rested = false; S.Leader.PlayedOnTurn = 0;
        N.Life.Clear();
        for (int i = 0; i < northLife; i++)
            N.Life.Add(new CardInstance { InstanceId = $"nlife{i}", CardId = "ST01-006", Owner = "north", Zone = "life" });
        return st;
    }

    static void DriveLeaderAttack(GameState st)
    {
        var S = st.Players["south"]; var N = st.Players["north"];
        Apply(st, new GameCommand { Type = "declareAttack", Seat = "south", Attacker = S.Leader.InstanceId, Target = N.Leader.InstanceId });
        Apply(st, new GameCommand { Type = "passBlock", Seat = "north" });
        Apply(st, new GameCommand { Type = "passCounter", Seat = "north" });
        Apply(st, new GameCommand { Type = "resolveAttack", Seat = "north" });
        for (int i = 0; i < 6 && st.Battle != null && st.Battle.Step == "trigger"; i++)
            Apply(st, new GameCommand { Type = "passTrigger", Seat = "north" });
    }

    static GameState TrashPickState(string effectText, params string[] trashCardIds)
    {
        var st = GameEngine.CreateMatch(new MatchConfig { SouthDeck = "st08", NorthDeck = "st01", Seed = "trashpick" });
        st.Status = "active"; st.Phase = "main"; st.ActiveSeat = "south"; st.TurnNumber = 3;
        var S = st.Players["south"];
        foreach (var cid in trashCardIds)
        {
            var c = MakeInPlay(cid, "south"); c.Zone = "trash";
            S.Trash.Add(c);
        }
        st.PendingEffects.Add(new PendingEffect
        {
            EffectId = "ggbell", Seat = "south", Text = effectText, Timing = "trigger",
            Optional = true, TargetZone = EffectTargetZone.Trash, SelectionsRemaining = 1,
        });
        return st;
    }

    static string InstId(GameState st, string seat, string cardId) =>
        st.Players[seat].Trash.First(c => c.CardId == cardId).InstanceId;

    // Bug: ST08-002 Uta "cannot be K.O.'d in battle by Leaders" was blocking Character K.O.s too.
    static void UtaKoByCharacterVsLeader()
    {
        // Character attacker (7000) K.O.s Uta (4000) → Uta should go to trash.
        {
            var st = FreshBattleBoard(attackerCardId: "ST01-005" /*Jinbe 5000*/, attackerPower: null,
                                      defenderCardId: "ST08-002" /*Uta*/);
            DriveAttackToResolve(st);
            bool utaTrashed = st.Players["north"].Trash.Any(c => c.CardId == "ST08-002");
            bool utaStillBoard = st.Players["north"].CharacterArea.Any(c => c?.CardId == "ST08-002");
            Check("Uta ST08-002 K.O.'d by a CHARACTER attack", utaTrashed && !utaStillBoard,
                  $"trashed={utaTrashed} stillOnBoard={utaStillBoard} battle={(st.Battle==null?"null":st.Battle.Step)} phase={st.Phase}  {Tail(st)}");
        }
        // Leader attacker vs Uta → Uta survives (immune to Leaders).
        {
            var st = FreshBattleBoard(attackerCardId: null, attackerPower: null, defenderCardId: "ST08-002", attackerIsLeader: true);
            DriveAttackToResolve(st);
            bool utaStillBoard = st.Players["north"].CharacterArea.Any(c => c?.CardId == "ST08-002");
            Check("Uta ST08-002 survives a LEADER attack (immune by Leaders)", utaStillBoard,
                  $"stillOnBoard={utaStillBoard}  {Tail(st)}");
        }
    }

    // ---- scenario construction helpers ----

    // South attacks north. South gets one attacker (a Character, or its Leader if attackerIsLeader),
    // north gets one defender Character. All other board cleared. It's south's turn, main phase,
    // past turn 1 so attacks are legal.
    static GameState FreshBattleBoard(string attackerCardId, int? attackerPower, string defenderCardId, bool attackerIsLeader = false)
    {
        var st = GameEngine.CreateMatch(new MatchConfig { SouthDeck = "st01", NorthDeck = "st08", Seed = "scenario" });
        st.Status = "active";
        st.Phase = "main";
        st.ActiveSeat = "south";
        st.TurnNumber = 3;
        st.Battle = null;
        var S = st.Players["south"];
        var N = st.Players["north"];
        S.TurnsStarted = 2; N.TurnsStarted = 2;
        // clear boards
        for (int i = 0; i < 5; i++) { S.CharacterArea[i] = null; N.CharacterArea[i] = null; }

        // Defender character on north.
        var def = MakeInPlay(defenderCardId, "north");
        def.Rested = true; // resting so it's an eligible attack target and can't counter-attack
        N.CharacterArea[0] = def;

        // Attacker.
        if (attackerIsLeader)
        {
            S.Leader.Rested = false;
            S.Leader.PlayedOnTurn = 0;
        }
        else
        {
            var atk = MakeInPlay(attackerCardId, "south");
            atk.Rested = false;
            atk.PlayedOnTurn = 0; // not summoning sick
            S.CharacterArea[0] = atk;
        }
        return st;
    }

    static CardInstance MakeInPlay(string cardId, string owner) => new CardInstance
    {
        InstanceId = $"{owner}-{cardId}-{Guid.NewGuid():N}".Substring(0, 24),
        CardId = cardId,
        Owner = owner,
        Zone = "character",
        Rested = false,
    };

    static void DriveAttackToResolve(GameState st)
    {
        var S = st.Players["south"];
        string attackerId = S.CharacterArea[0]?.InstanceId ?? S.Leader.InstanceId;
        string targetId = st.Players["north"].CharacterArea[0].InstanceId;
        Apply(st, new GameCommand { Type = "declareAttack", Seat = "south", Attacker = attackerId, Target = targetId });
        // Defender resolves block/counter steps by passing.
        Apply(st, new GameCommand { Type = "passBlock", Seat = "north" });
        Apply(st, new GameCommand { Type = "passCounter", Seat = "north" });
        // The DEFENDER owns the final resolve (see GameEngine.ResolveAttack seat guard).
        Apply(st, new GameCommand { Type = "resolveAttack", Seat = "north" });
    }

    static void Apply(GameState st, GameCommand cmd)
    {
        try { GameEngine.ApplyCommand(st, cmd); } catch (Exception ex) { Console.WriteLine($"   apply {cmd.Type} threw {ex.GetType().Name}: {ex.Message}"); }
    }

    static string Tail(GameState st)
    {
        if (st.EventLog == null || st.EventLog.Count == 0) return "";
        return "log: " + string.Join(" · ", st.EventLog.Skip(Math.Max(0, st.EventLog.Count - 5)).Select(e => e.Message));
    }

    static void Check(string name, bool ok, string detail)
    {
        if (ok) { pass++; Console.WriteLine($"  PASS  {name}"); }
        else { fail++; Console.WriteLine($"  FAIL  {name}  [{detail}]"); }
    }
}
