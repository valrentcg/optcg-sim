// One Piece TCG - Launch-time update check shared by WebGL and desktop builds.
//
// Flow:
//   1. On startup, fetch {ManifestBaseUrl}/version.json?ts=<now>  (cache-busted).
//   2. Compare manifest.buildNumber to the number this build was compiled with.
//   3. WebGL  : newer build available  -> hard-reload the page (picks up the new
//               build from Cloudflare Pages automatically).
//   4. Desktop: newer build available  -> raise OnUpdateAvailable so the UI can
//               show an "Update available" prompt; OpenDownloadPage() sends the
//               player to GitHub Releases. If buildNumber < minSupportedBuildNumber,
//               treat it as a forced update (block play until updated).
//   5. assetsVersion is exposed so card-art/data loaders can version their
//      remote URLs (e.g. {cdn}/v{assetsVersion}/OfficialById/...) and invalidate
//      caches without touching the build.
//
// Setup:
//   - Put this + WebGLUpdater.jslib (in a Plugins folder) into the project.
//   - Call UpdateChecker.CheckAsync() early (e.g. from your bootstrap/main menu
//     init, before matchmaking).
//   - Bump CurrentBuildNumber (or wire it to a build script / PlayerSettings
//     bundleVersion) every release, and upload the matching version.json.

using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif
#if !UNITY_EDITOR && UNITY_STANDALONE_WIN
using Velopack;
using Velopack.Sources;
#endif

public static class UpdateChecker
{
    // ---- Configure these -------------------------------------------------
    // Where version.json lives. Use your Pages domain (or R2 custom domain).
    public const string ManifestBaseUrl = "https://optcg-sim.pages.dev";

    // Bump this every release. Must match "buildNumber" in the deployed
    // version.json for that release.
    public const int CurrentBuildNumber = 14;
    // -----------------------------------------------------------------------

    [Serializable]
    public class VersionManifest
    {
        public string buildVersion;
        public int buildNumber;
        public int minSupportedBuildNumber;
        public int assetsVersion;
        public string notes;
        public DownloadLinks downloads;
    }

    [Serializable]
    public class DownloadLinks
    {
        public string windows;
        public string mac;
    }

    public static VersionManifest Latest { get; private set; }

    /// True once CheckAsync has completed (successfully or not).
    public static bool Checked { get; private set; }

    /// Remote assetsVersion (falls back to 1 if the check failed). Card
    /// art/data loaders should build URLs from this.
    public static int AssetsVersion => Latest?.assetsVersion ?? 1;

    /// Fired on desktop when a newer build exists. bool = update is mandatory.
    public static event Action<VersionManifest, bool> OnUpdateAvailable;

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void OPTCG_ReloadPage();
    [DllImport("__Internal")] private static extern void OPTCG_OpenURL(string url);
#endif

