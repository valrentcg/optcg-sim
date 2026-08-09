// One Piece TCG - Player identity: globally-unique usernames (16-char cap, server-side
// profanity filter, enforced by Cloud Code since Cloud Save's Custom Data registry isn't
// client-writable) and email/password account recovery layered onto the anonymous UGS
// identity LobbyManager already establishes. Sibling to LobbyManager rather than folded
// into it - LobbyManager is scoped to session/lobby lifecycle, identity is a distinct
// concern MatchNetworkSync and a future FriendsManager will also depend on independently
// of whether a lobby is active.
//
// CREDENTIALS ARE UNITY'S PROBLEM, NOT OURS (2026-07-31 migration).
// This used to sit on Unity's Username/Password identity provider, which has NO built-in
// "forgot password" flow - so recovery was a hand-rolled stack: a mailed token in Cloud
// Save, Resend as the mail vendor, a service account, and an admin API call to force-set
// the password. That was ~400 lines of JS, three secrets and a third-party dependency, and
// it shipped broken (the admin call went to a URL that didn't exist).
//
// It is now Unity Player Accounts: a hosted browser flow that owns signup, EMAIL
// VERIFICATION (which we never had) and password reset. Unity sends the mail. We store no
// passwords and no email->account index. Recovery happens entirely at accounts.unity.com,
// so there is nothing here for us to break.
//
// Player Accounts ships INSIDE com.unity.services.authentication (the standalone
// com.unity.services.playeraccounts package is a 2023 pre-release relic - do not install
// it). The assembly is autoReferenced, so no manifest change was needed.
//
// What did NOT change: usernames. ClaimUsername.js still owns the unique 16-char display
// name + profanity filter, because friend lookup depends on that uniqueness and Unity's own
// Player Names feature would only offer "Name#1234". Login is now email-based and fully
// decoupled from the display name, which is strictly cleaner than the old arrangement where
// the claimed username doubled as the Unity Auth login.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Authentication.PlayerAccounts;
using Unity.Services.CloudCode;
using Unity.Services.CloudSave;
using Unity.Services.Core;
using UnityEngine;

public enum AccountFailureReason
{
    None,
    Empty,
    TooLong,
    BadChars,
    Profanity,
    NameTaken,
    AlreadyHasUsername,
    EmailAlreadyLinked,
    InvalidCredentials,
    InvalidOrExpiredToken,
    NoNetwork,
    // Player Accounts outcomes. Cancelled is a first-class, non-error result: the
    // browser flow is trivially abandonable (player closes the tab, alt-tabs away
    // and forgets) and that must not read as a failure in the UI.
    Cancelled,
    NotConfigured,
    ServerMisconfigured,
    Unknown,
}

public readonly struct AccountResult
{
    public bool Ok { get; }
    public AccountFailureReason Reason { get; }
    public string Message { get; }

    private AccountResult(bool ok, AccountFailureReason reason, string message)
    {
        Ok = ok;
        Reason = reason;
        Message = message;
    }

    public static AccountResult Success() => new AccountResult(true, AccountFailureReason.None, null);
    public static AccountResult Fail(AccountFailureReason reason, string message) => new AccountResult(false, reason, message);
}

public readonly struct UsernameClaimResult
{
    public bool Ok { get; }
    public AccountFailureReason Reason { get; }
    public string Username { get; }

    private UsernameClaimResult(bool ok, AccountFailureReason reason, string username)
    {
        Ok = ok;
        Reason = reason;
        Username = username;
    }

    public static UsernameClaimResult Success(string username) => new UsernameClaimResult(true, AccountFailureReason.None, username);
    public static UsernameClaimResult Fail(AccountFailureReason reason) => new UsernameClaimResult(false, reason, null);
}

public static class AccountManager
{
    private const string UsernameCloudSaveKey = "username";

    public static string CurrentUsername { get; private set; }

