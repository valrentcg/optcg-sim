using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

/// <summary>
/// Player-visible card-mechanics contract through CardData -> GameEngine -> GameManager.  These are
/// deliberately representative seams, not a claim that every printing is covered.  New bug repros
/// should become one small scenario here after a headless engine regression exists.
/// </summary>
public class CardMechanicsEndToEndPlayModeTests
{
    [UnityTest]
    public IEnumerator RepresentativeChoicesCostsTriggersOnceAndHiddenZonesReachTheUnityClient()
    {
        yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
        for (int i = 0; i < 10; i++) yield return null;

        var gameManagerType = Type.GetType("GameManager, Assembly-CSharp");
        Assert.That(gameManagerType, Is.Not.Null);
        Component manager = FindManager();
        if (manager == null)
        {
            gameManagerType.GetMethod("EnsureBoard", CardMechanicsPlayModeHarness.StaticAny).Invoke(null, null);
            for (int i = 0; i < 5; i++) yield return null;
            manager = FindManager();
        }
        Assert.That(manager, Is.Not.Null, "Could not boot the production GameManager.");

        var h = new CardMechanicsPlayModeHarness(manager);
        Assert.That((bool)h.CardDataType.GetProperty("OfficialLibraryLoaded").GetValue(null), Is.True,
            "The official card library must load before an end-to-end card test is meaningful.");

        OpponentHandPlayabilityDoesNotLeakThroughGreenHover(h);
        yield return null;
        ChoicePromptRoutesToTheDecidingSeatAndDispatchesForBothSeats(h);
        yield return null;
        LifeCostHighlightsAndResolvesForBothSeats(h);
        yield return null;
        TriggerButtonsBelongToTheDefenderAndPlayTheRevealedCard(h);
        yield return null;
        OncePerTurnOptionalSkipDoesNotConsumeButUseDoes(h);
        yield return null;
        HiddenDeckLookRejectsTheOtherSeatAndConservesEveryCard(h);
        yield return null;
        PublicRevealFlipsOnlyExactOpponentHandCardsAndOwnsTheActionPanel(h);
        yield return null;
        ImuMixedBoardOrHandCostUsesTheHighlightedCardClickRoute(h);
        yield return null;
        NewgateLeaderCombatPromptBelongsToTheDefenderAndResolvesThroughClicks(h);
        yield return null;
        MaserSaberRoutesTheOpponentDecisionThenReturnsTheContinuationToItsOwner(h);
        h.CancelTransientAnimations();
        yield return null;
    }

    private static Component FindManager()
    {
        foreach (var candidate in UnityEngine.Object.FindObjectsByType<MonoBehaviour>())
            if (candidate.GetType().Name == "GameManager") return candidate;
        return null;
    }

    private static void OpponentHandPlayabilityDoesNotLeakThroughGreenHover(CardMechanicsPlayModeHarness h)
    {
        var state = h.NewActiveState("north", "opponent-hand-hover-privacy");
        var playable = h.Hand(state, "north", "ST01-006");
        for (int i = 0; i < 10; i++) h.Don(state, "north", rested: false);

        Assert.That((bool)CardMechanicsPlayModeHarness.InvokeStatic(
                h.EngineType, "IsPlayableNow", state, "north", playable), Is.True,
            "The regression fixture must use a card the opponent can currently play.");

        h.SetNetworkViewer("south");
        Assert.That(h.IsGreenTarget(playable), Is.False,
            "A playable card in the remote opponent's hand leaked its playability through the green hover glow.");

        h.SetNetworkViewer("north");
        Assert.That(h.IsGreenTarget(playable), Is.True,
            "The same playable card did not glow for the local player who owns and can act with it.");

        h.AttachState(state);
        CardMechanicsPlayModeHarness.Set(h.Manager, "aiSeat", "north");
        Assert.That(h.IsGreenTarget(playable), Is.False,
            "A playable AI hand card leaked its playability through the local hover glow.");

        h.AttachState(state);
        Assert.That(h.IsGreenTarget(playable), Is.True,
            "Versus Self should keep both locally controlled hands actionable.");
    }

