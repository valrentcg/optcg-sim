// One Piece TCG - BLITZ / timed-match RUNTIME (client-side clock layer).
// The engine stays time-agnostic (Blitz spec §1). This layer runs personal chess-style clocks
// (Blitz) or a shared match clock (Ranked) on top of the normal command flow:
//  • ClockOwner is DERIVED from GameState each frame — whoever owns the current decision (§8/§9).
//  • Only the owner's clock drains (unscaled real time); increment is granted after the engine
//    validates a meaningful action in Dispatch (§5/§6/§7).
//  • Flag-fall / overtime / timeout-defaults are resolved here without touching the engine.
// Partial of GameManager. This file is the CORE (clocks, ownership, increment, warnings, flag-fall,
// HUD). Timeout-defaults (§9) and overtime/ranked resolution (§2) build on these hooks.

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using OnePieceTcg.Engine;

public partial class GameManager
{
    // Set by a launcher (vs-AI / custom lobby) before EnsureBoard(); consumed on match start.
    public static BlitzConfig PendingBlitzConfig;

    private BlitzConfig blitzConfig;
    private bool BlitzActive => blitzConfig != null && blitzConfig.IsTimed && !isReplayMode && !isSandbox;

    // Remaining seconds per seat (Blitz). Ranked uses blitzMatchClock instead.
    private float blitzSouth;
    private float blitzNorth;
    private float blitzMatchClock;          // Ranked: single shared clock
    private float blitzIncEarnedThisTurn;   // §7 per-turn increment cap bookkeeping
    private int blitzIncTurn = -1;          // which TurnNumber blitzIncEarnedThisTurn refers to
    private float blitzTurnTime;            // §4.2 optional per-turn wall cap remaining
    private bool blitzFlagged;              // flag-fall already dispatched (guard against repeats)

    // Response timer (§9/§10): resets whenever the pending decision changes.
    private float blitzResponse;
    private string blitzPromptSig;          // signature of the current decision, to detect prompt changes

    // Overtime (§2) — filled in by the overtime pass.
    private bool blitzOvertime;
    private float blitzOvertimeClock;
    private int blitzOvertimeEndTurn;       // TurnNumber at which overtime turns run out

    private float blitzLastTickTime = -1f;

    // ── Activation ─────────────────────────────────────────────────────────
    private void BlitzInit(BlitzConfig cfg)
    {
        blitzConfig = cfg;
        blitzFlagged = false;
        blitzOvertime = false;
        blitzIncTurn = -1;
        blitzIncEarnedThisTurn = 0f;
        blitzLastTickTime = -1f;
        blitzPromptSig = null;
        if (cfg == null || !cfg.IsTimed) return;
        blitzSouth = cfg.StartingSecondsFor("south");   // per-seat clocks (asymmetric custom times supported)
        blitzNorth = cfg.StartingSecondsFor("north");
        blitzMatchClock = cfg.startingSeconds;          // Ranked: single shared clock
        blitzTurnTime = cfg.turnCapSeconds;
        blitzResponse = cfg.responseLimitSeconds;
    }

    private float BlitzClockFor(string seat) =>
        blitzOvertime ? blitzOvertimeClock
        : blitzConfig != null && blitzConfig.ModeEnum == TimingMode.Ranked ? blitzMatchClock
        : seat == "south" ? blitzSouth : blitzNorth;

    private void BlitzAddClock(string seat, float delta)
    {
        if (blitzConfig != null && blitzConfig.ModeEnum == TimingMode.Ranked) { blitzMatchClock += delta; return; }
        if (seat == "south") blitzSouth += delta; else blitzNorth += delta;
        float cap = blitzConfig != null ? blitzConfig.maxClockSeconds : 0;
        if (cap > 0)
        {
            if (blitzSouth > cap) blitzSouth = cap;
            if (blitzNorth > cap) blitzNorth = cap;
        }
    }

