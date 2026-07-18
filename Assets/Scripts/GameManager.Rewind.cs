// One Piece TCG - FORGIVENESS-MODE REWIND (networked custom lobbies).
// When a custom lobby is created in Forgiveness Mode, both players get a rewind control: undo
// 1 action or roll back to the start of the current turn. Any rewind is opponent-approved — it
// sends an Accept/Decline prompt, and only on Accept do BOTH clients truncate their (identical,
// deterministically-synced) CommandHistory to the same cursor and resimulate. No game state is
// transmitted, only the target cursor — same trust model as the normal command sync. Partial of
// GameManager. See [[MatchNetworkSync]] for the wire messages.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using OnePieceTcg.Engine;

public partial class GameManager
{
    // Set from PendingNetworkedForgiveness in EnterNetworkedMatch.
    private bool isForgiveness;

    // Outgoing: we asked for a rewind and are awaiting the opponent's answer.
    private bool rewindWaiting;
    private string rewindWaitingKind;
    // Incoming: the opponent asked us to approve a rewind.
    private bool rewindPromptOpen;
    private int rewindPromptCursor;
    private string rewindPromptKind;
    // Transient status line (e.g. "Opponent declined").
    private string rewindNote;
    private float rewindNoteUntil;

    private void SubscribeRewind()
    {
        MatchNetworkSync.RewindRequested -= OnRewindRequested;
        MatchNetworkSync.RewindRequested += OnRewindRequested;
        MatchNetworkSync.RewindResponded -= OnRewindResponded;
        MatchNetworkSync.RewindResponded += OnRewindResponded;
    }

    private void UnsubscribeRewind()
    {
        MatchNetworkSync.RewindRequested -= OnRewindRequested;
        MatchNetworkSync.RewindResponded -= OnRewindResponded;
    }

    private bool RewindAvailable =>
        isForgiveness && isNetworked && !isReplayMode && state != null
        && state.Status == "active" && !opponentLeft && currentMatchConfig != null;

    // ── Target cursor computation ────────────────────────────────────────────
    // Resimulate once from the match config, recording the command index at which each new turn
    // began (TurnNumber changed). starts[0] = the first live turn's start (the floor for rewinds —
    // we never rewind back into coin-flip/mulligan setup).
    private List<int> RewindTurnStartCursors()
    {
        var starts = new List<int>();
        if (currentMatchConfig == null || state == null) return starts;
        var s = GameEngine.CreateMatch(currentMatchConfig);
        int prevTurn = s.TurnNumber;
        var ch = state.CommandHistory;
        for (int i = 0; i < ch.Count; i++)
        {
            s = GameEngine.ApplyCommand(s, ch[i]);
            if (s.TurnNumber != prevTurn) { starts.Add(i + 1); prevTurn = s.TurnNumber; }
        }
        return starts;
    }

    // Returns the target cursor for a rewind, or -1 if there's nothing to rewind.
    private int RewindTargetCursor(string kind)
    {
        int count = state.CommandHistory.Count;
        var starts = RewindTurnStartCursors();
        int firstPlay = starts.Count > 0 ? starts[0] : 0;
        if (kind == "action")
        {
            int t = count - 1;
            return t < firstPlay ? -1 : t;
        }
        // "turn": back to the start of the current turn; if we're already at a turn start
        // (just ended a turn, no actions yet) go to the previous turn's start.
        int curStart = firstPlay;
        foreach (var st in starts) if (st <= count) curStart = st;
        int target;
        if (count > curStart) target = curStart;
        else
        {
            int prev = firstPlay;
            foreach (var st in starts) if (st < curStart) prev = st;
            target = prev;
        }
        if (target >= count) return -1;
        return Mathf.Max(firstPlay, target);
    }

    // ── Flow ─────────────────────────────────────────────────────────────────
    private void RequestRewind(string kind)
    {
        if (!RewindAvailable || rewindWaiting || rewindPromptOpen) return;
        int cursor = RewindTargetCursor(kind);
        if (cursor < 0 || cursor >= state.CommandHistory.Count)
        {
            SetRewindNote(kind == "turn" ? "Nothing to rewind this turn." : "Nothing to rewind.");
            Render();
            return;
        }
        rewindWaiting = true;
        rewindWaitingKind = kind;
        MatchNetworkSync.SendRewindRequest(cursor, kind);
        Render();
    }

