// One Piece TCG — Sealed / Pre-Release: the mode driver.
//
// Owns the whole flow and nothing else:
//     set + format + seed  →  pack opening  →  deck builder  →  play / event / export
//
// Self-hosting: Open() builds its own full-screen canvas, so the menu only has to call
// SealedManager.Open() and does not need to know anything about sealed.
//
// The screens themselves live next door (SealedPackOpening, SealedDeckBuilderUI); this file is the
// state machine between them plus the product picker.

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sealed
{
    public sealed class SealedManager : MonoBehaviour
    {
        private enum Screen { Picker, Opening, Building, Ready, Event }

        private Canvas canvas;
        private RectTransform root, screenRoot;
        private Screen screen = Screen.Picker;

        private SealedPool pool;
        private string runId;
        private SealedEvent activeEvent;

        // Picker state
        private SealedProduct chosenProduct;
        private SealedLeaderMode chosenMode = SealedLeaderMode.RainbowLuffy;
        private string chosenSeed = "";
        private bool timedBuild;

        /// <summary>Set by whoever launches the mode so Sealed can hand control back.</summary>
        public static Action OnExitToMenu;

        public static SealedManager Open()
        {
            var go = new GameObject("Sealed Mode");
            var mgr = go.AddComponent<SealedManager>();
            mgr.Boot();
            return mgr;
        }

        private void Boot()
        {
            SealedLeaderRules.EnsureRegistered();

            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();

            root = SealedUI.Panel((RectTransform)transform, "Sealed Root", SealedUI.PanelBg);
            SealedUI.Fill(root);

            chosenProduct = SealedCatalog.Available().FirstOrDefault();
            chosenSeed = PackGenerator.NewSeed();
            ShowPicker();
        }

        private void Clear()
        {
            if (screenRoot != null) Destroy(screenRoot.gameObject);
            screenRoot = SealedUI.Panel(root, "Screen", new Color(0, 0, 0, 0));
            SealedUI.Fill(screenRoot);
        }

        // ---- Screen 1: choose product ---------------------------------------------------------

        private void ShowPicker()
        {
            screen = Screen.Picker;
            Clear();

            var title = SealedUI.Label(screenRoot, "Title", "SEALED  /  PRE-RELEASE", 30, SealedUI.Ink, TextAnchor.UpperLeft, true);
            SealedUI.Stretch(title.rectTransform, new Vector2(0.04f, 0.90f), new Vector2(0.6f, 0.97f));

            var sub = SealedUI.Label(screenRoot, "Sub",
                "Open six packs, build a 40-card deck, and play it. Everyone on the same seed opens the same packs.",
                13, SealedUI.Muted, TextAnchor.UpperLeft);
            SealedUI.Stretch(sub.rectTransform, new Vector2(0.04f, 0.855f), new Vector2(0.7f, 0.90f));

            SealedUI.Button(screenRoot, "◂ MENU", SealedUI.ChipOff, SealedUI.Ink, ExitToMenu)
                .let(rt => SealedUI.Stretch(rt, new Vector2(0.90f, 0.915f), new Vector2(0.97f, 0.96f)));

            // --- set list ---
            var listHost = SealedUI.Panel(screenRoot, "Sets", SealedUI.PanelBg2);
            SealedUI.Stretch(listHost, new Vector2(0.04f, 0.16f), new Vector2(0.46f, 0.84f));
            var list = SealedUI.ScrollColumn(listHost, "Sets", 5f);
            SealedUI.Stretch(list.parent as RectTransform, new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.98f));

            foreach (var product in SealedCatalog.Available())
            {
                var p = product;
                bool selected = chosenProduct != null && chosenProduct.SetCode == p.SetCode;
                var rowRt = SealedUI.Panel(list, p.SetCode, selected ? SealedUI.Accent : SealedUI.ChipOff, raycast: true);
                rowRt.gameObject.AddComponent<LayoutElement>().preferredHeight = 58f;
                rowRt.gameObject.AddComponent<Button>().onClick.AddListener(() => { chosenProduct = p; ShowPicker(); });

                var fg = selected ? SealedUI.BadgeInk : SealedUI.Ink;
                var name = SealedUI.Label(rowRt, "N", $"{p.SetCode}  {p.DisplayName}", 15, fg, TextAnchor.UpperLeft, true);
                SealedUI.Stretch(name.rectTransform, new Vector2(0.20f, 0.45f), new Vector2(0.98f, 0.95f));

                var meta = SealedUI.Label(rowRt, "M",
                    $"{p.ReleaseDate}   ·   {p.CardCount()} cards   ·   {p.LegalLeaders().Count} leaders"
                    + (SealedPackArt.HasRealArt(p.SetCode) ? "" : "   ·   generated wrapper"),
                    11, selected ? SealedUI.BadgeInk : SealedUI.Muted, TextAnchor.UpperLeft);
                SealedUI.Stretch(meta.rectTransform, new Vector2(0.20f, 0.06f), new Vector2(0.98f, 0.45f));

                // Pack thumbnail — real wrapper if present, generated foil otherwise.
                var art = SealedUI.Panel(rowRt, "Art", Color.white);
                SealedUI.Stretch(art, new Vector2(0.015f, 0.08f), new Vector2(0.175f, 0.92f));
                var img = art.GetComponent<Image>();
                img.sprite = SealedPackArt.For(p.SetCode);
                img.preserveAspect = true;
            }

            BuildOptionsPanel();
        }

        private void BuildOptionsPanel()
        {
            var opts = SealedUI.Panel(screenRoot, "Options", SealedUI.PanelBg2);
            SealedUI.Stretch(opts, new Vector2(0.48f, 0.16f), new Vector2(0.96f, 0.84f));
            var col = SealedUI.ScrollColumn(opts, "Opt", 6f);
            SealedUI.Stretch(col.parent as RectTransform, new Vector2(0.04f, 0.02f), new Vector2(0.96f, 0.98f));

            void Head(string t) => SealedUI.Label(col, "H", t, 12, SealedUI.Accent, TextAnchor.MiddleLeft, true)
                .rectTransform.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;
            void Note(string t) => SealedUI.Label(col, "N", t, 11, SealedUI.Muted, TextAnchor.UpperLeft)
                .rectTransform.gameObject.AddComponent<LayoutElement>().preferredHeight = 30f;

            // --- format variant ---
            Head("LEADER FORMAT");
            foreach (SealedLeaderMode mode in Enum.GetValues(typeof(SealedLeaderMode)))
            {
                var m = mode;
                bool on = chosenMode == m;
                var b = SealedUI.Button(col, SealedLeaderRules.ModeName(m),
                    on ? SealedUI.Accent : SealedUI.ChipOff, on ? SealedUI.BadgeInk : SealedUI.Ink,
                    () => { chosenMode = m; ShowPicker(); }, 12, on);
                b.gameObject.AddComponent<LayoutElement>().preferredHeight = 26f;
            }
            Note(SealedLeaderRules.ModeBlurb(chosenMode));

            // --- seed ---
            Head("SEED");
            var seedRow = SealedUI.Panel(col, "SeedRow", new Color(0, 0, 0, 0));
            seedRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 30f;

            var field = NewInput(seedRow, new Vector2(0f, 0f), new Vector2(0.68f, 1f), chosenSeed);
            field.text = chosenSeed;
            field.onValueChanged.AddListener(v => chosenSeed = v);

            SealedUI.Button(seedRow, "RANDOM", SealedUI.ChipOff, SealedUI.Ink, () =>
            { chosenSeed = PackGenerator.NewSeed(); ShowPicker(); })
                .let(rt => SealedUI.Stretch(rt, new Vector2(0.70f, 0f), new Vector2(1f, 1f)));

            Note("Share this seed and anyone opening the same set gets exactly the same six packs — "
               + "that is what makes a sealed event fair.");

            // --- build timer ---
            Head("BUILD TIMER");
            var t = SealedUI.Button(col, timedBuild ? "TOURNAMENT — 50:00" : "CASUAL — no timer",
                timedBuild ? SealedUI.Accent : SealedUI.ChipOff, timedBuild ? SealedUI.BadgeInk : SealedUI.Ink,
                () => { timedBuild = !timedBuild; ShowPicker(); }, 12, timedBuild);
            t.gameObject.AddComponent<LayoutElement>().preferredHeight = 26f;

            // --- collation honesty ---
            if (chosenProduct != null && chosenProduct.Collation.RatesAreApproximate)
            {
                Head("PACK ODDS");
                Note("Box rates follow the published figures (~8 SR, ~1 SEC, ~12 Leaders, ~2 parallels per "
                   + "24-pack box). The exact per-pack common/uncommon split is derived, not official.");
            }

            // --- saved runs ---
            var saved = SealedStore.All();
            if (saved.Count > 0)
            {
                Head("CONTINUE A RUN");
                foreach (var rec in saved.Take(6))
                {
                    var r = rec;
                    var b = SealedUI.Button(col, $"{r.Label}   ({r.DeckCardIds.Count} kinds)",
                        SealedUI.ChipOff, SealedUI.Ink, () => ResumeRun(r), 11, false);
                    b.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;
                }
            }

            // --- start ---
            var start = SealedUI.Button(screenRoot, "OPEN PACKS", SealedUI.Accent, SealedUI.BadgeInk, StartRun, 18);
            SealedUI.Stretch(start, new Vector2(0.48f, 0.055f), new Vector2(0.96f, 0.135f));
        }

        // ---- Flow -----------------------------------------------------------------------------

        private void StartRun()
        {
            if (chosenProduct == null) return;
            chosenProduct.LeaderMode = chosenMode;
            pool = SealedPool.Generate(chosenProduct, PackGenerator.NormalizeSeed(chosenSeed));
            pool.LeaderMode = chosenMode;
            if (chosenMode == SealedLeaderMode.RainbowLuffy)
                pool.LeaderId = SealedLeaderRules.RainbowLuffyId;

            runId = SealedStore.NewId();
            SealedStore.Save(SealedStore.ToRecord(pool, runId));

            screen = Screen.Opening;
            Clear();
            var opening = gameObject.AddComponent<SealedPackOpening>();
            opening.Begin(screenRoot, pool, () => { Destroy(opening); ShowBuilder(); });
        }

        private void ResumeRun(SealedRunRecord rec)
        {
            var restored = SealedStore.ToPool(rec);
            if (restored == null) return;
            pool = restored;
            runId = rec.Id;
            ShowBuilder();      // straight to the builder — the packs were already opened once
        }

        private void ShowBuilder()
        {
            screen = Screen.Building;
            Clear();
            var builder = gameObject.AddComponent<SealedDeckBuilderUI>();
            builder.Begin(screenRoot, pool, p =>
            {
                Destroy(builder);
                SealedStore.Save(SealedStore.ToRecord(p, runId));
                ShowReady();
            }, timedBuild ? 50 * 60 : 0);
        }

        // ---- Screen 4: deck done — play it ----------------------------------------------------

        private void ShowReady()
        {
            screen = Screen.Ready;
            Clear();

            var v = pool.Validate();

            var title = SealedUI.Label(screenRoot, "T", v.Ok ? "DECK READY" : "DECK NOT LEGAL YET",
                30, v.Ok ? SealedUI.Good : SealedUI.Bad, TextAnchor.UpperCenter, true);
            SealedUI.Stretch(title.rectTransform, new Vector2(0.1f, 0.86f), new Vector2(0.9f, 0.94f));

            var sub = SealedUI.Label(screenRoot, "S",
                v.Ok ? $"{pool.SetCode} · seed {pool.Seed} · {SealedLeaderRules.ModeName(pool.LeaderMode)}"
                     : string.Join("   ·   ", v.Problems.Take(2)),
                13, SealedUI.Muted, TextAnchor.UpperCenter);
            SealedUI.Stretch(sub.rectTransform, new Vector2(0.1f, 0.81f), new Vector2(0.9f, 0.86f));

            var col = SealedUI.ScrollColumn(screenRoot, "Actions", 8f);
            SealedUI.Stretch(col.parent as RectTransform, new Vector2(0.34f, 0.20f), new Vector2(0.66f, 0.78f));

            void Action(string label, Action act, bool enabled = true)
            {
                var b = SealedUI.Button(col, label,
                    enabled ? SealedUI.Accent : SealedUI.ChipOff,
                    enabled ? SealedUI.BadgeInk : SealedUI.Muted,
                    () => { if (enabled) act(); }, 14);
                b.gameObject.AddComponent<LayoutElement>().preferredHeight = 42f;
            }

            Action("PRACTICE vs A.I.", StartPracticeMatch, v.Ok);
            Action(activeEvent == null ? "ENTER 8-PLAYER EVENT" : "RESUME EVENT", StartOrResumeEvent, v.Ok);
            Action("EDIT DECK", ShowBuilder);
            Action("VIEW POOL BY PACK", () =>
            {
                ShowBuilder();
            });
            Action("EXPORT DECKLIST", () =>
            {
                GUIUtility.systemCopyBuffer = pool.ExportText();
                Toast("Decklist copied to clipboard");
            });
            Action("SHARE SEED", () =>
            {
                GUIUtility.systemCopyBuffer = pool.Seed;
                Toast($"Seed {pool.Seed} copied — anyone on {pool.SetCode} opens the same packs");
            });
            Action("NEW RUN", () => { pool = null; ShowPicker(); });

            SealedUI.Button(screenRoot, "◂ MENU", SealedUI.ChipOff, SealedUI.Ink, ExitToMenu)
                .let(rt => SealedUI.Stretch(rt, new Vector2(0.90f, 0.915f), new Vector2(0.97f, 0.96f)));
        }

        /// <summary>Swiss event: 8 players, 3 rounds, the A.I. field each opening its own pool from a
        /// derived seed. Created once and kept, so leaving to edit the deck between rounds resumes the
        /// same event rather than starting a fresh one.</summary>
        private void StartOrResumeEvent()
        {
            var product = pool.Product();
            if (product == null) return;
            activeEvent ??= SealedEvent.Create(product, pool.Seed, pool, playerCount: 8, timed: timedBuild);
            if (activeEvent.Phase == EventPhase.Building) activeEvent.StartPlay();

            screen = Screen.Event;
            Clear();
            var ui = gameObject.AddComponent<SealedEventUI>();
            ui.Begin(screenRoot, activeEvent, pool,
                (mine, theirs) =>
                {
                    // Play it for real; the result is reported back when the match ends.
                    SealedStore.Save(SealedStore.ToRecord(pool, runId));
                    SealedMatchLaunch.Requested = new SealedMatchLaunch
                    {
                        PlayerDeck = mine.ToDeckDef("Sealed Deck"),
                        OpponentDeck = (theirs ?? mine).ToDeckDef("Sealed Opponent"),
                        Seed = $"{pool.Seed}|r{activeEvent.CurrentRound}",
                    };
                    Toast("Starting round " + activeEvent.CurrentRound + "…");
                    ExitToMenu();
                },
                () => { Destroy(ui); ShowReady(); });
        }

        /// <summary>Hand the sealed deck to a practice match. The A.I. opens its OWN pool from a derived
        /// seed and builds against it, so a practice game is a real sealed matchup rather than the bot
        /// piloting a constructed deck.</summary>
        private void StartPracticeMatch()
        {
            var product = pool.Product();
            if (product == null) return;

            var aiPool = SealedDeckAI.BuildOpponent(product, pool.Seed, 1);

            // Persist first — a match should never be able to lose the run.
            SealedStore.Save(SealedStore.ToRecord(pool, runId));

            SealedMatchLaunch.Requested = new SealedMatchLaunch
            {
                PlayerDeck = pool.ToDeckDef("Sealed Deck"),
                OpponentDeck = aiPool.ToDeckDef("Sealed A.I."),
                Seed = pool.Seed,
            };
            Toast("Starting practice match…");
            ExitToMenu();
        }

        // ---- Bits -----------------------------------------------------------------------------

        private void Toast(string message)
        {
            var toast = SealedUI.Panel(root, "Toast", new Color32(20, 40, 56, 245));
            SealedUI.Stretch(toast, new Vector2(0.30f, 0.06f), new Vector2(0.70f, 0.12f));
            toast.SetAsLastSibling();
            var t = SealedUI.Label(toast, "T", message, 13, SealedUI.Ink, TextAnchor.MiddleCenter);
            SealedUI.Fill(t.rectTransform);
            Destroy(toast.gameObject, 2.4f);
        }

        private void ExitToMenu()
        {
            if (pool != null && !string.IsNullOrEmpty(runId))
                SealedStore.Save(SealedStore.ToRecord(pool, runId));
            OnExitToMenu?.Invoke();
            Destroy(gameObject);
        }

        private InputField NewInput(RectTransform parent, Vector2 min, Vector2 max, string placeholder)
        {
            var go = new GameObject("Input", typeof(RectTransform), typeof(Image), typeof(InputField));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            SealedUI.Stretch(rt, min, max);
            go.GetComponent<Image>().color = new Color32(20, 34, 50, 235);
            var f = go.GetComponent<InputField>();
            var ph = SealedUI.Label(rt, "PH", placeholder, 13, SealedUI.Muted);
            var tx = SealedUI.Label(rt, "TX", "", 13, SealedUI.Ink);
            SealedUI.Stretch(ph.rectTransform, new Vector2(0.03f, 0f), new Vector2(0.97f, 1f));
            SealedUI.Stretch(tx.rectTransform, new Vector2(0.03f, 0f), new Vector2(0.97f, 1f));
            f.textComponent = tx;
            f.placeholder = ph;
            return f;
        }
    }

    /// <summary>Hand-off slot for launching a match from Sealed. GameManager picks this up on the next
    /// NewMatch, the same way PendingSouthDeckId/PendingAiDifficulty already work, so Sealed does not
    /// need a reference to the match layer.</summary>
    public sealed class SealedMatchLaunch
    {
        public DeckDef PlayerDeck;
        public DeckDef OpponentDeck;
        public string Seed;

        public static SealedMatchLaunch Requested;

        public static SealedMatchLaunch Consume()
        {
            var r = Requested;
            Requested = null;
            return r;
        }
    }
}
