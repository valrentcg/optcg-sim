// One Piece TCG - Deck Showcase view (design_handoff_deck_showcase).
//
// Partial of DeckBuilderManager: a read-only, one-page "this is my deck" board.
// Leader upper-left, then every unique card as a fanned stack of its copies
// (top copy fully visible, earlier copies peeking left), 6 columns, ordered by
// cost. The grouped decklist stays on the right (BuildSelectDecklist verbatim);
// clicking a stack or a decklist row cross-highlights the card in both.
//
// Entry: the SHOWCASE button on the Select view's leader panel. Read-only —
// EDIT DECK (top-right) hops to the editor; no add/remove controls here.

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public partial class DeckBuilderManager
{
    // Cross-highlighted card id (stack ⇄ decklist row). Session-only.
    private string selectedShowcaseCardId;

    private const float ShowCardW = 130f;
    private const float ShowCardH = 182f;   // OP card aspect ≈ 0.716
    private const float ShowFanDx = 15f;    // per-copy fan offset

    private void OpenShowcase(string deckId)
    {
        selectedDeckId = deckId;
        selectedShowcaseCardId = null;
        pendingDeleteDeckId = null;
        view = View.Showcase;
        Render();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // View
    // ══════════════════════════════════════════════════════════════════════════

    private void RenderShowcase()
    {
        var deck = DeckStore.Get(selectedDeckId);
        if (deck == null) { view = View.Select; Render(); return; }

        var (ok, total, _) = Validate(deck);
        TopBar("DECK SHOWCASE",
            $"{(string.IsNullOrEmpty(deck.name) ? "Untitled" : deck.name)}  ·  {total} / 50  ·  read-only",
            () => { view = View.Select; Render(); });

        // EDIT DECK (top-right)
        var editBtn = Panel("Edit Deck", root, Accent);
        editBtn.anchorMin = editBtn.anchorMax = new Vector2(1f, 1f);
        editBtn.pivot = new Vector2(1f, 1f);
        editBtn.sizeDelta = new Vector2(150f, 38f);
        editBtn.anchoredPosition = new Vector2(-24f, -13f);
        Round(editBtn);
        var ebt = Text_("t", editBtn, "EDIT DECK", 12, BadgeInk, TextAnchor.MiddleCenter, monoFont);
        ebt.fontStyle = FontStyle.Bold;
        Stretch(ebt.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var editId = deck.id;
        editBtn.gameObject.AddComponent<Button>().onClick.AddListener(() =>
        {
            var d = DeckStore.Get(editId);
            if (d != null)
            {
                editing = d.Clone(); view = View.Editor; pickingLeader = false;
                ResetFilters(); Render();
            }
        });

        // Body: board (left/main) + the existing grouped decklist (right).
        var body = Panel("Body", root, new Color(0, 0, 0, 0));
        Stretch(body, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0f, -64f));
        body.GetComponent<Image>().raycastTarget = false;

        const float RightW = 480f;

        var rightPanel = Panel("Right", body, new Color(0, 0, 0, 0));
        Stretch(rightPanel, new Vector2(1f, 0f), Vector2.one, new Vector2(-RightW, 0f), Vector2.zero);
        rightPanel.GetComponent<Image>().raycastTarget = false;
        var rdiv = Panel("RDiv", rightPanel, new Color(120f / 255f, 180f / 255f, 220f / 255f, 0.10f));
        rdiv.anchorMin = new Vector2(0f, 0.05f); rdiv.anchorMax = new Vector2(0f, 0.95f);
        rdiv.pivot = new Vector2(0f, 0.5f); rdiv.sizeDelta = new Vector2(1f, 0f);
        rdiv.GetComponent<Image>().raycastTarget = false;

        var mainPanel = Panel("Board", body, new Color(0, 0, 0, 0));
        Stretch(mainPanel, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-RightW, 0f));
        mainPanel.GetComponent<Image>().raycastTarget = false;

        BuildShowcaseBoard(mainPanel, deck);
        BuildSelectDecklist(rightPanel, deck);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Board grid: leader + one fanned stack per unique card, 6 columns
    // ══════════════════════════════════════════════════════════════════════════

    private void BuildShowcaseBoard(RectTransform panel, DeckData deck)
    {
        // Unique cards in decklist order, flattened across types:
        // cost ↑, then character → event → stage, then name.
        int TypeOrder(CardRec r)
        {
            switch ((r.type ?? "").ToLower())
            {
                case "event": return 1;
                case "stage": return 2;
                default:      return 0;
            }
        }
        var uniques = deck.cards
            .Select(e => new { e.id, e.count, rec = Card(e.id) })
            .Where(x => x.rec != null && x.count > 0)
            .OrderBy(x => x.rec.cost).ThenBy(x => TypeOrder(x.rec)).ThenBy(x => x.rec.name)
            .ToList();

        var leaderRec = string.IsNullOrEmpty(deck.leaderId) ? null : Card(deck.leaderId);
        int cellCount = uniques.Count + (leaderRec != null ? 1 : 0);

        // Grid geometry at the 1920x1080 reference: board ≈ 1440 wide, body
        // ≈ 1016 tall. 6 columns; rows share the height evenly. Most decks are
        // 16-20 uniques (3 rows); genuinely bigger decks flow into a scroll.
        const int COLS = 6;
        const float FOOTER_H = 34f;
        const float BOARD_W = 1920f - 480f;
        const float BOARD_H = 1016f - FOOTER_H;
        int rows = Mathf.Max(3, Mathf.CeilToInt(cellCount / (float)COLS));
        bool scrolls = rows > 3;
        float cellH = scrolls ? 226f : BOARD_H / rows;
        float cellW = (BOARD_W - 24f) / COLS;

        RectTransform content;
        if (scrolls)
        {
            var area = Panel("BoardArea", panel, new Color(0, 0, 0, 0));
            Stretch(area, Vector2.zero, Vector2.one, new Vector2(12f, FOOTER_H), new Vector2(-12f, 0f));
            content = MakeScroll(area);
            content.sizeDelta = new Vector2(0f, rows * cellH + 16f);
        }
        else
        {
            content = Panel("BoardContent", panel, new Color(0, 0, 0, 0));
            Stretch(content, Vector2.zero, Vector2.one, new Vector2(12f, FOOTER_H), new Vector2(-12f, 0f));
            content.GetComponent<Image>().raycastTarget = false;
        }

        int cell = 0;
        if (leaderRec != null) { BuildShowcaseStack(content, leaderRec, 1, true, cell, cellW, cellH); cell++; }
        foreach (var u in uniques)
        {
            BuildShowcaseStack(content, u.rec, u.count, false, cell, cellW, cellH);
            cell++;
        }

        if (cellCount == 0)
        {
            var empty = Text_("Empty", panel, "This deck has no cards yet.", 13, Muted, TextAnchor.MiddleCenter);
            Stretch(empty.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        }

        // Footer watermark — cosmetic, reads nicely in a shared screenshot.
        var wm = Panel("Watermark", panel, new Color(0, 0, 0, 0));
        wm.anchorMin = new Vector2(0.5f, 0f); wm.anchorMax = new Vector2(0.5f, 0f);
        wm.pivot = new Vector2(0.5f, 0f);
        wm.sizeDelta = new Vector2(420f, FOOTER_H);
        wm.anchoredPosition = new Vector2(0f, 2f);
        wm.GetComponent<Image>().raycastTarget = false;
        var wmDiamond = Panel("D", wm, new Color(Accent.r, Accent.g, Accent.b, 0.5f));
        wmDiamond.anchorMin = wmDiamond.anchorMax = new Vector2(0.5f, 0.5f);
        wmDiamond.pivot = new Vector2(0.5f, 0.5f);
        wmDiamond.sizeDelta = new Vector2(7f, 7f);
        wmDiamond.anchoredPosition = new Vector2(-138f, 0f);
        wmDiamond.localRotation = Quaternion.Euler(0f, 0f, 45f);
        wmDiamond.GetComponent<Image>().raycastTarget = false;
        var wmT = Text_("t", wm, "ONE PIECE TCG  ·  DECK SHOWCASE", 10,
            new Color(Muted.r, Muted.g, Muted.b, 0.5f), TextAnchor.MiddleCenter, monoFont);
        Stretch(wmT.rectTransform, Vector2.zero, Vector2.one, new Vector2(20f, 0f), Vector2.zero);
    }

    // One grid cell: a fanned stack of `count` copies (or the single leader).
    private void BuildShowcaseStack(RectTransform content, CardRec rec, int count,
        bool isLeader, int cellIndex, float cellW, float cellH)
    {
        int col = cellIndex % 6, row = cellIndex / 6;
        bool selected = rec.id == selectedShowcaseCardId;

        float stackW = ShowCardW + (count - 1) * ShowFanDx;
        var stack = Panel(rec.id + " Stack", content, new Color(0, 0, 0, 0));
        stack.anchorMin = stack.anchorMax = new Vector2(0f, 1f);
        stack.pivot = new Vector2(0.5f, 0.5f);
        stack.sizeDelta = new Vector2(stackW, ShowCardH);
        stack.anchoredPosition = new Vector2(col * cellW + cellW / 2f, -(row * cellH + cellH / 2f));
        if (selected) stack.SetAsLastSibling();   // selected sits above neighbours

        for (int i = 0; i < count; i++)
        {
            bool front = i == count - 1;
            var card = BuildShowcaseCardFace(stack, rec, isLeader, i * ShowFanDx);
            if (front)
            {
                if (isLeader && !selected) AddBorder(card, new Color(Gold.r, Gold.g, Gold.b, 0.9f), 2f);
                if (selected) AddBorder(card, Accent, 2.5f);
            }
        }

        // ×count pill — cyan, pinned above the stack's top-right. (The red
        // hexagon from an earlier design iteration was rejected; keep the pill.)
        if (count > 1)
        {
            var pill = Panel("Count Pill", stack, Accent);
            pill.anchorMin = pill.anchorMax = new Vector2(1f, 1f);
            pill.pivot = new Vector2(1f, 0.5f);
            pill.sizeDelta = new Vector2(34f, 18f);
            pill.anchoredPosition = new Vector2(4f, 6f);
            Round(pill);
            var pt = Text_("t", pill, "×" + count, 11, BadgeInk, TextAnchor.MiddleCenter, monoFont);
            pt.fontStyle = FontStyle.Bold;
            Stretch(pt.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        }

        // LEADER pill — gold, top-left, above the frame.
        if (isLeader)
        {
            var lp = Panel("Leader Pill", stack, Gold);
            lp.anchorMin = lp.anchorMax = new Vector2(0f, 1f);
            lp.pivot = new Vector2(0f, 0.5f);
            lp.sizeDelta = new Vector2(52f, 16f);
            lp.anchoredPosition = new Vector2(-4f, 6f);
            Round(lp);
            var lt = Text_("t", lp, "LEADER", 8, BadgeInk, TextAnchor.MiddleCenter, monoFont);
            lt.fontStyle = FontStyle.Bold;
            Stretch(lt.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        }

        // Click = cross-highlight; hover = the usual big card preview.
        var hov = stack.gameObject.AddComponent<HoverPreview>();
        hov.mgr = this; hov.cardId = rec.id;
        var img = stack.GetComponent<Image>();
        img.raycastTarget = true;   // transparent hit area over the whole stack
        var btn = stack.gameObject.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        string id = rec.id;
        btn.onClick.AddListener(() => { selectedShowcaseCardId = id; Render(); });
    }

    // One copy's card face: the FULL card image fit to the 130x182 frame —
    // like DON!! cards stacked in the cost area during a match. The printed
    // card already shows cost/power/name, so no overlay chrome is added.
    private RectTransform BuildShowcaseCardFace(RectTransform stack, CardRec rec,
        bool isLeader, float x)
    {
        var card = Panel(rec.id + " @" + x, stack, new Color32(11, 10, 22, 255));
        card.anchorMin = card.anchorMax = new Vector2(0f, 0.5f);
        card.pivot = new Vector2(0f, 0.5f);
        card.sizeDelta = new Vector2(ShowCardW, ShowCardH);
        card.anchoredPosition = new Vector2(x, 0f);
        var cardImg = card.GetComponent<Image>();
        cardImg.sprite = RoundSprite(); cardImg.type = Image.Type.Sliced;
        cardImg.raycastTarget = false;
        var mask = card.gameObject.AddComponent<Mask>();
        mask.showMaskGraphic = true;

        bool knownNoArt = _thumbCache.TryGetValue(rec.id, out var cached) && cached == null;
        if (!knownNoArt)
        {
            // Thumbs are full card scans at the same 0.716 aspect as the frame,
            // so a plain stretch IS fit-to-frame — no crop, no face math.
            var art = Panel("Art", card, cached != null ? Color.white : (Color)RowBg);
            var ai = art.GetComponent<Image>();
            ai.preserveAspect = false; ai.raycastTarget = false;
            Stretch(art, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            if (cached != null) { ai.sprite = cached; ai.type = Image.Type.Simple; }
            else RequestThumbArt(ai, rec.id);
        }
        else
        {
            var mono = Text_("Mono", card,
                string.IsNullOrEmpty(rec.name) ? "?" : rec.name.Substring(0, 1).ToUpper(),
                34, new Color(1f, 1f, 1f, 0.16f), TextAnchor.MiddleCenter);
            Stretch(mono.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        }

        return card;
    }
}