    private static void ChoicePromptRoutesToTheDecidingSeatAndDispatchesForBothSeats(CardMechanicsPlayModeHarness h)
    {
        foreach (var seat in new[] { "south", "north" })
        {
            var state = h.NewActiveState(seat, "choice-" + seat);
            var bepo = h.Character(state, seat, "OP13-035", rested: true);
            h.Don(state, seat, rested: true);
            var clause = h.StripTimingTags(h.CardText("OP13-035", "Effect"));
            StringAssert.Contains("this Character or up to 1 of your DON!!", clause,
                "The fixture must follow the current CardData text, not a retyped test-only rule.");
            h.QueueCardText(state, seat, bepo, "endOfYourTurn", clause);
            var pending = h.Pending(state, seat);
            Assert.That(pending, Is.Not.Null, seat + " Bepo did not expose a player decision.");
            h.Dispatch(h.Command("resolveEffect", seat, effectId: (string)CardMechanicsPlayModeHarness.Get(pending, "EffectId")));

            var choice = CardMechanicsPlayModeHarness.Get(state, "ActiveChoice");
            Assert.That(choice, Is.Not.Null, seat + " Bepo did not open the production choose-one state.");
            Assert.That(CardMechanicsPlayModeHarness.Get(choice, "Seat"), Is.EqualTo(seat));

            h.SetNetworkViewer(seat == "south" ? "north" : "south");
            var waitingBody = h.DrawActions("DrawChoiceActions");
            Assert.That(CardMechanicsPlayModeHarness.HasDescendant(waitingBody, "Choose A Button"), Is.False,
                "The non-deciding network seat received the opponent's choice controls.");
            UnityEngine.Object.DestroyImmediate(waitingBody.root.gameObject);

            h.SetNetworkViewer(seat);
            var ownerBody = h.DrawActions("DrawChoiceActions");
            Assert.That(CardMechanicsPlayModeHarness.HasDescendant(ownerBody, "Choose A Button"), Is.True);
            Assert.That(CardMechanicsPlayModeHarness.HasDescendant(ownerBody, "Choose B Button"), Is.True);
            UnityEngine.Object.DestroyImmediate(ownerBody.root.gameObject);

            h.Dispatch(h.Command("resolveChoice", seat, target: "A"));
            Assert.That(CardMechanicsPlayModeHarness.Get(bepo, "Rested"), Is.False,
                seat + " chose Bepo itself but the card did not become active.");
            Assert.That(CardMechanicsPlayModeHarness.Get(state, "ActiveChoice"), Is.Null);
        }
    }

    private static void LifeCostHighlightsAndResolvesForBothSeats(CardMechanicsPlayModeHarness h)
    {
        foreach (var seat in new[] { "south", "north" })
        {
            string other = seat == "south" ? "north" : "south";
            var state = h.NewActiveState(seat, "life-cost-" + seat);
            var wyper = h.Character(state, seat, "OP15-114");
            var life = h.Life(state, seat, "ST01-005", faceUp: false);
            var victim = h.Character(state, other, "ST01-003");
            var fullText = h.CardText("OP15-114", "Effect");
            var onPlay = h.StripTimingTags(fullText.Split(new[] { "[Activate: Main]" }, StringSplitOptions.None)[0]);
            StringAssert.StartsWith("You may turn 1 card", onPlay);
            h.QueueCardText(state, seat, wyper, "onPlay", onPlay);

            var pending = h.Pending(state, seat);
            Assert.That(pending, Is.Not.Null, "Wyper's Life cost did not remain an explicit decision.");
            // This cost names the TOP card, not "top or bottom", so there is no target choice:
            // the action-panel button is the affordance and the engine deterministically pays with
            // Life[^1].  Top/bottom costs use the separate clickable Life-card picker.
            Assert.That(CardMechanicsPlayModeHarness.Get(life, "FaceUp"), Is.EqualTo(false));

            h.SetNetworkViewer(seat);
            var body = h.DrawActions("DrawPendingEffectActions");
            Assert.That(CardMechanicsPlayModeHarness.HasDescendant(body,
                    "Turn 1 card from the top of your Life cards face-up Button"), Is.True,
                "The player-facing action did not name Wyper's cost.");
            UnityEngine.Object.DestroyImmediate(body.root.gameObject);

            h.Dispatch(h.Command("resolveEffect", seat,
                effectId: (string)CardMechanicsPlayModeHarness.Get(pending, "EffectId")));
            Assert.That(CardMechanicsPlayModeHarness.Get(life, "FaceUp"), Is.EqualTo(true),
                "Paying Wyper's cost did not visibly turn the selected Life card face-up.");
            Assert.That(CardMechanicsPlayModeHarness.Get(victim, "Zone"), Is.EqualTo("character"),
                "A positive-power control victim was removed unexpectedly while resolving the cost.");
        }
    }

