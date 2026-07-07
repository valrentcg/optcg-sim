// One Piece TCG - Unified card asset access for desktop (file IO) and WebGL (HTTP/CDN).
//
// WHY THIS EXISTS
// ---------------
// GameManager / DeckBuilderManager / MainMenuManager currently load card art and
// data with Application.dataPath + File.Exists/File.ReadAllBytes. That works in
// the Editor and desktop builds, but WebGL has NO filesystem — StreamingAssets is
// served over HTTP there, and for the hosted version the multi-GB Cards folder
// moves out of the build entirely, onto Cloudflare R2.
//
// This facade gives every manager one API that works on both platforms:
//
//   bool     CardAssets.Ready                    — true once InitAsync completed
//   Task     CardAssets.InitAsync()              — call once at boot (after UpdateChecker.CheckAsync)
//   bool     CardAssets.Exists(relPath)          — sync, replaces File.Exists
//   Task<byte[]> CardAssets.ReadBytesAsync(rel)  — replaces File.ReadAllBytes
//   Task<string> CardAssets.ReadTextAsync(rel)   — replaces File.ReadAllText
//
// relPath is always relative to the Cards root, forward slashes:
//   "official-card-library.json", "OfficialById/OP01/OP01-001.png",
//   "Thumbs/OP01/OP01-001.jpg", "ColorIcons/Red_Green.png", "optcg_card_back.jpg"
//
// HOW Exists() WORKS ON WEBGL
// ---------------------------
// You can't stat a URL cheaply, so the R2 upload script (Deploy/upload_assets.ps1)
// generates index.json — a flat list of every relative path in the bucket version —
// and InitAsync downloads it once (~a few hundred KB, cached). Exists() is then a
// HashSet lookup, preserving the managers' existing candidate-path probing logic
// (OfficialById/{set}/{id}.png -> Official/... -> {set}/... -> {id}.png) unchanged.
//
// MIGRATION PATTERN (see Deploy/MIGRATION.md for the per-call-site map)
// ---------------------------------------------------------------------
//   Path.Combine(Application.dataPath, "StreamingAssets", "Cards", a, b) -> $"{a}/{b}"
//   File.Exists(p)        -> CardAssets.Exists(rel)
//   File.ReadAllBytes(p)  -> await CardAssets.ReadBytesAsync(rel)
//   File.ReadAllText(p)   -> await CardAssets.ReadTextAsync(rel)
//
// DeckBuilderManager's IO stage already produces byte[] handed to a decode queue,
// so only its IO step changes. GameManager's sync LoadFile/LoadFront callers should
// migrate to a request-now, apply-when-loaded pattern (placeholder texture until
// the Task completes) — same UX the DeckBuilder queue already implements.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

public static class CardAssets
{
    // ---- Configure ---------------------------------------------------------
    // R2 public base URL, no trailing slash, no version suffix.
    // Currently the r2.dev development URL (rate-limited but fine for testing);
    // swap for a custom domain (e.g. cards.yourdomain.com) before real release.
    public const string CdnBaseUrl = "https://pub-1c84d77f14ce4cd7a7152662d1e2db58.r2.dev";

    // WebGL always uses the CDN. Flip to true on desktop too if you later want
    // desktop builds to stream art instead of bundling StreamingAssets/Cards.
    // static readonly (not const) on purpose: with a const the compiler flags
    // every not-taken branch at call sites with CS0162 "unreachable code".
#if UNITY_WEBGL && !UNITY_EDITOR
    public static readonly bool UseCdn = true;
#else
    public static readonly bool UseCdn = false;
#endif
    // -------------------------------------------------------------------------

    public static bool Ready { get; private set; }

    static HashSet<string> _index;          // CDN mode: every relPath in this assets version
    static readonly Dictionary<string, byte[]> _byteCache = new();

    static string LocalRoot => Path.Combine(Application.dataPath, "StreamingAssets", "Cards");
    static string CdnRoot   => $"{CdnBaseUrl}/v{UpdateChecker.AssetsVersion}";

