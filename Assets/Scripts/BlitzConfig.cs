// One Piece TCG - BLITZ / timed-match configuration model.
// Pure data (JsonUtility-serializable so it can ride in the networked MatchStartPayload). Blitz
// changes only time management, never rules — see the Blitz spec §1. This holds every knob from
// the spec (§2–§10): clock preset, increment mode, response/complex/turn limits, overtime, caps.

using System;

// The three custom-match timing modes the user picks at lobby creation.
//  Standard  – no clock at all (the classic experience).
//  Ranked    – a single shared match clock + rulebook-style overtime (finish current turn + N turns).
//  Blitz     – personal chess-style clocks per player, with increment (the detailed spec).
public enum TimingMode { Standard, Ranked, Blitz }

// How increment (chess-style bonus time) is granted. See spec §5.2.
public enum IncrementTrigger { None, Turn, Action, Hybrid }

[Serializable]
public sealed class BlitzConfig
{
    public string mode = "standard";        // TimingMode as string (JsonUtility has no enum-name control)
    public string presetName = "Custom";

    // ── Personal clock (Blitz) / match clock (Ranked) ──
    public int startingSeconds = 450;        // SOUTH's personal clock (Blitz) or shared match length (Ranked)
    public int startingSecondsNorth = 0;     // NORTH's personal clock (Blitz only); 0 → mirror startingSeconds.
                                             // Host is always south / guest north (see StartMatchClicked), so in a
                                             // custom lobby this is the guest's clock. Ranked ignores it (one shared clock).
    public int maxClockSeconds = 0;          // 0 = uncapped (§4.1)

    // ── Increment (Blitz) ──
    public string incrementTrigger = "action";   // IncrementTrigger name
    public int incrementSeconds = 2;              // per completed action (§5)
    public int turnIncrementSeconds = 3;          // hybrid/turn mode: per completed turn
    public int perTurnIncrementCapSeconds = 20;   // §7: max increment banked in one turn (0 = uncapped)

    // ── Response / selection / turn limits (§9, §10) ──
    public int responseLimitSeconds = 15;         // block/counter/trigger prompt cap
    public int complexSelectionLimitSeconds = 20; // multi-pick selection cap
    public int turnCapSeconds = 0;                // §4.2 optional per-turn wall cap (0 = none)

    // ── Warnings (§2) ──
    public int finalWarningSeconds = 30;
    public int criticalWarningSeconds = 10;

    // ── Overtime (§2) ──
    public bool overtimeEnabled = true;
    public int overtimeSeconds = 300;             // shared overtime clock length
    public int overtimeTurns = 3;                 // current turn = Turn 0, then Turns 1..N
    public bool useTiebreakers = true;
    public bool allowDraw = true;                 // ranked: only if tiebreakers can't decide

    public TimingMode ModeEnum =>
        mode == "blitz" ? TimingMode.Blitz : mode == "ranked" ? TimingMode.Ranked : TimingMode.Standard;

    public IncrementTrigger IncrementEnum => incrementTrigger switch
    {
        "turn" => IncrementTrigger.Turn,
        "action" => IncrementTrigger.Action,
        "hybrid" => IncrementTrigger.Hybrid,
        _ => IncrementTrigger.None,
    };

    public bool IsTimed => ModeEnum != TimingMode.Standard;

    // Per-seat starting clock. NORTH falls back to startingSeconds when startingSecondsNorth is unset
    // (the symmetric case). Ranked has one shared clock, so seat doesn't matter there.
    public int StartingSecondsFor(string seat) =>
        seat == "north" && startingSecondsNorth > 0 ? startingSecondsNorth : startingSeconds;

    public bool AsymmetricClocks => startingSecondsNorth > 0 && startingSecondsNorth != startingSeconds;

    public BlitzConfig Clone() => (BlitzConfig)MemberwiseClone();

    // ── Presets (spec §3) ──
    public static BlitzConfig Standard() => new BlitzConfig { mode = "standard", presetName = "Standard" };

    // Increment is granted PER TURN (+5s at end of turn), not per action.
    public static BlitzConfig Bullet() => new BlitzConfig
    {
        mode = "blitz", presetName = "Bullet",
        startingSeconds = 300, incrementTrigger = "turn", incrementSeconds = 0, turnIncrementSeconds = 5,
        responseLimitSeconds = 10, complexSelectionLimitSeconds = 15, perTurnIncrementCapSeconds = 0,
    };

    public static BlitzConfig Blitz() => new BlitzConfig
    {
        mode = "blitz", presetName = "Blitz",
        startingSeconds = 450, incrementTrigger = "turn", incrementSeconds = 0, turnIncrementSeconds = 5,
        responseLimitSeconds = 15, complexSelectionLimitSeconds = 20, perTurnIncrementCapSeconds = 0,
    };

    public static BlitzConfig Rapid() => new BlitzConfig
    {
        mode = "blitz", presetName = "Rapid",
        startingSeconds = 720, incrementTrigger = "turn", incrementSeconds = 0, turnIncrementSeconds = 5,
        responseLimitSeconds = 20, complexSelectionLimitSeconds = 25, perTurnIncrementCapSeconds = 0,
    };

    // Custom Blitz: independent per-player starting clocks (asymmetric / handicap allowed). Increment
    // and the response/selection limits inherit the standard Blitz preset — only the two clocks differ.
    // southSeconds = host's clock, northSeconds = guest's (host is always south).
    public static BlitzConfig Custom(int southSeconds, int northSeconds)
    {
        var c = Blitz();
        c.presetName = "Custom";
        c.startingSeconds = Math.Max(1, southSeconds);
        c.startingSecondsNorth = Math.Max(1, northSeconds);
        return c;
    }

    // Fuller custom overload: also set the per-TURN increment (+Ns at end of turn) and the two
    // response-window limits.
    public static BlitzConfig Custom(int southSeconds, int northSeconds, int perTurnIncrement, int responseLimit, int complexLimit)
    {
        var c = Custom(southSeconds, northSeconds);
        c.incrementTrigger = "turn";
        c.incrementSeconds = 0;
        c.turnIncrementSeconds = Math.Max(0, perTurnIncrement);
        c.perTurnIncrementCapSeconds = 0;
        c.responseLimitSeconds = Math.Max(1, responseLimit);
        c.complexSelectionLimitSeconds = Math.Max(1, complexLimit);
        return c;
    }

    // Rulebook-style ranked timing: one shared match clock + overtime, no per-action increment.
    public static BlitzConfig Ranked() => new BlitzConfig
    {
        mode = "ranked", presetName = "Ranked",
        startingSeconds = 1500,                    // 25-min shared match clock (placeholder; confirm vs rulebook)
        incrementTrigger = "none", incrementSeconds = 0, perTurnIncrementCapSeconds = 0,
        responseLimitSeconds = 30, complexSelectionLimitSeconds = 45, turnCapSeconds = 0,
        overtimeEnabled = true, overtimeSeconds = 0, overtimeTurns = 3, useTiebreakers = true, allowDraw = true,
    };

    // mm:ss for the HUD.
    public static string Format(float seconds)
    {
        if (seconds < 0f) seconds = 0f;
        int total = (int)System.Math.Ceiling(seconds);
        int m = total / 60, s = total % 60;
        return $"{m}:{s:00}";
    }
}