    private static void TriggerButtonsBelongToTheDefenderAndPlayTheRevealedCard(CardMechanicsPlayModeHarness h)
    {
        var state = h.NewActiveState("north", "life-trigger");
        var revealed = h.Card("OP01-009", "south", "life");
        var battle = Activator.CreateInstance(h.BattleType);
        CardMechanicsPlayModeHarness.Set(battle, "Id", "unity-e2e-trigger-battle");
        CardMechanicsPlayModeHarness.Set(battle, "Step", "trigger");
        CardMechanicsPlayModeHarness.Set(battle, "PrioritySeat", "south");
        CardMechanicsPlayModeHarness.Set(battle, "AttackerSeat", "north");
        CardMechanicsPlayModeHarness.Set(battle, "TargetSeat", "south");
        CardMechanicsPlayModeHarness.Set(battle, "TargetId",
            CardMechanicsPlayModeHarness.Get(h.Player(state, "south"), "Leader") is object leader
                ? CardMechanicsPlayModeHarness.Get(leader, "InstanceId") : null);
        CardMechanicsPlayModeHarness.Set(battle, "RevealedLife", revealed);
        CardMechanicsPlayModeHarness.Set(battle, "PendingLifeDamage", 0);
        CardMechanicsPlayModeHarness.Set(state, "Battle", battle);
        h.AttachState(state);

        h.SetNetworkViewer("north");
        var attackerBody = h.DrawActions("DrawBattleActions");
        Assert.That(CardMechanicsPlayModeHarness.HasDescendant(attackerBody, "Resolve Trigger Button"), Is.False,
            "The attacker received controls for the defender's hidden Life decision.");
        UnityEngine.Object.DestroyImmediate(attackerBody.root.gameObject);

        h.SetNetworkViewer("south");
        var defenderBody = h.DrawActions("DrawBattleActions");
        Assert.That(CardMechanicsPlayModeHarness.HasDescendant(defenderBody, "Resolve Trigger Button"), Is.True);
        Assert.That(CardMechanicsPlayModeHarness.HasDescendant(defenderBody, "Pass Trigger Button"), Is.True);
        UnityEngine.Object.DestroyImmediate(defenderBody.root.gameObject);

        h.Dispatch(h.Command("useTrigger", "south"));
        var southArea = CardMechanicsPlayModeHarness.List(h.Player(state, "south"), "CharacterArea");
        Assert.That(southArea.Cast<object>().Any(x => x != null &&
            (string)CardMechanicsPlayModeHarness.Get(x, "InstanceId") ==
            (string)CardMechanicsPlayModeHarness.Get(revealed, "InstanceId")), Is.True,
            "The defender chose the real 'Play this card' Trigger, but it did not reach the field.");
    }

    private static void OncePerTurnOptionalSkipDoesNotConsumeButUseDoes(CardMechanicsPlayModeHarness h)
    {
        var state = OnceBoard(h, "once-skip");
        var lucy = FindCharacter(h, state, "south", "OP07-112");
        DeclareAttack(h, state, lucy, "south");
        var first = h.Pending(state, "south");
        Assert.That(first, Is.Not.Null, "Lucy's first optional once-per-turn attack did not prompt.");
        h.Dispatch(h.Command("passEffect", "south", effectId: (string)CardMechanicsPlayModeHarness.Get(first, "EffectId")));
        FinishBattle(h, state, "north");
        CardMechanicsPlayModeHarness.Set(lucy, "Rested", false);
        DeclareAttack(h, state, lucy, "south");
        Assert.That(h.Pending(state, "south"), Is.Not.Null,
            "Declining a 'you may' effect incorrectly consumed its once-per-turn use.");

        state = OnceBoard(h, "once-use");
        lucy = FindCharacter(h, state, "south", "OP07-112");
        DeclareAttack(h, state, lucy, "south");
        int lifeBefore = CardMechanicsPlayModeHarness.List(h.Player(state, "south"), "Life").Count;
        ResolvePendingWithLegalTargets(h, state, "south");
        Assert.That(CardMechanicsPlayModeHarness.List(h.Player(state, "south"), "Life").Count,
            Is.EqualTo(lifeBefore - 1), "Using Lucy did not pay the Life-card cost.");
        FinishBattle(h, state, "north");
        CardMechanicsPlayModeHarness.Set(lucy, "Rested", false);
        DeclareAttack(h, state, lucy, "south");
        Assert.That(h.Pending(state, "south"), Is.Null,
            "A used once-per-turn ability prompted again during the same turn.");
    }

    private static object OnceBoard(CardMechanicsPlayModeHarness h, string seed)
    {
        var state = h.NewActiveState("south", seed);
        h.Character(state, "south", "OP07-112");
        h.Character(state, "north", "OP15-040");
        for (int i = 0; i < 5; i++)
        {
            h.Life(state, "south", "ST01-005");
            h.Life(state, "north", "ST01-005");
        }
        for (int i = 0; i < 10; i++)
        {
            h.Don(state, "south", rested: false);
            h.Don(state, "north", rested: false);
        }
        h.AttachState(state);
        return state;
    }

    private static object FindCharacter(CardMechanicsPlayModeHarness h, object state, string seat, string cardId)
    {
        return CardMechanicsPlayModeHarness.List(h.Player(state, seat), "CharacterArea")
            .Cast<object>().First(x => x != null && (string)CardMechanicsPlayModeHarness.Get(x, "CardId") == cardId);
    }

    private static void DeclareAttack(CardMechanicsPlayModeHarness h, object state, object attacker, string seat)
    {
        string defender = seat == "south" ? "north" : "south";
        CardMechanicsPlayModeHarness.Set(state, "ActiveSeat", seat);
        CardMechanicsPlayModeHarness.Set(state, "Phase", "main");
        h.Dispatch(h.Command("declareAttack", seat,
            attacker: (string)CardMechanicsPlayModeHarness.Get(attacker, "InstanceId"),
            target: (string)CardMechanicsPlayModeHarness.Get(
                CardMechanicsPlayModeHarness.Get(h.Player(state, defender), "Leader"), "InstanceId")));
    }