    /// Call once at boot, after UpdateChecker.CheckAsync() (which supplies
    /// AssetsVersion). No-op on desktop. Safe to call repeatedly.
    public static async Task InitAsync()
    {
        if (Ready) return;
        if (!UseCdn) { Ready = true; return; }

        try
        {
            using var req = UnityWebRequest.Get($"{CdnRoot}/index.json");
            req.timeout = 20;
            var op = req.SendWebRequest();
            while (!op.isDone) await Task.Yield();

            if (req.result == UnityWebRequest.Result.Success)
            {
                var file = JsonUtility.FromJson<IndexFile>(req.downloadHandler.text);
                _index = new HashSet<string>(file?.files ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                Debug.LogError($"[CardAssets] index.json fetch failed: {req.error} — card art will be unavailable.");
                _index = new HashSet<string>();
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[CardAssets] Init error: {e.Message}");
            _index = new HashSet<string>();
        }
        Ready = true;
    }

    [Serializable] class IndexFile { public string[] files; }

    /// A fetchable location for a Cards-relative path: the CDN URL on CDN
    /// builds, else the absolute disk path (callers turn it into a file:// URI
    /// via new Uri(...) — https URLs pass through that unchanged).
    public static string FetchUri(string relPath)
        => UseCdn ? $"{CdnRoot}/{relPath}" : LocalPath(relPath);

    /// Absolute on-disk path for a Cards-relative path (desktop/editor sync IO).
    public static string LocalPath(string relPath)
        => Path.Combine(LocalRoot, relPath.Replace('/', Path.DirectorySeparatorChar));

    /// Sync existence check. relPath uses forward slashes, relative to Cards root.
    public static bool Exists(string relPath)
    {
        if (string.IsNullOrEmpty(relPath)) return false;
        if (!UseCdn) return File.Exists(Path.Combine(LocalRoot, relPath.Replace('/', Path.DirectorySeparatorChar)));
        return _index != null && _index.Contains(relPath);
    }

    /// First path in `candidates` that exists, else null. Mirrors the managers'
    /// probing loops (ArtPath / LoadFront candidate lists).
    public static string FirstExisting(params string[] candidates)
    {
        foreach (var c in candidates) if (Exists(c)) return c;
        return null;
    }

    public static async Task<byte[]> ReadBytesAsync(string relPath)
    {
        if (string.IsNullOrEmpty(relPath)) return null;
        if (_byteCache.TryGetValue(relPath, out var hit)) return hit;

        if (!UseCdn)
        {
            var p = Path.Combine(LocalRoot, relPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(p)) return null;
            // Off-thread read keeps big masters from hitching the main thread.
            var data = await Task.Run(() => File.ReadAllBytes(p));
            return data;
        }

        using var req = UnityWebRequest.Get($"{CdnRoot}/{relPath}");
        req.timeout = 30;
        var op = req.SendWebRequest();
        while (!op.isDone) await Task.Yield();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning($"[CardAssets] Fetch failed {relPath}: {req.error}");
            return null;
        }
        var bytes = req.downloadHandler.data;
        // Only memo small/shared files (icons, backs, json) — thousands of card
        // masters would exhaust WebGL heap; texture caches at the call sites
        // already hold decoded results.
        if (bytes != null && bytes.Length < 2_000_000) _byteCache[relPath] = bytes;
        return bytes;
    }

    public static async Task<string> ReadTextAsync(string relPath)
    {
        var bytes = await ReadBytesAsync(relPath);
        return bytes == null ? null : System.Text.Encoding.UTF8.GetString(bytes);
    }

    /// Convenience: fetch + decode into a Texture2D (with mip chain, non-readable),
    /// matching the decode settings the managers use today.
    public static async Task<Texture2D> LoadTextureAsync(string relPath, bool mipChain = true)
    {
        var bytes = await ReadBytesAsync(relPath);
        if (bytes == null) return null;
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain);
        if (!tex.LoadImage(bytes, true)) { UnityEngine.Object.Destroy(tex); return null; }
        return tex;
    }

    // ---- Path helpers mirroring the existing candidate conventions ----------

    // Most OfficialById art is opaque and was re-encoded PNG->JPEG (q85, ~3x
    // smaller); die-cut/alt-art cards with real transparency stayed PNG. Since
    // it's a genuine per-card mix, every path tries .jpg before falling back
    // to .png.
    public static string[] ArtCandidates(string cardId)
    {
        string safe = (cardId ?? "").Trim();
        if (safe.Length == 0) return Array.Empty<string>();
        string set = safe.Contains("-") ? safe.Split('-')[0] : "";
        return new[]
        {
            $"OfficialById/{set}/{safe}.jpg",
            $"OfficialById/{set}/{safe}.png",
            $"Official/{set}/{safe}.jpg",
            $"Official/{set}/{safe}.png",
            $"{set}/{safe}.jpg",
            $"{set}/{safe}.png",
            $"{safe}.jpg",
            $"{safe}.png",
        };
    }

    public static string ThumbCandidate(string cardId)
    {
        string safe = (cardId ?? "").Trim();
        if (safe.Length == 0) return null;
        string set = safe.Contains("-") ? safe.Split('-')[0] : "";
        return $"Thumbs/{set}/{safe}.jpg";
    }
}
