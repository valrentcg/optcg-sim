// DON!! deck builder — the player's personal DON!! art layout.
//
// Reached from a small corner button on the Select screen's centre column, so the hex roster
// keeps its full size (the cluster auto-sizes to the panel; carving a box out of it would shrink
// every hex). The button opens a full overlay, same pattern as the deck import modal.
//
// Three modes, mirroring DonDeckSettings:
//   DEFAULT  stock art on every DON
//   UNIFORM  one chosen art on all of them
//   CUSTOM   each of the 10 slots individually
//
// Art comes from DonArtCatalog, which discovers StreamingAssets/Cards/Don/*.png — dropping a new
// file in that folder is all it takes to add a DON, no code change.

using System.IO;
using UnityEngine;
using UnityEngine.UI;

public partial class DeckBuilderManager
{
    private RectTransform donOverlay;
    private int donEditingSlot = -1;             // slot awaiting an art pick in CUSTOM mode
    private readonly System.Collections.Generic.Dictionary<string, Sprite> donArtThumbs =
        new System.Collections.Generic.Dictionary<string, Sprite>();

    // ── Entry point: a corner chip on the centre column ──────────────────────
    private void BuildDonDeckButton(RectTransform centerPanel)
    {
        var btn = Panel("DonDeckBtn", centerPanel, PanelFill);
        btn.anchorMin = btn.anchorMax = new Vector2(1f, 0f);
        btn.pivot = new Vector2(1f, 0f);
        btn.sizeDelta = new Vector2(150f, 40f);
        btn.anchoredPosition = new Vector2(-10f, 10f);
        Round(btn); AddBorder(btn, Accent, 1.2f);

        var label = Text_("L", btn, "DON!! DECK", 12, Ink, TextAnchor.MiddleCenter);
        label.fontStyle = FontStyle.Bold;
        Stretch(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(8f, 14f), new Vector2(-8f, 0f));

        var sub = Text_("S", btn, DonModeSummary(), 9, Accent, TextAnchor.MiddleCenter);
        Stretch(sub.rectTransform, Vector2.zero, Vector2.one, new Vector2(8f, 0f), new Vector2(-8f, -20f));

        btn.gameObject.AddComponent<Button>().onClick.AddListener(OpenDonDeckPanel);
    }

    private static string DonModeSummary()
    {
        switch (DonDeckSettings.CurrentMode)
        {
            case DonDeckSettings.Mode.Uniform:
                return DonArtCatalog.DisplayFor(DonDeckSettings.UniformArt).ToUpperInvariant();
            case DonDeckSettings.Mode.Custom: return "CUSTOM";
            default: return "DEFAULT";
        }
    }

    // ── The overlay ──────────────────────────────────────────────────────────
    private void OpenDonDeckPanel()
    {
        DonArtCatalog.Rescan();                  // pick up art added since the app started
        donEditingSlot = -1;
        RenderDonDeckPanel();
    }

    private void CloseDonDeckPanel()
    {
        if (donOverlay != null) Destroy(donOverlay.gameObject);
        donOverlay = null;
        donEditingSlot = -1;
        Render();                                // refresh the corner chip's summary line
    }

