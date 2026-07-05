// DeckBuilderManager.cs
// Deck Library + Deck Builder for One Piece TCG Simulator.
// Pure procedural uGUI — same palette, helpers, and single-scene teardown
// pattern as MainMenuManager / GameManager. No prefabs, no UI Toolkit.
//
// Rules enforced (official OPTCG): exactly 1 Leader, exactly 50 main-deck cards,
// max 4 copies per card number, every main-deck card must share a colour with
// the Leader. DON!! deck is fixed at 10 and is not part of deck building.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using OnePieceTcg.Engine;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Networking;
using UnityEngine.UI;

// ════════════════════════════════════════════════════════════════════════════
// Persisted deck model
// ════════════════════════════════════════════════════════════════════════════

[Serializable]
public class DeckEntry
{
    public string id;
    public int    count;
}

[Serializable]
public class DeckData
{
    public string id;                 // stable unique id (file name)
    public string name = "New Deck";
    public string leaderId = "";      // card id of the leader, "" if none
    public bool   favorite;
    public long   updatedTicks;
    public int    slot = -1;          // hex roster slot (0..MaxDecks-1); -1 = unassigned.
                                      // Slots may have holes — a deck stays exactly where
                                      // it was dropped, even next to empty cells.
    public List<DeckEntry> cards = new List<DeckEntry>();   // main deck only

    public int MainCount()
    {
        int n = 0;
        if (cards != null) foreach (var e in cards) n += e.count;
        return n;
    }

    public int CountOf(string cardId)
    {
        if (cards == null) return 0;
        foreach (var e in cards) if (e.id == cardId) return e.count;
        return 0;
    }

    public void SetCount(string cardId, int count)
    {
        cards ??= new List<DeckEntry>();
        var e = cards.FirstOrDefault(c => c.id == cardId);
        if (count <= 0)
        {
            if (e != null) cards.Remove(e);
            return;
        }
        if (e == null) cards.Add(new DeckEntry { id = cardId, count = count });
        else e.count = count;
    }

    public DeckData Clone()
    {
        return new DeckData
        {
            id = id, name = name, leaderId = leaderId, favorite = favorite,
            updatedTicks = updatedTicks, slot = slot,
            cards = cards.Select(c => new DeckEntry { id = c.id, count = c.count }).ToList()
        };
    }
}

[Serializable]
internal class DeckListFile { public List<DeckData> decks = new List<DeckData>(); }

// ════════════════════════════════════════════════════════════════════════════
// Persistence — one JSON file holding all decks (simple, atomic, easy to back up)
// ════════════════════════════════════════════════════════════════════════════

public static class DeckStore
{
    public const int MaxDecks = 36;   // radius-3 hex roster: 36 slots around the RANDOM centre

    // Decks are per-identity (account player id / guest key) so accounts and
    // guests on the same machine each have their own roster - see the matching
    // scoping in ReplayStore. Legacy Decks/decks.json is migrated into the first
    // signed-in account's folder; guests start with an empty roster.
    private static string RootDir => Path.Combine(Application.persistentDataPath, "Decks");
    private static string Dir  => Path.Combine(RootDir, AccountManager.CurrentIdentityKey);
    private static string File_ => Path.Combine(Dir, "decks.json");
    private static string LegacyFile => Path.Combine(RootDir, "decks.json");

    public static string ActiveDeckId;   // which deck the menu/solo game uses

    private static List<DeckData> _cache;
    private static string _cacheIdentity; // identity the cache was loaded for

    private static void MigrateLegacyIfNeeded(string ident)
    {
        try
        {
            if (ident == "local" || ident.StartsWith("guest_")) return;
            if (File.Exists(File_) || !File.Exists(LegacyFile)) return;
            Directory.CreateDirectory(Dir);
            File.Move(LegacyFile, File_);
            Debug.Log("DeckStore: migrated legacy decks to account folder.");
        }
        catch (Exception e) { Debug.LogWarning("Deck migration failed: " + e.Message); }
    }

    public static List<DeckData> All()
    {
        string ident = AccountManager.CurrentIdentityKey;
        if (_cache != null && _cacheIdentity == ident) return _cache;
        // Identity changed (sign-in, sign-out, guest switch): drop everything the
        // previous user had in memory, including which deck was active.
        if (_cacheIdentity != null && _cacheIdentity != ident) ActiveDeckId = null;
        _cacheIdentity = ident;
        _cache = new List<DeckData>();
        MigrateLegacyIfNeeded(ident);
        try
        {
            if (File.Exists(File_))
            {
                var json = File.ReadAllText(File_);
                var wrap = JsonUtility.FromJson<DeckListFile>(json);
                if (wrap?.decks != null) _cache = wrap.decks;
            }
        }
        catch (Exception e) { Debug.LogWarning("DeckStore load failed: " + e.Message); }
        NormalizeSlots();
        return _cache;
    }

    // Every deck gets a unique slot in [0, MaxDecks). Decks saved before slots
    // existed (or with clashing/out-of-range values) take the first free slot in
    // list order. The list is kept sorted by slot so "first deck" == top-left.
    private static void NormalizeSlots()
    {
        if (_cache == null) return;
        var used = new HashSet<int>();
        foreach (var d in _cache)
        {
            if (d.slot >= 0 && d.slot < MaxDecks && !used.Contains(d.slot)) used.Add(d.slot);
            else d.slot = -1;
        }
        int next = 0;
        foreach (var d in _cache)
        {
            if (d.slot >= 0) continue;
            while (used.Contains(next)) next++;
            d.slot = next; used.Add(next);
        }
        _cache.Sort((a, b) => a.slot.CompareTo(b.slot));
    }

    // Commit a full slot assignment (from the roster's live drag preview) in one
    // shot, then re-sort and persist.
    public static void ApplySlots(IEnumerable<KeyValuePair<string, int>> slots)
    {
        var list = All();
        foreach (var kv in slots)
        {
            var d = list.FirstOrDefault(x => x.id == kv.Key);
            if (d != null) d.slot = Mathf.Clamp(kv.Value, 0, MaxDecks - 1);
        }
        NormalizeSlots();
        Flush();
    }

    // Delete every deck (Clear-all button on the Select screen).
    public static void ClearAll()
    {
        _cache = new List<DeckData>();
        ActiveDeckId = null;
        Flush();
    }

    private static void Flush()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var wrap = new DeckListFile { decks = _cache ?? new List<DeckData>() };
            File.WriteAllText(File_, JsonUtility.ToJson(wrap, true));
        }
        catch (Exception e) { Debug.LogWarning("DeckStore save failed: " + e.Message); }
    }

    public static bool CanAddNew() => All().Count < MaxDecks;

    public static DeckData Get(string id) => All().FirstOrDefault(d => d.id == id);

    // Insert or update by id.
    public static void Save(DeckData deck)
    {
        if (deck == null) return;
        var list = All();
        deck.updatedTicks = DateTime.UtcNow.Ticks;
        var existing = list.FirstOrDefault(d => d.id == deck.id);
        if (existing != null)
        {
            int i = list.IndexOf(existing);
            list[i] = deck;
        }
        else
        {
            if (list.Count >= MaxDecks) return;
            list.Add(deck);
        }
        NormalizeSlots();   // new decks take their requested slot or the first free one
        Flush();
    }

    public static void Delete(string id)
    {
        var list = All();
        var d = list.FirstOrDefault(x => x.id == id);
        if (d != null) { list.Remove(d); Flush(); }
        if (ActiveDeckId == id) ActiveDeckId = null;
    }

    public static void ToggleFavorite(string id)
    {
        var d = Get(id);
        if (d != null) { d.favorite = !d.favorite; Flush(); }
    }

    // Move a deck directly to a hex slot. If the slot is empty the deck simply
    // takes it (holes are fine); if occupied, ApplySlots callers handle the
    // insertion shuffle and this is just the single-deck fallback.
    public static void Move(string id, int toSlot)
    {
        var d = Get(id);
        if (d == null) return;
        toSlot = Mathf.Clamp(toSlot, 0, MaxDecks - 1);
        var occupant = All().FirstOrDefault(x => x.slot == toSlot && x.id != id);
        if (occupant != null) occupant.slot = d.slot;   // swap as a safe fallback
        d.slot = toSlot;
        NormalizeSlots();
        Flush();
    }

    public static string NewId() => "deck_" + DateTime.UtcNow.Ticks.ToString("x");

    // The deck the rest of the game should use: explicit active, else first favourite, else first.
    public static DeckData ActiveOrDefault()
    {
        var list = All();
        if (list.Count == 0) return null;
        if (!string.IsNullOrEmpty(ActiveDeckId))
        {
            var a = Get(ActiveDeckId);
            if (a != null) return a;
        }
        return list.FirstOrDefault(d => d.favorite) ?? list[0];
    }
}

// ════════════════════════════════════════════════════════════════════════════
// Card library record (subset of the StreamingAssets JSON we care about)
// ════════════════════════════════════════════════════════════════════════════

[Serializable]
public class CardRec
{
    public string id;
    public string type;
    public string name;
    public string color;
    public int    cost;
    public int    life;
    public int    power;
    public int    counter;
    public string attribute;
    public string rarity;
    public string effect;
    public string feature;

    // cached derived
    [NonSerialized] public string[] colors;
    public string[] Colors()
    {
        if (colors == null)
            colors = string.IsNullOrEmpty(color)
                ? new string[0]
                : color.Split('/').Select(c => c.Trim()).Where(c => c.Length > 0).ToArray();
        return colors;
    }

    [NonSerialized] public string[] features;
    public string[] Features()
    {
        if (features == null)
            features = string.IsNullOrEmpty(feature)
                ? new string[0]
                : feature.Split('/').Select(f => f.Trim()).Where(f => f.Length > 0).ToArray();
        return features;
    }
}

[Serializable]
internal class CardLibFile { public CardRec[] cards = new CardRec[0]; }

// Precomputed face positions from detect_faces.py: parallel arrays of card id →
// eye-line fraction (0 = top of card). JsonUtility-friendly shape.
[Serializable]
internal class FaceMapFile
{
    public string[] ids = new string[0];
    public float[]  y   = new float[0];   // eye-line fraction (0 = top of card)
    public float[]  x   = new float[0];   // face centre x fraction (0 = left) — optional, newer files
    public float[]  z   = new float[0];   // hex zoom multiplier (1 = default window; <1 = tighter) — optional
}

// ════════════════════════════════════════════════════════════════════════════
// The builder
// ════════════════════════════════════════════════════════════════════════════

public class DeckBuilderManager : MonoBehaviour
{
    // ── Palette (identical to MainMenuManager) ────────────────────────────────
    private static readonly Color Ink        = new Color32(238, 242, 247, 255);
    private static readonly Color Muted      = new Color32(159, 171, 190, 255);
    private static readonly Color Accent     = new Color32( 79, 195, 224, 255);
    private static readonly Color Accent2    = new Color32(134, 214, 238, 255);
    private static readonly Color BadgeInk   = new Color32(  6,  32,  44, 255);
    private static readonly Color MenuBg     = new Color32( 12,  23,  38, 255);
    private static readonly Color LogBgDark  = new Color32( 14,  30,  46, 184);
    private static readonly Color MenuB      = new Color32(120, 180, 220,  46);
    private static readonly Color ZoneBorder = new Color32(120, 180, 220,  66);
    private static readonly Color Gold       = new Color32(226, 190, 102, 255);
    private static readonly Color Steel      = new Color32(176, 184, 196, 255);  // brushed-steel hex outline
    private static readonly Color SteelDim   = new Color32(120, 132, 148, 150);  // dim steel (empty slots)
    private static readonly Color DeepBlueMetal = new Color32( 60,  82, 118, 255);  // same brushed-metal look as Steel, tinted a darker blue — the RANDOM hex's rim
    private static readonly Color Bronze        = new Color32(150,  96,  48, 255);  // base fill for the RANDOM hex's "?" — a warm gold sheen is layered on top

    // Archetype segment palette (cycled for the top archetypes; "Other" is fixed).
    private static readonly Color[] ArchPalette = {
        new Color32( 70, 140, 220, 255),  // blue
        new Color32( 90, 100, 120, 255),  // steel
        new Color32(214,  68,  68, 255),  // red
        new Color32(226, 190, 102, 255),  // gold
        new Color32( 70, 180, 110, 255),  // green
        new Color32(160, 110, 210, 255),  // purple
    };
    private static readonly Color ArchOther = new Color32(51, 65, 84, 255);
    private static readonly Color RedAccent  = new Color32(230,  84,  84, 255);
    private static readonly Color GoodGreen  = new Color32( 79, 208, 138, 255);
    private static readonly Color MatTop     = new Color32( 13,  33,  60, 255);
    private static readonly Color MatBottom  = new Color32( 13,  38,  50, 255);

    private static readonly Color PanelFill  = new Color32( 10, 20, 33, 235);
    private static readonly Color TileFill   = new Color32(  8, 16, 24, 200);
    // Opaque deck-row colour. Rows are solid so the art can fade seamlessly into
    // this exact colour at the edges (no visible patch / hard border).
    private static readonly Color RowBg      = new Color32( 17, 32, 47, 255);

    // One Piece colour swatches for filter chips / dots.
    private static readonly Dictionary<string, Color> ColorSwatch = new Dictionary<string, Color>
    {
        { "Red",    new Color32(214,  68,  68, 255) },
        { "Green",  new Color32( 70, 180, 110, 255) },
        { "Blue",   new Color32( 70, 140, 220, 255) },
        { "Purple", new Color32(160, 110, 210, 255) },
        { "Black",  new Color32( 90, 100, 120, 255) },
        { "Yellow", new Color32(230, 200,  90, 255) },
    };

    // ── State ─────────────────────────────────────────────────────────────────
    private enum View { Library, Editor, Select, Starter }
    private View view = View.Library;
    private static bool _openAsSelect = false;
    // Starter-deck browser (ST01, ST02, ...): same leader/hex/decklist format as the
    // Select screen, but the hexes are a fixed, non-draggable, read-order roster instead
    // of the player's own movable deck collection.
    private string selectedStarterId;
    private readonly Dictionary<string, RectTransform> starterHexCells = new Dictionary<string, RectTransform>();

    private DeckData editing;          // deck currently open in the editor
    private string selectedDeckId;     // deck shown in the Select view
    // Local-space centres of the 36 deck slots in the hex roster (index = slot
    // number). Used to resolve where a dragged deck was dropped.
    private List<Vector2> hexSlotCenters = new List<Vector2>();
    // Live-reorder state for the hex roster (mirrors the in-hand card reorder):
    // hexPreviewIds[k] occupies slot hexPreviewSlots[k]; both stay slot-sorted.
    private readonly Dictionary<string, RectTransform> hexDeckCells = new Dictionary<string, RectTransform>();
    private readonly List<string> hexPreviewIds = new List<string>();
    private readonly List<int> hexPreviewSlots = new List<int>();
    // Deck id being drag-reordered right now, else null. Unity fires the cell's
    // Button.onClick BEFORE OnEndDrag on the same object, and that click's
    // Render() tore the roster down mid-gesture — so clicks are ignored while set.
    private string hexDraggingId;
    private float hexFontScale = 1f;   // cell font scaling when hexes shrink to fit
    // Two-click confirmations (survive a Render so the warning label shows).
    private string pendingDeleteDeckId;   // deck awaiting a confirm-delete second click
    private bool   pendingClearAll;        // clear-all awaiting its confirm second click
    private bool     pickingLeader;    // editor: leader slot clicked, pool shows leaders
    private RectTransform importOverlay;   // paste-a-decklist modal, built on demand

    // filters
    private string filterText  = "";
    private string filterColor = "";   // "" = all
    private string filterType  = "";   // "" = all  (character/event/stage/leader)
    private int    filterCost  = -1;   // -1 = all, 10 = 10+
    private bool   colorLock    = true; // only show cards matching the leader's colours
    private bool   archetypeLock;       // only show cards sharing an archetype/feature tag with the leader

    // ── Virtualised card grid ──────────────────────────────────────────────────
    // A small fixed pool of tiles is recycled as you scroll, so the GameObject
    // count is constant no matter how many cards match the filter. Card art
    // streams in over a few frames via a queue so nothing hitches.
    private const float CellW = 120f, CellH = 196f, SpaceX = 12f, SpaceY = 14f;
    private const float PadL = 8f, PadR = 8f, PadT = 4f, PadB = 10f;

    private class TileView
    {
        public RectTransform root;
        public Image art;
        public Text cost, label;
        public RectTransform badge;
        public Text badgeText;
        public string boundId;   // card this tile currently represents
        public string artId;     // card whose art is currently shown (null = placeholder)
    }

    // Pops the right-side card preview while the pointer is over a card widget.
    // For recycled pool tiles it reads the tile's live boundId; for fixed widgets
    // (deck rows, leader slot) it uses a captured id. Attach to any raycastable
    // graphic (the widget's background Image).
    private sealed class HoverPreview : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        // Runtime-only wiring — never serialized (avoids UAC1001 on the plain
        // TileView class and keeps these out of the inspector).
        [NonSerialized] public DeckBuilderManager mgr;
        [NonSerialized] public TileView tile;    // set for recycled pool tiles (live id); else null
        [NonSerialized] public string   cardId;  // set for fixed widgets

        public void OnPointerEnter(PointerEventData e)
        {
            if (mgr == null) return;
            string id = tile != null ? tile.boundId : cardId;
            mgr.ShowCardPreview(id);
        }