    /// <summary>True when this session is backed by a Unity Player Account rather than
    /// a bare anonymous session. An anonymous account signed out is unrecoverable, so the
    /// sign-out flow warns when this is false.
    ///
    /// THIS CANNOT JUST ASK PlayerAccountService. Its refresh token is held in memory only
    /// (verified in the SDK source - nothing persists it), so after an app restart
    /// PlayerAccountService.IsSignedIn is FALSE even though Unity Authentication has
    /// happily restored the real session from its own cached token. Trusting it alone
    /// would tell a perfectly good account "you're playing on this device only" on every
    /// launch, and arm the "you'll lose this account forever" sign-out warning. That is
    /// the same restart-blindness the old email-linked flag had, arriving by a new route.
    /// Three sources, cheapest first, any one of them is proof.</summary>
    public static bool HasUnityAccount
    {
        get
        {
            // 1. Browser sign-in completed during THIS run.
            try { if (PlayerAccountService.Instance.IsSignedIn) return true; }
            catch { /* services not initialized yet */ }

            // 2. A restored UGS session carrying a linked Unity identity. Needs
            //    RefreshUnityAccountAsync to have run - PlayerInfo is bare at boot.
            try
            {
                if (!string.IsNullOrEmpty(AuthenticationService.Instance.PlayerInfo?.GetUnityId()))
                    return true;
            }
            catch { /* not signed in yet */ }

            // 3. Local memory, keyed per player. Set when we KNOW (a successful sign-in),
            //    and only ever upgrades to true - an identity list that comes back empty
            //    is not treated as proof of absence.
            try { return PlayerPrefs.GetInt(UnityAccountPrefKey, 0) == 1; }
            catch { return false; }
        }
    }

    private static string UnityAccountPrefKey => "account_unity_linked_" + SafePlayerId();
    private static string SafePlayerId()
    {
        try { return AuthenticationService.Instance.PlayerId ?? "none"; }
        catch { return "none"; }
    }
    private static void RememberUnityAccount()
    {
        PlayerPrefs.SetInt(UnityAccountPrefKey, 1);
        PlayerPrefs.Save();
    }

