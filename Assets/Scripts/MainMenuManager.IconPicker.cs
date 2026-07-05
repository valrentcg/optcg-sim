// One Piece TCG - Profile Icon Picker (design_handoff_profile_icon_picker).
//
// Partial of MainMenuManager: a searchable grid of circular face-crop icons
// (leader card art + face-data.json crops, the same technique as
// BuildLeaderBanner) with a current-pick chip and a Save action.
//
// ENTRY POINT (deviation from the handoff, per Nathan): the picker opens from
// the avatar next to your name INSIDE My Profile (BuildProfileHeader adds the
// button), not from the top-bar identity block — that click opens My Profile
// itself. Closing the picker therefore falls back to the profile stage
// (showingProfile stays true underneath).
//
// Persistence: AccountManager.ProfileIconId / SetProfileIconAsync — Cloud Save
// key "profileIcon" + PlayerPrefs cache, PlayerPrefs-only for guests.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public partial class MainMenuManager
{
    // ── Picker sub-state ─────────────────────────────────────────────────────
    private bool showingProfileIcon;
    private string stagedIconId;            // clicked but not yet saved
    private string iconSearchQuery = "";
    private bool iconSearchFocused;
    private InputField iconSearchField;     // re-focused after each rebuild while typing
    private bool profileIconRefreshKicked;  // one cloud refresh per menu lifetime

    // Async tile-art fill: thumbs decode a few per frame instead of all at once
    // (150+ leaders would hitch the render for seconds on first open).
    private readonly List<(Image img, RectTransform fallback, string id)> _iconArtPending
        = new List<(Image, RectTransform, string)>();
    private Coroutine _iconArtPump;

    // Curated non-leader character arts offered as icons alongside the leaders
    // (per Nathan). Face centers for the tricky ones are hand-tuned below —
    // face-data.json's heuristic guesses miss on busy or off-center art.
    private static readonly string[] ProfileIconExtras =
    {
        "OP05-047",  // Basil Hawkins
        "OP05-054",  // Monkey D. Garp
        "OP08-028",  // Nekomamushi
        "OP08-064",  // Charlotte Cracker
        "OP15-109",  // Nico Robin
        "OP15-091",  // Margarita
        "OP15-090",  // Perona
        "OP15-083",  // Spoils
        "OP15-081",  // Sanji
        "OP15-070",  // Fuza
        "OP15-039",  // Rebecca
        "OP14-081",  // Spider Mice
        "PRB02-016", // Otama
    };

    // id → (fx, fy, zoom) card-fraction face centers that override face-data.
    // zoom (z) is the crop window's height as a fraction of the card; 0 means
    // "use the default". Tuned by eye against the actual art.
    private static readonly Dictionary<string, Vector3> IconFaceOverrides = new Dictionary<string, Vector3>
    {
        { "OP15-109", new Vector3(0.52f, 0.27f, 0f)    },  // Robin — under the hat
        { "OP15-090", new Vector3(0.55f, 0.29f, 0f)    },  // Perona — upside-down pose
        { "OP15-070", new Vector3(0.53f, 0.26f, 0.34f) },  // Fuza — tight window clears the top frame
        { "OP15-039", new Vector3(0.54f, 0.25f, 0.32f) },  // Rebecca — down-left, clear of the right frame
        { "OP14-081", new Vector3(0.60f, 0.40f, 0f)    },  // Spider Mice — the mice pair
    };

    private const float IconTileW = 116f;   // cell width  (portrait circle = 96)
    private const float IconTileH = 162f;   // circle + name + card id + selected label + row gap
    private static readonly Color IconSubInk = new Color32(111, 134, 150, 255);
    private static readonly Color IconNameInk = new Color32(201, 214, 224, 255);

    // ══════════════════════════════════════════════════════════════════════════
    // Entry / exit / persistence plumbing
    // ══════════════════════════════════════════════════════════════════════════

    // Committed icon: server value once loaded, else the instant local cache.
    private string EffectiveProfileIconId() =>
        AccountManager.ProfileIconId ?? AccountManager.CachedProfileIconId;

    // Fire-and-forget cloud refresh so the top bar corrects itself shortly
    // after boot if another device changed the icon. Repaints on completion.
    private async void KickProfileIconCloudRefresh()
    {
        if (profileIconRefreshKicked) return;
        profileIconRefreshKicked = true;
        string before = EffectiveProfileIconId();
        try { await AccountManager.EnsureProfileIconLoadedAsync(); }
        catch (Exception ex) { Debug.LogWarning($"Profile icon refresh failed: {ex.Message}"); }
        if (this == null || menuRoot == null) return;
        if (EffectiveProfileIconId() != before) RenderMenu();
    }

    private void OpenProfileIconPicker()
    {
        stagedIconId = EffectiveProfileIconId();
        iconSearchQuery = "";
        iconSearchFocused = false;
        showingProfileIcon = true;
        RenderMenu();
    }

    private void CloseProfileIconPicker()
    {
        showingProfileIcon = false;   // discard stagedIconId; committed value stands
        iconSearchFocused = false;
        RenderMenu();
    }

    private void SaveProfileIconClicked()
    {
        _ = AccountManager.SetProfileIconAsync(stagedIconId);   // local commit is synchronous
        showingProfileIcon = false;
        iconSearchFocused = false;
        RenderMenu();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Stage
    // ══════════════════════════════════════════════════════════════════════════

    private void BuildProfileIconStage(RectTransform stage)
    {
        EnsureMenuCardLibrary();
        EnsureMenuFaceData();
        _iconArtPending.Clear();

        var leaders = (menuCardLibrary ?? new CardRec[0])
            .Where(c => c != null && c.type == "Leader" && !string.IsNullOrEmpty(c.id))
            .ToList();
        foreach (var extraId in ProfileIconExtras)
        {
            var rec = MenuCard(extraId);
            if (rec != null && leaders.All(l => l.id != extraId)) leaders.Add(rec);
        }
        leaders = leaders.OrderBy(c => c.name, StringComparer.OrdinalIgnoreCase).ToList();

        string q = (iconSearchQuery ?? "").Trim().ToLowerInvariant();
        var shown = q.Length == 0
            ? leaders
            : leaders.Where(c => (c.name ?? "").ToLowerInvariant().Contains(q)
                              || c.id.ToLowerInvariant().Contains(q)).ToList();

        // ── Header row (~78px): back + title + subtitle | current chip + save ─
        var header = PanelObject("IP Header", stage, new Color(0, 0, 0, 0));
        Stretch(header, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -78f), Vector2.zero);

        var back = TextObject("Back", header, "‹ BACK TO PROFILE", 12, Accent, TextAnchor.UpperLeft, monoFont);
        back.fontStyle = FontStyle.Bold;
        back.raycastTarget = true;
        Stretch(back.rectTransform, new Vector2(0f, 1f), new Vector2(0.3f, 1f), new Vector2(4f, -22f), Vector2.zero);
        var backBtn = back.gameObject.AddComponent<Button>();
        backBtn.transition = Selectable.Transition.None;
        backBtn.onClick.AddListener(CloseProfileIconPicker);

        var title = TextObject("Title", header, "Profile Icon", 26, Ink, TextAnchor.UpperLeft);
        title.fontStyle = FontStyle.Bold;
        Stretch(title.rectTransform, new Vector2(0f, 0f), new Vector2(0.4f, 1f), new Vector2(4f, 16f), new Vector2(0f, -24f));

        var sub = TextObject("Sub", header, $"SELECT YOUR CLIENT AVATAR  ·  {leaders.Count} UNLOCKED",
            11, Muted, TextAnchor.LowerLeft, monoFont);
        Stretch(sub.rectTransform, Vector2.zero, new Vector2(0.5f, 0.3f), new Vector2(4f, 0f), Vector2.zero);

        // Current-pick chip
        var chip = PanelObject("Current Chip", header, RowBg);
        chip.anchorMin = new Vector2(1f, 0.5f); chip.anchorMax = new Vector2(1f, 0.5f);
        chip.pivot = new Vector2(1f, 0.5f);
        chip.sizeDelta = new Vector2(240f, 56f);
        chip.anchoredPosition = new Vector2(-166f, 0f);
        RoundBig(chip);
        AddRoundedCardBorder(chip, MenuB, 1f);
        BuildCircleFaceIcon(chip, stagedIconId, 40f, new Vector2(8f, 0f));
        var curLabel = TextObject("Cur", chip, "CURRENT", 9, IconSubInk, TextAnchor.LowerLeft, monoFont);
        Stretch(curLabel.rectTransform, new Vector2(0f, 0.5f), Vector2.one, new Vector2(58f, 2f), new Vector2(-8f, -8f));
        var curName = TextObject("CurName", chip, IconDisplayName(stagedIconId), 14, Ink, TextAnchor.UpperLeft);
        curName.fontStyle = FontStyle.Bold;
        Stretch(curName.rectTransform, Vector2.zero, new Vector2(1f, 0.5f), new Vector2(58f, 6f), new Vector2(-8f, 0f));

        // Save button
        var save = PanelObject("Save", header, Accent);
        save.anchorMin = new Vector2(1f, 0.5f); save.anchorMax = new Vector2(1f, 0.5f);
        save.pivot = new Vector2(1f, 0.5f);
        save.sizeDelta = new Vector2(150f, 42f);
        save.anchoredPosition = new Vector2(0f, 0f);
        Round(save);
        var saveText = TextObject("t", save, "Save Changes", 13, BadgeInk, TextAnchor.MiddleCenter);
        saveText.fontStyle = FontStyle.Bold;
        Stretch(saveText.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var saveBtn = save.gameObject.AddComponent<Button>();
        saveBtn.onClick.AddListener(SaveProfileIconClicked);

        // ── Search row (~42px) ───────────────────────────────────────────────
        var searchRow = PanelObject("IP Search Row", stage, new Color(0, 0, 0, 0));
        Stretch(searchRow, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -134f), new Vector2(0f, -92f));

        var input = MakeInput(searchRow, "Search characters…", iconSearchQuery,
            v => { iconSearchQuery = v; iconSearchFocused = true; RenderMenu(); }, null);
        Stretch(input, new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(340f, 0f));
        iconSearchField = input.GetComponent<InputField>();
        // Magnifier glyph inside the field; indent the text past it.
        var glyph = TextObject("Glyph", input, "⌕", 16, IconSubInk, TextAnchor.MiddleLeft, monoFont);
        Stretch(glyph.rectTransform, Vector2.zero, Vector2.one, new Vector2(10f, 0f), Vector2.zero);
        foreach (Transform child in input)
        {
            var t = child.GetComponent<Text>();
            if (t != null && t.name != "Glyph")
                t.rectTransform.offsetMin = new Vector2(30f, t.rectTransform.offsetMin.y);
        }

        var count = TextObject("Count", searchRow,
            q.Length > 0 ? $"{shown.Count} OF {leaders.Count} SHOWN" : $"{leaders.Count} CHARACTERS",
            11, Muted, TextAnchor.MiddleLeft, monoFont);
        Stretch(count.rectTransform, new Vector2(0f, 0f), Vector2.one, new Vector2(356f, 0f), Vector2.zero);

        // ── Grid ─────────────────────────────────────────────────────────────
        var gridArea = PanelObject("IP Grid", stage, new Color(0, 0, 0, 0));
        Stretch(gridArea, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0f, -148f));

        if (shown.Count == 0)
        {
            var bigGlyph = TextObject("NoRes Glyph", gridArea, "⌕", 64,
                new Color(IconSubInk.r, IconSubInk.g, IconSubInk.b, 0.35f), TextAnchor.LowerCenter, monoFont);
            Stretch(bigGlyph.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.75f), Vector2.zero, Vector2.zero);
            var noRes = TextObject("NoRes", gridArea, $"No characters match \"{iconSearchQuery.Trim()}\".",
                13, IconSubInk, TextAnchor.UpperCenter, monoFont);
            Stretch(noRes.rectTransform, new Vector2(0f, 0.3f), new Vector2(1f, 0.5f), Vector2.zero, Vector2.zero);
        }
        else
        {
            int cols = Mathf.Max(1, Mathf.FloorToInt((MatchStageW + 8f) / (IconTileW + 8f)));
            int rows = (shown.Count + cols - 1) / cols;
            var content = MakeMenuScroll(gridArea, rows * IconTileH + 12f);
            for (int i = 0; i < shown.Count; i++)
            {
                int r = i / cols, c = i % cols;
                float x = c * (IconTileW + 8f);
                float y = -(r * IconTileH) - 6f;
                BuildIconTile(content, shown[i], x, y);
            }
        }

        // Typing rebuilt the stage — put the caret back in the fresh field.
        if (iconSearchFocused && iconSearchField != null)
        {
            iconSearchField.Select();
            iconSearchField.ActivateInputField();
            iconSearchField.caretPosition = (iconSearchQuery ?? "").Length;
        }

        // Pump the pending tile art a few per frame.
        if (_iconArtPump != null) StopCoroutine(_iconArtPump);
        if (_iconArtPending.Count > 0) _iconArtPump = StartCoroutine(PumpIconArt());
    }

    private void BuildIconTile(RectTransform content, CardRec rec, float x, float y)
    {
        bool selected = rec.id == stagedIconId;

        var cell = PanelObject("Icon " + rec.id, content, new Color(0, 0, 0, 0));
        cell.anchorMin = cell.anchorMax = new Vector2(0f, 1f);
        cell.pivot = new Vector2(0f, 1f);
        cell.sizeDelta = new Vector2(IconTileW, IconTileH);
        cell.anchoredPosition = new Vector2(x, y);


        // Portrait circle: a circle-sprite Image acting as a Mask, art inside.
        var circle = PanelObject("Circle", cell, new Color32(11, 20, 32, 255));
        circle.anchorMin = circle.anchorMax = new Vector2(0.5f, 1f);
        circle.pivot = new Vector2(0.5f, 1f);
        circle.sizeDelta = new Vector2(96f, 96f);
        circle.anchoredPosition = new Vector2(0f, -6f);
        SetAvatarCircle(circle, GetAvatarCircleSprite());
        var mask = circle.gameObject.AddComponent<Mask>();
        mask.showMaskGraphic = true;   // keeps the dark base under transparent art edges
        BuildCircleFaceArt(circle, rec, 96f, deferArt: true);   // grid tiles pump a few per frame
        AddAvatarEdgeRing(circle, ZoneBorder);
        // THE gold glow: the same animated "ethereal mist" rim shader the
        // selected starter-deck hex and card previews use, circle-shaped
        // (UI/CircleRimGlow). Parented to the cell so the mask doesn't clip it.
        if (selected) AddCircleSelectionGlow(cell, circle);

        var name = TextObject("Name", cell, (rec.name ?? rec.id).ToUpperInvariant(), 10,
            selected ? Ink : IconNameInk, TextAnchor.UpperCenter, monoFont);
        Stretch(name.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f),
            new Vector2(0f, 0f), new Vector2(0f, -112f));

        // Card the art was pulled from, in smaller dimmer type under the name.
        var idLabel = TextObject("CardId", cell, rec.id, 8, IconSubInk, TextAnchor.UpperCenter, monoFont);
        Stretch(idLabel.rectTransform, Vector2.zero, Vector2.one, new Vector2(0f, 0f), new Vector2(0f, -126f));

        if (selected)
        {
            var sel = TextObject("Selected", cell, "SELECTED", 8, Accent, TextAnchor.UpperCenter, monoFont);
            sel.fontStyle = FontStyle.Bold;
            Stretch(sel.rectTransform, Vector2.zero, Vector2.one, new Vector2(0f, 0f), new Vector2(0f, -138f));
        }

        var btn = cell.gameObject.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        string id = rec.id;
        btn.onClick.AddListener(() => { stagedIconId = id; RenderMenu(); });
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Circular face-crop rendering (shared: top bar, profile header, chip, tiles)
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>Left-center anchored circular avatar for panels (top bar,
    /// current-pick chip, profile header). Falls back to a steel circle with
    /// the player's/character's initial when no icon or art exists.</summary>
    private void BuildCircleFaceIcon(RectTransform parent, string cardId, float size, Vector2 pos)
    {
        var circle = PanelObject("Avatar Circle", parent, new Color32(11, 20, 32, 255));
        circle.anchorMin = circle.anchorMax = new Vector2(0f, 0.5f);
        circle.pivot = new Vector2(0f, 0.5f);
        circle.sizeDelta = new Vector2(size, size);
        circle.anchoredPosition = pos;
        SetAvatarCircle(circle, GetAvatarCircleSprite());
        var mask = circle.gameObject.AddComponent<Mask>();
        mask.showMaskGraphic = true;

        // The card library parses lazily; a fresh menu instance (e.g. back from
        // the deck builder) hasn't loaded it yet when the top bar builds, which
        // used to silently drop the icon back to the initial fallback.
        if (!string.IsNullOrEmpty(cardId)) { EnsureMenuCardLibrary(); EnsureMenuFaceData(); }

        var rec = MenuCard(cardId);
        if (rec != null)
        {
            BuildCircleFaceArt(circle, rec, size);
            AddAvatarEdgeRing(circle, ZoneBorder);   // border + covers the mask's clip edge
            return;
        }

        // Fallback: steel gradient + initial (player initial when no icon set).
        string who = AccountManager.CurrentUsername ?? AccountManager.GuestDisplayName ?? "?";
        var init = TextObject("Init", circle, who.Substring(0, 1).ToUpperInvariant(),
            Mathf.RoundToInt(size * 0.45f), new Color(0.914f, 0.941f, 0.969f, 0.94f), TextAnchor.MiddleCenter);
        init.fontStyle = FontStyle.Bold;
        AddAvatarEdgeRing(circle, ZoneBorder);
        Stretch(init.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
    }

    // Art inside an existing circle mask: same eye-crop technique as
    // BuildLeaderBanner, but a square window centered on the face. Art loads
    // through the shared thumb cache; tiles defer decode to PumpIconArt.
    // deferArt: grid tiles queue their decode for PumpIconArt (150+ at once
    // would hitch); single avatars (top bar, profile header, current-pick chip)
    // decode synchronously — there is no pump running outside the picker stage,
    // so a deferred request there would never load and the initial-letter
    // placeholder would stick forever (the original bug).
    private void BuildCircleFaceArt(RectTransform circle, CardRec rec, float size, bool deferArt = false)
    {
        var art = PanelObject("Art", circle, Color.white);
        var ai = art.GetComponent<Image>();
        ai.raycastTarget = false;

        // Face-crop window: ~42% of the card's height across the circle. Wider
        // than the first pass (0.36) so faces sit smaller inside the circle and
        // the round mask stops shaving hat-tops and chins.
        const float VIS_H = 0.42f;
        float visH = VIS_H;
        float fy = 0.20f, fx = 0.5f;
        if (IconFaceOverrides.TryGetValue(rec.id, out var over))
        {
            fx = over.x; fy = over.y;
            if (over.z > 0f) visH = over.z;
        }
        else
        {
            // face-data 'y' is the EYE line; sit the window slightly below it.
            if (menuFaceY != null && menuFaceY.TryGetValue(rec.id, out var my)) fy = Mathf.Clamp(my, 0.08f, 0.5f) + 0.04f;
            if (menuFaceX != null && menuFaceX.TryGetValue(rec.id, out var mx)) fx = Mathf.Clamp(mx, 0.15f, 0.85f);
        }

        const float ASPECT = 0.716f;   // card w/h
        float artH = size / visH;
        float artW = artH * ASPECT;
        float visWFrac = size / artW;

        // Clamp the window inside the card's ILLUSTRATION safe area so the crop
        // can never slide into the printed frame, cost badges, or text box.
        const float SAFE_L = 0.075f, SAFE_R = 0.925f, SAFE_T = 0.055f, SAFE_B = 0.965f;
        float cxFrac = Mathf.Clamp(fx, SAFE_L + visWFrac * 0.5f, SAFE_R - visWFrac * 0.5f);
        float cyFrac = Mathf.Clamp(fy, SAFE_T + visH * 0.5f, SAFE_B - visH * 0.5f);

        art.anchorMin = art.anchorMax = new Vector2(0.5f, 0.5f);
        art.pivot = new Vector2(0.5f, 0.5f);
        art.sizeDelta = new Vector2(artW, artH);
        art.anchoredPosition = new Vector2(-(cxFrac - 0.5f) * artW, (cyFrac - 0.5f) * artH);

        var cached = LoadThumbSpriteIfCached(rec.id);
        if (cached == null && !deferArt)
            cached = LoadThumbSprite(rec.id);   // one sync decode — cheap for a single avatar
        if (cached != null) { ai.sprite = cached; ai.type = Image.Type.Simple; ai.preserveAspect = false; }
        else
        {
            // Neutral fill until the pump decodes it (or immediately for the few
            // avatars outside the picker grid, where a sync decode is fine).
            ai.color = new Color32(23, 34, 50, 255);
            var fallbackInit = TextObject("Init", circle,
                string.IsNullOrEmpty(rec.name) ? "?" : rec.name.Substring(0, 1).ToUpperInvariant(),
                Mathf.RoundToInt(size * 0.33f), new Color(0.914f, 0.941f, 0.969f, 0.5f), TextAnchor.MiddleCenter);
            fallbackInit.fontStyle = FontStyle.Bold;
            Stretch(fallbackInit.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            _iconArtPending.Add((ai, fallbackInit.rectTransform, rec.id));
        }
    }

    private Sprite LoadThumbSpriteIfCached(string id) =>
        id != null && _thumbArtCache.TryGetValue(id, out var sp) ? sp : null;

    // Decodes a few pending tile thumbs per frame (LoadThumbSprite caches, so
    // reopening the picker or re-rendering is instant for anything already seen).
    private IEnumerator PumpIconArt()
    {
        const int PerFrame = 3;
        while (_iconArtPending.Count > 0)
        {
            int n = Mathf.Min(PerFrame, _iconArtPending.Count);
            for (int k = 0; k < n; k++)
            {
                var (img, fallback, id) = _iconArtPending[0];
                _iconArtPending.RemoveAt(0);
                if (img == null) continue;   // stage rebuilt since
                var sp = LoadThumbSprite(id);
                if (sp != null && img != null)
                {
                    img.sprite = sp; img.type = Image.Type.Simple; img.color = Color.white;
                    if (fallback != null) Destroy(fallback.gameObject);
                }
            }
            yield return null;
        }
        _iconArtPump = null;
    }

    // ── Selection glow: UI/CircleRimGlow (circle twin of UI/HexRimGlow) ──────
    // Same driver/params pattern as DeckBuilderManager.AddHexSelectionGlow so
    // the picker's selected tile glows exactly like the selected starter hex.
    private static Shader _circleGlowShader;

    private sealed class CircleRimGlowDriver : MonoBehaviour
    {
        private static readonly int IntensityID = Shader.PropertyToID("_Intensity");
        private Material mat;
        private RectTransform rt;
        private float expand;
        private float current;
        public void Init(RawImage image, float expandFraction)
        {
            rt = image.rectTransform;
            mat = image.material;
            expand = expandFraction;
            current = 0f;
            if (mat != null) mat.SetFloat(IntensityID, 0f);
        }
        private void Update()
        {
            if (mat == null || rt == null) return;
            Vector2 g = rt.rect.size;
            if (g.x > 1f && g.y > 1f)
            {
                Vector2 circle = g / (1f + 2f * expand);
                float mn = circle.y;
                mat.SetVector("_GlowSize", new Vector4(g.x, g.y, 0f, 0f));
                mat.SetVector("_CardSize", new Vector4(circle.x, circle.y, 0f, 0f));
                mat.SetFloat("_BleedPx", mn * 0.05f);
                mat.SetFloat("_GlowWidthPx", mn * 0.11f);
                mat.SetFloat("_CoreWidthPx", Mathf.Max(1.5f, mn * 0.025f));
                mat.SetFloat("_WispPx", mn * 0.08f);
            }
            current = Mathf.MoveTowards(current, 1f, 6.5f * Time.unscaledDeltaTime);
            mat.SetFloat(IntensityID, current);
        }
        private void OnDestroy() { if (mat != null) Destroy(mat); }
    }

    // Attaches the rim glow around `circle`, parented to `host` (outside the
    // circle's Mask, which would otherwise clip the glow to the portrait).
    private void AddCircleSelectionGlow(RectTransform host, RectTransform circle)
    {
        if (_circleGlowShader == null) _circleGlowShader = Shader.Find("UI/CircleRimGlow");
        if (_circleGlowShader == null) return;   // shader missing — degrade gracefully

        const float glowExpand = 0.34f;
        float d = circle.sizeDelta.x;
        var rimGo = new GameObject("SelIconGlow");
        rimGo.transform.SetParent(host, false);
        var rimRt = rimGo.AddComponent<RectTransform>();
        rimRt.anchorMin = circle.anchorMin;
        rimRt.anchorMax = circle.anchorMax;
        // Centre the (larger) glow quad on the circle's CENTRE — copying the
        // circle's own top pivot made the bigger quad hang low.
        rimRt.pivot = new Vector2(0.5f, 0.5f);
        rimRt.sizeDelta = new Vector2(d * (1f + 2f * glowExpand), d * (1f + 2f * glowExpand));
        rimRt.anchoredPosition = circle.anchoredPosition + new Vector2(
            (0.5f - circle.pivot.x) * d, (0.5f - circle.pivot.y) * d);
        rimRt.SetAsFirstSibling();   // behind the portrait circle
        var rimImg = rimGo.AddComponent<RawImage>();
        rimImg.texture = Texture2D.whiteTexture;
        rimImg.raycastTarget = false;
        var m = new Material(_circleGlowShader);
        m.SetColor("_GlowColor", new Color(1.00f, 0.59f, 0.10f, 1f) * 1.28f);
        m.SetColor("_CoreColor", new Color(1.00f, 0.80f, 0.38f, 1f) * 1.12f);
        m.SetColor("_OuterColor", new Color(0.82f, 0.29f, 0.04f, 1f) * 1.05f);
        m.SetFloat("_Speed", 0.55f);
        m.SetFloat("_NoiseScale", 3.0f);
        m.SetFloat("_Pulse", 0.22f);
        rimImg.material = m;
        rimGo.AddComponent<CircleRimGlowDriver>().Init(rimImg, glowExpand);
    }

    // ── High-resolution circle sprites for avatars ───────────────────────────
    // The menu's shared GetCircleSprite() is 48x48 (made for tiny status dots);
    // stretched over a 96px portrait and used as a stencil mask its edge clips
    // into visible stair-steps. Avatars use these 256px anti-aliased sprites
    // instead, plus a ring OVERLAY on top of the art that hides the stencil
    // mask's hard cutoff behind a smooth anti-aliased edge.
    private Sprite _avatarCircleSprite;
    private Sprite _avatarRingSprite;

    private Sprite GetAvatarCircleSprite()
    {
        if (_avatarCircleSprite != null) return _avatarCircleSprite;
        const int S = 256;
        float rad = S / 2f;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        var px = new Color32[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = x + 0.5f - rad, dy = y + 0.5f - rad;
                float a = Mathf.Clamp01(rad - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                px[y * S + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        tex.SetPixels32(px); tex.Apply(false, true);
        _avatarCircleSprite = Sprite.Create(tex, new Rect(0, 0, S, S),
            new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        return _avatarCircleSprite;
    }

    // Donut outline: outer edge feathers outward over the mask's clip line,
    // inner edge feathers into the art — jaggies vanish under it.
    private Sprite GetAvatarRingSprite()
    {
        if (_avatarRingSprite != null) return _avatarRingSprite;
        const int S = 256;
        const float THICK = 7f;        // ~2.6px at a 96px portrait
        float rad = S / 2f;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        var px = new Color32[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = x + 0.5f - rad, dy = y + 0.5f - rad;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float outer = Mathf.Clamp01(rad - d + 0.5f);
                float inner = Mathf.Clamp01(d - (rad - THICK) + 0.5f);
                px[y * S + x] = new Color32(255, 255, 255,
                    (byte)Mathf.RoundToInt(outer * inner * 255f));
            }
        tex.SetPixels32(px); tex.Apply(false, true);
        _avatarRingSprite = Sprite.Create(tex, new Rect(0, 0, S, S),
            new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        return _avatarRingSprite;
    }

    private void SetAvatarCircle(RectTransform rt, Sprite sp)
    {
        var img = rt.GetComponent<Image>();
        img.sprite = sp;
        img.type = Image.Type.Simple;
        img.raycastTarget = false;
    }

    // Smooth ring drawn OVER the art: visual border + anti-aliasing cover.
    private void AddAvatarEdgeRing(RectTransform circle, Color color)
    {
        var edge = PanelObject("Edge Ring", circle, color);
        Stretch(edge, Vector2.zero, Vector2.one, new Vector2(-1f, -1f), new Vector2(1f, 1f));
        SetAvatarCircle(edge, GetAvatarRingSprite());
        edge.SetAsLastSibling();
    }

    private string IconDisplayName(string cardId)
    {
        if (string.IsNullOrEmpty(cardId)) return "Default";
        var rec = MenuCard(cardId);
        return rec?.name ?? cardId;
    }
}