    private void RenderDonDeckPanel()
    {
        if (donOverlay != null) Destroy(donOverlay.gameObject);

        donOverlay = Panel("Don Overlay", canvas.transform, new Color(0f, 0f, 0f, 0.62f));
        Stretch(donOverlay, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        donOverlay.SetAsLastSibling();
        donOverlay.gameObject.AddComponent<Button>().onClick.AddListener(CloseDonDeckPanel);

        var box = Panel("Box", donOverlay, PanelFill);
        box.anchorMin = box.anchorMax = new Vector2(0.5f, 0.5f);
        box.pivot = new Vector2(0.5f, 0.5f);
        box.sizeDelta = new Vector2(780f, 600f);
        RoundBig(box); AddBorder(box, Accent, 1.4f);
        box.gameObject.AddComponent<Button>();   // swallow clicks so the backdrop doesn't close it

        var title = Text_("Title", box, "DON!! DECK", 16, Ink, TextAnchor.UpperLeft);
        title.fontStyle = FontStyle.Bold;
        Stretch(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(20f, -38f), new Vector2(-20f, -12f));

        var hint = Text_("Hint", box,
            DonArtCatalog.Count > 1
                ? "Your DON!! art travels with you — your opponent sees it too. Drop new art into "
                  + "StreamingAssets/Cards/Don/ to add more."
                : "Only the stock DON!! art is installed. Drop .png files into "
                  + "StreamingAssets/Cards/Don/ and reopen this panel to choose between them.",
            11, Muted, TextAnchor.UpperLeft);
        hint.horizontalOverflow = HorizontalWrapMode.Wrap;
        Stretch(hint.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(20f, -80f), new Vector2(-20f, -42f));

        BuildDonModeRow(box);

        switch (DonDeckSettings.CurrentMode)
        {
            case DonDeckSettings.Mode.Uniform:
                BuildDonArtGrid(box, DonDeckSettings.UniformArt, id =>
                {
                    DonDeckSettings.UniformArt = id;
                    RenderDonDeckPanel();
                });
                break;

            case DonDeckSettings.Mode.Custom:
                BuildDonSlotRow(box);
                if (donEditingSlot >= 0)
                    BuildDonArtGrid(box, DonDeckSettings.Slots[donEditingSlot], id =>
                    {
                        DonDeckSettings.SetSlot(donEditingSlot, id);
                        RenderDonDeckPanel();
                    });
                else
                    CenteredNote(box, "Pick a slot above, then choose its art.");
                break;

            default:
                CenteredNote(box, "Every DON!! uses the stock art.");
                break;
        }

        var done = Panel("Done", box, Accent);
        done.anchorMin = done.anchorMax = new Vector2(1f, 0f);
        done.pivot = new Vector2(1f, 0f);
        done.sizeDelta = new Vector2(120f, 34f);
        done.anchoredPosition = new Vector2(-20f, 16f);
        Round(done);
        var dt = Text_("T", done, "DONE", 12, MenuBg, TextAnchor.MiddleCenter);
        dt.fontStyle = FontStyle.Bold;
        Stretch(dt.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        done.gameObject.AddComponent<Button>().onClick.AddListener(CloseDonDeckPanel);
    }

    private void CenteredNote(RectTransform box, string message)
    {
        var t = Text_("Note", box, message, 12, Muted, TextAnchor.MiddleCenter);
        Stretch(t.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f),
            new Vector2(20f, 60f), new Vector2(-20f, -160f));
    }

    // ── Mode selector ────────────────────────────────────────────────────────
    private void BuildDonModeRow(RectTransform box)
    {
        (DonDeckSettings.Mode mode, string label)[] modes =
        {
            (DonDeckSettings.Mode.Default, "DEFAULT"),
            (DonDeckSettings.Mode.Uniform, "ALL ONE ART"),
            (DonDeckSettings.Mode.Custom,  "PER DON!!"),
        };

        const float w = 220f, h = 34f, gap = 10f;
        float totalW = modes.Length * w + (modes.Length - 1) * gap;

        for (int i = 0; i < modes.Length; i++)
        {
            bool on = DonDeckSettings.CurrentMode == modes[i].mode;
            var chip = Panel("Mode" + i, box, on ? Accent : new Color(1f, 1f, 1f, 0.05f));
            chip.anchorMin = chip.anchorMax = new Vector2(0.5f, 1f);
            chip.pivot = new Vector2(0.5f, 1f);
            chip.sizeDelta = new Vector2(w, h);
            chip.anchoredPosition = new Vector2(-totalW / 2f + w / 2f + i * (w + gap), -92f);
            Round(chip); AddBorder(chip, on ? Accent : MenuB, 1f);

            var t = Text_("T", chip, modes[i].label, 12, on ? MenuBg : Ink, TextAnchor.MiddleCenter);
            t.fontStyle = FontStyle.Bold;
            Stretch(t.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            var picked = modes[i].mode;
            chip.gameObject.AddComponent<Button>().onClick.AddListener(() =>
            {
                DonDeckSettings.CurrentMode = picked;
                donEditingSlot = -1;
                RenderDonDeckPanel();
            });
        }
    }

    // ── CUSTOM: the 10 DON!! slots ───────────────────────────────────────────
    private void BuildDonSlotRow(RectTransform box)
    {
        int n = DonDeckSettings.MaxSlots;
        const float cw = 58f, ch = 82f, gap = 6f;
        float totalW = n * cw + (n - 1) * gap;
        var slots = DonDeckSettings.Slots;

        for (int i = 0; i < n; i++)
        {
            bool sel = donEditingSlot == i;
            var cell = Panel("Slot" + i, box, new Color(1f, 1f, 1f, 0.04f));
            cell.anchorMin = cell.anchorMax = new Vector2(0.5f, 1f);
            cell.pivot = new Vector2(0.5f, 1f);
            cell.sizeDelta = new Vector2(cw, ch);
            cell.anchoredPosition = new Vector2(-totalW / 2f + cw / 2f + i * (cw + gap), -142f);
            Round(cell); AddBorder(cell, sel ? Accent : MenuB, sel ? 1.6f : 1f);

            var art = DonThumb(slots[i]);
            if (art != null)
            {
                var img = Panel("Art", cell, Color.white).GetComponent<Image>();
                img.sprite = art; img.preserveAspect = true; img.raycastTarget = false;
                Stretch(img.rectTransform, Vector2.zero, Vector2.one, new Vector2(3f, 15f), new Vector2(-3f, -3f));

                var hov = cell.gameObject.AddComponent<DonArtHover>();
                hov.mgr = this; hov.art = art;
            }

            var num = Text_("N", cell, (i + 1).ToString(), 9, sel ? Accent : Muted, TextAnchor.LowerCenter);
            Stretch(num.rectTransform, Vector2.zero, Vector2.one, new Vector2(0f, 2f), new Vector2(0f, 0f));

            int idx = i;
            cell.gameObject.AddComponent<Button>().onClick.AddListener(() =>
            {
                donEditingSlot = donEditingSlot == idx ? -1 : idx;
                RenderDonDeckPanel();
            });
        }
    }

    // ── Art chooser ──────────────────────────────────────────────────────────
    private void BuildDonArtGrid(RectTransform box, string selectedId, System.Action<string> onPick)
    {
        var all = DonArtCatalog.All;
        const float cw = 84f, ch = 132f, gap = 10f, rowGap = 12f;
        int perRow = 7;

        // Scrolled: the catalogue is however many files are installed (31 today), which is far
        // more than fits. Without this the last rows spilled out of the panel entirely.
        var area = Panel("ArtArea", box, new Color(0, 0, 0, 0));
        Stretch(area, new Vector2(0f, 0f), new Vector2(1f, 1f),
            new Vector2(20f, 60f),
            new Vector2(-20f, DonDeckSettings.CurrentMode == DonDeckSettings.Mode.Custom ? -240f : -140f));
        area.GetComponent<Image>().raycastTarget = false;

        var host = MakeScroll(area);
        int rows = Mathf.CeilToInt(all.Count / (float)perRow);
        host.sizeDelta = new Vector2(0f, rows * (ch + rowGap) + 8f);

        for (int i = 0; i < all.Count; i++)
        {
            int row = i / perRow, col = i % perRow;
            bool sel = string.Equals(all[i].Id, selectedId, System.StringComparison.OrdinalIgnoreCase);

            var cell = Panel("Art" + i, host, new Color(1f, 1f, 1f, 0.04f));
            cell.anchorMin = cell.anchorMax = new Vector2(0f, 1f);
            cell.pivot = new Vector2(0f, 1f);
            cell.sizeDelta = new Vector2(cw, ch);
            cell.anchoredPosition = new Vector2(col * (cw + gap), -row * (ch + rowGap));
            Round(cell); AddBorder(cell, sel ? Accent : MenuB, sel ? 1.6f : 1f);

            var art = DonThumb(all[i].Id);
            if (art != null)
            {
                var img = Panel("Art", cell, Color.white).GetComponent<Image>();
                img.sprite = art; img.preserveAspect = true; img.raycastTarget = false;
                Stretch(img.rectTransform, Vector2.zero, Vector2.one, new Vector2(4f, 26f), new Vector2(-4f, -4f));

                var hov = cell.gameObject.AddComponent<DonArtHover>();
                hov.mgr = this; hov.art = art;
            }

            // Two lines, auto-shrinking. "Edward Newgate Gold" wrapped past the caption and only
            // its last word showed, so every long name read as just "Gold".
            var name = Text_("N", cell, all[i].Display, 9, sel ? Accent : Muted, TextAnchor.LowerCenter);
            name.horizontalOverflow = HorizontalWrapMode.Wrap;
            name.verticalOverflow = VerticalWrapMode.Truncate;
            name.resizeTextForBestFit = true;
            name.resizeTextMinSize = 6;
            name.resizeTextMaxSize = 9;
            name.raycastTarget = false;
            Stretch(name.rectTransform, Vector2.zero, Vector2.one, new Vector2(2f, 3f), new Vector2(-2f, -(ch - 24f)));

            string id = all[i].Id;
            cell.gameObject.AddComponent<Button>().onClick.AddListener(() => onPick(id));
        }
    }

    // Thumbnails are read straight off disk — this screen is not in a match, so a synchronous
    // load is simpler than the CDN path and there are only ever a handful of files.
    private Sprite DonThumb(string artId)
    {
        artId = artId ?? DonArtCatalog.DefaultId;
        if (donArtThumbs.TryGetValue(artId, out var cached)) return cached;

        string path = CardAssets.LocalPath(DonArtCatalog.RelPathFor(artId));
        Sprite sprite = null;
        if (File.Exists(path))
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (tex.LoadImage(File.ReadAllBytes(path)))
                sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
        }
        donArtThumbs[artId] = sprite;
        return sprite;
    }
}
