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

    // ── Guest mode ──────────────────────────────────────────────────────────
    // Display-only identity for trying the game without an account. Nothing is
    // claimed or persisted server-side, and online-identity features (friends,
    // ranked) should check IsGuest and disable themselves. The choice itself is
    // remembered locally (PlayerPrefs) so the welcome gate only shows once, not
    // on every launch - it comes back via Settings > Create Account, or if the
    // player signs out / clears prefs.
    private const string GuestNamePrefKey = "account_guest_name";
    public static string GuestDisplayName { get; private set; }
    public static bool IsGuest => string.IsNullOrEmpty(CurrentUsername) && !string.IsNullOrEmpty(GuestDisplayName);

    public static void StartGuestSession(string displayName)
    {
        GuestDisplayName = displayName;
        PlayerPrefs.SetString(GuestNamePrefKey, displayName);
        PlayerPrefs.Save();
    }

    public static void EndGuestSession()
    {
        GuestDisplayName = null;
        PlayerPrefs.DeleteKey(GuestNamePrefKey);
        PlayerPrefs.Save();
    }

    // Restores a previously chosen guest identity. Returns true if one existed.
    public static bool TryRestoreGuestSession()
    {
        var saved = PlayerPrefs.GetString(GuestNamePrefKey, "");
        if (string.IsNullOrEmpty(saved)) return false;
        GuestDisplayName = saved;
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
    // survives app restarts instead of only being true in the session where linking happened.
    public static void RefreshEmailLinkedFromIdentities()
    {
        try
        {
            var ids = AuthenticationService.Instance.PlayerInfo?.Identities;
            if (ids != null)
                HasEmailLinked = ids.Exists(i =>
                    !string.IsNullOrEmpty(i.TypeId) &&
                    i.TypeId.ToLowerInvariant().Contains("username"));
        }
        catch { /* PlayerInfo unavailable until signed in - keep current value */ }
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
        RefreshEmailLinkedFromIdentities();
        var results = await CloudSaveService.Instance.Data.Player.LoadAsync(new HashSet<string> { UsernameCloudSaveKey });
        if (results.TryGetValue(UsernameCloudSaveKey, out var item))
        {
            CurrentUsername = item.Value.GetAs<string>();
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
            HasEmailLinked = true;

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
            HasEmailLinked = true;
            await LoadOwnUsernameAsync();
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