    // ── Ownership (§8/§9): who owns the live decision? ───────────────────────
    private string BlitzOwner()
    {
        if (state == null || state.Status != "active") return null;
        if (state.DeckLook != null) return state.DeckLook.Seat;
        if (state.ActiveChoice != null) return state.ActiveChoice.Seat ?? state.ActiveChoice.ControllerSeat;
        if (state.PendingEffects.Count > 0) return state.PendingEffects[0].Seat;
        if (state.Battle != null) return state.Battle.PrioritySeat ?? state.Battle.TargetSeat;
        return state.ActiveSeat;
    }

    // True while a card was just dealt/animated or a blocking overlay is up — the clock pauses
    // for purely automatic moments (§9.2) and modal menus.
    private bool BlitzShouldTick()
    {
        if (!BlitzActive || blitzFlagged) return false;
        if (state == null || state.Status != "active") return false;
        if (handDealAnimating || mulliganDealAnimating > 0) return false;
        if (menuOpen || soundMenuOpen || surrenderConfirmOpen) return false;
        if (rewindWaiting || rewindPromptOpen) return false;   // pause during a forgiveness-rewind negotiation
        if (ResultScreenActive() || opponentLeft) return false;
        // Networked: only the local player's own clock is authoritative here; the opponent's
        // clock is driven by their reported time (BlitzOnClockSync). Still tick locally for display.
        return true;
    }

    // Current decision fingerprint — response timer resets when this changes.
    private string BlitzPromptSignature()
    {
        if (state == null) return null;
        string owner = BlitzOwner();
        string kind =
            state.DeckLook != null ? "look:" + state.DeckLook.Step :
            state.ActiveChoice != null ? "choice" :
            state.PendingEffects.Count > 0 ? "effect:" + state.PendingEffects[0].EffectId :
            state.Battle != null ? "battle:" + state.Battle.Step :
            "main";
        return $"{state.TurnNumber}|{owner}|{kind}";
    }

    private bool BlitzResponseIsComplex() =>
        state != null && (state.DeckLook != null
            || (state.PendingEffects.Count > 0 && state.PendingEffects[0].SelectionsRemaining > 1));

    // ── Per-frame tick (called from Update) ──────────────────────────────────
    private void BlitzTick()
    {
        if (!BlitzActive) return;
        float now = Time.unscaledTime;
        if (blitzLastTickTime < 0f) { blitzLastTickTime = now; return; }
        float dt = now - blitzLastTickTime;
        blitzLastTickTime = now;
        if (dt <= 0f || dt > 2f) return;   // clamp huge frames (alt-tab, breakpoints)

        // Reset the response timer whenever the pending decision changes.
        string sig = BlitzPromptSignature();
        if (sig != blitzPromptSig)
        {
            blitzPromptSig = sig;
            blitzResponse = BlitzResponseIsComplex() ? blitzConfig.complexSelectionLimitSeconds : blitzConfig.responseLimitSeconds;
        }

        if (!BlitzShouldTick()) return;
        string owner = BlitzOwner();
        if (string.IsNullOrEmpty(owner)) return;

        // Drain the owner's personal (or match) clock.
        float before = BlitzClockFor(owner);
        BlitzTickClock(owner, dt);
        float after = BlitzClockFor(owner);

        // Optional turn wall-cap (§4.2): drains while the active player is on the clock.
        if (blitzConfig.turnCapSeconds > 0 && owner == state.ActiveSeat)
        {
            blitzTurnTime -= dt;
            if (blitzTurnTime <= 0f) { blitzTurnTime = 0f; BlitzOnTurnCapExpired(); }
        }

        // Response-window timer (§9/§10): auto-resolves the prompt on expiry.
        if (BlitzHasResponsePrompt())
        {
            blitzResponse -= dt;
            if (blitzResponse <= 0f) { blitzResponse = 0f; BlitzOnResponseExpired(owner); }
        }

        // Flag-fall / overtime when the clock crosses zero.
        if (after <= 0f && before > 0f) BlitzOnClockZero(owner);
        // Overtime turn-count end.
        if (blitzOvertime) BlitzCheckOvertimeEnd();
    }

    private void BlitzTickClock(string owner, float dt)
    {
        if (blitzOvertime) { if (blitzOvertimeClock > 0f) blitzOvertimeClock = Mathf.Max(0f, blitzOvertimeClock - dt); return; }
        if (blitzConfig.ModeEnum == TimingMode.Ranked) blitzMatchClock = Mathf.Max(0f, blitzMatchClock - dt);
        else if (owner == "south") blitzSouth = Mathf.Max(0f, blitzSouth - dt);
        else blitzNorth = Mathf.Max(0f, blitzNorth - dt);
    }

