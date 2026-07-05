// One Piece TCG - My Profile stage (player-scouting design handoff, surface E/F).
//
// Partial of MainMenuManager: same destroy-and-rebuild render model, same
// PanelObject/TextObject/Stretch helpers, palette and fonts. This file adds the
// "My Profile" nav row's stage: a tabbed account hub (Overview / Deck History /
// Seasonal) plus the Privacy panel with live "Preview as" masking.
//
// Real data only — no roster mocks here:
//   StatsStore            lifetime + current-season buckets (games/wins/streaks,
//                         per-leader breakdowns, matchups[], months[])
//   MatchHistoryStore     newest-20 match summaries (Recent Matches list)
//   AccountManager        handle / guest state
//   ProfilePrivacyStore   decks/replays visibility + canSee rules
//
// PROVISIONAL STATS: the design shows Bounty / ELO / Tier, which have no
// server-side rating yet (ranked hasn't shipped). Until a real rating service
// exists they are DERIVED, display-only values computed from the lifetime
// bucket in ProfileDerived below — deterministic, monotonic-ish with wins, and
// clearly labeled PROVISIONAL in the UI. When ranked ships, replace
// ProfileDerived with the server values and delete the label.
//
// PvP-only caveat: today StatsStore records every finished match (solo modes
// included). The design wants PvP-only numbers; until the write path tags
// match type, the strip is labeled "ALL MODES" instead of lying with "PVP".

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public partial class MainMenuManager
{
    // ── My Profile sub-state (only meaningful while showingProfile) ──────────
    private bool showingProfile;
    private string profileTab = "overview";      // "overview" | "decks" | "seasonal"
    private string profileSelDeck;               // selected leader id in Deck History
    private string profileSeasonMode = "casual"; // "casual" | "ranked"
    private bool profilePrivacyOpen;
    private string profileViewAs = "me";         // "me" | "friend" | "public" (session only)

    // Loaded data. profileLifetime == null means "not loaded yet" and kicks off
    // LoadProfileData, mirroring the matchHistory pattern.
    private StatsBucket profileLifetime;
    private StatsBucket profileSeason;
    private ProfilePrivacySettings profilePrivacy;
    private bool profileLoading;

    private static readonly Color ProfileAmber = new Color32(226, 190, 102, 255); // privacy/locked
    private static readonly Color ProfileWrLow = new Color32(230, 138, 79, 255);  // <45% win rate

    private static readonly (string name, Color color, int minElo)[] ProfileTiers =
    {
        ("Rookie",          new Color32(159, 171, 190, 255),    0),
        ("Supernova",       new Color32( 79, 208, 138, 255), 1100),
        ("Warlord",         new Color32( 79, 195, 224, 255), 1300),
        ("Yonko Commander", new Color32( 70, 140, 220, 255), 1500),
        ("Emperor",         new Color32(160, 110, 210, 255), 1700),
        ("Pirate King",     new Color32(226, 190, 102, 255), 1900),
    };

    // ══════════════════════════════════════════════════════════════════════════
    // Navigation + data load
    // ══════════════════════════════════════════════════════════════════════════

    private void OpenMyProfile()
    {
        showingAccountSettings = false;
        showingFriends = false;
        showingReplays = false;
        showingProfile = true;
        // Opening resets to Overview / no deck / Casual, per the handoff.
        profileTab = "overview";
        profileSelDeck = null;
        profileSeasonMode = "casual";
        profilePrivacyOpen = false;
        profileViewAs = "me";
        profileLifetime = null;   // force a fresh load — stats may have changed
        RenderMenu();
    }

    private async void LoadProfileData()
    {
        profileLoading = true;
        StatsBucket lifetime = null, season = null;
        ProfilePrivacySettings privacy = null;
        List<MatchSummary> cloud = null;
        try
        {
            lifetime = await StatsStore.LoadLifetimeAsync(forceRefresh: true);
            int sid = StatsStore.CurrentSeasonId;
            season = sid > 0 ? await StatsStore.LoadSeasonAsync(sid) : new StatsBucket();
            privacy = await ProfilePrivacyStore.LoadAsync();
            if (matchHistory == null)
                cloud = await MatchHistoryStore.LoadAsync();
        }
        catch (Exception ex) { Debug.LogWarning($"Profile load failed: {ex.Message}"); }
        if (this == null || menuRoot == null) return;

        profileLoading = false;
        profileLifetime = lifetime ?? new StatsBucket();
        profileSeason = season ?? new StatsBucket();
        profilePrivacy = privacy ?? new ProfilePrivacySettings();
        if (matchHistory == null) matchHistory = MergeMatchHistory(cloud);
        if (showingProfile) RenderMenu();
    }

    // Owner-side gating for this screen: what the current "Preview as" audience
    // would be allowed to see, per the handoff's canSeeOwn rule.
    private bool ProfileCanSeeDecks() =>
        ProfilePrivacyStore.CanSeeOwn(profilePrivacy?.Decks ?? ProfileVisibility.Public, profileViewAs);
    private bool ProfileCanSeeReplays() =>
        ProfilePrivacyStore.CanSeeOwn(profilePrivacy?.Replays ?? ProfileVisibility.Public, profileViewAs);

    // ══════════════════════════════════════════════════════════════════════════
    // Stage
    // ══════════════════════════════════════════════════════════════════════════

    private void BuildProfileStage(RectTransform stage)
    {
        EnsureMenuCardLibrary();
        EnsureMenuFaceData();

        if (profileLifetime == null)
        {
            if (!profileLoading) LoadProfileData();
            var loading = TextObject("Loading", stage, "Loading your log...",
                13, Muted, TextAnchor.UpperLeft, monoFont);
            Stretch(loading.rectTransform, new Vector2(0f, 1f), Vector2.one,
                new Vector2(4f, -60f), Vector2.zero);
            return;
        }

        BuildProfileHeader(stage);

        var body = PanelObject("Profile Body", stage, new Color(0, 0, 0, 0));
        Stretch(body, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0f, -108f));

        switch (profileTab)
        {
            case "decks":    BuildProfileDecksTab(body); break;
            case "seasonal": BuildProfileSeasonalTab(body); break;
            default:         BuildProfileOverviewTab(body); break;
        }

        if (profilePrivacyOpen) BuildPrivacyPanel(stage);
    }

    // ── Header: handle · log line · shared summary · PRIVACY · tabs ──────────

    private void BuildProfileHeader(RectTransform stage)
    {
        var b = profileLifetime;
        var header = PanelObject("PF Header", stage, new Color(0, 0, 0, 0));
        Stretch(header, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -64f), Vector2.zero);

        string handle = !string.IsNullOrEmpty(AccountManager.CurrentUsername)
            ? AccountManager.CurrentUsername
            : (AccountManager.IsGuest ? AccountManager.GuestDisplayName : DefaultPlayerName);

        // Clickable avatar next to the handle — THE entry point to the Profile
        // Icon Picker (per Nathan: lives here, not on the top-bar identity).
        var avatarBtnHolder = PanelObject("Avatar Btn", header, new Color(0, 0, 0, 0));
        avatarBtnHolder.anchorMin = avatarBtnHolder.anchorMax = new Vector2(0f, 0.5f);
        avatarBtnHolder.pivot = new Vector2(0f, 0.5f);
        avatarBtnHolder.sizeDelta = new Vector2(52f, 52f);
        avatarBtnHolder.anchoredPosition = new Vector2(2f, 0f);
        BuildCircleFaceIcon(avatarBtnHolder, EffectiveProfileIconId(), 52f, Vector2.zero);
        // Small accent "edit" affordance pinned to the circle's lower-right.
        var editDot = PanelObject("Edit Dot", avatarBtnHolder, Accent);
        editDot.anchorMin = editDot.anchorMax = new Vector2(1f, 0f);
        editDot.pivot = new Vector2(1f, 0f);
        editDot.sizeDelta = new Vector2(16f, 16f);
        editDot.anchoredPosition = new Vector2(0f, 0f);
        RoundCircle(editDot);
        editDot.GetComponent<Image>().raycastTarget = false;
        if (font != null && font.HasCharacter('\u270E'))
        {
            var editGlyph = TextObject("g", editDot, "\u270E", 10, BadgeInk, TextAnchor.MiddleCenter);
            Stretch(editGlyph.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        }
        else
        {
            // Hand-drawn classic pencil: a 45°-rotated shaft with a small tip.
            var shaft = PanelObject("Pencil Shaft", editDot, BadgeInk);
            shaft.anchorMin = shaft.anchorMax = new Vector2(0.5f, 0.5f);
            shaft.pivot = new Vector2(0.5f, 0.5f);
            shaft.sizeDelta = new Vector2(2.6f, 8f);
            shaft.anchoredPosition = new Vector2(0.8f, 0.8f);
            shaft.localRotation = Quaternion.Euler(0f, 0f, 45f);
            shaft.GetComponent<Image>().raycastTarget = false;
            var tip = PanelObject("Pencil Tip", editDot, BadgeInk);
            tip.anchorMin = tip.anchorMax = new Vector2(0.5f, 0.5f);
            tip.pivot = new Vector2(0.5f, 0.5f);
            tip.sizeDelta = new Vector2(2.6f, 2.6f);
            tip.anchoredPosition = new Vector2(-2.6f, -2.6f);
            tip.localRotation = Quaternion.Euler(0f, 0f, 45f);
            RoundCircle(tip);
            tip.GetComponent<Image>().raycastTarget = false;
        }
        var avatarBtn = avatarBtnHolder.gameObject.AddComponent<Button>();
        avatarBtn.transition = Selectable.Transition.None;
        avatarBtn.onClick.AddListener(OpenProfileIconPicker);

        var title = TextObject("Handle", header, handle, 27, Ink, TextAnchor.LowerLeft);
        title.fontStyle = FontStyle.Bold;
        Stretch(title.rectTransform, new Vector2(0f, 0.38f), new Vector2(0.5f, 1f), new Vector2(64f, 0f), new Vector2(0f, -4f));

        var sub = TextObject("Sub", header,
            $"CAPTAIN'S LOG  ·  {b.games} GAMES  ·  ALL MODES", 11, Muted, TextAnchor.UpperLeft, monoFont);
        Stretch(sub.rectTransform, Vector2.zero, new Vector2(0.5f, 0.38f), new Vector2(64f, 4f), Vector2.zero);

        // Right side: live shared summary + preview badge + PRIVACY button.
        string shared = $"SHARED — DECKS: {profilePrivacy.decks.ToUpperInvariant()} · REPLAYS: {profilePrivacy.replays.ToUpperInvariant()}";
        if (profileViewAs != "me") shared = $"PREVIEWING AS {profileViewAs.ToUpperInvariant()}  ·  " + shared;
        var sharedText = TextObject("Shared", header, shared, 10,
            profileViewAs != "me" ? ProfileAmber : Muted, TextAnchor.MiddleRight, monoFont);
        Stretch(sharedText.rectTransform, new Vector2(0.4f, 0f), new Vector2(1f, 1f),
            new Vector2(0f, 0f), new Vector2(-150f, 0f));

        var privBtnHolder = PanelObject("Priv Holder", header, new Color(0, 0, 0, 0));
        privBtnHolder.anchorMin = privBtnHolder.anchorMax = new Vector2(1f, 0.5f);
        privBtnHolder.pivot = new Vector2(1f, 0.5f);
        privBtnHolder.sizeDelta = new Vector2(118f, 34f);
        privBtnHolder.anchoredPosition = new Vector2(0f, 0f);
        AddButton(privBtnHolder, "PRIVACY", () => { profilePrivacyOpen = !profilePrivacyOpen; RenderMenu(); },
            enabled: true, dot: true, fill: true);
        // Settings persist, but nobody else can scout you until Find Players
        // ships — the enforcement half of this feature is still in dev.
        AddProfileStatusChip(privBtnHolder, "DEV", -4f, 8f);   // corner badge

        // Tab switch (Overview / Deck History / Seasonal) — segmented like the
        // match-detail tabs.
        var tabs = PanelObject("PF Tabs", stage, new Color32(14, 28, 43, 255));
        tabs.anchorMin = tabs.anchorMax = new Vector2(0f, 1f);
        tabs.pivot = new Vector2(0f, 0.5f);
        tabs.sizeDelta = new Vector2(430f, 34f);
        tabs.anchoredPosition = new Vector2(4f, -87f);
        Round(tabs);
        AddRoundedCardBorder(tabs, MenuB, 1f);
        BuildProfileTab(tabs, "OVERVIEW", "overview", 0f, 1f / 3f);
        BuildProfileTab(tabs, "DECK HISTORY", "decks", 1f / 3f, 2f / 3f);
        BuildProfileTab(tabs, "SEASONAL", "seasonal", 2f / 3f, 1f);

        // Privacy badges next to the tabs (green = visible to current preview
        // audience, amber = hidden), per the handoff's profile header.
        BuildPrivacyBadge(stage, "DECKS", profilePrivacy.decks, ProfileCanSeeDecks(), 446f);
        BuildPrivacyBadge(stage, "REPLAYS", profilePrivacy.replays, ProfileCanSeeReplays(), 446f + 128f);
    }

    private void BuildProfileTab(RectTransform group, string label, string value, float x0, float x1)
    {
        bool active = profileTab == value;
        var tab = PanelObject(value + " Tab", group, active ? Accent : new Color(0, 0, 0, 0));
        Stretch(tab, new Vector2(x0, 0f), new Vector2(x1, 1f), new Vector2(3f, 3f), new Vector2(-3f, -3f));
        Round(tab);
        var t = TextObject("t", tab, label, 10, active ? BadgeInk : Muted, TextAnchor.MiddleCenter, monoFont);
        t.fontStyle = FontStyle.Bold;
        Stretch(t.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var btn = tab.gameObject.AddComponent<Button>();
        btn.onClick.AddListener(() =>
        {
            profileTab = value;
            profileSelDeck = null;
            RenderMenu();
        });
    }

    private void BuildPrivacyBadge(RectTransform stage, string label, string scope, bool visible, float x)
    {
        var color = visible ? GoodGreen : ProfileAmber;
        var badge = PanelObject(label + " Badge", stage, new Color(color.r, color.g, color.b, 0.10f));
        badge.anchorMin = badge.anchorMax = new Vector2(0f, 1f);
        badge.pivot = new Vector2(0f, 0.5f);
        badge.sizeDelta = new Vector2(120f, 24f);
        badge.anchoredPosition = new Vector2(x, -87f);
        Round(badge);
        AddRoundedCardBorder(badge, new Color(color.r, color.g, color.b, 0.45f), 1f);
        var t = TextObject("t", badge, $"{label} · {scope.ToUpperInvariant()}", 9, color, TextAnchor.MiddleCenter, monoFont);
        t.fontStyle = FontStyle.Bold;
        Stretch(t.rectTransform, Vector2.zero, Vector2.one, new Vector2(2f, 0f), new Vector2(-2f, 0f));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // OVERVIEW tab
    // ══════════════════════════════════════════════════════════════════════════

    private void BuildProfileOverviewTab(RectTransform body)
    {
        var b = profileLifetime;
        float y = 0f;   // running offset from the top of body (negative = down)

        // ── Hero (186px): main-leader art, name/tier; Bounty + ELO cards + WR ─
        var hero = PanelObject("PF Hero", body, RowBg);
        Stretch(hero, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, y - 186f), new Vector2(0f, y));
        RoundBig(hero);
        AddRoundedCardBorder(hero, MenuB, 1f);

        string mainLeader = TopOwnLeaderId(b);
        var banner = PanelObject("Hero Banner", hero, RowBg);
        Stretch(banner, Vector2.zero, new Vector2(0.42f, 1f), Vector2.zero, Vector2.zero);
        if (mainLeader != null) BuildLeaderBanner(banner, mainLeader, 680f, 186f, darkLeft: false);

        var derived = ProfileDerived.From(b);
        var tier = ProfileTierFor(derived.elo);

        var heroName = TextObject("Hero Name", hero,
            mainLeader != null ? (MenuCard(mainLeader)?.name ?? mainLeader) : "No matches yet",
            22, Ink, TextAnchor.UpperLeft);
        heroName.fontStyle = FontStyle.Bold;
        Stretch(heroName.rectTransform, new Vector2(0.02f, 0.55f), new Vector2(0.42f, 0.92f),
            new Vector2(12f, 0f), Vector2.zero);

        var heroSub = TextObject("Hero Sub", hero,
            mainLeader != null ? $"MAIN LEADER  ·  {OwnLeaderGames(b, mainLeader)} GAMES" : "PLAY A MATCH TO START YOUR LOG",
            10, Muted, TextAnchor.UpperLeft, monoFont);
        Stretch(heroSub.rectTransform, new Vector2(0.02f, 0.36f), new Vector2(0.42f, 0.55f),
            new Vector2(12f, 0f), Vector2.zero);

        // Tier chip
        var tierChip = PanelObject("Tier Chip", hero, new Color(tier.color.r, tier.color.g, tier.color.b, 0.14f));
        tierChip.anchorMin = tierChip.anchorMax = new Vector2(0.02f, 0.2f);
        tierChip.pivot = new Vector2(0f, 0.5f);
        tierChip.sizeDelta = new Vector2(24f + tier.name.Length * 7.4f, 22f);
        tierChip.anchoredPosition = new Vector2(12f, 0f);
        Round(tierChip);
        AddRoundedCardBorder(tierChip, new Color(tier.color.r, tier.color.g, tier.color.b, 0.5f), 1f);
        var tierText = TextObject("t", tierChip, tier.name.ToUpperInvariant(), 9, tier.color, TextAnchor.MiddleCenter, monoFont);
        tierText.fontStyle = FontStyle.Bold;
        Stretch(tierText.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        // Tier is derived from provisional ELO — carries the DEV marker too.
        var tierDev = PanelObject("Tier Dev Anchor", hero, new Color(0f, 0f, 0f, 0f));
        tierDev.anchorMin = tierDev.anchorMax = new Vector2(0.02f, 0.2f);
        tierDev.pivot = new Vector2(0f, 0.5f);
        tierDev.sizeDelta = new Vector2(40f, 22f);
        tierDev.anchoredPosition = new Vector2(12f + tierChip.sizeDelta.x + 8f, 0f);
        AddProfileStatusChip(tierDev, "DEV", 0f, -3f);

        // Bounty / ELO cards + win-rate cell
        // Bounty / ELO / Tier are client-derived until a ranked rating service
        // exists — marked with the same DEV chip as the Versus A.I. mode tile.
        BuildHeroStatCard(hero, "BOUNTY", derived.bountyLabel, tier.color, 0.44f, 0.635f, dev: true);
        BuildHeroStatCard(hero, "ELO", derived.elo.ToString(), Accent, 0.645f, 0.80f, dev: true);
        BuildHeroStatCard(hero, "WIN RATE", b.games > 0 ? $"{Mathf.RoundToInt(b.WinRate * 100f)}%" : "—",
            ProfileWrColor(b.WinRate, b.games), 0.81f, 0.985f, wrBar: b.games > 0 ? b.WinRate : (float?)null);

        y -= 198f;

        // ── Stat strip: 6 cells ──────────────────────────────────────────────
        var strip = PanelObject("PF Strip", body, new Color(0, 0, 0, 0));
        Stretch(strip, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, y - 84f), new Vector2(0f, y));
        var cells = new (string label, string val, Color color)[]
        {
            ("GAMES", b.games.ToString(), Ink),
            ("RECORD", $"{b.wins}W – {b.losses}L", Ink),
            ("WIN RATE", b.games > 0 ? $"{Mathf.RoundToInt(b.WinRate * 100f)}%" : "—", ProfileWrColor(b.WinRate, b.games)),
            ("BEST STREAK", b.bestWinStreak > 0 ? $"{b.bestWinStreak}W" : "—", GoodGreen),
            ("GOING FIRST", b.firstGames > 0 ? $"{Mathf.RoundToInt(100f * b.firstWins / b.firstGames)}%" : "—", Ink),
            ("TIER", tier.name.ToUpperInvariant(), tier.color),
        };
        for (int i = 0; i < cells.Length; i++)
        {
            float x0 = i / 6f, x1 = (i + 1) / 6f;
            var cell = PanelObject("Cell " + cells[i].label, strip, RowBg);
            Stretch(cell, new Vector2(x0, 0f), new Vector2(x1, 1f),
                new Vector2(i == 0 ? 0f : 6f, 0f), new Vector2(i == 5 ? 0f : -6f, 0f));
            Round(cell);
            AddRoundedCardBorder(cell, MenuB, 1f);
            var lab = TextObject("l", cell, cells[i].label, 9, Muted, TextAnchor.UpperLeft, monoFont);
            Stretch(lab.rectTransform, new Vector2(0f, 0.6f), Vector2.one, new Vector2(12f, 0f), new Vector2(-8f, -10f));
            var val = TextObject("v", cell, cells[i].val, 20, cells[i].color, TextAnchor.LowerLeft, monoFont);
            val.fontStyle = FontStyle.Bold;
            Stretch(val.rectTransform, Vector2.zero, new Vector2(1f, 0.62f), new Vector2(12f, 10f), new Vector2(-8f, 0f));
            if (cells[i].label == "TIER") AddProfileStatusChip(cell);   // derived stat
        }
        y -= 96f;

        // ── Most-played leaders (top 3, with WR bar) ─────────────────────────
        BuildSectionLabel(body, "MOST-PLAYED LEADERS", y); y -= 26f;
        var top = (b.byOwnLeader ?? new List<LeaderStat>())
            .Where(l => l != null && l.games > 0)
            .OrderByDescending(l => l.games).Take(3).ToList();
        var lead = PanelObject("PF Leaders", body, new Color(0, 0, 0, 0));
        Stretch(lead, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, y - 140f), new Vector2(0f, y));
        if (top.Count == 0)
        {
            var none = TextObject("None", lead, "No leader data yet — play some matches.", 12, Muted, TextAnchor.MiddleLeft, monoFont);
            Stretch(none.rectTransform, Vector2.zero, Vector2.one, new Vector2(4f, 0f), Vector2.zero);
        }
        for (int i = 0; i < top.Count; i++)
        {
            float x0 = i / 3f, x1 = (i + 1) / 3f;
            var card = PanelObject("Leader " + top[i].leaderId, lead, RowBg);
            Stretch(card, new Vector2(x0, 0f), new Vector2(x1, 1f),
                new Vector2(i == 0 ? 0f : 6f, 0f), new Vector2(i == 2 ? 0f : -6f, 0f));
            RoundBig(card);
            AddRoundedCardBorder(card, MenuB, 1f);
            BuildLeaderStatCard(card, top[i]);
        }
        y -= 152f;

        // ── Recent matches (gated by replays visibility) ─────────────────────
        BuildSectionLabel(body, "RECENT MATCHES", y); y -= 26f;
        if (!ProfileCanSeeReplays())
        {
            BuildLockCard(body, y, 96f, "replays",
                $"Replays shared with {profilePrivacy.replays} only.",
                "This is what other captains see under your current setting.");
            return;
        }
        var recent = (matchHistory ?? new List<MatchSummary>()).Take(5).ToList();
        if (recent.Count == 0)
        {
            var none = TextObject("NoMatches", body, "No matches recorded yet.", 12, Muted, TextAnchor.UpperLeft, monoFont);
            Stretch(none.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(4f, y - 30f), new Vector2(0f, y));
            return;
        }
        for (int i = 0; i < recent.Count; i++)
        {
            BuildRecentMatchRow(body, recent[i], y);
            y -= 58f;
        }
    }

    private void BuildHeroStatCard(RectTransform hero, string label, string value, Color valColor,
        float x0, float x1, float? wrBar = null, bool dev = false)
    {
        var card = PanelObject(label + " Card", hero, new Color32(14, 28, 43, 255));
        Stretch(card, new Vector2(x0, 0.14f), new Vector2(x1, 0.86f), Vector2.zero, Vector2.zero);
        Round(card);
        AddRoundedCardBorder(card, MenuB, 1f);
        if (dev) AddProfileStatusChip(card);   // derived/provisional stat
        var lab = TextObject("l", card, label, 9, Muted, TextAnchor.UpperLeft, monoFont);
        Stretch(lab.rectTransform, new Vector2(0f, 0.66f), Vector2.one, new Vector2(14f, 0f), new Vector2(-10f, -12f));
        var val = TextObject("v", card, value, 30, valColor, TextAnchor.MiddleLeft, monoFont);
        val.fontStyle = FontStyle.Bold;
        Stretch(val.rectTransform, new Vector2(0f, 0.2f), new Vector2(1f, 0.68f), new Vector2(14f, 0f), new Vector2(-10f, 0f));
        if (wrBar.HasValue)
        {
            var barBg = PanelObject("BarBg", card, new Color(1f, 1f, 1f, 0.07f));
            Stretch(barBg, new Vector2(0.06f, 0.1f), new Vector2(0.94f, 0.17f), Vector2.zero, Vector2.zero);
            Round(barBg);
            var fill = PanelObject("Fill", barBg, valColor);
            Stretch(fill, Vector2.zero, new Vector2(Mathf.Clamp01(wrBar.Value), 1f), Vector2.zero, Vector2.zero);
            Round(fill);
        }
    }

    private void BuildSectionLabel(RectTransform body, string label, float y)
    {
        var holder = PanelObject(label + " Label", body, new Color(0, 0, 0, 0));
        Stretch(holder, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, y - 20f), new Vector2(0f, y));
        // Diamond section marker, per the design's motif.
        var d = PanelObject("Diamond", holder, Accent);
        d.anchorMin = d.anchorMax = new Vector2(0f, 0.5f);
        d.pivot = new Vector2(0f, 0.5f);
        d.sizeDelta = new Vector2(7f, 7f);
        d.anchoredPosition = new Vector2(2f, 0f);
        d.localRotation = Quaternion.Euler(0f, 0f, 45f);
        var t = TextObject("t", holder, label, 10, Muted, TextAnchor.MiddleLeft, monoFont);
        t.fontStyle = FontStyle.Bold;
        Stretch(t.rectTransform, Vector2.zero, Vector2.one, new Vector2(18f, 0f), Vector2.zero);
    }

    private void BuildLeaderStatCard(RectTransform card, LeaderStat stat)
    {
        var banner = PanelObject("Art", card, RowBg);
        Stretch(banner, Vector2.zero, new Vector2(0.38f, 1f), Vector2.zero, Vector2.zero);
        BuildLeaderBanner(banner, stat.leaderId, 200f, 140f, darkLeft: false);

        var rec = MenuCard(stat.leaderId);
        var name = TextObject("Name", card, rec?.name ?? stat.leaderId, 15, Ink, TextAnchor.UpperLeft);
        name.fontStyle = FontStyle.Bold;
        Stretch(name.rectTransform, new Vector2(0.42f, 0.62f), Vector2.one, Vector2.zero, new Vector2(-10f, -14f));

        float wr = stat.games > 0 ? (float)stat.wins / stat.games : 0f;
        var line = TextObject("Line", card,
            $"{stat.games} GAMES  ·  {stat.wins}W–{stat.games - stat.wins}L  ·  {Mathf.RoundToInt(wr * 100f)}%",
            10, ProfileWrColor(wr, stat.games), TextAnchor.UpperLeft, monoFont);
        Stretch(line.rectTransform, new Vector2(0.42f, 0.38f), new Vector2(1f, 0.62f), Vector2.zero, new Vector2(-10f, 0f));

        var barBg = PanelObject("BarBg", card, new Color(1f, 1f, 1f, 0.07f));
        Stretch(barBg, new Vector2(0.42f, 0.2f), new Vector2(0.94f, 0.29f), Vector2.zero, Vector2.zero);
        Round(barBg);
        var fill = PanelObject("Fill", barBg, MenuLeaderColor(stat.leaderId));
        Stretch(fill, Vector2.zero, new Vector2(Mathf.Clamp01(wr), 1f), Vector2.zero, Vector2.zero);
        Round(fill);
    }

    private void BuildRecentMatchRow(RectTransform body, MatchSummary m, float y)
    {
        bool win = m.result == "win";
        var resColor = win ? GoodGreen : RedAccent;
        var row = PanelObject("PF Match " + m.id, body, RowBg);
        Stretch(row, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, y - 52f), new Vector2(0f, y));
        Round(row);
        AddRoundedCardBorder(row, MenuB, 1f);

        var stripe = PanelObject("Stripe", row, resColor);
        Stretch(stripe, Vector2.zero, new Vector2(0f, 1f), new Vector2(0f, 6f), new Vector2(3f, -6f));

        var res = TextObject("Res", row, win ? "WIN" : "LOSS", 11, resColor, TextAnchor.MiddleLeft, monoFont);
        res.fontStyle = FontStyle.Bold;
        Stretch(res.rectTransform, Vector2.zero, new Vector2(0.07f, 1f), new Vector2(16f, 0f), Vector2.zero);

        var you = MenuCard(m.youLeaderId); var opp = MenuCard(m.oppLeaderId);
        var matchup = TextObject("Mu", row,
            $"{you?.name ?? m.youLeaderId ?? "?"}  vs  {opp?.name ?? m.oppLeaderId ?? "?"}",
            13, Ink, TextAnchor.MiddleLeft);
        Stretch(matchup.rectTransform, new Vector2(0.07f, 0f), new Vector2(0.52f, 1f), Vector2.zero, Vector2.zero);

        var oppName = TextObject("Opp", row, (m.oppName ?? "Opponent").ToUpperInvariant(), 10, Muted, TextAnchor.MiddleLeft, monoFont);
        Stretch(oppName.rectTransform, new Vector2(0.52f, 0f), new Vector2(0.72f, 1f), Vector2.zero, Vector2.zero);

        var life = TextObject("Life", row, $"{m.youFinalLife} – {m.oppFinalLife} LIFE  ·  T{m.turnCount}", 10, Muted, TextAnchor.MiddleLeft, monoFont);
        Stretch(life.rectTransform, new Vector2(0.72f, 0f), new Vector2(0.88f, 1f), Vector2.zero, Vector2.zero);

        var ago = TextObject("Ago", row, FormatAgo(m.savedAtIso), 10, new Color32(111, 134, 150, 255), TextAnchor.MiddleRight, monoFont);
        Stretch(ago.rectTransform, new Vector2(0.88f, 0f), Vector2.one, Vector2.zero, new Vector2(-14f, 0f));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // DECK HISTORY tab — master–detail, gated by decks visibility
    // ══════════════════════════════════════════════════════════════════════════

    private void BuildProfileDecksTab(RectTransform body)
    {
        if (!ProfileCanSeeDecks())
        {
            BuildLockCard(body, 0f, 120f, "decks",
                $"Decks shared with {profilePrivacy.decks} only.",
                "This is what other captains see under your current setting.");
            return;
        }

        var b = profileLifetime;
        var decks = (b.byOwnLeader ?? new List<LeaderStat>())
            .Where(l => l != null && l.games > 0)
            .OrderByDescending(l => l.games).ToList();
        if (decks.Count == 0)
        {
            var none = TextObject("None", body, "No deck history yet — play some matches.", 12, Muted, TextAnchor.UpperLeft, monoFont);
            Stretch(none.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(4f, -34f), Vector2.zero);
            return;
        }
        if (profileSelDeck == null || decks.All(d => d.leaderId != profileSelDeck))
            profileSelDeck = decks[0].leaderId;

        // ── Left: 300px deck list ────────────────────────────────────────────
        var list = PanelObject("Deck List", body, LogBgDark);
        Stretch(list, new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(300f, 0f));
        RoundBig(list);
        AddRoundedCardBorder(list, MenuB, 1f);
        float ly = -10f;
        foreach (var d in decks.Take(12))
        {
            bool sel = d.leaderId == profileSelDeck;
            var row = PanelObject("Deck " + d.leaderId, list,
                sel ? new Color(Accent.r, Accent.g, Accent.b, 0.13f) : new Color(0, 0, 0, 0));
            Stretch(row, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(8f, ly - 60f), new Vector2(-8f, ly));
            Round(row);
            AddRoundedCardBorder(row, sel ? new Color(Accent.r, Accent.g, Accent.b, 0.4f) : MenuB, sel ? 1.5f : 1f);

            var dot = PanelObject("Dot", row, MenuLeaderColor(d.leaderId));
            dot.anchorMin = dot.anchorMax = new Vector2(0f, 0.5f);
            dot.pivot = new Vector2(0f, 0.5f);
            dot.sizeDelta = new Vector2(8f, 8f);
            dot.anchoredPosition = new Vector2(12f, 0f);
            RoundCircle(dot);

            var rec = MenuCard(d.leaderId);
            var name = TextObject("Name", row, rec?.name ?? d.leaderId, 13, sel ? Ink : (Color)new Color32(198, 211, 220, 255), TextAnchor.LowerLeft);
            name.fontStyle = sel ? FontStyle.Bold : FontStyle.Normal;
            Stretch(name.rectTransform, new Vector2(0f, 0.5f), Vector2.one, new Vector2(30f, 2f), new Vector2(-8f, -6f));

            float wr = d.games > 0 ? (float)d.wins / d.games : 0f;
            var line = TextObject("Line", row, $"{d.games} GAMES · {Mathf.RoundToInt(wr * 100f)}%",
                9, ProfileWrColor(wr, d.games), TextAnchor.UpperLeft, monoFont);
            Stretch(line.rectTransform, Vector2.zero, new Vector2(1f, 0.5f), new Vector2(30f, 6f), new Vector2(-8f, -2f));

            string id = d.leaderId;
            var btn = row.gameObject.AddComponent<Button>();
            btn.onClick.AddListener(() => { profileSelDeck = id; RenderMenu(); });
            ly -= 66f;
        }

        // ── Right: detail for selected deck ──────────────────────────────────
        var detail = PanelObject("Deck Detail", body, new Color(0, 0, 0, 0));
        Stretch(detail, Vector2.zero, Vector2.one, new Vector2(316f, 0f), Vector2.zero);
        var selStat = decks.First(d => d.leaderId == profileSelDeck);
        BuildDeckDetail(detail, selStat);
    }

    private void BuildDeckDetail(RectTransform area, LeaderStat stat)
    {
        var b = profileLifetime;
        var rec = MenuCard(stat.leaderId);
        float wr = stat.games > 0 ? (float)stat.wins / stat.games : 0f;

        // Header card
        var head = PanelObject("DD Head", area, RowBg);
        Stretch(head, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -84f), Vector2.zero);
        Round(head);
        AddRoundedCardBorder(head, MenuB, 1f);
        var banner = PanelObject("Art", head, RowBg);
        Stretch(banner, Vector2.zero, new Vector2(0.16f, 1f), Vector2.zero, Vector2.zero);
        BuildLeaderBanner(banner, stat.leaderId, 220f, 84f, darkLeft: false);
        var name = TextObject("Name", head, rec?.name ?? stat.leaderId, 18, Ink, TextAnchor.UpperLeft);
        name.fontStyle = FontStyle.Bold;
        Stretch(name.rectTransform, new Vector2(0.18f, 0.45f), new Vector2(0.6f, 1f), Vector2.zero, new Vector2(0f, -14f));
        var line = TextObject("Line", head,
            $"{stat.games} GAMES  ·  {stat.wins}W–{stat.games - stat.wins}L  ·  {Mathf.RoundToInt(wr * 100f)}% WIN RATE",
            10, ProfileWrColor(wr, stat.games), TextAnchor.UpperLeft, monoFont);
        Stretch(line.rectTransform, new Vector2(0.18f, 0.12f), new Vector2(0.7f, 0.45f), Vector2.zero, Vector2.zero);

        // ── Lifetime chart: monthly games bars + win-rate markers ────────────
        BuildSectionLabel(area, "LIFETIME PLAYRATE & WIN RATE", -100f);
        var chart = PanelObject("DD Chart", area, RowBg);
        Stretch(chart, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -126f - 240f), new Vector2(0f, -126f));
        Round(chart);
        AddRoundedCardBorder(chart, MenuB, 1f);
        BuildDeckTimelineChart(chart, stat.leaderId);

        // ── Matchup grid ─────────────────────────────────────────────────────
        BuildSectionLabel(area, "RECORD VS EACH LEADER", -382f);
        var mups = (b.matchups ?? new List<MatchupStat>())
            .Where(m => m != null && m.ownLeaderId == stat.leaderId && m.games > 0)
            .OrderByDescending(m => m.games).Take(8).ToList();
        if (mups.Count == 0)
        {
            var none = TextObject("None", area,
                "No matchup data yet — matchups are tracked from matches played after this update.",
                11, Muted, TextAnchor.UpperLeft, monoFont);
            Stretch(none.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(4f, -438f), new Vector2(0f, -408f));
            return;
        }
        float my = -408f;
        foreach (var m in mups)
        {
            BuildMatchupRow(area, m, my);
            my -= 40f;
        }
    }

    // Design adaptation: the handoff's SVG bars+line become UGUI bars (games,
    // leader color @55% alpha) with a small green marker at the month's win-rate
    // height. A dashed 50% reference line is approximated with a 1px hairline.
    private void BuildDeckTimelineChart(RectTransform chart, string leaderId)
    {
        var months = (profileLifetime.months ?? new List<MonthStat>())
            .Where(m => m != null && m.leaderId == leaderId && m.games > 0)
            .OrderBy(m => m.ym, StringComparer.Ordinal).ToList();
        if (months.Count == 0)
        {
            var none = TextObject("None", chart,
                "No timeline yet — monthly play data is tracked from matches played after this update.",
                11, Muted, TextAnchor.MiddleCenter, monoFont);
            Stretch(none.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            return;
        }

        var plot = PanelObject("Plot", chart, new Color(0, 0, 0, 0));
        Stretch(plot, Vector2.zero, Vector2.one, new Vector2(16f, 30f), new Vector2(-16f, -14f));

        // 50% win-rate reference hairline
        var refLine = PanelObject("Ref50", plot, new Color(1f, 1f, 1f, 0.10f));
        Stretch(refLine, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 0f), new Vector2(0f, 1f));

        int maxGames = Mathf.Max(1, months.Max(m => m.games));
        var barColor = MenuLeaderColor(leaderId); barColor.a = 0.55f;
        int n = months.Count;
        float slot = 1f / Mathf.Max(n, 6); // keep bars readable when few months exist
        for (int i = 0; i < n; i++)
        {
            var m = months[i];
            float x0 = i * slot, x1 = x0 + slot;
            float h = (float)m.games / maxGames;
            var bar = PanelObject("Bar " + m.ym, plot, barColor);
            Stretch(bar, new Vector2(x0, 0f), new Vector2(x1, Mathf.Max(h, 0.03f)),
                new Vector2(4f, 0f), new Vector2(-4f, 0f));
            Round(bar);

            float mwr = (float)m.wins / m.games;
            var marker = PanelObject("WR " + m.ym, plot, GoodGreen);
            Stretch(marker, new Vector2(x0, Mathf.Clamp01(mwr)), new Vector2(x1, Mathf.Clamp01(mwr)),
                new Vector2(6f, -1.5f), new Vector2(-6f, 1.5f));

            // Month label just below the plot band.
            var lab = TextObject("L " + m.ym, plot, MonthLabel(m.ym), 8, Muted, TextAnchor.MiddleCenter, monoFont);
            var lrt = lab.rectTransform;
            lrt.anchorMin = lrt.anchorMax = new Vector2(Mathf.Lerp(x0, x1, 0.5f), 0f);
            lrt.pivot = new Vector2(0.5f, 1f);
            lrt.sizeDelta = new Vector2(60f, 16f);
            lrt.anchoredPosition = new Vector2(0f, -4f);
        }

        var legend = TextObject("Legend", chart, "BARS = GAMES / MONTH   ·   MARKER = WIN RATE (LINE AT 50%)",
            8, new Color32(111, 134, 150, 255), TextAnchor.LowerRight, monoFont);
        Stretch(legend.rectTransform, Vector2.zero, Vector2.one, new Vector2(0f, 4f), new Vector2(-10f, 0f));
    }

    private void BuildMatchupRow(RectTransform area, MatchupStat m, float y)
    {
        var row = PanelObject("Mup " + m.oppLeaderId, area, RowBg);
        Stretch(row, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, y - 34f), new Vector2(0f, y));
        Round(row);
        AddRoundedCardBorder(row, MenuB, 1f);

        var rec = MenuCard(m.oppLeaderId);
        var name = TextObject("Name", row, "VS  " + (rec?.name ?? m.oppLeaderId), 11, Ink, TextAnchor.MiddleLeft);
        Stretch(name.rectTransform, Vector2.zero, new Vector2(0.3f, 1f), new Vector2(12f, 0f), Vector2.zero);

        // Green/red split bar sized by wins vs losses (flex-grow equivalent).
        int losses = m.games - m.wins;
        float winFrac = m.games > 0 ? (float)m.wins / m.games : 0f;
        var barBg = PanelObject("Bar", row, new Color(RedAccent.r, RedAccent.g, RedAccent.b, 0.45f));
        Stretch(barBg, new Vector2(0.32f, 0.32f), new Vector2(0.78f, 0.68f), Vector2.zero, Vector2.zero);
        Round(barBg);
        if (winFrac > 0f)
        {
            var winSeg = PanelObject("Wins", barBg, GoodGreen);
            Stretch(winSeg, Vector2.zero, new Vector2(winFrac, 1f), Vector2.zero, Vector2.zero);
            Round(winSeg);
        }

        var label = TextObject("WL", row, $"{m.wins}W–{losses}L  ·  {Mathf.RoundToInt(winFrac * 100f)}%",
            10, ProfileWrColor(winFrac, m.games), TextAnchor.MiddleRight, monoFont);
        Stretch(label.rectTransform, new Vector2(0.78f, 0f), Vector2.one, Vector2.zero, new Vector2(-12f, 0f));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // SEASONAL tab
    // ══════════════════════════════════════════════════════════════════════════

    private void BuildProfileSeasonalTab(RectTransform body)
    {
        int sid = StatsStore.CurrentSeasonId;

        // Season banner + Casual/Ranked toggle
        var bannerRow = PanelObject("Season Banner", body, RowBg);
        Stretch(bannerRow, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -64f), Vector2.zero);
        Round(bannerRow);
        AddRoundedCardBorder(bannerRow, MenuB, 1f);
        var sTitle = TextObject("T", bannerRow,
            sid > 0 ? $"SEASON {sid}" : "NO ACTIVE SEASON", 16, Ink, TextAnchor.MiddleLeft, monoFont);
        sTitle.fontStyle = FontStyle.Bold;
        Stretch(sTitle.rectTransform, Vector2.zero, new Vector2(0.4f, 1f), new Vector2(16f, 0f), Vector2.zero);

        var toggle = PanelObject("Mode Toggle", bannerRow, new Color32(14, 28, 43, 255));
        toggle.anchorMin = new Vector2(1f, 0.5f); toggle.anchorMax = new Vector2(1f, 0.5f);
        toggle.pivot = new Vector2(1f, 0.5f);
        toggle.sizeDelta = new Vector2(220f, 34f);
        toggle.anchoredPosition = new Vector2(-12f, 0f);
        Round(toggle);
        AddRoundedCardBorder(toggle, MenuB, 1f);
        BuildSeasonModeTab(toggle, "CASUAL", "casual", 0f, 0.5f);
        BuildSeasonModeTab(toggle, "RANKED", "ranked", 0.5f, 1f);
        // Ranked isn't live — same SOON marker as the Ranked Match portal.
        AddProfileStatusChip(toggle, "SOON", -4f, 8f);   // corner badge

        var area = PanelObject("Season Area", body, new Color(0, 0, 0, 0));
        Stretch(area, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0f, -76f));

        if (profileSeasonMode == "ranked") BuildRankedEmptyState(area);
        else BuildSeasonCasual(area, sid);
    }

    private void BuildSeasonModeTab(RectTransform group, string label, string value, float x0, float x1)
    {
        bool active = profileSeasonMode == value;
        var tab = PanelObject(value + " Tab", group, active ? Accent : new Color(0, 0, 0, 0));
        Stretch(tab, new Vector2(x0, 0f), new Vector2(x1, 1f), new Vector2(3f, 3f), new Vector2(-3f, -3f));
        Round(tab);
        var t = TextObject("t", tab, label, 10, active ? BadgeInk : Muted, TextAnchor.MiddleCenter, monoFont);
        t.fontStyle = FontStyle.Bold;
        Stretch(t.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var btn = tab.gameObject.AddComponent<Button>();
        btn.onClick.AddListener(() => { profileSeasonMode = value; RenderMenu(); });
    }

    private void BuildSeasonCasual(RectTransform area, int seasonId)
    {
        var s = profileSeason ?? new StatsBucket();
        float y = 0f;

        // 4 stat cards
        var cards = new (string label, string val, Color color)[]
        {
            ("GAMES", s.games.ToString(), Ink),
            ("RECORD", $"{s.wins}W – {s.losses}L", Ink),
            ("WIN RATE", s.games > 0 ? $"{Mathf.RoundToInt(s.WinRate * 100f)}%" : "—", ProfileWrColor(s.WinRate, s.games)),
            ("STREAK", s.currentStreak == 0 ? "—" : (s.currentStreak > 0 ? $"{s.currentStreak}W" : $"{-s.currentStreak}L"),
                s.currentStreak >= 0 ? GoodGreen : RedAccent),
        };
        for (int i = 0; i < cards.Length; i++)
        {
            float x0 = i / 4f, x1 = (i + 1) / 4f;
            var card = PanelObject("SC " + cards[i].label, area, RowBg);
            Stretch(card, new Vector2(x0, 1f), new Vector2(x1, 1f),
                new Vector2(i == 0 ? 0f : 6f, y - 84f), new Vector2(i == 3 ? 0f : -6f, y));
            Round(card);
            AddRoundedCardBorder(card, MenuB, 1f);
            var lab = TextObject("l", card, cards[i].label, 9, Muted, TextAnchor.UpperLeft, monoFont);
            Stretch(lab.rectTransform, new Vector2(0f, 0.6f), Vector2.one, new Vector2(12f, 0f), new Vector2(-8f, -10f));
            var val = TextObject("v", card, cards[i].val, 22, cards[i].color, TextAnchor.LowerLeft, monoFont);
            val.fontStyle = FontStyle.Bold;
            Stretch(val.rectTransform, Vector2.zero, new Vector2(1f, 0.62f), new Vector2(12f, 10f), new Vector2(-8f, 0f));
        }
        y -= 100f;

        // Deck usage this season (gated by decks)
        BuildSectionLabel(area, "DECK USAGE THIS SEASON", y); y -= 26f;
        if (!ProfileCanSeeDecks())
        {
            BuildLockCard(area, y, 84f, "decks",
                $"Decks shared with {profilePrivacy.decks} only.", null);
            y -= 96f;
        }
        else
        {
            var usage = (s.byOwnLeader ?? new List<LeaderStat>())
                .Where(l => l != null && l.games > 0).OrderByDescending(l => l.games).Take(4).ToList();
            if (usage.Count == 0)
            {
                var none = TextObject("None", area, seasonId > 0 ? "No games this season yet." : "Season play hasn't started.",
                    11, Muted, TextAnchor.UpperLeft, monoFont);
                Stretch(none.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(4f, y - 26f), new Vector2(0f, y));
                y -= 38f;
            }
            else
            {
                int maxG = Mathf.Max(1, usage.Max(u => u.games));
                foreach (var u in usage)
                {
                    var row = PanelObject("Use " + u.leaderId, area, RowBg);
                    Stretch(row, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, y - 34f), new Vector2(0f, y));
                    Round(row);
                    AddRoundedCardBorder(row, MenuB, 1f);
                    var rec = MenuCard(u.leaderId);
                    var name = TextObject("N", row, rec?.name ?? u.leaderId, 11, Ink, TextAnchor.MiddleLeft);
                    Stretch(name.rectTransform, Vector2.zero, new Vector2(0.3f, 1f), new Vector2(12f, 0f), Vector2.zero);
                    var barBg = PanelObject("Bar", row, new Color(1f, 1f, 1f, 0.07f));
                    Stretch(barBg, new Vector2(0.32f, 0.32f), new Vector2(0.8f, 0.68f), Vector2.zero, Vector2.zero);
                    Round(barBg);
                    var fill = PanelObject("Fill", barBg, MenuLeaderColor(u.leaderId));
                    Stretch(fill, Vector2.zero, new Vector2((float)u.games / maxG, 1f), Vector2.zero, Vector2.zero);
                    Round(fill);
                    float uwr = (float)u.wins / u.games;
                    var lab = TextObject("L", row, $"{u.games} G · {Mathf.RoundToInt(uwr * 100f)}%",
                        10, ProfileWrColor(uwr, u.games), TextAnchor.MiddleRight, monoFont);
                    Stretch(lab.rectTransform, new Vector2(0.8f, 0f), Vector2.one, Vector2.zero, new Vector2(-12f, 0f));
                    y -= 40f;
                }
            }
        }
        y -= 12f;

        // Head-to-head vs leaders (gated by replays)
        BuildSectionLabel(area, "HEAD-TO-HEAD VS LEADERS", y); y -= 26f;
        if (!ProfileCanSeeReplays())
        {
            BuildLockCard(area, y, 84f, "replays",
                $"Replays shared with {profilePrivacy.replays} only.", null);
            return;
        }
        var h2h = (s.byOpponentLeader ?? new List<LeaderStat>())
            .Where(l => l != null && l.games > 0).OrderByDescending(l => l.games).Take(6).ToList();
        if (h2h.Count == 0)
        {
            var none = TextObject("NoH2H", area, "No opponents faced this season yet.", 11, Muted, TextAnchor.UpperLeft, monoFont);
            Stretch(none.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(4f, y - 26f), new Vector2(0f, y));
            return;
        }
        foreach (var o in h2h)
        {
            BuildMatchupRow(area, new MatchupStat
            {
                ownLeaderId = "*", oppLeaderId = o.leaderId, games = o.games, wins = o.wins,
            }, y);
            y -= 40f;
        }
    }

    private void BuildRankedEmptyState(RectTransform area)
    {
        var card = PanelObject("Ranked Empty", area, RowBg);
        Stretch(card, new Vector2(0.15f, 1f), new Vector2(0.85f, 1f), new Vector2(0f, -240f), new Vector2(0f, -40f));
        RoundBig(card);
        AddRoundedCardBorder(card, MenuB, 1f);

        var title = TextObject("T", card, "RANKED HASN'T SET SAIL YET", 18, Ink, TextAnchor.MiddleCenter, monoFont);
        title.fontStyle = FontStyle.Bold;
        Stretch(title.rectTransform, new Vector2(0f, 0.55f), Vector2.one, Vector2.zero, Vector2.zero);
        var sub = TextObject("S", card, "COMPETITIVE LADDERS ARRIVE WITH A FUTURE SEASON.\nUNTIL THEN, EVERY MATCH COUNTS TOWARD YOUR CASUAL LOG.",
            11, Muted, TextAnchor.UpperCenter, monoFont);
        Stretch(sub.rectTransform, new Vector2(0.05f, 0.12f), new Vector2(0.95f, 0.55f), Vector2.zero, Vector2.zero);

        // Tier-ladder strip
        var strip = PanelObject("Tier Strip", area, new Color(0, 0, 0, 0));
        Stretch(strip, new Vector2(0.15f, 1f), new Vector2(0.85f, 1f), new Vector2(0f, -320f), new Vector2(0f, -260f));
        for (int i = 0; i < ProfileTiers.Length; i++)
        {
            float x0 = (float)i / ProfileTiers.Length, x1 = (float)(i + 1) / ProfileTiers.Length;
            var (tname, tcolor, _) = ProfileTiers[i];
            var chip = PanelObject("Tier " + tname, strip, new Color(tcolor.r, tcolor.g, tcolor.b, 0.12f));
            Stretch(chip, new Vector2(x0, 0f), new Vector2(x1, 1f),
                new Vector2(i == 0 ? 0f : 4f, 0f), new Vector2(i == ProfileTiers.Length - 1 ? 0f : -4f, 0f));
            Round(chip);
            AddRoundedCardBorder(chip, new Color(tcolor.r, tcolor.g, tcolor.b, 0.45f), 1f);
            var t = TextObject("t", chip, tname.ToUpperInvariant(), 8, tcolor, TextAnchor.MiddleCenter, monoFont);
            t.fontStyle = FontStyle.Bold;
            Stretch(t.rectTransform, Vector2.zero, Vector2.one, new Vector2(2f, 0f), new Vector2(-2f, 0f));
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Privacy panel (420px popover) + lock cards
    // ══════════════════════════════════════════════════════════════════════════

    private void BuildPrivacyPanel(RectTransform stage)
    {
        // Click-away scrim
        var scrim = PanelObject("Priv Scrim", stage, new Color(0f, 0f, 0f, 0.35f));
        Stretch(scrim, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var scrimBtn = scrim.gameObject.AddComponent<Button>();
        scrimBtn.transition = Selectable.Transition.None;
        scrimBtn.onClick.AddListener(() => { profilePrivacyOpen = false; RenderMenu(); });

        var panel = PanelObject("Priv Panel", stage, new Color32(13, 27, 42, 252));
        panel.anchorMin = panel.anchorMax = new Vector2(1f, 1f);
        panel.pivot = new Vector2(1f, 1f);
        panel.sizeDelta = new Vector2(420f, 384f);
        panel.anchoredPosition = new Vector2(-4f, -64f);
        RoundBig(panel);
        AddRoundedCardBorder(panel, new Color(Accent.r, Accent.g, Accent.b, 0.4f), 1.5f);

        var intro = TextObject("Intro", panel,
            "Choose who can see your decks and replays. This is how other captains will view you once Find Players ships (in dev).",
            11, Muted, TextAnchor.UpperLeft);
        AddProfileStatusChip(panel, "DEV", -10f, -10f);
        Stretch(intro.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(18f, -66f), new Vector2(-18f, -14f));

        BuildPrivacySegment(panel, "DECKS", profilePrivacy.Decks, -84f, v =>
        {
            profilePrivacy.decks = ProfilePrivacySettings.Wire(v);
            _ = ProfilePrivacyStore.SaveAsync(profilePrivacy.Decks, profilePrivacy.Replays);
            RenderMenu();
        });
        BuildPrivacySegment(panel, "REPLAYS", profilePrivacy.Replays, -160f, v =>
        {
            profilePrivacy.replays = ProfilePrivacySettings.Wire(v);
            _ = ProfilePrivacyStore.SaveAsync(profilePrivacy.Decks, profilePrivacy.Replays);
            RenderMenu();
        });

        // Divider
        var divider = PanelObject("Div", panel, MenuB);
        Stretch(divider, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(18f, -238f), new Vector2(-18f, -237f));

        // PREVIEW AS — session only, never persisted (matches the handoff).
        var pvLabel = TextObject("PV Label", panel, "PREVIEW AS", 10, Muted, TextAnchor.MiddleLeft, monoFont);
        pvLabel.fontStyle = FontStyle.Bold;
        Stretch(pvLabel.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(18f, -278f), new Vector2(-18f, -252f));
        var pvGroup = PanelObject("PV Group", panel, new Color32(14, 28, 43, 255));
        Stretch(pvGroup, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(18f, -322f), new Vector2(-18f, -284f));
        Round(pvGroup);
        AddRoundedCardBorder(pvGroup, MenuB, 1f);
        var pvOptions = new[] { ("ME", "me"), ("FRIEND", "friend"), ("PUBLIC", "public") };
        for (int i = 0; i < 3; i++)
        {
            var (label, value) = pvOptions[i];
            bool active = profileViewAs == value;
            var seg = PanelObject(value + " Seg", pvGroup, active ? Accent : new Color(0, 0, 0, 0));
            Stretch(seg, new Vector2(i / 3f, 0f), new Vector2((i + 1) / 3f, 1f), new Vector2(3f, 3f), new Vector2(-3f, -3f));
            Round(seg);
            var t = TextObject("t", seg, label, 10, active ? BadgeInk : Muted, TextAnchor.MiddleCenter, monoFont);
            t.fontStyle = FontStyle.Bold;
            Stretch(t.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            string v = value;
            var btn = seg.gameObject.AddComponent<Button>();
            btn.onClick.AddListener(() => { profileViewAs = v; RenderMenu(); });
        }

        var note = TextObject("Note", panel,
            "Preview masks your own profile so you can see exactly what each audience sees.",
            9, new Color32(111, 134, 150, 255), TextAnchor.UpperLeft);
        Stretch(note.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(18f, 10f), new Vector2(-18f, -330f));
    }

    private void BuildPrivacySegment(RectTransform panel, string label, ProfileVisibility current,
        float y, Action<ProfileVisibility> onPick)
    {
        var lab = TextObject(label + " Label", panel, label, 10, Muted, TextAnchor.MiddleLeft, monoFont);
        lab.fontStyle = FontStyle.Bold;
        Stretch(lab.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(18f, y - 22f), new Vector2(-18f, y));

        var group = PanelObject(label + " Group", panel, new Color32(14, 28, 43, 255));
        Stretch(group, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(18f, y - 66f), new Vector2(-18f, y - 28f));
        Round(group);
        AddRoundedCardBorder(group, MenuB, 1f);

        var options = new[]
        {
            ("PUBLIC", ProfileVisibility.Public),
            ("FRIENDS", ProfileVisibility.Friends),
            ("PRIVATE", ProfileVisibility.Private),
        };
        for (int i = 0; i < 3; i++)
        {
            var (optLabel, value) = options[i];
            bool active = current == value;
            var seg = PanelObject(optLabel + " Seg", group, active ? Accent : new Color(0, 0, 0, 0));
            Stretch(seg, new Vector2(i / 3f, 0f), new Vector2((i + 1) / 3f, 1f), new Vector2(3f, 3f), new Vector2(-3f, -3f));
            Round(seg);
            var t = TextObject("t", seg, optLabel, 10, active ? BadgeInk : Muted, TextAnchor.MiddleCenter, monoFont);
            t.fontStyle = FontStyle.Bold;
            Stretch(t.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var v = value;
            var btn = seg.gameObject.AddComponent<Button>();
            btn.onClick.AddListener(() => onPick(v));
        }
    }

    // Amber lock card for sections hidden from the current preview audience.
    // (The handoff wants a dashed border; AddDashedBorder is still a stub in
    // this codebase, so a solid amber border at 50% carries the state instead.)
    private void BuildLockCard(RectTransform body, float y, float height, string what,
        string line1, string line2)
    {
        var card = PanelObject("Lock " + what, body, new Color(ProfileAmber.r, ProfileAmber.g, ProfileAmber.b, 0.06f));
        Stretch(card, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, y - height), new Vector2(0f, y));
        Round(card);
        AddRoundedCardBorder(card, new Color(ProfileAmber.r, ProfileAmber.g, ProfileAmber.b, 0.5f), 1.2f);

        // Mini CSS-style padlock: shackle (circle border) over a rounded body.
        var shackle = PanelObject("Shackle", card, new Color(0, 0, 0, 0));
        shackle.anchorMin = shackle.anchorMax = new Vector2(0f, 0.5f);
        shackle.pivot = new Vector2(0f, 0.5f);
        shackle.sizeDelta = new Vector2(12f, 12f);
        shackle.anchoredPosition = new Vector2(20f, 8f);
        RoundCircle(shackle);
        AddRoundedCardBorder(shackle, ProfileAmber, 1.6f);
        var lockBody = PanelObject("LockBody", card, ProfileAmber);
        lockBody.anchorMin = lockBody.anchorMax = new Vector2(0f, 0.5f);
        lockBody.pivot = new Vector2(0f, 0.5f);
        lockBody.sizeDelta = new Vector2(16f, 11f);
        lockBody.anchoredPosition = new Vector2(18f, -2f);
        Round(lockBody);

        var t1 = TextObject("L1", card, line1, 12, ProfileAmber, TextAnchor.MiddleLeft);
        t1.fontStyle = FontStyle.Bold;
        Stretch(t1.rectTransform, new Vector2(0f, string.IsNullOrEmpty(line2) ? 0f : 0.4f), Vector2.one,
            new Vector2(48f, 0f), new Vector2(-12f, 0f));
        if (!string.IsNullOrEmpty(line2))
        {
            var t2 = TextObject("L2", card, line2, 10, Muted, TextAnchor.UpperLeft, monoFont);
            Stretch(t2.rectTransform, Vector2.zero, new Vector2(1f, 0.42f), new Vector2(48f, 10f), new Vector2(-12f, 0f));
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // DEV / SOON marker — same chip language as the mode tiles (Versus A.I.).
    // Placed on anything in this stage that is provisional or not live yet.
    // ══════════════════════════════════════════════════════════════════════════

    private void AddProfileStatusChip(RectTransform parent, string label = "DEV",
        float xOffset = -6f, float yOffset = -5f)
    {
        bool dev = label == "DEV";
        var chip = PanelObject(label + " Chip", parent, new Color(0f, 0f, 0f, 0f));
        chip.anchorMin = chip.anchorMax = new Vector2(1f, 1f);
        chip.pivot = new Vector2(1f, 1f);
        chip.sizeDelta = new Vector2(12f + label.Length * 7f, 16f);
        chip.anchoredPosition = new Vector2(xOffset, yOffset);
        Round(chip);
        AddRoundedCardBorder(chip, dev ? Gold : Muted, 1f);
        var ct = TextObject("ChipText", chip, label, 9, dev ? Gold : Muted, TextAnchor.MiddleCenter, monoFont);
        ct.fontStyle = FontStyle.Bold;
        Stretch(ct.rectTransform, Vector2.zero, Vector2.one, new Vector2(2f, 0f), new Vector2(-2f, 0f));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Small pure helpers
    // ══════════════════════════════════════════════════════════════════════════

    private static string TopOwnLeaderId(StatsBucket b) =>
        (b?.byOwnLeader ?? new List<LeaderStat>())
        .Where(l => l != null && l.games > 0)
        .OrderByDescending(l => l.games)
        .FirstOrDefault()?.leaderId;

    private static int OwnLeaderGames(StatsBucket b, string leaderId) =>
        (b?.byOwnLeader ?? new List<LeaderStat>()).Find(l => l?.leaderId == leaderId)?.games ?? 0;

    // Win-rate coloring per the handoff: ≥55% green, 45–54% neutral, <45% warm.
    private static Color ProfileWrColor(float wr, int games)
    {
        if (games == 0) return new Color32(198, 211, 220, 255);
        if (wr >= 0.55f) return GoodGreen;
        if (wr >= 0.45f) return new Color32(198, 211, 220, 255);
        return ProfileWrLow;
    }

    private static (string name, Color color) ProfileTierFor(int elo)
    {
        var best = ProfileTiers[0];
        foreach (var t in ProfileTiers)
            if (elo >= t.minElo) best = t;
        return (best.name, best.color);
    }

    private static string MonthLabel(string ym)
    {
        // "2026-07" → "JUL 26". Malformed ids render as-is.
        if (!string.IsNullOrEmpty(ym) && ym.Length == 7 &&
            int.TryParse(ym.Substring(0, 4), out var yy) && int.TryParse(ym.Substring(5, 2), out var mm) &&
            mm >= 1 && mm <= 12)
        {
            string[] names = { "JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC" };
            return $"{names[mm - 1]} {yy % 100:00}";
        }
        return ym ?? "";
    }

    /// <summary>Provisional bounty / ELO / tier derived from the lifetime bucket.
    /// Deterministic + pure so it's unit-testable and trivially replaced by a
    /// server-side rating once ranked ships. Labeled PROVISIONAL in the UI.</summary>
    public static class ProfileDerived
    {
        public static (int elo, long bounty, string bountyLabel) From(StatsBucket b)
        {
            int wins = b?.wins ?? 0, losses = b?.losses ?? 0, streak = b?.bestWinStreak ?? 0;
            int elo = Mathf.Max(100, 1000 + wins * 16 - losses * 13);
            long bounty = wins * 30_000_000L + streak * 50_000_000L + (b?.games ?? 0) * 1_000_000L;
            return (elo, bounty, FormatBounty(bounty));
        }

        public static string FormatBounty(long bounty)
        {
            if (bounty >= 1_000_000_000L) return $"฿{bounty / 1_000_000_000.0:0.00}B";
            if (bounty >= 1_000_000L) return $"฿{bounty / 1_000_000.0:0}M";
            return bounty > 0 ? $"฿{bounty:N0}" : "฿0";
        }
    }
}
