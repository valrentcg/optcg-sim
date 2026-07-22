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
        AttributeKoImmunity();
        ConditionalKoImmunity();
        GumGumBellTrigger();
        ImInvincibleNotACounter();
        BlackLuffyLeaderKoEffect();
        SixthCharacterReplace();
        DoubleAttackDamage();
        BlazeSliceCounter();
        ShanksDefensiveLeaderEffect();
        DoubleAttackTriggerOrder();
        AttackTargetRemovedNoHang();
        LawSecondClausePlaysFromHand();
        HeatCountsDonMinusAsOneEvent();
        HeatDonReturnedOncePerTurn();
        RayleighThenKoFires();
        ByrnndiSixDigitPowerGain();
        CabajiSelfBuffScope();
        Op12036ZoroSlashLeaderBuff();
        Eb02019ConditionalRushVsCharacters();
        BotDeterminizerFairView();
        PbLuffyDonReturnThreshold();
        BuggyCannotAttackAura();
        CrocodileMihawkDrawTrashThenPlay();
        SaboImmunityThenDrawTrash();
        PlayedOverIsNotAKo();
        DonAddActiveAndRested();
        PassiveKeywordGrants();
        DonX2ThresholdGating();
        PowerCostDurationScoping();
        PeronaSlashAttributeCondition();
        IpponmatsuSlashAttributeFilter();
        KingKoReplacementReturnDon();
        RemovalImmunityBlocksBounce();
        SaboRevealPlayGatesBuff();
        RestByYourEffectFlagSetAndReset();

        Console.WriteLine($"\nScenarios: {pass} passed, {fail} failed.");
        return fail > 0 ? 1 : 0;
    }

    // Bug (full-library sweep): "cannot be K.O.'d in battle by ＜X＞ attribute cards/Characters" was
    // over-immune (immune to everything) because only "by Leaders" was handled. OP03-008 Buggy
    // (Slash) is immune only to SLASH attackers.
    static void AttributeKoImmunity()
    {
        {   // same-attribute (Slash) attacker → Buggy survives
            var st = FreshBattleBoard("EB01-012" /*Cavendish, Slash 6000*/, null, "OP03-008" /*Buggy, Slash*/);
            DriveAttackToResolve(st);
            bool survived = st.Players["north"].CharacterArea.Any(c => c?.CardId == "OP03-008");
            Check("Buggy OP03-008 survives a SAME-attribute (Slash) attacker", survived, $"survived={survived}  {Tail(st)}");
        }
        {   // different-attribute (Strike) attacker → Buggy K.O.'d
            var st = FreshBattleBoard("EB01-008" /*LittleOars Jr., Strike 7000*/, null, "OP03-008");
            DriveAttackToResolve(st);
            bool trashed = st.Players["north"].Trash.Any(c => c.CardId == "OP03-008");
            Check("Buggy OP03-008 is K.O.'d by a DIFFERENT-attribute (Strike) attacker", trashed, $"trashed={trashed}  {Tail(st)}");
        }
    }

    // Condition-gated immunity now evaluates its condition (was over-immune). OP02-100 Jango:
    // "If you have [Fullbody], this Character cannot be K.O.'d in battle." Fullbody = OP02-111.
    static void ConditionalKoImmunity()
    {
        {   // with [Fullbody] on board → immune
            var st = FreshBattleBoard("ST01-005" /*Jinbe 5000*/, null, "OP02-100" /*Jango 3000*/);
            st.Players["north"].CharacterArea[1] = MakeInPlay("OP02-111", "north"); // Fullbody
            DriveAttackToResolve(st);
            bool survived = st.Players["north"].CharacterArea.Any(c => c?.CardId == "OP02-100");
            Check("Jango OP02-100 survives while [Fullbody] is on board", survived, $"survived={survived}  {Tail(st)}");
        }
        {   // no [Fullbody] → K.O.'d (condition not met)
            var st = FreshBattleBoard("ST01-005", null, "OP02-100");
            DriveAttackToResolve(st);
            bool trashed = st.Players["north"].Trash.Any(c => c.CardId == "OP02-100");
            Check("Jango OP02-100 is K.O.'d with no [Fullbody]", trashed, $"trashed={trashed}  {Tail(st)}");
        }
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

    // Coverage lead (compound-suspect linter): OP08-118 Silvers Rayleigh —
    // "[On Play] Select up to 2 of your opponent's Characters, and give 1 −3000 and the other
    // −2000 …. Then, K.O. up to 1 of your opponent's Characters with 3000 power or less." Verify
    // the trailing "Then, K.O." clause actually fires after the power-reduction clause.
    static void RayleighThenKoFires()
    {
        var st = GameEngine.CreateMatch(new MatchConfig { SouthDeck = "st08", NorthDeck = "st01", Seed = "rayleigh" });
        st.Status = "active"; st.Phase = "main"; st.ActiveSeat = "south"; st.TurnNumber = 5;
        var S = st.Players["south"]; var N = st.Players["north"];
        S.TurnsStarted = 3; N.TurnsStarted = 3;
        for (int i = 0; i < 5; i++) { S.CharacterArea[i] = null; N.CharacterArea[i] = null; }
        var n1 = MakeInPlay("ST01-006", "north"); N.CharacterArea[0] = n1;   // 1000 power
        var n2 = MakeInPlay("ST01-006", "north"); N.CharacterArea[1] = n2;   // 1000 power
        var hc = MakeInPlay("OP08-118", "south"); hc.Zone = "hand"; S.Hand.Add(hc);
        for (int i = 0; i < 10; i++) S.CostArea.Add(new DonInstance { InstanceId = $"rd{i}", Rested = false });

        Apply(st, new GameCommand { Type = "playCard", Seat = "south", InstanceId = hc.InstanceId, SlotIndex = 0 });
        // Resolve the whole On Play: pick the two power-reduction targets, then the K.O. target.
        Drive(st, "south", n1.InstanceId, n2.InstanceId, n1.InstanceId, n2.InstanceId);
        int koed = N.Trash.Count(c => c.CardId == "ST01-006");
        Check("OP08-118 Rayleigh: trailing 'Then, K.O.' fires (a Character is K.O.'d)",
              koed >= 1, $"northTrash(ST01-006)={koed}  {Tail(st)}");
    }

    // Playtest bug: OP02-082 Byrnndi World "[Activate: Main] DON!! −8: This Character gains
    // +792000 power" (real card text) resolved as manual — the self-buff gate capped at \d{3,5}.
    static void ByrnndiSixDigitPowerGain()
    {
        var st = GameEngine.CreateMatch(new MatchConfig { SouthDeck = "st01", NorthDeck = "st01", Seed = "byrnndi" });
        st.Status = "active"; st.Phase = "main"; st.ActiveSeat = "south"; st.TurnNumber = 6;
        var S = st.Players["south"]; S.TurnsStarted = 4;
        for (int i = 0; i < 5; i++) S.CharacterArea[i] = null;
        var by = MakeInPlay("OP02-082", "south"); by.PlayedOnTurn = 0; S.CharacterArea[0] = by;
        S.CostArea.Clear();
        for (int i = 0; i < 8; i++) S.CostArea.Add(new DonInstance { InstanceId = $"byd{i}", Rested = false });
        int basePow = GameEngine.GetCard(by).Power;

        Apply(st, new GameCommand { Type = "activateMain", Seat = "south", Target = by.InstanceId });
        Drive(st, "south", by.InstanceId);
        int pow = GameEngine.GetPower(st, by);
        Check("OP02-082 Byrnndi: +792000 power self-buff applies (6-digit gain)",
              pow == basePow + 792000, $"power={pow} base={basePow} expected={basePow + 792000}  {Tail(st)}");
    }

    // Playtest bug: ST25-002 Cabaji "[Opponent's Turn] This Character gains +5000 power" was
    // leaking onto EVERY Character instead of only Cabaji.
    static void CabajiSelfBuffScope()
    {
        var st = GameEngine.CreateMatch(new MatchConfig { SouthDeck = "st01", NorthDeck = "st01", Seed = "cabaji" });
        st.Status = "active"; st.Phase = "main"; st.ActiveSeat = "north"; st.TurnNumber = 6; // opponent's turn (south is defender)
        var S = st.Players["south"]; S.TurnsStarted = 3;
        for (int i = 0; i < 5; i++) S.CharacterArea[i] = null;
        var cab = MakeInPlay("ST25-002", "south"); S.CharacterArea[0] = cab;
        var other = MakeInPlay("ST01-006", "south"); S.CharacterArea[1] = other;   // 1000 power bystander
        int cabBase = GameEngine.GetCard(cab).Power;
        int otherBase = GameEngine.GetCard(other).Power;

        int cabPow = GameEngine.GetPower(st, cab);
        int otherPow = GameEngine.GetPower(st, other);
        Check("ST25-002 Cabaji: +5000 self-buff hits ONLY Cabaji, not the whole board",
              cabPow == cabBase + 5000 && otherPow == otherBase,
              $"cabaji={cabPow}(base {cabBase}) other={otherPow}(base {otherBase})  {Tail(st)}");
    }

    // Playtest bug: OP12-036 Zoro "If your Leader has the ＜Slash＞ attribute, this Character cannot be
    // K.O.'d in battle by ＜Slash＞ attribute cards and gains +1000 power." The +1000 never applied because
    // the static-self-buff scan bounded the sentence with [^.], which the literal periods in "K.O.'d"
    // defeat — the "gains +1000 power" clause sits two periods past "this Character". Verify the buff
    // applies ONLY when the Leader has the ＜Slash＞ attribute.
    static void Op12036ZoroSlashLeaderBuff()
    {
        int Power(string leaderId)
        {
            var st = GameEngine.CreateMatch(new MatchConfig { SouthDeck = "st01", NorthDeck = "st01", Seed = "zoro:" + leaderId });
            st.Status = "active"; st.Phase = "main"; st.ActiveSeat = "south"; st.TurnNumber = 6;
            var S = st.Players["south"]; S.TurnsStarted = 3;
            S.Leader.CardId = leaderId;
            for (int i = 0; i < 5; i++) S.CharacterArea[i] = null;
            var zoro = MakeInPlay("OP12-036", "south"); S.CharacterArea[0] = zoro;
            return GameEngine.GetPower(st, zoro);
        }
        int baseP = GameEngine.GetCard(MakeInPlay("OP12-036", "south")).Power; // 5000
        int withSlash = Power("EB01-001");   // Kouzuki Oden — ＜Slash＞ attribute leader
        int withStrike = Power("EB02-010");  // Luffy — ＜Strike＞ attribute leader (condition NOT met)
        Check("OP12-036 Zoro: +1000 power only when Leader has the ＜Slash＞ attribute",
              withSlash == baseP + 1000 && withStrike == baseP,
              $"base={baseP} slashLeader={withSlash} strikeLeader={withStrike}");
    }

    // Playtest bug: EB02-019 Zoro "If your opponent has 2 or more Characters, this Character can attack
    // Characters on the turn in which it is played." The engine ALLOWED the attack (good) but ignored the
    // leading condition, and the board dimmed the card as summoning-sick even when it could attack — the
    // shared CanAttackCharactersOnPlayTurn now drives BOTH, so the condition is honoured and the glow/dim
    // matches. Verify the gate is true iff the opponent has 2+ Characters, and false when negated.
    static void Eb02019ConditionalRushVsCharacters()
    {
        GameState Build(int oppChars)
        {
            var st = GameEngine.CreateMatch(new MatchConfig { SouthDeck = "st01", NorthDeck = "st01", Seed = "eb02019:" + oppChars });
            st.Status = "active"; st.Phase = "main"; st.ActiveSeat = "south"; st.TurnNumber = 6;
            var S = st.Players["south"]; var N = st.Players["north"]; S.TurnsStarted = 3;
            for (int i = 0; i < 5; i++) { S.CharacterArea[i] = null; N.CharacterArea[i] = null; }
            var zoro = MakeInPlay("EB02-019", "south"); zoro.PlayedOnTurn = st.TurnNumber; S.CharacterArea[0] = zoro;
            for (int i = 0; i < oppChars; i++) { var c = MakeInPlay("ST01-006", "north"); c.Rested = true; N.CharacterArea[i] = c; }
            return st;
        }
        var two = Build(2); var one = Build(1);
        bool canTwo = GameEngine.CanAttackCharactersOnPlayTurn(two, "south", two.Players["south"].CharacterArea[0]);
        bool canOne = GameEngine.CanAttackCharactersOnPlayTurn(one, "south", one.Players["south"].CharacterArea[0]);
        Check("EB02-019 Zoro: 'can attack Characters when played' true iff opponent has 2+ Characters",
              canTwo && !canOne, $"opp2={canTwo} opp1={canOne}");
    }

    // Fair-information restructure: the Advanced bot must decide on a BotDeterminizer.FairView, where the
    // zones hidden from the acting seat are resampled. Assert the three correctness properties:
    //   (1) NONINTERFERENCE — same public state + seed ⇒ identical sampled arrangement regardless of the TRUE
    //       hidden order (this is what proves the bot can't read the truth through the sample; it FAILS if the
    //       multiset isn't canonicalized before shuffling);
    //   (2) LEGALITY — the opponent's total hidden multiset (hand+deck+face-down Life) is preserved;
    //   (3) PRESERVATION — the acting seat's OWN hand keeps its exact ids AND CardIds (it must stay actionable).
    static void BotDeterminizerFairView()
    {
        CardInstance Mk(string id, string owner) => new CardInstance
        { InstanceId = $"{owner}:{id}:{Guid.NewGuid():N}".Substring(0, 22), CardId = id, Owner = owner, Zone = "hand" };

        GameState Build(bool reverseOppHand)
        {
            var st = GameEngine.CreateMatch(new MatchConfig { SouthDeck = "st01", NorthDeck = "st01", Seed = "det" });
            st.Status = "active"; st.Phase = "main"; st.ActiveSeat = "south"; st.TurnNumber = 6;
            var S = st.Players["south"]; var N = st.Players["north"];
            S.Hand.Clear();
            foreach (var id in new[] { "ST01-006", "ST01-007" }) S.Hand.Add(Mk(id, "south"));
            var oppIds = new List<string> { "OP12-036", "EB02-019", "OP09-083", "ST01-005" };
            if (reverseOppHand) oppIds.Reverse();
            N.Hand.Clear();
            foreach (var id in oppIds) N.Hand.Add(Mk(id, "north"));
            return st;
        }
        var a = Build(false); var b = Build(true);
        const int seed = 12345;
        var fa = GameEngine_FairView(a, "south", seed);
        var fb = GameEngine_FairView(b, "south", seed);

        List<string> Hand(GameState s, string seat) => s.Players[seat].Hand.Select(c => c.CardId).ToList();
        List<string> HiddenMultiset(GameState s) => s.Players["north"].Hand.Concat(s.Players["north"].Deck)
            .Concat(s.Players["north"].Life.Where(c => !c.FaceUp)).Select(c => c.CardId).OrderBy(x => x).ToList();

        bool noninterference = Hand(fa, "north").SequenceEqual(Hand(fb, "north"));
        bool legalMultiset = HiddenMultiset(fa).SequenceEqual(HiddenMultiset(a));
        bool ownCardIds = Hand(fa, "south").SequenceEqual(Hand(a, "south"));
        bool ownInstanceIds = fa.Players["south"].Hand.Select(c => c.InstanceId)
            .SequenceEqual(a.Players["south"].Hand.Select(c => c.InstanceId));
        Check("BotDeterminizer.FairView: noninterferent + legal multiset + preserves own hand",
              noninterference && legalMultiset && ownCardIds && ownInstanceIds,
              $"noninterf={noninterference} legal={legalMultiset} ownIds={ownCardIds} ownInst={ownInstanceIds} " +
              $"faNorthHand=[{string.Join(",", Hand(fa, "north"))}]");
    }

    static GameState GameEngine_FairView(GameState s, string seat, int seed) =>
        OnePieceTcg.Engine.Bot.Search.BotDeterminizer.FairView(s, seat, seed);

    // Playtest bug: OP09-061 P/B Luffy "[Your Turn][Once Per Turn] When 2 or more DON!! cards …
    // are returned …, add a DON!! active + add another rested" never fired — NotifyDonReturned
    // only matched the singular "When a DON!! card … is returned" wording and passed no count.
    static void PbLuffyDonReturnThreshold()
    {
        var st = GameEngine.CreateMatch(new MatchConfig { SouthDeck = "st01", NorthDeck = "st01", Seed = "pbluffy" });
        st.Status = "active"; st.Phase = "main"; st.ActiveSeat = "south"; st.TurnNumber = 6;
        var S = st.Players["south"]; S.TurnsStarted = 4;
        S.Leader.CardId = "OP09-061";                       // P/B Luffy leader
        for (int i = 0; i < 5; i++) S.CharacterArea[i] = null;
        var kaido = MakeInPlay("P-005", "south"); kaido.PlayedOnTurn = 0; S.CharacterArea[0] = kaido; // has [Activate: Main] DON!! −2
        S.CostArea.Clear();
        for (int i = 0; i < 4; i++) S.CostArea.Add(new DonInstance { InstanceId = $"pld{i}", Rested = false });

        Apply(st, new GameCommand { Type = "activateMain", Seat = "south", Target = kaido.InstanceId });
        Drive(st, "south", kaido.InstanceId, S.Leader.InstanceId);
        bool fired = S.AbilityUsedThisTurn.Contains(S.Leader.InstanceId + ":donReturned");
        Check("OP09-061 P/B Luffy: 'When 2 or more DON!! returned' fires on a DON!! −2 return",
              fired, $"leaderTriggerFired={fired}  {Tail(st)}");
    }

    // Playtest bug: P-084 Buggy "If your Leader is [Buggy], all Characters with a cost of 3 or 4
    // cannot attack." "all Characters" = BOTH players' Characters. The real scenario: the OPPONENT
    // (bot) has a Buggy leader + P-084 down, and MY 4-cost Usopp must not be able to attack.
    // Also verify it does NOT block when the opponent's leader isn't Buggy.
    static void BuggyCannotAttackAura()
    {
        // st = my (south) 4-cost attacker vs a north board that has P-084 Buggy + `northLeader`.
        GameState Build(string northLeaderId)
        {
            var st = GameEngine.CreateMatch(new MatchConfig { SouthDeck = "st01", NorthDeck = "st01", Seed = "buggy:" + northLeaderId });
            st.Status = "active"; st.Phase = "main"; st.ActiveSeat = "south"; st.TurnNumber = 6;
            var S = st.Players["south"]; var N = st.Players["north"];
            S.TurnsStarted = 4; N.TurnsStarted = 4;
            N.Leader.CardId = northLeaderId;
            for (int i = 0; i < 5; i++) { S.CharacterArea[i] = null; N.CharacterArea[i] = null; }
            var buggy = MakeInPlay("P-084", "north"); N.CharacterArea[0] = buggy;                       // opponent's aura source
            var atk = MakeInPlay("EB01-003", "south"); atk.PlayedOnTurn = 0; atk.Rested = false; S.CharacterArea[0] = atk; // MY cost-4 attacker
            return st;
        }

        var stBuggy = Build("OP09-042");   // opponent's leader IS Buggy → aura active for BOTH sides
        var atkB = stBuggy.Players["south"].CharacterArea[0];
        Apply(stBuggy, new GameCommand { Type = "declareAttack", Seat = "south", Attacker = atkB.InstanceId, Target = stBuggy.Players["north"].Leader.InstanceId });
        bool blocked = stBuggy.Battle == null;

        var stLuffy = Build("ST01-001");   // opponent's leader is Luffy → aura inactive
        var atkL = stLuffy.Players["south"].CharacterArea[0];
        Apply(stLuffy, new GameCommand { Type = "declareAttack", Seat = "south", Attacker = atkL.InstanceId, Target = stLuffy.Players["north"].Leader.InstanceId });
        bool allowed = stLuffy.Battle != null;

        Check("P-084 Buggy: opponent's Buggy aura blocks MY cost-4 attacker (both sides); inactive under non-Buggy leader",
              blocked && allowed, $"blockedByOppBuggy={blocked} allowedUnderLuffy={allowed}");
    }

    // Playtest bug (reported): ST25-003 Crocodile & Mihawk "[On Play] Draw 2 cards and trash 1
    // card from your hand. Then, play up to 1 {Cross Guild} type Character card with a cost of 4 or
    // less from your hand." The greedy Draw handler drew 2 and DROPPED the "trash 1" clause, so the
    // first prompt the player saw was the "play a card" step — clicking it played the card they
    // meant to trash. Verify the ORDERED button sequence: draw (silent) → a TRASH prompt (which
    // consumes a hand card to trash) → the play prompt.
    static void CrocodileMihawkDrawTrashThenPlay()
    {
        var st = GameEngine.CreateMatch(new MatchConfig { SouthDeck = "st01", NorthDeck = "st01", Seed = "crocmihawk" });
        st.Status = "active"; st.Phase = "main"; st.ActiveSeat = "south"; st.TurnNumber = 6;
        var S = st.Players["south"];
        S.TurnsStarted = 4;
        S.Hand.Clear();
        for (int i = 0; i < 5; i++) S.CharacterArea[i] = null;
        S.CostArea.Clear();
        for (int i = 0; i < 10; i++) S.CostArea.Add(new DonInstance { InstanceId = $"cmd{i}", Rested = false });
        S.DonDeck = 0;
        // Three plain non-Cross-Guild cards in hand so the trash step has a real target and the
        // play step correctly finds nothing eligible (so it skips, not mis-fires).
        for (int i = 0; i < 3; i++) { var h = MakeInPlay("ST01-006", "south"); h.Zone = "hand"; S.Hand.Add(h); }
        var croc = MakeInPlay("ST25-003", "south"); croc.Zone = "hand"; S.Hand.Add(croc);
        int handBefore = S.Hand.Count;              // 4 (3 filler + Crocodile)
        int trashBefore = S.Trash.Count;

        Apply(st, new GameCommand { Type = "playCard", Seat = "south", InstanceId = croc.InstanceId, SlotIndex = 0 });
        // First prompt after the (silent) draw MUST be the trash step, in the hand zone.
        var firstPrompt = st.PendingEffects.FirstOrDefault(e => e.Seat == "south");
        bool firstIsTrash = firstPrompt != null
            && firstPrompt.TargetZone == EffectTargetZone.Hand
            && (firstPrompt.Text ?? "").TrimStart().StartsWith("trash", StringComparison.OrdinalIgnoreCase);
        // Drive it: pick a filler card for the trash, then let the play step skip (no Cross Guild).
        var fillerId = S.Hand.First(c => c.CardId == "ST01-006").InstanceId;
        Drive(st, "south", fillerId);

        int drew = S.Hand.Count + S.Trash.Count - handBefore - trashBefore;   // net cards entering hand+trash = draws
        bool trashed = S.Trash.Count == trashBefore + 1;
        // hand: 4 start, +2 draw, −1 played(Crocodile to board), −1 trashed = 4
        bool handOk = S.Hand.Count == handBefore + 2 - 1 - 1;
        Check("ST25-003 Crocodile & Mihawk: On Play prompts TRASH before play (draw 2 → trash 1 → play)",
              firstIsTrash && trashed && handOk,
              $"firstIsTrash={firstIsTrash} trashed={trashed} hand={S.Hand.Count}(exp {handBefore} ) trash={S.Trash.Count}  {Tail(st)}");
    }

    // Bug (surfaced by the draw+trash fix): OP04-083 Sabo "[On Play] None of your Characters can be
    // K.O.'d by effects until the start of your next turn. Then, draw 2 cards and trash 2 cards from
    // your hand." The unrecognized board-wide immunity clause meant the ". Then," split never fired,
    // so the greedy Draw handler drew 2 and DROPPED the trash. Verify all three: immunity applied
    // (Sabo survives an effect K.O.), drew 2, trashed 2. Rulebook §10-2-1 (K.O. / immunity).
    static void SaboImmunityThenDrawTrash()
    {
        var st = GameEngine.CreateMatch(new MatchConfig { SouthDeck = "st01", NorthDeck = "st01", Seed = "sabo" });
        st.Status = "active"; st.Phase = "main"; st.ActiveSeat = "south"; st.TurnNumber = 6;
        var S = st.Players["south"];
        S.TurnsStarted = 4;
        S.Hand.Clear();
        for (int i = 0; i < 5; i++) S.CharacterArea[i] = null;
        S.CostArea.Clear();
        for (int i = 0; i < 10; i++) S.CostArea.Add(new DonInstance { InstanceId = $"sbd{i}", Rested = false });
        S.DonDeck = 0;
        for (int i = 0; i < 4; i++) { var h = MakeInPlay("ST01-006", "south"); h.Zone = "hand"; S.Hand.Add(h); }
        var sabo = MakeInPlay("OP04-083", "south"); sabo.Zone = "hand"; S.Hand.Add(sabo);
        var fillerIds = S.Hand.Where(c => c.CardId == "ST01-006").Select(c => c.InstanceId).ToArray();

        Apply(st, new GameCommand { Type = "playCard", Seat = "south", InstanceId = sabo.InstanceId, SlotIndex = 0 });
        Drive(st, "south", fillerIds);   // enough distinct hand cards for the 2-card trash step

        var saboInPlay = S.CharacterArea.FirstOrDefault(c => c?.CardId == "OP04-083");
        bool onBoard = saboInPlay != null;
        bool trashed2 = S.Trash.Count(c => c.CardId == "ST01-006") == 2;
        // Immunity: an opponent effect that K.O.s a low-cost Character must NOT trash Sabo.
        bool immune = false;
        if (onBoard)
        {
            var koEff = new PendingEffect { EffectId = "koTest", Seat = "north", SourceInstanceId = st.Players["north"].Leader.InstanceId,
                SourceCardId = st.Players["north"].Leader.CardId, Timing = "main",
                Text = "K.O. up to 1 of your opponent's Characters with a cost of 10 or less.", TargetZone = EffectTargetZone.Play };
            st.PendingEffects.Add(koEff);
            Apply(st, new GameCommand { Type = "resolveEffect", Seat = "north", EffectId = "koTest", Target = saboInPlay.InstanceId });
            immune = S.CharacterArea.Any(c => c?.CardId == "OP04-083"); // still there = immune
        }
        Check("OP04-083 Sabo: board immunity + draw 2 + trash 2 all resolve (dropped-clause + §10-2-1)",
              onBoard && trashed2 && immune,
              $"onBoard={onBoard} trashed2={trashed2} immune={immune} trash={S.Trash.Count}  {Tail(st)}");
    }

    // Rulebook §10-1-1 [Rush] and §10-2-9 [DON!! x1]: passive keyword grants must apply live.
    // Rush: a [Rush] Character played this turn is NOT summoning-sick (can attack); a non-Rush one
    // is. [DON!! x1] gains [Blocker]: Blocker is granted only while ≥1 DON!! is attached.
    static void PassiveKeywordGrants()
    {
        var st = GameEngine.CreateMatch(new MatchConfig { SouthDeck = "st01", NorthDeck = "st01", Seed = "passive" });
        st.Status = "active"; st.Phase = "main"; st.ActiveSeat = "south"; st.TurnNumber = 6;
        var S = st.Players["south"];
        for (int i = 0; i < 5; i++) S.CharacterArea[i] = null;
        // [Rush]: OP01-025 Zoro played THIS turn can attack; ST01-006 (no Rush) cannot.
        var zoro = MakeInPlay("OP01-025", "south"); zoro.PlayedOnTurn = st.TurnNumber; zoro.Rested = false; S.CharacterArea[0] = zoro;
        var plain = MakeInPlay("ST01-006", "south"); plain.PlayedOnTurn = st.TurnNumber; plain.Rested = false; S.CharacterArea[1] = plain;
        bool rushOk = !GameEngine.IsSummoningSick(st, zoro) && GameEngine.IsSummoningSick(st, plain);

        // [DON!! x1] This Character gains [Blocker]: P-004 Crocodile — Blocker only with DON attached.
        var croc = MakeInPlay("P-004", "south"); croc.PlayedOnTurn = 0; S.CharacterArea[2] = croc;
        bool noBlockerNoDon = !GameEngine.HasBlocker(st, croc);
        croc.AttachedDonIds.Add("don-on-croc");
        bool blockerWithDon = GameEngine.HasBlocker(st, croc);

        Check("§10-1-1/§10-2-9: [Rush] beats summoning sickness; [DON!! x1] grants [Blocker] only with DON attached",
              rushOk && noBlockerNoDon && blockerWithDon,
              $"rushOk={rushOk} blockerNoDon(want F)={!noBlockerNoDon} blockerWithDon={blockerWithDon}  {Tail(st)}");
    }

    // Playtest bug (reported): ST13-007 Sabo "[Activate: Main] trash this: Reveal top of Life. If it's
    // a [Sabo] cost 5, you may play it. If you do, your Leader gains +2000…". The +2000 was firing
    // even when no 5-cost Sabo was revealed/played. Verify it only buffs when the Sabo is played.
    static void SaboRevealPlayGatesBuff()
    {
        GameState Build(string topOfLifeCardId)
        {
            var st = GameEngine.CreateMatch(new MatchConfig { SouthDeck = "st01", NorthDeck = "st01", Seed = "sabo7:" + topOfLifeCardId });
            st.Status = "active"; st.Phase = "main"; st.ActiveSeat = "south"; st.TurnNumber = 6;
            var S = st.Players["south"]; S.TurnsStarted = 4;
            for (int i = 0; i < 5; i++) S.CharacterArea[i] = null;
            for (int i = 0; i < 10; i++) S.CostArea.Add(new DonInstance { InstanceId = $"sbd{i}", Rested = false });
            S.Life.Clear();
            S.Life.Add(MakeInPlay(topOfLifeCardId, "south"));   // end of list = TOP of Life (revealed)
            var sabo = MakeInPlay("ST13-007", "south"); sabo.PlayedOnTurn = 0; S.CharacterArea[0] = sabo;
            Apply(st, new GameCommand { Type = "activateMain", Seat = "south", Target = sabo.InstanceId });
            Drive(st, "south");
            return st;
        }
        int leaderBase = GameEngine.GetCard(GameEngine.CreateMatch(new MatchConfig { SouthDeck = "st01", NorthDeck = "st01", Seed = "x" }).Players["south"].Leader).Power;
        var withSabo = Build("ST13-008");   // 5-cost Sabo on top → played, Leader +2000
        bool played = withSabo.Players["south"].CharacterArea.Any(c => c?.CardId == "ST13-008");
        bool buffed = GameEngine.GetPower(withSabo, withSabo.Players["south"].Leader) >= leaderBase + 2000;
        var noSabo = Build("ST01-006");     // non-Sabo on top → NOT played, NO buff
        bool notPlayed = !noSabo.Players["south"].CharacterArea.Any(c => c?.CardId == "ST01-006");
        bool notBuffed = GameEngine.GetPower(noSabo, noSabo.Players["south"].Leader) < leaderBase + 2000;
        Check("ST13-007 Sabo: Leader +2000 fires ONLY when a 5-cost Sabo is revealed & played",
              played && buffed && notPlayed && notBuffed,
              $"withSabo[played={played} buffed={buffed}] noSabo[notPlayed={notPlayed} notBuffed={notBuffed}]  {Tail(withSabo)}");
    }

    // Official Q&A (OP02-027 Inuarashi): "cannot be removed from the field by your opponent's
    // effects" also blocks being RETURNED TO HAND / placed on deck / trashed — not only K.O. The
    // bounce/deck handlers weren't checking removal immunity. Gate: "if all of your DON!! cards are
    // rested". Verify Inuarashi resists a return-to-hand while a normal Character is bounced.
    static void RemovalImmunityBlocksBounce()
    {
        GameState st(bool donRested)
        {
            var s = GameEngine.CreateMatch(new MatchConfig { SouthDeck = "st01", NorthDeck = "st01", Seed = "removeimm" + donRested });
            s.Status = "active"; s.Phase = "main"; s.ActiveSeat = "north"; s.TurnNumber = 6;
            var S = s.Players["south"]; S.TurnsStarted = 4;
            for (int i = 0; i < 5; i++) S.CharacterArea[i] = null;
            S.CostArea.Clear();
            for (int i = 0; i < 4; i++) S.CostArea.Add(new DonInstance { InstanceId = $"rid{i}", Rested = donRested });
            var inu = MakeInPlay("OP02-027", "south"); S.CharacterArea[0] = inu;
            var eff = new PendingEffect { EffectId = "bounce", Seat = "north", SourceInstanceId = s.Players["north"].Leader.InstanceId,
                SourceCardId = s.Players["north"].Leader.CardId, Timing = "main",
                Text = "Return up to 1 Character with a cost of 10 or less to the owner's hand.", TargetZone = EffectTargetZone.Play };
            s.PendingEffects.Add(eff);
            Apply(s, new GameCommand { Type = "resolveEffect", Seat = "north", EffectId = "bounce", Target = inu.InstanceId });
            return s;
        }
        var rested = st(true);    // all DON rested → immune → stays on board
        bool resisted = rested.Players["south"].CharacterArea.Any(c => c?.CardId == "OP02-027");
        var active = st(false);   // DON active → condition unmet → bounced to hand
        bool bounced = active.Players["south"].Hand.Any(c => c.CardId == "OP02-027");
        Check("Q&A OP02-027: 'cannot be removed' blocks a bounce when its condition holds (not when it doesn't)",
              resisted && bounced, $"resistedWhenRested={resisted} bouncedWhenActive={bounced}  {Tail(rested)}");
    }

    // Rulebook §8-1-3-4 replacement effects: EB04-031 King "If this Character would be K.O.'d, you
    // may return 1 DON!! card from your field to your DON!! deck instead." The "would be K.O.'d"
    // trigger + the "return DON!! instead" action were unimplemented — King should SURVIVE an effect
    // K.O. by returning a DON!! (and be K.O.'d normally when it can't pay).
    static void KingKoReplacementReturnDon()
    {
        GameState Build(int donInCost)
        {
            var st = GameEngine.CreateMatch(new MatchConfig { SouthDeck = "st01", NorthDeck = "st01", Seed = "king" + donInCost });
            st.Status = "active"; st.Phase = "main"; st.ActiveSeat = "south"; st.TurnNumber = 6;
            var S = st.Players["south"]; S.TurnsStarted = 4;
            for (int i = 0; i < 5; i++) S.CharacterArea[i] = null;
            S.CostArea.Clear();
            for (int i = 0; i < donInCost; i++) S.CostArea.Add(new DonInstance { InstanceId = $"kdon{i}", Rested = false });
            S.DonDeck = 10 - donInCost;
            var king = MakeInPlay("EB04-031", "south"); S.CharacterArea[0] = king;
            var koEff = new PendingEffect { EffectId = "koKing", Seat = "north", SourceInstanceId = st.Players["north"].Leader.InstanceId,
                SourceCardId = st.Players["north"].Leader.CardId, Timing = "main",
                Text = "K.O. up to 1 of your opponent's Characters with a cost of 10 or less.", TargetZone = EffectTargetZone.Play };
            st.PendingEffects.Add(koEff);
            Apply(st, new GameCommand { Type = "resolveEffect", Seat = "north", EffectId = "koKing", Target = king.InstanceId });
            return st;
        }
        var withDon = Build(3);      // has DON → survives by returning 1
        bool survived = withDon.Players["south"].CharacterArea.Any(c => c?.CardId == "EB04-031");
        bool donReturned = withDon.Players["south"].DonDeck == 8 && withDon.Players["south"].CostArea.Count == 2;
        var noDon = Build(0);        // no DON → cannot pay → K.O.'d normally
        bool koed = noDon.Players["south"].Trash.Any(c => c.CardId == "EB04-031");
        Check("§8-1-3-4 EB04-031 King: 'would be K.O.'d → return 1 DON!! instead' (survives w/ DON, K.O.'d without)",
              survived && donReturned && koed,
              $"survived={survived} donReturned={donReturned}(deck={withDon.Players["south"].DonDeck}) koedNoDon={koed}  {Tail(withDon)}");
    }

    // Attribute-icon data restoration + ＜X＞ target filter: OP04-042 Ipponmatsu (own attribute
    // Wisdom!) "[On Play] Up to 1 of your ＜Slash＞ attribute Characters gains +3000 power…". The
    // ＜Slash＞ icon was stripped from the JSON; restored + made the target filter attribute-aware.
    // The buff must apply ONLY to a Slash Character (Zoro), not a Strike one (Chopper).
    static void IpponmatsuSlashAttributeFilter()
    {
        var st = GameEngine.CreateMatch(new MatchConfig { SouthDeck = "st01", NorthDeck = "st01", Seed = "ippon" });
        st.Status = "active"; st.Phase = "main"; st.ActiveSeat = "south"; st.TurnNumber = 6;
        var S = st.Players["south"]; S.TurnsStarted = 4;
        for (int i = 0; i < 5; i++) S.CharacterArea[i] = null;
        for (int i = 0; i < 10; i++) S.CostArea.Add(new DonInstance { InstanceId = $"ipd{i}", Rested = false });
        var zoro = MakeInPlay("OP01-025", "south"); zoro.PlayedOnTurn = 0; S.CharacterArea[0] = zoro;   // Slash 5000
        var chopper = MakeInPlay("ST01-006", "south"); chopper.PlayedOnTurn = 0; S.CharacterArea[1] = chopper; // Strike 1000
        int zoroBase = GameEngine.GetPower(st, zoro), chopBase = GameEngine.GetPower(st, chopper);
        var ippon = MakeInPlay("OP04-042", "south"); ippon.Zone = "hand"; S.Hand.Add(ippon);

        Apply(st, new GameCommand { Type = "playCard", Seat = "south", InstanceId = ippon.InstanceId, SlotIndex = 2 });
        var pe = st.PendingEffects.FirstOrDefault(e => e.Seat == "south");
        // Try to buff the Strike Character (Chopper) → must be rejected (not a ＜Slash＞ attribute Character).
        if (pe != null) Apply(st, new GameCommand { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId, Target = chopper.InstanceId });
        bool chopperUnbuffed = GameEngine.GetPower(st, chopper) == chopBase;
        // Buff the Slash Character (Zoro) → +3000.
        var pe2 = st.PendingEffects.FirstOrDefault(e => e.Seat == "south");
        if (pe2 != null) Apply(st, new GameCommand { Type = "resolveEffect", Seat = "south", EffectId = pe2.EffectId, Target = zoro.InstanceId });
        bool zoroBuffed = GameEngine.GetPower(st, zoro) == zoroBase + 3000;

        Check("OP04-042 Ipponmatsu ＜Slash＞ filter: buffs the Slash Character, rejects the Strike one",
              chopperUnbuffed && zoroBuffed,
              $"chopperUnbuffed={chopperUnbuffed}({GameEngine.GetPower(st, chopper)}) zoroBuffed={zoroBuffed}({GameEngine.GetPower(st, zoro)} exp {zoroBase + 3000})  {Tail(st)}");
    }

    // Card-data fix (reported): OP12-034 Perona "[On Play] If your Leader has the ＜Slash＞ attribute,
    // look at 5 cards …" — the ＜Slash＞ attribute icon had been STRIPPED from the JSON to a bare
    // "the attribute", so the condition never matched (and couldn't, since Perona's own attribute is
    // Special ≠ Slash). With the data restored + the attribute-condition parser: it fires under a
    // Slash Leader (OP01-001 Zoro) and is gated under a non-Slash Leader.
    static void PeronaSlashAttributeCondition()
    {
        GameState Build(string leaderId)
        {
            var st = GameEngine.CreateMatch(new MatchConfig { SouthDeck = "st01", NorthDeck = "st01", Seed = "perona:" + leaderId });
            st.Status = "active"; st.Phase = "main"; st.ActiveSeat = "south"; st.TurnNumber = 6;
            var S = st.Players["south"]; S.TurnsStarted = 4;
            S.Leader.CardId = leaderId;
            for (int i = 0; i < 5; i++) S.CharacterArea[i] = null;
            for (int i = 0; i < 10; i++) S.CostArea.Add(new DonInstance { InstanceId = $"prn{i}", Rested = false });
            var perona = MakeInPlay("OP12-034", "south"); perona.Zone = "hand"; S.Hand.Add(perona);
            Apply(st, new GameCommand { Type = "playCard", Seat = "south", InstanceId = perona.InstanceId, SlotIndex = 0 });
            return st;
        }
        var slash = Build("OP01-001");     // Zoro — Slash attribute → condition MET → deck-look opens
        bool firedUnderSlash = slash.DeckLook != null && slash.DeckLook.Seat == "south";
        var nonSlash = Build("ST01-001");  // Luffy — Strike attribute → condition NOT met → skipped
        bool gatedUnderStrike = nonSlash.DeckLook == null;
        Check("OP12-034 Perona ＜Slash＞ attribute condition: fires under a Slash Leader, gated otherwise",
              firedUnderSlash && gatedUnderStrike,
              $"firedUnderSlash={firedUnderSlash} gatedUnderStrike={gatedUnderStrike}  {Tail(slash)}");
    }

    // Rulebook §7-1-5-3/4 (power durations) + base-vs-current power/cost. A battle-scoped bonus
    // ("during this battle") is cleared when the battle ends; a turn-scoped bonus ("during this
    // turn") persists through the turn. Base power/cost (GetCard) never changes; GetPower/GetCost
    // reflect the live modifiers.
    static void PowerCostDurationScoping()
    {
        var st = GameEngine.CreateMatch(new MatchConfig { SouthDeck = "st01", NorthDeck = "st01", Seed = "durscope" });
        st.Status = "active"; st.Phase = "main"; st.ActiveSeat = "south"; st.TurnNumber = 6;
        var S = st.Players["south"];
        for (int i = 0; i < 5; i++) S.CharacterArea[i] = null;
        var ch = MakeInPlay("ST01-006", "south"); S.CharacterArea[0] = ch;   // base power 1000
        int basePow = GameEngine.GetCard(ch).Power;

        // Turn-scoped +2000 (cleared at the owner's refresh).
        st.TemporaryPowerBonus[ch.InstanceId] = 2000;
        int powTurn = GameEngine.GetPower(st, ch);                            // base + 2000
        // Battle-scoped +3000 stacks while a battle is live.
        st.Battle = new BattleState { Id = "b1", Step = "counter" };
        st.Battle.BattlePowerBonus[ch.InstanceId] = 3000;
        int powInBattle = GameEngine.GetPower(st, ch);                        // base + 2000 + 3000
        // Battle ends → battle bonus gone, turn bonus remains.
        st.Battle = null;
        int powAfterBattle = GameEngine.GetPower(st, ch);                     // base + 2000

        // Cost: base never changes; per-instance CostDelta raises/lowers current cost (clamped ≥0).
        int baseCost = GameEngine.GetCard(ch).Cost;                           // 2
        ch.Modifiers.Add(new ActiveModifier { CostDelta = 3, ExpiresAt = "endOfTurn" });
        int costUp = GameEngine.GetCost(st, ch);                             // base + 3
        ch.Modifiers.Add(new ActiveModifier { CostDelta = -99, ExpiresAt = "endOfTurn" });
        int costFloor = GameEngine.GetCost(st, ch);                          // clamped to 0

        bool ok = powTurn == basePow + 2000 && powInBattle == basePow + 5000 && powAfterBattle == basePow + 2000
                  && GameEngine.GetCard(ch).Power == basePow
                  && costUp == baseCost + 3 && costFloor == 0 && GameEngine.GetCard(ch).Cost == baseCost;
        Check("§7-1-5 power durations (battle clears at battle-end, turn persists) + base-vs-current power/cost",
              ok, $"turn={powTurn} inBattle={powInBattle} afterBattle={powAfterBattle} base={basePow} | costUp={costUp} floor={costFloor} baseCost={baseCost}");
    }

    // Rulebook §10-2-9 [DON!! xX]: the threshold is generic (x1/x2/x3), not just x1. EB02-003
    // Chopper "[DON!! x2] [Opponent's Turn] This Character gains +2000 power" (base 3000): the buff
    // applies ONLY with ≥2 DON attached, and only on the opponent's turn.
    static void DonX2ThresholdGating()
    {
        var st = GameEngine.CreateMatch(new MatchConfig { SouthDeck = "st01", NorthDeck = "st01", Seed = "donx2" });
        st.Status = "active"; st.Phase = "main"; st.ActiveSeat = "north"; st.TurnNumber = 6; // opponent's (north's) turn
        var S = st.Players["south"];
        for (int i = 0; i < 5; i++) S.CharacterArea[i] = null;
        var chopper = MakeInPlay("EB02-003", "south"); S.CharacterArea[0] = chopper;
        int basePow = GameEngine.GetCard(chopper).Power;   // 3000
        // Measured on the OPPONENT's turn, where attached DON give NO raw power (they only add
        // +1000 each on the OWNER's turn) — this isolates the [DON!! x2][Opponent's Turn] buff.
        int pow0 = GameEngine.GetPower(st, chopper);       // 0 DON, opp turn → base
        chopper.AttachedDonIds.Add("d1");
        int pow1 = GameEngine.GetPower(st, chopper);       // 1 DON, opp turn → base (< x2)
        chopper.AttachedDonIds.Add("d2");
        int pow2 = GameEngine.GetPower(st, chopper);       // 2 DON, opp turn → +2000 buff
        // On the OWNER's turn with 2 DON: raw DON give +2000, and the [Opponent's Turn] buff must
        // NOT also apply (would be base+4000 if it wrongly stacked).
        st.ActiveSeat = "south";
        int powOwnTurn = GameEngine.GetPower(st, chopper);

        bool ok = pow0 == basePow && pow1 == basePow && pow2 == basePow + 2000 && powOwnTurn == basePow + 2000;
        Check("§10-2-9 [DON!! x2]: +2000 buff needs ≥2 DON & opponent's turn (not stacked with raw DON on own turn)",
              ok, $"base={basePow} oppTurn[don0={pow0} don1={pow1} don2={pow2}] ownTurn2don={powOwnTurn}(exp {basePow+2000})  {Tail(st)}");
    }

    // Bug: "add up to 1 DON!! card from your DON!! deck and set it as active, and add up to 1
    // additional DON!! card and rest it" (EB04-031 King, OP09-061 P/B Luffy) must add ONE ACTIVE +
    // ONE RESTED (2 DON). The single rested-flag handler saw "rest it" and added only 1 rested.
    static void DonAddActiveAndRested()
    {
        var st = GameEngine.CreateMatch(new MatchConfig { SouthDeck = "st01", NorthDeck = "st01", Seed = "donaddar" });
        st.Status = "active"; st.Phase = "main"; st.ActiveSeat = "south"; st.TurnNumber = 6;
        var S = st.Players["south"];
        S.TurnsStarted = 4;
        S.CostArea.Clear();
        for (int i = 0; i < 5; i++) S.CostArea.Add(new DonInstance { InstanceId = $"dad{i}", Rested = false });
        S.DonDeck = 5;   // 5 cost + 5 deck = 10 total (conservation)
        int costBefore = S.CostArea.Count;      // 5
        int deckBefore = S.DonDeck;             // 5

        var eff = new PendingEffect { EffectId = "donAdd", Seat = "south", SourceInstanceId = S.Leader.InstanceId,
            SourceCardId = S.Leader.CardId, Timing = "main",
            Text = "add up to 1 DON!! card from your DON!! deck and set it as active, and add up to 1 additional DON!! card and rest it." };
        st.PendingEffects.Add(eff);
        Apply(st, new GameCommand { Type = "resolveEffect", Seat = "south", EffectId = "donAdd", Target = null });

        int added = S.CostArea.Count - costBefore;
        int nowActive = S.CostArea.Count(d => !d.Rested);
        int nowRested = S.CostArea.Count(d => d.Rested);
        bool ok = added == 2 && S.DonDeck == deckBefore - 2 && nowActive == 6 && nowRested == 1;
        Check("DON!! deck→cost: 'set active, and add 1 additional and rest it' adds 1 ACTIVE + 1 RESTED",
              ok, $"added={added} deck={S.DonDeck}(exp {deckBefore-2}) active={nowActive}(exp 6) rested={nowRested}(exp 1)  {Tail(st)}");
    }

    // Rulebook §10-2-1-3: a Character trashed by "some other method" (played over on a full board)
    // is NOT K.O.'d, so its [On K.O.] must NOT fire. Play a 6th Character over P-071 Marco ("[On
    // K.O.] You may add this Character card to your hand."): Marco must go to TRASH (not hand) and
    // no On-K.O. effect should be pending.
    static void PlayedOverIsNotAKo()
    {
        var st = GameEngine.CreateMatch(new MatchConfig { SouthDeck = "st01", NorthDeck = "st01", Seed = "playover" });
        st.Status = "active"; st.Phase = "main"; st.ActiveSeat = "south"; st.TurnNumber = 6;
        var S = st.Players["south"];
        S.TurnsStarted = 4;
        S.Hand.Clear();
        S.CostArea.Clear();
        for (int i = 0; i < 10; i++) S.CostArea.Add(new DonInstance { InstanceId = $"pod{i}", Rested = false });
        S.DonDeck = 0;
        // Full board; slot 0 = Marco P-071 (has [On K.O.]).
        var marco = MakeInPlay("P-071", "south"); S.CharacterArea[0] = marco;
        for (int i = 1; i < 5; i++) S.CharacterArea[i] = MakeInPlay("ST01-006", "south");
        var newChar = MakeInPlay("ST01-006", "south"); newChar.Zone = "hand"; S.Hand.Add(newChar);

        Apply(st, new GameCommand { Type = "playCard", Seat = "south", InstanceId = newChar.InstanceId, SlotIndex = 0 });

        bool marcoInTrash = S.Trash.Any(c => c.CardId == "P-071");
        bool marcoInHand = S.Hand.Any(c => c.CardId == "P-071");
        bool noOnKoPending = !st.PendingEffects.Any(e => e.Seat == "south" && e.Timing == "onKo");
        Check("§10-2-1-3: playing over a Character is NOT a K.O. (P-071 Marco's [On K.O.] does not fire)",
              marcoInTrash && !marcoInHand && noOnKoPending,
              $"inTrash={marcoInTrash} inHand={marcoInHand} noOnKoPending={noOnKoPending}  {Tail(st)}");
    }

    // Resolve pending effects for `seat`, trying the given target ids in order across steps
    // (and skipping when none apply), until nothing is pending. Mirrors the coverage driver.
    static void Drive(GameState st, string seat, params string[] targets)
    {
        for (int step = 0; step < 60; step++)
        {
            if (st.ActiveChoice != null && st.ActiveChoice.Seat == seat)
            { Apply(st, new GameCommand { Type = "resolveChoice", Seat = seat, Target = "A" }); continue; }
            var pe = st.PendingEffects.FirstOrDefault(e => e.Seat == seat);
            if (pe == null) break;
            string id = pe.EffectId;
            int before = st.PendingEffects.Count; int sel = pe.SelectionsRemaining;
            Apply(st, new GameCommand { Type = "resolveEffect", Seat = seat, EffectId = id, Target = null });
            bool progressed = !st.PendingEffects.Any(e => e.EffectId == id) || st.PendingEffects.Count != before
                              || (st.PendingEffects.FirstOrDefault(e => e.EffectId == id)?.SelectionsRemaining ?? -1) != sel;
            if (!progressed)
            {
                foreach (var t in targets)
                {
                    int b2 = st.PendingEffects.Count; var pe2 = st.PendingEffects.FirstOrDefault(e => e.EffectId == id); int s2 = pe2?.SelectionsRemaining ?? -1;
                    Apply(st, new GameCommand { Type = "resolveEffect", Seat = seat, EffectId = id, Target = t });
                    var pe3 = st.PendingEffects.FirstOrDefault(e => e.EffectId == id);
                    if (pe3 == null || st.PendingEffects.Count != b2 || (pe3.SelectionsRemaining) != s2) { progressed = true; break; }
                }
            }
            if (!progressed) Apply(st, new GameCommand { Type = "passEffect", Seat = seat, EffectId = id });
        }
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

    // Bug: ST10-001 Trafalgar Law's [Activate: Main] DON!! -3 is a COMPOUND effect —
    // "Place up to 1 … at the bottom of the owner's deck, AND play up to 1 Character card
    // (cost <= 4) from your hand." Only the first clause fired; the play-from-hand clause
    // was silently dropped. Verify BOTH clauses resolve.
    static void LawSecondClausePlaysFromHand()
    {
        var st = GameEngine.CreateMatch(new MatchConfig { SouthDeck = "st10", NorthDeck = "st01", Seed = "lawcompound" });
        st.Status = "active"; st.Phase = "main"; st.ActiveSeat = "south"; st.TurnNumber = 3;
        var S = st.Players["south"]; var N = st.Players["north"];
        S.TurnsStarted = 2; N.TurnsStarted = 2;
        for (int i = 0; i < 5; i++) { S.CharacterArea[i] = null; N.CharacterArea[i] = null; }

        var sink = MakeInPlay("ST01-006", "north"); sink.Rested = true; N.CharacterArea[0] = sink;   // Chopper 1000 (<=3000)
        var handChar = MakeInPlay("ST01-006", "south"); handChar.Zone = "hand"; S.Hand.Add(handChar); // cost-1 Character to play
        for (int i = 0; i < 4; i++) S.CostArea.Add(new DonInstance { InstanceId = $"lawdon{i}", Rested = false });

        Apply(st, new GameCommand { Type = "activateMain", Seat = "south", Target = S.Leader.InstanceId });
        var pe = st.PendingEffects.FirstOrDefault(e => e.Seat == "south");
        // One resolve with the sink target both auto-pays DON!! -3 and resolves the place clause.
        if (pe != null) Apply(st, new GameCommand { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId, Target = sink.InstanceId });
        // The queued rider ("play up to 1 Character … from your hand") now waits for a hand pick.
        var rider = st.PendingEffects.FirstOrDefault(e => e.Seat == "south");
        if (rider != null) Apply(st, new GameCommand { Type = "resolveEffect", Seat = "south", EffectId = rider.EffectId, Target = handChar.InstanceId });

        bool sunkToDeck = N.Deck.Any(c => c.InstanceId == sink.InstanceId) && !N.CharacterArea.Any(c => c?.InstanceId == sink.InstanceId);
        bool played = S.CharacterArea.Any(c => c?.InstanceId == handChar.InstanceId) && !S.Hand.Any(c => c.InstanceId == handChar.InstanceId);
        int donLeft = S.CostArea.Count;
        Check("Law ST10-001 DON!! -3: BOTH place-at-bottom AND play-from-hand resolve",
              sunkToDeck && played && donLeft == 1,
              $"sunkToDeck={sunkToDeck} played={played} donLeft={donLeft}  {Tail(st)}");
    }

    // Bug: ST10-011 Heat gains +2000 "When a DON!! card … is returned to your DON!! deck".
    // Paying a DON!! -3 cost by clicking 3 individual DON!! was firing the trigger 3 times
    // (+6000). A single DON!! -N return is ONE event → Heat should gain +2000 exactly once.
    static void HeatCountsDonMinusAsOneEvent()
    {
        var st = HeatBoardWithLaw(out var S, out var heat);
        Apply(st, new GameCommand { Type = "activateMain", Seat = "south", Target = S.Leader.InstanceId });
        var pe = st.PendingEffects.FirstOrDefault(e => e.Seat == "south");
        // Pay the -3 by clicking three individual cost-area DON!! (the per-click path).
        for (int i = 0; i < 3 && pe != null; i++)
            Apply(st, new GameCommand { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId, Target = $"heatdon{i}" });

        int power = GameEngine.GetPower(st, heat);
        Check("Heat ST10-011: DON!! -3 counts as ONE return event (+2000, not +6000)",
              power == 6000, $"heatPower={power} (base 4000, expected 6000)  {Tail(st)}");
    }

    // Bug companion: Heat is [Once Per Turn]. Even across separate DON!!-return events in the
    // same turn, it may only gain +2000 once.
    static void HeatDonReturnedOncePerTurn()
    {
        var st = HeatBoardWithLaw(out var S, out var heat);
        // First return event: click one DON!! for the -3 cost… then finish the other two.
        Apply(st, new GameCommand { Type = "activateMain", Seat = "south", Target = S.Leader.InstanceId });
        var pe = st.PendingEffects.FirstOrDefault(e => e.Seat == "south");
        for (int i = 0; i < 3 && pe != null; i++)
            Apply(st, new GameCommand { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId, Target = $"heatdon{i}" });
        bool onceMarked = S.AbilityUsedThisTurn.Contains(heat.InstanceId + ":donReturned");
        int power = GameEngine.GetPower(st, heat);
        Check("Heat ST10-011: [Once Per Turn] gate is consumed after a DON!! return",
              onceMarked && power == 6000, $"onceMarked={onceMarked} heatPower={power}  {Tail(st)}");
    }

    // South = ST10 Law leader with a Heat (ST10-011) on board and exactly 3 active DON!! whose
    // instance ids are heatdon0..2 (so tests can click them individually).
    static GameState HeatBoardWithLaw(out PlayerState S, out CardInstance heat)
    {
        var st = GameEngine.CreateMatch(new MatchConfig { SouthDeck = "st10", NorthDeck = "st01", Seed = "heat" });
        st.Status = "active"; st.Phase = "main"; st.ActiveSeat = "south"; st.TurnNumber = 3;
        S = st.Players["south"]; var N = st.Players["north"];
        S.TurnsStarted = 2; N.TurnsStarted = 2;
        for (int i = 0; i < 5; i++) { S.CharacterArea[i] = null; N.CharacterArea[i] = null; }
        heat = MakeInPlay("ST10-011", "south"); heat.Rested = false; heat.PlayedOnTurn = 0; S.CharacterArea[0] = heat;
        for (int i = 0; i < 3; i++) S.CostArea.Add(new DonInstance { InstanceId = $"heatdon{i}", Rested = false });
        return st;
    }

    // Fix (b): "[Your Turn] [Once Per Turn] If a Character is rested by your effect, <X>" (OP07-031
    // Bartolomeo, OP10-036 Perona) was DEAD — EvaluateCondition didn't recognise the condition, so it
    // fired never and spammed "Unknown condition …" on every poll. The general fix: a per-turn
    // GameState.CharRestedByEffectThisTurn flag, SET by the effect-rest-a-target resolvers and READ by
    // the shared condition. This asserts the mechanism directly (text-driven, no card-id logic): resting
    // an opponent's Character via an effect sets the flag, and it resets at the next turn start.
    static void RestByYourEffectFlagSetAndReset()
    {
        var st = GameEngine.CreateMatch(new MatchConfig { SouthDeck = "st01", NorthDeck = "st01", Seed = "restflag" });
        st.Status = "active"; st.Phase = "main"; st.ActiveSeat = "south"; st.TurnNumber = 4;
        var S = st.Players["south"]; var N = st.Players["north"];
        S.TurnsStarted = 3;
        for (int i = 0; i < 5; i++) { S.CharacterArea[i] = null; N.CharacterArea[i] = null; }
        for (int i = 0; i < 5; i++) S.CostArea.Add(new DonInstance { InstanceId = $"rf{i}", Rested = false });
        // Opponent has a cost-1 Character (Laffitte) — a legal target for EB01-015's "cost 2 or less" rest.
        var victim = MakeInPlay("OP09-095", "north");
        N.CharacterArea[0] = victim;
        // South plays EB01-015: "[On Play] Rest up to 1 of your opponent's Characters with a cost of 2 or less."
        var mover = MakeInPlay("EB01-015", "south"); mover.Zone = "hand"; S.Hand.Add(mover);

        bool emptyBefore = !st.CharRestedByEffectThisTurn.Contains("south");
        Apply(st, new GameCommand { Type = "playCard", Seat = "south", InstanceId = mover.InstanceId, SlotIndex = 0 });
        var pend = st.PendingEffects.FirstOrDefault();
        Apply(st, new GameCommand { Type = "resolveEffect", Seat = "south", EffectId = pend?.EffectId, Target = victim.InstanceId });

        bool victimRested = victim.Rested;
        bool flagSet = st.CharRestedByEffectThisTurn.Contains("south");

        // Reset: passing the turn to north clears the flag at north's turn start (ApplyStartOfTurn).
        Apply(st, new GameCommand { Type = "endTurn", Seat = "south" });
        bool flagReset = !st.CharRestedByEffectThisTurn.Contains("south");

        Check("Fix (b): resting an opponent Character by effect sets CharRestedByEffectThisTurn, resets next turn",
              emptyBefore && victimRested && flagSet && flagReset,
              $"emptyBefore={emptyBefore} victimRested={victimRested} flagSet={flagSet} flagReset={flagReset}  {Tail(st)}");
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
