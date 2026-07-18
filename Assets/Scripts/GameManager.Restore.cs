// One Piece TCG - REPLAY POSITION EXPORT / RESTORE.
// In a replay you can "Export Position" — a compact, shareable code for the EXACT state at the
// current cursor. Restoring that code resimulates the deterministic engine to that position and
// then lets you pick a POV / control assignment and play it out yourself ("I wonder what happens
// if I'd played it differently"). The code is self-contained (embeds both decklists + the command
// history up to the cursor) so it's portable to anyone, not just the original players.
//
// Determinism: a match is fully reproducible from {seed, both decks, commands} because the engine
// mutates one GameState via CommandHistory + the seeded RNG (see GameState.cs). So restore =
// CreateMatch(seed, decks) + replay commands → the identical position. Partial of GameManager.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using OnePieceTcg.Engine;

// Serializable payload behind a position code. JsonUtility-friendly (no nullables, no tuples).
[Serializable]
public sealed class PositionCode
{
    public int v = 1;
    public string seed;
    public string firstPlayer = "south";
    public NetworkDeck south;   // null → ST01 starter default
    public NetworkDeck north;   // null → ST02 starter default
    public List<SerializableCommand> commands = new List<SerializableCommand>();
}

public partial class GameManager
{
    // ── Restore entry ────────────────────────────────────────────────────────
    public static string PendingRestoreCode;

    /// <summary>Boots the board, decodes `code`, resimulates to that position, and shows the
    /// POV picker before interactive play begins.</summary>
    public static void LaunchRestore(string code)
    {
        PendingRestoreCode = code;
        EnsureBoard();
    }

    // ── Restore runtime state ────────────────────────────────────────────────
    private bool isRestoredGame;            // a solo continuation from a restored position
    private string povSeat;                 // which seat renders at the bottom (see BottomSeat)
    private bool restorePickPending;        // POV picker overlay is up (play not started yet)
    private string restoreError;            // non-null → decode failed; shown in the overlay

    private static string OtherSeatOf(string seat) => seat == "south" ? "north" : "south";

    private void BeginRestore(string code)
    {
        isReplayMode = false;
        loadedReplay = null;
        isSandbox = false;
        aiSeat = null;
        povSeat = "south";
        restoreError = null;
        lastCardPoses.Clear();
        suppressMoveAnim.Clear();

        PositionCode pc = null;
        try { pc = DecodePositionCode(code); }
        catch (Exception ex) { restoreError = "Couldn't read that code (" + ex.Message + ")."; }

        if (pc == null)
        {
            if (restoreError == null) restoreError = "That code was empty or invalid.";
            // Boot an empty-ish live match so the overlay has a board behind it; the overlay's
            // only action when errored is "Main Menu".
            state = GameEngine.CreateMatch(new MatchConfig { Seed = Guid.NewGuid().ToString("N") });
            currentMatchConfig = null;
            restorePickPending = true;
            Render();
            return;
        }

        var config = new MatchConfig { Seed = string.IsNullOrEmpty(pc.seed) ? "restored" : pc.seed };
        var southDef = pc.south?.ToDeckDef();
        var northDef = pc.north?.ToDeckDef();
        if (southDef != null) config.SouthDeckDef = southDef;
        if (northDef != null) config.NorthDeckDef = northDef;
        currentMatchConfig = config;

        try
        {
            state = GameEngine.CreateMatch(config);
            if (pc.commands != null)
                foreach (var sc in pc.commands)
                    state = GameEngine.ApplyCommand(state, sc.ToCommand());
        }
        catch (Exception ex)
        {
            restoreError = "The code decoded but failed to replay (" + ex.Message + ").";
        }

        selectedId = null;
        selectedSeat = null;
        previewLockCard = null;
        ClearDonSelection(false);
        finishedResultText = null;
        matchResultHidden = false;
        restorePickPending = true;
        Render();
    }