    private static void FinishBattle(CardMechanicsPlayModeHarness h, object state, string defender)
    {
        for (int guard = 0; guard < 8 && CardMechanicsPlayModeHarness.Get(state, "Battle") != null; guard++)
        {
            var battle = CardMechanicsPlayModeHarness.Get(state, "Battle");
            string step = (string)CardMechanicsPlayModeHarness.Get(battle, "Step");
            string command = step == "block" ? "passBlock"
                : step == "counter" ? "passCounter"
                : step == "trigger" ? "passTrigger" : "resolveAttack";
            h.Dispatch(h.Command(command, defender));
        }
        Assert.That(CardMechanicsPlayModeHarness.Get(state, "Battle"), Is.Null, "Battle fixture did not finish.");
    }

    private static void ResolvePendingWithLegalTargets(CardMechanicsPlayModeHarness h, object state, string seat)
    {
        for (int guard = 0; guard < 8; guard++)
        {
            var effect = h.Pending(state, seat);
            if (effect == null) return;
            object target = AllCards(h, state).FirstOrDefault(card => h.IsValidTarget(state, effect, card));
            h.Dispatch(h.Command("resolveEffect", seat,
                target: target == null ? null : (string)CardMechanicsPlayModeHarness.Get(target, "InstanceId"),
                effectId: (string)CardMechanicsPlayModeHarness.Get(effect, "EffectId")));
        }
        Assert.Fail("Pending-effect fixture exceeded its resolution guard.");
    }

    private static IEnumerable<object> AllCards(CardMechanicsPlayModeHarness h, object state)
    {
        foreach (var seat in new[] { "south", "north" })
        {
            var p = h.Player(state, seat);
            foreach (var field in new[] { "Life", "Hand", "CharacterArea", "Trash" })
                foreach (var card in CardMechanicsPlayModeHarness.List(p, field))
                    if (card != null) yield return card;
            var leader = CardMechanicsPlayModeHarness.Get(p, "Leader");
            if (leader != null) yield return leader;
        }
    }

    private static void HiddenDeckLookRejectsTheOtherSeatAndConservesEveryCard(CardMechanicsPlayModeHarness h)
    {
        var state = h.NewActiveState("south", "deck-look-hidden");
        var nami = h.Character(state, "south", "OP01-016");
        var deck = CardMechanicsPlayModeHarness.List(h.Player(state, "south"), "Deck");
        deck.Clear();
        foreach (var id in new[] { "ST01-003", "ST02-005", "ST01-003", "ST02-005", "ST01-005" })
            deck.Add(h.Card(id, "south", "deck"));
        var allIds = deck.Cast<object>().Select(x => (string)CardMechanicsPlayModeHarness.Get(x, "InstanceId")).ToHashSet();
        h.QueueCardText(state, "south", nami, "onPlay", h.StripTimingTags(h.CardText("OP01-016", "Effect")));
        var look = CardMechanicsPlayModeHarness.Get(state, "DeckLook");
        Assert.That(look, Is.Not.Null, "Nami did not open the production hidden-deck selection state.");
        var looked = CardMechanicsPlayModeHarness.List(look, "Cards");
        var eligible = looked.Cast<object>().Single(x => (string)CardMechanicsPlayModeHarness.Get(x, "CardId") == "ST01-005");

        h.SetNetworkViewer("north");
        CardMechanicsPlayModeHarness.Invoke(h.Manager, "SelectDeckLookCard",
            (string)CardMechanicsPlayModeHarness.Get(eligible, "InstanceId"));
        Assert.That(CardMechanicsPlayModeHarness.List(h.Player(state, "south"), "Hand").Count, Is.EqualTo(0),
            "The non-owning network seat was able to pick from the opponent's hidden deck look.");

        h.SetNetworkViewer("south");
        CardMechanicsPlayModeHarness.Invoke(h.Manager, "SelectDeckLookCard",
            (string)CardMechanicsPlayModeHarness.Get(eligible, "InstanceId"));
        Assert.That(CardMechanicsPlayModeHarness.List(h.Player(state, "south"), "Hand").Contains(eligible), Is.True,
            "The owning seat's card-click route did not add Nami's legal choice to hand.");

        look = CardMechanicsPlayModeHarness.Get(state, "DeckLook");
        Assert.That(look, Is.Not.Null);
        var remainder = CardMechanicsPlayModeHarness.List(look, "Cards").Cast<object>()
            .Select(x => (string)CardMechanicsPlayModeHarness.Get(x, "InstanceId")).ToList();
        h.Dispatch(h.Command("deckLookConfirmOrder", "south", orderedIds: remainder));
        Assert.That(CardMechanicsPlayModeHarness.Get(state, "DeckLook"), Is.Null,
            "The hidden-card rearrange did not close after the player's confirm command.");
        var finalIds = CardMechanicsPlayModeHarness.List(h.Player(state, "south"), "Deck").Cast<object>()
            .Concat(CardMechanicsPlayModeHarness.List(h.Player(state, "south"), "Hand").Cast<object>())
            .Select(x => (string)CardMechanicsPlayModeHarness.Get(x, "InstanceId")).ToHashSet();
        Assert.That(finalIds.SetEquals(allIds), Is.True,
            "Nami's hidden-zone selection lost or duplicated a card across deck and hand.");
    }

