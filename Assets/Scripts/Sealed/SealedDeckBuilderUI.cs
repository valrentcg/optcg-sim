// One Piece TCG — Sealed / Pre-Release: the deck builder.
//
// This is NOT the constructed deck builder. Sealed needs a different tool: you are working from a
// fixed 72-card pool rather than the whole game, the deck is 40 not 50, there is no colour rule and
// no copy limit, and the questions you ask are limited-format ones — "how many Counters do I have",
// "what does my curve look like", "what actually came out of pack 4".
//
// All of the decisions about WHICH cards show, in what order, under what headings live in
// SealedPoolView (pure C#, tested headlessly). This file owns pixels only: chips, grid, stats,
// validation, timer.
//
// Card interaction matches the spec:
//   click        add one          right-click   remove one
//   double-click add every copy   shift-click   move all
//   hover        zoom preview

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sealed
{
    public sealed partial class SealedDeckBuilderUI : MonoBehaviour
    {
        private RectTransform root, body;
        private SealedPool pool;
        private SealedPoolView view;
        private List<SealedFilter> chips;
        private Action<SealedPool> onDone;
        private readonly Dictionary<string, Sprite> spriteCache = new Dictionary<string, Sprite>();

        private RectTransform preview;
        private float deadlineUnscaled = -1f;      // build timer; -1 = untimed
        private Text timerText;

        public void Begin(RectTransform parent, SealedPool sealedPool, Action<SealedPool> done, int buildSeconds = 0)
        {
            root = parent;
            pool = sealedPool;
            onDone = done;
            view = new SealedPoolView();
            chips = SealedFilters.All();
            if (buildSeconds > 0) deadlineUnscaled = Time.unscaledTime + buildSeconds;
            Render();
            // The grid draws a cell immediately, so a per-cell async load always loses the race and the
            // card falls back to its name placeholder. Preload the pool (72 cards, all ids known) and
            // redraw once when it lands.
            StartCoroutine(PreloadPoolArt());
        }

        /// <summary>Ids with a load in flight. WITHOUT this, the "have I already asked for this?" test
        /// was spriteCache.ContainsKey — but GetSprite inserts a null placeholder the moment a cell is
        /// drawn, so by the time the preload ran every id was already "present" and it kicked nothing,
        /// waited on nothing, and refreshed before a single image had landed. That is why the builder
        /// stayed on name placeholders while the pack opening (which preloads BEFORE drawing) was fine.</summary>
        private readonly HashSet<string> loading = new HashSet<string>();
        private bool gridNeedsRefresh;

        private System.Collections.IEnumerator PreloadPoolArt()
        {
            foreach (var id in pool.PoolCounts().Keys.ToList())
                if (GetSprite(id) == null) { }        // GetSprite kicks the load if it is not cached

            // Redraw whenever a batch of art lands, not once on a guessed deadline, so the grid fills
            // in progressively and a slow file cannot leave it permanently blank.
            float waited = 0f;
            while (loading.Count > 0 && waited < 15f)
            {
                waited += Time.unscaledDeltaTime;
                if (gridNeedsRefresh) { gridNeedsRefresh = false; RefreshGrid(); }
                yield return null;
            }
            RefreshGrid();
        }

        private void LateUpdate()
        {
            // Late arrivals (a load that finished after the preload window) still get shown.
            if (!gridNeedsRefresh) return;
            gridNeedsRefresh = false;
            RefreshGrid();
        }

        private void Update()
        {
            if (deadlineUnscaled < 0f || timerText == null) return;
            float left = Mathf.Max(0f, deadlineUnscaled - Time.unscaledTime);
            timerText.text = $"{(int)(left / 60):00}:{(int)(left % 60):00}";
            timerText.color = left <= 300f ? SealedUI.Bad : left <= 600f ? SealedUI.Gold : SealedUI.Ink;
            if (left <= 0f) { deadlineUnscaled = -1f; Finish(); }
        }

        // ---- Layout --------------------------------------------------------------------------

        private void Render()
        {
            if (body != null) Destroy(body.gameObject);
            body = SealedUI.Panel(root, "Sealed Builder", SealedUI.PanelBg);
            SealedUI.Fill(body);

            BuildToolbar();
            BuildFilterRail();
            BuildGrid();
            BuildStatsPanel();
        }

        private void BuildToolbar()
        {
            var bar = SealedUI.Panel(body, "Toolbar", SealedUI.PanelBg2);
            SealedUI.Stretch(bar, new Vector2(0f, 0.94f), new Vector2(1f, 1f));

            var title = SealedUI.Label(bar, "Title",
                $"SEALED · {pool.SetCode} · seed {pool.Seed}", 14, SealedUI.Accent, TextAnchor.MiddleLeft, true);
            SealedUI.Stretch(title.rectTransform, new Vector2(0.01f, 0f), new Vector2(0.24f, 1f));

            // Search
            var field = NewInput(bar, new Vector2(0.25f, 0.15f), new Vector2(0.40f, 0.85f), "Search...");
            field.onValueChanged.AddListener(v => { view.Search = v; RefreshGrid(); });

            // Sort cycle
            SealedUI.Button(bar, "SORT: " + view.Sort, SealedUI.ChipOff, SealedUI.Ink, () =>
            {
                var all = (SealedSort[])Enum.GetValues(typeof(SealedSort));
                view.Sort = all[(Array.IndexOf(all, view.Sort) + 1) % all.Length];
                Render();
            }).let(rt => SealedUI.Stretch(rt, new Vector2(0.41f, 0.15f), new Vector2(0.525f, 0.85f)));

            // Quick view cycle
            SealedUI.Button(bar, "VIEW: " + view.QuickView, SealedUI.ChipOff, SealedUI.Ink, () =>
            {
                var all = (SealedQuickView[])Enum.GetValues(typeof(SealedQuickView));
                view.QuickView = all[(Array.IndexOf(all, view.QuickView) + 1) % all.Length];
                Render();
            }).let(rt => SealedUI.Stretch(rt, new Vector2(0.53f, 0.15f), new Vector2(0.645f, 0.85f)));

            SealedUI.Button(bar, "RESET", SealedUI.ChipOff, SealedUI.Ink, () => { view.Reset(); Render(); })
                .let(rt => SealedUI.Stretch(rt, new Vector2(0.65f, 0.15f), new Vector2(0.71f, 0.85f)));

            // POOL ⇄ SHOWCASE. The showcase is a view of the DECK, so it belongs in the builder next
            // to the pool rather than behind a separate screen — the whole point is seeing the deck
            // take shape while you are still cutting cards.
            SealedUI.Button(bar, showcase ? "◧ POOL" : "◧ SHOWCASE",
                showcase ? SealedUI.Accent : SealedUI.ChipOff,
                showcase ? SealedUI.BadgeInk : SealedUI.Ink,
                () => { showcase = !showcase; Render(); })
                .let(rt => SealedUI.Stretch(rt, new Vector2(0.715f, 0.15f), new Vector2(0.825f, 0.85f)));

            // Build timer (tournament mode only)
            if (deadlineUnscaled >= 0f)
            {
                timerText = SealedUI.Label(bar, "Timer", "--:--", 18, SealedUI.Ink, TextAnchor.MiddleCenter, true);
                SealedUI.Stretch(timerText.rectTransform, new Vector2(0.83f, 0f), new Vector2(0.93f, 1f));
            }

            // Vertically inset more than the other controls on purpose. SealedManager lays its "MENU"
            // overlay across y 0.915-0.96 of the SCREEN, which clips the bottom of this row, and it is
            // added after the builder so it wins the click — the bottom sliver of DONE used to exit the
            // builder instead of confirming the deck. Starting at 0.42 of the toolbar clears it.
            SealedUI.Button(bar, "DONE", SealedUI.Accent, SealedUI.BadgeInk, Finish)
                .let(rt => SealedUI.Stretch(rt, new Vector2(0.94f, 0.42f), new Vector2(0.99f, 0.88f)));
        }

        private void BuildFilterRail()
        {
            var rail = SealedUI.Panel(body, "Filters", SealedUI.PanelBg2);
            SealedUI.Stretch(rail, new Vector2(0f, 0f), new Vector2(0.145f, 0.94f));
            var col = SealedUI.ScrollColumn(rail, "Filter");
            SealedUI.Stretch(col.parent as RectTransform, new Vector2(0.03f, 0.01f), new Vector2(0.97f, 0.99f));

            SealedUI.Label(col, "H", "FILTERS", 11, SealedUI.Muted, TextAnchor.MiddleLeft, true)
                .rectTransform.gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;

            string lastGroup = null;
            RectTransform row = null;
            foreach (var chip in chips)
            {
                if (chip.Group != lastGroup)
                {
                    lastGroup = chip.Group;
                    row = SealedUI.ChipRow(col, chip.Group + " row");
                    var le = row.gameObject.AddComponent<LayoutElement>();
                    le.preferredHeight = 26f * Mathf.Ceil(chips.Count(c => c.Group == chip.Group) / 2f);
                }
                var captured = chip;
                SealedUI.Chip(row, chip.Label, view.IsActive(chip), () => { view.Toggle(captured); Render(); });
            }

            // Deck-only toggle sits with the filters because that is how it reads to a player.
            var deckOnly = SealedUI.Chip(col, view.DeckOnly ? "IN DECK ✓" : "IN DECK", view.DeckOnly,
                () => { view.DeckOnly = !view.DeckOnly; Render(); });
            deckOnly.gameObject.GetComponent<LayoutElement>().preferredHeight = 24f;
        }

        private RectTransform poolHost;      // the pool grid's outer panel — swapped for the showcase
        private RectTransform gridHost;
        /// <summary>Usable width of the card grid, used to derive the column count. Taken from the live
        /// rect when it has been laid out, else from the canvas reference width times the grid's anchor
        /// span — a freshly-created rect reads 0 on the first frame, which would collapse to 1 column.</summary>
        private float gridWidth = 1240f;

        private void BuildGrid()
        {
            var host = SealedUI.Panel(body, "Pool", new Color(0, 0, 0, 0));
            SealedUI.Stretch(host, new Vector2(0.145f, 0f), new Vector2(0.795f, 0.94f));
            poolHost = host;
            if (showcase) { BuildShowcase(host); return; }
            float live = host.rect.width;
            gridWidth = live > 200f ? live : 1920f * (0.795f - 0.145f);
            gridHost = SealedUI.ScrollColumn(host, "Pool", 6f);
            SealedUI.Stretch(gridHost.parent as RectTransform, new Vector2(0.01f, 0.01f), new Vector2(0.99f, 0.99f));
            RefreshGrid();
        }

        private void RefreshGrid()
        {
            if (showcase) { RefreshShowcase(); return; }
            if (gridHost == null) return;
            for (int i = gridHost.childCount - 1; i >= 0; i--) Destroy(gridHost.GetChild(i).gameObject);

            var groups = view.Build(pool, chips);
            if (groups.Count == 0 || groups.All(g => g.Count == 0))
            {
                var none = SealedUI.Label(gridHost, "Empty", "No cards match those filters.", 13, SealedUI.Muted);
                none.rectTransform.gameObject.AddComponent<LayoutElement>().preferredHeight = 40f;
                return;
            }

            foreach (var group in groups)
            {
                if (!string.IsNullOrEmpty(group.Heading))
                {
                    var h = SealedUI.Label(gridHost, "H " + group.Heading,
                        $"{group.Heading.ToUpperInvariant()}  ({group.Count})", 11, SealedUI.Accent, TextAnchor.MiddleLeft, true);
                    h.rectTransform.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;
                }

                var grid = new GameObject("Grid", typeof(RectTransform)).GetComponent<RectTransform>();
                grid.SetParent(gridHost, false);
                var g = grid.gameObject.AddComponent<GridLayoutGroup>();
                g.cellSize = new Vector2(132f, 184f);
                g.spacing = new Vector2(8f, 8f);
                g.childAlignment = TextAnchor.UpperLeft;
                // Rows are derived from the ACTUAL width the grid gets, not a guessed column
                // count, so the cards fill the row at any window size instead of leaving a dead margin.
                int cols = Mathf.Max(1, Mathf.FloorToInt((gridWidth + 8f) / (132f + 8f)));
                g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                g.constraintCount = cols;
                int rows = Mathf.CeilToInt(group.Count / (float)cols);
                grid.gameObject.AddComponent<LayoutElement>().preferredHeight = rows * 192f;

                foreach (var id in group.CardIds) AddCardCell(grid, id);
            }
        }

        private void AddCardCell(RectTransform parent, string cardId)
        {
            var def = CardData.GetCard(cardId);
            int inPool = pool.InPool(cardId);
            int inDeck = pool.InDeck(cardId);
            bool exhausted = pool.Remaining(cardId) <= 0;

            var cell = SealedUI.Panel(parent, cardId, new Color32(24, 36, 52, 255), raycast: true);
            var img = cell.GetComponent<Image>();
            var sprite = GetSprite(cardId);
            if (sprite != null) { img.sprite = sprite; img.color = exhausted ? new Color(0.45f, 0.45f, 0.5f) : Color.white; img.preserveAspect = true; }
            else
            {
                var t = SealedUI.Label(cell, "N", $"{def?.Name ?? cardId}\n{def?.Cost}c", 9, SealedUI.Ink, TextAnchor.MiddleCenter);
                SealedUI.Stretch(t.rectTransform, new Vector2(0.05f, 0.05f), new Vector2(0.95f, 0.95f));
                t.horizontalOverflow = HorizontalWrapMode.Wrap;
            }

            // Pool / deck counts, always visible — no guessing how many are left.
            SealedUI.CornerBadge(cell, inPool.ToString(), new Vector2(0f, 1f), new Color32(40, 54, 72, 235), SealedUI.Muted);
            if (inDeck > 0)
                SealedUI.CornerBadge(cell, inDeck.ToString(), new Vector2(1f, 1f), SealedUI.Accent, SealedUI.BadgeInk);

            var click = cell.gameObject.AddComponent<SealedUI.ClickRouter>();
            click.OnLeft = () =>
            {
                bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                if (shift) pool.AddAll(cardId); else pool.Add(cardId);
                RefreshGrid(); RefreshStats();
            };
            click.OnDoubleLeft = () => { pool.AddAll(cardId); RefreshGrid(); RefreshStats(); };
            click.OnRight = () =>
            {
                bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                if (shift) pool.RemoveAll(cardId); else pool.Remove(cardId);
                RefreshGrid(); RefreshStats();
            };

            var hover = cell.gameObject.AddComponent<SealedUI.HoverRouter>();
            hover.OnEnter = () => ShowPreview(cardId);
            hover.OnExit = HidePreview;
        }

        // ---- Stats panel ---------------------------------------------------------------------

        private RectTransform statsHost;

        private void BuildStatsPanel()
        {
            var panel = SealedUI.Panel(body, "Stats", SealedUI.PanelBg2);
            SealedUI.Stretch(panel, new Vector2(0.795f, 0f), new Vector2(1f, 0.94f));
            statsHost = SealedUI.ScrollColumn(panel, "Stats", 3f);
            SealedUI.Stretch(statsHost.parent as RectTransform, new Vector2(0.05f, 0.01f), new Vector2(0.95f, 0.99f));
            RefreshStats();
        }

        private void RefreshStats()
        {
            if (statsHost == null) return;
            for (int i = statsHost.childCount - 1; i >= 0; i--) Destroy(statsHost.GetChild(i).gameObject);

            var v = pool.Validate();
            var s = pool.DeckStats();

            Row($"DECK  {v.MainCount} / {v.RequiredCount}", v.MainCount == v.RequiredCount ? SealedUI.Good : SealedUI.Ink, 15, true);

            // Leader
            string leaderName = string.IsNullOrEmpty(pool.LeaderId)
                ? "— none —"
                : (SealedLeaderRules.IsRainbowLuffy(pool.LeaderId)
                    ? "Rainbow Luffy"
                    : CardData.GetCard(pool.LeaderId)?.Name ?? pool.LeaderId);
            Row($"Leader: {leaderName}", v.LeaderLegal && v.HasLeader ? SealedUI.Good : SealedUI.Bad, 12);
            Row($"Format: {SealedLeaderRules.ModeName(pool.LeaderMode)}", SealedUI.Muted, 10);

            // Composition, counters, curve and colour identity are the SAME graphs the constructed
            // builder draws (DeckStatsPanel), not a limited-only text readout. The ASCII bars that used
            // to live here said the same things less clearly, and a player moving between the two
            // builders should not have to learn a second way of reading their own deck.
            Gap();
            SealedDeckGraphs.RenderInto(statsHost, pool.Deck);

            // Keywords stay as text: they are a LIMITED question ("do I have enough Blockers to
            // survive a slow board?") with no equivalent in the constructed panel.
            Gap();
            Row("KEYWORDS", SealedUI.Accent, 11, true);
            Row($"Blockers     {s.Blockers}", SealedUI.Ink, 12);
            Row($"Triggers     {s.Triggers}", SealedUI.Ink, 12);
            if (s.Rush > 0) Row($"Rush         {s.Rush}", SealedUI.Ink, 12);
            if (s.DoubleAttack > 0) Row($"Dbl Attack   {s.DoubleAttack}", SealedUI.Ink, 12);
            if (s.NoCounter > 20) Row($"No Counter   {s.NoCounter}", SealedUI.Bad, 12);

            if (!v.Ok)
            {
                Gap();
                Row("TO FIX", SealedUI.Bad, 11, true);
                foreach (var p in v.Problems.Take(4)) Row("• " + p, SealedUI.Bad, 11);
            }

            void Row(string text, Color colour, int size, bool bold = false)
            {
                var t = SealedUI.Label(statsHost, "R", text, size, colour, TextAnchor.MiddleLeft, bold);
                t.rectTransform.gameObject.AddComponent<LayoutElement>().preferredHeight = size + 6f;
            }
            void Gap()
            {
                var g = SealedUI.Panel(statsHost, "Gap", new Color(0, 0, 0, 0));
                g.gameObject.AddComponent<LayoutElement>().preferredHeight = 8f;
            }
        }

        // ---- Preview -------------------------------------------------------------------------

        private void ShowPreview(string cardId)
        {
            HidePreview();
            var sprite = GetSprite(cardId);
            preview = SealedUI.Panel(root, "Preview", sprite != null ? Color.white : new Color32(24, 36, 52, 250));
            preview.anchorMin = preview.anchorMax = new Vector2(0.5f, 0.5f);
            preview.pivot = new Vector2(0.5f, 0.5f);
            preview.sizeDelta = new Vector2(420f, 588f);
            preview.SetAsLastSibling();
            var img = preview.GetComponent<Image>();
            if (sprite != null) { img.sprite = sprite; img.preserveAspect = true; }
            else
            {
                var def = CardData.GetCard(cardId);
                var t = SealedUI.Label(preview, "N",
                    $"{def?.Name}\n\n{def?.Effect}", 13, SealedUI.Ink, TextAnchor.UpperCenter);
                SealedUI.Stretch(t.rectTransform, new Vector2(0.06f, 0.06f), new Vector2(0.94f, 0.94f));
                t.horizontalOverflow = HorizontalWrapMode.Wrap;
                t.verticalOverflow = VerticalWrapMode.Overflow;
            }
        }

        private void HidePreview()
        {
            if (preview != null) { Destroy(preview.gameObject); preview = null; }
        }

        // ---- Art / exit ----------------------------------------------------------------------

        private Sprite GetSprite(string cardId)
        {
            if (string.IsNullOrEmpty(cardId)) return null;
            if (spriteCache.TryGetValue(cardId, out var s) && s != null) return s;
            // Kick a load unless one is already in flight. Keyed on `loading`, NOT on the cache, since
            // the cache holds a null placeholder from the very first draw.
            if (loading.Add(cardId)) KickLoad(cardId);
            return null;
        }

        private async void KickLoad(string cardId, Action onDone = null)
        {
            try
            {
                while (!CardAssets.Ready) await System.Threading.Tasks.Task.Yield();
                var rel = CardAssets.FirstExisting(CardAssets.ArtCandidates(cardId));
                if (string.IsNullOrEmpty(rel)) return;
                var bytes = await CardAssets.ReadBytesAsync(rel);
                if (bytes == null || bytes.Length == 0) return;
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(bytes)) return;
                spriteCache[cardId] = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            }
            catch (Exception e) { Debug.LogWarning($"[SealedBuilder] art load failed for {cardId}: {e.Message}"); }
            finally
            {
                loading.Remove(cardId);
                gridNeedsRefresh = true;   // coalesced in LateUpdate, so N arrivals cost one redraw
                onDone?.Invoke();
            }
        }

        private InputField NewInput(RectTransform parent, Vector2 min, Vector2 max, string placeholder)
        {
            var go = new GameObject("Search", typeof(RectTransform), typeof(Image), typeof(InputField));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            SealedUI.Stretch(rt, min, max);
            go.GetComponent<Image>().color = new Color32(20, 34, 50, 235);
            var f = go.GetComponent<InputField>();
            var ph = SealedUI.Label(rt, "PH", placeholder, 12, SealedUI.Muted);
            var tx = SealedUI.Label(rt, "TX", "", 12, SealedUI.Ink);
            SealedUI.Stretch(ph.rectTransform, new Vector2(0.03f, 0f), new Vector2(0.97f, 1f));
            SealedUI.Stretch(tx.rectTransform, new Vector2(0.03f, 0f), new Vector2(0.97f, 1f));
            f.textComponent = tx;
            f.placeholder = ph;
            return f;
        }

        private void Finish()
        {
            HidePreview();
            if (body != null) Destroy(body.gameObject);
            onDone?.Invoke(pool);
        }
    }

    internal static class RectTransformChain
    {
        /// <summary>Tiny fluent helper so a Button(...) call can be positioned inline.</summary>
        public static RectTransform let(this RectTransform rt, Action<RectTransform> act) { act?.Invoke(rt); return rt; }
    }
}
