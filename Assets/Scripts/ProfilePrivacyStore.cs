// One Piece TCG - Profile privacy settings (who can see your decks / replays).
//
// Backs the My Profile PRIVACY panel from the player-scouting design handoff:
// each captain chooses, per section, whether other players can see it —
//   decks   : Deck History tab, Built Decks, Seasonal deck usage
//   replays : Recent Matches, Seasonal head-to-head
// Values are "public" | "friends" | "private" (default public).
//
// Persistence follows the StatsStore idiom: one small Cloud Save player-data
// key ("profilePrivacy"), JsonUtility blob, in-memory cache, Changed event,
// never throws out of the public API. Guests keep a PlayerPrefs copy instead —
// a guest has no cloud account, but their local choice should still stick so
// the panel doesn't reset every launch.
//
// TRUST MODEL: like StatsStore, this is client-written Cloud Save for now. It
// is the OWNER'S OWN setting, so client writes are fine forever; what must
// eventually move server-side is the *enforcement* — when a Find Players
// backend exists, its profile endpoint must read this key server-side and
// withhold deck/replay payloads from unauthorized viewers. The client-side
// masking in MainMenuManager.Profile.cs is presentation only.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.CloudSave;
using UnityEngine;

public enum ProfileVisibility
{
    Public,
    Friends,
    Private,
}

[Serializable]
public sealed class ProfilePrivacySettings
{
    // Serialized as the handoff's wire strings so a future Cloud Code reader
    // ("public"|"friends"|"private") matches the design doc exactly.
    public string decks = "public";
    public string replays = "public";

    public ProfileVisibility Decks => Parse(decks);
    public ProfileVisibility Replays => Parse(replays);

    public static string Wire(ProfileVisibility v) =>
        v == ProfileVisibility.Friends ? "friends" :
        v == ProfileVisibility.Private ? "private" : "public";

    public static ProfileVisibility Parse(string s) =>
        s == "friends" ? ProfileVisibility.Friends :
        s == "private" ? ProfileVisibility.Private : ProfileVisibility.Public;
}

public static class ProfilePrivacyStore
{
    private const string CloudKey = "profilePrivacy";
    private const string PrefsKey = "optcg_privacy"; // guest / offline fallback

    private static ProfilePrivacySettings _cache;
    public static event Action Changed;

    /// <summary>Viewer-side visibility rule from the design handoff.
    /// 'private' is never visible to others (the owner sees their own profile
    /// through CanSeeOwn instead).</summary>
    public static bool CanSee(ProfileVisibility visibility, bool viewerIsFriend) =>
        visibility == ProfileVisibility.Public
        || (visibility == ProfileVisibility.Friends && viewerIsFriend);

    /// <summary>Owner-side "Preview as" rule: viewAs is "me" | "friend" | "public".</summary>
    public static bool CanSeeOwn(ProfileVisibility visibility, string viewAs) =>
        viewAs == "me"
        || visibility == ProfileVisibility.Public
        || (visibility == ProfileVisibility.Friends && viewAs == "friend");

    // ── Read ────────────────────────────────────────────────────────────────

    public static async Task<ProfilePrivacySettings> LoadAsync(bool forceRefresh = false)
    {
        if (!forceRefresh && _cache != null) return _cache;

        var settings = LoadLocal(); // guests + offline fallback
        try
        {
            if (!AccountManager.IsGuest)
            {
                await AccountManager.EnsureReadyAsync();
                var results = await CloudSaveService.Instance.Data.Player.LoadAsync(
                    new HashSet<string> { CloudKey });
                if (results.TryGetValue(CloudKey, out var item))
                {
                    var parsed = JsonUtility.FromJson<ProfilePrivacySettings>(item.Value.GetAs<string>());
                    if (parsed != null) settings = parsed;
                }
                _cache = settings; // only cache values confirmed against the cloud
            }
        }
        catch (Exception ex)
        {
            // Offline: local copy renders, not cached, next call retries.
            Debug.LogWarning($"ProfilePrivacyStore load failed: {ex.Message}");
        }
        return settings ?? new ProfilePrivacySettings();
    }

    // ── Write ───────────────────────────────────────────────────────────────

    public static async Task SaveAsync(ProfileVisibility decks, ProfileVisibility replays)
    {
        var settings = new ProfilePrivacySettings
        {
            decks = ProfilePrivacySettings.Wire(decks),
            replays = ProfilePrivacySettings.Wire(replays),
        };
        _cache = settings;
        SaveLocal(settings);
        Changed?.Invoke();

        if (AccountManager.IsGuest) return;
        try
        {
            await AccountManager.EnsureReadyAsync();
            await CloudSaveService.Instance.Data.Player.SaveAsync(
                new Dictionary<string, object> { { CloudKey, JsonUtility.ToJson(settings) } });
        }
        catch (Exception ex)
        {
            // Local copy already saved — the choice sticks on this device and the
            // next successful SaveAsync (or sign-in elsewhere) re-syncs the cloud.
            Debug.LogWarning($"ProfilePrivacyStore save failed (kept locally): {ex.Message}");
        }
    }

    // ── Local (PlayerPrefs) copy ────────────────────────────────────────────

    private static ProfilePrivacySettings LoadLocal()
    {
        try
        {
            var json = PlayerPrefs.GetString(PrefsKey, null);
            if (!string.IsNullOrEmpty(json))
            {
                var parsed = JsonUtility.FromJson<ProfilePrivacySettings>(json);
                if (parsed != null) return parsed;
            }
        }
        catch (Exception ex) { Debug.LogWarning($"ProfilePrivacyStore local load failed: {ex.Message}"); }
        return new ProfilePrivacySettings();
    }

    private static void SaveLocal(ProfilePrivacySettings settings)
    {
        try
        {
            PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(settings));
            PlayerPrefs.Save();
        }
        catch (Exception ex) { Debug.LogWarning($"ProfilePrivacyStore local save failed: {ex.Message}"); }
    }
}
