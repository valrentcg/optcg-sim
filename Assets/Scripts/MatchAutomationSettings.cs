using UnityEngine;

/// <summary>
/// Per-player convenience toggles for things the client can do on your behalf during a match, plus the
/// "ask me first" confirmations. Same "optcg.*" PlayerPrefs convention as <see cref="CursorSettings"/>
/// and <see cref="DisplaySettings"/>. Modelled on the equivalent options in the other OPTCG sim, which
/// players already expect to find.
///
/// ── THE ONE RULE FOR ANYTHING ADDED HERE ────────────────────────────────────────────────────────
/// These settings may ONLY drive the CLIENT. Never make engine behaviour conditional on one.
///
/// Both clients in a networked match replay the SAME GameCommand log, so the engine must compute an
/// identical state on both sides. A local preference that changed what the engine does would make the
/// two boards drift apart — silently, because nothing on the wire would look wrong. It is exactly the
/// desync MatchStartPayload.build exists to prevent, arriving through the front door.
///
/// Safe shape: the engine always presents the step, and the client optionally answers it for you by
/// dispatching the ordinary command (draw / passTrigger / ...). That command travels the normal network
/// path, so the opponent sees precisely what they would have seen had you clicked it yourself.
///
/// This is also why "auto-skip the block step when you have no Blockers" is NOT a toggle here: we do
/// that in the ENGINE (MaybeAutoPassBlock), identically on both clients, which is correct and must stay
/// deterministic. It costs nothing in information terms either — blocker availability is public, since
/// the board is visible to both players.
/// </summary>
public static class MatchAutomationSettings
{
    private const string KeyAutoDraw        = "optcg.auto.draw";
    private const string KeyAutoPassTrigger = "optcg.auto.passTrigger";
    private const string KeyConfirmEndTurn  = "optcg.confirm.endTurn";
    private const string KeyConfirmDon      = "optcg.confirm.don";
    private const string KeyConfirmCounter  = "optcg.confirm.counter";
    private const string KeyHideNames       = "optcg.privacy.hideNames";
    private const string KeyMuteMatchChat   = "optcg.chat.muteMatch";

    private static bool Get(string key, bool dflt) => PlayerPrefs.GetInt(key, dflt ? 1 : 0) == 1;
    private static void Set(string key, bool v) { PlayerPrefs.SetInt(key, v ? 1 : 0); PlayerPrefs.Save(); }

    /// <summary>Draw for your turn automatically instead of clicking Draw. Default ON — the draw is
    /// mandatory and has no decision attached, so the click is pure ceremony.</summary>
    public static bool AutoDraw
    {
        get => Get(KeyAutoDraw, true);
        set => Set(KeyAutoDraw, value);
    }

    /// <summary>Pass the Trigger step automatically when the revealed Life card has no [Trigger].
    ///
    /// DEFAULT OFF, and it must stay that way. The engine now always enters the Trigger step precisely
    /// so the attacker cannot tell a Trigger card from a blank one by whether the game paused. Turning
    /// this on re-creates that tell for YOUR Life cards: an instant pass means "no Trigger", every time,
    /// on every damage event. That is real hidden information about your deck and your remaining Life.
    /// Offered because the convenience is genuine and the cost is yours to choose — not defaulted,
    /// because a default that quietly leaks is not a default anyone consented to.</summary>
    public static bool AutoPassTriggerWhenNone
    {
        get => Get(KeyAutoPassTrigger, false);
        set => Set(KeyAutoPassTrigger, value);
    }

    /// <summary>Require a second click on End Turn. Default ON: ending a turn early is unrecoverable
    /// and is the most common misclick in the game.</summary>
    public static bool ConfirmEndTurn
    {
        get => Get(KeyConfirmEndTurn, false);
        set => Set(KeyConfirmEndTurn, value);
    }

    /// <summary>Require a confirm step after attaching DON!! Default OFF — attaching is frequent and
    /// mostly reversible in practice, so a prompt every time is friction for most players.</summary>
    public static bool ConfirmDonAttach
    {
        get => Get(KeyConfirmDon, false);
        set => Set(KeyConfirmDon, value);
    }

    /// <summary>Ask before spending a card as a Counter, rather than committing on the click.
    /// Default ON: a Counter permanently trashes a card from your hand.</summary>
    public static bool ConfirmCounter
    {
        get => Get(KeyConfirmCounter, false);
        set => Set(KeyConfirmCounter, value);
    }

    /// <summary>Hide both players' names during multiplayer (streamer privacy). Default OFF.</summary>
    public static bool HidePlayerNames
    {
        get => Get(KeyHideNames, false);
        set => Set(KeyHideNames, value);
    }

    /// <summary>Suppress ALL incoming in-match chat, permanently, across matches. Default OFF.
    ///
    /// Ranked and casual pair you with strangers, and blocking only exists for friends — so without
    /// this the only escape from an abusive opponent was to leave the match, which dispatches a
    /// concede and takes the loss. Nobody should have to forfeit a game to stop reading something.
    /// The chat panel also carries a per-match mute for players who want chat on by default and just
    /// want to silence one opponent.</summary>
    public static bool MuteMatchChat
    {
        get => Get(KeyMuteMatchChat, false);
        set => Set(KeyMuteMatchChat, value);
    }
}