    // A "response prompt" is a decision the DEFENDER/non-active player must make in reaction to the
    // opponent (block/counter/trigger, or an opponent-facing choice) — those carry the documented
    // response-timeout default. The ACTIVE player resolving their OWN effect/selection on their own turn
    // is NOT a response: it's backstopped by their personal clock, so no response countdown appears.
    private bool BlitzHasResponsePrompt()
    {
        if (state == null) return false;
        bool pending = state.Battle != null || state.PendingEffects.Count > 0 || state.ActiveChoice != null || state.DeckLook != null;
        if (!pending) return false;
        string ownerSeat = BlitzOwner();
        return !string.IsNullOrEmpty(ownerSeat) && ownerSeat != state.ActiveSeat;
    }

    // ── Increment (§5/§6/§7): award after the engine commits a real action ───
    private static readonly HashSet<string> BlitzActionCommands = new HashSet<string>
    {
        "playCard", "activateMain", "declareAttack", "counterWithCard", "resolveEffect",
        "resolveChoice", "useTrigger", "takeLife", "attachDon", "deckLookSelect",
        "deckLookConfirmOrder", "deckLookScryConfirm",
    };

    // Called from Dispatch AFTER GameEngine.ApplyCommand committed `command`. `changed` = the command
    // mutated the authoritative state (we approximate via command history growth).
    private void BlitzOnCommandApplied(GameCommand command, bool changed)
    {
        if (!BlitzActive || command == null || string.IsNullOrEmpty(command.Seat)) return;
        // Reset per-turn budgets on a new turn (covers both the increment cap and the turn wall-cap).
        if (blitzIncTurn != state.TurnNumber)
        {
            blitzIncTurn = state.TurnNumber;
            blitzIncEarnedThisTurn = 0f;
            blitzTurnTime = blitzConfig.turnCapSeconds;
        }
        if (blitzOvertime) return;   // §2: increment is disabled in overtime
        var trig = blitzConfig.IncrementEnum;
        if (trig == IncrementTrigger.None) return;

        float award = 0f;
        bool isTurnEnd = command.Type == "endTurn";
        bool isAction = changed && BlitzActionCommands.Contains(command.Type);

        if ((trig == IncrementTrigger.Action || trig == IncrementTrigger.Hybrid) && isAction)
            award += blitzConfig.incrementSeconds;
        if ((trig == IncrementTrigger.Turn || trig == IncrementTrigger.Hybrid) && isTurnEnd)
            award += blitzConfig.turnIncrementSeconds;
        if (award <= 0f) return;

        // §7 per-turn cap on ACTION increment (turn-end bonus is exempt).
        if (blitzConfig.perTurnIncrementCapSeconds > 0 && !isTurnEnd)
        {
            float room = blitzConfig.perTurnIncrementCapSeconds - blitzIncEarnedThisTurn;
            award = Mathf.Min(award, Mathf.Max(0f, room));
            blitzIncEarnedThisTurn += award;
        }
        if (award > 0f)
        {
            BlitzAddClock(command.Seat, award);
            SpawnBlitzIncrementFlash(command.Seat, Mathf.RoundToInt(award));
        }
    }

    // ── Expiry handlers ──────────────────────────────────────────────────────
    private void BlitzOnClockZero(string owner)
    {
        if (blitzFlagged) return;
        if (blitzOvertime) { BlitzResolveOvertime(); return; }              // shared overtime clock ran out
        if (blitzConfig.overtimeEnabled && (blitzConfig.overtimeSeconds > 0 || blitzConfig.overtimeTurns > 0))
        { BlitzEnterOvertime(owner); return; }
        BlitzFlagFall(owner);
    }

