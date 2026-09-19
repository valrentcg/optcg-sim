using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Reflection-backed fixture for exercising the production Assembly-CSharp from the PlayMode test
/// assembly.  Keeping the test assembly independent is intentional: it makes the seam match the
/// serialized GameCommand boundary used by the client and catches missing fields/methods at runtime.
/// </summary>
internal sealed class CardMechanicsPlayModeHarness
{
    internal const BindingFlags InstanceAny = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    internal const BindingFlags StaticAny = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    internal readonly Type CardDataType = RequiredType("OnePieceTcg.Engine.CardData");
    internal readonly Type CardType = RequiredType("OnePieceTcg.Engine.CardInstance");
    internal readonly Type CommandType = RequiredType("OnePieceTcg.Engine.GameCommand");
    internal readonly Type DonType = RequiredType("OnePieceTcg.Engine.DonInstance");
    internal readonly Type EngineType = RequiredType("OnePieceTcg.Engine.GameEngine");
    internal readonly Type MatchConfigType = RequiredType("OnePieceTcg.Engine.MatchConfig");
    internal readonly Type BattleType = RequiredType("OnePieceTcg.Engine.BattleState");

    internal readonly Component Manager;
    internal readonly Type ManagerType;

    private int serial;

    internal CardMechanicsPlayModeHarness(Component manager)
    {
        Manager = manager ?? throw new ArgumentNullException(nameof(manager));
        ManagerType = manager.GetType();
    }

    internal object NewActiveState(string activeSeat = "south", string seed = "unity-card-e2e")
    {
        var config = Activator.CreateInstance(MatchConfigType);
        Set(config, "SouthDeck", "st01");
        Set(config, "NorthDeck", "st01");
        Set(config, "Seed", seed + "-" + (++serial));
        var state = InvokeStatic(EngineType, "CreateMatch", config);
        Set(state, "Status", "active");
        Set(state, "Phase", "main");
        Set(state, "ActiveSeat", activeSeat);
        Set(state, "TurnNumber", 8);
        Set(state, "Battle", null);
        Set(state, "ActiveChoice", null);
        Set(state, "DeckLook", null);
        List(state, "PendingEffects").Clear();

        foreach (var seat in new[] { "south", "north" })
        {
            var player = Player(state, seat);
            List(player, "Hand").Clear();
            List(player, "Life").Clear();
            List(player, "Trash").Clear();
            List(player, "CostArea").Clear();
            var area = List(player, "CharacterArea");
            for (int i = 0; i < area.Count; i++) area[i] = null;
            Set(player, "Stage", null);
            Set(player, "DonDeck", 0);
            Set(player, "TurnsStarted", 4);
            var usedAbilities = Get(player, "AbilityUsedThisTurn");
            Assert.That(usedAbilities, Is.InstanceOf<IEnumerable>(),
                "AbilityUsedThisTurn is no longer an enumerable production collection.");
            InvokeCollection(usedAbilities, "Clear");
        }
        AttachState(state);
        return state;
    }

    internal void AttachState(object state)
    {
        Set(Manager, "state", state);
        Set(Manager, "isReplayMode", false);
        Set(Manager, "isPuzzle", false);
        Set(Manager, "isNetworked", false);
        Set(Manager, "aiSeat", null);
    }

    internal object Player(object state, string seat)
    {
        var players = (IDictionary)Get(state, "Players");
        Assert.That(players.Contains(seat), Is.True, "Missing production player seat " + seat);
        return players[seat];
    }

    internal object Card(string cardId, string owner, string zone, bool rested = false)
    {
        Assert.That(CardDefinition(cardId), Is.Not.Null, "CardData is missing representative card " + cardId);
        var card = Activator.CreateInstance(CardType);
        Set(card, "InstanceId", owner + "-" + cardId + "-unity-e2e-" + (++serial));
        Set(card, "CardId", cardId);
        Set(card, "Owner", owner);
        Set(card, "Zone", zone);
        Set(card, "Rested", rested);
        Set(card, "PlayedOnTurn", 0);
        return card;
    }

