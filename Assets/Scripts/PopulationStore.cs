// Live population preview for the menu. Values come only from the authenticated
// ranked worker; a failed/stale request is unavailable, never a made-up zero.
// "registered" is the number of identities first seen since the population
// registry was introduced, not a backfilled all-time Unity account count.

using System;
using System.Text;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using UnityEngine;
using UnityEngine.Networking;

[Serializable]
public sealed class PopulationCounts
{
    public int ranked;
    public int casual;
    public int custom;
}

[Serializable]
public sealed class PopulationSnapshot
{
    public int registered;
    public int online;
    public PopulationCounts queue;
    public PopulationCounts inMatch;
    public int ttlSeconds;

    // JsonUtility ignores this property; it is true only for a recent,
    // successfully decoded response from the worker.
    public bool IsAvailable { get; internal set; }
}

public static class PopulationStore
{
    private const float HeartbeatIntervalSeconds = 40f; // worker TTL is 90 seconds
    private const float SnapshotIntervalSeconds = 30f;
    private const float MaxSnapshotAgeSeconds = 90f;

    private static PopulationSnapshot _snapshot = new PopulationSnapshot();
    private static float _snapshotAt = float.NegativeInfinity;
    private static string _activity = "menu";
    private static PopulationStoreRunner _runner;
    private static bool _heartbeatBusy;
    private static bool _snapshotBusy;

    public static event Action Changed;

    public static PopulationSnapshot Snapshot => _snapshot;

    public static void EnsureRunning()
    {
        if (_runner != null) return;
        var go = new GameObject("Population Preview");
        UnityEngine.Object.DontDestroyOnLoad(go);
        _runner = go.AddComponent<PopulationStoreRunner>();
    }

    // Call at the network match's start and on return to menu/lobby. Solo play
    // should remain "menu" because it is not an online multiplayer match.
    public static void SetActivity(string activity)
    {
        string next = activity == "match_ranked" || activity == "match_casual"
            || activity == "match_custom" ? activity : "menu";
        if (_activity == next) return;
        _activity = next;
        if (_runner != null) _runner.RequestImmediateHeartbeat();
    }

    public static async Task RefreshAsync()
    {
        if (_snapshotBusy) return;
        _snapshotBusy = true;
        try
        {
            var req = await AuthorizedRequestAsync("/population/snapshot", null);
            if (req == null)
            {
                SetUnavailable();
                return;
            }
            using (req)
            {
                if (req.result != UnityWebRequest.Result.Success)
                {
                    SetUnavailable();
                    return;
                }
                var parsed = JsonUtility.FromJson<PopulationSnapshot>(req.downloadHandler.text);
                if (parsed == null || parsed.queue == null || parsed.inMatch == null
                    || parsed.registered < 0 || parsed.online < 0
                    || parsed.queue.ranked < 0 || parsed.queue.casual < 0
                    || parsed.inMatch.ranked < 0 || parsed.inMatch.casual < 0)
                {
                    SetUnavailable();
                    return;
                }
                parsed.IsAvailable = true;
                _snapshot = parsed;
                _snapshotAt = Time.realtimeSinceStartup;
                Changed?.Invoke();
            }
        }
        catch (Exception)
        {
            SetUnavailable();
        }
        finally { _snapshotBusy = false; }
    }

    internal static async Task HeartbeatAsync()
    {
        if (_heartbeatBusy) return;
        _heartbeatBusy = true;
        string sentActivity = _activity;
        try
        {
            // Capture once so a mode transition during the request cannot have
            // an older activity sent after a newer one without a follow-up tick.
            string body = JsonUtility.ToJson(new HeartbeatBody { activity = sentActivity });
            using var req = await AuthorizedRequestAsync("/population/heartbeat", body);
            if (req == null || req.result != UnityWebRequest.Result.Success)
                SetUnavailable();
            else
                await RefreshAsync(); // show our newly registered/activity state promptly
        }
        catch (Exception) { SetUnavailable(); }
        finally
        {
            _heartbeatBusy = false;
            if (_activity != sentActivity && _runner != null)
                _runner.RequestImmediateHeartbeat();
        }
    }

    private static async Task<UnityWebRequest> AuthorizedRequestAsync(string path, string jsonBody)
    {
        if (!RankedStore.IsConfigured || string.IsNullOrEmpty(AppConfig.AppSecret)) return null;
        await AccountManager.EnsureReadyAsync();
        string token = AuthenticationService.Instance.AccessToken;
        if (string.IsNullOrEmpty(token)) return null;

        UnityWebRequest req;
        if (jsonBody == null) req = UnityWebRequest.Get(RankedStore.WorkerBase + path);
        else
        {
            req = new UnityWebRequest(RankedStore.WorkerBase + path, "POST");
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
        }
        req.SetRequestHeader("X-App-Secret", AppConfig.AppSecret);
        req.SetRequestHeader("Authorization", "Bearer " + token);
        req.timeout = 12;
        try
        {
            var op = req.SendWebRequest();
            while (!op.isDone) await Task.Yield();
            return req;
        }
        catch
        {
            req.Dispose();
            throw;
        }
    }

    internal static void SetUnavailable()
    {
        if (!_snapshot.IsAvailable) return;
        _snapshot.IsAvailable = false;
        Changed?.Invoke();
    }

    [Serializable] private sealed class HeartbeatBody { public string activity; }

    internal static float HeartbeatEvery => HeartbeatIntervalSeconds;
    internal static float SnapshotEvery => SnapshotIntervalSeconds;
    internal static bool IsStale => _snapshot.IsAvailable
        && Time.realtimeSinceStartup - _snapshotAt > MaxSnapshotAgeSeconds;
    internal static void RunnerDestroyed(PopulationStoreRunner runner)
    {
        if (_runner == runner) _runner = null;
    }
}

// One persistent runner survives the menu/game scene transition and avoids
// spawning one poll loop per menu rebuild.
public sealed class PopulationStoreRunner : MonoBehaviour
{
    private float nextHeartbeat;
    private float nextSnapshot;

    private void Awake()
    {
        // The first heartbeat performs the initial snapshot after its write.
        nextSnapshot = Time.realtimeSinceStartup + PopulationStore.SnapshotEvery;
    }

    private void Update()
    {
        float now = Time.realtimeSinceStartup;
        if (now >= nextHeartbeat)
        {
            nextHeartbeat = now + PopulationStore.HeartbeatEvery;
            _ = PopulationStore.HeartbeatAsync();
        }
        if (now >= nextSnapshot)
        {
            nextSnapshot = now + PopulationStore.SnapshotEvery;
            _ = PopulationStore.RefreshAsync();
        }
        if (PopulationStore.IsStale) PopulationStore.SetUnavailable();
    }

    internal void RequestImmediateHeartbeat() { nextHeartbeat = 0f; }

    private void OnDestroy() { PopulationStore.RunnerDestroyed(this); }
}
