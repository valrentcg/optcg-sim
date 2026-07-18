// One Piece TCG - SANDBOX mode.
// A free-form testing board: boot a clean active match, then directly edit any zone for
// either seat (spawn cards into hand/board/trash/life/deck, move/delete/rest them, attach
// DON!!, buff power, set Life, force the phase/active seat) and play out real mechanics with
// the ordinary engine command flow (attacks, effects, triggers, counters all still work).
// Undo/redo + save/load board snapshots are provided via Newtonsoft JSON clones of GameState.
//
// Design: the editor MUTATES GameState in place, bypassing rules legality, then re-renders.
// Setting up a board this way and then using the normal interactive board to "try it out"
// is exactly the sandbox use-case ("I wonder how X works in Y situation"). This file is a
// partial of GameManager so it can reach the private view helpers (PanelObject/TextObject/…)
// and the live `state` without widening their visibility.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Newtonsoft.Json;
using OnePieceTcg.Engine;

public partial class GameManager
{
    // ── Entry ────────────────────────────────────────────────────────────────
    // Set by the menu before EnsureBoard(); Start() branches to NewSandbox() when true.
    public static bool PendingSandbox;
    public static string PendingSandboxSouthDeckId;   // optional; falls back to the ST01/ST02 starters
    public static string PendingSandboxNorthDeckId;

    /// <summary>Enters a blank sandbox board. Deck ids are optional (used only to stock the
    /// two decks with real cards so you can draw/spawn from them); pass null for the starters.</summary>
    public static void LaunchSandbox(string southDeckId = null, string northDeckId = null)
    {
        PendingSandbox = true;
        PendingSandboxSouthDeckId = southDeckId;
        PendingSandboxNorthDeckId = northDeckId;
        EnsureBoard();
    }

    // ── Sandbox runtime state ────────────────────────────────────────────────
    private bool isSandbox;
    private string sandboxViewSeat = "south";      // which seat renders at the BOTTOM (drives BottomSeat)
    private bool sandboxPanelOpen = false;          // right-edge editor panel — collapsed by default; the tab shine invites a click
    private bool sandboxEditMode = true;            // clicking a card selects it for editing instead of normal play
    private CardInstance sandboxSelectedCard;       // target of the SELECTED-card ops section
    private int sandboxSpawnCounter;                // makes spawned InstanceIds unique

    // Spawn target: where the card picker drops the chosen card.
    private string sandboxSpawnSeat = "bottom";     // "bottom" | "top" (resolved via BottomSeat/TopSeat)
    private string sandboxSpawnZone = "character";  // character|hand|trash|life|deck|stage|leader
    private bool sandboxPickerOpen;
    private string sandboxPickerSearch = "";
    private InputField sandboxPickerField;

    // Persisted SANDBOX-tab position along the screen rim (draggable; see SandboxTabDrag). Edge +
    // fraction along that edge, saved to PlayerPrefs so it survives leaving/re-entering sandbox.
    private static string sandboxTabEdge = "right";   // right | left | top | bottom
    private static float sandboxTabFrac = 0.5f;       // 0..1 along the edge
    private static bool sandboxTabPrefsLoaded;
    private const string SandboxTabEdgeKey = "sandbox.tab.edge";
    private const string SandboxTabFracKey = "sandbox.tab.frac";

    // Restore-from-code paste overlay (in-sandbox entry point to the replay-position restore flow).
    private bool sandboxRestoreOpen;
    private string sandboxRestoreDraft = "";

    // Undo/redo hold in-memory GameState deep clones. GameClone (the engine's own bot-search clone)
    // is faithful to every mutated field incl. CardInstance.PlayedOnTurn — which a JSON serializer
    // silently drops because it's [NonSerialized]. So undo uses GameClone, and only disk save/load
    // uses JSON (where losing PlayedOnTurn is benign — null just means "not summoning-sick", the
    // sandbox default).
    private readonly List<GameState> sandboxUndoStack = new List<GameState>();
    private readonly List<GameState> sandboxRedoStack = new List<GameState>();
    private const int SandboxUndoCap = 60;
    private const int SandboxSlotCount = 4;

    private string SpawnSeatResolved => sandboxSpawnSeat == "top" ? TopSeat : BottomSeat;

    private static GameState SandboxClone(GameState s) => OnePieceTcg.Engine.Bot.Search.GameClone.Clone(s);

    // Reference-preserving JSON for DISK snapshots so a saved board rebuilds shared CardInstance
    // references (e.g. a life card also pointed at by Battle.RevealedLife) as one object.
    private static readonly JsonSerializerSettings SbJson = new JsonSerializerSettings
    {
        PreserveReferencesHandling = PreserveReferencesHandling.Objects,
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
        Formatting = Formatting.None,
    };

    // ── Boot a clean board ───────────────────────────────────────────────────
    private void NewSandbox()
    {
        isSandbox = true;
        isReplayMode = false;
        loadedReplay = null;
        aiSeat = null;
        sandboxViewSeat = "south";
        sandboxSelectedCard = null;
        sandboxSpawnCounter = 0;
        sandboxUndoStack.Clear();
        sandboxRedoStack.Clear();

        var config = new MatchConfig { Seed = System.Guid.NewGuid().ToString("N") };
        var southDef = string.IsNullOrEmpty(PendingSandboxSouthDeckId) ? null : BuildDeckDef(DeckStore.Get(PendingSandboxSouthDeckId));
        var northDef = string.IsNullOrEmpty(PendingSandboxNorthDeckId) ? null : BuildDeckDef(DeckStore.Get(PendingSandboxNorthDeckId));
        if (southDef != null) config.SouthDeckDef = southDef;
        if (northDef != null) config.NorthDeckDef = northDef;
        currentMatchConfig = config;

        southDisplayName = "South (P1)";
        northDisplayName = "North (P2)";

        // Fresh-match animation bookkeeping (mirror NewMatch) so the first render doesn't
        // fling ghosts for cards that were never on screen.
        mulliganAnimShownKey = null;
        mulliganRedrawSeat = null;
        handDealAnimating = false;
        mulliganDealAnimating = 0;
        lastCardPoses.Clear();
        suppressMoveAnim.Clear();

        state = GameEngine.CreateMatch(config);

        // Skip coin-flip / mulligan / life-deal entirely: jump straight to a live main phase
        // with an empty board on both sides. Leaders + decks are kept; everything else blank.
        state.Status = "active";
        state.Phase = "main";
        state.TurnNumber = 1;
        state.FirstPlayer = "south";
        state.ActiveSeat = "south";
        state.Battle = null;
        state.PendingEffects.Clear();
        state.ActiveChoice = null;
        foreach (var seat in new[] { "south", "north" })
        {
            var p = state.Players[seat];
            foreach (var c in p.Hand) c.Zone = "deck";
            p.Deck.InsertRange(0, p.Hand);
            p.Hand.Clear();
            foreach (var c in p.Life) c.Zone = "deck";
            p.Deck.AddRange(p.Life);
            p.Life.Clear();
            p.CharacterArea = new List<CardInstance> { null, null, null, null, null };
            p.Stage = null;
            p.Trash.Clear();
            p.CostArea.Clear();
            p.MulliganDecided = true;
            p.MulliganUsed = false;
            // The engine forbids attacking while TurnsStarted <= 1 (the real "no battle on your
            // first turn" rule). NewSandbox never runs a start-of-turn, so bump both seats past it —
            // otherwise every attack on a freshly-booted sandbox board is silently rejected.
            p.TurnsStarted = 2;
        }

        selectedId = null;
        selectedSeat = null;
        previewLockCard = null;
        ClearDonSelection(false);
        finishedResultText = null;
        matchResultHidden = false;
        SandboxLog("Sandbox ready — blank board, both seats yours.");
        Render();
    }

