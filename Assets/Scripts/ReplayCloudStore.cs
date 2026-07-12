// One Piece TCG - Cloud replay search/download (optcg-gamelogs-upload Worker, GET side).
// The upload half lives in GameLogStore.UploadAsync, which already sends the ReplayRecord JSON
// alongside the markdown log on every finished match. This is the read side: look up a
// player's public match list by username, and download any match's ReplayRecord straight into
// this account's local ReplayStore so it opens in the viewer like any other local replay.
//
// Reads are unauthenticated by design (see the Worker's own header comment) — this is public
// match data, and the search/download UI needs to work for anyone looking up any username, not
// just the logged-in player's own data.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

[Serializable]
public sealed class CloudMatchSummary
{
    public string matchId;
    public string category;
    public string southUsername;
    public string northUsername;
    public string southDeckName;
    public string northDeckName;
    public string winnerUsername;
    public int turnCount;
    public string firstPlayer;
    public string uploadedAt;
    public bool hasReplay;
}

[Serializable]
internal sealed class CloudMatchListResponse
{
    public bool ok;
    public string username;
    public CloudMatchSummary[] matches;
}

public static class ReplayCloudStore
{
    private const string BaseUrl = "https://optcg-gamelogs-upload.valrentcg.workers.dev";

    /// <summary>Fetches `username`'s public match list (either side), newest first. Calls
    /// `onDone` with an empty list on any failure — never throws into the caller.</summary>
    public static async void SearchByUsername(string username, Action<List<CloudMatchSummary>> onDone)
    {
        var result = new List<CloudMatchSummary>();
        if (string.IsNullOrEmpty(username)) { onDone?.Invoke(result); return; }

        try
        {
            using var req = UnityWebRequest.Get($"{BaseUrl}/matches?username={UnityWebRequest.EscapeURL(username)}");
            req.timeout = 15;
            var op = req.SendWebRequest();
            while (!op.isDone) await Task.Yield();

            if (req.result == UnityWebRequest.Result.Success)
            {
                var parsed = JsonUtility.FromJson<CloudMatchListResponse>(req.downloadHandler.text);
                if (parsed?.matches != null) result.AddRange(parsed.matches);
            }
            else
            {
                Debug.LogWarning($"[ReplayCloudStore] Match search failed for '{username}': {req.error}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ReplayCloudStore] Match search exception for '{username}': {ex.Message}");
        }
        onDone?.Invoke(result);
    }

    /// <summary>Downloads `matchId`'s ReplayRecord and imports it into this account's local
    /// ReplayStore (same validation/collision handling as importing a file someone sent you —
    /// see ReplayStore.Import). Calls `onDone(null)` on any failure (no replay uploaded for
    /// that match, network error, etc.).</summary>
    public static async void DownloadReplay(string matchId, Action<ReplayRecord> onDone)
    {
        if (string.IsNullOrEmpty(matchId)) { onDone?.Invoke(null); return; }

        try
        {
            using var req = UnityWebRequest.Get($"{BaseUrl}/replay/{UnityWebRequest.EscapeURL(matchId)}");
            req.timeout = 20;
            var op = req.SendWebRequest();
            while (!op.isDone) await Task.Yield();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[ReplayCloudStore] Replay download failed for '{matchId}': {req.error}");
                onDone?.Invoke(null);
                return;
            }

            var imported = ReplayStore.Import(req.downloadHandler.text);
            if (imported == null)
                Debug.LogWarning($"[ReplayCloudStore] Downloaded replay for '{matchId}' failed local import (malformed JSON?).");
            onDone?.Invoke(imported);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ReplayCloudStore] Replay download exception for '{matchId}': {ex.Message}");
            onDone?.Invoke(null);
        }
    }
}