    public static async Task CheckAsync()
    {
        Debug.Log($"[Version] Running build {CurrentBuildNumber}");
        try
        {
            // ts param defeats any intermediate cache; version.json itself is
            // also served with Cache-Control: no-cache via the _headers file.
            string url = $"{ManifestBaseUrl}/version.json?ts={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            using var req = UnityWebRequest.Get(url);
            req.timeout = 10;
            var op = req.SendWebRequest();
            while (!op.isDone) await Task.Yield();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[UpdateChecker] Manifest fetch failed: {req.error} — continuing with current build.");
                return;
            }

            Latest = JsonUtility.FromJson<VersionManifest>(req.downloadHandler.text);
            if (Latest == null) return;

            if (Latest.buildNumber > CurrentBuildNumber)
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                Debug.Log($"[UpdateChecker] New build {Latest.buildVersion} available — reloading page.");
                OPTCG_ReloadPage();
#else
                bool mandatory = CurrentBuildNumber < Latest.minSupportedBuildNumber;
                Debug.Log($"[UpdateChecker] New build {Latest.buildVersion} available (mandatory={mandatory}).");
                OnUpdateAvailable?.Invoke(Latest, mandatory);
#endif
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[UpdateChecker] Update check error: {e.Message} — continuing with current build.");
        }
        finally
        {
            Checked = true;
        }
    }

    /// Desktop: open the platform's download page (GitHub Releases).
    public static void OpenDownloadPage()
    {
        if (Latest?.downloads == null) return;
#if UNITY_STANDALONE_OSX
        string url = Latest.downloads.mac;
#else
        string url = Latest.downloads.windows;
#endif
        if (string.IsNullOrEmpty(url)) return;
#if UNITY_WEBGL && !UNITY_EDITOR
        OPTCG_OpenURL(url);
#else
        Application.OpenURL(url);
#endif
    }

    // GitHub repo hosting the packaged Velopack releases (Setup.exe + delta patches).
    public const string GithubRepoUrl = "https://github.com/valrentcg/optcg-sim";

    /// Desktop only: checks the GitHub release feed via Velopack, downloads any
    /// newer version (delta patch if available), and restarts into it. Safe to
    /// call on every launch - no-ops if already on the latest version.
    ///
    /// `status` receives human-readable progress ("DOWNLOADING UPDATE... 47%") and
    /// MAY BE CALLED FROM A BACKGROUND THREAD - write it somewhere thread-safe
    /// (e.g. a static string a Unity Update() polls); do not touch UI from it.
    ///
    /// Returns true when an update was found and a restart is imminent — the
    /// caller should keep its "updating" UI up and skip the rest of its boot.
    /// <summary>Structured update progress for the themed launch splash. Mutated in place across phases
    /// and reported repeatedly; the UI thread reads the latest snapshot.</summary>
    public sealed class UpdateProgress
    {
        public string phase = "CHECKING FOR UPDATES…";   // human-readable phase
        public int percent = -1;                         // 0..100 during download; -1 = indeterminate
        public string fromVersion;                       // version we're on
        public string toVersion;                         // version we're updating to
        public string notes;                             // the new release's patch notes (markdown-ish)
    }

    public static async Task<bool> CheckAndApplyDesktopUpdateAsync(Action<UpdateProgress> report = null)
    {
#if !UNITY_EDITOR && UNITY_STANDALONE_WIN
        var prog = new UpdateProgress();
        try
        {
            report?.Invoke(prog);
            var source = new GithubSource(GithubRepoUrl, null, false);
            var mgr = new UpdateManager(source);

            var newVersion = await mgr.CheckForUpdatesAsync();
            if (newVersion == null) return false;

            prog.fromVersion = mgr.CurrentVersion?.ToString();
            prog.toVersion = newVersion.TargetFullRelease.Version?.ToString();
            // Release notes come from the packed release (vpk pack --releaseNotes). The exact property name
            // has varied across Velopack versions (NotesMarkdown / NotesHTML), so fetch it by reflection to
            // stay build-proof regardless of the installed Velopack version.
            try
            {
                var rel = newVersion.TargetFullRelease;
                var pi = rel.GetType().GetProperty("NotesMarkdown") ?? rel.GetType().GetProperty("NotesHtml")
                         ?? rel.GetType().GetProperty("NotesHTML");
                prog.notes = pi?.GetValue(rel) as string;
            }
            catch { prog.notes = null; }

            Debug.Log($"[UpdateChecker] Velopack update found: {prog.toVersion} - downloading.");
            prog.phase = "DOWNLOADING UPDATE";
            prog.percent = 0;
            report?.Invoke(prog);
            await mgr.DownloadUpdatesAsync(newVersion, p => { prog.percent = p; report?.Invoke(prog); });

            prog.phase = "RESTARTING TO APPLY UPDATE…";
            prog.percent = 100;
            report?.Invoke(prog);
            mgr.ApplyUpdatesAndRestart(newVersion);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[UpdateChecker] Velopack update check failed: {e.Message} - continuing with current build.");
            return false;
        }
#else
        await Task.CompletedTask;
        return false;
#endif
    }
}