    private void SandboxLog(string msg)
    {
        if (state == null) return;
        state.EventLog.Add(new LogEntry { Actor = "sandbox", Message = msg, Turn = state.TurnNumber, Sequence = state.LogSequence++ });
    }

    // ── Undo / redo / snapshots ──────────────────────────────────────────────

    // Snapshot the current state onto the undo stack (and clear redo). Called before every
    // destructive edit and — via Dispatch — before every engine command in sandbox.
    private void PushUndo()
    {
        if (!isSandbox || state == null) return;
        sandboxUndoStack.Add(SandboxClone(state));
        if (sandboxUndoStack.Count > SandboxUndoCap) sandboxUndoStack.RemoveAt(0);
        sandboxRedoStack.Clear();
    }

    // Make `g` the live state and clear any selection/preview that pointed at the old graph.
    private void SandboxApplyState(GameState g)
    {
        state = g;
        sandboxSelectedCard = null;
        previewLockCard = null;
        ClearDonSelection(false);
        Render();
    }

    private void SandboxUndo()
    {
        if (sandboxUndoStack.Count == 0) return;
        sandboxRedoStack.Add(SandboxClone(state));
        var g = sandboxUndoStack[sandboxUndoStack.Count - 1];
        sandboxUndoStack.RemoveAt(sandboxUndoStack.Count - 1);
        SandboxApplyState(g);
    }

    private void SandboxRedo()
    {
        if (sandboxRedoStack.Count == 0) return;
        sandboxUndoStack.Add(SandboxClone(state));
        var g = sandboxRedoStack[sandboxRedoStack.Count - 1];
        sandboxRedoStack.RemoveAt(sandboxRedoStack.Count - 1);
        SandboxApplyState(g);
    }

    private static string SandboxSlotDir => Path.Combine(Application.persistentDataPath, "sandbox_snapshots");
    private static string SandboxSlotPath(int i) => Path.Combine(SandboxSlotDir, $"slot{i}.json");
    private bool SandboxSlotExists(int i) => File.Exists(SandboxSlotPath(i));

    private void SandboxSaveSlot(int i)
    {
        try
        {
            Directory.CreateDirectory(SandboxSlotDir);
            File.WriteAllText(SandboxSlotPath(i), JsonConvert.SerializeObject(state, SbJson));
            SandboxLog($"Saved board to slot {i}.");
        }
        catch (System.Exception ex) { SandboxLog($"Save failed: {ex.Message}"); }
        Render();
    }

    private void SandboxLoadSlot(int i)
    {
        if (!SandboxSlotExists(i)) return;
        try
        {
            PushUndo();
            var g = JsonConvert.DeserializeObject<GameState>(File.ReadAllText(SandboxSlotPath(i)), SbJson);
            SandboxApplyState(g);
            SandboxLog($"Loaded board from slot {i}.");
        }
        catch (System.Exception ex) { SandboxLog($"Load failed: {ex.Message}"); Render(); }
    }

    // ── State-edit primitives (mutate + re-render) ───────────────────────────
    // Each destructive primitive PushUndo()s first so it can be reverted.

    private CardInstance SandboxMakeInstance(string cardId, string owner, string zone)
    {
        return new CardInstance
        {
            InstanceId = $"{owner}-{cardId}-sb{++sandboxSpawnCounter}",
            CardId = cardId,
            Owner = owner,
            Zone = zone,
            Rested = false,
            // null (not the current turn) → not summoning-sick, so a spawned Character can attack
            // immediately. That's the useful sandbox default; rest it manually to test the sick case.
            PlayedOnTurn = null,
        };
    }

    // Detach a card from wherever it currently lives (leaves char slots as null). Returns false
    // for the leader (leaders are replaced, never removed). Does NOT touch attached DON.
    private bool SandboxDetach(CardInstance card)
    {
        if (card == null) return false;
        foreach (var p in state.Players.Values)
        {
            if (p.Leader == card) return false;
            if (p.Hand.Remove(card)) return true;
            if (p.Trash.Remove(card)) return true;
            if (p.Deck.Remove(card)) return true;
            if (p.Life.Remove(card)) return true;
            if (p.Stage == card) { p.Stage = null; return true; }
            int idx = p.CharacterArea.IndexOf(card);
            if (idx >= 0) { p.CharacterArea[idx] = null; return true; }
        }
        return false;
    }

    // Place an existing instance into a zone for the given seat. Handles the 5-slot character
    // area (first empty slot; ignored if full), stage/leader replacement, and list zones.
    private bool SandboxPlace(CardInstance card, string seat, string zone)
    {
        var p = state.Players[seat];
        card.Owner = seat;
        switch (zone)
        {
            case "character":
                int slot = p.CharacterArea.FindIndex(c => c == null);
                if (slot < 0) { SandboxLog("Character area is full (5)."); return false; }
                p.CharacterArea[slot] = card; card.Zone = "character"; return true;
            case "stage":
                if (p.Stage != null) SandboxDetach(p.Stage);
                p.Stage = card; card.Zone = "stage"; return true;
            case "leader":
                // Replacing the leader: the old leader instance is simply dropped.
                p.Leader = card; card.Zone = "leader"; card.Rested = false; return true;
            case "hand": p.Hand.Add(card); card.Zone = "hand"; return true;
            case "trash": p.Trash.Add(card); card.Zone = "trash"; return true;
            case "life": card.FaceUp = false; p.Life.Add(card); card.Zone = "life"; return true;
            case "deck": p.Deck.Insert(0, card); card.Zone = "deck"; return true;
            default: return false;
        }
    }

    private void SandboxSpawn(string cardId, string seat, string zone)
    {
        if (string.IsNullOrEmpty(cardId)) return;
        PushUndo();
        var inst = SandboxMakeInstance(cardId, seat, zone);
        if (SandboxPlace(inst, seat, zone))
        {
            var def = CardData.GetCard(cardId);
            SandboxLog($"Spawned {(def != null ? def.Name : cardId)} → {seat} {zone}.");
        }
        Render();
    }

    private void SandboxMoveSelected(string zone)
    {
        if (sandboxSelectedCard == null) return;
        var card = sandboxSelectedCard;
        string seat = card.Owner;
        if (state.Players[seat].Leader == card && zone != "leader")
        {
            SandboxLog("Can't move the Leader — spawn a new leader to replace it.");
            Render();
            return;
        }
        PushUndo();
        SandboxDetach(card);
        SandboxPlace(card, seat, zone);
        Render();
    }

    private void SandboxDeleteSelected()
    {
        if (sandboxSelectedCard == null) return;
        PushUndo();
        if (SandboxDetach(sandboxSelectedCard))
        {
            sandboxSelectedCard.AttachedDonIds.Clear();
            sandboxSelectedCard = null;
        }
        else SandboxLog("Can't delete the Leader.");
        Render();
    }

    private void SandboxDuplicateSelected()
    {
        var card = sandboxSelectedCard;
        if (card == null || state.Players[card.Owner].Leader == card) return;
        PushUndo();
        var copy = SandboxMakeInstance(card.CardId, card.Owner, card.Zone);
        copy.Rested = card.Rested;
        copy.FaceUp = card.FaceUp;
        SandboxPlace(copy, card.Owner, card.Zone);
        Render();
    }

