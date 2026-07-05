// One Piece TCG - Player identity: globally-unique usernames (16-char cap, server-side
// profanity filter, enforced by Cloud Code since Cloud Save's Custom Data registry isn't
// client-writable) and email/password account recovery layered onto the anonymous UGS
// identity LobbyManager already establishes. Sibling to LobbyManager rather than folded
// into it - LobbyManager is scoped to session/lobby lifecycle, identity is a distinct
// concern MatchNetworkSync and a future FriendsManager will also depend on independently
// of whether a lobby is active.
//
// Unity's Username/Password identity provider has no built-in "forgot password" flow
// (confirmed absent from the installed Authentication SDK), so RequestPasswordResetAsync/
// ConfirmPasswordResetAsync call our own Cloud Code scripts (RequestPasswordReset.js /
// ConfirmPasswordReset.js) that implement it via a mailed token instead.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Authentication;
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
    public static bool HasEmailLinked { get; private set; }

    // Display copies of the account's email addresses. The credential itself is
    // username+password (emails are just registry lookups server-side), so these
    // exist purely so the settings UI can show what's on file. Persisted in Cloud
    // Save player data because Unity Auth has no API to read back an identity's
    // email address.
    public static string PrimaryEmail { get; private set; }
    public static string RecoveryEmail { get; private set; }

    private const string EmailsCloudSaveKey = "accountEmails";

    // Stable key for scoping LOCAL storage (decks, replays) to whoever is using
    // the game right now, so accounts and guests on the same machine never see
    // each other's data. Signed-in accounts use their UGS player id; guests get
    // a key derived from their guest name; "local" is the brief pre-sign-in
    // window at boot (nothing user-visible loads that early in practice).
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

    [Serializable]
    private class EmailsBlob { public string primary; public string recovery; }

    // Local per-player memory that a credential exists. Unity's GetPlayerInfoAsync
    // has proven unreliable at reporting username/password identities (returns an
    // empty list for accounts that demonstrably sign in with credentials), so the
    // game records the fact itself at the moments it KNOWS: a successful
    // AddUsernamePasswordAsync or a successful credential sign-in.
    private static string LinkedPrefKey => "account_linked_" + SafePlayerId();
    private static string SafePlayerId()
    {
        try { return AuthenticationService.Instance.PlayerId ?? "none"; }
        catch { return "none"; }
    }
    private static void RememberLinked()
    {
        HasEmailLinked = true;
        PlayerPrefs.SetInt(LinkedPrefKey, 1);
        PlayerPrefs.Save();
    }

    private static async Task SaveEmailsAsync()
    {
        var blob = new EmailsBlob { primary = PrimaryEmail ?? "", recovery = RecoveryEmail ?? "" };
        await CloudSaveService.Instance.Data.Player.SaveAsync(new Dictionary<string, object>
        { [EmailsCloudSaveKey] = JsonUtility.ToJson(blob) });
    }

    // Registers an additional email for this account: it lands in the same
    // emailRegistry the password-reset and email-sign-in flows read, so reset
    // codes can be sent to either address and either signs you in.
    public static async Task<AccountResult> SetRecoveryEmailAsync(string email)
    {
        await EnsureReadyAsync();
        if (string.IsNullOrEmpty(CurrentUsername))
            return AccountResult.Fail(AccountFailureReason.Unknown, "Claim a name first.");
        try
        {
            var response = await CloudCodeService.Instance.CallEndpointAsync<SimpleOkResponse>(
                "RegisterEmailForRecovery", new Dictionary<string, object>
                { ["email"] = email, ["username"] = CurrentUsername });
            if (!response.ok)
                return AccountResult.Fail(ParseReason(response.reason), "Couldn't register that email.");

            RecoveryEmail = email;
            await SaveEmailsAsync();
            return AccountResult.Success();
        }
        catch (RequestFailedException ex)
        {
            return AccountResult.Fail(AccountFailureReason.NoNetwork, $"Couldn't reach the server: {ex.Message}");
        }
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

    // Reflects the username/password identity on the signed-in player, so HasEmailLinked
    // survives app restarts instead of only being true in the session where linking
    // happened. IMPORTANT: a cached session resumed at boot has a bare PlayerInfo whose
    // Identities list is NOT populated - it must be fetched with GetPlayerInfoAsync(),
    // otherwise a fully-linked account looks unlinked after every restart (and the
    // sign-out flow shows a scary "you'll lose this account" warning it shouldn't).
    public static async Task RefreshEmailLinkedAsync()
    {
        try
        {
            // Local memory first: if this player linked a credential on this machine,
            // that fact is definitive regardless of what the identity endpoint says.
            if (PlayerPrefs.GetInt(LinkedPrefKey, 0) == 1) HasEmailLinked = true;

            var info = await AuthenticationService.Instance.GetPlayerInfoAsync();
            var ids = info?.Identities ?? AuthenticationService.Instance.PlayerInfo?.Identities;
            if (ids != null)
            {
                Debug.Log($"RefreshEmailLinked: player={AuthenticationService.Instance.PlayerId} " +
                    $"{ids.Count} identities [{string.Join(", ", ids.ConvertAll(i => i.TypeId))}] " +
                    $"localLinked={PlayerPrefs.GetInt(LinkedPrefKey, 0)}");
                // Identities can only ever UPGRADE to linked - an empty list is not
                // trusted as proof of absence (observed returning [] for accounts
                // that sign in with credentials just fine).
                if (ids.Exists(i => !string.IsNullOrEmpty(i.TypeId) &&
                        (i.TypeId.ToLowerInvariant().Contains("username") ||
                         i.TypeId.ToLowerInvariant().Contains("password"))))
                    HasEmailLinked = true;
            }
        }
        catch (Exception ex)
        {
            // Offline or not signed in - keep the current value rather than lying.
            Debug.LogWarning($"RefreshEmailLinked failed: {ex.Message}");
        }
    }

    // Signs out and clears the cached session token so the next launch does not silently
    // resume this account. Caller is responsible for warning the user first when no email
    // is linked (an anonymous account signed out this way is unrecoverable).
    public static void SignOut()
    {
        try
        {
            AuthenticationService.Instance.SignOut(true);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"SignOut failed: {ex.Message}");
        }
        CurrentUsername = null;
        HasEmailLinked = false;
        PrimaryEmail = null;
        RecoveryEmail = null;
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
            "INVALID_TOKEN" => AccountFailureReason.InvalidOrExpiredToken,
            "EXPIRED" => AccountFailureReason.InvalidOrExpiredToken,
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
        await RefreshEmailLinkedAsync();
        var results = await CloudSaveService.Instance.Data.Player.LoadAsync(
            new HashSet<string> { UsernameCloudSaveKey, EmailsCloudSaveKey });
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
        if (results.TryGetValue(EmailsCloudSaveKey, out var emailsItem))
        {
            try
            {
                var blob = JsonUtility.FromJson<EmailsBlob>(emailsItem.Value.GetAs<string>());
                PrimaryEmail = string.IsNullOrEmpty(blob.primary) ? null : blob.primary;
                RecoveryEmail = string.IsNullOrEmpty(blob.recovery) ? null : blob.recovery;
                // The emails blob is only ever written after a successful credential
                // link, so its presence is cross-device proof one exists - unlike the
                // unreliable identity endpoint.
                if (PrimaryEmail != null) HasEmailLinked = true;
            }
            catch { /* malformed blob - emails just won't display */ }
        }
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

    public static async Task<AccountResult> LinkEmailPasswordAsync(string email, string password)
    {
        await EnsureReadyAsync();

        // The claimed game name IS the login username (Unity Auth just needs any unique
        // string, and username uniqueness is already enforced by ClaimUsername). Email
        // sign-in resolves email -> username via the emailRegistry first.
        if (string.IsNullOrEmpty(CurrentUsername))
            return AccountResult.Fail(AccountFailureReason.Unknown, "Claim a name before linking an email.");

        try
        {
            await AuthenticationService.Instance.AddUsernamePasswordAsync(CurrentUsername.ToLowerInvariant(), password);
            RememberLinked();
            PrimaryEmail = email;
            try { await SaveEmailsAsync(); }
            catch (Exception ex) { Debug.LogWarning($"Saving account emails failed: {ex.Message}"); }

            try
            {
                await CloudCodeService.Instance.CallEndpointAsync<SimpleOkResponse>(
                    "RegisterEmailForRecovery", new Dictionary<string, object>
                    { ["email"] = email, ["username"] = CurrentUsername });
            }
            catch (RequestFailedException ex)
            {
                // Non-fatal: the account is linked either way, this only affects the
                // reverse lookup password-reset needs later. Log and move on.
                Debug.LogWarning($"RegisterEmailForRecovery failed: {ex.Message}");
            }

            // Welcome-aboard email. Strictly fire-and-forget: registration is done
            // and a mail hiccup must never surface as an account error.
            try
            {
                _ = CloudCodeService.Instance.CallEndpointAsync<SimpleOkResponse>(
                    "SendWelcomeEmail", new Dictionary<string, object>
                    { ["email"] = email, ["username"] = CurrentUsername ?? "Captain" });
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"SendWelcomeEmail failed: {ex.Message}");
            }

            return AccountResult.Success();
        }
        catch (AuthenticationException ex) when (ex.ErrorCode == AuthenticationErrorCodes.AccountAlreadyLinked)
        {
            return AccountResult.Fail(AccountFailureReason.EmailAlreadyLinked,
                "That email's already linked to an account - try signing in instead.");
        }
        catch (RequestFailedException ex)
        {
            return AccountResult.Fail(AccountFailureReason.NoNetwork, $"Couldn't reach the server: {ex.Message}");
        }
    }

    [Serializable]
    private class LoginNameResponse
    {
        public bool ok;
        public string reason;   // present on ok:false responses - the Cloud Code
                                // deserializer is strict and errors on any member
                                // the script returns that the class doesn't declare
        public string username;
    }

    // For recovering an account on a new device/after reinstall - distinct from the
    // anonymous-session path since it requires AuthenticationState.SignedOut first.
    // Accepts a username OR an email: emails are resolved to the login username via
    // Cloud Code (GetLoginNameForEmail) while the anonymous session is still alive,
    // since Cloud Code calls need an authenticated caller.
    public static async Task<AccountResult> SignInWithEmailPasswordAsync(string identifier, string password)
    {
        string loginName = identifier.Trim();
        if (loginName.Contains("@"))
        {
            try
            {
                await EnsureReadyAsync();
                var lookup = await CloudCodeService.Instance.CallEndpointAsync<LoginNameResponse>(
                    "GetLoginNameForEmail", new Dictionary<string, object> { ["email"] = loginName });
                if (lookup.ok && !string.IsNullOrEmpty(lookup.username)) loginName = lookup.username;
                Debug.Log($"Email sign-in: '{identifier.Trim()}' resolved to login name '{loginName}' (lookup ok={lookup.ok} reason={lookup.reason})");
                // Not found: fall through with the email itself - deliberately generic
                // failure below, so this can't be used to probe which emails exist.
            }
            catch (RequestFailedException ex)
            {
                return AccountResult.Fail(AccountFailureReason.NoNetwork, $"Couldn't reach the server: {ex.Message}");
            }
        }

        await LobbyManager.EnsureServicesInitializedAsync();
        if (AuthenticationService.Instance.IsSignedIn)
        {
            AuthenticationService.Instance.SignOut();
        }

        try
        {
            await AuthenticationService.Instance.SignInWithUsernamePasswordAsync(loginName.ToLowerInvariant(), password);
            RememberLinked(); // signing in WITH a credential is proof one exists
            await LoadOwnUsernameAsync();

            // Self-heal the email registry on EVERY successful credential sign-in.
            // The email to register comes from what the player typed (if it was an
            // email) or from the account's stored primary email (loaded from Cloud
            // Save just above) - so even a username sign-in repairs a broken/missing
            // registry entry. Covers accounts whose original RegisterEmailForRecovery
            // call failed; without this, their password reset / email sign-in stays
            // broken forever.
            string healEmail = identifier.Contains("@") ? identifier.Trim() : PrimaryEmail;
            if (!string.IsNullOrEmpty(healEmail) && !string.IsNullOrEmpty(CurrentUsername))
            {
                try
                {
                    var heal = await CloudCodeService.Instance.CallEndpointAsync<SimpleOkResponse>(
                        "RegisterEmailForRecovery", new Dictionary<string, object>
                        { ["email"] = healEmail, ["username"] = CurrentUsername });
                    Debug.Log($"Email registry self-heal for '{healEmail}': ok={heal.ok} reason={heal.reason}");
                    if (string.IsNullOrEmpty(PrimaryEmail)) { PrimaryEmail = healEmail; await SaveEmailsAsync(); }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Email registry self-heal failed: {ex.Message}");
                }
            }
            return AccountResult.Success();
        }
        catch (AuthenticationException ex)
        {
            // Deliberately generic - don't distinguish "wrong password" from "no such
            // account" so this can't be used to enumerate registered emails.
            Debug.LogWarning($"SignInWithEmailPassword failed: {ex.Message}");
            return AccountResult.Fail(AccountFailureReason.InvalidCredentials, "Incorrect email or password.");
        }
        catch (RequestFailedException ex)
        {
            return AccountResult.Fail(AccountFailureReason.NoNetwork, $"Couldn't reach the server: {ex.Message}");
        }
    }

    public static async Task<AccountResult> RequestPasswordResetAsync(string email)
    {
        await EnsureReadyAsync();
        try
        {
            await CloudCodeService.Instance.CallEndpointAsync<SimpleOkResponse>(
                "RequestPasswordReset", new Dictionary<string, object> { ["email"] = email });
            // Always reported as success by design (avoids email enumeration) regardless
            // of whether the address is actually registered.
            return AccountResult.Success();
        }
        catch (RequestFailedException ex)
        {
            return AccountResult.Fail(AccountFailureReason.NoNetwork, $"Couldn't reach the server: {ex.Message}");
        }
    }

    public static async Task<AccountResult> ConfirmPasswordResetAsync(string token, string newPassword)
    {
        await EnsureReadyAsync();
        try
        {
            var response = await CloudCodeService.Instance.CallEndpointAsync<SimpleOkResponse>(
                "ConfirmPasswordReset", new Dictionary<string, object> { ["token"] = token, ["newPassword"] = newPassword });
            return response.ok ? AccountResult.Success() : AccountResult.Fail(ParseReason(response.reason), "That code is invalid or has expired.");
        }
        catch (RequestFailedException ex)
        {
            return AccountResult.Fail(AccountFailureReason.NoNetwork, $"Couldn't reach the server: {ex.Message}");
        }
    }
}
