// One Piece TCG — Sealed / Pre-Release: the PACK OPENING sequence.
//
// The feel this is going for: a pack sits centre-screen breathing slightly, you tap, it shudders,
// tears along its heat-seal strip, light pours out of the tear, and the cards come out ONE AT A TIME
// — sliding up out of the pack, flipping face-up, and stacking into the pool. Hits get celebrated:
// a Super Rare fires a gold burst, a Secret Rare gets a bigger, longer, prismatic one, and a
// parallel gets a cool silver shimmer. Six packs, then everything lands in the deck builder.
//
// Everything here is unscaled-time coroutines over uGUI primitives, self-contained so it does not
// depend on GameManager's private UI helpers. Cards are loaded through CardAssets (the same pipeline
// the match view uses) and degrade to a coloured placeholder if art is missing, so a missing image
// never blocks the sequence.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sealed
{
    public sealed class SealedPackOpening : MonoBehaviour
    {
        // Rarity celebration tiers. A pack's reveal builds to its best card, so the tier is decided
        // per card as it flips.
        private enum HitTier { None, Parallel, SuperRare, SecretRare }

        private RectTransform root;
        private SealedPool pool;
        private Action onComplete;
        private bool skipRequested;
        private readonly Dictionary<string, Sprite> spriteCache = new Dictionary<string, Sprite>();
        /// <summary>The real OPTCG card back, shown while a card is still face-down. Same asset the
        /// match view uses (StreamingAssets/Cards/optcg_card_back.jpg), so a higher-quality file
        /// dropped in there is picked up everywhere at once.</summary>
        private Sprite backSprite;

        private static readonly Color Ink = new Color32(238, 242, 247, 255);
        private static readonly Color Muted = new Color32(159, 171, 190, 255);
        private static readonly Color Accent = new Color32(79, 195, 224, 255);
        private static readonly Color Gold = new Color32(226, 188, 74, 255);
        private static readonly Color Silver = new Color32(206, 214, 226, 255);

        /// <summary>Run the whole six-pack sequence under <paramref name="parent"/>, then call
        /// <paramref name="done"/>. Safe to skip at any point.</summary>
        public void Begin(RectTransform parent, SealedPool sealedPool, Action done)
        {
            root = parent;
            pool = sealedPool;
            onComplete = done;
            StartCoroutine(RunAll());
        }

        public void Skip() => skipRequested = true;

        // ---- Sequence ------------------------------------------------------------------------

        private IEnumerator RunAll()
        {
            var backdrop = Panel(root, "Opening Backdrop", new Color32(6, 10, 16, 252));
            Stretch(backdrop, Vector2.zero, Vector2.one);

            var counter = Text(backdrop, "Pack Counter", "", 26, Muted, TextAnchor.UpperCenter);
            Stretch(counter.rectTransform, new Vector2(0.1f, 0.90f), new Vector2(0.9f, 0.96f));

            AddButton(backdrop, "SKIP", new Vector2(0.86f, 0.03f), new Vector2(0.98f, 0.09f), Skip);

            // PRELOAD every card's art before the first pack. The reveal draws a card the instant it
            // flips, so kicking an async load at that moment always lost the race and the card fell
            // back to its name placeholder — which is why almost nothing showed art. Every id is known
            // up front, so fetch them all first and the sequence then runs entirely from cache.
            counter.text = "PREPARING PACKS…";
            KickBackLoad();
            yield return StartCoroutine(PreloadArt(pool.Packs.SelectMany(p => p.Cards).Select(c => c.CardId)));

            for (int i = 0; i < pool.Packs.Count && !skipRequested; i++)
            {
                counter.text = $"PACK {i + 1} / {pool.Packs.Count}";
                yield return StartCoroutine(OpenOnePack(backdrop, pool.Packs[i]));
            }

            // Skipping still shows what you got — it skips the ceremony, not the information.
            if (skipRequested) yield return StartCoroutine(ShowEverythingAtOnce(backdrop));

            if (backdrop != null) Destroy(backdrop.gameObject);
            onComplete?.Invoke();
        }

        private IEnumerator OpenOnePack(RectTransform parent, SealedPack pack)
        {
            var stage = Panel(parent, "Pack Stage", new Color(0, 0, 0, 0));
            Stretch(stage, Vector2.zero, Vector2.one);

            // ---- the pack itself ----
            var packRt = Panel(stage, "Pack", Color.white);
            packRt.anchorMin = packRt.anchorMax = new Vector2(0.5f, 0.52f);
            packRt.pivot = new Vector2(0.5f, 0.5f);
            packRt.sizeDelta = new Vector2(430f, 601f);
            var packImg = packRt.GetComponent<Image>();
            packImg.sprite = SealedPackArt.For(pool.SetCode);
            packImg.preserveAspect = true;

            var hint = Text(stage, "Hint", "click to open", 18, Muted, TextAnchor.MiddleCenter);
            Stretch(hint.rectTransform, new Vector2(0.3f, 0.10f), new Vector2(0.7f, 0.16f));

            // Idle breathing until the player taps (or ~1.6s passes, so it never blocks).
            bool tapped = false;
            AddFullscreenClick(stage, () => tapped = true);
            float idle = 0f;
            while (!tapped && !skipRequested && idle < 0.7f)
            {
                idle += Time.unscaledDeltaTime;
                float b = 1f + Mathf.Sin(idle * 2.2f) * 0.015f;
                if (packRt != null) packRt.localScale = new Vector3(b, b, 1f);
                yield return null;
            }
            if (hint != null) Destroy(hint.gameObject);
            if (skipRequested) { Destroy(stage.gameObject); yield break; }

            // ---- shudder, then tear ----
            yield return StartCoroutine(Shudder(packRt, 0.18f));
            yield return StartCoroutine(Tear(stage, packRt, pack));

            // ---- cards out, one at a time ----
            var landed = new List<RectTransform>();
            foreach (var card in pack.Cards)
            {
                if (skipRequested) break;
                yield return StartCoroutine(RevealCard(stage, card, landed));
            }

            if (!skipRequested) yield return WaitUnscaled(0.20f);
            if (stage != null) Destroy(stage.gameObject);
        }

        /// <summary>Small violent shake — the moment before the seal gives.</summary>
        private IEnumerator Shudder(RectTransform rt, float dur)
        {
            float t = 0f;
            Vector2 home = rt != null ? rt.anchoredPosition : Vector2.zero;
            while (t < dur && rt != null)
            {
                t += Time.unscaledDeltaTime;
                float amp = Mathf.Lerp(3.5f, 9f, t / dur);
                rt.anchoredPosition = home + new Vector2(
                    Mathf.Sin(t * 90f) * amp, Mathf.Cos(t * 77f) * amp * 0.5f);
                yield return null;
            }
            if (rt != null) rt.anchoredPosition = home;
        }

        /// <summary>Split the pack along its heat-seal strip: the top slice rips away and tumbles off
        /// while light floods out of the tear.</summary>
        private IEnumerator Tear(RectTransform stage, RectTransform packRt, SealedPack pack)
        {
            if (packRt == null) yield break;

            // The torn-off top strip, cloned from the pack so it matches whatever art is in use.
            var strip = Panel(stage, "Pack Top", Color.white);
            strip.anchorMin = strip.anchorMax = new Vector2(0.5f, 0.52f);
            strip.pivot = new Vector2(0.5f, 0.5f);
            strip.sizeDelta = new Vector2(436f, 76f);
            strip.anchoredPosition = new Vector2(0f, 282f);
            var stripImg = strip.GetComponent<Image>();
            stripImg.sprite = SealedPackArt.For(pool.SetCode);
            stripImg.color = new Color(0.8f, 0.8f, 0.85f, 1f);

            // Light pouring out of the tear, tinted by the best card in the pack.
            var tier = BestTier(pack);
            var glow = Panel(stage, "Tear Glow", TierColour(tier));
            glow.anchorMin = glow.anchorMax = new Vector2(0.5f, 0.52f);
            glow.pivot = new Vector2(0.5f, 0.5f);
            glow.sizeDelta = new Vector2(70f, 34f);
            glow.anchoredPosition = new Vector2(0f, 242f);
            var glowImg = glow.GetComponent<Image>();

            float t = 0f;
            const float dur = 0.32f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / dur);

                if (strip != null)
                {
                    strip.anchoredPosition = new Vector2(Mathf.Lerp(0f, 230f, k), Mathf.Lerp(282f, 480f, k));
                    strip.localRotation = Quaternion.Euler(0, 0, Mathf.Lerp(0f, 38f, k));
                    var c = stripImg.color; c.a = 1f - k; stripImg.color = c;
                }
                if (glow != null)
                {
                    glow.sizeDelta = new Vector2(Mathf.Lerp(70f, 560f, k), Mathf.Lerp(34f, 220f, k));
                    var gc = glowImg.color; gc.a = Mathf.Sin(k * Mathf.PI) * 0.85f; glowImg.color = gc;
                }
                yield return null;
            }
            if (strip != null) Destroy(strip.gameObject);
            if (glow != null) Destroy(glow.gameObject);
        }

        /// <summary>One card rises out of the pack, flips face-up, celebrates if it is a hit, then
        /// tucks into the growing stack at the bottom of the screen.</summary>
        private IEnumerator RevealCard(RectTransform stage, PulledCard card, List<RectTransform> landed)
        {
            var holder = Panel(stage, "Card " + card.CardId, new Color(0, 0, 0, 0));
            holder.anchorMin = holder.anchorMax = new Vector2(0.5f, 0.52f);
            holder.pivot = new Vector2(0.5f, 0.5f);
            holder.sizeDelta = new Vector2(400f, 559f);
            holder.anchoredPosition = new Vector2(0f, -30f);

            // Face-DOWN to begin with: a card coming out of a pack is a card back until it turns over.
            // Previously this was a flat navy rectangle, which lost the whole "what did I pull" moment.
            var face = Panel(holder, "Face", Color.white);
            Stretch(face, Vector2.zero, Vector2.one);
            var faceImg = face.GetComponent<Image>();
            if (backSprite != null) { faceImg.sprite = backSprite; faceImg.preserveAspect = true; }
            else faceImg.color = new Color32(24, 36, 52, 255);

            // Rise out of the pack.
            float t = 0f;
            while (t < 0.10f)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / 0.10f);
                holder.anchoredPosition = new Vector2(0f, Mathf.Lerp(-60f, 60f, Ease(k)));
                yield return null;
            }

            // Flip: squash to zero width, swap in the face, expand back.
            t = 0f;
            while (t < 0.11f)
            {
                t += Time.unscaledDeltaTime;
                holder.localScale = new Vector3(1f - Mathf.Clamp01(t / 0.11f), 1f, 1f);
                yield return null;
            }

            var sprite = GetSprite(card.CardId);
            if (sprite != null) { faceImg.sprite = sprite; faceImg.color = Color.white; faceImg.preserveAspect = true; }
            else AddLabel(face, card);

            if (card.IsParallel) AddFoilTint(face);

            t = 0f;
            while (t < 0.13f)
            {
                t += Time.unscaledDeltaTime;
                holder.localScale = new Vector3(Mathf.Clamp01(t / 0.13f), 1f, 1f);
                yield return null;
            }
            holder.localScale = Vector3.one;

            // Celebrate a hit.
            var tier = TierOf(card);
            if (tier != HitTier.None) yield return StartCoroutine(Celebrate(stage, holder, tier));
            else yield return WaitUnscaled(0.10f);   // a beat to read the card before it tucks away

            // Tuck into the stack along the bottom. For an ordinary card this runs DETACHED so the next
            // card starts rising immediately — the cards flow out continuously instead of the sequence
            // stopping dead on every common. Only a hit holds the sequence, which is what makes a hit
            // feel like one. Fully sequential it was ~1s a card, so a six-pack kit ran over a minute.
            int idx = landed.Count;
            landed.Add(holder);
            var tuck = TuckAway(holder, idx);
            if (tier != HitTier.None) yield return StartCoroutine(tuck);
            else StartCoroutine(tuck);
        }

        private IEnumerator TuckAway(RectTransform holder, int idx)
        {
            var target = new Vector2(-742f + idx * 135f, -352f);
            Vector2 from = holder != null ? holder.anchoredPosition : Vector2.zero;
            float t = 0f;
            while (t < 0.14f && holder != null)
            {
                t += Time.unscaledDeltaTime;
                float k = Ease(Mathf.Clamp01(t / 0.14f));
                holder.anchoredPosition = Vector2.Lerp(from, target, k);
                holder.localScale = Vector3.one * Mathf.Lerp(1f, 0.31f, k);
                yield return null;
            }
            if (holder != null)
            {
                holder.anchoredPosition = target;
                holder.localScale = Vector3.one * 0.31f;
            }
        }

        /// <summary>The payoff. Each tier looks distinct at a glance: silver shimmer for a parallel,
        /// a gold burst for a Super Rare, and a bigger, longer, prismatic eruption for a Secret Rare.</summary>
        private IEnumerator Celebrate(RectTransform stage, RectTransform card, HitTier tier)
        {
            int count = tier switch { HitTier.SecretRare => 46, HitTier.SuperRare => 26, _ => 14 };
            float life = tier switch { HitTier.SecretRare => 0.85f, HitTier.SuperRare => 0.50f, _ => 0.30f };
            float reach = tier switch { HitTier.SecretRare => 330f, HitTier.SuperRare => 220f, _ => 150f };

            // Screen flare — only the big two get one, and only SEC gets a full-screen wash.
            if (tier != HitTier.Parallel)
            {
                var flare = Panel(stage, "Flare", TierColour(tier));
                if (tier == HitTier.SecretRare) Stretch(flare, Vector2.zero, Vector2.one);
                else
                {
                    flare.anchorMin = flare.anchorMax = new Vector2(0.5f, 0.52f);
                    flare.pivot = new Vector2(0.5f, 0.5f);
                    flare.sizeDelta = new Vector2(420f, 420f);
                }
                StartCoroutine(FadeOut(flare, tier == HitTier.SecretRare ? 0.55f : 0.35f, 0.5f));
            }

            // Sparks.
            var sparks = new List<RectTransform>();
            for (int i = 0; i < count; i++)
            {
                var s = Panel(stage, "Spark", SparkColour(tier, i));
                s.anchorMin = s.anchorMax = new Vector2(0.5f, 0.52f);
                s.pivot = new Vector2(0.5f, 0.5f);
                float size = tier == HitTier.SecretRare ? UnityEngine.Random.Range(5f, 13f) : UnityEngine.Random.Range(4f, 9f);
                s.sizeDelta = new Vector2(size, size);
                sparks.Add(s);
            }

            var angles = new float[sparks.Count];
            var dists = new float[sparks.Count];
            for (int i = 0; i < sparks.Count; i++)
            {
                angles[i] = (360f / sparks.Count) * i + UnityEngine.Random.Range(-9f, 9f);
                dists[i] = reach * UnityEngine.Random.Range(0.55f, 1f);
            }

            // Card kick — a quick pop so the hit registers even without the particles.
            float t = 0f;
            while (t < life)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / life);

                for (int i = 0; i < sparks.Count; i++)
                {
                    if (sparks[i] == null) continue;
                    float d = Mathf.Lerp(0f, dists[i], Ease(k));
                    float rad = angles[i] * Mathf.Deg2Rad;
                    sparks[i].anchoredPosition = new Vector2(Mathf.Cos(rad) * d, Mathf.Sin(rad) * d * 0.85f);
                    var img = sparks[i].GetComponent<Image>();
                    var c = img.color; c.a = 1f - k; img.color = c;
                    sparks[i].localScale = Vector3.one * Mathf.Lerp(1.2f, 0.25f, k);
                }

                if (card != null)
                {
                    float pop = 1f + Mathf.Sin(Mathf.Clamp01(t / 0.25f) * Mathf.PI) * (tier == HitTier.SecretRare ? 0.22f : 0.12f);
                    card.localScale = new Vector3(pop, pop, 1f);
                }
                yield return null;
            }

            foreach (var s in sparks) if (s != null) Destroy(s.gameObject);
            if (card != null) card.localScale = Vector3.one;
        }

        /// <summary>Skip path: lay the whole pool out at once so skipping never costs information.</summary>
        private IEnumerator ShowEverythingAtOnce(RectTransform parent)
        {
            var grid = Panel(parent, "All Cards", new Color(0, 0, 0, 0));
            Stretch(grid, new Vector2(0.04f, 0.10f), new Vector2(0.96f, 0.88f));
            var layout = grid.gameObject.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(122f, 170f);
            layout.spacing = new Vector2(7f, 7f);
            layout.childAlignment = TextAnchor.UpperCenter;

            foreach (var card in pool.Packs.SelectMany(p => p.Cards))
            {
                var cell = Panel(grid, card.CardId, new Color32(24, 36, 52, 255));
                var img = cell.GetComponent<Image>();
                var sprite = GetSprite(card.CardId);
                if (sprite != null) { img.sprite = sprite; img.color = Color.white; img.preserveAspect = true; }
                else AddLabel(cell, card);
            }
            yield return WaitUnscaled(0.05f);
        }

        // ---- Tiers ---------------------------------------------------------------------------

        private static HitTier TierOf(PulledCard c) =>
            c.RarityCode == Rarity.SecretRare || c.RarityCode == Rarity.TreasureRare ? HitTier.SecretRare
            : c.RarityCode == Rarity.SuperRare || c.RarityCode == Rarity.Special ? HitTier.SuperRare
            : c.IsParallel ? HitTier.Parallel
            : HitTier.None;

        private static HitTier BestTier(SealedPack pack)
        {
            var best = HitTier.None;
            foreach (var c in pack.Cards) { var t = TierOf(c); if (t > best) best = t; }
            return best;
        }

        private static Color TierColour(HitTier tier) => tier switch
        {
            HitTier.SecretRare => new Color(1f, 0.72f, 1f, 0.5f),   // prismatic wash
            HitTier.SuperRare => new Color(Gold.r, Gold.g, Gold.b, 0.42f),
            HitTier.Parallel => new Color(Silver.r, Silver.g, Silver.b, 0.32f),
            _ => new Color(1f, 1f, 1f, 0.18f),
        };

        /// <summary>SEC sparks cycle the whole spectrum so the burst reads as prismatic rather than
        /// just "gold but more"; SR stays warm gold; a parallel is cool silver.</summary>
        private static Color SparkColour(HitTier tier, int i) => tier switch
        {
            HitTier.SecretRare => Color.HSVToRGB((i * 0.13f) % 1f, 0.65f, 1f),
            HitTier.SuperRare => Color.Lerp(Gold, Color.white, (i % 3) * 0.25f),
            _ => Color.Lerp(Silver, Color.white, (i % 2) * 0.35f),
        };

        // ---- Art -----------------------------------------------------------------------------

        /// <summary>Fetch every distinct card's art up front, so the reveal never races a load.
        /// Bounded so a missing/slow file can never hang the sequence.</summary>
        private IEnumerator PreloadArt(IEnumerable<string> cardIds)
        {
            var ids = cardIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            int pending = 0;
            foreach (var id in ids)
            {
                if (spriteCache.ContainsKey(id)) continue;
                spriteCache[id] = null;
                pending++;
                KickLoad(id, () => pending--);
            }

            float waited = 0f;
            while (pending > 0 && waited < 8f && !skipRequested)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        /// <summary>Load the shared card back. Same candidates and same order the match view uses, so
        /// whatever file is dropped in for the board is what the pack reveal shows too.</summary>
        private async void KickBackLoad()
        {
            try
            {
                while (!CardAssets.Ready) await System.Threading.Tasks.Task.Yield();
                var rel = CardAssets.FirstExisting(new[]
                {
                    "optcg_card_back.jpg", "optcg_card_back.png", "backs/CardBackRegular.png",
                });
                if (string.IsNullOrEmpty(rel)) return;
                var bytes = await CardAssets.ReadBytesAsync(rel);
                if (bytes == null || bytes.Length == 0) return;
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(bytes)) return;
                backSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            }
            catch (Exception e) { Debug.LogWarning("[SealedPackOpening] card back load failed: " + e.Message); }
        }

        private Sprite GetSprite(string cardId)
        {
            if (string.IsNullOrEmpty(cardId)) return null;
            if (spriteCache.TryGetValue(cardId, out var s)) return s;
            spriteCache[cardId] = null;      // placeholder; the async load fills it in
            KickLoad(cardId);
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
            catch (Exception e) { Debug.LogWarning($"[SealedPackOpening] art load failed for {cardId}: {e.Message}"); }
            finally { onDone?.Invoke(); }
        }

        private void AddLabel(RectTransform parent, PulledCard card)
        {
            var t = Text(parent, "Label", $"{CardData.GetCard(card.CardId)?.Name ?? card.CardId}\n{card.RarityCode}",
                11, Ink, TextAnchor.MiddleCenter);
            Stretch(t.rectTransform, new Vector2(0.06f, 0.06f), new Vector2(0.94f, 0.94f));
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
        }

        private void AddFoilTint(RectTransform face)
        {
            var foil = Panel(face, "Foil", new Color(0.75f, 0.85f, 1f, 0.22f));
            Stretch(foil, Vector2.zero, Vector2.one);
            foil.GetComponent<Image>().raycastTarget = false;
        }

        // ---- Tiny self-contained uGUI helpers -------------------------------------------------

        private static float Ease(float k) => 1f - Mathf.Pow(1f - Mathf.Clamp01(k), 3f);

        private static IEnumerator WaitUnscaled(float seconds)
        {
            float t = 0f;
            while (t < seconds) { t += Time.unscaledDeltaTime; yield return null; }
        }

        private static IEnumerator FadeOut(RectTransform rt, float from, float dur)
        {
            var img = rt != null ? rt.GetComponent<Image>() : null;
            float t = 0f;
            while (t < dur && img != null)
            {
                t += Time.unscaledDeltaTime;
                var c = img.color; c.a = Mathf.Lerp(from, 0f, t / dur); img.color = c;
                yield return null;
            }
            if (rt != null) Destroy(rt.gameObject);
        }

        private static RectTransform Panel(RectTransform parent, string name, Color colour)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = colour;
            img.raycastTarget = false;
            return rt;
        }

        private static Text Text(RectTransform parent, string name, string value, int size, Color colour, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.text = value;
            t.fontSize = size;
            t.color = colour;
            t.alignment = anchor;
            t.raycastTarget = false;
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return t;
        }

        private static void Stretch(RectTransform rt, Vector2 min, Vector2 max)
        {
            rt.anchorMin = min; rt.anchorMax = max;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        private void AddFullscreenClick(RectTransform parent, Action onClick)
        {
            var catcher = Panel(parent, "Click Catcher", new Color(0, 0, 0, 0));
            Stretch(catcher, Vector2.zero, Vector2.one);
            catcher.GetComponent<Image>().raycastTarget = true;
            catcher.gameObject.AddComponent<Button>().onClick.AddListener(() => onClick?.Invoke());
        }

        private void AddButton(RectTransform parent, string label, Vector2 min, Vector2 max, Action onClick)
        {
            var rt = Panel(parent, label + " Button", new Color32(40, 54, 72, 235));
            Stretch(rt, min, max);
            rt.GetComponent<Image>().raycastTarget = true;
            var t = Text(rt, "Label", label, 12, Ink, TextAnchor.MiddleCenter);
            Stretch(t.rectTransform, Vector2.zero, Vector2.one);
            rt.gameObject.AddComponent<Button>().onClick.AddListener(() => onClick?.Invoke());
        }
    }
}