    private static void PublicRevealFlipsOnlyExactOpponentHandCardsAndOwnsTheActionPanel(
        CardMechanicsPlayModeHarness h)
    {
        var state = h.NewActiveState("south", "public-reveal-hand");
        var source = h.Character(state, "south", "OP16-003");
        var first = h.Hand(state, "south", "EB01-041");
        var second = h.Hand(state, "south", "EB02-042");
        h.Hand(state, "south", "ST01-007");
        h.QueueCardText(state, "south", source, "onPlay",
            "You may reveal 2 Character cards with 8000 power from your hand: Give up to 1 of your opponent's Characters -6000 power during this turn.");
        var effect = h.Pending(state, "south");
        Assert.That(effect, Is.Not.Null);

        // Exercise the ordinary hand click route used by Newgate's proof cost. The engine records
        // only the selected instance identities as publicly shown.
        h.SetNetworkViewer("south");
        CardMechanicsPlayModeHarness.Invoke(h.Manager, "OnCardClick", first, "south-hand");
        CardMechanicsPlayModeHarness.Invoke(h.Manager, "OnCardClick", second, "south-hand");
        var reveal = CardMechanicsPlayModeHarness.Get(state, "ActiveReveal");
        Assert.That(reveal, Is.Not.Null, "The opponent-hand reveal never reached the shared reveal state.");
        var refs = CardMechanicsPlayModeHarness.List(reveal, "Cards").Cast<object>()
            .Select(x => (string)CardMechanicsPlayModeHarness.Get(x, "InstanceId")).ToHashSet();
        Assert.That(refs.SetEquals(new[]
        {
            (string)CardMechanicsPlayModeHarness.Get(first, "InstanceId"),
            (string)CardMechanicsPlayModeHarness.Get(second, "InstanceId"),
        }), Is.True, "The public reveal leaked a non-selected hand card or omitted a chosen card.");

        h.SetNetworkViewer("south");
        var waitingPanel = h.DrawActions("DrawPublicRevealActions");
        Assert.That(CardMechanicsPlayModeHarness.HasDescendant(waitingPanel, "Confirm Reveal Button"), Is.False,
            "The non-confirming client received the other player's confirmation control.");
        Assert.That(waitingPanel.GetComponentsInChildren<Image>(true)
            .Count(i => i.gameObject.name == "Revealed Art"), Is.EqualTo(2),
            "The right-side action panel did not show both exact reveal cards.");
        UnityEngine.Object.DestroyImmediate(waitingPanel.root.gameObject);

        h.SetNetworkViewer("north");
        var confirmingPanel = h.DrawActions("DrawPublicRevealActions");
        Assert.That(CardMechanicsPlayModeHarness.HasDescendant(confirmingPanel, "Confirm Reveal Button"), Is.True,
            "The confirming client did not receive the reveal acknowledgement button.");
        UnityEngine.Object.DestroyImmediate(confirmingPanel.root.gameObject);

        // The far-side hand must flip only those two cards. The third card remains a back.
        var canvasObject = new GameObject("Reveal Hand Visual Probe", typeof(RectTransform), typeof(Canvas));
        var handRoot = new GameObject("Reveal Hand Row", typeof(RectTransform)).GetComponent<RectTransform>();
        handRoot.SetParent(canvasObject.transform, false);
        handRoot.sizeDelta = new Vector2(900f, 220f);
        CardMechanicsPlayModeHarness.Invoke(h.Manager, "DrawFannedHandRow", handRoot,
            CardMechanicsPlayModeHarness.List(h.Player(state, "south"), "Hand"), "south-hand", true);
        Canvas.ForceUpdateCanvases();
        var back = (Sprite)CardMechanicsPlayModeHarness.Invoke(h.Manager, "GetBackSprite");
        int backs = 0, faces = 0;
        for (int i = 0; i < handRoot.childCount; i++)
        {
            // AddCard inserts a named card root between the fan holder and Card Face. Query the
            // rendered image by its stable leaf name so this assertion follows the actual Unity
            // hierarchy instead of assuming that intermediate card-name object is absent.
            var art = handRoot.GetChild(i).GetComponentsInChildren<Image>(true)
                .FirstOrDefault(image => image.gameObject.name == "Art");
            if (art == null) continue;
            if (art.sprite == back) backs++; else faces++;
        }
        Assert.That(faces, Is.EqualTo(2));
        Assert.That(backs, Is.EqualTo(1));
        UnityEngine.Object.DestroyImmediate(canvasObject);

        h.Dispatch(h.Command("confirmReveal", "north"));
        Assert.That(CardMechanicsPlayModeHarness.Get(state, "ActiveReveal"), Is.Null,
            "Confirming did not immediately restore the hand's hidden-information state.");
    }