        public void OnPointerExit(PointerEventData e)
        {
            if (mgr != null) mgr.HideCardPreview();
        }
    }

    // Drag a deck hex to any slot to reorder the roster. A plain click still
    // selects (drag only kicks in past the EventSystem threshold), so this
    // composes with the existing click-to-lock-in Button on the same cell.
    private sealed class HexDragReorder : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [NonSerialized] public DeckBuilderManager mgr;
        [NonSerialized] public string deckId;
        [NonSerialized] public RectTransform cell;
        private bool dragging;

        public void OnBeginDrag(PointerEventData e)
        {
            if (mgr == null || cell == null) return;
            dragging = true;
            mgr.hexDraggingId = deckId;   // suppress the same-object click until the drop commits
            var tw = cell.GetComponent<HexSlideTween>();   // never fight a leftover tween
            if (tw != null) Destroy(tw);
            cell.SetAsLastSibling();                 // float above the other hexes
            cell.localScale = new Vector3(1.05f, 1.05f, 1f);
        }

        public void OnDrag(PointerEventData e)
        {
            if (!dragging) return;
            var parent = cell.parent as RectTransform;
            if (parent != null &&
                RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, e.position, e.pressEventCamera, out var lp))
            {
                cell.anchoredPosition = lp;          // follow the cursor
                mgr.PreviewHexReorder(deckId, lp);   // other hexes slide aside live
            }
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (!dragging) return;
            dragging = false;
            cell.localScale = Vector3.one;
            mgr.HandleHexDrop(deckId, cell.anchoredPosition);   // commits previewed order + re-renders
        }

        // Failsafe: if the button is up but OnEndDrag never fired (focus loss,
        // release outside the window, event eaten by a rebuild), commit anyway —
        // otherwise the roster stays scrambled and clicks go dead (hexDraggingId).
        private void Update()
        {
            if (!dragging) return;
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null && !mouse.leftButton.isPressed)
            {
                dragging = false;
                if (cell != null)
                {
                    cell.localScale = Vector3.one;
                    mgr.HandleHexDrop(deckId, cell.anchoredPosition);
                }
                else if (mgr != null) mgr.HandleHexDrop(deckId, Vector2.zero);
            }
        }
    }

    // Exponential ease of anchoredPosition toward a target; self-removes on
    // arrival. Gives dragged-over hexes the "slide aside" feel the hand fan uses.
    private sealed class HexSlideTween : MonoBehaviour
    {
        [NonSerialized] public Vector2 target;
        private RectTransform rt;
        private void Awake() { rt = (RectTransform)transform; }
        private void Update()
        {
            if (rt == null) { Destroy(this); return; }
            float k = 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime);
            rt.anchoredPosition = Vector2.Lerp(rt.anchoredPosition, target, k);
            if ((rt.anchoredPosition - target).sqrMagnitude < 0.04f)
            {
                rt.anchoredPosition = target;
                Destroy(this);
            }
        }
        public static void Slide(RectTransform cell, Vector2 to)
        {
            if (cell == null) return;
            var t = cell.GetComponent<HexSlideTween>();
            if (t == null) t = cell.gameObject.AddComponent<HexSlideTween>();
            t.target = to;
        }
    }

    // Drives the UI/HexRimGlow material — the hex twin of play mode's card glow.
    private sealed class HexRimGlowDriver : MonoBehaviour
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
                Vector2 hex = g / (1f + 2f * expand);
                float mn = hex.y;
                mat.SetVector("_GlowSize", new Vector4(g.x, g.y, 0f, 0f));
                mat.SetVector("_CardSize", new Vector4(hex.x, hex.y, 0f, 0f));
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

    private static Shader _hexGlowShader;

    // Gold-orange animated rim glow around the selected hex — same HDR palette
    // as the in-match hover glow (UI/HexRimGlow, ported from UI/CardHoverGlow).
    private void AddHexSelectionGlow(RectTransform cell)
    {
        if (_hexGlowShader == null) _hexGlowShader = Shader.Find("UI/HexRimGlow");
        if (_hexGlowShader == null) return;   // shader missing — degrade gracefully

        const float glowExpand = 0.34f;
        var rimGo = new GameObject("SelHexGlow");
        rimGo.transform.SetParent(cell, false);
        var rimRt = rimGo.AddComponent<RectTransform>();
        rimRt.anchorMin = new Vector2(-glowExpand, -glowExpand);
        rimRt.anchorMax = new Vector2(1f + glowExpand, 1f + glowExpand);
        rimRt.offsetMin = rimRt.offsetMax = Vector2.zero;
        rimRt.SetAsFirstSibling();
        var rimImg = rimGo.AddComponent<RawImage>();
        rimImg.texture = Texture2D.whiteTexture;
        rimImg.raycastTarget = false;
        var m = new Material(_hexGlowShader);
        m.SetColor("_GlowColor", new Color(1.00f, 0.59f, 0.10f, 1f) * 1.28f);
        m.SetColor("_CoreColor", new Color(1.00f, 0.80f, 0.38f, 1f) * 1.12f);
        m.SetColor("_OuterColor", new Color(0.82f, 0.29f, 0.04f, 1f) * 1.05f);
        m.SetFloat("_Speed", 0.55f);
        m.SetFloat("_NoiseScale", 3.0f);
        m.SetFloat("_Pulse", 0.22f);
        rimImg.material = m;
        rimGo.AddComponent<HexRimGlowDriver>().Init(rimImg, glowExpand);
    }

    private readonly List<CardRec> poolCards = new List<CardRec>();
    private readonly List<TileView> tilePool = new List<TileView>();
    private RectTransform poolViewport, poolContent;
    private int gridCols;

    // async art decoding (off the main thread via UnityWebRequestTexture)
    //
    // Loading a card's art has two phases with very different costs: FETCH
    // (UnityWebRequest reading the file off disk - cheap, yields normally, fine
    // to run several at once) and DECODE (Texture2D.LoadImage + mip chain, NOT
    // yieldable, runs fully synchronously the instant a fetch resolves). Capping
    // only fetch concurrency doesn't bound decode cost: local file:// reads tend
    // to resolve close together, so several un-yielded decodes can still land in
    // the same frame and stutter. So the two are throttled separately per tier
    // below (MaxConcurrentLoads/MaxConcurrentThumbLoads bound how many ids are
    // anywhere in the fetch-or-awaiting-decode pipeline - generous, fetching
    // itself is cheap), but decoding is throttled ONCE, jointly, for both tiers
    // (see MaxDecodeCostPerFrame / DrainDecodeQueue near the shared _decodeQueue)
    // - two independently-budgeted decode steps running every frame would be
    // additive on any frame both have work, silently blowing the real per-frame
    // cost bound this whole scheme exists to enforce.
    //
    // Two tiers: MASTER (full-resolution card art, ~715x1000 - leader previews,
    // hover preview, the card-library tile pool) and THUMB (pre-generated
    // downscaled ~448x626 copies under StreamingAssets/Cards/Thumbs/, see
    // generate_thumbnails.py - hex cells, decklist rows, library thumbnail
    // cards: contexts that only ever display art small, where full-res decode
    // cost was being paid for pixels the GPU immediately throws away). Each
    // tier's fetch-stage state is fully separate (simple, low-risk, keeps the
    // master pipeline untouched); an id's slot in _artLoading/activeArtLoads (or
    // _thumbLoading/activeThumbLoads) is held for its WHOLE fetch+decode
    // lifetime, not just the fetch - that's what keeps the decode queue's depth
    // bounded instead of growing unboundedly under a burst.
    private static readonly HashSet<string> _artLoading = new HashSet<string>();
    private int activeArtLoads;
    private const int MaxConcurrentLoads = 8;

    private static readonly Dictionary<string, Sprite> _thumbCache = new Dictionary<string, Sprite>();
    private static readonly HashSet<string> _thumbLoading = new HashSet<string>();
    private int activeThumbLoads;
    private const int MaxConcurrentThumbLoads = 12;   // thumbnails are smaller to fetch too

    private Canvas canvas;
    private RectTransform root;
    private Font font;
    private Font monoFont;
    private Font bronzeFont;   // engraved/medallion-style face for the RANDOM hex's "?"

    // Floating card preview docked on the right of the canvas — mirrors the
    // solo-vs-self ShowPreview popup. Built once and reused; shown while a card
    // is hovered anywhere in the builder (pool tiles, deck rows, leader slot).
    private RectTransform previewRoot;
    private Image previewImage;
    private Text  previewName;

    // live editor sub-panels we refresh without rebuilding the whole screen
    private RectTransform deckListContent;
    private RectTransform leaderSlot;
    private RectTransform statsRoot;     // left-column deck-stats graphs
    private Text validityText;
    private Text countText;
    private Text resultCountText;
    private Image saveButtonBg;
    private Text saveButtonText;

    private CardRec[] library = new CardRec[0];
    private Dictionary<string, CardRec> byId = new Dictionary<string, CardRec>();

    // ── sprite / texture caches ────────────────────────────────────────────────
    private Sprite _round, _roundBig, _circle, _hGradient, _vGradient, _hexSprite, _hexMetalSprite, _bgGrad;
    private readonly Dictionary<int, Sprite> _borders = new Dictionary<int, Sprite>();
    private static readonly Dictionary<string, Sprite> _artCache = new Dictionary<string, Sprite>();
    private static readonly Dictionary<string, Vector2> _faceCenterCache = new Dictionary<string, Vector2>();
    private static Dictionary<string, float> _faceMap;    // face-data.json: eye-line y per card
    private static Dictionary<string, float> _faceMapX;   // face-data.json: face centre x per card (optional)
    private static Dictionary<string, float> _faceMapZ;   // face-data.json: hex zoom per card (optional)

    // ══════════════════════════════════════════════════════════════════════════
    // Entry point — created on demand by the main menu (single-scene design)
    // ══════════════════════════════════════════════════════════════════════════
    public static void Open()
    {
        if (UnityEngine.Object.FindAnyObjectByType<DeckBuilderManager>() == null)
            new GameObject("DeckBuilderManager").AddComponent<DeckBuilderManager>();
    }

    public static void OpenSelect()
    {
        _openAsSelect = true;
        var existing = UnityEngine.Object.FindAnyObjectByType<DeckBuilderManager>();
        if (existing != null) { existing.view = View.Select; existing.Render(); }
        else new GameObject("DeckBuilderManager").AddComponent<DeckBuilderManager>();
    }

    // ── Picker mode ───────────────────────────────────────────────────────────
    // Opens the Select (hex roster) view as a one-shot deck picker: clicking a
    // deck stages it for preview without touching DeckStore.ActiveDeckId, and a
    // CONFIRM button (in place of the usual "locked in" note) hands the chosen
    // deck id back to `onChosen`. Used to pick two independent decks for a
    // versus-self match. Always spins up a fresh instance rather than reusing
    // whatever's currently on screen, so chaining OpenPicker calls from inside
    // an onChosen callback (picking deck 2 right after deck 1) can't clobber
    // the instance that's still unwinding its own teardown this frame.
    private static bool pickerActive;
    private static string pickerTitle;
    private static string pickerSubtitle;
    private static Action<string> pickerCallback;
    private static Action pickerCancel;

    public static void OpenPicker(string title, string subtitle, Action<string> onChosen, Action onCancel)
    {
        pickerActive = true;
        pickerTitle = title;
        pickerSubtitle = subtitle;
        pickerCallback = onChosen;
        pickerCancel = onCancel;
        _openAsSelect = true;
        new GameObject("DeckBuilderManager").AddComponent<DeckBuilderManager>();
    }

    private void ConfirmPicker(string deckId)
    {
        var cb = pickerCallback;
        pickerActive = false; pickerTitle = null; pickerSubtitle = null;
        pickerCallback = null; pickerCancel = null;
        if (canvas != null) canvas.gameObject.SetActive(false);
        cb?.Invoke(deckId);
        if (canvas != null) Destroy(canvas.gameObject);
        Destroy(gameObject);
    }

    private void CancelPicker()
    {
        var cancel = pickerCancel;
        pickerActive = false; pickerTitle = null; pickerSubtitle = null;
        pickerCallback = null; pickerCancel = null;
        if (canvas != null) canvas.gameObject.SetActive(false);
        cancel?.Invoke();
        if (canvas != null) Destroy(canvas.gameObject);
        Destroy(gameObject);
    }

    private void Awake()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        try { monoFont = Font.CreateDynamicFontFromOSFont(
            new[] { "JetBrains Mono", "Consolas", "Cascadia Mono", "Courier New" }, 14); }
        catch { monoFont = null; }
        try { bronzeFont = Font.CreateDynamicFontFromOSFont(
            new[] { "Copperplate Gothic Bold", "Perpetua Titling MT", "Constantia", "Cambria", "Georgia" }, 14); }
        catch { bronzeFont = null; }

        _artLoading.Clear();    // any in-flight markers from a prior session are dead
        _thumbLoading.Clear();  // same reason - static HashSet, coroutines don't survive scene teardown
        LoadLibrary();
        LoadFaceData();
        EnsureEventSystem();
        BuildCanvas();
        if (_openAsSelect) { _openAsSelect = false; view = View.Select; }
        Render();
    }

    private void ReturnToMenu()
    {
        if (canvas != null) canvas.gameObject.SetActive(false);
        MainMenuManager.EnsureMenu();
        if (canvas != null) Destroy(canvas.gameObject);
        Destroy(gameObject);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Card library
    // ══════════════════════════════════════════════════════════════════════════
    private void LoadLibrary()
    {
        try
        {
            string path = Path.Combine(Application.dataPath, "StreamingAssets", "Cards", "official-card-library.json");
            if (!File.Exists(path)) { Debug.LogWarning("Card library not found: " + path); return; }
            string json = File.ReadAllText(path);
            var wrap = JsonUtility.FromJson<CardLibFile>("{\"cards\":" + json + "}");
            library = wrap?.cards ?? new CardRec[0];
            byId = new Dictionary<string, CardRec>();
            foreach (var c in library) if (c != null && !string.IsNullOrEmpty(c.id)) byId[c.id] = c;
        }
        catch (Exception e) { Debug.LogWarning("Card library parse failed: " + e.Message); }
    }

    private CardRec Card(string id) => (id != null && byId.TryGetValue(id, out var c)) ? c : null;

    // ══════════════════════════════════════════════════════════════════════════
    // Canvas
    // ══════════════════════════════════════════════════════════════════════════
    private void EnsureEventSystem()
    {
        foreach (var old in UnityEngine.Object.FindObjectsByType<StandaloneInputModule>(FindObjectsInactive.Include))
            Destroy(old);
        var existing = UnityEngine.Object.FindAnyObjectByType<EventSystem>();
        if (existing != null)
        {
            if (existing.GetComponent<InputSystemUIInputModule>() == null)
                existing.gameObject.AddComponent<InputSystemUIInputModule>();
            return;
        }
        var es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        es.AddComponent<InputSystemUIInputModule>();
    }

    private void BuildCanvas()
    {
        var go = new GameObject("DeckBuilder Canvas");
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 60;
        go.AddComponent<GraphicRaycaster>();
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode          = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution  = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight   = 0.5f;
        scaler.dynamicPixelsPerUnit = 4f;

        root = Panel("Root", canvas.transform, MatTop);
        Stretch(root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        root.GetComponent<Image>().raycastTarget = false;

        // The hover preview lives directly under the canvas (a sibling of root)
        // so Render()'s teardown of root's children never destroys it. It stays
        // hidden until a card is hovered.
        BuildPreviewOverlay();
    }

    // ── Floating card preview (right side) — like solo-vs-self ShowPreview ───────
    private void BuildPreviewOverlay()
    {
        previewRoot = Panel("Card Preview Overlay", canvas.transform, new Color(0, 0, 0, 0));
        Stretch(previewRoot, new Vector2(0.725f, 0.16f), new Vector2(0.985f, 0.84f), Vector2.zero, Vector2.zero);
        previewRoot.GetComponent<Image>().raycastTarget = false;
        var group = previewRoot.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;   // never eats hover/clicks meant for the cards behind it
        group.interactable   = false;

        // Card art region (upper ~82%), name caption below.
        var region = Panel("Preview Region", previewRoot, new Color(0, 0, 0, 0));
        Stretch(region, new Vector2(0.05f, 0.14f), new Vector2(0.95f, 0.99f), Vector2.zero, Vector2.zero);
        region.GetComponent<Image>().raycastTarget = false;
        previewImage = MakeRoundedCard(region, true, out _);

        previewName = Text_("Preview Name", previewRoot, "", 17, Ink, TextAnchor.UpperCenter, titleFontOrNull());
        Stretch(previewName.rectTransform, new Vector2(0.02f, 0.0f), new Vector2(0.98f, 0.13f), Vector2.zero, Vector2.zero);
        previewName.horizontalOverflow = HorizontalWrapMode.Wrap;
        previewName.fontStyle = FontStyle.Bold;

        previewRoot.gameObject.SetActive(false);
    }

    // The builder has no separate title font; fall back to the default UI font.
    private Font titleFontOrNull() => font;

    // Show the big right-side preview for a card id. No-ops to hidden if the id
    // has no art or record.
    private void ShowCardPreview(string id)
    {
        if (previewRoot == null) return;
        var rec = Card(id);
        if (rec == null) { HideCardPreview(); return; }

        bool haveEntry = _artCache.TryGetValue(id, out var cachedSp);
        if (haveEntry && cachedSp == null) { HideCardPreview(); return; }   // confirmed: no art for this card

        if (cachedSp != null) { previewImage.sprite = cachedSp; previewImage.color = Color.white; }
        else { previewImage.sprite = null; previewImage.color = new Color(1f, 1f, 1f, 0.06f); RequestArt(previewImage, id); }

        if (previewName != null) previewName.text = rec.name ?? "";
        previewRoot.gameObject.SetActive(true);
        previewRoot.SetAsLastSibling();   // keep it above everything just drawn
    }

    private void HideCardPreview()
    {
        if (previewRoot != null) previewRoot.gameObject.SetActive(false);
    }

    // Creates a rounded, card-aspect image that fits inside `parent`, masked to
    // rounded corners (same masking trick the deck rows use). Returns the art
    // Image so callers can set/replace the sprite; `holder` is the sized frame.
    private Image MakeRoundedCard(RectTransform parent, bool big, out RectTransform holder)
    {
        const float cardAspect = 168f / 235f;   // ~0.715 standard card proportion

        holder = Panel("Card", parent, Color.white);
        holder.anchorMin = holder.anchorMax = new Vector2(0.5f, 0.5f);
        holder.pivot = new Vector2(0.5f, 0.5f);
        var fit = holder.gameObject.AddComponent<AspectRatioFitter>();
        fit.aspectMode  = AspectRatioFitter.AspectMode.FitInParent;
        fit.aspectRatio = cardAspect;

        var hImg = holder.GetComponent<Image>();
        hImg.sprite = big ? RoundSpriteBig() : RoundSprite();
        hImg.type   = Image.Type.Sliced;
        hImg.raycastTarget = false;
        var mask = holder.gameObject.AddComponent<Mask>();
        mask.showMaskGraphic = false;   // clip only; rounded corners become transparent

        var art = Panel("Art", holder, Color.white);
        Stretch(art, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var aImg = art.GetComponent<Image>();
        aImg.preserveAspect = false;    // holder already matches card aspect
        aImg.raycastTarget  = false;
        return aImg;
    }

    private void Render()
    {
        // Destroying root's children also destroys the recycled pool tiles, so the
        // stale TileView references in tilePool must be dropped or Virtualize/Update
        // will touch destroyed RectTransforms next frame (MissingReferenceException).
        tilePool.Clear();
        poolCards.Clear();
        poolContent = poolViewport = null;
        gridCols = 0;

        HideCardPreview();   // never carry a hover popup across a rebuild

        for (int i = root.childCount - 1; i >= 0; i--) Destroy(root.GetChild(i).gameObject);
        deckListContent = leaderSlot = statsRoot = null;
        validityText = countText = resultCountText = saveButtonText = null;
        saveButtonBg = null;

        // Background: one smooth full-height MatBottom→MatTop blend (no hard
        // mid-screen seam — that split belongs to the in-game board mat).
        var grad = Panel("BG", root, Color.white);
        Stretch(grad, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var gradImg = grad.GetComponent<Image>();
        gradImg.sprite = BgGradientSprite();
        gradImg.type = Image.Type.Simple;
        gradImg.raycastTarget = false;

        if      (view == View.Library) RenderLibrary();
        else if (view == View.Select)  RenderSelect();
        else if (view == View.Starter) RenderStarter();
        else                           RenderEditor();

        Canvas.ForceUpdateCanvases();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Shared top bar
    // ══════════════════════════════════════════════════════════════════════════
    private RectTransform TopBar(string title, string subtitle, UnityEngine.Events.UnityAction back)
    {
        var bar = Panel("Top Bar", root, new Color32(8, 14, 21, 170));
        Stretch(bar, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -64f), Vector2.zero);
        var line = Panel("Hairline", bar, MenuB);
        line.anchorMin = Vector2.zero; line.anchorMax = new Vector2(1f, 0f);
        line.pivot = new Vector2(0.5f, 0f); line.sizeDelta = new Vector2(0f, 1f);
        line.anchoredPosition = Vector2.zero;
        line.GetComponent<Image>().raycastTarget = false;

        // back button
        var backBtn = Panel("Back", bar, new Color32(34, 58, 78, 235));
        backBtn.anchorMin = backBtn.anchorMax = new Vector2(0f, 0.5f);
        backBtn.pivot = new Vector2(0f, 0.5f);
        backBtn.sizeDelta = new Vector2(108f, 34f);
        backBtn.anchoredPosition = new Vector2(18f, 0f);
        Round(backBtn); AddBorder(backBtn, MenuB, 1.1f);
        var bt = Text_("t", backBtn, "‹  BACK", 11, Ink, TextAnchor.MiddleCenter, monoFont);
        bt.fontStyle = FontStyle.Bold;
        Stretch(bt.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var bb = backBtn.gameObject.AddComponent<Button>();
        bb.onClick.AddListener(back);

        var diamond = Panel("Diamond", bar, Accent);
        diamond.anchorMin = diamond.anchorMax = new Vector2(0f, 0.5f);
        diamond.pivot = new Vector2(0f, 0.5f);
        diamond.sizeDelta = new Vector2(14f, 14f);
        diamond.anchoredPosition = new Vector2(146f, 0f);
        diamond.localRotation = Quaternion.Euler(0, 0, 45f);
        Round(diamond);

        var titleText = Text_("Title", bar, title, 18, Ink, TextAnchor.LowerLeft);
        titleText.fontStyle = FontStyle.Bold;
        Stretch(titleText.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.7f, 1f),
            new Vector2(168f, 0f), new Vector2(0f, -8f));
        var subText = Text_("Sub", bar, subtitle, 10, Muted, TextAnchor.UpperLeft, monoFont);
        Stretch(subText.rectTransform, new Vector2(0f, 0f), new Vector2(0.7f, 0.5f),
            new Vector2(168f, 6f), new Vector2(0f, -2f));
        return bar;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // LIBRARY VIEW
    // ══════════════════════════════════════════════════════════════════════════
    private void RenderLibrary()
    {
        var decks = DeckStore.All()
            .OrderByDescending(d => d.favorite)
            .ThenByDescending(d => d.updatedTicks)
            .ToList();

        TopBar("DECK LIBRARY", $"{decks.Count} / {DeckStore.MaxDecks} decks  ·  build & manage", ReturnToMenu);

        // New Deck button (top-right)
        bool canAdd = DeckStore.CanAddNew();
        var newBtn = Panel("New Deck", root, canAdd ? (Color)new Color32(79, 195, 224, 255)
                                                     : (Color)new Color32(24, 38, 52, 200));
        newBtn.anchorMin = newBtn.anchorMax = new Vector2(1f, 1f);
        newBtn.pivot = new Vector2(1f, 1f);
        newBtn.sizeDelta = new Vector2(170f, 38f);
        newBtn.anchoredPosition = new Vector2(-24f, -13f);
        Round(newBtn);
        if (!canAdd) AddBorder(newBtn, MenuB, 1f);
        var nbt = Text_("t", newBtn, canAdd ? "+  NEW DECK" : "LIMIT 25 REACHED", 12,
            canAdd ? BadgeInk : Muted, TextAnchor.MiddleCenter, monoFont);
        nbt.fontStyle = FontStyle.Bold;
        Stretch(nbt.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        if (canAdd)
        {
            var nb = newBtn.gameObject.AddComponent<Button>();
            nb.onClick.AddListener(() =>
            {
                editing = new DeckData { id = DeckStore.NewId(), name = "New Deck" };
                view = View.Editor; pickingLeader = false; ResetFilters();
                Render();
            });
        }

        // Scroll area of deck cards
        var area = Panel("Library Area", root, new Color(0, 0, 0, 0));
        Stretch(area, Vector2.zero, new Vector2(1f, 1f), new Vector2(24f, 20f), new Vector2(-24f, -76f));
        var content = MakeScroll(area);
        var grid = content.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(360f, 132f);
        grid.spacing  = new Vector2(18f, 18f);
        grid.padding  = new RectOffset(2, 2, 2, 6);
        var fit = content.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        if (decks.Count == 0)
        {
            var empty = Text_("Empty", area,
                "No decks yet.  Press  + NEW DECK  to build your first one.",
                14, Muted, TextAnchor.MiddleCenter);
            Stretch(empty.rectTransform, new Vector2(0f, 0.55f), new Vector2(1f, 0.75f), Vector2.zero, Vector2.zero);
            return;
        }

        foreach (var d in decks) BuildDeckCard(content, d);
    }

    private void BuildDeckCard(RectTransform parent, DeckData d)
    {
        var card = Panel(d.name + " Card", parent, PanelFill);
        RoundBig(card);
        AddBorder(card, d.favorite ? Gold : MenuB, d.favorite ? 1.3f : 1f);

        // leader thumbnail
        var thumb = Panel("Thumb", card, new Color32(6, 12, 20, 255));
        thumb.anchorMin = thumb.anchorMax = new Vector2(0f, 0.5f);
        thumb.pivot = new Vector2(0f, 0.5f);
        thumb.sizeDelta = new Vector2(74f, 104f);
        thumb.anchoredPosition = new Vector2(14f, 0f);
        Round(thumb); AddBorder(thumb, MenuB, 1f);
        var lead = Card(d.leaderId);
        bool haveEntry  = _thumbCache.TryGetValue(d.leaderId, out var cachedLsp);
        bool knownNoArt = haveEntry && cachedLsp == null;
        if (!knownNoArt)
        {
            var im = Panel("Art", thumb, cachedLsp != null ? Color.white : new Color(0f, 0f, 0f, 0f));
            var imImg = im.GetComponent<Image>();
            imImg.preserveAspect = true;
            Stretch(im, Vector2.zero, Vector2.one, new Vector2(3, 3), new Vector2(-3, -3));
            if (cachedLsp != null) imImg.sprite = cachedLsp;
            else RequestThumbArt(imImg, d.leaderId);
        }
        else
        {
            var ph = Text_("ph", thumb, lead != null ? "LEADER" : "NO\nLEADER", 9, Muted, TextAnchor.MiddleCenter, monoFont);
            Stretch(ph.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        }

        // name
        var nameText = Text_("Name", card, string.IsNullOrEmpty(d.name) ? "Untitled" : d.name,
            16, Ink, TextAnchor.UpperLeft);
        nameText.fontStyle = FontStyle.Bold;
        Stretch(nameText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(100f, -14f), new Vector2(-12f, -36f));

        // leader name / colours
        var leadName = lead != null ? lead.name : "No leader set";
        var subText = Text_("Lead", card, leadName, 11, Muted, TextAnchor.UpperLeft, monoFont);
        Stretch(subText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(100f, -38f), new Vector2(-12f, -56f));

        // colour dots
        if (lead != null)
        {
            var dotRow = Row("Dots", card, 4, TextAnchor.MiddleLeft);
            Stretch(dotRow, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(100f, -76f), new Vector2(-12f, -58f));
            foreach (var col in lead.Colors())
            {
                var dot = Panel(col, dotRow, ColorSwatch.TryGetValue(col, out var sc) ? sc : Muted);
                dot.sizeDelta = new Vector2(12f, 12f);
                SetPref(dot, new Vector2(12f, 12f));
                RoundCircle(dot);
            }
        }

        // validity badge
        var (ok, total, _) = Validate(d);
        var badge = Panel("Badge", card, ok ? new Color(GoodGreen.r, GoodGreen.g, GoodGreen.b, 0.16f)
                                             : new Color(RedAccent.r, RedAccent.g, RedAccent.b, 0.14f));
        badge.anchorMin = new Vector2(0f, 0f); badge.anchorMax = new Vector2(0f, 0f);
        badge.pivot = new Vector2(0f, 0f);
        badge.sizeDelta = new Vector2(150f, 22f);
        badge.anchoredPosition = new Vector2(100f, 14f);
        Round(badge); AddBorder(badge, ok ? GoodGreen : RedAccent, 1f);
        var bt = Text_("bt", badge, (ok ? "LEGAL  ·  " : "INVALID  ·  ") + total + "/50", 9,
            ok ? GoodGreen : RedAccent, TextAnchor.MiddleCenter, monoFont);
        bt.fontStyle = FontStyle.Bold;
        Stretch(bt.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        // action buttons row (favorite / edit / delete) bottom-right
        float bx = -12f;
        bx = MiniIcon(card, "✕", RedAccent, bx, () => { DeckStore.Delete(d.id); Render(); });
        bx = MiniIcon(card, "EDIT", Accent, bx, () =>
        {
            editing = d.Clone(); view = View.Editor; pickingLeader = false; ResetFilters(); Render();
        }, 54f);
        bx = MiniIcon(card, d.favorite ? "★" : "☆", d.favorite ? Gold : Muted, bx,
            () => { DeckStore.ToggleFavorite(d.id); Render(); });

        // clicking the card body edits it too
        var hit = card.gameObject.AddComponent<Button>();
        hit.onClick.AddListener(() =>
        {
            editing = d.Clone(); view = View.Editor; pickingLeader = false; ResetFilters(); Render();
        });
    }

    // small right-aligned pill; returns the next x offset to the left
    private float MiniIcon(RectTransform card, string label, Color col, float x,
        UnityEngine.Events.UnityAction action, float w = 30f)
    {
        var b = Panel("Mini " + label, card, new Color32(20, 34, 48, 230));
        b.anchorMin = b.anchorMax = new Vector2(1f, 0f);
        b.pivot = new Vector2(1f, 0f);
        b.sizeDelta = new Vector2(w, 24f);
        b.anchoredPosition = new Vector2(x, 14f);
        Round(b); AddBorder(b, MenuB, 1f);
        var t = Text_("t", b, label, label.Length > 2 ? 9 : 12, col, TextAnchor.MiddleCenter,
            label.Length > 2 ? monoFont : null);
        t.fontStyle = FontStyle.Bold;
        Stretch(t.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var btn = b.gameObject.AddComponent<Button>();
        btn.onClick.AddListener(action);
        return x - w - 8f;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // SELECT VIEW  — Smash-style hex roster
    // ══════════════════════════════════════════════════════════════════════════
    private void RenderSelect()
    {
        // Stored list order == roster order, so drag-to-reorder is honoured here.
        var allDecks = new List<DeckData>(DeckStore.All());

        if (string.IsNullOrEmpty(selectedDeckId) || DeckStore.Get(selectedDeckId) == null)
            selectedDeckId = DeckStore.ActiveOrDefault()?.id;

        // A pending delete confirmation only applies to the deck it was armed on.
        if (pendingDeleteDeckId != null && pendingDeleteDeckId != selectedDeckId)
            pendingDeleteDeckId = null;

        TopBar(pickerActive ? pickerTitle : "SELECT A DECK",
            pickerActive ? (pickerSubtitle ?? "choose a deck, then confirm to continue")
                         : $"{allDecks.Count} / {DeckStore.MaxDecks} decks  ·  build & manage",
            pickerActive ? (UnityEngine.Events.UnityAction)CancelPicker : ReturnToMenu);

        // + NEW DECK (top-right, same as Library)
        bool canAdd = DeckStore.CanAddNew();
        var newBtn = Panel("New Deck", root, canAdd ? (Color)Accent : (Color)new Color32(24, 38, 52, 200));
        newBtn.anchorMin = newBtn.anchorMax = new Vector2(1f, 1f);
        newBtn.pivot = new Vector2(1f, 1f);
        newBtn.sizeDelta = new Vector2(170f, 38f);
        newBtn.anchoredPosition = new Vector2(-24f, -13f);
        Round(newBtn);
        if (!canAdd) AddBorder(newBtn, MenuB, 1f);
        var nbt = Text_("t", newBtn, canAdd ? "+  NEW DECK" : "LIMIT REACHED", 12,
            canAdd ? BadgeInk : Muted, TextAnchor.MiddleCenter, monoFont);
        nbt.fontStyle = FontStyle.Bold;
        Stretch(nbt.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        if (canAdd)
        {
            var nb = newBtn.gameObject.AddComponent<Button>();
            nb.onClick.AddListener(() =>
            {
                editing = new DeckData { id = DeckStore.NewId(), name = "New Deck" };
                view = View.Editor; pickingLeader = false; ResetFilters(); Render();
            });
        }

        // STARTER DECKS (top-right, just left of + NEW DECK)
        var starterBtn = Panel("Starter Decks", root, new Color32(24, 38, 52, 200));
        starterBtn.anchorMin = starterBtn.anchorMax = new Vector2(1f, 1f);
        starterBtn.pivot = new Vector2(1f, 1f);
        starterBtn.sizeDelta = new Vector2(150f, 38f);
        starterBtn.anchoredPosition = new Vector2(-24f - 170f - 10f, -13f);
        Round(starterBtn);
        AddBorder(starterBtn, MenuB, 1f);
        var sbt = Text_("t", starterBtn, "STARTER DECKS", 12, Ink, TextAnchor.MiddleCenter, monoFont);
        sbt.fontStyle = FontStyle.Bold;
        Stretch(sbt.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        starterBtn.gameObject.AddComponent<Button>().onClick.AddListener(() =>
        {
            view = View.Starter; Render();
        });

        // CLEAR ALL DECKS (bottom-left, out of the way). Unarmed: a single muted
        // button. Armed: transforms into a dual CLEAR ALL (commit) / CANCEL bar.
        if (!pendingClearAll)
        {
            var clearBtn = Panel("ClearAll", root, new Color32(28, 22, 26, 210));
            clearBtn.anchorMin = clearBtn.anchorMax = new Vector2(0f, 0f);
            clearBtn.pivot = new Vector2(0f, 0f);
            clearBtn.sizeDelta = new Vector2(150f, 30f);
            clearBtn.anchoredPosition = new Vector2(16f, 14f);
            Round(clearBtn);
            AddBorder(clearBtn, MenuB, 1f);
            var cbt = Text_("t", clearBtn, "CLEAR ALL DECKS", 10, Muted, TextAnchor.MiddleCenter, monoFont);
            cbt.fontStyle = FontStyle.Bold;
            Stretch(cbt.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var cbtn = clearBtn.gameObject.AddComponent<Button>();
            cbtn.onClick.AddListener(() => { pendingClearAll = true; Render(); });   // arm
        }
        else
        {
            const float barW = 264f, cgap = 6f;
            float chalf = (barW - cgap) / 2f;
            var bar = Panel("ClearConfirmBar", root, new Color(0, 0, 0, 0));
            bar.anchorMin = bar.anchorMax = new Vector2(0f, 0f);
            bar.pivot = new Vector2(0f, 0f);
            bar.sizeDelta = new Vector2(barW, 30f);
            bar.anchoredPosition = new Vector2(16f, 14f);
            bar.GetComponent<Image>().raycastTarget = false;

            // Confirm (left, solid red)
            var yes = Panel("ClearYes", bar, (Color)RedAccent);
            yes.anchorMin = yes.anchorMax = new Vector2(0f, 0.5f);
            yes.pivot = new Vector2(0f, 0.5f);
            yes.sizeDelta = new Vector2(chalf, 30f);
            yes.anchoredPosition = Vector2.zero;
            Round(yes);
            var yesT = Text_("t", yes, "CLEAR ALL", 10, Ink, TextAnchor.MiddleCenter, monoFont);
            yesT.fontStyle = FontStyle.Bold;
            Stretch(yesT.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var yesB = yes.gameObject.AddComponent<Button>();
            yesB.onClick.AddListener(() =>
            {
                pendingClearAll = false;
                pendingDeleteDeckId = null;
                DeckStore.ClearAll();
                selectedDeckId = null;
                Render();
            });

            // Cancel (right, neutral)
            var no = Panel("ClearNo", bar, new Color32(38, 50, 64, 235));
            no.anchorMin = no.anchorMax = new Vector2(1f, 0.5f);
            no.pivot = new Vector2(1f, 0.5f);
            no.sizeDelta = new Vector2(chalf, 30f);
            no.anchoredPosition = Vector2.zero;
            Round(no);
            AddBorder(no, MenuB, 1f);
            var noT = Text_("t", no, "CANCEL", 10, Ink, TextAnchor.MiddleCenter, monoFont);
            noT.fontStyle = FontStyle.Bold;
            Stretch(noT.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var noB = no.gameObject.AddComponent<Button>();
            noB.onClick.AddListener(() => { pendingClearAll = false; Render(); });
        }

        // Body below top bar
        var body = Panel("Body", root, new Color(0, 0, 0, 0));
        Stretch(body, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0f, -64f));
        body.GetComponent<Image>().raycastTarget = false;

        const float LeftW = 470f, RightW = 480f;

        // Left column (fixed 470px)
        var leftPanel = Panel("Left", body, new Color(0, 0, 0, 0));
        Stretch(leftPanel, Vector2.zero, new Vector2(0f, 1f), Vector2.zero, new Vector2(LeftW, 0f));
        leftPanel.GetComponent<Image>().raycastTarget = false;
        var ldiv = Panel("LDiv", leftPanel, new Color(120f / 255f, 180f / 255f, 220f / 255f, 0.10f));
        ldiv.anchorMin = new Vector2(1f, 0.05f); ldiv.anchorMax = new Vector2(1f, 0.95f);
        ldiv.pivot = new Vector2(1f, 0.5f); ldiv.sizeDelta = new Vector2(1f, 0f);
        ldiv.GetComponent<Image>().raycastTarget = false;

        // Right column (fixed 480px)
        var rightPanel = Panel("Right", body, new Color(0, 0, 0, 0));
        Stretch(rightPanel, new Vector2(1f, 0f), Vector2.one, new Vector2(-RightW, 0f), Vector2.zero);
        rightPanel.GetComponent<Image>().raycastTarget = false;
        var rdiv = Panel("RDiv", rightPanel, new Color(120f / 255f, 180f / 255f, 220f / 255f, 0.10f));
        rdiv.anchorMin = new Vector2(0f, 0.05f); rdiv.anchorMax = new Vector2(0f, 0.95f);
        rdiv.pivot = new Vector2(0f, 0.5f); rdiv.sizeDelta = new Vector2(1f, 0f);
        rdiv.GetComponent<Image>().raycastTarget = false;

        // Center column (fills remaining)
        var centerPanel = Panel("Center", body, new Color(0, 0, 0, 0));
        Stretch(centerPanel, Vector2.zero, Vector2.one, new Vector2(LeftW, 0f), new Vector2(-RightW, 0f));
        centerPanel.GetComponent<Image>().raycastTarget = false;

        var selected = DeckStore.Get(selectedDeckId);
        BuildSelectLeaderCard(leftPanel, selected);
        // Decklist before the hex roster: art requests queue in build order, and
        // the ~15-19 rows you're actually reading matter more than the ~37
        // peripheral hex thumbnails, so they should win the race to decode first.
        BuildSelectDecklist(rightPanel, selected);
        BuildSelectHexRoster(centerPanel, allDecks);
    }

    // ════════════════════════════════════════════════════════════════════════
    // Starter deck browser (ST01, ST02, ...). Same leader-left / hex-middle /
    // decklist-right format as the Select screen, but sourced from
    // CardData.StarterDecks (fixed, not user-owned) instead of DeckStore, so the
    // hexes are read-order and non-draggable, and the leader panel offers a
    // "copy to my decks" action instead of edit/delete/favorite.
    // ════════════════════════════════════════════════════════════════════════

    private DeckData FromStarterDef(DeckDef def)
    {
        if (def == null) return null;
        return new DeckData
        {
            id = def.Id,
            name = def.Name,
            leaderId = def.Leader,
            cards = def.List?.Select(t => new DeckEntry { id = t.cardId, count = t.qty }).ToList()
                ?? new List<DeckEntry>(),
        };
    }

    // Some starter decks (ST17/ST18/ST24/ST28, etc.) show a dedicated alt-art print of
    // their leader on the physical box/card rather than the plain booster-set art -
    // see CardData.StarterLeaderArtOverride. Stats lookups should still use def.Leader;
    // only art loading should prefer this id when one is registered.
    private static string StarterLeaderArtId(DeckDef def)
    {
        if (def == null) return null;
        return CardData.StarterLeaderArtOverride.TryGetValue(def.Id, out var altId) ? altId : def.Leader;
    }

    private void RenderStarter()
    {
        var starterDecks = CardData.StarterDecks.Values
            .OrderBy(d => d.Id, StringComparer.OrdinalIgnoreCase).ToList();

        if (string.IsNullOrEmpty(selectedStarterId) || !starterDecks.Any(d => d.Id == selectedStarterId))
            selectedStarterId = starterDecks.Count > 0 ? starterDecks[0].Id : null;

        TopBar("STARTER DECKS", $"{starterDecks.Count} official starter deck(s)  ·  browse & copy",
            () => { view = View.Select; Render(); });

        var body = Panel("Body", root, new Color(0, 0, 0, 0));
        Stretch(body, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0f, -64f));
        body.GetComponent<Image>().raycastTarget = false;

        const float LeftW = 470f, RightW = 480f;

        var leftPanel = Panel("Left", body, new Color(0, 0, 0, 0));
        Stretch(leftPanel, Vector2.zero, new Vector2(0f, 1f), Vector2.zero, new Vector2(LeftW, 0f));
        leftPanel.GetComponent<Image>().raycastTarget = false;
        var ldiv = Panel("LDiv", leftPanel, new Color(120f / 255f, 180f / 255f, 220f / 255f, 0.10f));
        ldiv.anchorMin = new Vector2(1f, 0.05f); ldiv.anchorMax = new Vector2(1f, 0.95f);
        ldiv.pivot = new Vector2(1f, 0.5f); ldiv.sizeDelta = new Vector2(1f, 0f);
        ldiv.GetComponent<Image>().raycastTarget = false;

        var rightPanel = Panel("Right", body, new Color(0, 0, 0, 0));
        Stretch(rightPanel, new Vector2(1f, 0f), Vector2.one, new Vector2(-RightW, 0f), Vector2.zero);
        rightPanel.GetComponent<Image>().raycastTarget = false;
        var rdiv = Panel("RDiv", rightPanel, new Color(120f / 255f, 180f / 255f, 220f / 255f, 0.10f));
        rdiv.anchorMin = new Vector2(0f, 0.05f); rdiv.anchorMax = new Vector2(0f, 0.95f);
        rdiv.pivot = new Vector2(0f, 0.5f); rdiv.sizeDelta = new Vector2(1f, 0f);
        rdiv.GetComponent<Image>().raycastTarget = false;

        var centerPanel = Panel("Center", body, new Color(0, 0, 0, 0));
        Stretch(centerPanel, Vector2.zero, Vector2.one, new Vector2(LeftW, 0f), new Vector2(-RightW, 0f));
        centerPanel.GetComponent<Image>().raycastTarget = false;

        var selectedDef = starterDecks.FirstOrDefault(d => d.Id == selectedStarterId);
        BuildStarterLeaderCard(leftPanel, selectedDef);
        // Decklist before the hex roster - see the matching comment in
        // RenderSelect(): art requests queue in build order, and the selected
        // deck's own list matters more than the peripheral hex thumbnails.
        BuildSelectDecklist(rightPanel, FromStarterDef(selectedDef));
        BuildStarterHexRoster(centerPanel, starterDecks);
    }

    private void BuildStarterLeaderCard(RectTransform panel, DeckDef def)
    {
        const float CARD_W = 300f, CARD_H = 418f, CONTENT_W = 300f;
        const float HDR_H = 30f, NAME_H = 28f, CHIP_H = 20f, BADGE_H = 26f, BTN_H = 40f;
        const float GAP = 14f;
        float totalH = HDR_H + 10f + CARD_H + GAP + NAME_H + 6f + CHIP_H + 16f + BADGE_H + GAP + BTN_H;
        float startY = totalH / 2f;

        if (def == null)
        {
            var noSel = Text_("NoSel", panel,
                "Select a starter deck from the roster to preview it here.",
                13, Muted, TextAnchor.MiddleCenter);
            noSel.horizontalOverflow = HorizontalWrapMode.Wrap;
            Stretch(noSel.rectTransform, new Vector2(0.1f, 0.35f), new Vector2(0.9f, 0.65f),
                Vector2.zero, Vector2.zero);
            return;
        }

        var deck = FromStarterDef(def);

        // ── Header: deck code + name (no favorite star - not applicable here) ──
        var nameT = Text_("DName", panel, string.IsNullOrEmpty(def.Name) ? def.Id : $"{def.Id} — {def.Name}",
            19, Ink, TextAnchor.MiddleLeft);
        nameT.fontStyle = FontStyle.Bold;
        nameT.rectTransform.anchorMin = nameT.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        nameT.rectTransform.pivot = new Vector2(0f, 1f);
        nameT.rectTransform.sizeDelta = new Vector2(CONTENT_W, HDR_H);
        nameT.rectTransform.anchoredPosition = new Vector2(-CONTENT_W / 2f, startY);

        // ── Leader card visual ──
        float cardY = startY - HDR_H - 10f;
        var card = Panel("Card", panel, new Color32(4, 9, 18, 255));
        card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
        card.pivot = new Vector2(0.5f, 1f);
        card.sizeDelta = new Vector2(CARD_W, CARD_H);
        card.anchoredPosition = new Vector2(0f, cardY);
        RoundBig(card);

        var lead = Card(def.Leader);
        if (lead != null)
        {
            string artId = StarterLeaderArtId(def);
            bool haveEntry  = _artCache.TryGetValue(artId, out var cachedArtSp);
            bool knownNoArt = haveEntry && cachedArtSp == null;
            if (!knownNoArt)
            {
                var pimg = MakeRoundedCard(card, true, out _);
                if (cachedArtSp != null) pimg.sprite = cachedArtSp;
                else { pimg.color = new Color32(10, 20, 32, 255); RequestArt(pimg, artId); }
            }
            else
            {
                if (lead.Colors().Length > 0 && ColorSwatch.TryGetValue(lead.Colors()[0], out var sc))
                {
                    var cg = Panel("ColorBg", card, new Color(sc.r, sc.g, sc.b, 0.18f));
                    cg.GetComponent<Image>().raycastTarget = false;
                    Stretch(cg, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                }
                var mono = Text_("Mono", card,
                    !string.IsNullOrEmpty(lead.name) ? lead.name[0].ToString().ToUpper() : "?",
                    72, new Color(1f, 1f, 1f, 0.10f), TextAnchor.MiddleCenter);
                Stretch(mono.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            }
        }
        else
        {
            var nd = Text_("ND", card, "◇", 40, Muted, TextAnchor.MiddleCenter);
            Stretch(nd.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.72f), Vector2.zero, Vector2.zero);
            var nl = Text_("NL", card, "NO LEADER", 14, Muted, TextAnchor.MiddleCenter, monoFont);
            nl.fontStyle = FontStyle.Bold;
            Stretch(nl.rectTransform, new Vector2(0f, 0.36f), new Vector2(1f, 0.5f), Vector2.zero, Vector2.zero);
        }
        AddBorder(card, new Color(120f / 255f, 180f / 255f, 220f / 255f, 0.5f), 1f);

        // ── Caption under the card ──
        float nameY = cardY - CARD_H - GAP;
        var capName = Text_("LeadName", panel, lead != null ? lead.name : "No leader set",
            22, lead != null ? Ink : Muted, TextAnchor.UpperCenter);
        capName.fontStyle = FontStyle.Bold;
        capName.horizontalOverflow = HorizontalWrapMode.Wrap;
        capName.rectTransform.anchorMin = capName.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        capName.rectTransform.pivot = new Vector2(0.5f, 1f);
        capName.rectTransform.sizeDelta = new Vector2(CONTENT_W, NAME_H);
        capName.rectTransform.anchoredPosition = new Vector2(0f, nameY);

        float chipsY = nameY - NAME_H - 6f;
        if (lead != null)
        {
            var chips = Row("LeadChips", panel, 6f, TextAnchor.MiddleCenter);
            chips.anchorMin = chips.anchorMax = new Vector2(0.5f, 0.5f);
            chips.pivot = new Vector2(0.5f, 1f);
            chips.sizeDelta = new Vector2(CONTENT_W, CHIP_H);
            chips.anchoredPosition = new Vector2(0f, chipsY);
            foreach (var col in lead.Colors()) AddColorChip(chips, col);
            AddPreviewChip(chips, "LIFE " + lead.life, false);
            AddPreviewChip(chips, "POWER " + lead.power, false);
            AddNicheRuleChips(chips, def.Leader);
        }

        // ── Legal badge ──
        float badgeY = chipsY - CHIP_H - 16f;
        var (ok, total, _) = Validate(deck);
        var badge = Panel("Badge", panel, ok
            ? new Color(GoodGreen.r, GoodGreen.g, GoodGreen.b, 0.16f)
            : new Color(RedAccent.r, RedAccent.g, RedAccent.b, 0.14f));
        badge.anchorMin = badge.anchorMax = new Vector2(0.5f, 0.5f);
        badge.pivot = new Vector2(0.5f, 1f);
        badge.sizeDelta = new Vector2(210f, BADGE_H);
        badge.anchoredPosition = new Vector2(0f, badgeY);
        Round(badge);
        AddBorder(badge, ok ? GoodGreen : RedAccent, 1f);
        var badgeT = Text_("BT", badge,
            (ok ? "● LEGAL  ·  " : "● INVALID  ·  ") + total + " / 50",
            10, ok ? GoodGreen : RedAccent, TextAnchor.MiddleCenter, monoFont);
        badgeT.fontStyle = FontStyle.Bold;
        Stretch(badgeT.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        // ── Primary action: in picker mode (Solo/PvP deck selection) this deck
        // can be used directly - copy it into DeckStore (so match setup can
        // resolve it the same way as any owned deck) and hand it straight back
        // to the picker. Outside picker mode it's just COPY TO MY DECKS, since
        // starter decks aren't user-owned data and can't be played from here. ──
        float btnY = badgeY - BADGE_H - GAP;
        bool canAdd = DeckStore.CanAddNew();
        var copyBtn = Panel("Copy", panel, canAdd ? (Color)Accent : (Color)new Color32(40, 60, 78, 220));
        copyBtn.anchorMin = copyBtn.anchorMax = new Vector2(0.5f, 0.5f);
        copyBtn.pivot = new Vector2(0f, 1f);
        copyBtn.sizeDelta = new Vector2(220f, BTN_H);
        copyBtn.anchoredPosition = new Vector2(-110f, btnY);
        Round(copyBtn);
        if (!canAdd) AddBorder(copyBtn, MenuB, 1f);
        string btnLabel = canAdd ? (pickerActive ? "USE THIS DECK ▸" : "COPY TO MY DECKS") : "DECK LIMIT REACHED";
        var copyT = Text_("t", copyBtn, btnLabel, 13,
            canAdd ? BadgeInk : Muted, TextAnchor.MiddleCenter, monoFont);
        copyT.fontStyle = FontStyle.Bold;
        Stretch(copyT.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        if (canAdd)
        {
            var capturedDef = def;
            bool wasPicking = pickerActive;
            copyBtn.gameObject.AddComponent<Button>().onClick.AddListener(() =>
            {
                var copy = FromStarterDef(capturedDef);
                copy.id = DeckStore.NewId();
                copy.name = capturedDef.Name;
                copy.slot = -1;
                DeckStore.Save(copy);
                if (wasPicking)
                {
                    ConfirmPicker(copy.id);
                    return;
                }
                selectedDeckId = copy.id;
                view = View.Select;
                Render();
            });
        }
    }

    // ── Center: hex roster (fixed, read-order, non-draggable) ──────────────────
    private void BuildStarterHexRoster(RectTransform parent, List<DeckDef> starterDecks)
    {
        const int RINGS = 3;   // same visual size/shape as the movable deck roster

        float availW = parent.rect.width, availH = parent.rect.height;
        if (availW <= 0f) availW = 1920f - 470f - 480f;
        if (availH <= 0f) availH = 1080f - 64f;
        availH -= 56f;
        float S = Mathf.Min(56f,
            (availW - 24f) / (3f * RINGS + 2f),
            availH / ((2f * RINGS + 1f) * Mathf.Sqrt(3f) + 0.6f));
        hexFontScale = S / 72f;

        float HW = 2f * S;
        float HH = Mathf.Sqrt(3f) * S;

        var container = new GameObject("StarterHexContainer").AddComponent<RectTransform>();
        container.SetParent(parent, false);
        container.anchorMin = container.anchorMax = new Vector2(0.5f, 0.5f);
        container.pivot = new Vector2(0.5f, 0.5f);
        container.sizeDelta = Vector2.zero;
        container.anchoredPosition = new Vector2(0f, 18f);

        var cells = new List<(int q, int r, float x, float y)>();
        for (int q = -RINGS; q <= RINGS; q++)
            for (int r = -RINGS; r <= RINGS; r++)
            {
                int s = -q - r;
                if (Mathf.Max(Mathf.Abs(q), Mathf.Abs(r), Mathf.Abs(s)) <= RINGS)
                {
                    float px = 1.5f * S * q;
                    float py = -HH * (r + q * 0.5f);
                    cells.Add((q, r, px, py));
                }
            }

        // Reading order: topmost hex first, then down, left to right within a row -
        // NOT the "outward from center" order the movable deck-select grid uses. The
        // (0,0) center cell is still detected independently below regardless of where
        // it lands in this order, so it stays reserved for RANDOM either way.
        cells.Sort((a, b) =>
        {
            int cmp = b.y.CompareTo(a.y);
            return cmp != 0 ? cmp : a.x.CompareTo(b.x);
        });

        var hexSp = HexSprite();
        var metalSp = HexMetalSprite();

        // Static socket underlay, same as the movable grid - purely cosmetic backdrop.
        foreach (var (sq, sr, scx, scy) in cells)
        {
            var sock = Panel("Socket", container, new Color(SteelDim.r, SteelDim.g, SteelDim.b, 0.55f));
            sock.anchorMin = sock.anchorMax = new Vector2(0.5f, 0.5f);
            sock.pivot = new Vector2(0.5f, 0.5f);
            sock.sizeDelta = new Vector2(HW, HH);
            sock.anchoredPosition = new Vector2(scx, scy);
            var si = sock.GetComponent<Image>();
            si.sprite = hexSp; si.type = Image.Type.Simple; si.raycastTarget = false;
            var fill = Panel("Fill", sock, new Color(0x0B / 255f, 0x15 / 255f, 0x22 / 255f, 1f));
            Stretch(fill, Vector2.zero, Vector2.one, new Vector2(2f, 2f), new Vector2(-2f, -2f));
            var fi = fill.GetComponent<Image>();
            fi.sprite = hexSp; fi.type = Image.Type.Simple; fi.raycastTarget = false;
        }

        starterHexCells.Clear();

        int slotIdx = 0;
        foreach (var (q, r, cx, cy) in cells)
        {
            bool isCenter = (q == 0 && r == 0);

            var cellGo = new GameObject(isCenter ? "Cell_RANDOM" : $"StarterCell_{slotIdx}");
            var cell = cellGo.AddComponent<RectTransform>();
            var hitImg = cellGo.AddComponent<Image>();
            hitImg.color = Color.clear;
            hitImg.sprite = hexSp;
            hitImg.alphaHitTestMinimumThreshold = 0.5f;
            cell.SetParent(container, false);
            cell.anchorMin = cell.anchorMax = new Vector2(0.5f, 0.5f);
            cell.pivot = new Vector2(0.5f, 0.5f);
            cell.sizeDelta = new Vector2(HW, HH);
            cell.anchoredPosition = new Vector2(cx, cy);

            if (isCenter)
            {
                BuildStarterRandomHexCell(cell, hexSp, metalSp, starterDecks);
            }
            else
            {
                DeckDef def = slotIdx < starterDecks.Count ? starterDecks[slotIdx] : null;
                if (def != null) starterHexCells[def.Id] = cell;
                BuildStarterDeckHexCell(cell, hexSp, metalSp, def);
                slotIdx++;
            }
        }

        if (!string.IsNullOrEmpty(selectedStarterId) &&
            starterHexCells.TryGetValue(selectedStarterId, out var selCell) && selCell != null)
            selCell.SetAsLastSibling();

        var hint = Text_("Hint", parent,
            "click to preview  ·  ? for a random starter deck",
            11, new Color(0x7F / 255f, 0x8C / 255f, 0xA0 / 255f, 1f),
            TextAnchor.MiddleCenter, monoFont);
        Stretch(hint.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f),
            new Vector2(0f, 10f), new Vector2(0f, 36f));
    }

    private void BuildStarterDeckHexCell(RectTransform cell, Sprite hexSp, Sprite metalSp, DeckDef def)
    {
        bool isEmpty = def == null;
        bool isSelected = def != null && def.Id == selectedStarterId;
        const float pad = 3f;

        if (isSelected)
        {
            cell.SetAsLastSibling();
            AddHexSelectionGlow(cell);
        }

        var ring = Panel("Ring", cell, isEmpty ? SteelDim : Steel);
        ring.GetComponent<Image>().sprite = metalSp;
        ring.GetComponent<Image>().type = Image.Type.Simple;
        ring.GetComponent<Image>().raycastTarget = false;
        Stretch(ring, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        if (isEmpty)
        {
            // Unlike the movable deck grid, an unpopulated starter-deck slot isn't a
            // "click here to create a new deck" invite - just a blank socket, no "+".
            var innerE = Panel("InnerE", cell, new Color(0x0B / 255f, 0x15 / 255f, 0x22 / 255f, 0.9f));
            innerE.GetComponent<Image>().sprite = hexSp;
            innerE.GetComponent<Image>().type = Image.Type.Simple;
            innerE.GetComponent<Image>().raycastTarget = false;
            Stretch(innerE, Vector2.zero, Vector2.one, new Vector2(3f, 3f), new Vector2(-3f, -3f));
            return;
        }

        var artMask = Panel("ArtMask", cell, new Color32(8, 16, 24, 255));
        var amImg = artMask.GetComponent<Image>();
        amImg.sprite = hexSp; amImg.type = Image.Type.Simple; amImg.raycastTarget = false;
        Stretch(artMask, Vector2.zero, Vector2.one, new Vector2(pad, pad), new Vector2(-pad, -pad));
        var maskComp = artMask.gameObject.AddComponent<Mask>();
        maskComp.showMaskGraphic = false;

        var lead = Card(def.Leader);
        string artId = StarterLeaderArtId(def);
        bool haveEntry  = _thumbCache.TryGetValue(artId, out var cachedArtSp);
        bool knownNoArt = haveEntry && cachedArtSp == null;

        if (!knownNoArt)
        {
            const float SAFE_L = 0.07f, SAFE_R = 0.93f;   // stay clear of the card border
            const float SAFE_T = 0.05f, SAFE_B = 0.60f;   // ...and of the text box below the art
            const float BLEED = 1.03f;

            float mW = cell.sizeDelta.x - 2f * pad, mH = cell.sizeDelta.y - 2f * pad;
            float aspect = (cachedArtSp != null && cachedArtSp.rect.height > 0f)
                ? cachedArtSp.rect.width / cachedArtSp.rect.height : 0.716f;
            float hexAspect = mH > 0f ? mW / mH : 1.1547f;

            // Per-card zoom (face-data.json "z"): tightens the window on
            // small/off-centre faces and on the focused half of duo leaders.
            float visH = 0.44f * FaceZoom(artId);
            float visW = visH * hexAspect / Mathf.Max(aspect, 0.01f);
            if (visW > SAFE_R - SAFE_L) { visW = SAFE_R - SAFE_L; visH = visW * aspect / hexAspect; }

            Vector2 face = FaceCenter(artId, lead != null ? lead.type : null);
            float cxFrac = Mathf.Clamp(face.x, SAFE_L + visW * 0.5f, SAFE_R - visW * 0.5f);
            float cyFrac = Mathf.Clamp(face.y + visH * 0.05f, SAFE_T + visH * 0.5f, SAFE_B - visH * 0.5f);

            float aw = (mW / visW) * BLEED;
            float ah = aw / aspect;

            // Loading placeholder must be OPAQUE and match artMask's own fill
            // (8,16,24) - artMask's own graphic is hidden (showMaskGraphic =
            // false, it's stencil-only), so a transparent Art child here would
            // let the steel Ring panel underneath show through as grey across
            // the whole hex interior instead of just its intended thin rim.
            var artImg = Panel("Art", artMask, cachedArtSp != null ? Color.white : new Color32(8, 16, 24, 255));
            var ai = artImg.GetComponent<Image>();
            ai.raycastTarget = false;
            artImg.anchorMin = artImg.anchorMax = new Vector2(0.5f, 0.5f);
            artImg.pivot = new Vector2(0.5f, 0.5f);
            artImg.sizeDelta = new Vector2(aw, ah);
            artImg.anchoredPosition = new Vector2(-(cxFrac - 0.5f) * aw, (cyFrac - 0.5f) * ah);

            if (cachedArtSp != null) { ai.sprite = cachedArtSp; ai.type = Image.Type.Simple; ai.preserveAspect = false; }
            else RequestThumbArt(ai, artId);
        }
        else
        {
            string init = (lead != null && !string.IsNullOrEmpty(lead.name))
                ? lead.name[0].ToString().ToUpper() : "?";
            var mono = Text_("Mono", artMask, init, HexFont(32),
                new Color(1f, 1f, 1f, 0.16f), TextAnchor.MiddleCenter);
            Stretch(mono.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        }

        // Click -> preview only. No HexDragReorder component: starter-deck hexes
        // are fixed in place, never draggable.
        var btn = cell.gameObject.AddComponent<Button>();
        var capturedId = def.Id;
        btn.onClick.AddListener(() =>
        {
            selectedStarterId = capturedId;
            Render();
        });
    }

    private void BuildStarterRandomHexCell(RectTransform cell, Sprite hexSp, Sprite metalSp, List<DeckDef> starterDecks)
    {
        var outer = Panel("Outer", cell, DeepBlueMetal);
        outer.GetComponent<Image>().sprite = metalSp;
        outer.GetComponent<Image>().type = Image.Type.Simple;
        outer.GetComponent<Image>().raycastTarget = false;
        Stretch(outer, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        var inner = Panel("Inner", cell, new Color(0x0B / 255f, 0x1C / 255f, 0x2E / 255f, 1f));
        inner.GetComponent<Image>().sprite = hexSp;
        inner.GetComponent<Image>().type = Image.Type.Simple;
        inner.GetComponent<Image>().raycastTarget = false;
        Stretch(inner, Vector2.zero, Vector2.one, new Vector2(3f, 3f), new Vector2(-3f, -3f));

        var qMark = Text_("Q", cell, "?", HexFont(48), Bronze, TextAnchor.MiddleCenter, bronzeFont);
        qMark.fontStyle = FontStyle.Bold;
        Stretch(qMark.rectTransform, Vector2.zero, Vector2.one, new Vector2(0f, -4f), new Vector2(0f, -4f));
        var qOutline = qMark.gameObject.AddComponent<Outline>();
        qOutline.effectColor = new Color32(46, 26, 12, 235);
        qOutline.effectDistance = new Vector2(1.4f, -1.4f);
        qOutline.useGraphicAlpha = false;
        qMark.gameObject.AddComponent<Mask>().showMaskGraphic = true;

        var qSheen = Panel("QSheen", qMark.rectTransform, Color.white);
        var qSheenImg = qSheen.GetComponent<Image>();
        qSheenImg.sprite = GetVGradientSprite();
        qSheenImg.type = Image.Type.Simple;
        qSheenImg.color = new Color(1f, 0.87f, 0.62f, 0.65f);
        qSheenImg.raycastTarget = false;
        Stretch(qSheen, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        var btn = cell.gameObject.AddComponent<Button>();
        btn.onClick.AddListener(() =>
        {
            if (starterDecks.Count > 0)
            {
                selectedStarterId = starterDecks[UnityEngine.Random.Range(0, starterDecks.Count)].Id;
                Render();
            }
        });
    }

    // ── Left column: large leader card + name/legal/buttons ─────────────────
    private void BuildSelectLeaderCard(RectTransform panel, DeckData deck)
    {
        const float CARD_W = 300f, CARD_H = 418f, CONTENT_W = 300f;
        const float HDR_H = 30f, NAME_H = 28f, CHIP_H = 20f, BADGE_H = 26f, BTN_H = 40f;
        const float GAP = 14f;
        // Stack: header + card + caption(name + chips) + badge + edit button, centred.
        float totalH = HDR_H + 10f + CARD_H + GAP + NAME_H + 6f + CHIP_H + 16f + BADGE_H + GAP + BTN_H;
        float startY = totalH / 2f;

        if (deck == null)
        {
            var noSel = Text_("NoSel", panel,
                "Select a deck from the roster to preview it here.",
                13, Muted, TextAnchor.MiddleCenter);
            noSel.horizontalOverflow = HorizontalWrapMode.Wrap;
            Stretch(noSel.rectTransform, new Vector2(0.1f, 0.35f), new Vector2(0.9f, 0.65f),
                Vector2.zero, Vector2.zero);
            return;
        }

        bool fav = deck.favorite;

        // ── Header: fav ★/☆ + deck name ──────────────────────────────────────
        var favBtn = Panel("FavBtn", panel, new Color(0, 0, 0, 0));
        favBtn.anchorMin = favBtn.anchorMax = new Vector2(0.5f, 0.5f);
        favBtn.pivot = new Vector2(0f, 1f);
        favBtn.sizeDelta = new Vector2(26f, HDR_H);
        favBtn.anchoredPosition = new Vector2(-CONTENT_W / 2f, startY);
        var favT = Text_("Fav", favBtn, fav ? "★" : "☆", 18, fav ? Gold : Muted,
            TextAnchor.MiddleCenter);
        Stretch(favT.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var favComp = favBtn.gameObject.AddComponent<Button>();
        var favId = deck.id;
        favComp.onClick.AddListener(() => { DeckStore.ToggleFavorite(favId); Render(); });

        var nameT = Text_("DName", panel, string.IsNullOrEmpty(deck.name) ? "Untitled" : deck.name,
            19, Ink, TextAnchor.MiddleLeft);
        nameT.fontStyle = FontStyle.Bold;
        nameT.rectTransform.anchorMin = nameT.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        nameT.rectTransform.pivot = new Vector2(0f, 1f);
        nameT.rectTransform.sizeDelta = new Vector2(CONTENT_W - 32f, HDR_H);
        nameT.rectTransform.anchoredPosition = new Vector2(-CONTENT_W / 2f + 30f, startY);

        // ── Leader card visual (clean — no overlaid text) ────────────────────
        float cardY = startY - HDR_H - 10f;
        var card = Panel("Card", panel, new Color32(4, 9, 18, 255));
        card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
        card.pivot = new Vector2(0.5f, 1f);
        card.sizeDelta = new Vector2(CARD_W, CARD_H);
        card.anchoredPosition = new Vector2(0f, cardY);
        RoundBig(card);

        var lead = Card(deck.leaderId);
        if (lead != null)
        {
            bool haveEntry  = _artCache.TryGetValue(deck.leaderId, out var cachedArtSp);
            bool knownNoArt = haveEntry && cachedArtSp == null;
            if (!knownNoArt)
            {
                var pimg = MakeRoundedCard(card, true, out _);
                if (cachedArtSp != null) pimg.sprite = cachedArtSp;   // the full leader card, shown clean
                else { pimg.color = new Color32(10, 20, 32, 255); RequestArt(pimg, deck.leaderId); }
            }
            else
            {
                // Colour-identity placeholder with monogram (no art on disk).
                if (lead.Colors().Length > 0 && ColorSwatch.TryGetValue(lead.Colors()[0], out var sc))
                {
                    var cg = Panel("ColorBg", card, new Color(sc.r, sc.g, sc.b, 0.18f));
                    cg.GetComponent<Image>().raycastTarget = false;
                    Stretch(cg, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                }
                var mono = Text_("Mono", card,
                    !string.IsNullOrEmpty(lead.name) ? lead.name[0].ToString().ToUpper() : "?",
                    72, new Color(1f, 1f, 1f, 0.10f), TextAnchor.MiddleCenter);
                Stretch(mono.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            }
        }
        else
        {
            var nd = Text_("ND", card, "◇", 40, Muted, TextAnchor.MiddleCenter);
            Stretch(nd.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.72f), Vector2.zero, Vector2.zero);
            var nl = Text_("NL", card, "NO LEADER", 14, Muted, TextAnchor.MiddleCenter, monoFont);
            nl.fontStyle = FontStyle.Bold;
            Stretch(nl.rectTransform, new Vector2(0f, 0.36f), new Vector2(1f, 0.5f), Vector2.zero, Vector2.zero);
        }
        // Border last so it frames the art rather than sitting behind it.
        AddBorder(card, fav ? Gold : new Color(120f / 255f, 180f / 255f, 220f / 255f, 0.5f),
            fav ? 1.6f : 1f);

        // ── Caption UNDER the card: leader name + colour / life / power ───────
        float nameY  = cardY - CARD_H - GAP;
        var capName = Text_("LeadName", panel, lead != null ? lead.name : "No leader set",
            22, lead != null ? Ink : Muted, TextAnchor.UpperCenter);
        capName.fontStyle = FontStyle.Bold;
        capName.horizontalOverflow = HorizontalWrapMode.Wrap;
        capName.rectTransform.anchorMin = capName.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        capName.rectTransform.pivot = new Vector2(0.5f, 1f);
        capName.rectTransform.sizeDelta = new Vector2(CONTENT_W, NAME_H);
        capName.rectTransform.anchoredPosition = new Vector2(0f, nameY);

        float chipsY = nameY - NAME_H - 6f;
        if (lead != null)
        {
            var chips = Row("LeadChips", panel, 6f, TextAnchor.MiddleCenter);
            chips.anchorMin = chips.anchorMax = new Vector2(0.5f, 0.5f);
            chips.pivot = new Vector2(0.5f, 1f);
            chips.sizeDelta = new Vector2(CONTENT_W, CHIP_H);
            chips.anchoredPosition = new Vector2(0f, chipsY);
            foreach (var col in lead.Colors()) AddColorChip(chips, col);
            AddPreviewChip(chips, "LIFE " + lead.life, false);
            AddPreviewChip(chips, "POWER " + lead.power, false);
            AddNicheRuleChips(chips, deck.leaderId);
        }

        // ── Legal badge ──────────────────────────────────────────────────────
        float badgeY = chipsY - CHIP_H - 16f;
        var (ok, total, _) = Validate(deck);
        var badge = Panel("Badge", panel, ok
            ? new Color(GoodGreen.r, GoodGreen.g, GoodGreen.b, 0.16f)
            : new Color(RedAccent.r, RedAccent.g, RedAccent.b, 0.14f));
        badge.anchorMin = badge.anchorMax = new Vector2(0.5f, 0.5f);
        badge.pivot = new Vector2(0.5f, 1f);
        badge.sizeDelta = new Vector2(210f, BADGE_H);
        badge.anchoredPosition = new Vector2(0f, badgeY);
        Round(badge);
        AddBorder(badge, ok ? GoodGreen : RedAccent, 1f);
        var badgeT = Text_("BT", badge,
            (ok ? "● LEGAL  ·  " : "● INVALID  ·  ") + total + " / 50",
            10, ok ? GoodGreen : RedAccent, TextAnchor.MiddleCenter, monoFont);
        badgeT.fontStyle = FontStyle.Bold;
        Stretch(badgeT.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        // ── EDIT + DELETE buttons (no Play — selecting a deck already locks it in) ─
        float btnY = badgeY - BADGE_H - GAP;
        const float ROW_W = 220f, DEL_W = 92f, ROW_GAP = 8f;
        float editW = ROW_W - DEL_W - ROW_GAP;

        // EDIT DECK (left)
        var editBtn = Panel("Edit", panel, Accent);
        editBtn.anchorMin = editBtn.anchorMax = new Vector2(0.5f, 0.5f);
        editBtn.pivot = new Vector2(0f, 1f);
        editBtn.sizeDelta = new Vector2(editW, BTN_H);
        editBtn.anchoredPosition = new Vector2(-ROW_W / 2f, btnY);
        Round(editBtn);
        var editT = Text_("t", editBtn, "EDIT DECK", 13, BadgeInk, TextAnchor.MiddleCenter, monoFont);
        editT.fontStyle = FontStyle.Bold;
        Stretch(editT.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var editComp = editBtn.gameObject.AddComponent<Button>();
        var editId = deck.id;
        editComp.onClick.AddListener(() =>
        {
            pendingDeleteDeckId = null;   // cancel any armed delete
            var d = DeckStore.Get(editId);
            if (d != null)
            {
                editing = d.Clone(); view = View.Editor; pickingLeader = false;
                ResetFilters(); Render();
            }
        });

        // DELETE (right). Unarmed: a single red "DELETE" in its slot. Armed: the
        // row transforms into a dual bar over the whole EDIT+DELETE width — a red
        // DELETE half (commits) and a neutral CANCEL half (aborts).
        bool delArmed = pendingDeleteDeckId == deck.id;
        var delId = deck.id;
        if (!delArmed)
        {
            var delBtn = Panel("Delete", panel, new Color(RedAccent.r, RedAccent.g, RedAccent.b, 0.18f));
            delBtn.anchorMin = delBtn.anchorMax = new Vector2(0.5f, 0.5f);
            delBtn.pivot = new Vector2(0f, 1f);
            delBtn.sizeDelta = new Vector2(DEL_W, BTN_H);
            delBtn.anchoredPosition = new Vector2(-ROW_W / 2f + editW + ROW_GAP, btnY);
            Round(delBtn);
            AddBorder(delBtn, RedAccent, 1f);
            var delT = Text_("dt", delBtn, "DELETE", 13, RedAccent, TextAnchor.MiddleCenter, monoFont);
            delT.fontStyle = FontStyle.Bold;
            Stretch(delT.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var delComp = delBtn.gameObject.AddComponent<Button>();
            delComp.onClick.AddListener(() => { pendingDeleteDeckId = delId; Render(); });   // arm
        }
        else
        {
            var bar = Panel("DelConfirmBar", panel, new Color(0, 0, 0, 0));
            bar.anchorMin = bar.anchorMax = new Vector2(0.5f, 0.5f);
            bar.pivot = new Vector2(0f, 1f);
            bar.sizeDelta = new Vector2(ROW_W, BTN_H);
            bar.anchoredPosition = new Vector2(-ROW_W / 2f, btnY);
            bar.GetComponent<Image>().raycastTarget = false;
            bar.SetAsLastSibling();   // covers the EDIT button while confirming

            float halfW = (ROW_W - ROW_GAP) / 2f;

            // Confirm (left, solid red)
            var yes = Panel("DelYes", bar, (Color)RedAccent);
            yes.anchorMin = yes.anchorMax = new Vector2(0f, 0.5f);
            yes.pivot = new Vector2(0f, 0.5f);
            yes.sizeDelta = new Vector2(halfW, BTN_H);
            yes.anchoredPosition = Vector2.zero;
            Round(yes);
            var yesT = Text_("yt", yes, "DELETE", 13, Ink, TextAnchor.MiddleCenter, monoFont);
            yesT.fontStyle = FontStyle.Bold;
            Stretch(yesT.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var yesB = yes.gameObject.AddComponent<Button>();
            yesB.onClick.AddListener(() =>
            {
                pendingDeleteDeckId = null;
                DeckStore.Delete(delId);
                selectedDeckId = DeckStore.ActiveOrDefault()?.id;
                Render();
            });

            // Cancel (right, neutral)
            var no = Panel("DelNo", bar, new Color32(38, 50, 64, 235));
            no.anchorMin = no.anchorMax = new Vector2(1f, 0.5f);
            no.pivot = new Vector2(1f, 0.5f);
            no.sizeDelta = new Vector2(halfW, BTN_H);
            no.anchoredPosition = Vector2.zero;
            Round(no);
            AddBorder(no, MenuB, 1f);
            var noT = Text_("nt", no, "CANCEL", 13, Ink, TextAnchor.MiddleCenter, monoFont);
            noT.fontStyle = FontStyle.Bold;
            Stretch(noT.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var noB = no.gameObject.AddComponent<Button>();
            noB.onClick.AddListener(() => { pendingDeleteDeckId = null; Render(); });
        }

        if (pickerActive)
        {
            // Picker mode: an explicit CONFIRM button hands this deck back to the
            // caller instead of silently overwriting DeckStore.ActiveDeckId.
            bool hasLeader = !string.IsNullOrEmpty(deck.leaderId);
            float confirmY = btnY - BTN_H - 10f;
            var confirmBtn = Panel("ConfirmPick", panel,
                hasLeader ? (Color)Accent : (Color)new Color32(40, 60, 78, 220));
            confirmBtn.anchorMin = confirmBtn.anchorMax = new Vector2(0.5f, 0.5f);
            confirmBtn.pivot = new Vector2(0f, 1f);
            confirmBtn.sizeDelta = new Vector2(ROW_W, BTN_H);
            confirmBtn.anchoredPosition = new Vector2(-ROW_W / 2f, confirmY);
            Round(confirmBtn);
            if (!hasLeader) AddBorder(confirmBtn, MenuB, 1f);
            var confirmT = Text_("t", confirmBtn, hasLeader ? "USE THIS DECK ▸" : "SET A LEADER FIRST",
                13, hasLeader ? BadgeInk : Muted, TextAnchor.MiddleCenter, monoFont);
            confirmT.fontStyle = FontStyle.Bold;
            Stretch(confirmT.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            if (hasLeader)
            {
                var pickedId = deck.id;
                confirmBtn.gameObject.AddComponent<Button>().onClick.AddListener(() => ConfirmPicker(pickedId));
            }
            return;
        }

        // This deck is the active one (set on click). Tell the player how to launch.
        bool isActive = DeckStore.ActiveDeckId == deck.id;
        var note = Text_("ActiveNote", panel,
            isActive ? "✓ locked in — press BACK to play" : "click this deck to lock it in",
            10, isActive ? GoodGreen : Muted, TextAnchor.UpperCenter, monoFont);
        note.rectTransform.anchorMin = note.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        note.rectTransform.pivot = new Vector2(0.5f, 1f);
        note.rectTransform.sizeDelta = new Vector2(CONTENT_W, 16f);
        note.rectTransform.anchoredPosition = new Vector2(0f, btnY - BTN_H - 8f);
    }

    private void BuildSelectStatPair(RectTransform card, string label, string value, float x, float fromBottom)
    {
        var lbl = Text_("Lbl" + label, card, label, 8, Muted, TextAnchor.LowerLeft, monoFont);
        lbl.fontStyle = FontStyle.Bold;
        lbl.rectTransform.anchorMin = lbl.rectTransform.anchorMax = new Vector2(0f, 0f);
        lbl.rectTransform.pivot = new Vector2(0f, 0f);
        lbl.rectTransform.sizeDelta = new Vector2(60f, 14f);
        lbl.rectTransform.anchoredPosition = new Vector2(x, fromBottom + 26f);

        var val = Text_("Val" + label, card, value, 22, Accent2, TextAnchor.LowerLeft, monoFont);
        val.fontStyle = FontStyle.Bold;
        val.rectTransform.anchorMin = val.rectTransform.anchorMax = new Vector2(0f, 0f);
        val.rectTransform.pivot = new Vector2(0f, 0f);
        val.rectTransform.sizeDelta = new Vector2(80f, 32f);
        val.rectTransform.anchoredPosition = new Vector2(x, fromBottom);
    }

    // ── Center: hex roster ────────────────────────────────────────────────────
    private void BuildSelectHexRoster(RectTransform parent, List<DeckData> decks)
    {
        const int RINGS = 3;   // rings around the RANDOM centre → 36 deck slots

        // Size the hexes so the whole radius-3 cluster fits inside the centre
        // region. Flat-top extents for edge length S: width 11S, height ≈ 12.13S.
        float availW = parent.rect.width, availH = parent.rect.height;
        if (availW <= 0f) availW = 1920f - 470f - 480f;
        if (availH <= 0f) availH = 1080f - 64f;
        availH -= 56f;                                    // room for the hint line
        float S = Mathf.Min(56f,
            (availW - 24f) / (3f * RINGS + 2f),
            availH / ((2f * RINGS + 1f) * Mathf.Sqrt(3f) + 0.6f));
        hexFontScale = S / 72f;

        float HW = 2f * S;
        float HH = Mathf.Sqrt(3f) * S;

        // Cluster anchored to center of center panel (slightly above center to leave room for hint)
        var container = new GameObject("HexContainer").AddComponent<RectTransform>();
        container.SetParent(parent, false);
        container.anchorMin = container.anchorMax = new Vector2(0.5f, 0.5f);
        container.pivot = new Vector2(0.5f, 0.5f);
        container.sizeDelta = Vector2.zero;
        container.anchoredPosition = new Vector2(0f, 18f);

        // Generate all 37 cells for hex-radius-3 (axial coordinates)
        var cells = new List<(int q, int r, float x, float y)>();
        for (int q = -RINGS; q <= RINGS; q++)
            for (int r = -RINGS; r <= RINGS; r++)
            {
                int s = -q - r;
                if (Mathf.Max(Mathf.Abs(q), Mathf.Abs(r), Mathf.Abs(s)) <= RINGS)
                {
                    float px = 1.5f * S * q;
                    float py = -HH * (r + q * 0.5f);
                    cells.Add((q, r, px, py));
                }
            }

        // Sort: center (ring 0) first, then outward; same ring → by angle
        cells.Sort((a, b) =>
        {
            int ra = Mathf.Max(Mathf.Abs(a.q), Mathf.Abs(a.r), Mathf.Abs(a.q + a.r));
            int rb = Mathf.Max(Mathf.Abs(b.q), Mathf.Abs(b.r), Mathf.Abs(b.q + b.r));
            if (ra != rb) return ra.CompareTo(rb);
            return Mathf.Atan2(a.y, a.x).CompareTo(Mathf.Atan2(b.y, b.x));
        });

        var hexSp = HexSprite();
        var metalSp = HexMetalSprite();

        // Static socket underlay: every lattice position gets a non-interactive
        // empty-hex backdrop UNDERNEATH the live cells, so a tile that slides or
        // is dragged away reveals an empty socket instead of a bare hole.
        foreach (var (sq, sr, scx, scy) in cells)
        {
            var sock = Panel("Socket", container, new Color(SteelDim.r, SteelDim.g, SteelDim.b, 0.55f));
            sock.anchorMin = sock.anchorMax = new Vector2(0.5f, 0.5f);
            sock.pivot = new Vector2(0.5f, 0.5f);
            sock.sizeDelta = new Vector2(HW, HH);
            sock.anchoredPosition = new Vector2(scx, scy);
            var si = sock.GetComponent<Image>();
            si.sprite = hexSp; si.type = Image.Type.Simple; si.raycastTarget = false;
            var fill = Panel("Fill", sock, new Color(0x0B / 255f, 0x15 / 255f, 0x22 / 255f, 1f));
            Stretch(fill, Vector2.zero, Vector2.one, new Vector2(2f, 2f), new Vector2(-2f, -2f));
            var fi = fill.GetComponent<Image>();
            fi.sprite = hexSp; fi.type = Image.Type.Simple; fi.raycastTarget = false;
        }

        // Cell 0 is the centre RANDOM cell; the other 36 are deck slots. Each
        // deck sits at its OWN stored slot (deck.slot) — empty cells in between
        // are fine. Record slot centres so drags resolve to slots.
        hexSlotCenters.Clear();
        hexDeckCells.Clear();
        hexPreviewIds.Clear();
        hexPreviewSlots.Clear();

        var deckBySlot = new Dictionary<int, DeckData>();
        foreach (var d in decks)
            if (d.slot >= 0 && !deckBySlot.ContainsKey(d.slot)) deckBySlot[d.slot] = d;

        int slotIdx = 0;
        for (int i = 0; i < cells.Count; i++)
        {
            var (q, r, cx, cy) = cells[i];
            bool isCenter = (q == 0 && r == 0);

            var cellGo = new GameObject(isCenter ? "Cell_RANDOM" : $"Cell_{i}");
            var cell = cellGo.AddComponent<RectTransform>();
            var hitImg = cellGo.AddComponent<Image>();
            hitImg.color = Color.clear;
            // Hex-shaped hit area — otherwise the hit zone is the full bounding
            // rect and neighbours' invisible corners steal clicks/drags.
            hitImg.sprite = hexSp;
            hitImg.alphaHitTestMinimumThreshold = 0.5f;
            cell.SetParent(container, false);
            cell.anchorMin = cell.anchorMax = new Vector2(0.5f, 0.5f);
            cell.pivot = new Vector2(0.5f, 0.5f);
            cell.sizeDelta = new Vector2(HW, HH);
            cell.anchoredPosition = new Vector2(cx, cy);

            if (isCenter)
                BuildRandomHexCell(cell, hexSp, metalSp);
            else
            {
                hexSlotCenters.Add(new Vector2(cx, cy));
                deckBySlot.TryGetValue(slotIdx, out var deck);
                if (deck != null)
                {
                    hexDeckCells[deck.id] = cell;
                    hexPreviewIds.Add(deck.id);
                    hexPreviewSlots.Add(slotIdx);
                }
                BuildDeckHexCell(cell, hexSp, metalSp, deck, slotIdx);
                slotIdx++;
            }
        }

        // Bring the selected cell to the front AFTER every cell exists.
        if (!string.IsNullOrEmpty(selectedDeckId) &&
            hexDeckCells.TryGetValue(selectedDeckId, out var selCell) && selCell != null)
            selCell.SetAsLastSibling();

        // Hint text below the cluster
        var hint = Text_("Hint", parent,
            "click to lock in  ·  drag to reorder  ·  ? for a random deck",
            11, new Color(0x7F / 255f, 0x8C / 255f, 0xA0 / 255f, 1f),
            TextAnchor.MiddleCenter, monoFont);
        Stretch(hint.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f),
            new Vector2(0f, 10f), new Vector2(0f, 36f));
    }

    // Cell font sizes scale with the hexes so text keeps fitting as they shrink.
    private int HexFont(float baseSize) =>
        Mathf.Max(7, Mathf.RoundToInt(baseSize * hexFontScale));

    // Nearest slot (occupied OR empty) to a container-local position.
    private int NearestHexSlot(Vector2 localPos)
    {
        int best = 0; float bestD = float.MaxValue;
        for (int k = 0; k < hexSlotCenters.Count; k++)
        {
            float d = (hexSlotCenters[k] - localPos).sqrMagnitude;
            if (d < bestD) { bestD = d; best = k; }
        }
        return best;
    }

    // Live reorder preview while a hex is dragged. Empty target → the deck claims
    // it outright; occupied target → insertion shuffle among the occupied slots.
    private void PreviewHexReorder(string deckId, Vector2 localPos)
    {
        if (string.IsNullOrEmpty(deckId) || hexPreviewIds.Count == 0 || hexSlotCenters.Count == 0)
            return;
        int cur = hexPreviewIds.IndexOf(deckId);
        if (cur < 0) return;

        int target = NearestHexSlot(localPos);
        if (target == hexPreviewSlots[cur]) return;

        int occ = hexPreviewSlots.IndexOf(target);   // who's there now? (-1 = empty)
        if (occ < 0)
        {
            hexPreviewIds.RemoveAt(cur);
            hexPreviewSlots.RemoveAt(cur);
            int ins = 0;
            while (ins < hexPreviewSlots.Count && hexPreviewSlots[ins] < target) ins++;
            hexPreviewIds.Insert(ins, deckId);
            hexPreviewSlots.Insert(ins, target);
            return;
        }

        hexPreviewIds.RemoveAt(cur);
        hexPreviewIds.Insert(occ, deckId);

        for (int k = 0; k < hexPreviewIds.Count; k++)
        {
            string id = hexPreviewIds[k];
            if (id == deckId) continue;
            if (hexDeckCells.TryGetValue(id, out var cell) && cell != null)
                HexSlideTween.Slide(cell, hexSlotCenters[hexPreviewSlots[k]]);
        }
    }

    // Resolve a drag drop: commit the live-previewed slot assignment exactly as
    // shown (nearest-slot fallback if no preview ran), persist, re-render.
    private void HandleHexDrop(string deckId, Vector2 droppedLocalPos)
    {
        hexDraggingId = null;   // drop resolved — clicks live again

        if (string.IsNullOrEmpty(deckId) || hexSlotCenters.Count == 0)
        { Render(); return; }

        PreviewHexReorder(deckId, droppedLocalPos);

        if (hexPreviewIds.Contains(deckId))
        {
            var assignment = new List<KeyValuePair<string, int>>();
            for (int k = 0; k < hexPreviewIds.Count && k < hexPreviewSlots.Count; k++)
                assignment.Add(new KeyValuePair<string, int>(hexPreviewIds[k], hexPreviewSlots[k]));
            DeckStore.ApplySlots(assignment);
        }
        else
            DeckStore.Move(deckId, NearestHexSlot(droppedLocalPos));

        selectedDeckId = deckId;
        Render();
    }

    private void BuildRandomHexCell(RectTransform cell, Sprite hexSp, Sprite metalSp)
    {
        // Outer hex — the exact same brushed-metal sprite every other cell's rim
        // uses, just tinted a darker blue than Steel so this one still reads as
        // "metal" but is distinct at a glance. No outward-extending halo — at
        // the shrunken radius-3 size a +10px halo poked out past the neighbours
        // ("overextended area" around the centre), so the ring is flush now.
        var outer = Panel("Outer", cell, DeepBlueMetal);
        outer.GetComponent<Image>().sprite = metalSp;
        outer.GetComponent<Image>().type = Image.Type.Simple;
        outer.GetComponent<Image>().raycastTarget = false;
        Stretch(outer, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        // Inner hex — a deeper navy than the other cells' fill, to sit under the
        // darker rim tint.
        var inner = Panel("Inner", cell, new Color(0x0B / 255f, 0x1C / 255f, 0x2E / 255f, 1f));
        inner.GetComponent<Image>().sprite = hexSp;
        inner.GetComponent<Image>().type = Image.Type.Simple;
        inner.GetComponent<Image>().raycastTarget = false;
        Stretch(inner, Vector2.zero, Vector2.one, new Vector2(3f, 3f), new Vector2(-3f, -3f));

        // ? — a bronze medallion glyph, not a flat colour: a dark-bronze base
        // (with an engraved outline for definition) gets a warm gold sheen
        // layered on top, clipped to the glyph's own shape via Mask so only the
        // "?" itself catches the highlight — the same trick used for gradient
        // fills in legacy uGUI (a masked Graphic's rendered pixels become the
        // stencil for its children).
        var qMark = Text_("Q", cell, "?", HexFont(48), Bronze, TextAnchor.MiddleCenter, bronzeFont);
        qMark.fontStyle = FontStyle.Bold;
        Stretch(qMark.rectTransform, Vector2.zero, Vector2.one, new Vector2(0f, -4f), new Vector2(0f, -4f));
        var qOutline = qMark.gameObject.AddComponent<Outline>();
        qOutline.effectColor = new Color32(46, 26, 12, 235);
        qOutline.effectDistance = new Vector2(1.4f, -1.4f);
        qOutline.useGraphicAlpha = false;
        qMark.gameObject.AddComponent<Mask>().showMaskGraphic = true;

        var qSheen = Panel("QSheen", qMark.rectTransform, Color.white);
        var qSheenImg = qSheen.GetComponent<Image>();
        qSheenImg.sprite = GetVGradientSprite();          // opaque top → transparent bottom
        qSheenImg.type = Image.Type.Simple;
        qSheenImg.color = new Color(1f, 0.87f, 0.62f, 0.65f);
        qSheenImg.raycastTarget = false;
        Stretch(qSheen, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        var btn = cell.gameObject.AddComponent<Button>();
        btn.onClick.AddListener(() =>
        {
            if (hexDraggingId != null) return;   // ignore mid-drag
            var all = DeckStore.All();
            if (all.Count > 0)
            {
                var pick = all[UnityEngine.Random.Range(0, all.Count)].id;
                selectedDeckId = pick;
                if (!pickerActive) DeckStore.ActiveDeckId = pick;   // random also locks in (not while picking)
                Render();
            }
        });
    }

    private void BuildDeckHexCell(RectTransform cell, Sprite hexSp, Sprite metalSp, DeckData deck, int slotIdx)
    {
        bool isSelected = deck != null && deck.id == selectedDeckId;
        bool isFav      = deck != null && deck.favorite;
        bool isEmpty    = deck == null;

        // Rims are brushed metal (greyscale sprite × tint): steel normally, gold
        // for favourites. The SELECTED hex keeps the steel rim and gets the
        // play-mode gold-orange mist glow around it instead of a painted outline.
        Color ringColor = isEmpty ? SteelDim : (isFav ? Gold : Steel);
        const float pad = 3f;   // rim thickness (art inset)

        if (isSelected)
        {
            cell.SetAsLastSibling();
            AddHexSelectionGlow(cell);
        }

        // Ring
        var ring = Panel("Ring", cell, ringColor);
        ring.GetComponent<Image>().sprite = metalSp;
        ring.GetComponent<Image>().type = Image.Type.Simple;
        ring.GetComponent<Image>().raycastTarget = false;
        Stretch(ring, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        if (isEmpty)
        {
            var innerE = Panel("InnerE", cell, new Color(0x0B / 255f, 0x15 / 255f, 0x22 / 255f, 0.9f));
            innerE.GetComponent<Image>().sprite = hexSp;
            innerE.GetComponent<Image>().type = Image.Type.Simple;
            innerE.GetComponent<Image>().raycastTarget = false;
            Stretch(innerE, Vector2.zero, Vector2.one, new Vector2(3f, 3f), new Vector2(-3f, -3f));
            var plus = Text_("+", cell, "+", HexFont(22),
                new Color(Steel.r, Steel.g, Steel.b, 0.45f), TextAnchor.MiddleCenter, monoFont);
            plus.fontStyle = FontStyle.Bold;
            Stretch(plus.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var ebtn = cell.gameObject.AddComponent<Button>();
            int capturedSlot = slotIdx;   // the new deck takes the clicked cell's slot
            ebtn.onClick.AddListener(() =>
            {
                if (hexDraggingId != null) return;
                editing = new DeckData { id = DeckStore.NewId(), name = "New Deck", slot = capturedSlot };
                view = View.Editor; pickingLeader = false; ResetFilters(); Render();
            });
            return;
        }

        // Art masked to hex shape (inset by the rim thickness).
        var artMask = Panel("ArtMask", cell, new Color32(8, 16, 24, 255));
        var amImg = artMask.GetComponent<Image>();
        amImg.sprite = hexSp; amImg.type = Image.Type.Simple; amImg.raycastTarget = false;
        Stretch(artMask, Vector2.zero, Vector2.one, new Vector2(pad, pad), new Vector2(-pad, -pad));
        var maskComp = artMask.gameObject.AddComponent<Mask>();
        maskComp.showMaskGraphic = false;

        var lead = Card(deck.leaderId);
        // Cache-only lookup against the small pre-generated thumbnail tier
        // (_thumbCache, not the full-res _artCache - a hex cell never needs the
        // master). A cache MISS that's still `false` (never attempted) queues
        // an async fetch and shows a placeholder that RequestThumbArt() fades
        // the real art into once decoded; a cache HIT of null means the art was
        // already tried and truly doesn't exist, so the mono-initial placeholder
        // is permanent, not a loading state.
        bool haveEntry  = _thumbCache.TryGetValue(deck.leaderId, out var cachedArtSp);
        bool knownNoArt = haveEntry && cachedArtSp == null;

        if (!knownNoArt)
        {
            // Face-CENTRED framing: the hex shows a window whose centre aims at
            // the detected face (face-data.json, else the skin-tone heuristic) in
            // BOTH axes, clamped inside the card's illustration region so the hex
            // only ever contains art — no border, cost/power boxes, or text.
            const float SAFE_L = 0.07f, SAFE_R = 0.93f;   // art-safe horizontal bounds (clear of the card border)
            const float SAFE_T = 0.05f, SAFE_B = 0.60f;   // OPTCG illustration region (clear of the text box)
            const float BLEED  = 1.03f;

            float mW = cell.sizeDelta.x - 2f * pad, mH = cell.sizeDelta.y - 2f * pad;
            // Every OPTCG card art shares this aspect, so the framing math needs
            // no change once the real texture lands - only the sprite swaps in.
            float aspect    = (cachedArtSp != null && cachedArtSp.rect.height > 0f)
                ? cachedArtSp.rect.width / cachedArtSp.rect.height : 0.716f;
            float hexAspect = mH > 0f ? mW / mH : 1.1547f;

            // Window height (fraction of card shown); 0.44 keeps the whole head
            // in frame even when detection is imperfect. Width follows the hex.
            // Per-card zoom (face-data.json "z") tightens the window on
            // small/off-centre faces and on the focused half of duo leaders
            // (Luffy & Ace, Zoro & Sanji...), so the hex features ONE face
            // instead of splitting the frame between two half-cropped ones.
            float visH = 0.44f * FaceZoom(deck.leaderId);
            float visW = visH * hexAspect / Mathf.Max(aspect, 0.01f);
            if (visW > SAFE_R - SAFE_L) { visW = SAFE_R - SAFE_L; visH = visW * aspect / hexAspect; }

            Vector2 face = FaceCenter(deck.leaderId, lead != null ? lead.type : null);
            float cxFrac = Mathf.Clamp(face.x, SAFE_L + visW * 0.5f, SAFE_R - visW * 0.5f);
            float cyFrac = Mathf.Clamp(face.y + visH * 0.05f, SAFE_T + visH * 0.5f, SAFE_B - visH * 0.5f);

            float aw = (mW / visW) * BLEED;
            float ah = aw / aspect;

            // Loading placeholder must be OPAQUE and match artMask's own fill
            // (8,16,24) - artMask's own graphic is hidden (showMaskGraphic =
            // false, it's stencil-only), so a transparent Art child here would
            // let the steel Ring panel underneath show through as grey across
            // the whole hex interior instead of just its intended thin rim.
            var artImg = Panel("Art", artMask, cachedArtSp != null ? Color.white : new Color32(8, 16, 24, 255));
            var ai = artImg.GetComponent<Image>();
            ai.raycastTarget = false;
            artImg.anchorMin = artImg.anchorMax = new Vector2(0.5f, 0.5f);   // hex centre
            artImg.pivot = new Vector2(0.5f, 0.5f);
            artImg.sizeDelta = new Vector2(aw, ah);
            // Slide the card so the illustration window's centre sits at the hex centre.
            artImg.anchoredPosition = new Vector2(-(cxFrac - 0.5f) * aw, (cyFrac - 0.5f) * ah);

            if (cachedArtSp != null) { ai.sprite = cachedArtSp; ai.type = Image.Type.Simple; ai.preserveAspect = false; }
            else RequestThumbArt(ai, deck.leaderId);   // still loading - fades in once decoded
        }
        else
        {
            string init = (lead != null && !string.IsNullOrEmpty(lead.name))
                ? lead.name[0].ToString().ToUpper() : "?";
            var mono = Text_("Mono", artMask, init, HexFont(32),
                new Color(1f, 1f, 1f, 0.16f), TextAnchor.MiddleCenter);
            Stretch(mono.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        }

        // Favorite pip ★ (top-center, above art mask, so parented to cell)
        if (deck.favorite)
        {
            var star = Text_("Star", cell, "★", HexFont(13), Gold, TextAnchor.UpperCenter);
            star.rectTransform.anchorMin = new Vector2(0f, 1f);
            star.rectTransform.anchorMax = new Vector2(1f, 1f);
            star.rectTransform.pivot = new Vector2(0.5f, 1f);
            star.rectTransform.sizeDelta = new Vector2(0f, 18f);
            star.rectTransform.anchoredPosition = new Vector2(0f, 0f);
        }

        // Click → select AND lock this deck in as the active deck (no Play button).
        // Ignored mid-drag (the click fires before OnEndDrag on the same object).
        var btn = cell.gameObject.AddComponent<Button>();
        var capturedId = deck.id;
        btn.onClick.AddListener(() =>
        {
            if (hexDraggingId != null) return;
            selectedDeckId = capturedId;
            if (!pickerActive) DeckStore.ActiveDeckId = capturedId;   // picking doesn't touch the global active deck
            pendingClearAll = false;   // any deliberate selection cancels an armed clear-all
            Render();
        });

        // Drag → reorder the roster (click still works; drag needs to pass threshold).
        var drag = cell.gameObject.AddComponent<HexDragReorder>();
        drag.mgr = this; drag.deckId = deck.id; drag.cell = cell;
    }

    // ── Right column: grouped decklist ────────────────────────────────────────
    private void BuildSelectDecklist(RectTransform panel, DeckData deck)
    {
        const float PX = 26f, PT = 28f;

        // Header row
        var hdrRow = Row("DLHdr", panel, 8f, TextAnchor.MiddleLeft);
        Stretch(hdrRow, new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(PX, -PT - 24f), new Vector2(-PX, -PT));
        var dlHdr = Text_("Label", hdrRow, "DECKLIST", 16, Ink, TextAnchor.MiddleLeft);
        dlHdr.fontStyle = FontStyle.Bold;
        SetPref(dlHdr.rectTransform, new Vector2(120f, 24f));

        var (dlOk, dlTotal, _) = deck != null ? Validate(deck) : (false, 0, new System.Collections.Generic.List<string>());
        var totalT = Text_("Total", hdrRow, dlTotal + " / 50", 13,
            dlOk ? GoodGreen : RedAccent, TextAnchor.MiddleLeft, monoFont);
        totalT.fontStyle = FontStyle.Bold;
        SetPref(totalT.rectTransform, new Vector2(80f, 24f));

        // Summary line
        int chars = 0, events = 0, stages = 0;
        if (deck != null)
            foreach (var e in deck.cards)
            {
                var rec = Card(e.id); if (rec == null) continue;
                switch ((rec.type ?? "").ToLower())
                {
                    case "event": events += e.count; break;
                    case "stage": stages += e.count; break;
                    default:      chars  += e.count; break;
                }
            }
        var sumT = Text_("Sum", panel,
            $"{chars} characters  ·  {events} events  ·  {stages} stages",
            10, Muted, TextAnchor.UpperLeft, monoFont);
        Stretch(sumT.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(PX, -PT - 52f), new Vector2(-PX, -PT - 28f));

        // Divider
        var div = Panel("Div", panel, MenuB);
        Stretch(div, new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(PX, -PT - 58f), new Vector2(-PX, -PT - 56f));
        div.GetComponent<Image>().raycastTarget = false;

        // Scroll area
        var listArea = Panel("ListArea", panel, new Color(0, 0, 0, 0));
        Stretch(listArea, Vector2.zero, new Vector2(1f, 1f),
            new Vector2(PX - 6f, 12f), new Vector2(-6f, -PT - 62f));
        var listContent = MakeScroll(listArea);
        var vlg = listContent.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 5f; vlg.childControlWidth = true; vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        vlg.padding = new RectOffset(6, 6, 2, 6);
        var fit = listContent.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        if (deck == null || deck.cards.Count == 0)
        {
            var empty = Text_("Empty", listContent,
                "This deck has no cards yet.", 12, Muted, TextAnchor.MiddleCenter);
            empty.horizontalOverflow = HorizontalWrapMode.Wrap;
            SetPref(empty.rectTransform, new Vector2(0f, 44f));
            return;
        }

        var entries = deck.cards
            .Select(e => new { e.id, e.count, rec = Card(e.id) })
            .Where(x => x.rec != null).ToList();

        foreach (var grp in new[] { "character", "event", "stage" })
        {
            var rows = entries.Where(x => (x.rec.type ?? "").ToLower() == grp)
                              .OrderBy(x => x.rec.cost).ThenBy(x => x.rec.name).ToList();
            if (rows.Count == 0) continue;

            int sub = rows.Sum(r2 => r2.count);
            var head = Text_("H_" + grp, listContent,
                grp.ToUpper() + "  (" + sub + ")", 9, Accent2, TextAnchor.LowerLeft, monoFont);
            head.fontStyle = FontStyle.Bold;
            SetPref(head.rectTransform, new Vector2(0f, 20f));

            foreach (var r in rows) BuildSelectDeckRow(listContent, r.rec, r.count);
        }
    }

    private void BuildSelectDeckRow(RectTransform parent, CardRec rec, int count)
    {
        var row = Panel(rec.id + " SR", parent, RowBg);
        SetPref(row, new Vector2(0f, 34f));
        Round(row);
        // Hovering a deck row pops the same right-side preview (same as editor BuildDeckRow).
        var rowHov = row.gameObject.AddComponent<HoverPreview>();
        rowHov.mgr = this; rowHov.cardId = rec.id;

        // Art bleed (same pattern as editor BuildDeckRow, minus ± controls) -
        // small pre-generated thumbnail tier, this never needs the master.
        bool rowKnownNoArt = _thumbCache.TryGetValue(rec.id, out var rowCachedArt) && rowCachedArt == null;
        if (!rowKnownNoArt)
        {
            var clip = Panel("ArtClip", row, Color.white);
            clip.anchorMin = new Vector2(1f, 0f); clip.anchorMax = new Vector2(1f, 1f);
            clip.pivot = new Vector2(1f, 0.5f);
            clip.sizeDelta = new Vector2(180f, 0f);
            clip.anchoredPosition = Vector2.zero;
            var clipImg = clip.GetComponent<Image>();
            clipImg.sprite = RoundSprite(); clipImg.type = Image.Type.Sliced; clipImg.raycastTarget = false;
            var clipMask = clip.gameObject.AddComponent<Mask>();
            clipMask.showMaskGraphic = false;

            const float aspect = 0.716f, rowH = 34f, zoom = 1.5f;
            float w = 180f * zoom, h = w / aspect;
            var artTint = new Color(1f, 1f, 1f, 0.5f);
            // Same reasoning as the hex cells: clip's own graphic is stencil-only
            // (showMaskGraphic = false), so the loading placeholder must be an
            // opaque colour rather than transparent, or whatever sits behind the
            // row could show through unexpectedly instead of a clean fill.
            var art = Panel("Art", clip, rowCachedArt != null ? artTint : RowBg);
            var artImg = art.GetComponent<Image>();
            artImg.preserveAspect = false;
            artImg.raycastTarget = false;
            art.anchorMin = art.anchorMax = new Vector2(0.5f, 1f);
            art.pivot = new Vector2(0.5f, 1f);
            art.sizeDelta = new Vector2(w, h);
            float band = FaceBand(rec.id, rec.type);
            art.anchoredPosition = new Vector2(0f, band * h - rowH / 2f);

            if (rowCachedArt != null) { artImg.sprite = rowCachedArt; artImg.type = Image.Type.Simple; }
            else RequestThumbArt(artImg, rec.id, artTint);

            EdgeFade(clip, GetHGradientSprite(),  1f,  1f, Vector2.zero,           new Vector2(0.55f, 1f));
            EdgeFade(clip, GetVGradientSprite(),  1f,  1f, new Vector2(0f, 0.74f), Vector2.one);
            EdgeFade(clip, GetVGradientSprite(),  1f, -1f, Vector2.zero,           new Vector2(1f, 0.26f));
        }

        AddBorder(row, MenuB, 0.8f);

        // Cost circle
        var cost = Panel("Cost", row, new Color32(20, 36, 50, 255));
        cost.anchorMin = cost.anchorMax = new Vector2(0f, 0.5f);
        cost.pivot = new Vector2(0f, 0.5f);
        cost.sizeDelta = new Vector2(25f, 25f);
        cost.anchoredPosition = new Vector2(6f, 0f);
        RoundCircle(cost);
        var ct = Text_("c", cost, rec.cost.ToString(), 11, Accent2, TextAnchor.MiddleCenter, monoFont);
        ct.fontStyle = FontStyle.Bold;
        Stretch(ct.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        // Name
        var nm = Text_("n", row, rec.name, 12, Ink, TextAnchor.MiddleLeft);
        Stretch(nm.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f),
            new Vector2(37f, 0f), new Vector2(-52f, 0f));

        // ×count
        var cc = Text_("cc", row, "×" + count, 12, Accent, TextAnchor.MiddleCenter, monoFont);
        cc.fontStyle = FontStyle.Bold;
        cc.rectTransform.anchorMin = cc.rectTransform.anchorMax = new Vector2(1f, 0.5f);
        cc.rectTransform.pivot = new Vector2(1f, 0.5f);
        cc.rectTransform.sizeDelta = new Vector2(46f, 24f);
        cc.rectTransform.anchoredPosition = new Vector2(-6f, 0f);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // EDITOR VIEW
    // ══════════════════════════════════════════════════════════════════════════
    private void RenderEditor()
    {
        TopBar("DECK BUILDER", "edit cards, then save", () => { view = View.Select; Render(); });

        // Save button (top-right)
        var saveBtn = Panel("Save", root, Accent);
        saveBtn.anchorMin = saveBtn.anchorMax = new Vector2(1f, 1f);
        saveBtn.pivot = new Vector2(1f, 1f);
        saveBtn.sizeDelta = new Vector2(150f, 38f);
        saveBtn.anchoredPosition = new Vector2(-24f, -13f);
        Round(saveBtn);
        saveButtonBg = saveBtn.GetComponent<Image>();
        saveButtonText = Text_("t", saveBtn, "SAVE DECK", 12, BadgeInk, TextAnchor.MiddleCenter, monoFont);
        saveButtonText.fontStyle = FontStyle.Bold;
        Stretch(saveButtonText.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var sb = saveBtn.gameObject.AddComponent<Button>();
        sb.onClick.AddListener(SaveCurrent);

        // Clear All button (top-right, left of Save) — empties the main deck.
        var clearBtn = Panel("Clear All", root, new Color32(46, 26, 30, 235));
        clearBtn.anchorMin = clearBtn.anchorMax = new Vector2(1f, 1f);
        clearBtn.pivot = new Vector2(1f, 1f);
        clearBtn.sizeDelta = new Vector2(126f, 38f);
        clearBtn.anchoredPosition = new Vector2(-182f, -13f);
        Round(clearBtn); AddBorder(clearBtn, RedAccent, 1.1f);
        var clt = Text_("t", clearBtn, "CLEAR ALL", 12, RedAccent, TextAnchor.MiddleCenter, monoFont);
        clt.fontStyle = FontStyle.Bold;
        Stretch(clt.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var cb = clearBtn.gameObject.AddComponent<Button>();
        cb.onClick.AddListener(ClearAll);

        // Export Deck button (top-right, left of Clear All) — copies the deck
        // list to the OS clipboard in OPTCGSim's "NxCODE" format, the format
        // shared by OPTCGSim, EGMan, OnePieceTopDecks and most community tools.
        var exportBtn = Panel("Export Deck", root, new Color32(34, 58, 78, 235));
        exportBtn.anchorMin = exportBtn.anchorMax = new Vector2(1f, 1f);
        exportBtn.pivot = new Vector2(1f, 1f);
        exportBtn.sizeDelta = new Vector2(140f, 38f);
        exportBtn.anchoredPosition = new Vector2(-316f, -13f);
        Round(exportBtn); AddBorder(exportBtn, Accent2, 1.1f);
        var ext = Text_("t", exportBtn, "EXPORT DECK", 12, Accent2, TextAnchor.MiddleCenter, monoFont);
        ext.fontStyle = FontStyle.Bold;
        Stretch(ext.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var eb = exportBtn.gameObject.AddComponent<Button>();
        eb.onClick.AddListener(ExportCurrent);

        // Import Deck button (top-right, left of Export) — opens a paste modal
        // that accepts any of the common community deck-list text formats.
        var importBtn = Panel("Import Deck", root, new Color32(34, 58, 78, 235));
        importBtn.anchorMin = importBtn.anchorMax = new Vector2(1f, 1f);
        importBtn.pivot = new Vector2(1f, 1f);
        importBtn.sizeDelta = new Vector2(140f, 38f);
        importBtn.anchoredPosition = new Vector2(-464f, -13f);
        Round(importBtn); AddBorder(importBtn, Accent2, 1.1f);
        var imt = Text_("t", importBtn, "IMPORT DECK", 12, Accent2, TextAnchor.MiddleCenter, monoFont);
        imt.fontStyle = FontStyle.Bold;
        Stretch(imt.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var ib = importBtn.gameObject.AddComponent<Button>();
        ib.onClick.AddListener(OpenImportModal);

        // Body: 3 columns — left (leader + stats), centre (card pool), right (main deck).
        var body = Panel("Body", root, new Color(0, 0, 0, 0));
        Stretch(body, Vector2.zero, Vector2.one, new Vector2(24f, 20f), new Vector2(-24f, -76f));

        const float LeftW = 300f, RightW = 320f, Gap = 18f;

        // LEFT: leader card + deck-composition graphs.
        var leftPanel = Panel("Left Panel", body, LogBgDark);
        Stretch(leftPanel, Vector2.zero, new Vector2(0f, 1f), Vector2.zero, new Vector2(LeftW, 0f));
        RoundBig(leftPanel); AddBorder(leftPanel, MenuB, 1f);
        BuildLeftPanel(leftPanel);

        // CENTRE: searchable card pool.
        var poolPanel = Panel("Pool Panel", body, LogBgDark);
        Stretch(poolPanel, Vector2.zero, Vector2.one,
            new Vector2(LeftW + Gap, 0f), new Vector2(-(RightW + Gap), 0f));
        RoundBig(poolPanel); AddBorder(poolPanel, MenuB, 1f);
        BuildPoolPanel(poolPanel);

        // RIGHT: the main deck being built.
        var deckPanel = Panel("Deck Panel", body, LogBgDark);
        Stretch(deckPanel, new Vector2(1f, 0f), Vector2.one, new Vector2(-RightW, 0f), Vector2.zero);
        RoundBig(deckPanel); AddBorder(deckPanel, MenuB, 1f);
        BuildDeckPanel(deckPanel);
    }

    // ── Left column: leader card slot + live deck-composition graphs ─────────────
    private void BuildLeftPanel(RectTransform panel)
    {
        var hdr = Text_("LeaderHdr", panel, "LEADER", 10, Accent2, TextAnchor.LowerLeft, monoFont);
        hdr.fontStyle = FontStyle.Bold;
        Stretch(hdr.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(16f, -30f), new Vector2(-16f, -12f));

        // leader slot (full card-style preview, like the in-game selected-card dock)
        leaderSlot = Panel("Leader Slot", panel, new Color32(8, 16, 26, 255));
        Stretch(leaderSlot, new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(16f, -454f), new Vector2(-16f, -34f));
        Round(leaderSlot);
        BuildLeaderSlot();

        // stats header
        var shdr = Text_("StatsHdr", panel, "DECK STATS", 10, Accent2, TextAnchor.LowerLeft, monoFont);
        shdr.fontStyle = FontStyle.Bold;
        Stretch(shdr.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(16f, -478f), new Vector2(-16f, -460f));

        // stats container (rebuilt by RefreshStats)
        statsRoot = Panel("Stats", panel, new Color(0, 0, 0, 0));
        Stretch(statsRoot, Vector2.zero, new Vector2(1f, 1f), new Vector2(16f, 12f), new Vector2(-16f, -482f));
        statsRoot.GetComponent<Image>().raycastTarget = false;
        RefreshStats();
    }

    // Clears every card from the main deck (leader is left untouched).
    private void ClearAll()
    {
        if (editing == null) return;
        editing.cards.Clear();
        RefreshDeckList();
        RefreshValidity();
        RefreshStats();
        RefreshBadges();
        Flash("Deck cleared");
    }

    // ── Right: the main deck being built ────────────────────────────────────────
    private void BuildDeckPanel(RectTransform panel)
    {
        // name field
        var nameField = MakeInput(panel, "Deck name", editing.name, s => { editing.name = s; }, null);
        Stretch(nameField, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(16f, -52f), new Vector2(-16f, -16f));

        // count + validity summary
        countText = Text_("Count", panel, "", 13, Ink, TextAnchor.MiddleRight, monoFont);
        countText.fontStyle = FontStyle.Bold;
        Stretch(countText.rectTransform, new Vector2(0.5f, 1f), new Vector2(1f, 1f),
            new Vector2(0f, -82f), new Vector2(-16f, -58f));

        validityText = Text_("Validity", panel, "", 11, Muted, TextAnchor.UpperLeft, monoFont);
        validityText.horizontalOverflow = HorizontalWrapMode.Wrap;
        validityText.verticalOverflow = VerticalWrapMode.Truncate;
        Stretch(validityText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(16f, -140f), new Vector2(-16f, -86f));

        // section header
        var hdr = Text_("CardsHdr", panel, "MAIN DECK", 10, Muted, TextAnchor.LowerLeft, monoFont);
        hdr.fontStyle = FontStyle.Bold;
        Stretch(hdr.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(16f, -166f), new Vector2(-16f, -148f));

        // scroll list
        var listArea = Panel("List Area", panel, new Color(0, 0, 0, 0));
        Stretch(listArea, Vector2.zero, new Vector2(1f, 1f), new Vector2(10f, 12f), new Vector2(-8f, -172f));
        deckListContent = MakeScroll(listArea);
        var vlg = deckListContent.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 5f; vlg.childControlWidth = true; vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        vlg.padding = new RectOffset(6, 6, 2, 6);
        var fit = deckListContent.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        RefreshDeckList();
        RefreshValidity();
    }

    // Full card-style leader preview — mirrors the solo-vs-self docked card:
    // upright rounded card art, name, TYPE/COLOR/LIFE/POWER chips, effect box.
    // The whole slot stays clickable (tap to change the leader) and hovering it
    // pops the same big right-side preview as every other card.
    private void BuildLeaderSlot()
    {
        for (int i = leaderSlot.childCount - 1; i >= 0; i--) Destroy(leaderSlot.GetChild(i).gameObject);
        AddBorder(leaderSlot, pickingLeader ? Accent : MenuB, pickingLeader ? 1.6f : 1f);

        var lead = Card(editing.leaderId);
        Sprite cachedArt = null;
        bool knownNoArt;
        if (string.IsNullOrEmpty(editing.leaderId)) knownNoArt = true;
        else
        {
            bool haveEntry = _artCache.TryGetValue(editing.leaderId, out cachedArt);
            knownNoArt = haveEntry && cachedArt == null;
        }

        // Section label, top-left.
        var lbl = Text_("LeadLbl", leaderSlot, "LEADER", 9, Accent, TextAnchor.UpperLeft, monoFont);
        lbl.fontStyle = FontStyle.Bold;
        Stretch(lbl.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(14f, -22f), new Vector2(-14f, -6f));

        // Card art region, centred near the top.
        var cardRegion = Panel("Card Region", leaderSlot, new Color(0, 0, 0, 0));
        cardRegion.anchorMin = cardRegion.anchorMax = new Vector2(0.5f, 1f);
        cardRegion.pivot = new Vector2(0.5f, 1f);
        cardRegion.sizeDelta = new Vector2(150f, 210f);
        cardRegion.anchoredPosition = new Vector2(0f, -28f);
        cardRegion.GetComponent<Image>().raycastTarget = false;
        if (!knownNoArt)
        {
            var pimg = MakeRoundedCard(cardRegion, false, out _);
            if (cachedArt != null) pimg.sprite = cachedArt;
            else { pimg.color = new Color32(10, 20, 32, 255); RequestArt(pimg, editing.leaderId); }
        }
        else
        {
            var ph = Panel("Empty", cardRegion, new Color(1f, 1f, 1f, 0.02f));
            Stretch(ph, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Round(ph); AddBorder(ph, MenuB, 1f);
            ph.GetComponent<Image>().raycastTarget = false;
        }

        // Name.
        var nm = Text_("LeadName", leaderSlot, lead != null ? lead.name : "Tap to choose a leader",
            15, lead != null ? Ink : Muted, TextAnchor.UpperCenter);
        nm.fontStyle = FontStyle.Bold;
        nm.horizontalOverflow = HorizontalWrapMode.Wrap;
        Stretch(nm.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(12f, -286f), new Vector2(-12f, -246f));

        if (lead != null)
        {
            // Chip row (TYPE / COLOR / LIFE / POWER), centred.
            var chips = Row("LeadChips", leaderSlot, 5f, TextAnchor.MiddleCenter);
            chips.anchorMin = new Vector2(0f, 1f); chips.anchorMax = new Vector2(1f, 1f);
            chips.pivot = new Vector2(0.5f, 1f);
            chips.sizeDelta = new Vector2(0f, 20f);
            chips.anchoredPosition = new Vector2(0f, -290f);
            AddPreviewChip(chips, "LEADER", true);
            if (!string.IsNullOrEmpty(lead.color)) AddPreviewChip(chips, lead.color.ToUpper(), false);
            AddPreviewChip(chips, "LIFE " + lead.life, false);
            AddPreviewChip(chips, "POWER " + lead.power, false);
            AddNicheRuleChips(chips, editing.leaderId);

            // Effect box.
            var effBox = Panel("LeadEffect", leaderSlot, LogBgDark);
            Stretch(effBox, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(12f, -398f), new Vector2(-12f, -318f));
            RoundBig(effBox); AddBorder(effBox, MenuB, 1f);
            effBox.GetComponent<Image>().raycastTarget = false;
            var eff = Text_("LeadEffectText", effBox,
                !string.IsNullOrEmpty(lead.effect) ? lead.effect : "No effect text.",
                10, string.IsNullOrEmpty(lead.effect) ? Muted : Ink, TextAnchor.UpperLeft);
            eff.horizontalOverflow = HorizontalWrapMode.Wrap;
            eff.verticalOverflow   = VerticalWrapMode.Truncate;
            Stretch(eff.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(8f, 6f), new Vector2(-8f, -6f));

            var change = Text_("Change", leaderSlot, "tap to change ▸", 9, Accent, TextAnchor.LowerCenter, monoFont);
            Stretch(change.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(12f, 2f), new Vector2(-12f, 18f));
        }

        var btn = leaderSlot.gameObject.AddComponent<Button>();
        btn.onClick.AddListener(() =>
        {
            pickingLeader = true; filterType = "leader"; filterText = "";
            // when picking a leader, the pool shows leaders matching nothing yet
            Render();
        });
        // Hovering the leader also pops the big right-side preview.
        var hov = leaderSlot.gameObject.AddComponent<HoverPreview>();
        hov.mgr = this; hov.cardId = editing.leaderId;
    }

    // Small pill used by the leader preview. Primary chips read as the accent
    // colour; secondary chips are faint with a hairline border. All chips are
    // non-raycast so they never block the leader slot's tap-to-change button.
    private void AddPreviewChip(RectTransform parent, string label, bool primary)
    {
        if (string.IsNullOrEmpty(label)) return;
        var chip = Panel(label + " Chip", parent, primary ? Accent : new Color(1f, 1f, 1f, 0.05f));
        float chipW = 12f + label.Length * 6.6f;
        chip.sizeDelta = new Vector2(chipW, 18f);
        SetPref(chip, new Vector2(chipW, 18f));
        Round(chip);
        chip.GetComponent<Image>().raycastTarget = false;
        if (!primary) AddBorder(chip, ZoneBorder, 1f);
        var t = Text_("t", chip, label, 9, primary ? BadgeInk : Ink, TextAnchor.MiddleCenter, monoFont);
        t.fontStyle = FontStyle.Bold;
        Stretch(t.rectTransform, Vector2.zero, Vector2.one, new Vector2(4, 0), new Vector2(-4, 0));
    }

    // Gold-tinted chip flagging a niche rule override on this leader (e.g. a
    // non-default DON!! deck size) — same "special" gold used for favourites
    // and the cost-curve average marker elsewhere in the builder.
    private void AddRuleChip(RectTransform parent, string label)
    {
        if (string.IsNullOrEmpty(label)) return;
        var chip = Panel(label + " Chip", parent, new Color(Gold.r, Gold.g, Gold.b, 0.18f));
        float chipW = 12f + label.Length * 6.6f;
        chip.sizeDelta = new Vector2(chipW, 18f);
        SetPref(chip, new Vector2(chipW, 18f));
        Round(chip); AddBorder(chip, Gold, 1f);
        chip.GetComponent<Image>().raycastTarget = false;
        var t = Text_("t", chip, label, 9, Gold, TextAnchor.MiddleCenter, monoFont);
        t.fontStyle = FontStyle.Bold;
        Stretch(t.rectTransform, Vector2.zero, Vector2.one, new Vector2(4, 0), new Vector2(-4, 0));
    }

    // A chip tinted with a One Piece colour swatch (RED / GREEN / BLUE / …).
    private void AddColorChip(RectTransform parent, string colorName)
    {
        if (string.IsNullOrEmpty(colorName)) return;
        var sc = ColorSwatch.TryGetValue(colorName, out var c) ? c : Muted;
        string lab = colorName.ToUpper();
        var chip = Panel(colorName + " Chip", parent, new Color(sc.r, sc.g, sc.b, 0.9f));
        float chipW = 12f + lab.Length * 6.6f;
        chip.sizeDelta = new Vector2(chipW, 18f);
        SetPref(chip, new Vector2(chipW, 18f));
        Round(chip);
        chip.GetComponent<Image>().raycastTarget = false;
        // Dark text on light swatches (yellow/green), light text on dark ones.
        float lum = 0.299f * sc.r + 0.587f * sc.g + 0.114f * sc.b;
        var t = Text_("t", chip, lab, 9, lum > 0.6f ? BadgeInk : Ink, TextAnchor.MiddleCenter, monoFont);
        t.fontStyle = FontStyle.Bold;
        Stretch(t.rectTransform, Vector2.zero, Vector2.one, new Vector2(4, 0), new Vector2(-4, 0));
    }

    private void RefreshDeckList()
    {
        if (deckListContent == null) return;
        for (int i = deckListContent.childCount - 1; i >= 0; i--) Destroy(deckListContent.GetChild(i).gameObject);

        // group entries by type
        var entries = editing.cards
            .Select(e => new { e.id, e.count, rec = Card(e.id) })
            .Where(x => x.rec != null)
            .ToList();

        if (entries.Count == 0)
        {
            var empty = Text_("Empty", deckListContent,
                "No cards yet — click cards on the right to add them.", 11, Muted, TextAnchor.UpperLeft);
            empty.horizontalOverflow = HorizontalWrapMode.Wrap;
            SetPref(empty.rectTransform, new Vector2(0f, 44f));
            return;
        }

        foreach (var grp in new[] { "character", "event", "stage" })
        {
            var rows = entries.Where(x => (x.rec.type ?? "").ToLower() == grp)
                              .OrderBy(x => x.rec.cost).ThenBy(x => x.rec.name).ToList();
            if (rows.Count == 0) continue;

            int sub = rows.Sum(r => r.count);
            var head = Text_("H_" + grp, deckListContent,
                grp.ToUpper() + "  (" + sub + ")", 9, Accent2, TextAnchor.LowerLeft, monoFont);
            head.fontStyle = FontStyle.Bold;
            SetPref(head.rectTransform, new Vector2(0f, 20f));

            foreach (var r in rows) BuildDeckRow(r.rec, r.count);
        }
    }

    private void BuildDeckRow(CardRec rec, int count)
    {
        var row = Panel(rec.id + " Row", deckListContent, RowBg);
        SetPref(row, new Vector2(0f, 34f));
        Round(row);
        // Hovering a deck row pops the same right-side preview.
        var rowHov = row.gameObject.AddComponent<HoverPreview>();
        rowHov.mgr = this; rowHov.cardId = rec.id;

        // Hearthstone-style flavour: the card art bleeds in from the right, behind
        // the controls, and fades into the row so the name/cost stay readable.
        // Small pre-generated thumbnail tier - this never needs the master.
        bool rowKnownNoArt = _thumbCache.TryGetValue(rec.id, out var rowCachedArt) && rowCachedArt == null;
        if (!rowKnownNoArt)
        {
            // Clip fills the full row and is masked to the row's rounded shape, so
            // the art reaches every edge without poking past the rounded corners.
            var clip = Panel("Art Clip", row, Color.white);
            clip.anchorMin = new Vector2(1f, 0f); clip.anchorMax = new Vector2(1f, 1f);
            clip.pivot = new Vector2(1f, 0.5f);
            clip.sizeDelta = new Vector2(232f, 0f);
            clip.anchoredPosition = Vector2.zero;
            var clipImg = clip.GetComponent<Image>();
            clipImg.sprite = RoundSprite(); clipImg.type = Image.Type.Sliced; clipImg.raycastTarget = false;
            var mask = clip.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;                    // round-clip only, don't draw the box

            // Zoom in so the card's own frame/border is cropped out — only the
            // inner illustration fills the strip.
            const float aspect = 0.716f, rowH = 34f, zoom = 1.5f;
            float w = 232f * zoom, h = w / aspect;
            var artTint = new Color(1f, 1f, 1f, 0.5f);
            // Same reasoning as the hex cells: clip's own graphic is stencil-only
            // (showMaskGraphic = false), so the loading placeholder must be an
            // opaque colour rather than transparent, or whatever sits behind the
            // row could show through unexpectedly instead of a clean fill.
            var art = Panel("Art", clip, rowCachedArt != null ? artTint : RowBg);
            var aim = art.GetComponent<Image>();
            aim.preserveAspect = false;
            aim.raycastTarget = false;
            art.anchorMin = art.anchorMax = new Vector2(0.5f, 1f);   // top-centre of clip
            art.pivot = new Vector2(0.5f, 1f);
            art.sizeDelta = new Vector2(w, h);
            // Face-aware framing: aim the slice at the detected eye line for
            // characters/leaders (cached per card); centre for events/stages.
            float band = FaceBand(rec.id, rec.type);
            art.anchoredPosition = new Vector2(0f, band * h - rowH / 2f);

            if (rowCachedArt != null) { aim.sprite = rowCachedArt; aim.type = Image.Type.Simple; }
            else RequestThumbArt(aim, rec.id, artTint);

            // Feather only the left (for text) and a touch of top/bottom so the
            // horizontal mask cut blends; the art now fills the strip edge-to-edge.
            EdgeFade(clip, GetHGradientSprite(),  1f,  1f, Vector2.zero,           new Vector2(0.55f, 1f));   // left
            EdgeFade(clip, GetVGradientSprite(),  1f,  1f, new Vector2(0f, 0.74f), Vector2.one);              // top
            EdgeFade(clip, GetVGradientSprite(),  1f, -1f, Vector2.zero,           new Vector2(1f, 0.26f));   // bottom
        }

        // Cyan rim on top of the art so the bubble keeps its framed edge.
        AddBorder(row, MenuB, 0.8f);

        // cost chip
        var cost = Panel("Cost", row, new Color32(20, 36, 50, 255));
        cost.anchorMin = cost.anchorMax = new Vector2(0f, 0.5f);
        cost.pivot = new Vector2(0f, 0.5f);
        cost.sizeDelta = new Vector2(24f, 24f);
        cost.anchoredPosition = new Vector2(6f, 0f);
        RoundCircle(cost);
        var ct = Text_("c", cost, rec.cost.ToString(), 11, Accent2, TextAnchor.MiddleCenter, monoFont);
        ct.fontStyle = FontStyle.Bold;
        Stretch(ct.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        var nm = Text_("n", row, rec.name, 11, Ink, TextAnchor.MiddleLeft);
        Stretch(nm.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f),
            new Vector2(36f, 0f), new Vector2(-110f, 0f));

        // − count +
        StepButton(row, "−", -64f, () => { AddCard(rec.id, -1); });
        var cc = Text_("cc", row, "×" + count, 12, Accent, TextAnchor.MiddleCenter, monoFont);
        cc.fontStyle = FontStyle.Bold;
        cc.rectTransform.anchorMin = cc.rectTransform.anchorMax = new Vector2(1f, 0.5f);
        cc.rectTransform.pivot = new Vector2(1f, 0.5f);
        cc.rectTransform.sizeDelta = new Vector2(40f, 24f);
        cc.rectTransform.anchoredPosition = new Vector2(-34f, 0f);
        StepButton(row, "+", -6f, () => { AddCard(rec.id, +1); });
    }

    // One directional fade scrim (row colour) over a sub-region of the art clip.
    // sx/sy of -1 mirror the gradient so the opaque end sits on the opposite edge.
    private void EdgeFade(RectTransform clip, Sprite grad, float sx, float sy, Vector2 aMin, Vector2 aMax)
    {
        var f = Panel("Fade", clip, RowBg);
        var im = f.GetComponent<Image>();
        im.sprite = grad; im.type = Image.Type.Simple; im.raycastTarget = false;
        Stretch(f, aMin, aMax, Vector2.zero, Vector2.zero);
        f.localScale = new Vector3(sx, sy, 1f);
    }

    private void StepButton(RectTransform row, string label, float x, UnityEngine.Events.UnityAction action)
    {
        var b = Panel("Step " + label, row, new Color32(26, 44, 60, 255));
        b.anchorMin = b.anchorMax = new Vector2(1f, 0.5f);
        b.pivot = new Vector2(1f, 0.5f);
        b.sizeDelta = new Vector2(24f, 24f);
        b.anchoredPosition = new Vector2(x, 0f);
        Round(b); AddBorder(b, MenuB, 1f);
        var t = Text_("t", b, label, 15, Ink, TextAnchor.MiddleCenter);
        t.fontStyle = FontStyle.Bold;
        Stretch(t.rectTransform, Vector2.zero, Vector2.one, new Vector2(0, -1), Vector2.zero);
        var btn = b.gameObject.AddComponent<Button>();
        btn.onClick.AddListener(action);
    }

    // ── Right: searchable card pool ─────────────────────────────────────────────
    private void BuildPoolPanel(RectTransform panel)
    {
        // search field
        var search = MakeInput(panel, "Search by name or text…", filterText,
            s => { filterText = s; RefreshGrid(); }, null);
        Stretch(search, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(16f, -52f), new Vector2(-150f, -16f));

        resultCountText = Text_("Results", panel, "", 10, Muted, TextAnchor.MiddleRight, monoFont);
        Stretch(resultCountText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-140f, -52f), new Vector2(-16f, -16f));

        // colour filter chips
        var colorRow = Row("Colors", panel, 6, TextAnchor.MiddleLeft);
        Stretch(colorRow, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(16f, -86f), new Vector2(-16f, -60f));
        ChipToggle(colorRow, "ALL", "", filterColor == "", c => { filterColor = ""; RefreshGrid(); }, Muted);
        foreach (var col in new[] { "Red", "Green", "Blue", "Purple", "Black", "Yellow" })
        {
            var cc = col;
            ChipToggle(colorRow, col.ToUpper(), col, filterColor == col,
                _ => { filterColor = filterColor == cc ? "" : cc; RefreshGrid(); },
                ColorSwatch[col]);
        }

        // type + cost chips
        var typeRow = Row("Types", panel, 6, TextAnchor.MiddleLeft);
        Stretch(typeRow, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(16f, -116f), new Vector2(-16f, -90f));
        foreach (var (lab, val) in new[] { ("ALL", ""), ("CHAR", "character"), ("EVENT", "event"), ("STAGE", "stage") })
        {
            var v = val;
            ChipToggle(typeRow, lab, val, filterType == val && !pickingLeader,
                _ => { if (!pickingLeader) { filterType = v; RefreshGrid(); } }, Accent2);
        }
        // LEADER type: browse leaders directly in the pool and click one to set it.
        ChipToggle(typeRow, "LEADER", "leader", pickingLeader, _ =>
        {
            pickingLeader = !pickingLeader;
            filterType = pickingLeader ? "leader" : "";
            filterText = "";
            Render();   // rebuild so the pool + chip states reflect leader-pick mode
        }, Accent2, 60f);
        // cost chips
        var costRow = Row("Costs", panel, 5, TextAnchor.MiddleLeft);
        Stretch(costRow, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(16f, -146f), new Vector2(-16f, -120f));
        ChipToggle(costRow, "COST·ALL", "", filterCost == -1, _ => { filterCost = -1; RefreshGrid(); }, Muted);
        for (int c = 0; c <= 10; c++)
        {
            int cv = c;
            ChipToggle(costRow, c == 10 ? "10+" : c.ToString(), c.ToString(), filterCost == cv,
                _ => { filterCost = filterCost == cv ? -1 : cv; RefreshGrid(); }, Accent2, 34f);
        }

        // colour-lock + archetype-lock toggles (only show cards matching the leader's
        // colours / sharing a feature tag with the leader)
        var lockChip = Row("Lock", panel, 6, TextAnchor.MiddleLeft);
        Stretch(lockChip, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(16f, -176f), new Vector2(-16f, -150f));
        ChipToggle(lockChip, colorLock ? "◢ MATCH LEADER COLOURS: ON" : "◢ MATCH LEADER COLOURS: OFF", "",
            colorLock, _ => { colorLock = !colorLock; RefreshGrid(); }, colorLock ? Accent : Muted, 280f);
        ChipToggle(lockChip, archetypeLock ? "◢ MATCH LEADER ARCHETYPE: ON" : "◢ MATCH LEADER ARCHETYPE: OFF", "",
            archetypeLock, _ => { archetypeLock = !archetypeLock; RefreshGrid(); }, archetypeLock ? Accent : Muted, 300f);

        if (pickingLeader)
        {
            var hint = Text_("Hint", lockChip, "  ◂ choosing a LEADER — click one to set it", 10, Accent, TextAnchor.MiddleLeft, monoFont);
            SetPref(hint.rectTransform, new Vector2(340f, 22f));
        }

        // grid scroll
        var gridArea = Panel("Grid Area", panel, new Color(0, 0, 0, 0));
        Stretch(gridArea, Vector2.zero, new Vector2(1f, 1f), new Vector2(10f, 12f), new Vector2(-8f, -204f));
        poolContent  = MakeScroll(gridArea);
        poolViewport = (RectTransform)poolContent.parent;
        var sr = gridArea.GetComponent<ScrollRect>();
        if (sr != null) sr.onValueChanged.AddListener(_ => Virtualize());

        SetPoolData();
    }

    private IEnumerable<CardRec> FilteredCards()
    {
        var lead = Card(editing.leaderId);
        string[] leadColors   = lead != null ? lead.Colors()   : new string[0];
        string[] leadFeatures = lead != null ? lead.Features() : new string[0];
        var costCeiling = lead != null ? DeckCostCeilingFor(editing.leaderId) : null;
        string deckFeatureLock = lead != null ? DeckFeatureRestrictionFor(editing.leaderId) : null;
        string txt = (filterText ?? "").Trim().ToLowerInvariant();

        // The library has multiple records per card id (alternate arts / reprints).
        // Show only one tile per unique card id.
        var seen = new HashSet<string>();

        foreach (var c in library)
        {
            if (c == null || string.IsNullOrEmpty(c.id)) continue;
            if (!seen.Add(c.id)) continue;
            string ct = (c.type ?? "").ToLowerInvariant();

            if (pickingLeader)
            {
                if (ct != "leader") continue;
            }
            else
            {
                if (ct == "leader") continue;                       // leaders only via the slot
                if (!string.IsNullOrEmpty(filterType) && ct != filterType) continue;
                if (colorLock && leadColors.Length > 0 &&
                    !c.Colors().Any(cc => leadColors.Contains(cc))) continue;
                if (archetypeLock && leadFeatures.Length > 0 &&
                    !c.Features().Any(f => leadFeatures.Contains(f))) continue;
                if (costCeiling.HasValue &&
                    (costCeiling.Value.restrictedType == null || ct == costCeiling.Value.restrictedType) &&
                    c.cost >= costCeiling.Value.ceiling) continue;   // e.g. Rayleigh: no cost 5+ cards; Imu: no cost 2+ Events
                if (!string.IsNullOrEmpty(deckFeatureLock) && !c.Features().Contains(deckFeatureLock)) continue;   // e.g. Nami: East Blue only
            }

            if (!string.IsNullOrEmpty(filterColor) && !c.Colors().Contains(filterColor)) continue;
            if (filterCost >= 0)
            {
                if (filterCost == 10) { if (c.cost < 10) continue; }
                else if (c.cost != filterCost) continue;
            }
            if (txt.Length > 0)
            {
                bool hit = (c.name ?? "").ToLowerInvariant().Contains(txt)
                        || (c.effect ?? "").ToLowerInvariant().Contains(txt)
                        || (c.id ?? "").ToLowerInvariant().Contains(txt)
                        || (c.feature ?? "").ToLowerInvariant().Contains(txt);
                if (!hit) continue;
            }
            yield return c;
        }
    }

    // Filter handlers call this; it just re-filters and re-lays-out the recycler.
    private void RefreshGrid() => SetPoolData();

    // Recompute the filtered list and lay the recycler out from the top.
    private void SetPoolData()
    {
        if (poolContent == null) return;
        poolCards.Clear();
        poolCards.AddRange(FilteredCards()
            .OrderBy(c => c.cost).ThenBy(c => c.id, StringComparer.Ordinal));
        if (resultCountText != null)
            resultCountText.text = poolCards.Count + (poolCards.Count == 1 ? " card" : " cards");
        LayoutPool(true);
    }

    private void LayoutPool(bool resetScroll)
    {
        if (poolViewport == null) return;
        Canvas.ForceUpdateCanvases();
        float vw = poolViewport.rect.width, vh = poolViewport.rect.height;
        if (vw < 1f || vh < 1f) { if (isActiveAndEnabled) StartCoroutine(DeferLayout(resetScroll)); return; }

        gridCols = Mathf.Max(1, Mathf.FloorToInt((vw - PadL - PadR + SpaceX) / (CellW + SpaceX)));
        int rows = Mathf.CeilToInt(poolCards.Count / (float)gridCols);
        float contentH = PadT + rows * CellH + Mathf.Max(0, rows - 1) * SpaceY + PadB;
        poolContent.sizeDelta = new Vector2(0f, Mathf.Max(contentH, vh));
        if (resetScroll) poolContent.anchoredPosition = new Vector2(poolContent.anchoredPosition.x, 0f);

        // Extra buffer rows above & below the viewport so art for rows just out of
        // view is already decoded before you scroll to them.
        int visRows = Mathf.CeilToInt(vh / (CellH + SpaceY)) + 4;
        EnsurePool(gridCols * (visRows + 1));
        Virtualize();
    }

    private IEnumerator DeferLayout(bool resetScroll) { yield return null; LayoutPool(resetScroll); }

    private void EnsurePool(int need)
    {
        while (tilePool.Count < need) tilePool.Add(CreateTile());
    }

    private TileView CreateTile()
    {
        var tile = Panel("Tile", poolContent, new Color32(6, 12, 20, 255));
        tile.anchorMin = tile.anchorMax = new Vector2(0f, 1f);
        tile.pivot = new Vector2(0f, 1f);
        tile.sizeDelta = new Vector2(CellW, CellH);
        Round(tile); AddBorder(tile, MenuB, 1f);

        var holder = Panel("Art", tile, new Color32(12, 22, 34, 255));
        Stretch(holder, Vector2.zero, Vector2.one, new Vector2(3f, 26f), new Vector2(-3f, -3f));
        Round(holder);
        var artImg = holder.GetComponent<Image>(); artImg.raycastTarget = false;

        var cost = Panel("Cost", tile, new Color32(6, 32, 44, 235));
        cost.anchorMin = cost.anchorMax = new Vector2(0f, 1f);
        cost.pivot = new Vector2(0f, 1f);
        cost.sizeDelta = new Vector2(24f, 24f);
        cost.anchoredPosition = new Vector2(4f, -2f);
        RoundCircle(cost); AddBorder(cost, Accent, 1f);
        var costText = Text_("c", cost, "", 11, Accent2, TextAnchor.MiddleCenter, monoFont);
        costText.fontStyle = FontStyle.Bold;
        Stretch(costText.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        var label = Text_("n", tile, "", 8, Ink, TextAnchor.LowerCenter);
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        Stretch(label.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(2f, 2f), new Vector2(-2f, 24f));

        var badge = Panel("InDeck", tile, Accent);
        badge.anchorMin = badge.anchorMax = new Vector2(1f, 1f);
        badge.pivot = new Vector2(1f, 1f);
        badge.sizeDelta = new Vector2(26f, 22f);
        badge.anchoredPosition = new Vector2(-4f, -2f);
        Round(badge);
        var badgeText = Text_("b", badge, "", 10, BadgeInk, TextAnchor.MiddleCenter, monoFont);
        badgeText.fontStyle = FontStyle.Bold;
        Stretch(badgeText.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        var tv = new TileView { root = tile, art = artImg, cost = costText, label = label,
                                badge = badge, badgeText = badgeText };
        var btn = tile.gameObject.AddComponent<Button>();
        btn.onClick.AddListener(() => OnTileClick(tv));
        // Hovering the tile pops the big right-side preview (reads the tile's live
        // binding, so it stays correct as tiles are recycled while scrolling).
        var hov = tile.gameObject.AddComponent<HoverPreview>();
        hov.mgr = this; hov.tile = tv;
        return tv;
    }

    private void OnTileClick(TileView tv)
    {
        if (string.IsNullOrEmpty(tv.boundId)) return;
        if (pickingLeader) SetLeader(tv.boundId);
        else               AddCard(tv.boundId, +1);
    }

    // Position + bind the recycled tiles to the cards currently in view.
    private void Virtualize()
    {
        if (gridCols <= 0 || poolContent == null) return;
        float rowH = CellH + SpaceY;
        float scrollY = Mathf.Max(0f, poolContent.anchoredPosition.y);
        int firstRow = Mathf.Max(0, Mathf.FloorToInt((scrollY - PadT) / rowH) - 2);
        int start = firstRow * gridCols;

        for (int p = 0; p < tilePool.Count; p++)
        {
            var tv = tilePool[p];
            if (tv == null || tv.root == null) continue;   // destroyed tile (post-Render)
            int index = start + p;
            if (index < poolCards.Count)
            {
                int row = index / gridCols, col = index % gridCols;
                tv.root.anchoredPosition = new Vector2(PadL + col * (CellW + SpaceX), -(PadT + row * rowH));
                if (!tv.root.gameObject.activeSelf) tv.root.gameObject.SetActive(true);
                Bind(tv, poolCards[index]);
            }
            else if (tv.root.gameObject.activeSelf)
            {
                tv.root.gameObject.SetActive(false);
                tv.boundId = null;
            }
        }
    }

    private void Bind(TileView tv, CardRec c)
    {
        if (tv.boundId != c.id)
        {
            tv.boundId = c.id;
            tv.cost.text  = (c.type ?? "") == "leader" ? "L" : c.cost.ToString();
            tv.label.text = c.name;
            if (_artCache.TryGetValue(c.id, out var sp)) { ApplyArt(tv, sp); tv.artId = c.id; }
            else { ApplyArt(tv, null); tv.artId = null; }   // Update() decodes it
        }
        int n = pickingLeader ? 0 : editing.CountOf(c.id);
        if (n > 0) { tv.badge.gameObject.SetActive(true); tv.badgeText.text = "×" + n; }
        else tv.badge.gameObject.SetActive(false);
    }

    private void ApplyArt(TileView tv, Sprite sp)
    {
        if (sp != null)
        {
            tv.art.sprite = sp; tv.art.type = Image.Type.Simple;
            tv.art.preserveAspect = true; tv.art.color = Color.white;
        }
        else
        {
            tv.art.sprite = RoundSprite(); tv.art.type = Image.Type.Sliced;
            tv.art.preserveAspect = false; tv.art.color = new Color32(12, 22, 34, 255);
        }
    }

    // Refresh just the in-deck count badges on visible tiles (cheap; used after add/remove).
    private void RefreshBadges()
    {
        foreach (var tv in tilePool)
        {
            if (tv == null || tv.root == null) continue;
            if (!tv.root.gameObject.activeSelf || string.IsNullOrEmpty(tv.boundId)) continue;
            int n = pickingLeader ? 0 : editing.CountOf(tv.boundId);
            if (n > 0) { tv.badge.gameObject.SetActive(true); tv.badgeText.text = "×" + n; }
            else tv.badge.gameObject.SetActive(false);
        }
    }

    // Stream card art for whatever is currently on screen. Cached art applies
    // instantly; uncached art is decoded under a per-frame time budget so a fast
    // scroll fills in within a frame or two of stopping and never hitches. Because
    // we always look at the live tile bindings, scrolling past a card never wastes
    // work on it — only what's actually visible gets decoded.
    // Each frame: apply cached art instantly, and kick off async decodes (on a
    // worker thread) for visible tiles that still need art. Concurrency is capped
    // so we never flood, and because we only look at live tile bindings, a fast
    // scroll only ever requests what's actually on screen.
    private void Update()
    {
        if (poolContent != null && tilePool.Count > 0)
        {
            foreach (var tv in tilePool)
            {
                if (tv == null || tv.root == null) continue;             // destroyed tile
                if (!tv.root.gameObject.activeSelf || string.IsNullOrEmpty(tv.boundId)) continue;
                if (tv.artId == tv.boundId) continue;                    // already correct

                if (_artCache.TryGetValue(tv.boundId, out var sp))
                {
                    ApplyArt(tv, sp); tv.artId = tv.boundId;             // cached → instant
                    continue;
                }
                if (activeArtLoads < MaxConcurrentLoads && !_artLoading.Contains(tv.boundId))
                    StartCoroutine(LoadArtAsync(tv.boundId));            // decode off-thread
            }
        }

        // One shared decode budget for BOTH tiers, drained once per frame
        // regardless of how many Pump*Requests calls follow - see the comment
        // on MaxDecodeCostPerFrame for why this can't be two independent steps.
        DrainDecodeQueue();
        PumpArtRequests();
        PumpThumbArtRequests();
    }

    // ── Generic async art delivery for one-off Images (leader preview, hover
    // preview) — the non-pooled counterpart to the tile-pool loop above.
    // RequestArt() queues a fetch and remembers which Image wants it; this
    // drains that queue under the same concurrency cap and hands off finished
    // art the moment it lands in _artCache, including art finished by the tile
    // pool's own loading. Hex cells / decklist rows / library thumbnail cards
    // use the parallel RequestThumbArt()/_thumbCache tier below instead (see
    // the comment on _thumbCache further up). A destroyed Image (its owning
    // row/panel got torn down by the next Render() before art arrived) is just
    // dropped — Unity's overridden null-check makes that a plain `== null`.
    private readonly List<(Image img, string id, Color tint)> _artWaiters = new List<(Image, string, Color)>();
    private readonly Queue<string> _artQueue = new Queue<string>();
    private readonly HashSet<string> _artQueued = new HashSet<string>();

    private readonly List<(Image img, string id, Color tint)> _thumbWaiters = new List<(Image, string, Color)>();
    private readonly Queue<string> _thumbQueue = new Queue<string>();
    private readonly HashSet<string> _thumbQueued = new HashSet<string>();

    // `tint` is the colour the Image should end up at once art lands - most
    // callers want opaque white, but a few (decklist row art-bleed) intentionally
    // render art at partial opacity, so that final tint has to survive the swap.
    private void RequestArt(Image img, string id, Color? tint = null)
    {
        if (img == null || string.IsNullOrEmpty(id)) return;
        // Supersede any older pending request on this same Image (matters for
        // long-lived Images like the hover preview, which gets re-requested
        // rapidly as the pointer moves - without this, a stale slow load could
        // land after a newer one and clobber it with the wrong card's art).
        _artWaiters.RemoveAll(w => w.img == img);
        Color finalTint = tint ?? Color.white;
        if (_artCache.TryGetValue(id, out var cached))
        {
            if (cached != null) ApplyLoadedArt(img, cached, finalTint);
            return;
        }
        _artWaiters.Add((img, id, finalTint));
        if (_artQueued.Add(id)) _artQueue.Enqueue(id);
    }

    // Same shape as RequestArt, but for the small pre-generated thumbnail tier.
    private void RequestThumbArt(Image img, string id, Color? tint = null)
    {
        if (img == null || string.IsNullOrEmpty(id)) return;
        _thumbWaiters.RemoveAll(w => w.img == img);
        Color finalTint = tint ?? Color.white;
        if (_thumbCache.TryGetValue(id, out var cached))
        {
            if (cached != null) ApplyLoadedArt(img, cached, finalTint);
            return;
        }
        _thumbWaiters.Add((img, id, finalTint));
        if (_thumbQueued.Add(id)) _thumbQueue.Enqueue(id);
    }

    private void ApplyLoadedArt(Image img, Sprite sp, Color tint)
    {
        // preserveAspect is left alone - every caller already sets it correctly
        // on the Image before requesting art, and Unity doesn't reset it on a
        // sprite swap, so touching it here would silently fight callers like
        // the library-card thumbnail that want preserveAspect = true.
        img.sprite = sp; img.type = Image.Type.Simple; img.color = tint;
    }

    private void PumpArtRequests()
    {
        while (_artQueue.Count > 0 && activeArtLoads < MaxConcurrentLoads)
        {
            string id = _artQueue.Dequeue();
            _artQueued.Remove(id);
            if (_artCache.ContainsKey(id) || _artLoading.Contains(id)) continue;
            StartCoroutine(LoadArtAsync(id));
        }
        DeliverWaiters(_artWaiters, _artCache);
    }

    private void PumpThumbArtRequests()
    {
        while (_thumbQueue.Count > 0 && activeThumbLoads < MaxConcurrentThumbLoads)
        {
            string id = _thumbQueue.Dequeue();
            _thumbQueued.Remove(id);
            if (_thumbCache.ContainsKey(id) || _thumbLoading.Contains(id)) continue;
            StartCoroutine(LoadThumbArtAsync(id));
        }
        DeliverWaiters(_thumbWaiters, _thumbCache);
    }

    private void DeliverWaiters(List<(Image img, string id, Color tint)> waiters, Dictionary<string, Sprite> cache)
    {
        if (waiters.Count == 0) return;
        for (int i = waiters.Count - 1; i >= 0; i--)
        {
            var (img, id, tint) = waiters[i];
            if (img == null) { waiters.RemoveAt(i); continue; }      // owner torn down
            if (cache.TryGetValue(id, out var sp))
            {
                if (sp != null) ApplyLoadedArt(img, sp, tint);
                waiters.RemoveAt(i);
            }
        }
    }

    // Fetches the card image bytes off the main thread (UnityWebRequest yields
    // normally here, cheap). On success the bytes go on the shared decode queue
    // instead of being decoded immediately - see MaxDecodeCostPerFrame below for
    // why. The id's slot in _artLoading/activeArtLoads is intentionally NOT
    // released here; it stays reserved until DrainDecodeQueue() actually decodes
    // it, so nothing else can start a duplicate fetch for the same id while it's
    // queued waiting for its turn to decode.
    private IEnumerator LoadArtAsync(string id)
    {
        _artLoading.Add(id);
        activeArtLoads++;

        string path = ArtPath(id);
        byte[] fetched = null;
        if (path != null)
        {
            string url = new System.Uri(path).AbsoluteUri;          // handles spaces in the project path
            using (var req = UnityWebRequest.Get(url))
            {
                req.downloadHandler = new DownloadHandlerBuffer();
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success) fetched = req.downloadHandler.data;
            }
        }

        if (fetched != null)
            _decodeQueue.Enqueue(new PendingDecode
            {
                id = id, data = fetched, cost = MasterDecodeCost,
                targetCache = _artCache, onFinish = FinishArtLoad,
            });
        else { _artCache[id] = null; FinishArtLoad(id, null); }   // nothing to decode - finalize now
    }

    // Same fetch shape as LoadArtAsync, but resolves through ResolveThumbPath
    // (falling back to the full-resolution master if no thumbnail has been
    // generated yet for this id). A fallback decode is a full master even
    // though it was requested through the thumb tier, so it's charged the
    // MASTER cost, not the thumbnail cost - see PendingDecode.cost below.
    private IEnumerator LoadThumbArtAsync(string id)
    {
        _thumbLoading.Add(id);
        activeThumbLoads++;

        var (path, isRealThumb) = ResolveThumbPath(id);
        byte[] fetched = null;
        if (path != null)
        {
            string url = new System.Uri(path).AbsoluteUri;
            using (var req = UnityWebRequest.Get(url))
            {
                req.downloadHandler = new DownloadHandlerBuffer();
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success) fetched = req.downloadHandler.data;
            }
        }

        if (fetched != null)
            _decodeQueue.Enqueue(new PendingDecode
            {
                id = id, data = fetched, cost = isRealThumb ? ThumbDecodeCost : MasterDecodeCost,
                targetCache = _thumbCache, onFinish = FinishThumbLoad,
            });
        else { _thumbCache[id] = null; FinishThumbLoad(id, null); }
    }

    // The one real per-frame cost bound: Texture2D.LoadImage (+ mip chain) must
    // run on the main thread and can't be yielded. A full ~715x1000 master
    // costs roughly 10-30ms; a ~448x626 thumbnail is estimated at roughly a
    // third of that given the ~2.5-3x fewer pixels (decode cost doesn't scale
    // perfectly linearly with pixel count - there's some fixed per-call
    // overhead - so this is a reasoned estimate, not a measurement; tune after
    // testing). MaxDecodeCostPerFrame=12 with these weights allows either 2
    // masters' worth (matches the previously-tuned, proven-safe ceiling
    // exactly, so a frame with only master-tier work can't regress) or up to 6
    // thumbnails, or any mix. Both tiers share this ONE budget (see
    // DrainDecodeQueue, called once per frame from Update()) specifically so
    // they can never add up to more than this in a single frame - two
    // independently-budgeted decode steps would be additive on any frame both
    // have work, which is exactly the bug this design avoids.
    private const int MasterDecodeCost = 6;
    private const int ThumbDecodeCost = 2;
    private const int MaxDecodeCostPerFrame = 12;

    private struct PendingDecode
    {
        public string id;
        public byte[] data;
        public int cost;
        public Dictionary<string, Sprite> targetCache;
        public Action<string, Sprite> onFinish;   // releases the correct tier's loading-slot bookkeeping
    }
    private readonly Queue<PendingDecode> _decodeQueue = new Queue<PendingDecode>();

    private void DrainDecodeQueue()
    {
        int budget = MaxDecodeCostPerFrame;
        while (_decodeQueue.Count > 0 && _decodeQueue.Peek().cost <= budget)
        {
            var item = _decodeQueue.Dequeue();
            budget -= item.cost;

            // Mipmapped + trilinear so the many downscaled displays (pool tiles,
            // hex crops, leader slots, previews) sample cleanly instead of
            // aliasing/looking blurry.
            Sprite made = null;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true);   // mipChain = true
            if (tex.LoadImage(item.data, true))                          // builds mips, then non-readable
            {
                tex.filterMode = FilterMode.Trilinear;
                tex.anisoLevel = 4;
                made = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            }
            item.targetCache[item.id] = made;
            item.onFinish(item.id, made);
        }
    }

    // Common tail for both the "nothing to decode" and "just decoded" exits on
    // the MASTER tier: releases the id's concurrency slot and pushes the result
    // to any tile-pool binding still waiting on it (RequestArt's own waiters
    // are delivered separately, from PumpArtRequests's waiter scan).
    private void FinishArtLoad(string id, Sprite sprite)
    {
        _artLoading.Remove(id);
        activeArtLoads--;

        foreach (var tv in tilePool)
            if (tv != null && tv.root != null &&
                tv.root.gameObject.activeSelf && tv.boundId == id && tv.artId != id)
            { ApplyArt(tv, sprite); tv.artId = id; }
    }

    // THUMB-tier counterpart to FinishArtLoad - no tile-pool notification
    // needed, the tile pool never uses thumbnails.
    private void FinishThumbLoad(string id, Sprite sprite)
    {
        _thumbLoading.Remove(id);
        activeThumbLoads--;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Deck mutation + rules
    // ══════════════════════════════════════════════════════════════════════════
    // Niche per-card/per-leader overrides, detected from the card's own printed
    // rules text rather than a hardcoded id list — any current or future card
    // reusing the same official wording is picked up automatically. Most of
    // these are prefixed "Under the rules of this game, ..." but not all are
    // worded identically (Nami's two leader prints phrase the deck-out rule
    // differently from each other), so each check looks for its own specific
    // clause. Mirrors OnePieceTcg.Engine.CardData's versions (which parse the
    // same text server-side for actual gameplay); duplicated here because the
    // deck builder loads its own CardRec library independently of the engine's
    // CardDef library.
    private static readonly Regex DeckCostCeilingRegex =
        new Regex(@"cannot include (\w+) with a cost of (\d+) or more in your deck", RegexOptions.Compiled);
    private static readonly Regex DeckFeatureRestrictionRegex =
        new Regex(@"you can only include \{([^}]+)\} type cards in your deck", RegexOptions.Compiled);
    private static readonly Regex DonDeckSizeRegex =
        new Regex(@"your DON!! deck consists of (\d+) cards", RegexOptions.Compiled);

    // Max copies of this card allowed in a deck (default 4; unlimited-copy
    // cards like Pacifista / Prisoner of Impel Down return 50 — effectively
    // uncapped, bounded only by the 50-card deck itself).
    private int MaxCopiesFor(string cardId)
    {
        var rec = Card(cardId);
        if (rec != null && (rec.effect ?? "").Contains("you may have any number of this card in your deck"))
            return 50;
        return 4;
    }

    // If this leader restricts deck-building by card cost (e.g. Silvers
    // Rayleigh: all cards; Imu: Events only), the affected card type (null =
    // every card type) and the lowest disallowed cost (cards of that type must
    // have cost < ceiling). Null when the leader has no such restriction.
    private (string restrictedType, int ceiling)? DeckCostCeilingFor(string leaderId)
    {
        var rec = Card(leaderId);
        if (rec == null) return null;
        var m = DeckCostCeilingRegex.Match(rec.effect ?? "");
        if (!m.Success) return null;
        string word = m.Groups[1].Value.ToLowerInvariant();
        string restrictedType = word == "cards" ? null : word.TrimEnd('s');   // "Events" -> "event"
        return (restrictedType, int.Parse(m.Groups[2].Value));
    }

    // If this leader restricts the deck to a single feature/type (e.g. Nami
    // [P-117]: East Blue only), the required feature name. Null otherwise.
    private string DeckFeatureRestrictionFor(string leaderId)
    {
        var rec = Card(leaderId);
        if (rec == null) return null;
        var m = DeckFeatureRestrictionRegex.Match(rec.effect ?? "");
        return m.Success ? m.Groups[1].Value : null;
    }

    // DON!! deck size this leader plays with (default 10; e.g. Enel: 6).
    // Display-only here — GameEngine enforces it at match creation.
    private int DonDeckSizeFor(string leaderId)
    {
        var rec = Card(leaderId);
        if (rec == null) return 10;
        var m = DonDeckSizeRegex.Match(rec.effect ?? "");
        return m.Success ? int.Parse(m.Groups[1].Value) : 10;
    }

    // Gold chips flagging every niche rule this leader carries, so none of
    // this is silently enforced — shown next to LIFE/POWER wherever a leader
    // is previewed (deck editor + deck-select hex view).
    private void AddNicheRuleChips(RectTransform chips, string leaderId)
    {
        int donSize = DonDeckSizeFor(leaderId);
        if (donSize != 10) AddRuleChip(chips, "DON!! " + donSize);

        var costCeiling = DeckCostCeilingFor(leaderId);
        if (costCeiling.HasValue)
        {
            var (restrictedType, ceiling) = costCeiling.Value;
            string label = restrictedType != null
                ? $"{restrictedType.ToUpperInvariant()} COST ≤{ceiling - 1}"
                : $"DECK COST ≤{ceiling - 1}";
            AddRuleChip(chips, label);
        }

        string featureLock = DeckFeatureRestrictionFor(leaderId);
        if (!string.IsNullOrEmpty(featureLock)) AddRuleChip(chips, featureLock.ToUpperInvariant() + " ONLY");

        string effect = Card(leaderId)?.effect ?? "";
        if (effect.Contains("you win the game instead of losing")) AddRuleChip(chips, "WINS ON DECK-OUT");
        else if (effect.Contains("you do not lose when your deck has 0 cards")) AddRuleChip(chips, "DECK-OUT DELAYED");
    }

    private void SetLeader(string id)
    {
        editing.leaderId = id;
        pickingLeader = false;
        filterType = "";
        // returning to the build view; refresh everything
        Render();
    }

    private void AddCard(string id, int delta)
    {
        var rec = Card(id);
        if (rec == null) return;
        string t = (rec.type ?? "").ToLower();
        if (t == "leader") { SetLeader(id); return; }

        int cur = editing.CountOf(id);
        int next = cur + delta;
        if (next < 0) next = 0;
        if (delta > 0)
        {
            int maxCopies = MaxCopiesFor(id);
            if (cur >= maxCopies) { Flash(maxCopies >= 50 ? "Deck is full (50)" : $"Max {maxCopies} copies of a card"); return; }
            if (editing.MainCount() >= 50) { Flash("Deck is full (50)"); return; }
            next = Mathf.Min(next, maxCopies);
        }
        editing.SetCount(id, next);
        RefreshDeckList();
        RefreshValidity();
        RefreshStats();
        RefreshBadges();   // cheap: just update the in-deck count badges
    }

    // (ok, total, messages)
    private (bool, int, List<string>) Validate(DeckData d)
    {
        var msgs = new List<string>();
        var lead = Card(d.leaderId);
        int total = d.MainCount();

        if (lead == null) msgs.Add("• No leader selected");
        if (total != 50)  msgs.Add($"• Main deck is {total}/50");

        string[] leadColors = lead != null ? lead.Colors() : new string[0];
        var costCeiling = d.leaderId != null ? DeckCostCeilingFor(d.leaderId) : null;
        string deckFeatureLock = d.leaderId != null ? DeckFeatureRestrictionFor(d.leaderId) : null;
        foreach (var e in d.cards)
        {
            var rec = Card(e.id);
            if (rec == null) { msgs.Add("• Unknown card " + e.id); continue; }
            int maxCopies = MaxCopiesFor(e.id);
            if (e.count > maxCopies) msgs.Add($"• {rec.name}: {e.count} copies (max {maxCopies})");
            if ((rec.type ?? "").ToLower() == "leader") msgs.Add($"• {rec.name} is a leader, not a deck card");
            if (lead != null && leadColors.Length > 0 &&
                !rec.Colors().Any(c => leadColors.Contains(c)))
                msgs.Add($"• {rec.name} ({rec.color}) is off-colour");
            if (costCeiling.HasValue)
            {
                var (restrictedType, ceiling) = costCeiling.Value;
                string ct = (rec.type ?? "").ToLowerInvariant();
                if ((restrictedType == null || ct == restrictedType) && rec.cost >= ceiling)
                {
                    string scope = restrictedType != null ? $" for {restrictedType}s" : "";
                    msgs.Add($"• {rec.name}: cost {rec.cost} exceeds {lead.name}'s deck-building limit (max {ceiling - 1}{scope})");
                }
            }
            if (!string.IsNullOrEmpty(deckFeatureLock) && !rec.Features().Contains(deckFeatureLock))
                msgs.Add($"• {rec.name}: {lead.name} can only include {{{deckFeatureLock}}} type cards");
        }
        bool ok = msgs.Count == 0 && lead != null && total == 50;
        return (ok, total, msgs);
    }

    private void RefreshValidity()
    {
        if (validityText == null) return;
        var (ok, total, msgs) = Validate(editing);
        if (countText != null)
        {
            countText.text = total + " / 50";
            countText.color = total == 50 ? GoodGreen : (total > 50 ? RedAccent : Ink);
        }
        if (ok)
        {
            validityText.text = "✓ Legal deck — ready to save.";
            validityText.color = GoodGreen;
        }
        else
        {
            var shown = msgs.Take(4).ToList();
            if (msgs.Count > 4) shown.Add($"…and {msgs.Count - 4} more");
            validityText.text = string.Join("\n", shown);
            validityText.color = Muted;
        }
        if (saveButtonBg != null)
        {
            saveButtonBg.color = ok ? Accent : (Color)new Color32(40, 60, 78, 220);
            if (saveButtonText != null) saveButtonText.color = ok ? BadgeInk : Muted;
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Deck-composition graphs (left column)
    // ══════════════════════════════════════════════════════════════════════════
    private void RefreshStats()
    {
        if (statsRoot == null) return;
        for (int i = statsRoot.childCount - 1; i >= 0; i--) Destroy(statsRoot.GetChild(i).gameObject);

        int chars = 0, events = 0, stages = 0;
        int[] cost = new int[11];
        int[] counter = new int[3];
        var colorCounts = new Dictionary<string, int>();
        var archCounts  = new Dictionary<string, int>();

        foreach (var e in editing.cards)
        {
            var rec = Card(e.id);
            if (rec == null) continue;
            string t = (rec.type ?? "").ToLowerInvariant();
            if (t == "event") events += e.count;
            else if (t == "stage") stages += e.count;
            else chars += e.count;

            cost[Mathf.Clamp(rec.cost, 0, 10)] += e.count;
            int ci = rec.counter >= 2000 ? 2 : (rec.counter >= 1000 ? 1 : 0);
            counter[ci] += e.count;

            foreach (var c in rec.Colors())
            {
                if (string.IsNullOrEmpty(c)) continue;
                colorCounts.TryGetValue(c, out int cv); colorCounts[c] = cv + e.count;
            }
            foreach (var f in rec.Features())
            {
                if (string.IsNullOrEmpty(f)) continue;
                archCounts.TryGetValue(f, out int av); archCounts[f] = av + e.count;
            }
        }

        // average cost (10+ counts as 10)
        int costTotal = 0; float weighted = 0f;
        for (int i = 0; i < cost.Length; i++) { costTotal += cost[i]; weighted += i * cost[i]; }
        float avgCost = costTotal > 0 ? weighted / costTotal : 0f;

        // top 5 archetypes + "Other" (features overlap, so these DON'T sum to 50)
        var archSorted = archCounts.OrderByDescending(k => k.Value).ToList();
        var archTop = new List<KeyValuePair<string, int>>();
        int shown = Mathf.Min(5, archSorted.Count);
        for (int i = 0; i < shown; i++) archTop.Add(archSorted[i]);
        int other = 0; for (int i = shown; i < archSorted.Count; i++) other += archSorted[i].Value;
        if (other > 0) archTop.Add(new KeyValuePair<string, int>("Other", other));

        var colorList = colorCounts.OrderByDescending(k => k.Value).ToList();

        float y = -4f;

        // color identity
        if (colorList.Count > 0) y = ColorIdentity(colorList, y);

        // cost curve (+ avg marker & readout)
        y = StatSectionLabel("COST CURVE", y, "avg " + avgCost.ToString("0.0"));
        y = CostCurve(cost, avgCost, y);
        y -= 10f;

        // card types
        y = StatSectionLabel("CARD TYPES", y);
        int typeMax = Mathf.Max(1, Mathf.Max(chars, Mathf.Max(events, stages)));
        y = StatBar("Character", chars, typeMax, y);
        y = StatBar("Event",     events, typeMax, y);
        y = StatBar("Stage",     stages, typeMax, y);
        y -= 10f;

        // counters
        y = StatSectionLabel("COUNTERS", y);
        int cMax = Mathf.Max(1, Mathf.Max(counter[0], Mathf.Max(counter[1], counter[2])));
        y = StatBar("None",  counter[0], cMax, y);
        y = StatBar("+1000", counter[1], cMax, y);
        y = StatBar("+2000", counter[2], cMax, y);
        y -= 10f;

        // archetypes
        if (archTop.Count > 0)
        {
            y = StatSectionLabel("ARCHETYPES", y, "by card count");
            y = ArchetypeShare(archTop, y);
        }
    }

    // Section caption; returns the next y cursor. Optional `right` adds a
    // right-aligned gold readout on the same line (e.g. "avg 3.8").
    private float StatSectionLabel(string text, float y, string right = null)
    {
        var t = Text_("S_" + text, statsRoot, text, 9, Muted, TextAnchor.LowerLeft, monoFont);
        t.fontStyle = FontStyle.Bold;
        var rt = t.rectTransform;
        rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(0f, 16f);
        rt.anchoredPosition = new Vector2(0f, y);

        if (!string.IsNullOrEmpty(right))
        {
            var rv = Text_("SR_" + text, statsRoot, right, 9, Gold, TextAnchor.LowerRight, monoFont);
            rv.fontStyle = FontStyle.Bold;
            var rr = rv.rectTransform;
            rr.anchorMin = new Vector2(0f, 1f); rr.anchorMax = new Vector2(1f, 1f);
            rr.pivot = new Vector2(0.5f, 1f);
            rr.sizeDelta = new Vector2(0f, 16f);
            rr.anchoredPosition = new Vector2(0f, y);
        }
        return y - 18f;
    }

    // Horizontal labelled bar (label · track · value); returns the next y cursor.
    private float StatBar(string label, int value, int max, float y)
    {
        const float rowH = 18f, labelW = 64f, valueW = 28f;

        // Full-width row container, top-anchored, positioned by y.
        var row = Panel("StatRow", statsRoot, new Color(0, 0, 0, 0));
        row.anchorMin = new Vector2(0f, 1f); row.anchorMax = new Vector2(1f, 1f);
        row.pivot = new Vector2(0.5f, 1f);
        row.sizeDelta = new Vector2(0f, rowH);
        row.anchoredPosition = new Vector2(0f, y);
        row.GetComponent<Image>().raycastTarget = false;

        // label (left, fixed width)
        var lbl = Text_("L", row, label, 9, Ink, TextAnchor.MiddleLeft, monoFont);
        Stretch(lbl.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(labelW, 0f));

        // track (fills between label and value)
        var track = Panel("Track", row, new Color(1f, 1f, 1f, 0.05f));
        Stretch(track, new Vector2(0f, 0f), new Vector2(1f, 1f),
            new Vector2(labelW, 3f), new Vector2(-valueW, -3f));
        Round(track);
        track.GetComponent<Image>().raycastTarget = false;

        // fill (fraction of the track width)
        float frac = max > 0 ? Mathf.Clamp01(value / (float)max) : 0f;
        var fill = Panel("Fill", track, value > 0 ? (Color)Accent : new Color(0, 0, 0, 0));
        Stretch(fill, new Vector2(0f, 0f), new Vector2(frac, 1f), Vector2.zero, Vector2.zero);
        Round(fill);
        fill.GetComponent<Image>().raycastTarget = false;

        // value (right, fixed width)
        var val = Text_("V", row, value.ToString(), 9, value > 0 ? Accent2 : Muted, TextAnchor.MiddleRight, monoFont);
        val.fontStyle = FontStyle.Bold;
        Stretch(val.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-valueW + 2f, 0f), Vector2.zero);

        return y - (rowH + 4f);
    }

    // Vertical cost-curve histogram (costs 0..9 and 10+); returns the next y cursor.
    private float CostCurve(int[] cost, float avg, float y)
    {
        const float barAreaH = 70f, capH = 12f, labelH = 14f;
        int n = cost.Length;
        int max = 1;
        for (int i = 0; i < n; i++) max = Mathf.Max(max, cost[i]);

        var area = Panel("CostArea", statsRoot, new Color(0, 0, 0, 0));
        area.anchorMin = new Vector2(0f, 1f); area.anchorMax = new Vector2(1f, 1f);
        area.pivot = new Vector2(0.5f, 1f);
        area.sizeDelta = new Vector2(0f, barAreaH + capH + labelH);
        area.anchoredPosition = new Vector2(0f, y);
        area.GetComponent<Image>().raycastTarget = false;

        for (int i = 0; i < n; i++)
        {
            float x0 = i / (float)n, x1 = (i + 1) / (float)n;
            float frac = Mathf.Clamp01(cost[i] / (float)max);

            // count cap
            var cap = Text_("cap" + i, area, cost[i] > 0 ? cost[i].ToString() : "", 8,
                cost[i] > 0 ? Accent2 : Muted, TextAnchor.LowerCenter, monoFont);
            var cr = cap.rectTransform;
            cr.anchorMin = new Vector2(x0, 1f); cr.anchorMax = new Vector2(x1, 1f);
            cr.offsetMin = new Vector2(1f, -capH); cr.offsetMax = new Vector2(-1f, 0f);

            // bar (grows up from the baseline above the label row)
            var bar = Panel("bar" + i, area, cost[i] > 0 ? (Color)Accent : new Color(1f, 1f, 1f, 0.05f));
            bar.anchorMin = new Vector2(x0, 0f); bar.anchorMax = new Vector2(x1, 0f);
            bar.pivot = new Vector2(0.5f, 0f);
            float h = Mathf.Max(2f, frac * barAreaH);
            bar.offsetMin = new Vector2(2f, labelH);
            bar.offsetMax = new Vector2(-2f, labelH + h);
            Round(bar);
            bar.GetComponent<Image>().raycastTarget = false;

            // cost label
            var lab = Text_("lab" + i, area, i == 10 ? "10+" : i.ToString(), 8, Muted, TextAnchor.LowerCenter, monoFont);
            var lr = lab.rectTransform;
            lr.anchorMin = new Vector2(x0, 0f); lr.anchorMax = new Vector2(x1, 0f);
            lr.offsetMin = new Vector2(0f, 0f); lr.offsetMax = new Vector2(0f, labelH);
        }

        // ── avg-cost marker (gold vertical line over the bar area) ──
        float mfrac = Mathf.Clamp01(avg / (n - 1));
        var mark = Panel("avg", area, new Color(Gold.r, Gold.g, Gold.b, 0.7f));
        mark.anchorMin = new Vector2(mfrac, 0f); mark.anchorMax = new Vector2(mfrac, 0f);
        mark.pivot = new Vector2(0.5f, 0f);
        mark.sizeDelta = new Vector2(1.6f, barAreaH);
        mark.anchoredPosition = new Vector2(0f, labelH);
        mark.GetComponent<Image>().raycastTarget = false;

        return y - (barAreaH + capH + labelH + 4f);
    }

    // Red/Green identity tiles (equal-width row), driven by rec.Colors().
    private float ColorIdentity(List<KeyValuePair<string, int>> colors, float y)
    {
        const float h = 30f;
        var row = Panel("ColorId", statsRoot, new Color(0, 0, 0, 0));
        row.anchorMin = new Vector2(0f, 1f); row.anchorMax = new Vector2(1f, 1f);
        row.pivot = new Vector2(0.5f, 1f);
        row.sizeDelta = new Vector2(0f, h);
        row.anchoredPosition = new Vector2(0f, y);
        row.GetComponent<Image>().raycastTarget = false;

        int n = Mathf.Max(1, colors.Count);
        for (int i = 0; i < colors.Count; i++)
        {
            var kv = colors[i];
            Color c = ColorSwatch.TryGetValue(kv.Key, out var sw) ? sw : Muted;
            float x0 = i / (float)n, x1 = (i + 1) / (float)n;

            var tile = Panel("c" + i, row, new Color(c.r, c.g, c.b, 0.12f));
            tile.anchorMin = new Vector2(x0, 0f); tile.anchorMax = new Vector2(x1, 1f);
            tile.offsetMin = new Vector2(i == 0 ? 0f : 3f, 0f);
            tile.offsetMax = new Vector2(-3f, 0f);
            Round(tile); AddBorder(tile, new Color(c.r, c.g, c.b, 0.4f), 1f);

            var dot = Panel("dot", tile, c);
            dot.anchorMin = dot.anchorMax = new Vector2(0f, 0.5f);
            dot.pivot = new Vector2(0f, 0.5f);
            dot.sizeDelta = new Vector2(9f, 9f);
            dot.anchoredPosition = new Vector2(9f, 0f);
            RoundCircle(dot);

            var lbl = Text_("l", tile, kv.Key.ToUpperInvariant(), 11, Ink, TextAnchor.MiddleLeft);
            Stretch(lbl.rectTransform, Vector2.zero, Vector2.one, new Vector2(24f, 0f), new Vector2(-26f, 0f));

            var val = Text_("v", tile, kv.Value.ToString(), 11, Ink, TextAnchor.MiddleRight, monoFont);
            val.fontStyle = FontStyle.Bold;
            Stretch(val.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-8f, 0f));
        }
        return y - (h + 12f);
    }

    // Stacked color line + 2-column legend, driven by top-5 features + "Other".
    private float ArchetypeShare(List<KeyValuePair<string, int>> items, float y)
    {
        const float barH = 14f, rowH = 18f;

        float total = 0f; foreach (var it in items) total += it.Value; if (total <= 0f) total = 1f;

        var bar = Panel("ArchBar", statsRoot, new Color(1f, 1f, 1f, 0.05f));
        bar.anchorMin = new Vector2(0f, 1f); bar.anchorMax = new Vector2(1f, 1f);
        bar.pivot = new Vector2(0.5f, 1f);
        bar.sizeDelta = new Vector2(0f, barH);
        bar.anchoredPosition = new Vector2(0f, y);
        Round(bar);
        bar.GetComponent<Image>().raycastTarget = false;

        float cum = 0f;
        for (int i = 0; i < items.Count; i++)
        {
            float w = items[i].Value / total;
            var seg = Panel("s" + i, bar, ArchColor(i, items[i].Key));
            seg.anchorMin = new Vector2(cum, 0f); seg.anchorMax = new Vector2(cum + w, 1f);
            seg.offsetMin = Vector2.zero; seg.offsetMax = Vector2.zero;
            seg.GetComponent<Image>().raycastTarget = false;
            cum += w;
        }
        y -= (barH + 11f);

        int rows = Mathf.CeilToInt(items.Count / 2f);
        var leg = Panel("ArchLeg", statsRoot, new Color(0, 0, 0, 0));
        leg.anchorMin = new Vector2(0f, 1f); leg.anchorMax = new Vector2(1f, 1f);
        leg.pivot = new Vector2(0.5f, 1f);
        leg.sizeDelta = new Vector2(0f, rows * rowH);
        leg.anchoredPosition = new Vector2(0f, y);
        leg.GetComponent<Image>().raycastTarget = false;

        for (int i = 0; i < items.Count; i++)
        {
            int col = i % 2, r = i / 2;
            float x0 = col * 0.5f, x1 = x0 + 0.5f;
            float pad = col == 1 ? 8f : 0f;

            var cell = Panel("cell" + i, leg, new Color(0, 0, 0, 0));
            cell.anchorMin = new Vector2(x0, 1f); cell.anchorMax = new Vector2(x1, 1f);
            cell.pivot = new Vector2(0.5f, 1f);
            cell.sizeDelta = new Vector2(0f, rowH);
            cell.anchoredPosition = new Vector2(0f, -r * rowH);
            cell.GetComponent<Image>().raycastTarget = false;

            var dot = Panel("d", cell, ArchColor(i, items[i].Key));
            dot.anchorMin = dot.anchorMax = new Vector2(0f, 0.5f);
            dot.pivot = new Vector2(0f, 0.5f);
            dot.sizeDelta = new Vector2(8f, 8f);
            dot.anchoredPosition = new Vector2(pad, 0f);
            Round(dot);

            Color nameCol = items[i].Key == "Other" ? Muted : Ink;
            var nm = Text_("n", cell, items[i].Key, 11, nameCol, TextAnchor.MiddleLeft);
            Stretch(nm.rectTransform, Vector2.zero, Vector2.one, new Vector2(pad + 13f, 0f), new Vector2(-22f, 0f));

            var vv = Text_("v", cell, items[i].Value.ToString(), 10,
                items[i].Key == "Other" ? Muted : Accent2, TextAnchor.MiddleRight, monoFont);
            vv.fontStyle = FontStyle.Bold;
            Stretch(vv.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-2f, 0f));
        }
        return y - (rows * rowH + 4f);
    }

    private Color ArchColor(int i, string key)
        => key == "Other" ? ArchOther : ArchPalette[i % ArchPalette.Length];

    // ══════════════════════════════════════════════════════════════════════════
    // Deck import / export
    // ══════════════════════════════════════════════════════════════════════════
    // Matches "4xOP05-069", "4x OP05-069", "4 OP05-069", "1xOP09-004-1" (alt-art
    // suffix), comma-separated tokens, and either one-per-line or space-packed
    // single-line lists — the union of every format used by OPTCGSim, EGMan,
    // OnePieceTopDecks and Limitless-style tournament decklist text.
    private static readonly Regex ImportTokenRegex = new Regex(
        @"(?<qty>\d{1,2})[\s,]*[xX]?[\s,]*(?<code>[A-Za-z]{1,4}\d{0,3}-\d{1,4}(?:-\d{1,3})?)",
        RegexOptions.Compiled);

    private void OpenImportModal()
    {
        if (importOverlay != null) Destroy(importOverlay.gameObject);

        importOverlay = Panel("Import Overlay", canvas.transform, new Color(0f, 0f, 0f, 0.62f));
        Stretch(importOverlay, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        importOverlay.SetAsLastSibling();
        importOverlay.gameObject.AddComponent<Button>().onClick.AddListener(CloseImportModal);

        var box = Panel("Box", importOverlay, PanelFill);
        box.anchorMin = box.anchorMax = new Vector2(0.5f, 0.5f);
        box.pivot = new Vector2(0.5f, 0.5f);
        box.sizeDelta = new Vector2(640f, 460f);
        RoundBig(box); AddBorder(box, Accent, 1.4f);

        var title = Text_("Title", box, "IMPORT DECK", 16, Ink, TextAnchor.UpperLeft);
        title.fontStyle = FontStyle.Bold;
        Stretch(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(20f, -38f), new Vector2(-20f, -12f));

        var hint = Text_("Hint", box,
            "Paste a deck list from OPTCGSim, EGMan, OnePieceTopDecks, Limitless or a tournament " +
            "submission — lines like \"4xOP05-069\" or \"4 OP05-069\", one per line or space-separated " +
            "all work. The leader is detected automatically.",
            11, Muted, TextAnchor.UpperLeft);
        hint.horizontalOverflow = HorizontalWrapMode.Wrap;
        Stretch(hint.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(20f, -94f), new Vector2(-20f, -40f));

        var fieldHolder = Panel("Field", box, new Color32(8, 16, 26, 255));
        Stretch(fieldHolder, new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(20f, -338f), new Vector2(-20f, -100f));
        Round(fieldHolder); AddBorder(fieldHolder, MenuB, 1f);

        var textComp = Text_("Text", fieldHolder, "", 13, Ink, TextAnchor.UpperLeft);
        Stretch(textComp.rectTransform, Vector2.zero, Vector2.one, new Vector2(10f, 8f), new Vector2(-10f, -8f));
        textComp.supportRichText = false;
        textComp.horizontalOverflow = HorizontalWrapMode.Wrap;
        textComp.verticalOverflow = VerticalWrapMode.Overflow;

        var ph = Text_("Placeholder", fieldHolder, "Paste deck list here (Ctrl+V)…",
            13, new Color(Muted.r, Muted.g, Muted.b, 0.6f), TextAnchor.UpperLeft);
        Stretch(ph.rectTransform, Vector2.zero, Vector2.one, new Vector2(10f, 8f), new Vector2(-10f, -8f));
        ph.fontStyle = FontStyle.Italic;

        var field = fieldHolder.gameObject.AddComponent<InputField>();
        field.textComponent = textComp;
        field.placeholder = ph;
        field.lineType = InputField.LineType.MultiLineNewline;
        field.text = "";
        field.caretColor = Accent; field.customCaretColor = true;
        field.Select();

        var pasteBtn = Panel("Paste", box, new Color32(34, 58, 78, 235));
        pasteBtn.anchorMin = pasteBtn.anchorMax = new Vector2(0f, 0f);
        pasteBtn.pivot = new Vector2(0f, 0f);
        pasteBtn.sizeDelta = new Vector2(170f, 36f);
        pasteBtn.anchoredPosition = new Vector2(20f, 18f);
        Round(pasteBtn); AddBorder(pasteBtn, MenuB, 1.1f);
        var pt = Text_("t", pasteBtn, "PASTE CLIPBOARD", 11, Ink, TextAnchor.MiddleCenter, monoFont);
        pt.fontStyle = FontStyle.Bold;
        Stretch(pt.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        pasteBtn.gameObject.AddComponent<Button>().onClick.AddListener(() =>
        {
            field.text = GUIUtility.systemCopyBuffer ?? "";
        });

        var cancelBtn = Panel("Cancel", box, new Color32(46, 26, 30, 235));
        cancelBtn.anchorMin = cancelBtn.anchorMax = new Vector2(1f, 0f);
        cancelBtn.pivot = new Vector2(1f, 0f);
        cancelBtn.sizeDelta = new Vector2(110f, 36f);
        cancelBtn.anchoredPosition = new Vector2(-142f, 18f);
        Round(cancelBtn); AddBorder(cancelBtn, RedAccent, 1.1f);
        var ct = Text_("t", cancelBtn, "CANCEL", 11, RedAccent, TextAnchor.MiddleCenter, monoFont);
        ct.fontStyle = FontStyle.Bold;
        Stretch(ct.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        cancelBtn.gameObject.AddComponent<Button>().onClick.AddListener(CloseImportModal);

        var goBtn = Panel("Go", box, Accent);
        goBtn.anchorMin = goBtn.anchorMax = new Vector2(1f, 0f);
        goBtn.pivot = new Vector2(1f, 0f);
        goBtn.sizeDelta = new Vector2(110f, 36f);
        goBtn.anchoredPosition = new Vector2(-20f, 18f);
        Round(goBtn);
        var gt = Text_("t", goBtn, "IMPORT", 12, BadgeInk, TextAnchor.MiddleCenter, monoFont);
        gt.fontStyle = FontStyle.Bold;
        Stretch(gt.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        goBtn.gameObject.AddComponent<Button>().onClick.AddListener(() => ApplyImport(field.text));
    }

    private void CloseImportModal()
    {
        if (importOverlay != null) Destroy(importOverlay.gameObject);
        importOverlay = null;
    }

    // Parses `raw` against every known community deck-list style, replaces the
    // deck being edited with what it finds, then re-renders. Unrecognised
    // tokens are skipped and reported in the post-import flash message rather
    // than blocking the whole import.
    private void ApplyImport(string raw)
    {
        if (editing == null) { CloseImportModal(); return; }
        if (string.IsNullOrWhiteSpace(raw)) { CloseImportModal(); return; }

        string newLeaderId = null;
        var counts = new Dictionary<string, int>();
        var unknown = new List<string>();

        foreach (Match m in ImportTokenRegex.Matches(raw))
        {
            int qty = int.Parse(m.Groups["qty"].Value);
            string code = m.Groups["code"].Value.ToUpperInvariant();

            var rec = Card(code);
            if (rec == null)
            {
                // Try again without a trailing alt-art suffix, e.g. "OP09-004-1" → "OP09-004".
                int lastDash = code.LastIndexOf('-');
                int firstDash = code.IndexOf('-');
                if (lastDash > firstDash && firstDash > 0)
                {
                    string stripped = code.Substring(0, lastDash);
                    var strippedRec = Card(stripped);
                    if (strippedRec != null) { rec = strippedRec; code = stripped; }
                }
            }
            if (rec == null) { unknown.Add(code); continue; }

            if ((rec.type ?? "").ToLower() == "leader")
            {
                newLeaderId = code;   // last leader token wins if more than one appears
                continue;
            }
            counts.TryGetValue(code, out int cur);
            counts[code] = cur + qty;
        }

        if (newLeaderId == null && counts.Count == 0)
        {
            Flash("Couldn't find any recognisable cards in that text");
            return;
        }

        if (newLeaderId != null) editing.leaderId = newLeaderId;
        editing.cards.Clear();
        foreach (var kv in counts)
            editing.cards.Add(new DeckEntry { id = kv.Key, count = Mathf.Clamp(kv.Value, 1, MaxCopiesFor(kv.Key)) });

        CloseImportModal();
        Render();

        int total = editing.MainCount();
        string msg = $"Imported {total}/50 cards";
        if (unknown.Count > 0) msg += $" — {unknown.Count} unrecognised skipped";
        Flash(msg);
    }

    // Builds the OPTCGSim-style "NxCODE" text — the format shared by OPTCGSim,
    // EGMan and OnePieceTopDecks, making it the most broadly compatible export
    // target for pasting into other simulators/deck sites.
    private string BuildExportText(DeckData d)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(d.leaderId)) sb.Append("1x").Append(d.leaderId).Append('\n');
        foreach (var e in d.cards.OrderBy(c => c.id, StringComparer.Ordinal))
            sb.Append(e.count).Append('x').Append(e.id).Append('\n');
        return sb.ToString().TrimEnd();
    }

    private void ExportCurrent()
    {
        if (editing == null) return;
        string text = BuildExportText(editing);
        if (string.IsNullOrEmpty(text)) { Flash("Nothing to export yet"); return; }
        GUIUtility.systemCopyBuffer = text;
        Flash("Deck copied to clipboard (OPTCGSim format)");
    }

    private void SaveCurrent()
    {
        if (string.IsNullOrWhiteSpace(editing.name)) editing.name = "Untitled Deck";
        // Saving is allowed even if incomplete (work-in-progress), but warn via validity.
        if (DeckStore.Get(editing.id) == null && !DeckStore.CanAddNew())
        {
            Flash("Deck limit (25) reached");
            return;
        }
        DeckStore.Save(editing.Clone());
        DeckStore.ActiveDeckId = editing.id;
        view = View.Select;
        Render();
    }

    private string flashMsg;
    private float flashUntil;
    private void Flash(string msg)
    {
        flashMsg = msg; flashUntil = Time.unscaledTime + 2.2f;
        if (validityText != null) { validityText.text = "⚠ " + msg; validityText.color = Gold; }
    }

    private void ResetFilters()
    {
        filterText = ""; filterColor = ""; filterType = ""; filterCost = -1;
        colorLock = true; archetypeLock = false;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Card art loading
    // ══════════════════════════════════════════════════════════════════════════

    // Resolve the on-disk art path for a card id (or null if no art exists).
    private static string ArtPath(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        string safe = id.Trim();
        string set = safe.Contains("-") ? safe.Split('-')[0] : "";
        var candidates = new[]
        {
            Path.Combine(Application.dataPath, "StreamingAssets", "Cards", "OfficialById", set, safe + ".png"),
            Path.Combine(Application.dataPath, "StreamingAssets", "Cards", "Official", set, safe + ".png"),
            Path.Combine(Application.dataPath, "StreamingAssets", "Cards", set, safe + ".png"),
            Path.Combine(Application.dataPath, "StreamingAssets", "Cards", safe + ".png"),
        };
        foreach (var p in candidates) if (File.Exists(p)) return p;
        return null;
    }

    // Resolves the pre-generated small thumbnail for a card (see
    // generate_thumbnails.py), falling back to the full-res master via
    // ArtPath() if no thumbnail exists yet for this id (e.g. a newly added set
    // before the script is rerun). The bool tells the caller which one it got,
    // since a fallback decode must be charged the master's decode cost, not
    // the thumbnail's - see MasterDecodeCost/ThumbDecodeCost.
    private static (string path, bool isRealThumb) ResolveThumbPath(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return (null, false);
        string safe = id.Trim();
        string set = safe.Contains("-") ? safe.Split('-')[0] : "";
        string thumbPath = Path.Combine(Application.dataPath, "StreamingAssets", "Cards", "Thumbs", set, safe + ".jpg");
        return File.Exists(thumbPath) ? (thumbPath, true) : (ArtPath(id), false);
    }

    // Vertical framing fraction (0 = top of card) used to slice the deck-row art.
    // Priority: face-data.json (run detect_faces.py once) > edge-density heuristic > 0.22f.
    // Events and stages always centre at 0.34f (no face to find).
    private void LoadFaceData()
    {
        if (_faceMap != null) return;
        _faceMap  = new Dictionary<string, float>();
        _faceMapX = new Dictionary<string, float>();
        _faceMapZ = new Dictionary<string, float>();
        try
        {
            string p = Path.Combine(Application.dataPath, "StreamingAssets", "Cards", "face-data.json");
            if (File.Exists(p))
            {
                var f = JsonUtility.FromJson<FaceMapFile>(File.ReadAllText(p));
                if (f != null && f.ids != null && f.y != null)
                    for (int i = 0; i < f.ids.Length && i < f.y.Length; i++)
                        _faceMap[f.ids[i]] = f.y[i];
                if (f != null && f.ids != null && f.x != null && f.x.Length == f.ids.Length)
                    for (int i = 0; i < f.ids.Length; i++)
                        _faceMapX[f.ids[i]] = f.x[i];
                if (f != null && f.ids != null && f.z != null && f.z.Length == f.ids.Length)
                    for (int i = 0; i < f.ids.Length; i++)
                        _faceMapZ[f.ids[i]] = f.z[i];
            }
        }
        catch (Exception e) { Debug.LogWarning("face-data load failed: " + e.Message); }
    }

    // Vertical eye-line only — used by the deck-row slices.
    private float FaceBand(string id, string type) => FaceCenter(id, type).y;

    // Full face centre (x, y) in card fractions — used by the hex roster to
    // centre the crop on the character's face in both axes.
    private Vector2 FaceCenter(string id, string type)
    {
        string tl = (type ?? "").ToLowerInvariant();
        if (tl == "event" || tl == "stage") return new Vector2(0.5f, 0.34f);

        if (_faceMap != null && _faceMap.TryGetValue(id, out var fy))
        {
            float fx = (_faceMapX != null && _faceMapX.TryGetValue(id, out var mx))
                ? Mathf.Clamp(mx, 0.10f, 0.90f) : 0.5f;
            return new Vector2(fx, Mathf.Clamp(fy, 0.10f, 0.46f));
        }
        if (_faceCenterCache.TryGetValue(id, out var cached)) return cached;

        var result = new Vector2(0.5f, 0.22f);
        try
        {
            string p = ArtPath(id);
            if (p != null)
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (tex.LoadImage(File.ReadAllBytes(p))) result = DetectFaceCenter(tex);
                UnityEngine.Object.Destroy(tex);
            }
        }
        catch { /* keep fallback */ }
        _faceCenterCache[id] = result;
        return result;
    }

    // Per-card hex zoom from detect_faces.py (1 = default 0.44-card-height
    // window). <1 tightens the crop to feature a small, off-centre, or
    // duo-focused face; clamped so a bad value can never zoom past sanity.
    private float FaceZoom(string id)
    {
        if (_faceMapZ != null && _faceMapZ.TryGetValue(id, out var z))
            return Mathf.Clamp(z, 0.60f, 1.0f);
        return 1f;
    }

    // Estimates the face position from warm-toned skin pixels: anchors to the
    // TOPMOST sustained skin band (the head) rather than the global centre of
    // mass, which skin-heavy art (Boa!) drags down to the chest. Returns (x, y)
    // card fractions. OPTCG: top ~62% is illustration; face is in its upper part.
    private Vector2 DetectFaceCenter(Texture2D tex)
    {
        int W = tex.width, H = tex.height;
        if (W < 8 || H < 8) return new Vector2(0.5f, 0.22f);

        Color32[] px = tex.GetPixels32();

        const float ART_TOP = 0.08f, ART_BOT = 0.60f;
        const float SEARCH_TOP = ART_TOP, SEARCH_BOT = ART_TOP + (ART_BOT - ART_TOP) * 0.60f;
        const int ROWS = 80, COLS = 36;
        const float COL_LO = 0.18f, COL_HI = 0.82f;

        var rowSkin = new float[ROWS];
        var rowX    = new float[ROWS];
        var rowFrac = new float[ROWS];
        for (int ri = 0; ri < ROWS; ri++)
        {
            float frac = Mathf.Lerp(SEARCH_TOP, SEARCH_BOT, ri / (float)(ROWS - 1));
            rowFrac[ri] = frac;
            int ty = Mathf.Clamp(Mathf.RoundToInt((1f - frac) * (H - 1)), 0, H - 1);
            int baseIdx = ty * W;
            float skin = 0f, xSum = 0f;
            for (int ci = 0; ci < COLS; ci++)
            {
                float fx = Mathf.Lerp(COL_LO, COL_HI, ci / (float)(COLS - 1));
                int tx = Mathf.Clamp(Mathf.RoundToInt(fx * (W - 1)), 0, W - 1);
                if (IsAnimeSkin(px[baseIdx + tx])) { skin += 1f; xSum += fx; }
            }
            rowSkin[ri] = skin / COLS;
            rowX[ri]    = skin > 0f ? xSum / skin : 0.5f;
        }

        // Topmost sustained skin band = the head.
        int start = -1;
        for (int ri = 0; ri < ROWS - 2; ri++)
            if (rowSkin[ri] > 0.08f && rowSkin[ri + 1] > 0.08f && rowSkin[ri + 2] > 0.08f)
            { start = ri; break; }

        if (start < 0)
        {
            float wy = 0f, wx = 0f, tot = 0f;
            for (int ri = 0; ri < ROWS; ri++)
            { wy += rowFrac[ri] * rowSkin[ri]; wx += rowX[ri] * rowSkin[ri]; tot += rowSkin[ri]; }
            if (tot < 0.4f) return new Vector2(0.5f, 0.22f);
            return new Vector2(Mathf.Clamp(wx / tot, 0.25f, 0.75f),
                               Mathf.Clamp(wy / tot - 0.02f, 0.12f, 0.42f));
        }

        float rowStep  = (SEARCH_BOT - SEARCH_TOP) / (ROWS - 1);
        int bandRows   = Mathf.Max(2, Mathf.RoundToInt(0.07f / rowStep));
        float by = 0f, bx = 0f, bt = 0f;
        for (int ri = start; ri < Mathf.Min(ROWS, start + bandRows); ri++)
        { by += rowFrac[ri] * rowSkin[ri]; bx += rowX[ri] * rowSkin[ri]; bt += rowSkin[ri]; }
        if (bt <= 0.001f) return new Vector2(0.5f, 0.22f);

        return new Vector2(Mathf.Clamp(bx / bt, 0.20f, 0.80f),
                           Mathf.Clamp(by / bt + 0.015f, 0.10f, 0.46f));
    }

    // Broad warm-tone skin test that covers the range of anime One Piece styles.
    // Condition: red dominates, noticeable warm shift from R to B, not oversaturated.
    private static bool IsAnimeSkin(Color32 c)
    {
        int r = c.r, g = c.g, b = c.b;
        int vMax = r > g ? (r > b ? r : b) : (g > b ? g : b);
        if (vMax < 80) return false;                     // too dark
        if (r <= g)    return false;                     // not warm enough
        int rMinusB = r - b;
        return rMinusB > 8 && rMinusB < 165             // warm but not pure orange
            && (g - b) > -20;                           // not magenta/purple
    }

    // ══════════════════════════════════════════════════════════════════════════
    // UI primitives (copied to match the rest of the game pixel-for-pixel)
    // ══════════════════════════════════════════════════════════════════════════
    private RectTransform Panel(string name, Transform parent, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        var img = go.AddComponent<Image>();
        img.color = color;
        return rt;
    }

    private Text Text_(string name, Transform parent, string value, int size, Color color,
        TextAnchor align, Font fontOverride = null)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        var t = go.AddComponent<Text>();
        t.font = fontOverride != null ? fontOverride : font;
        t.text = value; t.fontSize = size; t.color = color; t.alignment = align;
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Truncate;
        return t;
    }

    private void Stretch(RectTransform rt, Vector2 min, Vector2 max, Vector2 offMin, Vector2 offMax)
    {
        rt.anchorMin = min; rt.anchorMax = max; rt.offsetMin = offMin; rt.offsetMax = offMax;
    }

    private void SetPref(RectTransform rt, Vector2 size)
    {
        var le = rt.gameObject.GetComponent<LayoutElement>() ?? rt.gameObject.AddComponent<LayoutElement>();
        if (size.x > 0) { le.preferredWidth = size.x; le.minWidth = size.x; }
        if (size.y > 0) { le.preferredHeight = size.y; le.minHeight = size.y; }
    }

    private RectTransform Row(string name, RectTransform parent, float spacing, TextAnchor align)
    {
        var row = new GameObject(name).AddComponent<RectTransform>();
        row.SetParent(parent, false);
        var hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = spacing; hlg.childAlignment = align;
        hlg.childControlHeight = false; hlg.childControlWidth = false;
        hlg.childForceExpandHeight = false; hlg.childForceExpandWidth = false;
        return row;
    }

    private void ChipToggle(RectTransform parent, string label, string value, bool on,
        UnityEngine.Events.UnityAction<string> action, Color tint, float w = 0f)
    {
        // Selected filter chips highlight in the accent blue; unselected stay grey.
        // (The `tint` argument is kept for call-site compatibility but no longer
        // changes the on-colour — every active criterion reads as the same blue.)
        Color onColor = Accent;
        float width = w > 0 ? w : Mathf.Max(28f, 12f + label.Length * 8f);
        var chip = Panel(label + " Chip", parent,
            on ? new Color(onColor.r, onColor.g, onColor.b, 0.22f) : new Color(1f, 1f, 1f, 0.04f));
        chip.sizeDelta = new Vector2(width, 24f);
        SetPref(chip, new Vector2(width, 24f));
        Round(chip);
        AddBorder(chip, on ? onColor : ZoneBorder, on ? 1.3f : 1f);
        var t = Text_("t", chip, label, 9, on ? onColor : Muted, TextAnchor.MiddleCenter, monoFont);
        t.fontStyle = FontStyle.Bold;
        Stretch(t.rectTransform, Vector2.zero, Vector2.one, new Vector2(3, 0), new Vector2(-3, 0));
        var btn = chip.gameObject.AddComponent<Button>();
        btn.onClick.AddListener(() => action(value));
    }

    // Legacy uGUI InputField with placeholder, themed.
    private RectTransform MakeInput(RectTransform parent, string placeholder, string initial,
        UnityEngine.Events.UnityAction<string> onChanged, UnityEngine.Events.UnityAction<string> onEnd)
    {
        var holder = Panel("Input", parent, new Color32(8, 16, 26, 255));
        Round(holder); AddBorder(holder, MenuB, 1f);

        var textComp = Text_("Text", holder, "", 13, Ink, TextAnchor.MiddleLeft);
        Stretch(textComp.rectTransform, Vector2.zero, Vector2.one, new Vector2(12f, 0f), new Vector2(-10f, 0f));
        textComp.supportRichText = false;

        var ph = Text_("Placeholder", holder, placeholder, 13, new Color(Muted.r, Muted.g, Muted.b, 0.7f), TextAnchor.MiddleLeft);
        Stretch(ph.rectTransform, Vector2.zero, Vector2.one, new Vector2(12f, 0f), new Vector2(-10f, 0f));
        ph.fontStyle = FontStyle.Italic;

        var field = holder.gameObject.AddComponent<InputField>();
        field.textComponent = textComp;
        field.placeholder = ph;
        field.text = initial ?? "";
        field.lineType = InputField.LineType.SingleLine;
        field.caretColor = Accent; field.customCaretColor = true;
        if (onChanged != null) field.onValueChanged.AddListener(s => onChanged(s));
        if (onEnd != null) field.onEndEdit.AddListener(s => onEnd(s));
        return holder;
    }

    // ScrollRect with masked viewport + a thematic scrollbar in a right-side
    // gutter; returns the content RectTransform.
    private RectTransform MakeScroll(RectTransform parent)
    {
        var viewport = Panel("Viewport", parent, new Color(0, 0, 0, 0.001f));
        Stretch(viewport, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-14f, 0f));  // gutter
        viewport.gameObject.AddComponent<RectMask2D>();

        var content = new GameObject("Content").AddComponent<RectTransform>();
        content.SetParent(viewport, false);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot     = new Vector2(0.5f, 1f);
        content.offsetMin = new Vector2(0f, 0f);
        content.offsetMax = new Vector2(0f, 0f);

        var sr = parent.gameObject.AddComponent<ScrollRect>();
        sr.viewport = viewport; sr.content = content;
        sr.horizontal = false; sr.vertical = true;
        sr.movementType = ScrollRect.MovementType.Clamped;
        sr.scrollSensitivity = 26f;

        AddScrollbar(parent, sr);
        return content;
    }

    // Slim cyan-on-navy vertical scrollbar that matches the rest of the UI.
    // Sits in the gutter to the right of the viewport and auto-hides when the
    // content fits.
    private void AddScrollbar(RectTransform area, ScrollRect sr)
    {
        var track = Panel("Scrollbar", area, new Color(120f / 255f, 180f / 255f, 220f / 255f, 0.12f));
        track.anchorMin = new Vector2(1f, 0f);
        track.anchorMax = new Vector2(1f, 1f);
        track.pivot     = new Vector2(1f, 0.5f);
        track.sizeDelta = new Vector2(8f, -4f);
        track.anchoredPosition = new Vector2(-2f, 0f);
        Round(track);

        var slide = Panel("Sliding Area", track, new Color(0, 0, 0, 0));
        Stretch(slide, Vector2.zero, Vector2.one, new Vector2(1f, 1f), new Vector2(-1f, -1f));

        var handle = Panel("Handle", slide, new Color(Accent.r, Accent.g, Accent.b, 0.85f));
        Stretch(handle, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Round(handle);

        var sb = track.gameObject.AddComponent<Scrollbar>();
        sb.direction = Scrollbar.Direction.BottomToTop;
        sb.handleRect = handle;
        sb.targetGraphic = handle.GetComponent<Image>();

        sr.verticalScrollbar = sb;
        sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
    }

    private void Round(RectTransform rt)
    {
        var img = rt.GetComponent<Image>(); if (img == null) return;
        img.sprite = RoundSprite(); img.type = Image.Type.Sliced;
    }
    private void RoundBig(RectTransform rt)
    {
        var img = rt.GetComponent<Image>(); if (img == null) return;
        img.sprite = RoundSpriteBig(); img.type = Image.Type.Sliced;
    }
    private void RoundCircle(RectTransform rt)
    {
        var img = rt.GetComponent<Image>(); if (img == null) return;
        img.sprite = CircleSprite(); img.type = Image.Type.Simple;
    }

    private void AddBorder(RectTransform parent, Color color, float thickness)
    {
        if (parent == null || color.a <= 0f || thickness <= 0f) return;
        var go = new GameObject("Border");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        var img = go.AddComponent<Image>();
        img.sprite = BorderSprite(thickness); img.color = color;
        img.raycastTarget = false; img.type = Image.Type.Sliced;
        Stretch(rt, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
    }

    // Flat-top regular hexagon sprite, antialiased via SDF.
    // Texture is sized at the exact flat-top aspect ratio (W : sqrt(3)/2*W)
    // so that when Unity stretches it to the hex cell rect (HW × HH) neither
    // axis is scaled differently — no distortion.
    private Sprite HexSprite()
    {
        if (_hexSprite != null) return _hexSprite;
        const int W = 256;
        int H = Mathf.RoundToInt(W * Mathf.Sqrt(3f) * 0.5f);  // 222 — matches HH/HW ratio
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear; tex.wrapMode = TextureWrapMode.Clamp;
        var px = new Color32[W * H];
        float R  = W * 0.5f;                   // vertex-to-vertex radius = 128
        float h  = Mathf.Sqrt(3f) * 0.5f * R; // apothem ≈ 110.85 — fills height exactly
        float cx = W * 0.5f, cy = H * 0.5f;   // 128, 111
        // diagLen = sqrt(h²+(R/2)²) = sqrt(3R²/4+R²/4) = R — a flat-top hex identity
        float diagLen = R;
        for (int py = 0; py < H; py++)
        {
            float dy = Mathf.Abs(py - cy);
            for (int pxX = 0; pxX < W; pxX++)
            {
                float dx  = Mathf.Abs(pxX - cx);
                float d1  = h - dy;                                    // top/bottom edges
                float d2  = (h * R - h * dx - R * 0.5f * dy) / diagLen; // diagonal edges
                float d   = Mathf.Min(d1, d2);
                byte  ab  = (byte)Mathf.RoundToInt(Mathf.Clamp01(d + 0.5f) * 255f);
                px[py * W + pxX] = new Color32(255, 255, 255, ab);
            }
        }
        // Keep CPU-readable: roster cells use this sprite with
        // alphaHitTestMinimumThreshold, which samples texture alpha per raycast.
        tex.SetPixels32(px); tex.Apply(false, false);
        _hexSprite = Sprite.Create(tex, new Rect(0, 0, W, H),
            new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        return _hexSprite;
    }

    // Brushed-metal hex (same geometry as HexSprite): six specular lobes, a broad
    // chrome sweep, top-light, brushed streaks, and a polished outer lip. Greyscale
    // so the Image tint colours it (Steel → brushed steel, Gold → polished gold).
    private Sprite HexMetalSprite()
    {
        if (_hexMetalSprite != null) return _hexMetalSprite;
        const int W = 256;
        int H = Mathf.RoundToInt(W * Mathf.Sqrt(3f) * 0.5f);
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear; tex.wrapMode = TextureWrapMode.Clamp;
        var px = new Color32[W * H];
        float R  = W * 0.5f;
        float h  = Mathf.Sqrt(3f) * 0.5f * R;
        float cx = W * 0.5f, cy = H * 0.5f;
        float diagLen = R;
        for (int py = 0; py < H; py++)
        {
            float dyAbs = Mathf.Abs(py - cy);
            for (int pxX = 0; pxX < W; pxX++)
            {
                float dxAbs = Mathf.Abs(pxX - cx);
                float d1 = h - dyAbs;
                float d2 = (h * R - h * dxAbs - R * 0.5f * dyAbs) / diagLen;
                float d  = Mathf.Min(d1, d2);
                byte  ab = (byte)Mathf.RoundToInt(Mathf.Clamp01(d + 0.5f) * 255f);
                if (ab == 0) { px[py * W + pxX] = new Color32(0, 0, 0, 0); continue; }
                float ang = Mathf.Atan2(py - cy, pxX - cx);
                float lobes = Mathf.Pow(Mathf.Abs(Mathf.Cos(ang * 3f + 0.65f)), 2.6f);
                float sweep = Mathf.Pow(Mathf.Abs(Mathf.Cos(ang + 2.30f)), 5f);
                float top = Mathf.Clamp01((py - cy) / h * 0.5f + 0.5f);
                float sn = Mathf.Sin(ang * 97.13f + d * 0.35f) * 43758.5453f;
                float n  = sn - Mathf.Floor(sn);
                float lip    = Mathf.Exp(-d / 2.2f);
                float groove = Mathf.Exp(-Mathf.Abs(d - 5f) / 2.0f);
                float v = 0.36f + 0.30f * lobes + 0.20f * sweep + 0.14f * top
                        + 0.08f * (n - 0.5f) + 0.32f * lip - 0.10f * groove;
                byte g8 = (byte)Mathf.RoundToInt(Mathf.Clamp01(v) * 255f);
                px[py * W + pxX] = new Color32(g8, g8, g8, ab);
            }
        }
        tex.SetPixels32(px); tex.Apply(false, true);
        _hexMetalSprite = Sprite.Create(tex, new Rect(0, 0, W, H),
            new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        return _hexMetalSprite;
    }

    // Full-screen vertical blend baked as RGB (MatBottom at the bottom →
    // MatTop at the top), LINEAR so there's no perceived seam. Used as the menu
    // background instead of a hard mid-screen split.
    private Sprite BgGradientSprite()
    {
        if (_bgGrad != null) return _bgGrad;
        const int W = 4, H = 256;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear; tex.wrapMode = TextureWrapMode.Clamp;
        var px = new Color32[W * H];
        for (int y = 0; y < H; y++)   // y=0 is the bottom row in sprite space
        {
            Color32 c = Color.Lerp(MatBottom, MatTop, y / (float)(H - 1));
            for (int x = 0; x < W; x++) px[y * W + x] = c;
        }
        tex.SetPixels32(px); tex.Apply(false, true);
        _bgGrad = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect);
        return _bgGrad;
    }

    // Horizontal gradient: opaque white at the left, transparent at the right —
    // tinted with the row colour to fade card art into the deck row.
    private Sprite GetHGradientSprite()
    {
        if (_hGradient != null) return _hGradient;
        const int W = 64, H = 4;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear; tex.wrapMode = TextureWrapMode.Clamp;
        var px = new Color32[W * H];
        for (int x = 0; x < W; x++)
        {
            float t = x / (float)(W - 1);                 // 0 left → 1 right
            float a = 1f - Mathf.SmoothStep(0f, 1f, t);   // opaque left → transparent right
            byte ab = (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f);
            for (int y = 0; y < H; y++) px[y * W + x] = new Color32(255, 255, 255, ab);
        }
        tex.SetPixels32(px); tex.Apply(false, true);
        _hGradient = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect);
        return _hGradient;
    }

    private Sprite GetVGradientSprite()
    {
        if (_vGradient != null) return _vGradient;
        const int W = 4, H = 64;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear; tex.wrapMode = TextureWrapMode.Clamp;
        var px = new Color32[W * H];
        for (int y = 0; y < H; y++)
        {
            float t = y / (float)(H - 1);                 // 0 bottom → 1 top (texture space)
            float a = Mathf.SmoothStep(0f, 1f, t);        // opaque at top → transparent toward bottom
            byte ab = (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f);
            for (int x = 0; x < W; x++) px[y * W + x] = new Color32(255, 255, 255, ab);
        }
        tex.SetPixels32(px); tex.Apply(false, true);
        _vGradient = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect);
        return _vGradient;
    }

    private Sprite RoundSprite()
    {
        if (_round != null) return _round;
        _round = MakeRounded(24, 5f); return _round;
    }
    private Sprite RoundSpriteBig()
    {
        if (_roundBig != null) return _roundBig;
        _roundBig = MakeRounded(44, 12f); return _roundBig;
    }
    private Sprite MakeRounded(int S, float r)
    {
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear; tex.wrapMode = TextureWrapMode.Clamp;
        var px = new Color32[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float cx = Mathf.Min(x + 0.5f, S - x - 0.5f);
                float cy = Mathf.Min(y + 0.5f, S - y - 0.5f);
                float dx = Mathf.Max(0f, r - cx), dy = Mathf.Max(0f, r - cy);
                float a = Mathf.Clamp01(r + 0.75f - Mathf.Sqrt(dx * dx + dy * dy));
                px[y * S + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        tex.SetPixels32(px); tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect, new Vector4(r, r, r, r));
    }

    private Sprite CircleSprite()
    {
        if (_circle != null) return _circle;
        const int S = 48; float rad = S / 2f;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear; tex.wrapMode = TextureWrapMode.Clamp;
        var px = new Color32[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = x + 0.5f - rad, dy = y + 0.5f - rad;
                float a = Mathf.Clamp01(rad - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                px[y * S + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        tex.SetPixels32(px); tex.Apply(false, true);
        _circle = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
        return _circle;
    }

    private Sprite BorderSprite(float thickness)
    {
        int key = Mathf.Clamp(Mathf.RoundToInt(thickness * 10f), 1, 80);
        if (_borders.TryGetValue(key, out var cached)) return cached;
        const int W = 64, H = 90; const float R = 6.5f;
        float th = Mathf.Clamp(key / 10f, 0.75f, 8f);
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear; tex.wrapMode = TextureWrapMode.Clamp;
        var px = new Color32[W * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float outer = RR(x + 0.5f, y + 0.5f, W, H, R);
                float inner = RR(x + 0.5f - th, y + 0.5f - th, W - th * 2f, H - th * 2f, Mathf.Max(0f, R - th));
                float a = Mathf.Clamp01(outer * (1f - inner));
                px[y * W + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        tex.SetPixels32(px); tex.Apply(false, true);
        var sprite = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect, new Vector4(R, R, R, R));
        _borders[key] = sprite;
        return sprite;
    }

    private static float RR(float x, float y, float w, float h, float r)
    {
        if (w <= 0f || h <= 0f) return 0f;
        float px = Mathf.Min(x, w - x); float py = Mathf.Min(y, h - y);
        float dx = Mathf.Max(0f, r - px); float dy = Mathf.Max(0f, r - py);
        return Mathf.Clamp01(r + 0.75f - Mathf.Sqrt(dx * dx + dy * dy));
    }
}
