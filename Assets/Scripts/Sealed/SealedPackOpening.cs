// One Piece TCG — Sealed / Pre-Release: the PACK OPENING sequence.
//
// The pack stays physically still while the player swipes across its top seal. On release, only the
// narrow heat-seal ribbon peels away and cards come out ONE AT A TIME. Rarity uses restrained,
// particle-free light: cool silver for SR and a longer warm-gold reveal for SEC.
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
using UnityEngine.EventSystems;
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
        /// <summary>Abandon the whole ceremony and lay the entire pool out.</summary>
        private bool skipAll;
        /// <summary>Cut the CURRENT pack short and move to the next one. Reset per pack.</summary>
        private bool skipPack;
        /// <summary>True while either skip is in force — every step inside a pack's ceremony
        /// checks this, since both kinds of skip end the pack you are looking at.</summary>
        private bool SkipNow => skipAll || skipPack;
        private readonly Dictionary<string, Sprite> spriteCache = new Dictionary<string, Sprite>();
        /// <summary>The real OPTCG card back, shown while a card is still face-down. Same asset the
        /// match view uses (StreamingAssets/Cards/optcg_card_back.jpg), so a higher-quality file
        /// dropped in there is picked up everywhere at once.</summary>
        private Sprite backSprite;
        private RectTransform cardPreview;
        /// <summary>Kept so it can be re-raised above every pack stage. Each pack adds a FULL-SCREEN
        /// click catcher (the tap-to-open affordance) as a later sibling, which sat on top of SKIP and
        /// swallowed the click — the button was there and looked live but could never be pressed.</summary>
        private readonly List<RectTransform> skipButtons = new List<RectTransform>();

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

        /// <summary>Skip the rest of the sequence outright.</summary>
        public void Skip() => skipAll = true;

        /// <summary>Skip only the pack currently on screen; the next one still opens normally.</summary>
        public void SkipCurrentPack() => skipPack = true;

        // ---- Sequence ------------------------------------------------------------------------

        private IEnumerator RunAll()
        {
            var backdrop = Panel(root, "Opening Backdrop", Color.clear);
            Stretch(backdrop, Vector2.zero, Vector2.one);
            SealedUI.AddStandardBackground(backdrop);

            var counter = Text(backdrop, "Pack Counter", "", 26, Muted, TextAnchor.UpperCenter);
            Stretch(counter.rectTransform, new Vector2(0.1f, 0.90f), new Vector2(0.9f, 0.96f));

            // Two levels: drop the pack in front of you, or drop the ceremony entirely.
            skipButtons.Clear();
            skipButtons.Add(AddButton(backdrop, "SKIP PACK", new Vector2(0.705f, 0.03f), new Vector2(0.845f, 0.09f), SkipCurrentPack));
            skipButtons.Add(AddButton(backdrop, "SKIP ALL", new Vector2(0.86f, 0.03f), new Vector2(0.98f, 0.09f), Skip));

            // PRELOAD every card's art before the first pack. The reveal draws a card the instant it
            // flips, so kicking an async load at that moment always lost the race and the card fell
            // back to its name placeholder — which is why almost nothing showed art. Every id is known
            // up front, so fetch them all first and the sequence then runs entirely from cache.
            counter.text = "PREPARING PACKS…";
            KickBackLoad();
            yield return StartCoroutine(PreloadArt(pool.Packs.SelectMany(p => p.Cards).Select(c => c.CardId)));

            for (int i = 0; i < pool.Packs.Count && !skipAll; i++)
            {
                skipPack = false;                                   // per-pack, not sticky
                counter.text = $"PACK {i + 1} / {pool.Packs.Count}";
                yield return StartCoroutine(OpenOnePack(backdrop, pool.Packs[i], pool.Packs.Count - i - 1));

                // Skipped a single pack: still show what was in it before moving on. Skipping the
                // ceremony must never cost you the information.
                if (skipPack && !skipAll)
                {
                    counter.text = $"PACK {i + 1} / {pool.Packs.Count}  ·  CONTENTS";
                    yield return StartCoroutine(LayOutAtOnce(backdrop, pool.Packs[i].Cards, 1.35f, true));
                }
            }

            // Skipping still shows what you got — it skips the ceremony, not the information.
            if (skipAll) yield return StartCoroutine(LayOutAtOnce(backdrop, pool.Packs.SelectMany(p => p.Cards), 0.05f, false));

            HideCardPreview();
            if (backdrop != null) Destroy(backdrop.gameObject);
            onComplete?.Invoke();
        }

        private IEnumerator OpenOnePack(RectTransform parent, SealedPack pack, int unopenedAfter)
        {
            var stage = Panel(parent, "Pack Stage", new Color(0, 0, 0, 0));
            Stretch(stage, Vector2.zero, Vector2.one);
            foreach (var b in skipButtons) if (b != null) b.SetAsLastSibling();   // above this pack's click catcher

            // ---- the pack itself ----
            var packSprite = SealedPackArt.For(pool.SetCode);
            AddUnopenedPackStack(stage, packSprite, unopenedAfter);

            var packRt = Panel(stage, "Pack", Color.white);
            packRt.anchorMin = packRt.anchorMax = new Vector2(0.5f, 0.52f);
            packRt.pivot = new Vector2(0.5f, 0.5f);
            packRt.sizeDelta = new Vector2(430f, 601f);
            var packImg = packRt.GetComponent<Image>();
            packImg.sprite = packSprite;
            packImg.preserveAspect = true;

            var hint = Text(stage, "Hint", "SWIPE TO OPEN THE PACK", 18, Ink, TextAnchor.MiddleCenter);
            Stretch(hint.rectTransform, new Vector2(0.3f, 0.10f), new Vector2(0.7f, 0.16f));

            // Pocket-style input: the wrapper does not deform with the pointer. The swipe is only
            // recognised as a gesture; after release the narrow heat-seal ribbon animates away.
            bool swiped = false;
            bool dragging = false;
            float dragStartX = 0f;
            float seamY = packRt.sizeDelta.y * 0.422f;
            var traceGlow = SwipeTrace(packRt, "Swipe Glow", new Color(0.35f, 0.88f, 0.82f, 0.13f), 9f, seamY);
            var traceCore = SwipeTrace(packRt, "Swipe Core", new Color(0.95f, 1f, 0.99f, 0.96f), 2.6f, seamY);
            var traceGold = SwipeTrace(packRt, "Swipe Gold Edge", new Color(1f, 0.88f, 0.52f, 0.42f), 0.9f, seamY - 2f);
            StartCoroutine(SliceHintShine(packRt, seamY, () => swiped || SkipNow));

            var catcher = Panel(stage, "Swipe Catcher", new Color(0, 0, 0, 0));
            Stretch(catcher, Vector2.zero, Vector2.one);
            catcher.GetComponent<Image>().raycastTarget = true;
            var events = catcher.gameObject.AddComponent<EventTrigger>();
            events.triggers = new List<EventTrigger.Entry>();

            Action<PointerEventData> updateTrace = e =>
            {
                if (!dragging || packRt == null) return;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(packRt, e.position, e.pressEventCamera, out var p)) return;
                float x = Mathf.Clamp(p.x, -packRt.sizeDelta.x * 0.5f, packRt.sizeDelta.x * 0.5f);
                float left = Mathf.Min(dragStartX, x);
                float width = Mathf.Abs(x - dragStartX);
                SetSwipeTrace(traceGlow, left, width, seamY);
                SetSwipeTrace(traceCore, left, width, seamY);
                SetSwipeTrace(traceGold, left, width, seamY - 2f);
            };
            AddTrigger(events, EventTriggerType.PointerDown, data =>
            {
                var e = (PointerEventData)data;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(packRt, e.position, e.pressEventCamera, out var p)) return;
                if (Mathf.Abs(p.y - seamY) > packRt.sizeDelta.y * 0.12f) return;
                dragging = true;
                dragStartX = Mathf.Clamp(p.x, -packRt.sizeDelta.x * 0.5f, packRt.sizeDelta.x * 0.5f);
                updateTrace(e);
            });
            AddTrigger(events, EventTriggerType.Drag, data => updateTrace((PointerEventData)data));
            AddTrigger(events, EventTriggerType.PointerUp, data =>
            {
                var e = (PointerEventData)data;
                updateTrace(e);
                float travelled = traceCore != null ? traceCore.sizeDelta.x : 0f;
                dragging = false;
                if (travelled >= packRt.sizeDelta.x * 0.62f) swiped = true;
                else
                {
                    SetSwipeTrace(traceGlow, 0f, 0f, seamY);
                    SetSwipeTrace(traceCore, 0f, 0f, seamY);
                    SetSwipeTrace(traceGold, 0f, 0f, seamY - 2f);
                }
            });

            float idle = 0f;
            while (!swiped && !SkipNow)
            {
                idle += Time.unscaledDeltaTime;
                float b = 1f + Mathf.Sin(idle * 2.2f) * 0.012f;
                if (packRt != null) packRt.localScale = new Vector3(b, b, 1f);
                yield return null;
            }
            if (hint != null) Destroy(hint.gameObject);
            if (SkipNow) { HideCardPreview(); Destroy(stage.gameObject); yield break; }
            if (catcher != null) Destroy(catcher.gameObject);
            if (traceGlow != null) Destroy(traceGlow.gameObject);
            if (traceCore != null) Destroy(traceCore.gameObject);
            if (traceGold != null) Destroy(traceGold.gameObject);
            packRt.localScale = Vector3.one;

            // The gesture has finished. Only now does the seal ribbon detach.
            yield return StartCoroutine(Tear(packRt, pack));

            // ---- cards out, one at a time ----
            var landed = new List<RectTransform>();
            foreach (var card in pack.Cards)
            {
                if (SkipNow) break;
                yield return StartCoroutine(RevealCard(stage, card, landed));
                if (!SkipNow) yield return WaitUnscaled(0.18f);
            }

            if (!SkipNow) yield return WaitUnscaled(0.20f);
            HideCardPreview();
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

        /// <summary>After the swipe completes, crop the real wrapper into a stationary body and a
        /// narrow heat-seal ribbon. Only that ribbon detaches and curls away. There is deliberately
        /// no progressive wrapper deformation, seam bar, rarity foreshadowing, debris or particles.</summary>
        private IEnumerator Tear(RectTransform packRt, SealedPack pack)
        {
            if (packRt == null) yield break;

            float w = packRt.sizeDelta.x;
            float h = packRt.sizeDelta.y;
            float ribbonH = h * 0.078f;
            var source = packRt.GetComponent<Image>();
            var sprite = source != null ? source.sprite : null;

            // Replace the single full wrapper with two correctly cropped views of that same sprite.
            // The body does not include the ribbon, so there is no duplicate strip underneath it.
            var bodyClip = Panel(packRt, "Opened Pack Body Clip", Color.clear);
            bodyClip.anchorMin = bodyClip.anchorMax = new Vector2(0.5f, 0.5f);
            bodyClip.sizeDelta = new Vector2(w, h - ribbonH);
            bodyClip.anchoredPosition = new Vector2(0f, -ribbonH * 0.5f);
            bodyClip.gameObject.AddComponent<RectMask2D>();
            var bodyArt = Panel(bodyClip, "Opened Pack Body Art", Color.white);
            bodyArt.anchorMin = bodyArt.anchorMax = new Vector2(0.5f, 0.5f);
            bodyArt.sizeDelta = new Vector2(w, h);
            bodyArt.anchoredPosition = new Vector2(0f, ribbonH * 0.5f);
            var bodyImg = bodyArt.GetComponent<Image>();
            bodyImg.sprite = sprite;
            bodyImg.preserveAspect = true;

            var ribbon = Panel(packRt, "Detached Seal Ribbon", Color.clear);
            ribbon.anchorMin = ribbon.anchorMax = new Vector2(0.5f, 0.5f);
            ribbon.sizeDelta = new Vector2(w, ribbonH);
            Vector2 ribbonHome = new Vector2(0f, (h - ribbonH) * 0.5f);
            ribbon.anchoredPosition = ribbonHome;
            ribbon.gameObject.AddComponent<RectMask2D>();
            var ribbonArt = Panel(ribbon, "Seal Ribbon Art", Color.white);
            ribbonArt.anchorMin = ribbonArt.anchorMax = new Vector2(0.5f, 0.5f);
            ribbonArt.sizeDelta = new Vector2(w, h);
            ribbonArt.anchoredPosition = new Vector2(0f, -(h - ribbonH) * 0.5f);
            var ribbonImg = ribbonArt.GetComponent<Image>();
            ribbonImg.sprite = sprite;
            ribbonImg.preserveAspect = true;

            if (source != null) source.enabled = false;

            var hit = BestTier(pack);
            if (hit == HitTier.SecretRare || hit == HitTier.SuperRare)
            {
                bool secret = hit == HitTier.SecretRare;
                var sparkleColour = secret
                    ? new Color(1f, 0.80f, 0.28f, 0.96f)
                    : new Color(0.82f, 0.90f, 1f, 0.90f);
                StartCoroutine(SealedPackFx.RipSparkles(packRt,
                    new Vector2(0f, h * 0.5f - ribbonH), sparkleColour,
                    secret ? 18 : 12, w * 0.43f, secret ? 0.92f : 0.72f));
            }

            // A tapered additive glint marks the freshly exposed edge without becoming a solid bar.
            var tearLine = Panel(packRt, "Fresh Tear Glint", new Color(0.82f, 1f, 0.97f, 0.9f));
            tearLine.anchorMin = tearLine.anchorMax = new Vector2(0.5f, 0.5f);
            tearLine.sizeDelta = new Vector2(w * 1.02f, 8f);
            tearLine.anchoredPosition = new Vector2(0f, h * 0.5f - ribbonH);
            var tearLineImage = tearLine.GetComponent<Image>();
            tearLineImage.sprite = UiGlow.Sprite;
            tearLineImage.material = UiGlow.Additive;

            const float dur = 0.42f;
            float t = 0f;
            while (t < dur && ribbon != null)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / dur);
                float e = SealedPackFx.EaseOut(k);
                ribbon.anchoredPosition = ribbonHome + new Vector2(180f * e, 18f * e + 118f * e * e);
                ribbon.localRotation = Quaternion.Euler(0f, -42f * e, 52f * e);
                ribbon.localScale = new Vector3(1f, 1f + Mathf.Sin(k * Mathf.PI) * 0.10f, 1f);
                var c = ribbonImg.color;
                c.a = 1f - SealedPackFx.EaseIn(Mathf.InverseLerp(0.62f, 1f, k));
                ribbonImg.color = c;
                var lineColour = tearLineImage.color;
                lineColour.a = (0.48f + 0.34f * Mathf.Sin(k * Mathf.PI * 3f)) * (1f - k);
                tearLineImage.color = lineColour;
                yield return null;
            }

            if (ribbon != null) Destroy(ribbon.gameObject);
            if (tearLine != null) Destroy(tearLine.gameObject);
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
            RoundedCardMask.ApplyTo(faceImg);
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

            holder.GetComponent<Image>().raycastTarget = true;
            var hover = holder.gameObject.AddComponent<SealedUI.HoverRouter>();
            hover.OnEnter = () => ShowCardPreview(card.CardId);
            hover.OnExit = HideCardPreview;

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
            else yield return StartCoroutine(HoldRevealed(0.90f));   // hold so the card can be read

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

        /// <summary>Parallel receives only a neutral foil pass, SR is cool silver, and SEC is a
        /// warmer, larger and longer gold reveal. None of the tiers emit particle debris.</summary>
        private IEnumerator Celebrate(RectTransform stage, RectTransform card, HitTier tier)
        {
            Vector2 at = card != null ? card.anchoredPosition : Vector2.zero;

            if (tier == HitTier.Parallel)
            {
                StartCoroutine(SealedPackFx.Glint(card, 0.42f, 0.75f));
                yield return WaitUnscaled(0.38f);
                yield break;
            }

            bool secret = tier == HitTier.SecretRare;
            var tint = TierColour(tier);
            StartCoroutine(SealedPackFx.SoftGlow(stage, at, tint,
                secret ? 240f : 210f, secret ? 980f : 680f,
                secret ? 1.10f : 0.76f, secret ? 0.34f : 0.23f));
            if (secret)
                StartCoroutine(SealedPackFx.SoftGlow(stage, at, new Color(1f, 0.93f, 0.62f, 1f),
                    180f, 720f, 0.88f, 0.24f));

            var rays = SealedPackFx.Rays(stage, at, new Color(tint.r, tint.g, tint.b, 1f),
                secret ? 14 : 9, secret ? 1180f : 880f);
            rays.SetAsFirstSibling();
            StartCoroutine(SealedPackFx.SpinAndFade(rays, secret ? 20f : 12f,
                secret ? 1.12f : 0.78f, secret ? 0.24f : 0.16f));
            StartCoroutine(SealedPackFx.GlintTinted(card, secret ? 0.82f : 0.58f,
                secret ? 0.72f : 0.58f, tint));

            float life = secret ? 1.08f : 0.74f;
            float t = 0f;
            while (t < life)
            {
                t += Time.unscaledDeltaTime;
                if (card != null)
                {
                    float pop = 1f + Mathf.Sin(Mathf.Clamp01(t / 0.3f) * Mathf.PI) * (secret ? 0.12f : 0.07f);
                    card.localScale = new Vector3(pop, pop, 1f);
                    card.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(t * 5f) * (secret ? 1.4f : 0.7f));
                }
                yield return null;
            }
            if (card != null)
            {
                card.localScale = Vector3.one;
                card.localRotation = Quaternion.identity;
            }
        }

        /// <summary>Skip path: lay cards out at once so skipping never costs information. Used for
        /// a single skipped pack (held briefly, then cleared) and for the whole pool.</summary>
        private IEnumerator LayOutAtOnce(RectTransform parent, IEnumerable<PulledCard> cards,
                                         float hold, bool clearAfter)
        {
            var grid = Panel(parent, "All Cards", new Color(0, 0, 0, 0));
            Stretch(grid, new Vector2(0.04f, 0.10f), new Vector2(0.96f, 0.88f));
            var layout = grid.gameObject.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(122f, 170f);
            layout.spacing = new Vector2(7f, 7f);
            layout.childAlignment = TextAnchor.UpperCenter;

            foreach (var card in cards)
            {
                var cell = Panel(grid, card.CardId, new Color32(24, 36, 52, 255));
                var img = cell.GetComponent<Image>();
                img.raycastTarget = true;
                RoundedCardMask.ApplyTo(img);
                var sprite = GetSprite(card.CardId);
                if (sprite != null) { img.sprite = sprite; img.color = Color.white; img.preserveAspect = true; }
                else AddLabel(cell, card);
                var hover = cell.gameObject.AddComponent<SealedUI.HoverRouter>();
                hover.OnEnter = () => ShowCardPreview(card.CardId);
                hover.OnExit = HideCardPreview;
            }

            // The skip buttons must stay reachable over the grid, or skipping one pack would
            // leave you unable to skip the next.
            foreach (var b in skipButtons) if (b != null) b.SetAsLastSibling();

            yield return WaitUnscaled(hold);
            HideCardPreview();
            if (clearAfter && grid != null) Destroy(grid.gameObject);
        }

        private void ShowCardPreview(string cardId)
        {
            HideCardPreview();
            var sprite = GetSprite(cardId);
            cardPreview = SealedUI.Panel(root, "Opening Card Preview", Color.clear);
            cardPreview.anchorMin = cardPreview.anchorMax = new Vector2(0.875f, 0.51f);
            cardPreview.pivot = new Vector2(0.5f, 0.5f);
            cardPreview.sizeDelta = new Vector2(315f, 441f);
            cardPreview.SetAsLastSibling();
            var group = cardPreview.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false; group.interactable = false;
            SealedUI.AddCardPreviewGlow(cardPreview);

            var art = SealedUI.Panel(cardPreview, "Card Art",
                sprite != null ? Color.white : new Color32(24, 36, 52, 250));
            SealedUI.Fill(art);
            var image = art.GetComponent<Image>();
            RoundedCardMask.ApplyTo(image);
            if (sprite != null) { image.sprite = sprite; image.preserveAspect = true; }
            else
            {
                var def = CardData.GetCard(cardId);
                var label = SealedUI.Label(art, "Card Text", $"{def?.Name}\n\n{def?.Effect}", 13,
                    SealedUI.Ink, TextAnchor.UpperCenter);
                SealedUI.Stretch(label.rectTransform, new Vector2(0.06f, 0.06f), new Vector2(0.94f, 0.94f));
                label.horizontalOverflow = HorizontalWrapMode.Wrap;
                label.verticalOverflow = VerticalWrapMode.Overflow;
            }
        }

        private void HideCardPreview()
        {
            if (cardPreview != null) { Destroy(cardPreview.gameObject); cardPreview = null; }
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
            if (pack?.Cards == null) return best;
            foreach (var card in pack.Cards)
            {
                var tier = TierOf(card);
                if (tier > best) best = tier;
            }
            return best;
        }

        private static Color TierColour(HitTier tier) => tier switch
        {
            HitTier.SecretRare => new Color(Gold.r, Gold.g, Gold.b, 0.56f),
            HitTier.SuperRare => new Color(Silver.r, Silver.g, Silver.b, 0.42f),
            HitTier.Parallel => new Color(Silver.r, Silver.g, Silver.b, 0.26f),
            _ => new Color(1f, 1f, 1f, 0.18f),
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
            while (pending > 0 && waited < 8f && !skipAll)
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
            var image = foil.GetComponent<Image>();
            RoundedCardMask.ApplyTo(image);
            image.raycastTarget = false;
        }

        /// <summary>Draw the still-sealed packs behind the active wrapper. Every layer is a real
        /// wrapper with a small offset and alternating lean, matching the physical card/deck stacks
        /// elsewhere in the game. Rebuilding this per opening makes the pile visibly lose one layer.</summary>
        private static void AddUnopenedPackStack(RectTransform stage, Sprite sprite, int unopenedAfter)
        {
            int visible = Mathf.Min(Mathf.Max(0, unopenedAfter), 5);
            for (int layer = visible; layer >= 1; layer--)
            {
                var back = Panel(stage, $"Unopened Pack {layer}", new Color(0.82f, 0.85f, 0.90f, 0.96f));
                back.anchorMin = back.anchorMax = new Vector2(0.5f, 0.52f);
                back.pivot = new Vector2(0.5f, 0.5f);
                back.sizeDelta = new Vector2(430f, 601f);
                back.anchoredPosition = new Vector2(layer * 12f, -layer * 5f);
                float lean = (layer % 2 == 0 ? 1f : -1f) * (0.65f + layer * 0.24f);
                back.localRotation = Quaternion.Euler(0f, 0f, lean);
                back.localScale = Vector3.one * (1f - layer * 0.004f);

                var image = back.GetComponent<Image>();
                image.sprite = sprite;
                image.preserveAspect = true;
            }
        }

        // ---- Tiny self-contained uGUI helpers -------------------------------------------------

        private static float Ease(float k) => 1f - Mathf.Pow(1f - Mathf.Clamp01(k), 3f);

        /// <summary>Hold on a revealed card, but bail the instant SKIP is pressed. A plain wait would
        /// make the button feel dead for up to a second on every card.</summary>
        private IEnumerator HoldRevealed(float seconds)
        {
            float t = 0f;
            while (t < seconds && !SkipNow)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
        }

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

        private static RectTransform SwipeTrace(RectTransform pack, string name, Color colour, float height, float y)
        {
            var trace = Panel(pack, name, colour);
            trace.anchorMin = trace.anchorMax = new Vector2(0.5f, 0.5f);
            trace.pivot = new Vector2(0f, 0.5f);
            trace.sizeDelta = new Vector2(0f, height);
            trace.anchoredPosition = new Vector2(0f, y);
            var image = trace.GetComponent<Image>();
            image.sprite = UiGlow.Sprite;
            image.material = UiGlow.Additive;
            image.raycastTarget = false;
            return trace;
        }

        private static void SetSwipeTrace(RectTransform trace, float left, float width, float y)
        {
            if (trace == null) return;
            trace.anchoredPosition = new Vector2(left, y);
            trace.sizeDelta = new Vector2(width, trace.sizeDelta.y);
        }

        /// <summary>Periodically runs a compact specular shine along the exact swipe seam. It is a
        /// hint, not a permanent bar: the line appears, travels, fades completely, then rests before
        /// repeating so the wrapper still looks like a wrapper.</summary>
        private IEnumerator SliceHintShine(RectTransform pack, float seamY, Func<bool> finished)
        {
            if (pack == null) yield break;
            var shine = Panel(pack, "Slice Hint Shine", new Color(0.78f, 0.98f, 1f, 0f));
            shine.anchorMin = shine.anchorMax = new Vector2(0.5f, 0.5f);
            shine.pivot = new Vector2(0.5f, 0.5f);
            shine.sizeDelta = new Vector2(82f, 16f);
            shine.localRotation = Quaternion.Euler(0f, 0f, -7f);
            var image = shine.GetComponent<Image>();
            image.sprite = UiGlow.Sprite;
            image.material = UiGlow.Additive;
            image.raycastTarget = false;

            const float travelTime = 0.68f;
            const float restTime = 1.45f;
            while (pack != null && (finished == null || !finished()))
            {
                float t = 0f;
                while (t < travelTime && pack != null && (finished == null || !finished()))
                {
                    t += Time.unscaledDeltaTime;
                    float k = Mathf.Clamp01(t / travelTime);
                    shine.anchoredPosition = new Vector2(Mathf.Lerp(-pack.sizeDelta.x * 0.44f,
                        pack.sizeDelta.x * 0.44f, Ease(k)), seamY);
                    float alpha = Mathf.Sin(k * Mathf.PI);
                    image.color = new Color(0.78f, 0.98f, 1f, alpha * 0.78f);
                    yield return null;
                }
                if (image != null) image.color = new Color(0.78f, 0.98f, 1f, 0f);
                float rest = 0f;
                while (rest < restTime && pack != null && (finished == null || !finished()))
                {
                    rest += Time.unscaledDeltaTime;
                    yield return null;
                }
            }
            if (shine != null) Destroy(shine.gameObject);
        }

        private static void AddTrigger(EventTrigger trigger, EventTriggerType type, Action<BaseEventData> callback)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(data => callback?.Invoke(data));
            trigger.triggers.Add(entry);
        }

        private void AddFullscreenClick(RectTransform parent, Action onClick)
        {
            var catcher = Panel(parent, "Click Catcher", new Color(0, 0, 0, 0));
            Stretch(catcher, Vector2.zero, Vector2.one);
            catcher.GetComponent<Image>().raycastTarget = true;
            catcher.gameObject.AddComponent<Button>().onClick.AddListener(() => onClick?.Invoke());
        }

        private RectTransform AddButton(RectTransform parent, string label, Vector2 min, Vector2 max, Action onClick)
        {
            var rt = Panel(parent, label + " Button", new Color32(40, 54, 72, 235));
            Stretch(rt, min, max);
            rt.GetComponent<Image>().raycastTarget = true;
            var t = Text(rt, "Label", label, 12, Ink, TextAnchor.MiddleCenter);
            Stretch(t.rectTransform, Vector2.zero, Vector2.one);
            rt.gameObject.AddComponent<Button>().onClick.AddListener(() => onClick?.Invoke());
            return rt;
        }
    }
}
