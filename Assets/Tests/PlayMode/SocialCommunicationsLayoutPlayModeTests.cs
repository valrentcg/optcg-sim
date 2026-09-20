using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

public sealed class SocialCommunicationsLayoutPlayModeTests
{
    [UnityTest]
    public IEnumerator MainMenuUsesLeftRailSocialNavigationAndProfileHeaderStaysInsideDrawer()
    {
        var socialType = Type.GetType("SocialOverlayController, Assembly-CSharp");
        Assert.That(socialType, Is.Not.Null, "The production Social overlay was not compiled.");
        socialType.GetMethod("EnsureCreated", BindingFlags.Static | BindingFlags.Public).Invoke(null, null);
        yield return null;

        var instance = socialType.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public)?.GetValue(null);
        Assert.That(instance, Is.Not.Null);
        var root = (RectTransform)socialType.GetField("_root", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance);
        var contextField = socialType.GetField("_context", BindingFlags.Instance | BindingFlags.NonPublic);
        var drawerOpenField = socialType.GetField("_drawerOpen", BindingFlags.Instance | BindingFlags.NonPublic);
        var tabField = socialType.GetField("_tab", BindingFlags.Instance | BindingFlags.NonPublic);
        var buildDock = socialType.GetMethod("BuildDockButton", BindingFlags.Instance | BindingFlags.NonPublic);
        var buildDrawer = socialType.GetMethod("BuildDrawer", BindingFlags.Instance | BindingFlags.NonPublic);
        var render = socialType.GetMethod("Render", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(root, Is.Not.Null);
        Assert.That(buildDock, Is.Not.Null);
        Assert.That(buildDrawer, Is.Not.Null);
        Assert.That(render, Is.Not.Null);

        object mainContext = Enum.Parse(contextField.FieldType, "MainMenu");
        object matchContext = Enum.Parse(contextField.FieldType, "Match");

        Clear(root);
        contextField.SetValue(instance, mainContext);
        drawerOpenField.SetValue(instance, false);
        render.Invoke(instance, null);
        yield return null;
        Assert.That(root.Find("Social Dock"), Is.Null,
            "Main-menu Social must launch from the left navigation, not a floating upper-right button.");

        var menuType = Type.GetType("MainMenuManager, Assembly-CSharp");
        Assert.That(menuType, Is.Not.Null);
        menuType.GetMethod("EnsureMenu", BindingFlags.Static | BindingFlags.Public).Invoke(null, null);
        yield return null;
        var menu = UnityEngine.Object.FindObjectsByType<MonoBehaviour>()
            .FirstOrDefault(candidate => candidate.GetType() == menuType);
        Assert.That(menu, Is.Not.Null);
        var menuRoot = (RectTransform)menuType.GetField("menuRoot", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(menu);
        var friendsRow = menuRoot.GetComponentsInChildren<RectTransform>(true)
            .FirstOrDefault(rect => rect.name == "Friends Row");
        var socialRow = menuRoot.GetComponentsInChildren<RectTransform>(true)
            .FirstOrDefault(rect => rect.name == "Social Row");
        Assert.That(friendsRow, Is.Not.Null);
        Assert.That(socialRow, Is.Not.Null);
        Assert.That(socialRow.GetSiblingIndex(), Is.EqualTo(friendsRow.GetSiblingIndex() + 1),
            "Social should sit directly beneath Friends in the left rail.");
        socialRow.GetComponent<Button>().onClick.Invoke();
        yield return null;
        Assert.That((bool)socialType.GetProperty("DrawerOpen", BindingFlags.Static | BindingFlags.Public).GetValue(null), Is.True);
        Assert.That(root.Find("Social Drawer"), Is.Not.Null);
        Assert.That(root.Find("Social Dock"), Is.Null);
        socialRow = menuRoot.GetComponentsInChildren<RectTransform>(true)
            .FirstOrDefault(rect => rect.name == "Social Row");
        Assert.That(socialRow.Find("Active Bar"), Is.Not.Null,
            "The left-rail Social destination should show the same active treatment as other pages.");

        Clear(root);
        contextField.SetValue(instance, matchContext);
        drawerOpenField.SetValue(instance, false);
        buildDock.Invoke(instance, null);
        var dock = root.Find("Social Dock") as RectTransform;
        Assert.That(dock, Is.Not.Null);
        Assert.That(dock.sizeDelta.x, Is.GreaterThan(150f), "In-game communications regressed to a bubble.");
        Assert.That(dock.Find("Title").GetComponent<Text>().text, Is.EqualTo("CHAT & SOCIAL"));
        Assert.That(dock.GetComponents<Component>().Any(c => c.GetType().Name == "SocialOverlayDrag"), Is.False,
            "The in-game communications launcher must remain fixed.");

        Clear(root);
        contextField.SetValue(instance, mainContext);
        tabField.SetValue(instance, "messages");
        socialType.GetField("_selectedId", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(instance, "layout-fixture");
        var knownNames = (IDictionary)socialType.GetField("_knownNames", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(instance);
        knownNames["layout-fixture"] = "Layout Captain";
        buildDrawer.Invoke(instance, null);

        var drawer = root.Find("Social Drawer") as RectTransform;
        Assert.That(drawer, Is.Not.Null);
        Assert.That(drawer.anchorMin, Is.EqualTo(Vector2.one));
        Assert.That(drawer.pivot, Is.EqualTo(Vector2.one));
        Assert.That(drawer.GetComponentInChildren(Type.GetType("SocialOverlayDrag, Assembly-CSharp")), Is.Null,
            "The main-menu drawer must remain locked with its launcher.");

        var avatar = drawer.Find("Conversation/Conversation Head/Avatar") as RectTransform;
        Assert.That(avatar, Is.Not.Null);
        Assert.That(avatar.anchoredPosition.y, Is.EqualTo(-13f).Within(0.01f),
            "The profile image is no longer vertically aligned inside its header.");
        var conversationHead = avatar.parent as RectTransform;
        Assert.That(avatar.anchoredPosition.y, Is.LessThanOrEqualTo(0f));
        Assert.That(avatar.anchoredPosition.y - avatar.rect.height,
            Is.GreaterThanOrEqualTo(-conversationHead.rect.height - 0.01f),
            "The profile image extends through the bottom of its header.");

        Clear(root);
        yield return null;
    }

    [UnityTest]
    public IEnumerator MatchChatUsesTheFixedFourTabCommunicationsDrawer()
    {
        var managerType = Type.GetType("GameManager, Assembly-CSharp");
        Assert.That(managerType, Is.Not.Null);
        managerType.GetMethod("EnsureBoard", BindingFlags.Static | BindingFlags.Public).Invoke(null, null);
        yield return null;

        Component manager = null;
        foreach (var candidate in UnityEngine.Object.FindObjectsByType<MonoBehaviour>())
            if (candidate.GetType() == managerType) { manager = candidate; break; }
        Assert.That(manager, Is.Not.Null);

        var boardRoot = (RectTransform)managerType.GetField("boardRoot", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(manager);
        var chatOpen = managerType.GetField("chatOpen", BindingFlags.Instance | BindingFlags.NonPublic);
        chatOpen.SetValue(manager, true);
        managerType.GetMethod("DrawMatchChatPanel", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(manager, null);

        var panel = boardRoot.Find("Match Chat Panel") as RectTransform;
        Assert.That(panel, Is.Not.Null);
        Assert.That(panel.anchorMin, Is.EqualTo(Vector2.one));
        Assert.That(panel.pivot, Is.EqualTo(Vector2.one));
        var tabs = panel.Find("Match Communications Tabs");
        Assert.That(tabs, Is.Not.Null);
        Assert.That(tabs.Find("MATCH CHAT Tab"), Is.Not.Null);
        Assert.That(tabs.Find("MESSAGES Tab"), Is.Not.Null);
        Assert.That(tabs.Find("FRIENDS Tab"), Is.Not.Null);
        Assert.That(tabs.Find("REQUESTS Tab"), Is.Not.Null);
        Assert.That(boardRoot.Find("Chat Tab"), Is.Null, "The old floating in-game chat bubble returned.");

        UnityEngine.Object.DestroyImmediate(panel.gameObject);
        chatOpen.SetValue(manager, false);
        yield return null;
    }

    private static void Clear(RectTransform root)
    {
        for (int i = root.childCount - 1; i >= 0; i--)
            UnityEngine.Object.DestroyImmediate(root.GetChild(i).gameObject);
    }
}