    // Begin interactive play from the restored position with the chosen control setup.
    // controlSeat = the seat the human plays (renders at the bottom). aiOpponent = the OTHER
    // seat is piloted by the Advanced bot; when false it's hotseat (you control both seats).
    private void StartRestoredPlay(string controlSeat, bool aiOpponent)
    {
        restorePickPending = false;
        if (restoreRoot != null) Clear(restoreRoot);   // tear down the picker overlay
        isRestoredGame = true;
        povSeat = controlSeat;
        aiSeat = aiOpponent ? OtherSeatOf(controlSeat) : null;
        aiDifficulty = "advanced";
        aiArchetype = OnePieceTcg.Engine.Bot.Search.AdvancedContractBot.ClassifyArchetype(
            aiSeat == "south" ? currentMatchConfig?.SouthDeckDef : currentMatchConfig?.NorthDeckDef);
        aiTriedThisTurn.Clear();
        aiActivatedThisTurn.Clear();
        aiLastTurnSeen = -1;
        aiNextActionAt = 0f;
        southDisplayName = aiSeat == "south" ? "Advanced Bot" : "You";
        northDisplayName = aiSeat == "north" ? "Advanced Bot" : "You";
        if (!aiOpponent) { southDisplayName = "Player 1"; northDisplayName = "Player 2"; }
        matchStartRealtime = Time.realtimeSinceStartup;
        Render();
    }

    // ── POV picker overlay ───────────────────────────────────────────────────
    // Top-level container so the picker's dim blocks the sideRoot/leftRoot HUD too (otherwise the
    // player could click END TURN / actions behind it before choosing a perspective).
    private RectTransform restoreRoot;

