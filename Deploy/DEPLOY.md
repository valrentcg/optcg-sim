# Deployment Guide — One Piece TCG Simulator

Architecture: **Cloudflare Pages** (WebGL build) + **Cloudflare R2** (card art, data, and the oversized `.data` file) + **GitHub Releases** (desktop client zips) + **Unity Relay/Sessions** (multiplayer, already integrated).

## 0. One-time Cloudflare setup

1. Create a free Cloudflare account. Optional but recommended: add a custom domain you own (needed for R2 free-egress custom domains; `*.pages.dev` and `r2.dev` work without one but `r2.dev` is rate-limited and not meant for production).
2. Create a Pages project (e.g. `optcg-sim`) — Direct Upload mode is fine (no git integration needed).
3. Create an R2 bucket (e.g. `optcg-assets`). Attach a custom domain to it (e.g. `cards.yourdomain.com`) in bucket settings → this puts Cloudflare's CDN cache in front and keeps egress free.
4. Enable CORS on the bucket (WebGL fetches cross-origin):
   ```json
   [{ "AllowedOrigins": ["*"], "AllowedMethods": ["GET"], "AllowedHeaders": ["*"], "MaxAgeSeconds": 86400 }]
   ```
5. Install wrangler locally: `npm i -g wrangler` then `wrangler login`.

## 1. Unity build settings (WebGL)

- Publishing Settings → **Compression Format: Brotli**, **Decompression Fallback: OFF** (Pages serves real Brotli headers via `_headers`), **Name Files As Hashes: ON**.
- Player Settings → ensure **UnityTransport uses WebSockets** for WebGL (Relay over UDP/DTLS doesn't work in browsers). With com.unity.services.multiplayer Sessions + Netcode: set the transport's `UseWebSockets = true` (or `SetRelayServerData(..., isWebSocket: true)` path) in `NetworkBootstrap.EnsureNetworkManager()` under `#if UNITY_WEBGL`.
- Strip `StreamingAssets/Cards` from the WebGL build (move art loading to `RemoteCardArt`; see below). Delete the `_tmp_*` and `face-debug` folders from StreamingAssets regardless — they shouldn't ship.

## 2. Handling Pages' 25 MiB file cap

Unity's main `.data.br` file will exceed 25 MiB. Two options:

- **Option A (preferred): shrink it under 25 MiB.** With card art moved to R2, the `.data` file may already fit. Check the Build folder after building.
- **Option B: host the `.data.br` on R2.** Upload it to the bucket and edit the generated `index.html`'s config: `dataUrl: "https://cards.yourdomain.com/build/v{N}/GAME.data.br"`. Add matching `Content-Encoding: br` metadata on the R2 object (`--content-encoding br` on upload).

## 3. Upload card assets to R2 (versioned paths)

Mirror `StreamingAssets/Cards` under a version prefix (matches `RemoteCardArt`):

```bash
# from the project root — repeat per top-level item
wrangler r2 object put optcg-assets/v1/official-card-library.json --file "Assets/StreamingAssets/Cards/official-card-library.json"
# bulk upload (rclone is much faster for thousands of files):
rclone copy "Assets/StreamingAssets/Cards/OfficialById" r2:optcg-assets/v1/OfficialById --transfers 32
rclone copy "Assets/StreamingAssets/Cards/Thumbs"       r2:optcg-assets/v1/Thumbs --transfers 32
```

Set long cache on the bucket (safe because the `v{N}` prefix changes when content changes): bucket settings → default `Cache-Control: public, max-age=31536000, immutable`, or per-object at upload.

**New card set release:** upload under `v2/` (only changed/new files need uploading if you keep old paths), bump `assetsVersion` to 2 in `version.json`, done — no rebuild, clients get it on next launch.

## 4. Deploy the WebGL build to Pages

```bash
# Build output folder from Unity, with Deploy/_headers and Deploy/version.json copied into its root:
cp Deploy/_headers Deploy/version.json <BUILD_OUTPUT>/
wrangler pages deploy <BUILD_OUTPUT> --project-name optcg-sim
```

Release checklist (code update):
1. Bump `UpdateChecker.CurrentBuildNumber` (and `PlayerSettings` version) in Unity.
2. Build WebGL.
3. Update `version.json`: `buildNumber`, `buildVersion`, `notes` (and `minSupportedBuildNumber` if old clients must not matchmake — e.g. after a netcode protocol change).
4. `wrangler pages deploy ...`
5. Players get it automatically: WebGL clients hard-reload on next launch via `UpdateChecker`; already-loaded sessions finish their match on the old build.

## 5. Desktop client via GitHub Releases

1. Build Windows/macOS players, zip them.
2. `gh release create v0.1.0 OPTCGSim-Windows.zip OPTCGSim-Mac.zip --title "v0.1.0" --notes "..."` (2 GiB/file limit — fine).
3. `version.json`'s `downloads.windows/mac` already point at `releases/latest`, so no manifest edit needed per release.
4. Desktop clients see `OnUpdateAvailable` on launch → show your update prompt → `UpdateChecker.OpenDownloadPage()`. (Later upgrade path: Velopack for in-place auto-update instead of manual download.)

## 6. Multiplayer notes (already mostly done in-project)

- Unity Relay + Sessions free tier: 50 average monthly CCU — plenty to start.
- WebGL requirement: WebSocket transport (step 1). Desktop and WebGL clients CAN cross-play through Relay as long as both use compatible transports/protocol versions — this is what `minSupportedBuildNumber` protects.
- Cloud Code / Cloud Save / Friends free tiers also apply; watch usage in the UGS dashboard as player counts grow.

## 7. Free-tier budget summary

| Service | Free allowance | Your usage |
|---|---|---|
| Cloudflare Pages | unlimited bandwidth, 500 builds/mo, 25 MiB/file | build (~100–300 MB total, files <25 MiB or offloaded) |
| Cloudflare R2 | 10 GB-month storage, 10M reads/mo, $0 egress | card art + data (watch the 10 GB cap with versioned prefixes — prune old `v{N}` dirs) |
| GitHub Releases | 2 GiB/file | desktop zips |
| Unity Relay/Sessions | 50 avg monthly CCU | multiplayer |