    // Flag-fall: the owner loses on time. Concede(owner) makes them lose while keeping the engine
    // time-agnostic. Networked: only the local owner declares its own flag-fall (authoritative).
    private void BlitzFlagFall(string owner)
    {
        if (blitzFlagged || state == null || state.Status == "finished") return;
        if (isNetworked && owner != localSeat) return;   // trust the owner's own client to flag
        blitzFlagged = true;
        SandboxSafeLog($"{DisplayName(owner)} ran out of time.");
        Dispatch(new GameCommand { Type = "concede", Seat = owner });
    }

    // Overtime (§2): finish the current turn (Turn 0), then N more turns; a shared clock (if set)
    // caps the total. When it ends, the winner is decided by Life, then tiebreakers, then a draw.
    private void BlitzEnterOvertime(string owner)
    {
        if (blitzOvertime) return;
        blitzOvertime = true;
        blitzOvertimeClock = blitzConfig.overtimeSeconds;   // 0 = turn-count only
        blitzOvertimeEndTurn = state.TurnNumber + Mathf.Max(1, blitzConfig.overtimeTurns);
        SandboxSafeLog($"Time! Overtime — finish this turn, then {blitzConfig.overtimeTurns} more. No increment.");
        Render();
    }

    private void BlitzCheckOvertimeEnd()
    {
        if (!blitzOvertime || blitzFlagged || state == null || state.Status != "active") return;
        if (state.TurnNumber > blitzOvertimeEndTurn) BlitzResolveOvertime();
    }

    private void BlitzResolveOvertime()
    {
        if (blitzFlagged || state == null || state.Status == "finished") return;
        if (isNetworked && localSeat != "south") { blitzFlagged = true; return; }   // host resolves; guest gets the synced result
        int ls = state.Players["south"].Life.Count, ln = state.Players["north"].Life.Count;
        string loser = ls < ln ? "south" : ln < ls ? "north" : null;
        if (loser == null && blitzConfig.useTiebreakers)
        {
            int ps = BlitzBoardPower("south"), pn = BlitzBoardPower("north");
            loser = ps < pn ? "south" : pn < ps ? "north" : null;
        }
        blitzFlagged = true;
        if (loser == null)
        {
            if (blitzConfig.allowDraw) { BlitzDeclareDraw(); return; }
            loser = state.FirstPlayer;   // deterministic fallback: the player who went first loses the tie
        }
        SandboxSafeLog($"Overtime ended — {DisplayName(OtherSeatB(loser))} wins on tiebreakers.");
        Dispatch(new GameCommand { Type = "concede", Seat = loser });
    }

    private int BlitzBoardPower(string seat)
    {
        var p = state.Players[seat];
        int total = p.Leader != null ? GameEngine.GetPower(state, p.Leader) : 0;
        foreach (var c in p.CharacterArea) if (c != null) total += GameEngine.GetPower(state, c);
        return total;
    }

    private void BlitzDeclareDraw()
    {
        state.Status = "finished";
        state.Phase = "finished";
        finishedResultText = "DRAW — TIME";
        SandboxSafeLog("Overtime ended level — the match is a draw.");
        Render();
    }

    private static string OtherSeatB(string seat) => seat == "south" ? "north" : "south";

    // §4.2 turn wall-cap: when it runs out the active player can't start a new action — force the
    // turn to end once the board is quiescent (any resolving sequence finishes first).
    private void BlitzOnTurnCapExpired()
    {
        if (state == null || state.Status != "active") return;
        if (isNetworked && state.ActiveSeat != localSeat) return;
        if (state.Battle == null && state.PendingEffects.Count == 0 && state.ActiveChoice == null && state.DeckLook == null)
            Dispatch(new GameCommand { Type = "endTurn", Seat = state.ActiveSeat });
    }