    private static void ImuMixedBoardOrHandCostUsesTheHighlightedCardClickRoute(CardMechanicsPlayModeHarness h)
    {
        var state = h.NewActiveState("south", "imu-mixed-board-cost");
        var leader = h.SetLeader(state, "south", "OP13-079");
        var celestialDragon = h.Character(state, "south", "OP13-086");
        var wrongBoardType = h.Character(state, "south", "ST01-006");
        var anyHandCard = h.Hand(state, "south", "ST01-015");
        var deck = CardMechanicsPlayModeHarness.List(h.Player(state, "south"), "Deck");
        deck.Clear();
        deck.Add(h.Card("ST01-005", "south", "deck"));

        string fullText = h.CardText("OP13-079", "Effect");
        int activateIndex = fullText.IndexOf("[Activate: Main]", StringComparison.Ordinal);
        Assert.That(activateIndex, Is.GreaterThanOrEqualTo(0), "OP13-079 no longer has its Activate: Main text.");
        string clause = h.StripTimingTags(fullText.Substring(activateIndex));
        StringAssert.Contains("or 1 card from your hand", clause,
            "The Unity regression must follow the current mixed-zone CardData clause.");
        h.QueueCardText(state, "south", leader, "activateMain", clause);

        var pending = h.Pending(state, "south");
        Assert.That(pending, Is.Not.Null);
        Assert.That(CardMechanicsPlayModeHarness.Get(pending, "TargetZone").ToString(), Is.EqualTo("Any"));
        Assert.That(h.IsGreenTarget(celestialDragon), Is.True,
            "Imu's legal Celestial Dragons board payment did not receive the client valid-target affordance.");
        Assert.That(h.IsGreenTarget(anyHandCard), Is.True,
            "Imu's unfiltered hand payment did not receive the client valid-target affordance.");
        Assert.That(h.IsGreenTarget(wrongBoardType), Is.False,
            "Imu incorrectly highlighted a non-Celestial-Dragons board card.");
        Assert.That(h.IsGreenTarget(CardMechanicsPlayModeHarness.Get(h.Player(state, "north"), "Leader")), Is.False);

        h.SetNetworkViewer("north");
        CardMechanicsPlayModeHarness.Invoke(h.Manager, "OnCardClick", celestialDragon, "south");
        Assert.That(CardMechanicsPlayModeHarness.Get(celestialDragon, "Zone"), Is.EqualTo("character"),
            "The non-owning local network view paid Imu's south-seat cost.");
        Assert.That(h.Pending(state, "south"), Is.Not.Null);

        int handBeforeBoardPayment = CardMechanicsPlayModeHarness.List(h.Player(state, "south"), "Hand").Count;
        h.AttachState(state);
        CardMechanicsPlayModeHarness.Invoke(h.Manager, "OnCardClick", celestialDragon, "south");
        Assert.That(CardMechanicsPlayModeHarness.Get(celestialDragon, "Zone"), Is.EqualTo("trash"));
        Assert.That(CardMechanicsPlayModeHarness.List(h.Player(state, "south"), "Trash").Contains(celestialDragon), Is.True);
        Assert.That(CardMechanicsPlayModeHarness.List(h.Player(state, "south"), "Hand").Count,
            Is.EqualTo(handBeforeBoardPayment + 1), "Imu's board payment did not draw through GameManager.OnCardClick.");
        Assert.That(h.Pending(state, "south"), Is.Null);

        state = h.NewActiveState("south", "imu-mixed-hand-cost");
        leader = h.SetLeader(state, "south", "OP13-079");
        celestialDragon = h.Character(state, "south", "OP13-086");
        anyHandCard = h.Hand(state, "south", "ST01-015");
        deck = CardMechanicsPlayModeHarness.List(h.Player(state, "south"), "Deck");
        deck.Clear();
        deck.Add(h.Card("ST01-005", "south", "deck"));
        h.QueueCardText(state, "south", leader, "activateMain", clause);
        Assert.That(h.IsGreenTarget(anyHandCard), Is.True);
        int deckBeforeHandPayment = deck.Count;
        CardMechanicsPlayModeHarness.Invoke(h.Manager, "OnCardClick", anyHandCard, "south-hand");
        Assert.That(CardMechanicsPlayModeHarness.Get(anyHandCard, "Zone"), Is.EqualTo("trash"));
        Assert.That(CardMechanicsPlayModeHarness.List(h.Player(state, "south"), "Trash").Contains(anyHandCard), Is.True);
        Assert.That(CardMechanicsPlayModeHarness.List(h.Player(state, "south"), "CharacterArea").Contains(celestialDragon), Is.True,
            "Choosing Imu's hand alternative unexpectedly removed the legal board card.");
        Assert.That(deck.Count, Is.EqualTo(deckBeforeHandPayment - 1));
        Assert.That(h.Pending(state, "south"), Is.Null);
    }