    private void SandboxToggleRestSelected()
    {
        if (sandboxSelectedCard == null) return;
        PushUndo();
        sandboxSelectedCard.Rested = !sandboxSelectedCard.Rested;
        Render();
    }

    private void SandboxToggleFaceUpSelected()
    {
        if (sandboxSelectedCard == null) return;
        PushUndo();
        sandboxSelectedCard.FaceUp = !sandboxSelectedCard.FaceUp;
        Render();
    }

    private bool SandboxHasKeywordGrant(CardInstance card, string keyword) =>
        card != null && state.ActiveModifiers.Any(m => m.TargetInstanceId == card.InstanceId
            && m.ModifierType == "keyword" && string.Equals(m.Keyword, keyword, System.StringComparison.OrdinalIgnoreCase));

    // Toggle a permanent keyword grant on a card (Rush/Blocker/Double Attack/Banish) via the same
    // ActiveModifiers channel the engine's temporary grants use, so all keyword checks pick it up.
    private void SandboxToggleKeyword(CardInstance card, string keyword)
    {
        if (card == null) return;
        PushUndo();
        int removed = state.ActiveModifiers.RemoveAll(m => m.TargetInstanceId == card.InstanceId
            && m.ModifierType == "keyword" && string.Equals(m.Keyword, keyword, System.StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
            state.ActiveModifiers.Add(new CardModifier
            {
                SourceInstanceId = card.InstanceId,
                TargetInstanceId = card.InstanceId,
                ModifierType = "keyword",
                Keyword = keyword,
                Duration = "permanent",
                OwnerSeat = card.Owner,
            });
        Render();
    }

    // Attach/detach a DON!! to a card (+1000 power each on that card's controller's turn — see
    // GameEngine.GetPower). Fabricates a DON id; sandbox doesn't tie it to the cost area.
    private void SandboxAttachDon(CardInstance card, int delta)
    {
        if (card == null) return;
        PushUndo();
        if (delta > 0)
        {
            var p = state.Players[card.Owner];
            p.DonInstanceCounter += 1;
            card.AttachedDonIds.Add($"{card.Owner}-DON-att{p.DonInstanceCounter}");
        }
        else if (card.AttachedDonIds.Count > 0)
        {
            card.AttachedDonIds.RemoveAt(card.AttachedDonIds.Count - 1);
        }
        Render();
    }

    // Turn-scoped power buff via TemporaryPowerBonus (cleared at the owner's next Refresh Phase,
    // same as any "this turn" buff). The on-card power number reflects it immediately.
    private void SandboxPowerBuff(CardInstance card, int delta)
    {
        if (card == null) return;
        PushUndo();
        state.TemporaryPowerBonus.TryGetValue(card.InstanceId, out var cur);
        int next = cur + delta;
        if (next == 0) state.TemporaryPowerBonus.Remove(card.InstanceId);
        else state.TemporaryPowerBonus[card.InstanceId] = next;
        Render();
    }

    private void SandboxAddDon(string seat, int count, bool rested)
    {
        PushUndo();
        var p = state.Players[seat];
        for (int i = 0; i < count; i++)
        {
            p.DonInstanceCounter += 1;
            p.CostArea.Add(new DonInstance { InstanceId = $"{seat}-DON-sb{p.DonInstanceCounter}", Rested = rested });
        }
        Render();
    }

    private void SandboxRemoveDon(string seat, int count)
    {
        PushUndo();
        var p = state.Players[seat];
        for (int i = 0; i < count && p.CostArea.Count > 0; i++)
            p.CostArea.RemoveAt(p.CostArea.Count - 1);
        Render();
    }

    private void SandboxSetAllDonRested(string seat, bool rested)
    {
        PushUndo();
        foreach (var d in state.Players[seat].CostArea) d.Rested = rested;
        Render();
    }

    // Move the top of deck into Life (face-down); if the deck is empty, nothing to add.
    private void SandboxAddLifeFromDeck(string seat, int count)
    {
        PushUndo();
        var p = state.Players[seat];
        int added = 0;
        for (int i = 0; i < count && p.Deck.Count > 0; i++)
        {
            var top = p.Deck[0];
            p.Deck.RemoveAt(0);
            top.Zone = "life"; top.FaceUp = false;
            p.Life.Add(top);
            added++;
        }
        if (added == 0) SandboxLog("Deck empty — pick a card via Spawn → Life instead.");
        Render();
    }

    private void SandboxRemoveLife(string seat)
    {
        var p = state.Players[seat];
        if (p.Life.Count == 0) { Render(); return; }
        PushUndo();
        var bottom = p.Life[p.Life.Count - 1];
        p.Life.RemoveAt(p.Life.Count - 1);
        bottom.Zone = "trash"; bottom.FaceUp = false;
        p.Trash.Add(bottom);
        Render();
    }

    private void SandboxSetPhase(string phase)
    {
        PushUndo();
        state.Phase = phase;
        // Leaving the battle phase with a live attack would strand the battle UI.
        if (phase != "battle") state.Battle = null;
        Render();
    }

    private void SandboxSetActiveSeat(string seat)
    {
        PushUndo();
        state.ActiveSeat = seat;
        state.Battle = null;
        Render();
    }

    private void SandboxBumpTurn()
    {
        PushUndo();
        state.TurnNumber++;
        Render();
    }

    private void SandboxClearBoard(string seat)
    {
        PushUndo();
        var p = state.Players[seat];
        for (int i = 0; i < p.CharacterArea.Count; i++) p.CharacterArea[i] = null;
        p.Stage = null;
        state.Battle = null;
        Render();
    }

    private void SandboxClearAll()
    {
        PushUndo();
        foreach (var seat in new[] { "south", "north" })
        {
            var p = state.Players[seat];
            p.Hand.Clear();
            p.Trash.Clear();
            p.Life.Clear();
            p.CostArea.Clear();
            for (int i = 0; i < p.CharacterArea.Count; i++) p.CharacterArea[i] = null;
            p.Stage = null;
        }
        state.Battle = null;
        state.PendingEffects.Clear();
        sandboxSelectedCard = null;
        SandboxLog("Cleared everything (hands, boards, life, trash, DON).");
        Render();
    }

    private void SandboxDrawCards(string seat, int count)
    {
        PushUndo();
        var p = state.Players[seat];
        for (int i = 0; i < count && p.Deck.Count > 0; i++)
        {
            var top = p.Deck[0];
            p.Deck.RemoveAt(0);
            top.Zone = "hand";
            p.Hand.Add(top);
        }
        Render();
    }

    // ── Editor UI ────────────────────────────────────────────────────────────
    private static readonly Color SbAccent = new Color32(255, 176, 66, 255);   // warm amber, distinct from the cyan HUD
    private static readonly Color SbPanelBg = new Color32(18, 16, 26, 245);
    private static readonly Color SbBtnBg = new Color32(40, 34, 54, 235);
    private static readonly Color SbBtnOn = new Color32(255, 176, 66, 255);
    private static readonly Color SbBtnDim = new Color32(30, 28, 38, 180);
    private static readonly Color SbBorder = new Color32(90, 80, 110, 120);

    // Sandbox UI lives in its own full-screen container kept as the LAST child of the canvas, so it
    // always renders above boardRoot AND the sideRoot/leftRoot HUD columns (which are later canvas
    // siblings than boardRoot). Cleared and rebuilt every Render, like the HUD roots.
    private RectTransform sandboxRoot;

    private void DrawSandbox()
    {
        if (state == null) return;
        if (sandboxRoot == null)
        {
            sandboxRoot = PanelObject("Sandbox Root", canvas.transform, new Color(0, 0, 0, 0));
            Stretch(sandboxRoot, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            sandboxRoot.GetComponent<Image>().raycastTarget = false;   // only the buttons/panels catch clicks
        }
        Clear(sandboxRoot);
        sandboxRoot.SetAsLastSibling();
        DrawSandboxTab();
        if (sandboxPanelOpen) DrawSandboxPanel();
        if (sandboxPickerOpen) DrawSandboxPicker();
        if (sandboxRestoreOpen) DrawSandboxRestore();
    }

    // Draggable vertical tab pinned to a screen rim: click toggles the editor panel, drag repositions
    // it along the nearest edge (position persisted). While collapsed it wears a soft hugging glow.
    private void DrawSandboxTab()
    {
        if (!sandboxTabPrefsLoaded)
        {
            sandboxTabEdge = PlayerPrefs.GetString(SandboxTabEdgeKey, "right");
            sandboxTabFrac = Mathf.Clamp01(PlayerPrefs.GetFloat(SandboxTabFracKey, 0.5f));
            sandboxTabPrefsLoaded = true;
        }

        bool horizontal = sandboxTabEdge == "top" || sandboxTabEdge == "bottom";

        var tab = PanelObject("Sandbox Tab", sandboxRoot, sandboxPanelOpen ? (Color)SbAccent : SbBtnBg);
        PositionSandboxTab(tab);
        Round(tab);
        AddRoundedCardBorder(tab, SbAccent, 1.2f);
        // On the top/bottom rim the tab lies flat with horizontal text; on the sides it's a vertical bar.
        var t = TextObject("Sandbox Tab Text", tab, horizontal ? "SANDBOX" : "S\nA\nN\nD\nB\nO\nX", 9,
            sandboxPanelOpen ? (Color)BadgeInk : SbAccent, TextAnchor.MiddleCenter, monoFont);
        t.fontStyle = FontStyle.Bold;
        t.raycastTarget = false;
        Stretch(t.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var btn = tab.gameObject.AddComponent<Button>();
        btn.onClick.AddListener(() => { sandboxPanelOpen = !sandboxPanelOpen; Render(); });

        // While collapsed, the same GOLD glow we use around cards hugs the tab so it reads as a live,
        // interactive control.
        if (!sandboxPanelOpen) AddMysticalCardOutline(tab, true);

        // Drag along the rim; the position persists across sandbox sessions.
        var drag = tab.gameObject.AddComponent<SandboxTabDrag>();
        drag.tab = tab;
        drag.canvasRect = canvas != null ? canvas.transform as RectTransform : null;
        drag.onMoved = (edge, frac) => { sandboxTabEdge = edge; sandboxTabFrac = frac; PositionSandboxTab(tab); };
        drag.onCommitted = () =>
        {
            PlayerPrefs.SetString(SandboxTabEdgeKey, sandboxTabEdge);
            PlayerPrefs.SetFloat(SandboxTabFracKey, sandboxTabFrac);
            PlayerPrefs.Save();
        };
    }

    // Pin the tab to its saved rim edge at its saved fraction along that edge. Vertical bar on the
    // left/right rails; horizontal bar on the top/bottom rails.
    private void PositionSandboxTab(RectTransform tab)
    {
        bool horizontal = sandboxTabEdge == "top" || sandboxTabEdge == "bottom";
        tab.sizeDelta = horizontal ? new Vector2(120f, 26f) : new Vector2(26f, 120f);
        float f = Mathf.Clamp01(sandboxTabFrac);
        switch (sandboxTabEdge)
        {
            case "left":
                tab.anchorMin = tab.anchorMax = new Vector2(0f, f);
                tab.pivot = new Vector2(0f, 0.5f);
                tab.anchoredPosition = new Vector2(2f, 0f);
                break;
            case "top":
                tab.anchorMin = tab.anchorMax = new Vector2(f, 1f);
                tab.pivot = new Vector2(0.5f, 1f);
                tab.anchoredPosition = new Vector2(0f, -2f);
                break;
            case "bottom":
                tab.anchorMin = tab.anchorMax = new Vector2(f, 0f);
                tab.pivot = new Vector2(0.5f, 0f);
                tab.anchoredPosition = new Vector2(0f, 2f);
                break;
            default: // right
                tab.anchorMin = tab.anchorMax = new Vector2(1f, f);
                tab.pivot = new Vector2(1f, 0.5f);
                tab.anchoredPosition = new Vector2(-2f, 0f);
                break;
        }
    }

    // ── Layout helpers (scroll-driven so the panel can grow without overflow) ──
    private RectTransform SbHeader(RectTransform content, string label)
    {
        var h = new GameObject("Sb H " + label).AddComponent<RectTransform>();
        h.SetParent(content, false);
        var le = h.gameObject.AddComponent<LayoutElement>(); le.minHeight = 19; le.preferredHeight = 19;
        var t = TextObject(label, h, label, 9, SbAccent, TextAnchor.LowerLeft, monoFont);
        t.fontStyle = FontStyle.Bold;
        Stretch(t.rectTransform, Vector2.zero, Vector2.one, new Vector2(2, 0), new Vector2(-2, -2));
        return h;
    }

    private RectTransform SbRow(RectTransform content, float height = 25f)
    {
        var row = new GameObject("Sb Row").AddComponent<RectTransform>();
        row.SetParent(content, false);
        var le = row.gameObject.AddComponent<LayoutElement>(); le.minHeight = height; le.preferredHeight = height;
        var hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 3;
        hlg.childControlWidth = true; hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true; hlg.childForceExpandHeight = true;
        return row;
    }

    private void SbRowBtn(RectTransform row, string label, System.Action action, bool on = false, int font = 9, bool enabled = true)
    {
        var b = PanelObject("Sb " + label, row, !enabled ? SbBtnDim : on ? SbBtnOn : SbBtnBg);
        Round(b);
        AddRoundedCardBorder(b, on ? SbAccent : SbBorder, 1f);
        var col = !enabled ? (Color)new Color32(120, 115, 130, 160) : on ? (Color)BadgeInk : Ink;
        var t = TextObject("t", b, label, font, col, TextAnchor.MiddleCenter, monoFont);
        t.resizeTextForBestFit = true; t.resizeTextMinSize = 6; t.resizeTextMaxSize = font;
        Stretch(t.rectTransform, Vector2.zero, Vector2.one, new Vector2(2, 0), new Vector2(-2, 0));
        var btn = b.gameObject.AddComponent<Button>();
        btn.interactable = enabled;
        if (enabled) btn.onClick.AddListener(() => action());
    }

    // Non-button caption row (for the SELECTED card name etc.).
    private void SbTextRow(RectTransform content, string text, Color color, float height = 20f)
    {
        var r = new GameObject("Sb Cap").AddComponent<RectTransform>();
        r.SetParent(content, false);
        var le = r.gameObject.AddComponent<LayoutElement>(); le.minHeight = height; le.preferredHeight = height;
        var t = TextObject("t", r, text, 9, color, TextAnchor.MiddleLeft, monoFont);
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        Stretch(t.rectTransform, Vector2.zero, Vector2.one, new Vector2(2, 0), new Vector2(-2, 0));
    }

    private void DrawSandboxPanel()
    {
        var panel = PanelObject("Sandbox Panel", sandboxRoot, SbPanelBg);
        Stretch(panel, new Vector2(0.70f, 0.03f), new Vector2(0.978f, 0.965f), Vector2.zero, Vector2.zero);
        RoundBig(panel);
        AddRoundedCardBorder(panel, SbAccent, 1.5f);

        var title = TextObject("Sandbox Title", panel, "SANDBOX", 15, SbAccent, TextAnchor.MiddleLeft, titleFont);
        title.fontStyle = FontStyle.Bold;
        Stretch(title.rectTransform, new Vector2(0.05f, 0.955f), new Vector2(0.55f, 0.99f), Vector2.zero, Vector2.zero);
        var hint = TextObject("Sandbox Hint", panel, sandboxEditMode ? "edit: click a card" : "play mode", 8,
            Muted, TextAnchor.MiddleRight, monoFont);
        Stretch(hint.rectTransform, new Vector2(0.5f, 0.955f), new Vector2(0.95f, 0.99f), Vector2.zero, Vector2.zero);

        // Scroll viewport fills the panel below the title.
        var viewport = PanelObject("Sb Panel Viewport", panel, new Color(0, 0, 0, 0));
        Stretch(viewport, new Vector2(0.035f, 0.012f), new Vector2(0.965f, 0.945f), Vector2.zero, Vector2.zero);
        viewport.gameObject.AddComponent<RectMask2D>();
        var content = new GameObject("Sb Panel Content").AddComponent<RectTransform>();
        content.SetParent(viewport, false);
        content.anchorMin = new Vector2(0f, 1f); content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f); content.sizeDelta = Vector2.zero; content.anchoredPosition = Vector2.zero;
        var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 3; vlg.childControlWidth = true; vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false; vlg.childAlignment = TextAnchor.UpperLeft;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.content = content; scroll.viewport = viewport;
        scroll.horizontal = false; scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 24f;
        AttachScrollbar(scroll);

        string me = BottomSeat;

        // VIEW & MODE
        SbHeader(content, "VIEW & MODE");
        var r = SbRow(content);
        SbRowBtn(r, "Bottom: " + (sandboxViewSeat == "south" ? "SOUTH" : "NORTH"),
            () => { sandboxViewSeat = sandboxViewSeat == "south" ? "north" : "south"; Render(); });
        SbRowBtn(r, sandboxEditMode ? "EDIT: ON" : "EDIT: off",
            () => { sandboxEditMode = !sandboxEditMode; Render(); }, sandboxEditMode);

        // HISTORY
        r = SbRow(content);
        SbRowBtn(r, sandboxUndoStack.Count > 0 ? $"↶ UNDO ({sandboxUndoStack.Count})" : "↶ UNDO", SandboxUndo, false, 9, sandboxUndoStack.Count > 0);
        SbRowBtn(r, sandboxRedoStack.Count > 0 ? $"↷ REDO ({sandboxRedoStack.Count})" : "↷ REDO", SandboxRedo, false, 9, sandboxRedoStack.Count > 0);

        // LOAD POSITION — paste a replay/exported position code and boot it (with the POV picker).
        SbHeader(content, "LOAD POSITION");
        r = SbRow(content);
        SbRowBtn(r, "↺ RESTORE CODE", () => { sandboxRestoreOpen = true; sandboxRestoreDraft = ""; Render(); });

        // TURN & PHASE
        SbHeader(content, "TURN & PHASE");
        r = SbRow(content);
        SbRowBtn(r, "Active: SOUTH", () => SandboxSetActiveSeat("south"), state.ActiveSeat == "south");
        SbRowBtn(r, "Active: NORTH", () => SandboxSetActiveSeat("north"), state.ActiveSeat == "north");
        string[] phases = { "refresh", "draw", "don", "main", "battle", "end" };
        r = SbRow(content);
        for (int i = 0; i < 3; i++) { string ph = phases[i]; SbRowBtn(r, ph.ToUpper(), () => SandboxSetPhase(ph), state.Phase == ph, 8); }
        r = SbRow(content);
        for (int i = 3; i < 6; i++) { string ph = phases[i]; SbRowBtn(r, ph.ToUpper(), () => SandboxSetPhase(ph), state.Phase == ph, 8); }
        r = SbRow(content);
        SbRowBtn(r, "+2 DRAW", () => SandboxDrawCards(state.ActiveSeat, 2), false, 8);
        SbRowBtn(r, "END TURN", () => Dispatch(new GameCommand { Type = "endTurn", Seat = state.ActiveSeat }), false, 8);
        SbRowBtn(r, "Turn +1", SandboxBumpTurn, false, 8);

        // SPAWN
        SbHeader(content, "SPAWN A CARD");
        r = SbRow(content);
        SbRowBtn(r, "To: " + (sandboxSpawnSeat == "bottom" ? "MINE" : "OPP"),
            () => { sandboxSpawnSeat = sandboxSpawnSeat == "bottom" ? "top" : "bottom"; Render(); });
        SbRowBtn(r, "PICK CARD…", () => { sandboxPickerOpen = true; sandboxPickerSearch = ""; Render(); }, true);
        string[] zones = { "character", "hand", "trash", "life", "deck", "stage", "leader" };
        r = SbRow(content);
        for (int i = 0; i < 4; i++) { string z = zones[i]; SbRowBtn(r, z.ToUpper(), () => { sandboxSpawnZone = z; Render(); }, sandboxSpawnZone == z, 7); }
        r = SbRow(content);
        for (int i = 4; i < 7; i++) { string z = zones[i]; SbRowBtn(r, z.ToUpper(), () => { sandboxSpawnZone = z; Render(); }, sandboxSpawnZone == z, 7); }

        // RESOURCES
        SbHeader(content, "DON!! & LIFE — " + (me == "south" ? "SOUTH" : "NORTH"));
        r = SbRow(content);
        SbRowBtn(r, "DON −1", () => SandboxRemoveDon(me, 1), false, 8);
        SbRowBtn(r, "DON +1", () => SandboxAddDon(me, 1, false), false, 8);
        SbRowBtn(r, "DON +5", () => SandboxAddDon(me, 5, false), false, 8);
        r = SbRow(content);
        SbRowBtn(r, "Rest DON", () => SandboxSetAllDonRested(me, true), false, 8);
        SbRowBtn(r, "Active DON", () => SandboxSetAllDonRested(me, false), false, 8);
        r = SbRow(content);
        SbRowBtn(r, "Life −1", () => SandboxRemoveLife(me), false, 8);
        SbRowBtn(r, "Life +1", () => SandboxAddLifeFromDeck(me, 1), false, 8);
        SbRowBtn(r, "Life +5", () => SandboxAddLifeFromDeck(me, 5), false, 8);

        // SELECTED CARD
        if (sandboxEditMode) DrawSandboxSelected(content);

        // SNAPSHOTS
        SbHeader(content, "SNAPSHOTS");
        for (int i = 1; i <= SandboxSlotCount; i++)
        {
            int slot = i;
            bool exists = SandboxSlotExists(slot);
            r = SbRow(content);
            SbRowBtn(r, $"Slot {slot}" + (exists ? " ●" : " ○"), () => { }, false, 8, false);
            SbRowBtn(r, "Save", () => SandboxSaveSlot(slot), false, 8);
            SbRowBtn(r, "Load", () => SandboxLoadSlot(slot), false, 8, exists);
        }

        // RESET
        SbHeader(content, "RESET");
        r = SbRow(content);
        SbRowBtn(r, "Clear my board", () => SandboxClearBoard(me), false, 8);
        SbRowBtn(r, "Clear ALL", SandboxClearAll, false, 8);
        r = SbRow(content);
        SbRowBtn(r, "NEW SANDBOX", NewSandbox, true, 9);

        // Tail spacer so the last row isn't flush against the mask.
        SbTextRow(content, "", Muted, 6f);
    }

    private void DrawSandboxSelected(RectTransform content)
    {
        var card = sandboxSelectedCard;
        SbHeader(content, "SELECTED CARD");
        if (card == null)
        {
            SbTextRow(content, "click any card on the board / in a hand", Muted);
            return;
        }
        var def = CardData.GetCard(card.CardId);
        int power = def != null && (def.Type == "character" || def.Type == "leader")
            ? GameEngine.GetPower(state, card) : 0;
        string sub = $"{(def != null ? def.Name : card.CardId)}  [{card.Zone}]";
        string sub2 = (card.Rested ? "rested" : "active")
            + (card.AttachedDonIds.Count > 0 ? $" · {card.AttachedDonIds.Count} DON" : "")
            + (power != 0 ? $" · {power} pow" : "");
        SbTextRow(content, sub, Ink);
        SbTextRow(content, sub2, Muted, 16f);

        var r = SbRow(content);
        SbRowBtn(r, card.Rested ? "Set ACTIVE" : "Set RESTED", SandboxToggleRestSelected, false, 8);
        SbRowBtn(r, "Duplicate", SandboxDuplicateSelected, false, 8);
        SbRowBtn(r, "DELETE", SandboxDeleteSelected, false, 8);

        // DON attach + power buff (meaningful for Leaders/Characters in play).
        r = SbRow(content);
        SbRowBtn(r, "DON −1", () => SandboxAttachDon(card, -1), false, 8);
        SbRowBtn(r, "DON +1", () => SandboxAttachDon(card, +1), false, 8);
        SbRowBtn(r, "Pow −1k", () => SandboxPowerBuff(card, -1000), false, 8);
        SbRowBtn(r, "Pow +1k", () => SandboxPowerBuff(card, +1000), false, 8);
        if (card.Zone == "life")
        {
            r = SbRow(content);
            SbRowBtn(r, card.FaceUp ? "Flip DOWN" : "Flip UP", SandboxToggleFaceUpSelected, false, 8);
        }

        // Keyword grants (permanent) — toggle on/off; the on-state highlights.
        SbTextRow(content, "grant keyword:", Muted, 16f);
        string[] kws = { "Rush", "Blocker", "Double Attack", "Banish" };
        r = SbRow(content);
        for (int i = 0; i < 2; i++) { string kw = kws[i]; SbRowBtn(r, kw, () => SandboxToggleKeyword(card, kw), SandboxHasKeywordGrant(card, kw), 8); }
        r = SbRow(content);
        for (int i = 2; i < 4; i++) { string kw = kws[i]; SbRowBtn(r, kw, () => SandboxToggleKeyword(card, kw), SandboxHasKeywordGrant(card, kw), 8); }

        SbTextRow(content, "move to:", Muted, 16f);
        string[] zones = { "character", "hand", "trash", "life", "deck", "stage", "leader" };
        r = SbRow(content);
        for (int i = 0; i < 4; i++) { string z = zones[i]; SbRowBtn(r, z.ToUpper(), () => SandboxMoveSelected(z), false, 7); }
        r = SbRow(content);
        for (int i = 4; i < 7; i++) { string z = zones[i]; SbRowBtn(r, z.ToUpper(), () => SandboxMoveSelected(z), false, 7); }
    }

    // ── Restore-from-code overlay (paste a position code, boot it via the POV picker) ─────────
    private void DrawSandboxRestore()
    {
        var dim = PanelObject("Sb Restore Dim", sandboxRoot, new Color32(6, 6, 12, 210));
        Stretch(dim, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        dim.gameObject.AddComponent<Button>().onClick.AddListener(() => { sandboxRestoreOpen = false; Render(); });

        var modal = PanelObject("Sb Restore", sandboxRoot, SbPanelBg);
        Stretch(modal, new Vector2(0.30f, 0.36f), new Vector2(0.70f, 0.64f), Vector2.zero, Vector2.zero);
        RoundBig(modal);
        AddRoundedCardBorder(modal, SbAccent, 1.6f);

        var title = TextObject("Sb Restore Title", modal, "RESTORE POSITION", 14, SbAccent, TextAnchor.UpperLeft, titleFont);
        title.fontStyle = FontStyle.Bold;
        Stretch(title.rectTransform, new Vector2(0.06f, 0.80f), new Vector2(0.94f, 0.93f), Vector2.zero, Vector2.zero);

        var sub = TextObject("Sb Restore Sub", modal, "Paste a position code exported from a replay. It boots that exact position (you then pick a POV).",
            10, Muted, TextAnchor.UpperLeft, monoFont);
        sub.horizontalOverflow = HorizontalWrapMode.Wrap;
        Stretch(sub.rectTransform, new Vector2(0.06f, 0.62f), new Vector2(0.94f, 0.80f), Vector2.zero, Vector2.zero);

        // Input field (built like the picker's search field).
        var fieldGo = new GameObject("Sb Restore Field", typeof(RectTransform), typeof(Image), typeof(InputField));
        var fieldRt = fieldGo.GetComponent<RectTransform>();
        fieldRt.SetParent(modal, false);
        Stretch(fieldRt, new Vector2(0.06f, 0.40f), new Vector2(0.94f, 0.56f), Vector2.zero, Vector2.zero);
        fieldGo.GetComponent<Image>().color = new Color32(28, 24, 38, 235);
        Round(fieldRt);
        AddRoundedCardBorder(fieldRt, SbAccent, 1f);
        var field = fieldGo.GetComponent<InputField>();
        var ph = TextObject("ph", fieldRt, "OPTCG1:…paste code here…", 10, Muted, TextAnchor.MiddleLeft);
        var txt = TextObject("txt", fieldRt, "", 10, Ink, TextAnchor.MiddleLeft);
        Stretch(ph.rectTransform, new Vector2(0.03f, 0f), new Vector2(0.98f, 1f), Vector2.zero, Vector2.zero);
        Stretch(txt.rectTransform, new Vector2(0.03f, 0f), new Vector2(0.98f, 1f), Vector2.zero, Vector2.zero);
        field.textComponent = txt; field.placeholder = ph;
        field.lineType = InputField.LineType.SingleLine;
        field.text = sandboxRestoreDraft ?? "";
        field.onValueChanged.AddListener(v => sandboxRestoreDraft = v);

        // Actions.
        var actions = SbRow(modal);
        actions.anchorMin = new Vector2(0.06f, 0.10f); actions.anchorMax = new Vector2(0.94f, 0.28f);
        actions.pivot = new Vector2(0.5f, 0.5f); actions.offsetMin = actions.offsetMax = Vector2.zero;
        SbRowBtn(actions, "RESTORE", SandboxRestoreFromDraft, true);
        SbRowBtn(actions, "Cancel", () => { sandboxRestoreOpen = false; Render(); });
    }

    private void SandboxRestoreFromDraft()
    {
        string code = (sandboxRestoreDraft ?? "").Trim();
        if (string.IsNullOrEmpty(code)) return;   // nothing to do; leave the overlay open
        sandboxRestoreOpen = false;
        // BeginRestore rebuilds state from the code and shows its own error overlay if it's invalid.
        // It also clears sandbox mode — restoring exits the sandbox into the restored position.
        BeginRestore(code);
    }

    // ── Card picker overlay (deck-builder-style: search + colour chips + card-art grid + hover) ──
    private RectTransform sandboxPickerContent;
    private string sandboxPickerColor;   // null = all colours
    private static readonly string[] SbColors = { "Red", "Green", "Blue", "Purple", "Black", "Yellow" };

    private void DrawSandboxPicker()
    {
        var dim = PanelObject("Sb Picker Dim", sandboxRoot, new Color32(6, 6, 12, 205));
        Stretch(dim, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        dim.gameObject.AddComponent<Button>().onClick.AddListener(() => { sandboxPickerOpen = false; HideHandHoverPreview(); Render(); });

        var modal = PanelObject("Sb Picker", sandboxRoot, SbPanelBg);
        Stretch(modal, new Vector2(0.30f, 0.07f), new Vector2(0.72f, 0.95f), Vector2.zero, Vector2.zero);
        RoundBig(modal);
        AddRoundedCardBorder(modal, SbAccent, 1.6f);

        var title = TextObject("Sb Picker Title", modal,
            $"SPAWN INTO {(sandboxSpawnSeat == "bottom" ? "MINE" : "OPP")} · {sandboxSpawnZone.ToUpper()}", 13, SbAccent, TextAnchor.MiddleLeft, titleFont);
        title.fontStyle = FontStyle.Bold;
        Stretch(title.rectTransform, new Vector2(0.05f, 0.945f), new Vector2(0.85f, 0.985f), Vector2.zero, Vector2.zero);
        var close = PanelObject("Sb Picker Close", modal, SbBtnBg);
        Stretch(close, new Vector2(0.9f, 0.945f), new Vector2(0.96f, 0.99f), Vector2.zero, Vector2.zero);
        Round(close);
        var closeT = TextObject("x", close, "✕", 13, Ink, TextAnchor.MiddleCenter);
        Stretch(closeT.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        close.gameObject.AddComponent<Button>().onClick.AddListener(() => { sandboxPickerOpen = false; HideHandHoverPreview(); Render(); });

        // Search field.
        var fieldGo = new GameObject("Sb Picker Field", typeof(RectTransform), typeof(Image), typeof(InputField));
        var fieldRt = fieldGo.GetComponent<RectTransform>();
        fieldRt.SetParent(modal, false);
        Stretch(fieldRt, new Vector2(0.05f, 0.90f), new Vector2(0.96f, 0.94f), Vector2.zero, Vector2.zero);
        fieldGo.GetComponent<Image>().color = new Color32(28, 24, 38, 235);
        Round(fieldRt);
        AddRoundedCardBorder(fieldRt, SbAccent, 1f);
        var field = fieldGo.GetComponent<InputField>();
        var ph = TextObject("ph", fieldRt, "search name or id (e.g. Luffy, OP01-024)…", 11, Muted, TextAnchor.MiddleLeft);
        var txt = TextObject("txt", fieldRt, "", 11, Ink, TextAnchor.MiddleLeft);
        Stretch(ph.rectTransform, new Vector2(0.03f, 0f), new Vector2(0.98f, 1f), Vector2.zero, Vector2.zero);
        Stretch(txt.rectTransform, new Vector2(0.03f, 0f), new Vector2(0.98f, 1f), Vector2.zero, Vector2.zero);
        field.textComponent = txt; field.placeholder = ph;
        field.lineType = InputField.LineType.SingleLine;
        field.text = sandboxPickerSearch ?? "";
        field.onValueChanged.AddListener(v => { sandboxPickerSearch = v; ScheduleSandboxPickerRefresh(); });
        sandboxPickerField = field;

        // Colour filter chips (deck-builder-style): All + the six card colours.
        var chipRow = PanelObject("Sb Picker Chips", modal, new Color(0, 0, 0, 0));
        Stretch(chipRow, new Vector2(0.05f, 0.862f), new Vector2(0.96f, 0.895f), Vector2.zero, Vector2.zero);
        var chlg = chipRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        chlg.spacing = 4f; chlg.childAlignment = TextAnchor.MiddleLeft;
        chlg.childControlWidth = false; chlg.childControlHeight = true; chlg.childForceExpandHeight = true;
        SbFilterChip(chipRow, "ALL", sandboxPickerColor == null, () => { sandboxPickerColor = null; RefreshSandboxPickerList(); });
        foreach (var col in SbColors)
        {
            string cc = col;
            SbFilterChip(chipRow, cc.ToUpper(), sandboxPickerColor == cc, () => { sandboxPickerColor = cc; RefreshSandboxPickerList(); });
        }

        // Card-art grid.
        var viewport = PanelObject("Sb Picker Viewport", modal, new Color(0, 0, 0, 0));
        Stretch(viewport, new Vector2(0.05f, 0.035f), new Vector2(0.96f, 0.85f), Vector2.zero, Vector2.zero);
        viewport.gameObject.AddComponent<RectMask2D>();
        var content = new GameObject("Sb Picker Content").AddComponent<RectTransform>();
        content.SetParent(viewport, false);
        content.anchorMin = new Vector2(0f, 1f); content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f); content.sizeDelta = Vector2.zero; content.anchoredPosition = Vector2.zero;
        float cellH = 96f, cellW = cellH * CardAspect;
        var grid = content.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(cellW, cellH);
        grid.spacing = new Vector2(8f, 10f);
        grid.padding = new RectOffset(4, 4, 4, 4);
        grid.childAlignment = TextAnchor.UpperLeft;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.content = content; scroll.viewport = viewport;
        scroll.horizontal = false; scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 30f;
        AttachScrollbar(scroll);
        sandboxPickerContent = content;
        RefreshSandboxPickerList();
    }

    // Compact filter chip for the picker (colour filters).
    private void SbFilterChip(RectTransform row, string label, bool on, System.Action action)
    {
        Color acc = SbAccent;   // Color32 → Color so the channels are 0..1
        var chip = PanelObject(label + " Chip", row, on ? new Color(acc.r, acc.g, acc.b, 0.22f) : (Color)SbBtnBg);
        float w = Mathf.Max(34f, 12f + label.Length * 7f);
        var le = chip.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = w; le.minWidth = w;
        chip.sizeDelta = new Vector2(w, 22f);
        Round(chip);
        AddRoundedCardBorder(chip, on ? SbAccent : SbBorder, on ? 1.3f : 1f);
        var t = TextObject("t", chip, label, 8, on ? (Color)SbAccent : Muted, TextAnchor.MiddleCenter, monoFont);
        t.fontStyle = FontStyle.Bold;
        Stretch(t.rectTransform, Vector2.zero, Vector2.one, new Vector2(3, 0), new Vector2(-3, 0));
        chip.gameObject.AddComponent<Button>().onClick.AddListener(() => action());
    }

    // Hover preview hooks (called by SandboxCardHover on the grid tiles).
    internal void SandboxHoverEnter(string cardId, RectTransform source) => ShowSandboxCardPreview(cardId, source);
    internal void SandboxHoverExit() => HideHandHoverPreview();

    // Big card preview parked on the left third of the screen (never covers the centred grid).
    private void ShowSandboxCardPreview(string cardId, RectTransform source)
    {
        HideHandHoverPreview();
        if (string.IsNullOrEmpty(cardId) || canvas == null) return;
        var canvasRect = canvas.transform as RectTransform;
        if (canvasRect == null) return;
        var size = new Vector2(240f, 336f);
        var target = ClampToCanvas(new Vector2(-canvasRect.rect.width * 0.31f, 0f), size, canvasRect.rect, 12f);
        handHoverRoot = PanelObject("Sandbox Card Preview", canvas.transform, new Color(0, 0, 0, 0));
        handHoverRoot.anchorMin = handHoverRoot.anchorMax = new Vector2(0.5f, 0.5f);
        handHoverRoot.pivot = new Vector2(0.5f, 0.5f);
        handHoverRoot.sizeDelta = size;
        handHoverRoot.anchoredPosition = target;
        var group = handHoverRoot.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false; group.interactable = false;
        RoundedCardVisual("Sandbox Preview Art", handHoverRoot, GetCardSprite(cardId), out var image);
        if (image != null) { image.preserveAspect = true; image.raycastTarget = false; }
        handHoverRoot.SetAsLastSibling();
    }

    // Card type the current spawn zone accepts, or null for zones that legitimately hold any card
    // (hand/deck/trash/life). Character/leader/stage zones scope the search to that one type.
    private string SandboxZoneCardType() => sandboxSpawnZone switch
    {
        "character" => "character",
        "leader" => "leader",
        "stage" => "stage",
        _ => null,
    };

    // Debounce keystrokes: rebuilding dozens of image rows on EVERY character was the search lag.
    // Coalesce rapid typing into one rebuild ~120ms after the last keystroke.
    private void ScheduleSandboxPickerRefresh()
    {
        CancelInvoke(nameof(RefreshSandboxPickerList));
        Invoke(nameof(RefreshSandboxPickerList), 0.12f);
    }

    // Cached, pre-lowercased, id-sorted view of the card library so search doesn't re-scan +
    // re-ToLowerInvariant the whole library on every keystroke. Rebuilt only when the library grows
    // (card defs stream in from the CDN), keyed on Library.Count.
    private struct SbIndexEntry { public string idLower, nameLower, type, color; public CardDef def; }
    private static System.Collections.Generic.List<SbIndexEntry> _sbIndex;
    private static int _sbIndexCount = -1;
    private static System.Collections.Generic.List<SbIndexEntry> SandboxCardIndex()
    {
        if (_sbIndex != null && _sbIndexCount == CardData.Library.Count) return _sbIndex;
        var list = new System.Collections.Generic.List<SbIndexEntry>(CardData.Library.Count);
        foreach (var c in CardData.Library.Values)
        {
            if (c == null || string.IsNullOrEmpty(c.Id)) continue;
            list.Add(new SbIndexEntry
            {
                def = c,
                type = c.Type ?? "",
                color = c.Color ?? "",
                idLower = c.Id.ToLowerInvariant(),
                nameLower = (c.Name ?? "").ToLowerInvariant(),
            });
        }
        list.Sort((a, b) => string.Compare(a.idLower, b.idLower, System.StringComparison.OrdinalIgnoreCase));
        _sbIndex = list;
        _sbIndexCount = CardData.Library.Count;
        return _sbIndex;
    }

    private const int SandboxPickerRowCap = 80;

    private void RefreshSandboxPickerList()
    {
        if (!sandboxPickerOpen || sandboxPickerContent == null) return;
        HideHandHoverPreview();   // a stale preview from a tile that's about to be destroyed
        for (int i = sandboxPickerContent.childCount - 1; i >= 0; i--) Destroy(sandboxPickerContent.GetChild(i).gameObject);

        string q = (sandboxPickerSearch ?? "").Trim().ToLowerInvariant();
        string wantType = SandboxZoneCardType();
        string wantColor = sandboxPickerColor;

        int shown = 0;
        foreach (var e in SandboxCardIndex())
        {
            if (wantType != null && e.type != wantType) continue;
            if (wantColor != null && (e.color == null || e.color.IndexOf(wantColor, System.StringComparison.OrdinalIgnoreCase) < 0)) continue;
            if (q.Length > 0 && !e.nameLower.Contains(q) && !e.idLower.Contains(q)) continue;
            BuildSandboxPickerTile(e.def);
            if (++shown >= SandboxPickerRowCap) break;   // id-sorted, so an early stop == Take(N)
        }

        if (shown == 0)
        {
            string msg = CardData.Library.Count == 0 ? "card library still loading…" : "no cards match those filters";
            var empty = TextObject("Sb Picker Empty", sandboxPickerContent, msg, 11, Muted, TextAnchor.UpperLeft);
            empty.gameObject.AddComponent<LayoutElement>().preferredHeight = 26;
        }
    }

    // One card-art tile in the picker grid: hover shows the big preview, click spawns it.
    private void BuildSandboxPickerTile(CardDef c)
    {
        var cardId = c.Id;
        var cell = PanelObject("Sb Card " + cardId, sandboxPickerContent, new Color(0, 0, 0, 0));

        var art = RoundedCardVisual("art", cell, GetCardSprite(cardId), out var artImg);
        Stretch(art, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        if (artImg != null) { artImg.preserveAspect = true; artImg.raycastTarget = true; }

        cell.gameObject.AddComponent<SandboxCardHover>().Init(this, cardId, cell);
        cell.gameObject.AddComponent<Button>().onClick.AddListener(() =>
        {
            HideHandHoverPreview();
            SandboxSpawn(cardId, SpawnSeatResolved, sandboxSpawnZone);
        });
    }
}

// Drag handler for the SANDBOX tab: while dragging, snaps the tab to the nearest screen rim (edge)
// at the cursor's position along that edge, reporting (edge, fraction) live and persisting on release.
// A plain click (no drag) still falls through to the tab's Button, which toggles the editor panel.
public sealed class SandboxTabDrag : MonoBehaviour, IDragHandler, IEndDragHandler
{
    public RectTransform tab;
    public RectTransform canvasRect;
    public System.Action<string, float> onMoved;   // (edge, frac) during drag
    public System.Action onCommitted;               // persist on release

    public void OnDrag(PointerEventData e)
    {
        if (canvasRect == null || tab == null) return;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, e.position, e.pressEventCamera, out var lp))
            return;
        var r = canvasRect.rect;
        float nx = Mathf.Clamp01((lp.x - r.xMin) / Mathf.Max(1f, r.width));
        float ny = Mathf.Clamp01((lp.y - r.yMin) / Mathf.Max(1f, r.height));
        float dLeft = nx, dRight = 1f - nx, dBottom = ny, dTop = 1f - ny;
        float min = Mathf.Min(Mathf.Min(dLeft, dRight), Mathf.Min(dTop, dBottom));
        string edge; float frac;
        if (min == dRight) { edge = "right"; frac = ny; }
        else if (min == dLeft) { edge = "left"; frac = ny; }
        else if (min == dTop) { edge = "top"; frac = nx; }
        else { edge = "bottom"; frac = nx; }
        onMoved?.Invoke(edge, frac);
    }

    public void OnEndDrag(PointerEventData e) => onCommitted?.Invoke();
}

// Hover handler for a sandbox picker card tile: shows/hides the big card preview via the manager.
public sealed class SandboxCardHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private GameManager manager;
    private string cardId;
    private RectTransform source;

    public void Init(GameManager m, string id, RectTransform src) { manager = m; cardId = id; source = src; }
    public void OnPointerEnter(PointerEventData e) { if (manager != null) manager.SandboxHoverEnter(cardId, source); }
    public void OnPointerExit(PointerEventData e) { if (manager != null) manager.SandboxHoverExit(); }
}
