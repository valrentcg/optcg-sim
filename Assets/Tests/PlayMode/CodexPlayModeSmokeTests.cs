using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public class CodexPlayModeSmokeTests
{
    [Test]
    public void NetworkCommandRoundTripPreservesCardDecisionFields()
    {
        // This test assembly deliberately has no compile-time reference to the predefined
        // Assembly-CSharp assembly. Cross the same reflection boundary as the smoke test so the
        // regression verifies the actual production DTO without changing Unity's assembly graph.
        var commandType = Type.GetType("OnePieceTcg.Engine.GameCommand, Assembly-CSharp");
        var serializableType = Type.GetType("SerializableCommand, Assembly-CSharp");
        Assert.That(commandType, Is.Not.Null);
        Assert.That(serializableType, Is.Not.Null);

        var original = Activator.CreateInstance(commandType);
        SetField(commandType, original, "Type", "resolveEffect");
        SetField(commandType, original, "Seat", "south");
        SetField(commandType, original, "Target", "south-card-17");
        SetField(commandType, original, "EffectId", "effect-42");
        SetField(commandType, original, "Amount", 2);
        SetField(commandType, original, "SlotIndex", 3);
        SetField(commandType, original, "OrderedInstanceIds", new System.Collections.Generic.List<string> { "look-a", "look-b" });
        SetField(commandType, original, "DonInstanceIds", new System.Collections.Generic.List<string> { "don-a", "don-b" });

        var wire = serializableType.GetMethod("From").Invoke(null, new[] { original });
        var decoded = JsonUtility.FromJson(JsonUtility.ToJson(wire), serializableType);
        var roundTripped = serializableType.GetMethod("ToCommand").Invoke(decoded, null);

        Assert.That(GetField(commandType, roundTripped, "Type"), Is.EqualTo("resolveEffect"));
        Assert.That(GetField(commandType, roundTripped, "Seat"), Is.EqualTo("south"));
        Assert.That(GetField(commandType, roundTripped, "Target"), Is.EqualTo("south-card-17"));
        Assert.That(GetField(commandType, roundTripped, "EffectId"), Is.EqualTo("effect-42"));
        Assert.That(GetField(commandType, roundTripped, "Amount"), Is.EqualTo(2));
        Assert.That(GetField(commandType, roundTripped, "SlotIndex"), Is.EqualTo(3));
        Assert.That(GetField(commandType, roundTripped, "OrderedInstanceIds"), Is.EqualTo(new[] { "look-a", "look-b" }));
        Assert.That(GetField(commandType, roundTripped, "DonInstanceIds"), Is.EqualTo(new[] { "don-a", "don-b" }));
    }

    private static void SetField(Type type, object target, string name, object value) => type.GetField(name).SetValue(target, value);
    private static object GetField(Type type, object target, string name) => type.GetField(name).GetValue(target);

    [UnityTest]
    public IEnumerator SceneBootstrapLoadsLibraryAndReachesActiveMatch()
    {
        yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
        for (int i = 0; i < 10; i++) yield return null;

        Component manager = null;
        foreach (var candidate in UnityEngine.Object.FindObjectsByType<MonoBehaviour>())
        {
            if (candidate.GetType().Name == "GameManager") { manager = candidate; break; }
        }
        var gameManagerType = Type.GetType("GameManager, Assembly-CSharp");
        Assert.That(gameManagerType, Is.Not.Null, "GameManager runtime type was not loaded.");
        if (manager == null)
        {
            // SampleScene is menu-first by design; this is the same transition invoked by the
            // menu's ENTER/versus-self/network launch handlers.
            gameManagerType.GetMethod("EnsureBoard", BindingFlags.Static | BindingFlags.Public).Invoke(null, null);
            for (int i = 0; i < 5; i++) yield return null;
            foreach (var candidate in UnityEngine.Object.FindObjectsByType<MonoBehaviour>())
            {
                if (candidate.GetType().Name == "GameManager") { manager = candidate; break; }
            }
        }
        Assert.That(manager, Is.Not.Null, "Menu-to-board transition did not create GameManager.");

        var cardDataType = Type.GetType("OnePieceTcg.Engine.CardData, Assembly-CSharp");
        Assert.That(cardDataType, Is.Not.Null, "CardData runtime type was not loaded.");
        var libraryLoaded = (bool)cardDataType.GetProperty("OfficialLibraryLoaded", BindingFlags.Static | BindingFlags.Public).GetValue(null);
        Assert.That(libraryLoaded, Is.True, "Official card library did not load.");

        var managerType = manager.GetType();
        var stateField = managerType.GetField("state", BindingFlags.Instance | BindingFlags.NonPublic);
        var state = stateField?.GetValue(manager);
        Assert.That(state, Is.Not.Null, "GameManager did not create a live GameState.");
        var stateType = state.GetType();
        var status = (string)stateType.GetField("Status").GetValue(state);
        Assert.That(status, Is.EqualTo("coinflip").Or.EqualTo("setup").Or.EqualTo("active"));

        var dispatch = managerType.GetMethod("Dispatch", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(dispatch, Is.Not.Null, "GameManager.Dispatch was not found.");
        var commandType = Type.GetType("OnePieceTcg.Engine.GameCommand, Assembly-CSharp");
        Assert.That(commandType, Is.Not.Null, "GameCommand runtime type was not loaded.");
        if (status == "coinflip")
        {
            var winner = (string)stateType.GetField("CoinFlipWinner").GetValue(state);
            // Make south the active seat regardless of the coin-flip winner. This is a legal
            // choice by the winner and makes the ST01 Brook prompt fixture deterministic.
            dispatch.Invoke(manager, new object[] { Command(commandType, "chooseTurnOrder", winner, null, winner == "south") });
            dispatch.Invoke(manager, new object[] { Command(commandType, "mulliganDecision", "south", false, null) });
            dispatch.Invoke(manager, new object[] { Command(commandType, "mulliganDecision", "north", false, null) });
            yield return null;
        }

        state = stateField.GetValue(manager);
        Assert.That((string)stateType.GetField("Status").GetValue(state), Is.EqualTo("active"), "Live GameManager dispatch path did not reach active match.");
        var players = stateType.GetField("Players").GetValue(state) as System.Collections.IDictionary;
        Assert.That(players, Is.Not.Null);
        Assert.That(players.Count, Is.EqualTo(2));

        // Cross the live Unity/GameManager boundary with one real legal card action. Select the
        // first playable card from the active player's actual opening hand, then dispatch the same
        // command the hand-card UI emits. This keeps the fixture deterministic without hard-coding
        // a particular starter card id.
        var activeSeat = (string)stateType.GetField("ActiveSeat").GetValue(state);
        var activePlayer = players[activeSeat];
        var hand = (IList)activePlayer.GetType().GetField("Hand").GetValue(activePlayer);
        var playerType = activePlayer.GetType();
        var deck = (IList)playerType.GetField("Deck").GetValue(activePlayer);
        var brook = FindCard(deck, "ST01-011");
        bool brookAlreadyInHand = brook == null && FindCard(hand, "ST01-011") != null;
        if (brookAlreadyInHand) brook = FindCard(hand, "ST01-011");
        if (brook != null)
        {
            deck.Remove(brook);
            if (!brookAlreadyInHand)
            {
                brook.GetType().GetField("Zone").SetValue(brook, "hand");
                hand.Add(brook);
            }
            var costArea = (IList)playerType.GetField("CostArea").GetValue(activePlayer);
            var donType = Type.GetType("OnePieceTcg.Engine.DonInstance, Assembly-CSharp");
            for (int i = 0; i < 2; i++)
            {
                var don = Activator.CreateInstance(donType);
                donType.GetField("InstanceId").SetValue(don, "codex-don-" + i);
                donType.GetField("Rested").SetValue(don, false);
                costArea.Add(don);
            }
            playerType.GetField("DonDeck").SetValue(activePlayer, (int)playerType.GetField("DonDeck").GetValue(activePlayer) - 2);
        }
        Debug.Log($"[CodexPlayModeSmoke] activeSeat={activeSeat}; brookStaged={(brook != null)}; handCount={hand.Count}");
        var engineType = Type.GetType("OnePieceTcg.Engine.GameEngine, Assembly-CSharp");
        var playableMethod = engineType.GetMethod("IsPlayableNow", BindingFlags.Static | BindingFlags.Public);
        var getCardMethod = cardDataType.GetMethod("GetCard", BindingFlags.Static | BindingFlags.Public);
        object chosen = null;
        object chosenEffect = null;
        for (int i = 0; i < hand.Count; i++)
        {
            var candidate = hand[i];
            if ((bool)playableMethod.Invoke(null, new[] { state, activeSeat, candidate }))
            {
                chosen ??= candidate;
                var cardId = (string)candidate.GetType().GetField("CardId").GetValue(candidate);
                var definition = getCardMethod.Invoke(null, new object[] { cardId });
                var effect = definition.GetType().GetField("Effect")?.GetValue(definition) as string;
                var trigger = definition.GetType().GetField("Trigger")?.GetValue(definition) as string;
                if (!string.IsNullOrWhiteSpace(effect) || !string.IsNullOrWhiteSpace(trigger))
                {
                    chosenEffect = candidate;
                    break;
                }
            }
        }
        // If the deterministic fixture was found, play that exact effect-bearing card rather
        // than allowing a leader/keyword card earlier in the hand to win the preference scan.
        chosen = brook ?? chosenEffect ?? chosen;
        Assert.That(chosen, Is.Not.Null, "The active opening hand had no playable card for the live command smoke.");
        var chosenId = (string)chosen.GetType().GetField("InstanceId").GetValue(chosen);
        var chosenCardId = (string)chosen.GetType().GetField("CardId").GetValue(chosen);
        int historyBefore = ((IList)stateType.GetField("CommandHistory").GetValue(state)).Count;
        dispatch.Invoke(manager, new object[] { Command(commandType, "playCard", activeSeat, null, null, chosenId) });
        state = stateField.GetValue(manager);
        int historyAfter = ((IList)stateType.GetField("CommandHistory").GetValue(state)).Count;
        Assert.That(historyAfter, Is.GreaterThan(historyBefore), "Live playCard dispatch was not recorded.");
        var resultingZone = (string)chosen.GetType().GetField("Zone").GetValue(chosen);
        Assert.That(resultingZone, Is.Not.EqualTo("hand"), "Live playCard dispatch was recorded but the card remained in hand.");
        var pending = (IList)stateType.GetField("PendingEffects").GetValue(state);
        var deckLook = stateType.GetField("DeckLook").GetValue(state);
        Debug.Log($"[CodexPlayModeSmoke] live play passed: {chosenCardId}; pendingEffects={pending?.Count ?? 0}; deckLook={(deckLook != null)}");
        if (brook != null)
        {
            Assert.That(pending?.Count ?? 0, Is.GreaterThan(0), "Brook's On Play interaction did not create a live target state.");
            var effectId = (string)pending[0].GetType().GetField("EffectId").GetValue(pending[0]);
            var leader = playerType.GetField("Leader").GetValue(activePlayer);
            var leaderId = (string)leader.GetType().GetField("InstanceId").GetValue(leader);
            var greenTargetMethod = managerType.GetMethod("IsGreenTargetNow", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(greenTargetMethod, Is.Not.Null, "GameManager target-glow predicate was not found.");
            Assert.That((bool)greenTargetMethod.Invoke(manager, new[] { leader }), Is.True,
                "Brook's legal Leader target was not classified as a green UI target while its effect was pending.");
            var targetRectsField = managerType.GetField("cardTargetRects", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(targetRectsField, Is.Not.Null, "GameManager rendered target registry was not found.");
            var targetRects = targetRectsField.GetValue(manager) as System.Collections.IDictionary;
            Assert.That(targetRects, Is.Not.Null, "GameManager rendered target registry was not initialized.");
            var leaderRect = targetRects[leaderId] as RectTransform;
            Assert.That(leaderRect, Is.Not.Null, "The legal Leader target was not rendered into the card target registry.");
            var glow = leaderRect.Find("Card Face/Usable Glow Root");
            Assert.That(glow, Is.Not.Null, "The legal Leader target did not receive the production usable-glow hierarchy.");
            Assert.That(glow.gameObject.activeInHierarchy, Is.True, "The legal Leader target's usable glow was inactive.");
            dispatch.Invoke(manager, new object[] { Command(commandType, "resolveEffect", activeSeat, null, null, null, leaderId, effectId) });
            state = stateField.GetValue(manager);
            var pendingAfter = (IList)stateType.GetField("PendingEffects").GetValue(state);
            var attached = (IList)leader.GetType().GetField("AttachedDonIds").GetValue(leader);
            Assert.That(pendingAfter.Count, Is.EqualTo(0), "Brook's resolved On Play prompt remained pending.");
            Assert.That(attached.Count, Is.GreaterThanOrEqualTo(2), "Brook did not attach both available rested DON!! cards.");
            Debug.Log($"[CodexPlayModeSmoke] Brook prompt resolved: pendingEffects={pendingAfter.Count}; leaderAttachedDon={attached.Count}");
        }
    }

    private static object FindCard(IList cards, string cardId)
    {
        for (int i = 0; i < cards.Count; i++)
        {
            var card = cards[i];
            if ((string)card.GetType().GetField("CardId").GetValue(card) == cardId) return card;
        }
        return null;
    }

    private static object Command(System.Type commandType, string type, string seat, bool? mulligan, bool? goingFirst, string instanceId = null, string target = null, string effectId = null)
    {
        var command = System.Activator.CreateInstance(commandType);
        commandType.GetField("Type").SetValue(command, type);
        commandType.GetField("Seat").SetValue(command, seat);
        if (mulligan.HasValue) commandType.GetField("Mulligan").SetValue(command, mulligan);
        if (goingFirst.HasValue) commandType.GetField("GoingFirst").SetValue(command, goingFirst);
        if (!string.IsNullOrEmpty(instanceId)) commandType.GetField("InstanceId").SetValue(command, instanceId);
        if (!string.IsNullOrEmpty(target)) commandType.GetField("Target").SetValue(command, target);
        if (!string.IsNullOrEmpty(effectId)) commandType.GetField("EffectId").SetValue(command, effectId);
        return command;
    }
}