    internal object Character(object state, string seat, string cardId, bool rested = false)
    {
        var card = Card(cardId, seat, "character", rested);
        var area = List(Player(state, seat), "CharacterArea");
        for (int i = 0; i < area.Count; i++)
        {
            if (area[i] != null) continue;
            area[i] = card;
            return card;
        }
        Assert.Fail("No open Character slot for " + cardId);
        return null;
    }

    internal object Life(object state, string seat, string cardId, bool faceUp = false)
    {
        var card = Card(cardId, seat, "life");
        Set(card, "FaceUp", faceUp);
        List(Player(state, seat), "Life").Add(card); // Life top is the final element.
        return card;
    }

    internal object Hand(object state, string seat, string cardId)
    {
        var card = Card(cardId, seat, "hand");
        List(Player(state, seat), "Hand").Add(card);
        return card;
    }

    internal object SetLeader(object state, string seat, string cardId)
    {
        Assert.That(CardDefinition(cardId), Is.Not.Null, "CardData is missing representative leader " + cardId);
        var leader = Get(Player(state, seat), "Leader");
        Set(leader, "CardId", cardId);
        return leader;
    }

    internal object Don(object state, string seat, bool rested)
    {
        var don = Activator.CreateInstance(DonType);
        Set(don, "InstanceId", seat + "-don-unity-e2e-" + (++serial));
        Set(don, "Rested", rested);
        List(Player(state, seat), "CostArea").Add(don);
        return don;
    }

    internal object CardDefinition(string cardId) => InvokeStatic(CardDataType, "GetCard", cardId);

    internal string CardText(string cardId, string field)
    {
        var definition = CardDefinition(cardId);
        Assert.That(definition, Is.Not.Null, "CardData.GetCard returned null for " + cardId);
        return (string)Get(definition, field) ?? "";
    }

    internal string StripTimingTags(string text) =>
        (string)InvokeStatic(EngineType, "StripLeadingTimingTags", text);

    internal void QueueCardText(object state, string seat, object source, string timing, string text)
    {
        InvokeStatic(EngineType, "QueueClauseForTest", state, seat, source, timing, text);
        AttachState(state);
    }