    // §9 timeout-defaults for defender/choice windows: on response-timer expiry, auto-issue the
    // documented default so a prompt can't stall the match. Mandatory selections on your own turn
    // aren't auto-resolved here — the personal clock is their backstop.
    private void BlitzOnResponseExpired(string owner)
    {
        void ResetResp() => blitzResponse = BlitzResponseIsComplex() ? blitzConfig.complexSelectionLimitSeconds : blitzConfig.responseLimitSeconds;
        if (state == null || string.IsNullOrEmpty(owner) || state.Status != "active") { ResetResp(); return; }
        if (isNetworked && owner != localSeat) { ResetResp(); return; }   // opponent's client waits for the synced command
        if (state.Battle != null)
        {
            switch (state.Battle.Step)
            {
                case "block": Dispatch(new GameCommand { Type = "passBlock", Seat = owner }); return;
                case "counter": Dispatch(new GameCommand { Type = "passCounter", Seat = owner }); return;   // keeps already-committed counters
                case "trigger": Dispatch(new GameCommand { Type = "passTrigger", Seat = owner }); return;
            }
        }
        if (state.ActiveChoice != null) { Dispatch(new GameCommand { Type = "resolveChoice", Seat = owner, Target = "A" }); return; }
        if (state.PendingEffects.Count > 0 && state.PendingEffects[0].Optional)
        { Dispatch(new GameCommand { Type = "passEffect", Seat = owner, EffectId = state.PendingEffects[0].EffectId }); return; }
        ResetResp();
    }

    private void SandboxSafeLog(string msg)
    {
        if (state == null) return;
        state.EventLog.Add(new LogEntry { Actor = "blitz", Message = msg, Turn = state.TurnNumber, Sequence = state.LogSequence++ });
    }

    // ── HUD ──────────────────────────────────────────────────────────────────
    private RectTransform blitzRoot;
    // Live text refs so the numbers tick every frame without a full (expensive) Render — only the
    // owner highlight/border, which changes at command boundaries, relies on a full DrawBlitz.
    private Text blitzClockTextSouth, blitzClockTextNorth, blitzResponseText;

    // Per-frame: refresh the clock digits + warning colours in place (called from Update).
    private void BlitzHudTick()
    {
        if (!BlitzActive || blitzConfig == null) return;
        if (blitzClockTextSouth != null) BlitzStyleClockText(blitzClockTextSouth, BlitzClockFor("south"));
        if (blitzClockTextNorth != null) BlitzStyleClockText(blitzClockTextNorth, BlitzClockFor("north"));
        if (blitzResponseText != null)
        {
            bool crit = blitzResponse <= 5f;
            blitzResponseText.text = $"RESPOND  {Mathf.CeilToInt(Mathf.Max(0f, blitzResponse))}s";
            blitzResponseText.color = crit ? new Color32(240, 150, 150, 255) : Gold;
        }
    }

    private void BlitzStyleClockText(Text t, float secs)
    {
        if (t == null) return;
        var cfg = blitzConfig;
        t.text = cfg.ModeEnum == TimingMode.Ranked ? "⏱ " + BlitzConfig.Format(secs) : BlitzConfig.Format(secs);
        t.color = secs <= cfg.criticalWarningSeconds ? new Color32(240, 90, 90, 255)
            : secs <= cfg.finalWarningSeconds ? new Color32(240, 180, 80, 255)
            : (Color)Ink;
    }