    /// <summary>Populates PlayerInfo.Identities for a session restored at boot, so
    /// HasUnityAccount's source #2 can answer. A bare restored PlayerInfo has an empty
    /// identity list until this is fetched. Never throws - offline just leaves the other
    /// two sources to answer.</summary>
    public static async Task RefreshUnityAccountAsync()
    {
        try
        {
            var info = await AuthenticationService.Instance.GetPlayerInfoAsync();
            if (!string.IsNullOrEmpty(info?.GetUnityId())) RememberUnityAccount();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"RefreshUnityAccount failed: {ex.Message}");
        }
    }

    /// <summary>The account's email, straight from the Player Accounts ID token - we do
    /// not store it. Null until the player signs in through the browser flow (an
    /// anonymous or guest session has no email), and null if the `email` OAuth scope is
    /// ever turned off in Project Settings.</summary>
    public static string AccountEmail
    {
        get
        {
            try { return PlayerAccountService.Instance.IdTokenClaims?.Email; }
            catch { return null; }
        }
    }

    /// <summary>Whether Unity has confirmed the player owns that address. Unity drives
    /// verification itself; we only report it. The old hand-rolled flow had no concept of
    /// this at all - anyone could register any address they didn't own.</summary>
    public static bool AccountEmailVerified
    {
        get
        {
            try { return PlayerAccountService.Instance.IdTokenClaims?.EmailVerified ?? false; }
            catch { return false; }
        }
    }

    // Stable key for scoping LOCAL storage (decks, replays) to whoever is using
    // the game right now, so accounts and guests on the same machine never see
    // each other's data. Signed-in accounts use their UGS player id; guests get
    // a key built from their RANDOM GuestId (NOT their guest name — see GuestId, which is a fresh
    // Guid per "Continue as guest"); "local" is the brief pre-sign-in window at boot (nothing
    // user-visible loads that early in practice).
    //
    // This value is used as a PATH SEGMENT (DeckStore.Dir, ReplayStore scoping), so it must never
    // contain user-typed text: every branch here is either a UGS-issued PlayerId, "guest_" + 12 hex
    // chars, or the literal "local". Deck NAMES are likewise never filenames — decks live in a single
    // decks.json per identity — which is why no name sanitising is needed on our side.
    public static string CurrentIdentityKey
    {
        get
        {
            if (IsGuest) return "guest_" + GuestId;
            try
            {
                if (AuthenticationService.Instance.IsSignedIn)
                    return AuthenticationService.Instance.PlayerId;
            }
            catch { /* services not initialized yet */ }
            return "local";
        }
    }

    // Last-known username, cached locally so the menu can show the right name the
    // instant the game boots instead of flashing the placeholder while the Cloud
    // Save round-trip is in flight. The server value always wins once it arrives
    // (including winning with "no name" - see LoadOwnUsernameAsync).
    private const string CachedUsernamePrefKey = "account_cached_username";
    public static string CachedUsername
    {
        get { var v = PlayerPrefs.GetString(CachedUsernamePrefKey, ""); return string.IsNullOrEmpty(v) ? null : v; }
    }
    private static void CacheUsername(string name)
    {
        if (string.IsNullOrEmpty(name)) PlayerPrefs.DeleteKey(CachedUsernamePrefKey);
        else PlayerPrefs.SetString(CachedUsernamePrefKey, name);
        PlayerPrefs.Save();
    }

    // ── Profile icon (client avatar) ─────────────────────────────────────────
    // The card id whose face-crop is shown as the player's avatar (top bar +
    // My Profile). Same persistence shape as the username: a Cloud Save key so
    // it follows the account across devices, plus a PlayerPrefs cache scoped by
    // CurrentIdentityKey so the top bar can paint instantly at boot before the
    // Cloud Save round-trip lands. Guests are PlayerPrefs-only (no account).
    private const string ProfileIconCloudKey = "profileIcon";
    private static string ProfileIconPrefKey => "account_profile_icon_" + CurrentIdentityKey;

    /// <summary>Committed icon card id, or null for the default avatar.
    /// Server value once EnsureProfileIconLoadedAsync has run; before that,
    /// the local cache (which the server value then overwrites).</summary>
    public static string ProfileIconId { get; private set; }

    private static bool _profileIconLoadTried;

    public static string CachedProfileIconId
    {
        get { var v = PlayerPrefs.GetString(ProfileIconPrefKey, ""); return string.IsNullOrEmpty(v) ? null : v; }
    }

    private static void CacheProfileIcon(string cardId)
    {
        if (string.IsNullOrEmpty(cardId)) PlayerPrefs.DeleteKey(ProfileIconPrefKey);
        else PlayerPrefs.SetString(ProfileIconPrefKey, cardId);
        PlayerPrefs.Save();
    }

    /// <summary>One-shot cloud refresh (no-op for guests / repeat calls).
    /// Fire-and-forget from the menu; callers repaint on completion.</summary>
    public static async Task EnsureProfileIconLoadedAsync()
    {
        if (ProfileIconId == null) ProfileIconId = CachedProfileIconId;
        if (_profileIconLoadTried || IsGuest) return;
        _profileIconLoadTried = true;
        try
        {
            await EnsureReadyAsync();
            var results = await Unity.Services.CloudSave.CloudSaveService.Instance.Data.Player
                .LoadAsync(new HashSet<string> { ProfileIconCloudKey });
            if (results.TryGetValue(ProfileIconCloudKey, out var item))
            {
                var v = item.Value.GetAs<string>();
                ProfileIconId = string.IsNullOrEmpty(v) ? null : v;
                CacheProfileIcon(ProfileIconId);
            }
        }
        catch (Exception ex)
        {
            // Offline: the cached value stands; next launch retries.
            _profileIconLoadTried = false;
            Debug.LogWarning($"Profile icon load failed: {ex.Message}");
        }
    }

    /// <summary>Persist a new profile icon. Local cache + property update are
    /// immediate (UI can repaint synchronously); the Cloud Save write is
    /// best-effort, same never-throws policy as the stats stores.</summary>
    public static async Task SetProfileIconAsync(string cardId)
    {
        ProfileIconId = string.IsNullOrEmpty(cardId) ? null : cardId;
        CacheProfileIcon(ProfileIconId);
        if (IsGuest) return;
        try
        {
            await EnsureReadyAsync();
            await Unity.Services.CloudSave.CloudSaveService.Instance.Data.Player.SaveAsync(
                new Dictionary<string, object> { { ProfileIconCloudKey, ProfileIconId ?? "" } });
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Profile icon save failed (kept locally): {ex.Message}");
        }
    }

    /// <summary>Unity's hosted account portal (change password, change email, delete
    /// account). Opening it is the entire "manage my account" feature now - the URL comes
    /// from the SDK rather than being hardcoded, so it follows Unity if they move it.</summary>
    public static void OpenAccountPortal()
    {
        string url = null;
        try { url = PlayerAccountService.Instance.AccountPortalUrl; }
        catch (Exception ex) { Debug.LogWarning($"Account portal URL unavailable: {ex.Message}"); }
        Application.OpenURL(string.IsNullOrEmpty(url) ? "https://player-login.unity.com" : url);
    }

    /// <summary>Unity's hosted "forgot password" page. Reachable WITHOUT being signed in,
    /// which is the whole point - it's the one screen a locked-out player needs, and it is
    /// the same page the sign-in flow links to.</summary>
    public static void OpenPasswordReset()
    {
        Application.OpenURL("https://player-login.unity.com/request-reset-password");
    }

    // ── Guest mode ──────────────────────────────────────────────────────────
    // Display-only identity for trying the game without an account. Nothing is
    // claimed or persisted server-side, and online-identity features (friends,
    // ranked) should check IsGuest and disable themselves. The choice itself is
    // remembered locally (PlayerPrefs) so the welcome gate only shows once, not
    // on every launch - it comes back via Settings > Create Account, or if the
    // player signs out / clears prefs.
    private const string GuestNamePrefKey = "account_guest_name";
    private const string GuestIdPrefKey = "account_guest_id";
    public static string GuestDisplayName { get; private set; }
    // Random per-session profile id. Deliberately NOT derived from the display
    // name: every "Continue as guest" click is a brand-new profile (fresh decks,
    // fresh history), and two guests who roll the same character name must not
    // share local data.
    public static string GuestId { get; private set; }
    public static bool IsGuest => string.IsNullOrEmpty(CurrentUsername) && !string.IsNullOrEmpty(GuestDisplayName);

    /// <summary>The name to show for this player, wherever a name is shown or sent.
    ///
    /// A guest HAS a name — it is what the lobby, the profile card and the in-match log all display.
    /// The ranked paths resolved only CurrentUsername ?? CachedUsername, both of which are null for a
    /// guest, so a guest reported to the ladder anonymously and appeared on the Most Wanted board as
    /// "Unknown Pirate" even while everyone in the match saw their name. Resolve it in ONE place so
    /// the ladder cannot disagree with the rest of the app about who someone is.</summary>
    public static string DisplayName => CurrentUsername ?? CachedUsername ?? GuestDisplayName;

    /// <summary>Whether this player has a CLAIMED username — the precondition for every
    /// online feature that reports or displays an identity (ranked, invites, chat).
    ///
    /// `!IsGuest` is NOT sufficient on its own, which is what these call sites used to check.
    /// A guest has a display name but no claimed one; a player who has just signed in with
    /// Unity has NEITHER until the claim step completes. That second state didn't exist
    /// before Player Accounts (the old flow claimed the name first, then attached the
    /// login), and in it DisplayName is null — so a ranked report would carry
    /// `username: null` and land the player on the ladder as an unnamed entry, the same
    /// way guests once did. The gate normally blocks that window, but the ladder should
    /// not depend on a UI modal being up to stay correct.</summary>
    public static bool HasClaimedIdentity =>
        !IsGuest && !string.IsNullOrEmpty(CurrentUsername ?? CachedUsername);

    public static void StartGuestSession(string displayName)
    {
        // Guests are throwaway profiles - clear out the previous one's local
        // decks/replays instead of letting orphaned guest folders accumulate.
        DeleteGuestLocalData(PlayerPrefs.GetString(GuestIdPrefKey, ""));

        GuestDisplayName = displayName;
        GuestId = Guid.NewGuid().ToString("N").Substring(0, 12);
        PlayerPrefs.SetString(GuestNamePrefKey, displayName);
        PlayerPrefs.SetString(GuestIdPrefKey, GuestId);
        PlayerPrefs.Save();
    }

    public static void EndGuestSession()
    {
        DeleteGuestLocalData(GuestId);
        GuestDisplayName = null;
        GuestId = null;
        PlayerPrefs.DeleteKey(GuestNamePrefKey);
        PlayerPrefs.DeleteKey(GuestIdPrefKey);
        PlayerPrefs.Save();
    }

    private static void DeleteGuestLocalData(string guestId)
    {
        if (string.IsNullOrEmpty(guestId)) return;
        foreach (var root in new[] { "Decks", "Replays" })
        {
            try
            {
                string dir = System.IO.Path.Combine(Application.persistentDataPath, root, "guest_" + guestId);
                if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, true);
            }
            catch (Exception ex) { Debug.LogWarning($"Guest data cleanup failed: {ex.Message}"); }
        }
    }

    // Restores the ONGOING guest session across app launches (same profile, so
    // the welcome gate doesn't re-prompt every boot). A fresh profile is only
    // minted when the player explicitly clicks "Continue as guest" again.
    public static bool TryRestoreGuestSession()
    {
        var saved = PlayerPrefs.GetString(GuestNamePrefKey, "");
        if (string.IsNullOrEmpty(saved)) return false;
        GuestDisplayName = saved;
        GuestId = PlayerPrefs.GetString(GuestIdPrefKey, "");
        if (string.IsNullOrEmpty(GuestId))
        {
            // Pre-id guest sessions (older builds): mint one now.
            GuestId = Guid.NewGuid().ToString("N").Substring(0, 12);
            PlayerPrefs.SetString(GuestIdPrefKey, GuestId);
            PlayerPrefs.Save();
        }
        return true;
    }

    // "Stay signed in" preference. Unity Auth caches the session token by default, so
    // staying signed in is the natural behavior; when this is false, MainMenuManager
    // signs out on quit so the next launch lands on the sign-in screen instead.
    private const string StaySignedInPrefKey = "account_stay_signed_in";
    public static bool StaySignedIn
    {
        get => PlayerPrefs.GetInt(StaySignedInPrefKey, 1) == 1;
        set { PlayerPrefs.SetInt(StaySignedInPrefKey, value ? 1 : 0); PlayerPrefs.Save(); }
    }

    // Signs out and clears the cached session token so the next launch does not silently
    // resume this account. Caller is responsible for warning the user first when there is
    // no Unity account (an anonymous session signed out this way is unrecoverable).
    public static void SignOut()
    {
        // Player Accounts first: signing out of Unity Auth alone leaves the Player
        // Accounts session live, so the next "Sign In" would throw "Player is already
        // signed in" instead of opening the browser.
        try { PlayerAccountService.Instance.SignOut(); }
        catch (Exception ex) { Debug.LogWarning($"Player Accounts sign-out failed: {ex.Message}"); }

        try
        {
            AuthenticationService.Instance.SignOut(true);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"SignOut failed: {ex.Message}");
        }
        CurrentUsername = null;
        CacheUsername(null);
        // Reset in-memory profile-icon state so the next sign-in loads THAT
        // account's icon instead of showing this one (the PlayerPrefs cache is
        // per-identity already; this clears the session-static copy).
        ProfileIconId = null;
        _profileIconLoadTried = false;
    }

    [Serializable]
    private class ClaimUsernameResponse
    {
        public bool ok;
        public string reason;
        public string username;
    }

    [Serializable]
    private class LookupUsernameResponse
    {
        public bool ok;
        public string reason;
        public string ownerId;
        public string displayName;
    }

    [Serializable]
    private class SimpleOkResponse
    {
        public bool ok;
        public string reason;
    }

    private static AccountFailureReason ParseReason(string reason)
    {
        return reason switch
        {
            "EMPTY" => AccountFailureReason.Empty,
            "TOO_LONG" => AccountFailureReason.TooLong,
            "BAD_CHARS" => AccountFailureReason.BadChars,
            "PROFANITY" => AccountFailureReason.Profanity,
            "NAME_TAKEN" => AccountFailureReason.NameTaken,
            "ALREADY_HAS_USERNAME" => AccountFailureReason.AlreadyHasUsername,
            "EMAIL_TAKEN" => AccountFailureReason.EmailAlreadyLinked,
            _ => AccountFailureReason.Unknown,
        };
    }

    // Link/claim/lookup calls all assume an existing (anonymous-or-linked) session.
    public static Task EnsureReadyAsync() => LobbyManager.EnsureSignedInAsync();

    public static async Task<UsernameClaimResult> ClaimUsernameAsync(string desiredUsername)
    {
        await EnsureReadyAsync();
        try
        {
            var response = await CloudCodeService.Instance.CallEndpointAsync<ClaimUsernameResponse>(
                "ClaimUsername", new Dictionary<string, object> { ["username"] = desiredUsername });

            if (!response.ok) return UsernameClaimResult.Fail(ParseReason(response.reason));

            CurrentUsername = response.username;
            CacheUsername(response.username);

            // Welcome-aboard mail. This used to hang off the email-link step, which no
            // longer exists, so claiming the name is now the account-created moment. The
            // address comes from the Player Accounts ID token rather than a field we own.
            //
            // OPTIONAL: the only surviving use of Resend. If RESEND_API_KEY /
            // RESET_SENDER_EMAIL aren't configured the script fails server-side and this
            // swallows it - strictly fire-and-forget, a mail hiccup must never surface as
            // an account error. Delete SendWelcomeEmail.js and this block to drop Resend
            // entirely; Unity already sends its own verification mail at signup.
            string email = AccountEmail;
            if (!string.IsNullOrEmpty(email))
            {
                try
                {
                    _ = CloudCodeService.Instance.CallEndpointAsync<SimpleOkResponse>(
                        "SendWelcomeEmail", new Dictionary<string, object>
                        { ["email"] = email, ["username"] = response.username ?? "Captain" });
                }
                catch (Exception ex) { Debug.LogWarning($"SendWelcomeEmail failed: {ex.Message}"); }
            }

            return UsernameClaimResult.Success(response.username);
        }
        catch (RequestFailedException ex)
        {
            Debug.LogWarning($"ClaimUsername failed: {ex.Message}");
            return UsernameClaimResult.Fail(AccountFailureReason.NoNetwork);
        }
    }

    public static async Task<string> LoadOwnUsernameAsync()
    {
        await EnsureReadyAsync();
        // Runs on the boot path, which is exactly when PlayerInfo is bare and the
        // Player Accounts session hasn't been restored (it can't be - see HasUnityAccount).
        await RefreshUnityAccountAsync();
        // No email keys to load any more: the address lives in the Player Accounts ID
        // token (see AccountEmail), so there is nothing of ours to keep in sync with it.
        var results = await CloudSaveService.Instance.Data.Player.LoadAsync(
            new HashSet<string> { UsernameCloudSaveKey });
        if (results.TryGetValue(UsernameCloudSaveKey, out var item))
        {
            CurrentUsername = item.Value.GetAs<string>();
        }
        else
        {
            // Server truth: this player has no name. Clear any stale local cache so
            // the boot-time preview can't keep showing a name this account lost
            // (e.g. after dev-side data deletion or switching accounts).
            CurrentUsername = null;
        }
        CacheUsername(CurrentUsername);
        return CurrentUsername;
    }

    // Exposed now, not consumed until the friends-list feature: normalized username -> owner id.
    public static async Task<(bool found, string ownerId, string displayName)> LookupPlayerByUsernameAsync(string username)
    {
        await EnsureReadyAsync();
        try
        {
            var response = await CloudCodeService.Instance.CallEndpointAsync<LookupUsernameResponse>(
                "LookupPlayerByUsername", new Dictionary<string, object> { ["username"] = username });
            return response.ok ? (true, response.ownerId, response.displayName) : (false, null, null);
        }
        catch (RequestFailedException ex)
        {
            Debug.LogWarning($"LookupPlayerByUsername failed: {ex.Message}");
            return (false, null, null);
        }
    }

    // ── Unity Player Accounts ────────────────────────────────────────────────
    // The whole credential story, and it is deliberately small: hand off to Unity's
    // hosted browser flow, take the access token it returns, and exchange it for a
    // UGS session. Signup, email verification, password change and PASSWORD RESET all
    // live on Unity's pages - none of them are our code, our email vendor, or our bug
    // surface. That is the entire reason for the migration.

    // StartSignInAsync returns as soon as the SYSTEM BROWSER HAS BEEN LAUNCHED - it does
    // NOT wait for the player to finish signing in (verified in the SDK source:
    // PlayerAccountServiceInternal.StartSignInAsync awaits only LaunchUrlAsync). Completion
    // arrives later on the SignedIn / SignInFailed events, so awaiting that task alone
    // would hand back a session that isn't there yet. Everything below exists to bridge
    // those events back into a single awaitable result.
    private static TaskCompletionSource<bool> _browserSignIn;

    /// <summary>Abandon an in-flight browser sign-in. The player can always walk away from
    /// the browser tab, so the UI needs a way out that doesn't leave the menu stuck on
    /// "waiting" forever. Safe to call when nothing is pending.</summary>
    public static void CancelUnityAccountSignIn()
    {
        _browserSignIn?.TrySetResult(false);
        try { PlayerAccountService.Instance.SignOut(); } catch { /* nothing to cancel */ }
    }

    /// <summary>Sign in (or sign up) through Unity Player Accounts, then exchange the
    /// result for a UGS session. <paramref name="signingUp"/> only picks which page the
    /// browser lands on first; either page can reach the other, so it is a hint, not a
    /// mode.</summary>
    public static async Task<AccountResult> SignInWithUnityAccountAsync(bool signingUp = false)
    {
        // Services must be up, but do NOT force the anonymous sign-in here: the whole
        // point is to replace whatever session exists with the real account.
        await LobbyManager.EnsureServicesInitializedAsync();

        // A live Player Accounts session makes StartSignInAsync throw InvalidState rather
        // than opening the browser, which would strand the player on a dead button.
        try { if (PlayerAccountService.Instance.IsSignedIn) PlayerAccountService.Instance.SignOut(); }
        catch (Exception ex) { Debug.LogWarning($"Pre-sign-in cleanup failed: {ex.Message}"); }

        var pending = new TaskCompletionSource<bool>();
        _browserSignIn = pending;
        Exception failure = null;

        void OnSignedIn() => pending.TrySetResult(true);
        void OnFailed(RequestFailedException ex) { failure = ex; pending.TrySetResult(false); }

        PlayerAccountService.Instance.SignedIn += OnSignedIn;
        PlayerAccountService.Instance.SignInFailed += OnFailed;
        try
        {
            await PlayerAccountService.Instance.StartSignInAsync(signingUp);
            bool ok = await pending.Task;
            if (!ok)
            {
                if (failure != null)
                {
                    Debug.LogWarning($"Player Accounts sign-in failed: {failure.Message}");
                    return AccountResult.Fail(AccountFailureReason.NoNetwork,
                        "Sign-in didn't complete. Check your connection and try again.");
                }
                return AccountResult.Fail(AccountFailureReason.Cancelled, null);
            }
        }
        catch (PlayerAccountsException ex) when (ex.ErrorCode == PlayerAccountsErrorCodes.MissingClientId)
        {
            // Project Settings > Services > Player Accounts has no Client ID. This is a
            // BUILD misconfiguration, not anything the player did - say so instead of
            // showing them a generic failure they'll retry forever.
            Debug.LogError("Player Accounts Client ID is not configured - see Project Settings > Services > Player Accounts.");
            return AccountResult.Fail(AccountFailureReason.NotConfigured,
                "Accounts aren't set up in this build. Please report this - you can keep playing as a guest.");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Player Accounts sign-in threw: {ex.Message}");
            return AccountResult.Fail(AccountFailureReason.NoNetwork,
                "Couldn't open the sign-in page. Check your connection and try again.");
        }
        finally
        {
            PlayerAccountService.Instance.SignedIn -= OnSignedIn;
            PlayerAccountService.Instance.SignInFailed -= OnFailed;
            if (ReferenceEquals(_browserSignIn, pending)) _browserSignIn = null;
        }

        // Unity Auth refuses SignInWithUnityAsync unless it is in the SignedOut state, and
        // boot has almost certainly left an ANONYMOUS session running. Drop it first.
        // Note this deliberately does NOT link: linking would keep the throwaway anonymous
        // PlayerId, and a returning player on a new device must land on THEIR account.
        try
        {
            if (AuthenticationService.Instance.IsSignedIn) AuthenticationService.Instance.SignOut();
            await AuthenticationService.Instance.SignInWithUnityAsync(PlayerAccountService.Instance.AccessToken);
        }
        catch (AuthenticationException ex)
        {
            Debug.LogWarning($"SignInWithUnity failed: {ex.Message}");
            return AccountResult.Fail(AccountFailureReason.Unknown,
                "Signed in with Unity, but couldn't start your game session. Please try again.");
        }
        catch (RequestFailedException ex)
        {
            return AccountResult.Fail(AccountFailureReason.NoNetwork, $"Couldn't reach the server: {ex.Message}");
        }

        // Record the link NOW, against the new PlayerId, while we know for certain it's
        // real. This is what survives the restart that PlayerAccountService cannot.
        RememberUnityAccount();

        // A guest who signs in stops being a guest - otherwise IsGuest stays true (it keys
        // off GuestDisplayName) and the account would keep being treated as throwaway,
        // including having its local data deleted on the next guest session.
        if (!string.IsNullOrEmpty(GuestDisplayName)) EndGuestSession();

        await LoadOwnUsernameAsync();
        await EnsureProfileIconLoadedAsync();
        return AccountResult.Success();
    }
}