    private static void NewgateLeaderCombatPromptBelongsToTheDefenderAndResolvesThroughClicks(
        CardMechanicsPlayModeHarness h)
    {
        var state = h.NewActiveState("south", "newgate-defender-routing");
        var northLeader = h.SetLeader(state, "north", "OP17-039"); // Rocks Pirates
        var source = h.Character(state, "north", "OP17-040");
        var payment = h.Hand(state, "north", "ST01-006");
        var southLeader = CardMechanicsPlayModeHarness.Get(h.Player(state, "south"), "Leader");

        h.Dispatch(h.Command("declareAttack", "south",
            attacker: (string)CardMechanicsPlayModeHarness.Get(southLeader, "InstanceId"),
            target: (string)CardMechanicsPlayModeHarness.Get(northLeader, "InstanceId")));
        var pending = h.Pending(state, "north");
        Assert.That(pending, Is.Not.Null, "OP17-040 did not prompt when its Rocks Pirates Leader was attacked.");
        Assert.That(CardMechanicsPlayModeHarness.Get(pending, "SourceInstanceId"),
            Is.EqualTo(CardMechanicsPlayModeHarness.Get(source, "InstanceId")));
        Assert.That(CardMechanicsPlayModeHarness.Get(pending, "Timing"), Is.EqualTo("onLeaderCombat"));

        h.SetNetworkViewer("south");
        var attackerBody = h.DrawActions("DrawPendingEffectActions");
        Assert.That(CardMechanicsPlayModeHarness.HasDescendant(attackerBody, "Skip Button"), Is.False,
            "The attacking local view received the defender's OP17-040 decision controls.");
        UnityEngine.Object.DestroyImmediate(attackerBody.root.gameObject);

        h.SetNetworkViewer("north");
        var defenderBody = h.DrawActions("DrawPendingEffectActions");
        const string PayButton = "Trash 1 card from your hand Button";
        Assert.That(CardMechanicsPlayModeHarness.HasDescendant(defenderBody, PayButton), Is.True,
            "The defending owner did not receive a readable OP17-040 payment action.");
        Assert.That(CardMechanicsPlayModeHarness.HasDescendant(defenderBody, "Skip Button"), Is.True);
        h.AttachState(state);
        CardMechanicsPlayModeHarness.ClickButton(defenderBody, PayButton);
        UnityEngine.Object.DestroyImmediate(defenderBody.root.gameObject);

        pending = h.Pending(state, "north");
        Assert.That(pending, Is.Not.Null, "Accepting OP17-040 did not open its hand-payment selection.");
        Assert.That(CardMechanicsPlayModeHarness.Get(pending, "TargetZone").ToString(), Is.EqualTo("Hand"));
        Assert.That(h.IsGreenTarget(payment), Is.True, "OP17-040's legal discard did not highlight.");

        h.SetNetworkViewer("south");
        CardMechanicsPlayModeHarness.Invoke(h.Manager, "OnCardClick", payment, "north-hand");
        Assert.That(CardMechanicsPlayModeHarness.Get(payment, "Zone"), Is.EqualTo("hand"),
            "The attacking local view paid the defender's OP17-040 hand cost.");
        h.AttachState(state);
        CardMechanicsPlayModeHarness.Invoke(h.Manager, "OnCardClick", payment, "north-hand");
        Assert.That(CardMechanicsPlayModeHarness.Get(payment, "Zone"), Is.EqualTo("trash"));

        pending = h.Pending(state, "north");
        Assert.That(pending, Is.Not.Null, "OP17-040 did not continue from payment to its Leader target.");
        Assert.That(h.IsGreenTarget(northLeader), Is.True, "The defending Rocks Pirates Leader did not highlight.");
        CardMechanicsPlayModeHarness.Invoke(h.Manager, "OnCardClick", northLeader, "north");

        int actualPower = (int)CardMechanicsPlayModeHarness.InvokeStatic(h.EngineType, "GetPower", state, northLeader);
        int printedPower = (int)CardMechanicsPlayModeHarness.Get(h.CardDefinition("OP17-039"), "Power");
        Assert.That(actualPower, Is.EqualTo(printedPower + 3000),
            "OP17-040's player-click path did not apply the during-battle +3000 buff.");
        var used = (IEnumerable)CardMechanicsPlayModeHarness.Get(h.Player(state, "north"), "AbilityUsedThisTurn");
        Assert.That(used.Cast<object>().Any(x => (string)x ==
            (string)CardMechanicsPlayModeHarness.Get(source, "InstanceId") + ":yourLeaderCombat"), Is.True,
            "Resolving OP17-040 did not consume its production once-per-turn key.");
    }