    private void OnRewindRequested(RewindRequestPayload payload)
    {
        if (payload == null || !isForgiveness) return;
        // If we already have an outgoing ask, prefer answering theirs; simplest is to accept ours
        // is dropped — clear our waiting so we can respond (rare simultaneous case).
        rewindWaiting = false;
        rewindPromptOpen = true;
        rewindPromptCursor = payload.cursor;
        rewindPromptKind = string.IsNullOrEmpty(payload.kind) ? "action" : payload.kind;
        Render();
    }

    private void AcceptRewind()
    {
        int cursor = rewindPromptCursor;
        rewindPromptOpen = false;
        MatchNetworkSync.SendRewindResponse(true, cursor);
        PerformRewind(cursor);
        SetRewindNote("Rewound (you approved).");
        Render();
    }

    private void DeclineRewind()
    {
        rewindPromptOpen = false;
        MatchNetworkSync.SendRewindResponse(false, rewindPromptCursor);
        Render();
    }

    private void OnRewindResponded(RewindResponsePayload payload)
    {
        if (payload == null) return;
        rewindWaiting = false;
        if (payload.accept)
        {
            PerformRewind(payload.cursor);
            SetRewindNote("Rewound (opponent approved).");
        }
        else
        {
            SetRewindNote("Opponent declined the rewind.");
        }
        Render();
    }

    // Deterministic rewind: rebuild the match from its config and re-apply the (shared) command
    // history up to `cursor`. Both clients run this with the same cursor → identical state.
    private void PerformRewind(int cursor)
    {
        if (currentMatchConfig == null || state == null) return;
        var ch = new List<GameCommand>(state.CommandHistory);
        cursor = Mathf.Clamp(cursor, 0, ch.Count);
        var s = GameEngine.CreateMatch(currentMatchConfig);
        for (int i = 0; i < cursor; i++) s = GameEngine.ApplyCommand(s, ch[i]);
        state = s;
        selectedId = null;
        selectedSeat = null;
        previewLockCard = null;
        ClearDonSelection(false);
        rewindWaiting = false;
        rewindPromptOpen = false;
        replaySaved = false;   // the game is no longer at its finished state
        Render();
    }

    private void SetRewindNote(string msg)
    {
        rewindNote = msg;
        rewindNoteUntil = Time.unscaledTime + 4f;
    }

    // ── UI ───────────────────────────────────────────────────────────────────
    // Rewind UI lives in its own top-level container (like the sandbox) so its bar and its
    // approve/decline dims render ABOVE the sideRoot/leftRoot HUD columns — otherwise a dim on
    // boardRoot wouldn't block clicks on the side panel's END TURN / action buttons (desync risk)
    // and the top-left bar would hide behind leftRoot.
    private RectTransform rewindRoot;

