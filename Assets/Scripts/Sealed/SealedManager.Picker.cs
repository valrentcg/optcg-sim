// Pokemon-Pocket-style pack carousel and pre-run matchup setup for Sealed.

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sealed
{
    public sealed partial class SealedManager
    {
        private void ShowPicker()
        {
            screen = Screen.Picker;
            Clear();
            EnsureLeaderChoices();

            var title = SealedUI.Label(screenRoot, "Title", "CHOOSE YOUR PACKS", 30,
                SealedUI.Ink, TextAnchor.UpperCenter, true);
            SealedUI.Stretch(title.rectTransform, new Vector2(0.18f, 0.92f), new Vector2(0.82f, 0.975f));
            var sub = SealedUI.Label(screenRoot, "Sub",
                "Swipe the wheel, stop on a set, then open a fresh randomized six-pack kit.",
                13, SealedUI.Muted, TextAnchor.UpperCenter);
            SealedUI.Stretch(sub.rectTransform, new Vector2(0.18f, 0.875f), new Vector2(0.82f, 0.915f));
            SealedUI.Button(screenRoot, "◂ MENU", SealedUI.ChipOff, SealedUI.Ink, ExitToMenu)
                .let(rt => SealedUI.Stretch(rt, new Vector2(0.90f, 0.915f), new Vector2(0.97f, 0.96f)));

            if (SealedCatalog.Available().Count == 0)
            {
                var error = SealedUI.Label(screenRoot, "No Sets", "No booster sets are available.", 15,
                    SealedUI.Bad, TextAnchor.MiddleCenter);
                SealedUI.Stretch(error.rectTransform, new Vector2(0.1f, 0.35f), new Vector2(0.9f, 0.6f));
                return;
            }

            BuildPackWheel();
            BuildMatchSetup();

            var start = SealedUI.Button(screenRoot, "OPEN 6 PACKS", SealedUI.Accent,
                SealedUI.BadgeInk, StartRun, 18);
            SealedUI.Stretch(start, new Vector2(0.70f, 0.025f), new Vector2(0.94f, 0.09f));

            var saved = SealedStore.All();
            if (saved.Count > 0)
            {
                var latest = saved[0];
                var resume = SealedUI.Button(screenRoot, $"CONTINUE  {latest.Label}", SealedUI.ChipOff,
                    SealedUI.Ink, () => ResumeRun(latest), 11, false);
                SealedUI.Stretch(resume, new Vector2(0.06f, 0.035f), new Vector2(0.30f, 0.082f));
            }
        }

        private void ShowProductPicker()
        {
            screen = Screen.Picker;
            Clear();

            var title = SealedUI.Label(screenRoot, "Title", "CHOOSE SEALED SET", 30,
                SealedUI.Ink, TextAnchor.UpperCenter, true);
            SealedUI.Stretch(title.rectTransform, new Vector2(0.18f, 0.92f), new Vector2(0.82f, 0.975f));
            var sub = SealedUI.Label(screenRoot, "Sub",
                "Swipe through the pack wheel, stop on the set both players will open, then confirm.",
                13, SealedUI.Muted, TextAnchor.UpperCenter);
            SealedUI.Stretch(sub.rectTransform, new Vector2(0.15f, 0.875f), new Vector2(0.85f, 0.915f));

            var close = SealedUI.Button(screenRoot, "CANCEL", SealedUI.ChipOff, SealedUI.Ink, () =>
            {
                productPickerCancelled?.Invoke();
                Destroy(gameObject);
            }, 12);
            SealedUI.Stretch(close, new Vector2(0.88f, 0.915f), new Vector2(0.97f, 0.96f));

            if (chosenProduct == null)
            {
                var error = SealedUI.Label(screenRoot, "No Sets", "No booster sets are available.", 15,
                    SealedUI.Bad, TextAnchor.MiddleCenter);
                SealedUI.Stretch(error.rectTransform, new Vector2(0.1f, 0.35f), new Vector2(0.9f, 0.6f));
                return;
            }

            BuildPackWheel();
            var select = SealedUI.Button(screenRoot, "SELECT  " + chosenProduct.SetCode,
                SealedUI.Accent, SealedUI.BadgeInk, () =>
                {
                    string setCode = chosenProduct.SetCode;
                    productPickerChosen?.Invoke(setCode);
                    Destroy(gameObject);
                }, 18);
            SealedUI.Stretch(select, new Vector2(0.38f, 0.25f), new Vector2(0.62f, 0.325f));

            var help = SealedUI.Label(screenRoot, "Ready Help",
                "No packs open here. Pack ripping begins only after both lobby players select leaders and press Ready.",
                13, SealedUI.Muted, TextAnchor.UpperCenter);
            SealedUI.Stretch(help.rectTransform, new Vector2(0.18f, 0.17f), new Vector2(0.82f, 0.23f));
        }

        private void BuildPackWheel()
        {
            var products = SealedCatalog.Available();
            int selected = Mathf.Max(0, products.FindIndex(p => p.SetCode == chosenProduct?.SetCode));
            var wheel = SealedUI.Panel(screenRoot, "Pack Wheel", new Color(6f / 255f, 18f / 255f, 30f / 255f, 0.34f), true);
            SealedUI.Stretch(wheel, new Vector2(0.035f, 0.49f), new Vector2(0.965f, 0.86f));
            SealedUI.Round(wheel);
            var track = SealedUI.Panel(wheel, "Pack Track", Color.clear);
            SealedUI.Fill(track);

            var carouselItems = new List<SealedPackCarousel.Item>();
            for (int index = 0; index < products.Count; index++)
            {
                var pack = SealedUI.Panel(track, products[index].SetCode, Color.white);
                pack.anchorMin = pack.anchorMax = pack.pivot = new Vector2(0.5f, 0.5f);
                var image = pack.GetComponent<Image>();
                image.sprite = SealedPackArt.For(products[index].SetCode);
                image.preserveAspect = true;
                var code = SealedUI.Label(pack, "Code", products[index].SetCode, 12,
                    SealedUI.Ink, TextAnchor.LowerCenter, true);
                // Keep the individual set code tucked directly under its pack. The selected-set
                // details live below the wheel, so these two text layers never occupy the same band.
                SealedUI.Stretch(code.rectTransform, new Vector2(-0.2f, -0.035f), new Vector2(1.2f, 0.025f));
                carouselItems.Add(new SealedPackCarousel.Item
                {
                    Index = index,
                    Rect = pack,
                    Image = image,
                    Label = code,
                    Group = pack.gameObject.AddComponent<CanvasGroup>()
                });
            }

            var focus = SealedUI.Panel(wheel, "Selection Glow",
                new Color(SealedUI.Accent.r, SealedUI.Accent.g, SealedUI.Accent.b, 0.18f));
            SealedUI.Stretch(focus, new Vector2(0.405f, -0.005f), new Vector2(0.595f, 0.008f));

            var carousel = wheel.gameObject.AddComponent<SealedPackCarousel>();
            carousel.Initialize(wheel, carouselItems, selected, SelectProduct);

            SealedUI.Button(wheel, "‹", new Color32(17, 33, 50, 225), SealedUI.Ink,
                () => SelectProduct(selected - 1), 30)
                .let(rt => SealedUI.Stretch(rt, new Vector2(0.008f, 0.38f), new Vector2(0.045f, 0.62f)));
            SealedUI.Button(wheel, "›", new Color32(17, 33, 50, 225), SealedUI.Ink,
                () => SelectProduct(selected + 1), 30)
                .let(rt => SealedUI.Stretch(rt, new Vector2(0.955f, 0.38f), new Vector2(0.992f, 0.62f)));

            var info = SealedUI.Label(screenRoot, "Selected Set",
                $"{chosenProduct.SetCode}  ·  {chosenProduct.DisplayName}\n"
                + $"{chosenProduct.ReleaseDate}  ·  {chosenProduct.CardCount()} cards  ·  "
                + $"{chosenProduct.LegalLeaders().Count} leaders",
                14, SealedUI.Ink, TextAnchor.UpperCenter, true);
            SealedUI.Stretch(info.rectTransform, new Vector2(0.16f, 0.418f), new Vector2(0.84f, 0.486f));
        }

        private void BuildMatchSetup()
        {
            var panel = SealedUI.Panel(screenRoot, "Match Setup", SealedUI.PanelBg2);
            SealedUI.Stretch(panel, new Vector2(0.06f, 0.105f), new Vector2(0.94f, 0.415f));
            SealedUI.Round(panel);
            BuildLeaderSelector(panel, "YOUR LEADER", true, new Vector2(0.02f, 0.02f), new Vector2(0.34f, 0.98f));
            BuildLeaderSelector(panel, "OPPONENT LEADER", false, new Vector2(0.66f, 0.02f), new Vector2(0.98f, 0.98f));

            var heading = SealedUI.Label(panel, "Difficulty", "A.I. DIFFICULTY", 11,
                SealedUI.Muted, TextAnchor.MiddleCenter, true);
            SealedUI.Stretch(heading.rectTransform, new Vector2(0.36f, 0.71f), new Vector2(0.64f, 0.84f));
            AddDifficulty("BEGINNER", "beginner", 0.365f, 0.452f);
            AddDifficulty("INTERMEDIATE", "intermediate", 0.456f, 0.548f);
            AddDifficulty("ADVANCED", "advanced", 0.552f, 0.635f);

            var timer = SealedUI.Button(panel, timedBuild ? "BUILD TIMER  50:00" : "BUILD TIMER  OFF",
                timedBuild ? SealedUI.Accent : SealedUI.ChipOff,
                timedBuild ? SealedUI.BadgeInk : SealedUI.Ink,
                () => { timedBuild = !timedBuild; ShowPicker(); }, 11, timedBuild);
            SealedUI.Stretch(timer, new Vector2(0.405f, 0.16f), new Vector2(0.595f, 0.34f));

            void AddDifficulty(string label, string value, float minX, float maxX)
            {
                bool selected = chosenDifficulty == value;
                var button = SealedUI.Button(panel, label, selected ? SealedUI.Accent : SealedUI.ChipOff,
                    selected ? SealedUI.BadgeInk : SealedUI.Ink,
                    () => { chosenDifficulty = value; ShowPicker(); }, 9, selected);
                SealedUI.Stretch(button, new Vector2(minX, 0.48f), new Vector2(maxX, 0.66f));
                SealedUI.Round(button);
            }
        }

        private void BuildLeaderSelector(RectTransform parent, string heading, bool player, Vector2 min, Vector2 max)
        {
            // Keep the matchup setup visually open. Empty space is never an invisible button:
            // only the card receives clicks and the heading is plain text above it.
            var host = SealedUI.Panel(parent, heading, Color.clear);
            SealedUI.Stretch(host, min, max);
            string id = player ? chosenPlayerLeader : chosenOpponentLeader;

            var title = SealedUI.Label(host, "Heading", heading, 11,
                SealedUI.Accent, TextAnchor.MiddleCenter, true);
            SealedUI.Stretch(title.rectTransform, new Vector2(0f, 0.89f), new Vector2(1f, 1f));

            var art = SealedUI.Panel(host, "Leader Art", new Color32(25, 40, 57, 255), true);
            SealedUI.Stretch(art, new Vector2(0.20f, 0.005f), new Vector2(0.80f, 0.885f));
            RoundedCardMask.ApplyTo(art.GetComponent<Image>());
            ApplyLeaderArt(id, art.GetComponent<Image>());
            var button = art.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.ColorTint;
            button.onClick.AddListener(() => ShowLeaderPicker(player));
        }

        private void ShowLeaderPicker(bool player)
        {
            string selectedId = player ? chosenPlayerLeader : chosenOpponentLeader;
            var overlay = SealedUI.Panel(screenRoot, "Leader Picker Overlay", new Color(2f / 255f, 8f / 255f, 15f / 255f, 0.94f), true);
            SealedUI.Fill(overlay);
            overlay.SetAsLastSibling();

            var modal = SealedUI.Panel(overlay, "Leader Picker", SealedUI.PanelBg, true);
            SealedUI.Stretch(modal, new Vector2(0.055f, 0.045f), new Vector2(0.945f, 0.955f));
            SealedUI.Round(modal);
            SealedUI.Border(modal, new Color(SealedUI.Accent.r, SealedUI.Accent.g, SealedUI.Accent.b, 0.5f), 2f);

            var title = SealedUI.Label(modal, "Title",
                player ? "CHOOSE YOUR LEADER" : "CHOOSE OPPONENT LEADER",
                24, SealedUI.Ink, TextAnchor.MiddleLeft, true);
            SealedUI.Stretch(title.rectTransform, new Vector2(0.025f, 0.925f), new Vector2(0.7f, 0.98f));
            var help = SealedUI.Label(modal, "Help",
                "Filter the full roster, hover to inspect, then click a legal leader. Banned leaders remain visible for reference.",
                12, SealedUI.Muted, TextAnchor.MiddleLeft);
            SealedUI.Stretch(help.rectTransform, new Vector2(0.025f, 0.87f), new Vector2(0.80f, 0.92f));
            var close = SealedUI.Button(modal, "CLOSE", SealedUI.ChipOff, SealedUI.Ink,
                () =>
                {
                    if (leaderPickerOnly)
                    {
                        leaderPickerCancelled?.Invoke();
                        Destroy(gameObject);
                    }
                    else Destroy(overlay.gameObject);
                }, 12);
            SealedUI.Stretch(close, new Vector2(0.86f, 0.89f), new Vector2(0.97f, 0.955f));
            SealedUI.Round(close);

            Action refresh = () =>
            {
                Destroy(overlay.gameObject);
                ShowLeaderPicker(player);
            };

            string[] colors = { "RED", "GREEN", "BLUE", "PURPLE", "BLACK", "YELLOW" };
            BuildLeaderFilterRow(modal, "COLOUR", colors,
                value => leaderPickerColors.Contains(value),
                value =>
                {
                    if (!leaderPickerColors.Add(value)) leaderPickerColors.Remove(value);
                    refresh();
                },
                () => { leaderPickerColors.Clear(); refresh(); },
                new Vector2(0.025f, 0.82f), new Vector2(0.62f, 0.862f));

            string[] sets = AllPickerLeaders()
                .Where(id => !SealedLeaderRules.IsRainbowLuffy(id))
                .Select(LeaderSetCode).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(LeaderSetSortKey, StringComparer.OrdinalIgnoreCase).ToArray();
            BuildLeaderSetFilters(modal, sets, refresh,
                new Vector2(0.025f, 0.67f), new Vector2(0.975f, 0.805f));

            var sortRow = SealedUI.Panel(modal, "Sort Controls", Color.clear);
            SealedUI.Stretch(sortRow, new Vector2(0.64f, 0.82f), new Vector2(0.975f, 0.862f));
            var sortLabel = SealedUI.Label(sortRow, "Sort Label", "SORT", 10, SealedUI.Muted,
                TextAnchor.MiddleLeft, true);
            SealedUI.Stretch(sortLabel.rectTransform, new Vector2(0f, 0f), new Vector2(0.10f, 1f));
            string[] sorts = { "NAME", "SET", "LIFE", "POWER" };
            for (int i = 0; i < sorts.Length; i++)
            {
                string sort = sorts[i];
                bool on = string.Equals(leaderPickerSort, sort, StringComparison.OrdinalIgnoreCase);
                var chip = SealedUI.Button(sortRow, sort, on ? SealedUI.Accent : SealedUI.ChipOff,
                    on ? SealedUI.BadgeInk : SealedUI.Ink,
                    () => { leaderPickerSort = sort.ToLowerInvariant(); refresh(); }, 10, on);
                float x = 0.105f + i * 0.135f;
                SealedUI.Stretch(chip, new Vector2(x, 0f), new Vector2(x + 0.125f, 1f));
                SealedUI.Round(chip);
            }
            var direction = SealedUI.Button(sortRow, leaderPickerAscending ? "ASCENDING" : "DESCENDING",
                SealedUI.ChipOff, SealedUI.Ink,
                () => { leaderPickerAscending = !leaderPickerAscending; refresh(); }, 10);
            SealedUI.Stretch(direction, new Vector2(0.67f, 0f), new Vector2(1f, 1f));
            SealedUI.Round(direction);

            var viewport = SealedUI.Panel(modal, "Leader Grid Viewport", new Color(0, 0, 0, 0.12f), true);
            SealedUI.Stretch(viewport, new Vector2(0.025f, 0.035f), new Vector2(0.975f, 0.65f));
            viewport.gameObject.AddComponent<RectMask2D>();

            var content = new GameObject("Leader Grid", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(viewport, false);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;
            var grid = content.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(140f, 198f);
            grid.spacing = new Vector2(12f, 12f);
            grid.padding = new RectOffset(10, 10, 12, 12);
            grid.childAlignment = TextAnchor.UpperCenter;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 10;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.inertia = true;
            scroll.decelerationRate = 0.12f;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.scrollSensitivity = 34f;

            // This is a true overlay, like the in-match preview: it consumes no grid column,
            // ignores raycasts, and is raised above the cards only while one is hovered.
            // Match the in-game dock exactly: fixed against the canvas-right edge instead of the
            // modal or hovered tile, with both the glow and art fitted to the same card-aspect holder.
            var preview = SealedUI.Panel(overlay, "Leader Preview Overlay", Color.clear);
            SealedUI.Stretch(preview, new Vector2(0.76f, 0.195f), new Vector2(0.998f, 0.805f));
            var previewGroup = preview.gameObject.AddComponent<CanvasGroup>();
            previewGroup.blocksRaycasts = false; previewGroup.interactable = false;
            const float previewAspect = 168f / 235f;
            var previewRegion = SealedUI.Panel(preview, "Preview Card Region", Color.clear);
            SealedUI.Stretch(previewRegion, new Vector2(0.05f, 0.02f), new Vector2(0.95f, 0.98f));
            var glowHolder = SealedUI.Panel(previewRegion, "Preview Glow Holder", Color.clear);
            var glowFitter = glowHolder.gameObject.AddComponent<AspectRatioFitter>();
            glowFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            glowFitter.aspectRatio = previewAspect;
            SealedUI.AddCardPreviewGlow(glowHolder);
            var previewArt = SealedUI.Panel(previewRegion, "Art", new Color32(14, 25, 38, 255));
            var artFitter = previewArt.gameObject.AddComponent<AspectRatioFitter>();
            artFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            artFitter.aspectRatio = previewAspect;
            RoundedCardMask.ApplyTo(previewArt.GetComponent<Image>());
            preview.gameObject.SetActive(false);

            Action<string> showPreview = id =>
            {
                ApplyLeaderArt(id, previewArt.GetComponent<Image>());
                preview.gameObject.SetActive(true);
                preview.SetAsLastSibling();
            };
            Action hidePreview = () => preview.gameObject.SetActive(false);

            var filtered = FilteredPickerLeaders();
            var count = SealedUI.Label(modal, "Leader Count",
                $"SHOWING {filtered.Count} / {AllPickerLeaders().Count}  ·  {sets.Length} SETS",
                10, SealedUI.Muted, TextAnchor.MiddleRight, true);
            SealedUI.Stretch(count.rectTransform, new Vector2(0.69f, 0.875f), new Vector2(0.83f, 0.91f));

            foreach (string id in filtered)
                BuildLeaderOption(content, id, selectedId, player, showPreview, hidePreview);
        }

        private List<string> AllPickerLeaders()
        {
            SealedLeaderRules.EnsureRegistered();
            var leaders = CardData.Library
                .Where(kv => string.Equals(kv.Value?.Type, "leader", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(kv.Value?.Rarity, "L", StringComparison.OrdinalIgnoreCase))
                .Select(kv => kv.Key)
                .Concat(new[] { SealedLeaderRules.RainbowLuffyId })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(id => CardData.GetCard(id)?.Name ?? id, StringComparer.OrdinalIgnoreCase)
                .ThenBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return leaders;
        }

        private List<string> FilteredPickerLeaders()
        {
            var rainbow = AllPickerLeaders().FirstOrDefault(SealedLeaderRules.IsRainbowLuffy)
                ?? SealedLeaderRules.RainbowLuffyId;
            var leaders = AllPickerLeaders()
                .Where(id => !SealedLeaderRules.IsRainbowLuffy(id))
                .Where(id => leaderPickerColors.Count == 0 || SealedPool.SplitColors(CardData.GetCard(id)?.Color)
                    .Any(color => leaderPickerColors.Contains(color)))
                .Where(id => leaderPickerSets.Count == 0 || leaderPickerSets.Contains(LeaderSetCode(id)))
                .ToList();

            switch (leaderPickerSort)
            {
                case "set":
                    leaders = leaders.OrderBy(LeaderSetSortKey, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();
                    break;
                case "life":
                    leaders = leaders.OrderBy(id => CardData.GetCard(id)?.Life ?? 0)
                        .ThenBy(id => CardData.GetCard(id)?.Name ?? id, StringComparer.OrdinalIgnoreCase).ToList();
                    break;
                case "power":
                    leaders = leaders.OrderBy(id => CardData.GetCard(id)?.Power ?? 0)
                        .ThenBy(id => CardData.GetCard(id)?.Name ?? id, StringComparer.OrdinalIgnoreCase).ToList();
                    break;
                default:
                    leaders = leaders.OrderBy(id => CardData.GetCard(id)?.Name ?? id, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();
                    break;
            }
            if (!leaderPickerAscending) leaders.Reverse();
            leaders.Insert(0, rainbow); // Rainbow Luffy is a permanent first slot, independent of filters/sort.
            return leaders.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string LeaderSetCode(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return "OTHER";
            int dash = id.IndexOf('-');
            return (dash > 0 ? id.Substring(0, dash) : id).ToUpperInvariant();
        }

        private static string LeaderSetSortKey(string idOrSet)
        {
            string set = idOrSet != null && idOrSet.Contains("-") ? LeaderSetCode(idOrSet) : idOrSet ?? "";
            int split = 0;
            while (split < set.Length && !char.IsDigit(set[split])) split++;
            string prefix = set.Substring(0, split);
            return int.TryParse(set.Substring(split), out int number)
                ? prefix + number.ToString("D4")
                : prefix + set.Substring(split);
        }

        private void BuildLeaderFilterRow(RectTransform parent, string heading, IEnumerable<string> values,
            Func<string, bool> selected, Action<string> toggle, Action clear, Vector2 min, Vector2 max)
        {
            var row = SealedUI.Panel(parent, heading + " Filters", Color.clear);
            SealedUI.Stretch(row, min, max);
            var label = SealedUI.Label(row, "Label", heading, 10, SealedUI.Muted, TextAnchor.MiddleLeft, true);
            SealedUI.Stretch(label.rectTransform, new Vector2(0f, 0f), new Vector2(0.075f, 1f));

            var viewport = SealedUI.Panel(row, "Viewport", Color.clear, true);
            SealedUI.Stretch(viewport, new Vector2(0.075f, 0f), Vector2.one);
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = new GameObject("Chips", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(viewport, false);
            content.anchorMin = new Vector2(0f, 0f); content.anchorMax = new Vector2(0f, 1f);
            content.pivot = new Vector2(0f, 0.5f); content.offsetMin = content.offsetMax = Vector2.zero;
            var layout = content.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 7f; layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = false; layout.childForceExpandWidth = false;
            layout.childControlHeight = true; layout.childForceExpandHeight = true;
            content.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            bool none = !values.Any(selected);
            SealedUI.Chip(content, "ALL", none, clear);
            foreach (string value in values)
                SealedUI.Chip(content, value, selected(value), () => toggle(value));
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = content; scroll.viewport = viewport;
            scroll.horizontal = true; scroll.vertical = false; scroll.inertia = true;
            scroll.decelerationRate = 0.12f; scroll.scrollSensitivity = 24f;
            scroll.movementType = ScrollRect.MovementType.Elastic;
        }

        private void BuildLeaderSetFilters(RectTransform parent, IReadOnlyList<string> sets,
            Action refresh, Vector2 min, Vector2 max)
        {
            var host = SealedUI.Panel(parent, "All Set Filters", Color.clear);
            SealedUI.Stretch(host, min, max);
            var label = SealedUI.Label(host, "Label", "SET", 10, SealedUI.Muted,
                TextAnchor.UpperLeft, true);
            SealedUI.Stretch(label.rectTransform, new Vector2(0f, 0f), new Vector2(0.045f, 1f));

            var gridRoot = SealedUI.Panel(host, "Set Toggle Grid", Color.clear);
            SealedUI.Stretch(gridRoot, new Vector2(0.045f, 0f), Vector2.one);
            var grid = gridRoot.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(96f, 27f);
            grid.spacing = new Vector2(7f, 6f);
            grid.padding = new RectOffset(0, 0, 0, 0);
            grid.childAlignment = TextAnchor.UpperLeft;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 14;

            var all = SealedUI.Button(gridRoot, "ALL", leaderPickerSets.Count == 0 ? SealedUI.Accent : SealedUI.ChipOff,
                leaderPickerSets.Count == 0 ? SealedUI.BadgeInk : SealedUI.Ink,
                () => { leaderPickerSets.Clear(); refresh(); }, 9, leaderPickerSets.Count == 0);
            SealedUI.Round(all);
            foreach (string set in sets)
            {
                string captured = set;
                bool on = leaderPickerSets.Contains(captured);
                var chip = SealedUI.Button(gridRoot, captured, on ? SealedUI.Accent : SealedUI.ChipOff,
                    on ? SealedUI.BadgeInk : SealedUI.Ink,
                    () =>
                    {
                        if (!leaderPickerSets.Add(captured)) leaderPickerSets.Remove(captured);
                        refresh();
                    }, 9, on);
                SealedUI.Round(chip);
            }
        }

        private void BuildLeaderOption(RectTransform parent, string id, string selectedId, bool player,
            Action<string> onHover, Action onExit)
        {
            bool banned = SealedLeaderRules.IsBannedLeader(id);
            bool selected = string.Equals(id, selectedId, StringComparison.OrdinalIgnoreCase);
            var tile = SealedUI.Panel(parent, id,
                selected ? new Color32(30, 80, 96, 255) : new Color32(24, 39, 56, 255), true);
            SealedUI.Round(tile);
            SealedUI.Border(tile, selected ? SealedUI.Accent : new Color32(53, 70, 89, 255), selected ? 3f : 1f);

            var art = SealedUI.Panel(tile, "Art", new Color32(14, 25, 38, 255));
            SealedUI.Stretch(art, new Vector2(0.12f, 0.19f), new Vector2(0.88f, 0.97f));
            RoundedCardMask.ApplyTo(art.GetComponent<Image>());
            ApplyLeaderArt(id, art.GetComponent<Image>(), true);

            var def = CardData.GetCard(id);
            var name = SealedUI.Label(tile, "Name", def?.Name ?? id, 10,
                banned ? new Color32(126, 132, 143, 255) : SealedUI.Ink,
                TextAnchor.UpperCenter, true);
            SealedUI.Stretch(name.rectTransform, new Vector2(0.03f, 0.075f), new Vector2(0.97f, 0.19f));
            var code = SealedUI.Label(tile, "Code",
                SealedLeaderRules.IsRainbowLuffy(id) ? "RELEASE EVENT" : id,
                9, banned ? new Color32(112, 118, 128, 255) : SealedUI.Muted,
                TextAnchor.MiddleCenter, true);
            SealedUI.Stretch(code.rectTransform, new Vector2(0.03f, 0.005f), new Vector2(0.97f, 0.08f));

            if (banned)
            {
                var shade = SealedUI.Panel(tile, "Banned Shade", new Color(0.03f, 0.04f, 0.055f, 0.68f));
                SealedUI.Fill(shade);
                var badge = SealedUI.Label(shade, "Banned", "BANNED", 13, SealedUI.Bad,
                    TextAnchor.MiddleCenter, true);
                SealedUI.Stretch(badge.rectTransform, new Vector2(0.08f, 0.38f), new Vector2(0.92f, 0.62f));
            }
            else
            {
                var button = tile.gameObject.AddComponent<Button>();
                button.transition = Selectable.Transition.ColorTint;
                button.onClick.AddListener(() => SelectLeader(player, id));
            }

            tile.gameObject.AddComponent<SealedLeaderTileInput>().Init(
                () => onHover?.Invoke(id), onExit, tile.GetComponentInParent<ScrollRect>());
        }

        private void SelectLeader(bool player, string id)
        {
            if (string.IsNullOrEmpty(id) || SealedLeaderRules.IsBannedLeader(id)) return;
            if (leaderPickerOnly)
            {
                leaderPickerChosen?.Invoke(id);
                Destroy(gameObject);
                return;
            }
            if (player) chosenPlayerLeader = id;
            else chosenOpponentLeader = id;
            ShowPicker();
        }

        private List<string> PickerLeaders()
        {
            SealedLeaderRules.EnsureRegistered();
            var leaders = new List<string> { SealedLeaderRules.RainbowLuffyId };

            // Free-select Sealed lets either seat bring any legal Leader, not merely a Leader
            // printed in the booster being opened. Keep the selected set's Leaders near the front
            // for convenience, then expose the complete legal roster to both selectors.
            if (chosenProduct != null)
                leaders.AddRange(chosenProduct.LegalLeaders()
                    .Where(id => !SealedLeaderRules.IsBannedLeader(id)));
            leaders.AddRange(SealedLeaderRules.LegalLeaders(SealedLeaderMode.FreeSelect, null)
                .OrderBy(id => CardData.GetCard(id)?.Name ?? id, StringComparer.OrdinalIgnoreCase)
                .ThenBy(id => id, StringComparer.OrdinalIgnoreCase));
            return leaders.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private void EnsureLeaderChoices()
        {
            var leaders = PickerLeaders();
            if (leaders.Count == 0) return;
            if (!leaders.Contains(chosenPlayerLeader)) chosenPlayerLeader = leaders[0];
            if (!leaders.Contains(chosenOpponentLeader)) chosenOpponentLeader = leaders[Mathf.Min(1, leaders.Count - 1)];
        }

        private void SelectProduct(int index)
        {
            var products = SealedCatalog.Available();
            if (products.Count == 0) return;
            index = (index % products.Count + products.Count) % products.Count;
            chosenProduct = products[index];
            if (productPickerOnly) ShowProductPicker();
            else
            {
                EnsureLeaderChoices();
                ShowPicker();
            }
        }

        private async void ApplyLeaderArt(string id, Image image, bool preferThumbnail = false)
        {
            if (image == null || string.IsNullOrEmpty(id)) return;
            var request = image.GetComponent<SealedLeaderArtRequest>()
                ?? image.gameObject.AddComponent<SealedLeaderArtRequest>();
            request.CardId = id;
            var cache = preferThumbnail ? leaderThumbArt : leaderArt;
            if (cache.TryGetValue(id, out var cached) && cached != null)
            { image.sprite = cached; image.color = Color.white; image.preserveAspect = true; return; }
            try
            {
                while (!CardAssets.Ready) await System.Threading.Tasks.Task.Yield();
                string rel = preferThumbnail ? CardAssets.FirstExisting(CardAssets.ThumbCandidate(id)) : null;
                if (string.IsNullOrEmpty(rel)) rel = CardAssets.FirstExisting(CardAssets.ArtCandidates(id));
                var bytes = await CardAssets.ReadBytesAsync(rel);
                if (bytes == null || bytes.Length == 0 || image == null || request == null
                    || !string.Equals(request.CardId, id, StringComparison.OrdinalIgnoreCase)) return;
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(bytes)) return;
                var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                cache[id] = sprite;
                if (image != null && request != null
                    && string.Equals(request.CardId, id, StringComparison.OrdinalIgnoreCase))
                { image.sprite = sprite; image.color = Color.white; image.preserveAspect = true; }
            }
            catch (Exception e) { Debug.LogWarning($"[Sealed] Leader art failed for {id}: {e.Message}"); }
        }

    }

    internal sealed class SealedLeaderArtRequest : MonoBehaviour
    {
        public string CardId;
    }

    /// <summary>Leader tiles own hover, but wheel input must still drive their containing roster.</summary>
    internal sealed class SealedLeaderTileInput : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IScrollHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private Action enter, exit;
        private ScrollRect scroll;
        private bool middleDragging;

        public void Init(Action onEnter, Action onExit, ScrollRect owner)
        {
            enter = onEnter; exit = onExit; scroll = owner;
        }

        public void OnPointerEnter(PointerEventData eventData) => enter?.Invoke();
        public void OnPointerExit(PointerEventData eventData) => exit?.Invoke();
        public void OnScroll(PointerEventData eventData) => scroll?.OnScroll(eventData);

        public void OnBeginDrag(PointerEventData eventData)
        {
            middleDragging = eventData.button == PointerEventData.InputButton.Middle;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!middleDragging || scroll == null || scroll.content == null || scroll.viewport == null) return;
            float hiddenHeight = Mathf.Max(1f, scroll.content.rect.height - scroll.viewport.rect.height);
            scroll.verticalNormalizedPosition = Mathf.Clamp01(
                scroll.verticalNormalizedPosition - eventData.delta.y / hiddenHeight);
            scroll.StopMovement();
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Middle) middleDragging = false;
        }
    }

    /// <summary>
    /// Pointer-driven, looping pack wheel. Position is measured in pack slots so the
    /// gesture remains resolution-independent. A fast release coasts; a slow release
    /// magnetically settles on the closest pack.
    /// </summary>
    internal sealed class SealedPackCarousel : MonoBehaviour, IBeginDragHandler, IDragHandler,
        IEndDragHandler, IPointerDownHandler, IPointerUpHandler, IScrollHandler
    {
        internal sealed class Item
        {
            public int Index;
            public RectTransform Rect;
            public Image Image;
            public Text Label;
            public CanvasGroup Group;
        }

        private RectTransform viewport;
        private List<Item> items;
        private Action<int> onSettled;
        private float position;
        private float velocity;
        private float snapVelocity;
        private float lastPointerX;
        private float pointerDownX;
        private bool dragging;
        private int lastNotifiedIndex;

        private const float SpacingFraction = 0.145f;
        private const float CoastDrag = 2.25f;
        private const float SnapTime = 0.16f;
        private const float MaxVelocity = 11f;

        internal void Initialize(RectTransform viewportRect, List<Item> packItems, int selected,
            Action<int> settled)
        {
            viewport = viewportRect;
            items = packItems;
            position = selected;
            lastNotifiedIndex = selected;
            onSettled = settled;
            Layout();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            velocity = 0f;
            snapVelocity = 0f;
            pointerDownX = lastPointerX = LocalX(eventData);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            dragging = true;
            velocity = 0f;
            snapVelocity = 0f;
            pointerDownX = lastPointerX = LocalX(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            float x = LocalX(eventData);
            float spacing = SlotSpacing();
            float slotDelta = -(x - lastPointerX) / spacing;
            position += slotDelta;
            float dt = Mathf.Max(Time.unscaledDeltaTime, 1f / 240f);
            float instantaneous = slotDelta / dt;
            velocity = Mathf.Lerp(velocity, instantaneous, 0.48f);
            velocity = Mathf.Clamp(velocity, -MaxVelocity, MaxVelocity);
            lastPointerX = x;
            NormalizePosition();
            Layout();
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            dragging = false;
            if (Mathf.Abs(velocity) < 0.22f) velocity = 0f;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (dragging) return;
            float releasedX = LocalX(eventData);
            if (Mathf.Abs(releasedX - pointerDownX) > 8f) return;
            int offset = Mathf.RoundToInt(releasedX / SlotSpacing());
            if (offset == 0) return;
            position = Mathf.Round(position) + offset;
            velocity = 0f;
            snapVelocity = 0f;
        }

        public void OnScroll(PointerEventData eventData)
        {
            float impulse = Mathf.Abs(eventData.scrollDelta.y) > Mathf.Abs(eventData.scrollDelta.x)
                ? -eventData.scrollDelta.y
                : eventData.scrollDelta.x;
            // Apply both displacement and momentum so a single mouse-wheel notch is
            // visible immediately while a trackpad stream builds into a proper spin.
            position += impulse * 0.16f;
            velocity = Mathf.Clamp(velocity + impulse * 1.55f, -MaxVelocity, MaxVelocity);
            snapVelocity = 0f;
            NormalizePosition();
            Layout();
        }

        private void Update()
        {
            if (dragging || items == null || items.Count == 0) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);

            if (Mathf.Abs(velocity) > 1.05f)
            {
                position += velocity * dt;
                velocity *= Mathf.Exp(-CoastDrag * dt);
                snapVelocity = 0f;
            }
            else
            {
                float target = Mathf.Round(position);
                position = Mathf.SmoothDamp(position, target, ref snapVelocity, SnapTime,
                    Mathf.Infinity, dt);
                velocity = 0f;
                if (Mathf.Abs(position - target) < 0.001f && Mathf.Abs(snapVelocity) < 0.01f)
                {
                    position = target;
                    snapVelocity = 0f;
                    NotifySettled();
                }
            }

            NormalizePosition();
            Layout();
        }

        private void NotifySettled()
        {
            int count = items.Count;
            int index = ((Mathf.RoundToInt(position) % count) + count) % count;
            if (index == lastNotifiedIndex) return;
            lastNotifiedIndex = index;
            onSettled?.Invoke(index);
        }

        private void Layout()
        {
            if (viewport == null || items == null || items.Count == 0) return;
            float width = Mathf.Max(viewport.rect.width, 800f);
            float height = Mathf.Max(viewport.rect.height, 260f);
            float spacing = width * SpacingFraction;
            float baseWidth = Mathf.Min(width * 0.16f, height * 0.62f);
            float baseHeight = height * 0.88f;
            int count = items.Count;

            foreach (var item in items)
            {
                float relative = WrappedDelta(item.Index, position, count);
                float distance = Mathf.Abs(relative);
                bool visible = distance < 4.15f;
                item.Rect.gameObject.SetActive(visible);
                if (!visible) continue;

                float scale = Mathf.Lerp(1f, 0.58f, Mathf.Clamp01(distance / 3.4f));
                float y = -Mathf.Pow(Mathf.Min(distance, 3.5f), 1.35f) * height * 0.035f;
                item.Rect.sizeDelta = new Vector2(baseWidth, baseHeight);
                item.Rect.anchoredPosition = new Vector2(relative * spacing, y);
                item.Rect.localScale = Vector3.one * scale;
                item.Group.alpha = Mathf.Lerp(1f, 0.26f, Mathf.Clamp01(distance / 3.5f));
                item.Group.blocksRaycasts = false;
                item.Image.color = Color.white;
                item.Label.color = Color.Lerp(SealedUI.Ink, SealedUI.Muted,
                    Mathf.Clamp01(distance / 2f));
            }

            // Draw distant packs first and the focused pack last so cards overlap naturally.
            foreach (var item in items.Where(i => i.Rect.gameObject.activeSelf)
                         .OrderByDescending(i => Mathf.Abs(WrappedDelta(i.Index, position, count))))
                item.Rect.SetAsLastSibling();
        }

        private float LocalX(PointerEventData eventData)
        {
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(
                viewport, eventData.position, eventData.pressEventCamera, out var point) ? point.x : 0f;
        }

        private float SlotSpacing() => Mathf.Max(viewport.rect.width, 800f) * SpacingFraction;

        private void NormalizePosition()
        {
            int count = items.Count;
            if (position > count * 2f || position < -count * 2f)
                position = Mathf.Repeat(position, count);
        }

        private static float WrappedDelta(int index, float center, int count)
        {
            return Mathf.Repeat(index - center + count * 0.5f, count) - count * 0.5f;
        }
    }
}