    private static void MaserSaberRoutesTheOpponentDecisionThenReturnsTheContinuationToItsOwner(
        CardMechanicsPlayModeHarness h)
    {
        var state = h.NewActiveState("north", "maser-saber-opponent-decision");
        var attacker = h.Character(state, "north", "EB03-002");
        var victim = h.Character(state, "north", "ST01-005");
        h.Hand(state, "north", "ST01-006");
        h.Hand(state, "north", "ST01-007");
        h.Hand(state, "north", "ST01-008");
        h.Life(state, "south", "OP17-117");

        var southLeader = CardMechanicsPlayModeHarness.Get(h.Player(state, "south"), "Leader");
        h.Dispatch(h.Command("declareAttack", "north",
            attacker: (string)CardMechanicsPlayModeHarness.Get(attacker, "InstanceId"),
            target: (string)CardMechanicsPlayModeHarness.Get(southLeader, "InstanceId")));
        var battle = CardMechanicsPlayModeHarness.Get(state, "Battle");
        if ((string)CardMechanicsPlayModeHarness.Get(battle, "Step") == "block") h.Dispatch(h.Command("passBlock", "south"));
        battle = CardMechanicsPlayModeHarness.Get(state, "Battle");
        if ((string)CardMechanicsPlayModeHarness.Get(battle, "Step") == "counter") h.Dispatch(h.Command("passCounter", "south"));
        battle = CardMechanicsPlayModeHarness.Get(state, "Battle");
        if ((string)CardMechanicsPlayModeHarness.Get(battle, "Step") == "damage") h.Dispatch(h.Command("resolveAttack", "south"));
        battle = CardMechanicsPlayModeHarness.Get(state, "Battle");
        Assert.That(CardMechanicsPlayModeHarness.Get(battle, "Step"), Is.EqualTo("trigger"));

        h.SetNetworkViewer("north");
        var attackerBody = h.DrawActions("DrawBattleActions");
        Assert.That(CardMechanicsPlayModeHarness.HasDescendant(attackerBody, "Resolve Trigger Button"), Is.False,
            "The attacker received controls for the defender's OP17-117 Life trigger.");
        UnityEngine.Object.DestroyImmediate(attackerBody.root.gameObject);

        h.SetNetworkViewer("south");
        var defenderBody = h.DrawActions("DrawBattleActions");
        Assert.That(CardMechanicsPlayModeHarness.HasDescendant(defenderBody, "Resolve Trigger Button"), Is.True);
        h.AttachState(state);
        CardMechanicsPlayModeHarness.ClickButton(defenderBody, "Resolve Trigger Button");
        UnityEngine.Object.DestroyImmediate(defenderBody.root.gameObject);

        var triggerBody = h.Pending(state, "south");
        Assert.That(triggerBody, Is.Not.Null, "Using OP17-117's Trigger did not queue its production text.");
        h.Dispatch(h.Command("resolveEffect", "south",
            effectId: (string)CardMechanicsPlayModeHarness.Get(triggerBody, "EffectId")));
        var opponentDecision = h.Pending(state, "north");
        Assert.That(opponentDecision, Is.Not.Null, "OP17-117 did not hand its discard decision to the opponent.");
        Assert.That(CardMechanicsPlayModeHarness.Get(opponentDecision, "DeclineSeat"), Is.EqualTo("south"));
        Assert.That((string)CardMechanicsPlayModeHarness.Get(opponentDecision, "DeclineContinuation"), Is.Not.Empty);

        h.SetNetworkViewer("south");
        var ownerWaitingBody = h.DrawActions("DrawPendingEffectActions");
        Assert.That(CardMechanicsPlayModeHarness.HasDescendant(ownerWaitingBody, "Skip Button"), Is.False,
            "OP17-117's owner received controls for the opponent's discard decision.");
        UnityEngine.Object.DestroyImmediate(ownerWaitingBody.root.gameObject);

        h.SetNetworkViewer("north");
        var opponentBody = h.DrawActions("DrawPendingEffectActions");
        Assert.That(CardMechanicsPlayModeHarness.HasDescendant(opponentBody, "Skip Button"), Is.True,
            "The opponent did not receive the decline control for OP17-117's discard decision.");
        h.AttachState(state);
        CardMechanicsPlayModeHarness.ClickButton(opponentBody, "Skip Button");
        UnityEngine.Object.DestroyImmediate(opponentBody.root.gameObject);

        var koContinuation = h.Pending(state, "south");
        Assert.That(koContinuation, Is.Not.Null, "Declining OP17-117 did not return its K.O. continuation to the owner.");
        Assert.That(h.IsGreenTarget(victim), Is.True, "The owner's legal cost-6-or-less K.O. target did not highlight.");
        Assert.That(CardMechanicsPlayModeHarness.List(h.Player(state, "north"), "Hand").Count, Is.EqualTo(3),
            "Declining the optional three-card discard changed the opponent's hidden hand.");

        h.SetNetworkViewer("north");
        CardMechanicsPlayModeHarness.Invoke(h.Manager, "OnCardClick", victim, "north");
        Assert.That(CardMechanicsPlayModeHarness.Get(victim, "Zone"), Is.EqualTo("character"),
            "The opponent local view resolved OP17-117's owner-controlled K.O. continuation.");
        h.AttachState(state);
        CardMechanicsPlayModeHarness.Invoke(h.Manager, "OnCardClick", victim, "north");
        Assert.That(CardMechanicsPlayModeHarness.Get(victim, "Zone"), Is.EqualTo("trash"),
            "The OP17-117 owner clicked a legal target but the K.O. did not resolve.");
        Assert.That(h.Pending(state), Is.Null);
    }
}
