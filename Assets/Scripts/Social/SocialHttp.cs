// One Piece TCG - shared authenticated HTTP for the social layer (chat + invites),
// talking to the same Cloudflare worker RankedStore uses. Factored out of the two
// stores so the auth/secret/timeout policy lives in one place. Mirrors
// RankedStore.QueuePostAsync exactly: X-App-Secret gate + Unity access-token bearer,
// JsonUtility bodies, never throws to the caller (returns null on any failure).

using System;
using System.Text;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using UnityEngine;
using UnityEngine.Networking;

public static class SocialHttp
{
    // Reuse RankedStore's deployed worker base + "configured" gate + app secret, so the
    // social endpoints ship and authenticate identically to ranked with zero extra config.
    public static bool Available => RankedStore.IsConfigured && !AccountManager.IsGuest;

    /// <summary>Authenticated request to the worker. `path` may include a query string
    /// for GETs. `jsonBody` is null for GET / empty-body POSTs. Returns the raw response
    /// text on HTTP success, else null (offline, not configured, guest, non-2xx, timeout).</summary>
    public static async Task<string> RequestAsync(string method, string path, string jsonBody)
    {
        if (!Available) return null;
        try
        {
            await AccountManager.EnsureReadyAsync();
            string token = AuthenticationService.Instance.AccessToken;
            if (string.IsNullOrEmpty(token)) return null;

            using var req = new UnityWebRequest($"{RankedStore.WorkerBase}{path}", method);
            if (jsonBody != null)
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("X-App-Secret", AppConfig.AppSecret);
            req.SetRequestHeader("Authorization", "Bearer " + token);
            req.timeout = 15;

            var op = req.SendWebRequest();
            while (!op.isDone) await Task.Yield();

            if (req.result == UnityWebRequest.Result.Success) return req.downloadHandler.text;
            Debug.LogWarning($"Social {method} {path} failed: {req.error}");
        }
        catch (Exception ex) { Debug.LogWarning($"Social {method} {path} exception: {ex.Message}"); }
        return null;
    }

    public static Task<string> GetAsync(string path) => RequestAsync(UnityWebRequest.kHttpVerbGET, path, null);
    public static Task<string> PostAsync(string path, string jsonBody) => RequestAsync(UnityWebRequest.kHttpVerbPOST, path, jsonBody ?? "{}");

    /// <summary>URL-escape a query parameter value.</summary>
    public static string Esc(string v) => UnityWebRequest.EscapeURL(v ?? string.Empty);
}