    private void DrawRewind()
    {
        if (!RewindAvailable && !rewindWaiting && !rewindPromptOpen)
        {
            if (rewindRoot != null) Clear(rewindRoot);
            return;
        }
        if (rewindRoot == null)
        {
            rewindRoot = PanelObject("Rewind Root", canvas.transform, new Color(0, 0, 0, 0));
            Stretch(rewindRoot, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            rewindRoot.GetComponent<Image>().raycastTarget = false;
        }
        Clear(rewindRoot);
        rewindRoot.SetAsLastSibling();

        // The rewind control bar (top-left of the board). Hidden while the result screen is up.
        if (RewindAvailable && !ResultScreenActive())
        {
            var bar = PanelObject("Rewind Bar", rewindRoot, (Color)new Color32(14, 26, 40, 235));
            bar.anchorMin = bar.anchorMax = new Vector2(0f, 1f);
            bar.pivot = new Vector2(0f, 1f);
            bar.sizeDelta = new Vector2(268f, 40f);
            bar.anchoredPosition = new Vector2(12f, -12f);
            Round(bar);
            AddRoundedCardBorder(bar, Accent, 1.2f);
            var row = RowObject("Rewind Row", bar, 6, TextAnchor.MiddleCenter);
            Stretch(row, Vector2.zero, Vector2.one, new Vector2(6, 4), new Vector2(-6, -4));
            bool idle = !rewindWaiting && !rewindPromptOpen;
            AddButton(row, "↶ Action", () => RequestRewind("action"), idle, false);
            AddButton(row, "↶ Turn", () => RequestRewind("turn"), idle, false);
        }

        // Waiting-for-approval overlay (we asked).
        if (rewindWaiting)
        {
            var dim = PanelObject("Rewind Wait Dim", rewindRoot, new Color32(8, 10, 14, 170));
            Stretch(dim, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            dim.SetAsLastSibling();
            var panel = PanelObject("Rewind Wait", rewindRoot, (Color)new Color32(14, 26, 40, 250));
            Stretch(panel, new Vector2(0.34f, 0.42f), new Vector2(0.66f, 0.58f), Vector2.zero, Vector2.zero);
            RoundBig(panel);
            AddRoundedCardBorder(panel, Accent, 1.6f);
            panel.SetAsLastSibling();
            var t = TextObject("t", panel,
                $"Rewind {(rewindWaitingKind == "turn" ? "1 turn" : "1 action")} requested…\nWaiting for your opponent to approve.",
                13, Ink, TextAnchor.MiddleCenter, monoFont);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            Stretch(t.rectTransform, new Vector2(0.06f, 0f), new Vector2(0.94f, 1f), Vector2.zero, Vector2.zero);
        }

        // Approve/decline prompt (opponent asked us).
        if (rewindPromptOpen)
        {
            var dim = PanelObject("Rewind Ask Dim", rewindRoot, new Color32(8, 10, 14, 200));
            Stretch(dim, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            dim.SetAsLastSibling();
            var panel = PanelObject("Rewind Ask", rewindRoot, (Color)new Color32(14, 26, 40, 252));
            Stretch(panel, new Vector2(0.32f, 0.38f), new Vector2(0.68f, 0.62f), Vector2.zero, Vector2.zero);
            RoundBig(panel);
            AddRoundedCardBorder(panel, Accent, 2f);
            panel.SetAsLastSibling();
            var title = TextObject("t", panel, "REWIND REQUEST", 20, Accent, TextAnchor.UpperCenter, titleFont);
            title.fontStyle = FontStyle.Bold;
            Stretch(title.rectTransform, new Vector2(0.06f, 0.66f), new Vector2(0.94f, 0.92f), Vector2.zero, Vector2.zero);
            var sub = TextObject("s", panel,
                $"Your opponent wants to rewind {(rewindPromptKind == "turn" ? "to the start of the current turn" : "the last action")}.\nAllow it?",
                12, Muted, TextAnchor.UpperCenter, monoFont);
            sub.horizontalOverflow = HorizontalWrapMode.Wrap;
            Stretch(sub.rectTransform, new Vector2(0.06f, 0.36f), new Vector2(0.94f, 0.66f), Vector2.zero, Vector2.zero);
            var btns = RowObject("Rewind Ask Btns", panel, 10, TextAnchor.MiddleCenter);
            Stretch(btns, new Vector2(0.1f, 0.1f), new Vector2(0.9f, 0.34f), Vector2.zero, Vector2.zero);
            AddButton(btns, "Allow", AcceptRewind);
            AddButton(btns, "Decline", DeclineRewind);
        }

        // Transient status toast (bottom-centre).
        if (!string.IsNullOrEmpty(rewindNote) && Time.unscaledTime < rewindNoteUntil)
        {
            var toast = PanelObject("Rewind Toast", rewindRoot, (Color)new Color32(14, 30, 46, 245));
            toast.anchorMin = toast.anchorMax = new Vector2(0.5f, 0f);
            toast.pivot = new Vector2(0.5f, 0f);
            toast.sizeDelta = new Vector2(440f, 34f);
            toast.anchoredPosition = new Vector2(0f, 120f);
            Round(toast);
            AddRoundedCardBorder(toast, Accent, 1.2f);
            toast.SetAsLastSibling();
            var tt = TextObject("t", toast, rewindNote, 11, Accent2, TextAnchor.MiddleCenter, monoFont);
            Stretch(tt.rectTransform, Vector2.zero, Vector2.one, new Vector2(10, 0), new Vector2(-10, 0));
        }
    }
}