    private void DrawBlitz()
    {
        if (!BlitzActive) { if (blitzRoot != null) Clear(blitzRoot); blitzClockTextSouth = blitzClockTextNorth = blitzResponseText = null; return; }
        blitzClockTextSouth = blitzClockTextNorth = blitzResponseText = null;
        if (blitzRoot == null)
        {
            blitzRoot = PanelObject("Blitz Root", canvas.transform, new Color(0, 0, 0, 0));
            Stretch(blitzRoot, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            blitzRoot.GetComponent<Image>().raycastTarget = false;
        }
        Clear(blitzRoot);
        blitzRoot.SetAsLastSibling();

        string owner = BlitzOwner();
        // Two clock chips: top player and bottom player, on the right edge above the side panel gap.
        DrawBlitzClock(TopSeat, owner, true);
        DrawBlitzClock(BottomSeat, owner, false);

        if (blitzOvertime)
        {
            int turnsLeft = Mathf.Max(0, blitzOvertimeEndTurn - state.TurnNumber);
            var ot = PanelObject("Blitz OT", blitzRoot, (Color)new Color32(60, 20, 20, 240));
            ot.anchorMin = ot.anchorMax = new Vector2(1f, 0.5f);
            ot.pivot = new Vector2(1f, 0.5f);
            ot.sizeDelta = new Vector2(132f, 30f);
            ot.anchoredPosition = new Vector2(-12f, 0f);
            Round(ot);
            AddRoundedCardBorder(ot, new Color32(240, 120, 120, 255), 1.4f);
            var t = TextObject("t", ot, $"OVERTIME · {turnsLeft} left", 9, new Color32(245, 170, 170, 255), TextAnchor.MiddleCenter, monoFont);
            t.fontStyle = FontStyle.Bold;
            Stretch(t.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        }

        // Response-timer pill (only while a defender/selection prompt is live and the timer is meaningful).
        if (BlitzHasResponsePrompt() && !string.IsNullOrEmpty(owner))
        {
            var pill = PanelObject("Blitz Resp", blitzRoot, (Color)new Color32(20, 16, 10, 235));
            pill.anchorMin = pill.anchorMax = new Vector2(0.5f, 1f);
            pill.pivot = new Vector2(0.5f, 1f);
            pill.sizeDelta = new Vector2(190f, 26f);
            pill.anchoredPosition = new Vector2(0f, -8f);
            Round(pill);
            bool crit = blitzResponse <= 5f;
            AddRoundedCardBorder(pill, crit ? (Color)new Color32(230, 90, 90, 255) : Gold, 1.2f);
            var t = TextObject("t", pill, $"RESPOND  {Mathf.CeilToInt(blitzResponse)}s", 11,
                crit ? (Color)new Color32(240, 150, 150, 255) : Gold, TextAnchor.MiddleCenter, monoFont);
            Stretch(t.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            blitzResponseText = t;
        }
    }

    private const float BlitzClockW = 150f, BlitzClockH = 58f;
    // Screen-space point (just right of each clock) where the "+Ns" flash pops; recorded in DrawBlitzClock.
    private Vector2 blitzFlashAnchorSouth, blitzFlashAnchorNorth;

    private void DrawBlitzClock(string seat, string owner, bool top)
    {
        float secs = BlitzClockFor(seat);
        bool isOwner = seat == owner;
        var cfg = blitzConfig;
        Color face = secs <= cfg.criticalWarningSeconds ? new Color32(240, 90, 90, 255)
            : secs <= cfg.finalWarningSeconds ? new Color32(240, 180, 80, 255)
            : (Color)Ink;
        Color border = isOwner ? (Color)Gold : (Color)new Color32(90, 100, 116, 110);

        // Parent the clock into the player's board HALF and pin it to the fraction that sits between the
        // Stage (x≈0.152) and Deck (x≈0.246) zones (see DrawCoreZones), vertically on that zone row.
        // The half rects are always available (set in DrawReferencePlaymat), so this never falls back.
        RectTransform half = top ? northHalfRect : southHalfRect;
        var chip = PanelObject("Blitz Clock " + seat, half != null ? (Transform)half : blitzRoot.transform,
            isOwner ? (Color)new Color32(30, 40, 56, 250) : (Color)new Color32(16, 20, 28, 225));
        chip.sizeDelta = new Vector2(BlitzClockW, BlitzClockH);
        if (half != null)
        {
            chip.pivot = new Vector2(0.5f, 0.5f);
            if (stageZoneRects.TryGetValue(seat, out var sr) && sr != null
                && boardDeckPileRects.TryGetValue(seat, out var dr) && dr != null)
            {
                // EXACT: the midpoint of the Stage and Deck zone centers, in the half's local space —
                // horizontally equidistant and vertically level with those cards, for each player.
                Vector2 sLocal = half.InverseTransformPoint(sr.TransformPoint((Vector3)sr.rect.center));
                Vector2 dLocal = half.InverseTransformPoint(dr.TransformPoint((Vector3)dr.rect.center));
                chip.anchorMin = chip.anchorMax = new Vector2(0.5f, 0.5f);
                chip.anchoredPosition = (sLocal + dLocal) * 0.5f;
            }
            else
            {
                chip.anchorMin = chip.anchorMax = new Vector2(0.199f, 0.435f);   // fallback fraction
                chip.anchoredPosition = Vector2.zero;
            }
            chip.SetAsLastSibling();   // above the zone cards
        }
        else
        {
            chip.anchorMin = chip.anchorMax = new Vector2(0.815f, top ? 0.86f : 0.14f);
            chip.pivot = new Vector2(1f, 0.5f);
            chip.anchoredPosition = Vector2.zero;
        }
        RoundBig(chip);
        AddRoundedCardBorder(chip, border, isOwner ? 3f : 1.2f);

        // Record where the "+Ns" flash pops — just right of the chip, in screen space (works even though
        // the flash lives on the canvas so it can survive the turn-change re-render).
        Vector2 chipCenter = RectScreenCenter(chip);
        var flashPt = new Vector2(chipCenter.x + BlitzClockW * 0.5f + 14f, chipCenter.y);
        if (seat == "south") blitzFlashAnchorSouth = flashPt; else blitzFlashAnchorNorth = flashPt;

        string name = seat == "south" ? southNameOrDefaultB() : northNameOrDefaultB();
        var nm = TextObject("n", chip, (isOwner ? "▶ " : "") + name, 10, isOwner ? (Color)Gold : Muted, TextAnchor.MiddleCenter, monoFont);
        nm.fontStyle = FontStyle.Bold;
        Stretch(nm.rectTransform, new Vector2(0.06f, 0.60f), new Vector2(0.94f, 0.97f), Vector2.zero, Vector2.zero);
        // Time is vertically centered in the box.
        var clk = TextObject("c", chip,
            cfg.ModeEnum == TimingMode.Ranked ? "⏱ " + BlitzConfig.Format(secs) : BlitzConfig.Format(secs),
            28, face, TextAnchor.MiddleCenter, titleFont);
        clk.fontStyle = FontStyle.Bold;
        Stretch(clk.rectTransform, new Vector2(0.06f, 0.03f), new Vector2(0.94f, 0.60f), Vector2.zero, Vector2.zero);
        if (seat == "south") blitzClockTextSouth = clk; else blitzClockTextNorth = clk;
    }

    // Floating "+Ns" that rises + fades to the RIGHT of a player's clock when they bank increment time.
    private void SpawnBlitzIncrementFlash(string seat, int amount)
    {
        if (canvas == null || amount <= 0) return;
        Vector2 pt = seat == "south" ? blitzFlashAnchorSouth : blitzFlashAnchorNorth;
        if (pt == Vector2.zero) return;   // clock hasn't been laid out yet
        var flash = PanelObject("Blitz Inc Flash", canvas.transform, new Color(0, 0, 0, 0));
        flash.pivot = new Vector2(0f, 0.5f);
        flash.sizeDelta = new Vector2(84f, 34f);
        flash.position = new Vector3(pt.x, pt.y, 0f);
        flash.GetComponent<Image>().raycastTarget = false;
        var t = TextObject("t", flash, "+" + amount + "s", 22, new Color32(120, 245, 175, 255), TextAnchor.MiddleCenter, titleFont);
        t.fontStyle = FontStyle.Bold;
        t.raycastTarget = false;
        Stretch(t.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        flash.gameObject.AddComponent<BlitzFloatFade>().Init(t, flash);
        flash.SetAsLastSibling();
    }

    private string southNameOrDefaultB() => string.IsNullOrEmpty(southDisplayName) ? "South" : southDisplayName;
    private string northNameOrDefaultB() => string.IsNullOrEmpty(northDisplayName) ? "North" : northDisplayName;
}

// A "+Ns" increment flash that rises and fades beside a Blitz clock, then self-destructs. Runs on
// unscaled time so it animates independent of the (event-driven) UI rebuilds.
public sealed class BlitzFloatFade : MonoBehaviour
{
    private UnityEngine.UI.Text text;
    private RectTransform rt;
    private Vector2 startPos;
    private float t;
    private const float Duration = 1.15f;

    public void Init(UnityEngine.UI.Text txt, RectTransform r)
    {
        text = txt; rt = r; startPos = r.anchoredPosition;
    }

    private void Update()
    {
        t += Time.unscaledDeltaTime;
        float k = Mathf.Clamp01(t / Duration);
        if (rt != null) rt.anchoredPosition = startPos + new Vector2(0f, 28f * k);
        if (text != null) { var c = text.color; c.a = 1f - k; text.color = c; }
        if (t >= Duration) Destroy(gameObject);
    }
}