    internal object Command(string type, string seat = null, string instanceId = null,
        string target = null, string effectId = null, string attacker = null,
        string blocker = null, IList<string> orderedIds = null)
    {
        var command = Activator.CreateInstance(CommandType);
        Set(command, "Type", type);
        Set(command, "Seat", seat);
        if (instanceId != null) Set(command, "InstanceId", instanceId);
        if (target != null) Set(command, "Target", target);
        if (effectId != null) Set(command, "EffectId", effectId);
        if (attacker != null) Set(command, "Attacker", attacker);
        if (blocker != null) Set(command, "Blocker", blocker);
        if (orderedIds != null)
        {
            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(typeof(string)));
            foreach (var id in orderedIds) list.Add(id);
            Set(command, "OrderedInstanceIds", list);
        }
        return command;
    }

    /// <summary>Invoke the exact private GameManager.Dispatch used by button and card-click handlers.</summary>
    internal void Dispatch(object command)
    {
        Set(Manager, "isNetworked", false); // hot-seat: both seats are locally actionable in one process
        Invoke(Manager, "Dispatch", command);
    }

    internal object Pending(object state, string seat = null)
    {
        foreach (var effect in List(state, "PendingEffects"))
            if (effect != null && (seat == null || (string)Get(effect, "Seat") == seat)) return effect;
        return null;
    }

    internal bool IsValidTarget(object state, object effect, object card) =>
        (bool)InvokeStatic(EngineType, "IsValidEffectTarget", state, effect, card);

    internal bool IsGreenTarget(object card) => (bool)Invoke(Manager, "IsGreenTargetNow", card);

    internal void SetNetworkViewer(string localSeat)
    {
        Set(Manager, "isNetworked", true);
        Set(Manager, "localSeat", localSeat);
        Set(Manager, "aiSeat", null);
    }

    internal RectTransform DrawActions(string productionMethod)
    {
        var canvasGo = new GameObject("Card E2E Scratch Canvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var body = new GameObject("Card E2E Scratch Body", typeof(RectTransform), typeof(VerticalLayoutGroup))
            .GetComponent<RectTransform>();
        body.SetParent(canvasGo.transform, false);
        body.sizeDelta = new Vector2(420f, 700f);
        Invoke(Manager, productionMethod, body);
        Canvas.ForceUpdateCanvases();
        return body;
    }

    /// <summary>
    /// Remove visual-only transitions created by the production Render diff before another
    /// PlayMode test replaces the scene. EventBurn intentionally invokes its completion callback
    /// from OnDestroy; during a scene unload that callback can otherwise try to parent a trash
    /// reform under a hierarchy Unity is already destroying and turn a clean mechanics result into
    /// an unrelated teardown error.
    /// </summary>
    internal void CancelTransientAnimations()
    {
        (Manager as MonoBehaviour)?.StopAllCoroutines();
        foreach (var behaviour in UnityEngine.Object.FindObjectsByType<MonoBehaviour>())
        {
            if (behaviour == null) continue;
            if (behaviour.GetType().Name == "EventBurn")
            {
                Set(behaviour, "onBurnComplete", null);
                UnityEngine.Object.DestroyImmediate(behaviour.gameObject);
            }
            else if (behaviour.GetType().Name == "CardReform")
            {
                Set(behaviour, "onFinished", null);
                UnityEngine.Object.DestroyImmediate(behaviour.gameObject);
            }
        }
        Set(Manager, "reformRun", null);
    }

    internal static bool HasDescendant(Transform root, string exactName)
    {
        return FindDescendant(root, exactName) != null;
    }

    internal static Transform FindDescendant(Transform root, string exactName)
    {
        if (root.name == exactName) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var found = FindDescendant(root.GetChild(i), exactName);
            if (found != null) return found;
        }
        return null;
    }

    internal static void ClickButton(Transform root, string exactName)
    {
        var target = FindDescendant(root, exactName);
        Assert.That(target, Is.Not.Null, "Could not find player-facing button '" + exactName + "'.");
        var button = target.GetComponent<Button>();
        Assert.That(button, Is.Not.Null, exactName + " is no longer backed by a Unity Button.");
        Assert.That(button.interactable, Is.True, exactName + " is visible but disabled.");
        button.onClick.Invoke();
    }

    internal static IList List(object target, string field) => (IList)Get(target, field);

    internal static object Get(object target, string field)
    {
        Assert.That(target, Is.Not.Null, "Cannot read '" + field + "' from null.");
        var member = target.GetType().GetField(field, InstanceAny);
        Assert.That(member, Is.Not.Null, target.GetType().FullName + "." + field + " was not found.");
        return member.GetValue(target);
    }

    internal static void Set(object target, string field, object value)
    {
        Assert.That(target, Is.Not.Null, "Cannot write '" + field + "' on null.");
        var member = target.GetType().GetField(field, InstanceAny);
        Assert.That(member, Is.Not.Null, target.GetType().FullName + "." + field + " was not found.");
        member.SetValue(target, value);
    }

    internal static object Invoke(object target, string method, params object[] args)
    {
        var selected = FindMethod(target.GetType(), method, InstanceAny, args);
        try { return selected.Invoke(target, args); }
        catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
    }

    internal static object InvokeStatic(Type type, string method, params object[] args)
    {
        var selected = FindMethod(type, method, StaticAny, args);
        try { return selected.Invoke(null, args); }
        catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
    }

    private static MethodInfo FindMethod(Type type, string name, BindingFlags flags, object[] args)
    {
        var candidates = type.GetMethods(flags).Where(m => m.Name == name && m.GetParameters().Length == args.Length).ToList();
        foreach (var method in candidates)
        {
            var parameters = method.GetParameters();
            bool compatible = true;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (args[i] == null) continue;
                if (!parameters[i].ParameterType.IsInstanceOfType(args[i])) { compatible = false; break; }
            }
            if (compatible) return method;
        }
        Assert.Fail("No compatible " + type.FullName + "." + name + "(" + args.Length + " args) method was found.");
        return null;
    }

    private static void InvokeCollection(object collection, string method)
    {
        var info = collection.GetType().GetMethod(method, InstanceAny);
        Assert.That(info, Is.Not.Null);
        info.Invoke(collection, null);
    }

    private static Type RequiredType(string fullName)
    {
        var type = Type.GetType(fullName + ", Assembly-CSharp");
        Assert.That(type, Is.Not.Null, fullName + " did not load from Assembly-CSharp.");
        return type;
    }
}