    private void DrawRestorePovOverlay()
    {
        if (restoreRoot == null)
        {
            restoreRoot = PanelObject("Restore Root", canvas.transform, new Color(0, 0, 0, 0));
            Stretch(restoreRoot, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            restoreRoot.GetComponent<Image>().raycastTarget = false;
        }
        Clear(restoreRoot);
        restoreRoot.SetAsLastSibling();

        var dim = PanelObject("Restore Dim", restoreRoot, new Color32(8, 10, 14, 220));
        Stretch(dim, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        dim.SetAsLastSibling();

        var panel = PanelObject("Restore Panel", restoreRoot, (Color)new Color32(14, 26, 40, 252));
        Stretch(panel, new Vector2(0.30f, 0.28f), new Vector2(0.70f, 0.72f), Vector2.zero, Vector2.zero);
        RoundBig(panel);
        AddRoundedCardBorder(panel, Accent, 2f);
        panel.SetAsLastSibling();

        var title = TextObject("Restore Title", panel, restoreError == null ? "RESTORE POSITION" : "CAN'T RESTORE", 22,
            restoreError == null ? Accent : (Color)new Color32(230, 120, 120, 255), TextAnchor.UpperCenter, titleFont);
        title.fontStyle = FontStyle.Bold;
        Stretch(title.rectTransform, new Vector2(0.06f, 0.82f), new Vector2(0.94f, 0.95f), Vector2.zero, Vector2.zero);

        if (restoreError != null)
        {
            var err = TextObject("Restore Err", panel, restoreError, 12, Muted, TextAnchor.UpperCenter, monoFont);
            err.horizontalOverflow = HorizontalWrapMode.Wrap;
            Stretch(err.rectTransform, new Vector2(0.08f, 0.45f), new Vector2(0.92f, 0.8f), Vector2.zero, Vector2.zero);
            var back = RowObject("Restore Back Row", panel, 8, TextAnchor.MiddleCenter);
            Stretch(back, new Vector2(0.2f, 0.12f), new Vector2(0.8f, 0.3f), Vector2.zero, Vector2.zero);
            AddButton(back, "Main Menu", ReturnToMenu);
            return;
        }

        string sub = $"Turn {state.TurnNumber} · {(state.ActiveSeat == "south" ? southNameOrDefault() : northNameOrDefault())} to act."
            + "\nPick who you'll play from here.";
        var subT = TextObject("Restore Sub", panel, sub, 12, Muted, TextAnchor.UpperCenter, monoFont);
        subT.horizontalOverflow = HorizontalWrapMode.Wrap;
        Stretch(subT.rectTransform, new Vector2(0.06f, 0.62f), new Vector2(0.94f, 0.82f), Vector2.zero, Vector2.zero);

        var row1 = RowObject("Restore Row1", panel, 10, TextAnchor.MiddleCenter);
        Stretch(row1, new Vector2(0.06f, 0.38f), new Vector2(0.94f, 0.56f), Vector2.zero, Vector2.zero);
        AddButton(row1, "Play SOUTH (vs AI)", () => StartRestoredPlay("south", true));
        AddButton(row1, "Play NORTH (vs AI)", () => StartRestoredPlay("north", true));

        var row2 = RowObject("Restore Row2", panel, 10, TextAnchor.MiddleCenter);
        Stretch(row2, new Vector2(0.06f, 0.18f), new Vector2(0.94f, 0.36f), Vector2.zero, Vector2.zero);
        AddButton(row2, "Hotseat (both seats)", () => StartRestoredPlay("south", false));
        AddButton(row2, "Main Menu", ReturnToMenu);
    }

    private string southNameOrDefault() => string.IsNullOrEmpty(southDisplayName) ? "South" : southDisplayName;
    private string northNameOrDefault() => string.IsNullOrEmpty(northDisplayName) ? "North" : northDisplayName;

    // ── Export (from replay mode) ────────────────────────────────────────────
    private string restoreExportNote;   // shown in the replay action panel after an export

    // Builds a position code for the CURRENT replay cursor and copies it to the clipboard.
    private void ExportReplayPosition()
    {
        if (!isReplayMode || loadedReplay == null) return;
        var pc = new PositionCode
        {
            seed = loadedReplay.Seed,
            firstPlayer = string.IsNullOrEmpty(loadedReplay.FirstPlayer) ? "south" : loadedReplay.FirstPlayer,
            south = DeckDefToNetworkDeck(currentMatchConfig?.SouthDeckDef),
            north = DeckDefToNetworkDeck(currentMatchConfig?.NorthDeckDef),
            commands = new List<SerializableCommand>(),
        };
        int cut = Mathf.Clamp(replayCursor, 0, loadedReplay.CommandHistory.Count);
        for (int i = 0; i < cut; i++) pc.commands.Add(loadedReplay.CommandHistory[i]);

        string code = EncodePositionCode(pc);
        GUIUtility.systemCopyBuffer = code;
        restoreExportNote = $"Position code copied ({code.Length} chars) — paste it in Custom Room ▸ Restore Code.";
        Render();
    }

    private static NetworkDeck DeckDefToNetworkDeck(DeckDef d)
    {
        if (d == null) return null;
        var entries = new List<NetworkDeckEntry>();
        if (d.List != null)
            foreach (var (cardId, qty) in d.List)
                if (cardId != d.Leader && qty > 0) entries.Add(new NetworkDeckEntry { id = cardId, count = qty });
        return new NetworkDeck { id = d.Id, name = d.Name, leader = d.Leader, cards = entries.ToArray() };
    }

    // ── Code encoding (gzip + base64, with a version tag) ─────────────────────
    private const string PositionCodePrefix = "OPTCG1:";

    private static string EncodePositionCode(PositionCode pc)
    {
        string json = JsonUtility.ToJson(pc);
        var raw = Encoding.UTF8.GetBytes(json);
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, System.IO.Compression.CompressionLevel.Optimal, true)) gz.Write(raw, 0, raw.Length);
        return PositionCodePrefix + Convert.ToBase64String(ms.ToArray());
    }

    private static PositionCode DecodePositionCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        code = code.Trim();
        if (code.StartsWith(PositionCodePrefix)) code = code.Substring(PositionCodePrefix.Length);
        var comp = Convert.FromBase64String(code);
        using var ms = new MemoryStream(comp);
        using var gz = new GZipStream(ms, CompressionMode.Decompress);
        using var outMs = new MemoryStream();
        gz.CopyTo(outMs);
        string json = Encoding.UTF8.GetString(outMs.ToArray());
        return JsonUtility.FromJson<PositionCode>(json);
    }
}
