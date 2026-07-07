# Release Roadmap & Progress Log

Living document — tracks decisions, completed work, and remaining steps toward
releasing the One Piece TCG Simulator (WebGL + downloadable desktop client).
Last updated: **2026-07-07**.

## The architecture (decided, researched July 2026)

| Piece | Choice | Notes |
|---|---|---|
| WebGL build hosting | **Cloudflare Pages** (free) | Unlimited bandwidth; `_headers` file provides Brotli `Content-Encoding`; 25 MiB/file cap → big `.data.br` offloads to R2 if needed |
| Card art + data (~GBs) | **Cloudflare R2** (free: 10 GB, $0 egress) | Never bundled into the WebGL build; loaded at runtime under versioned prefixes `v{N}/...` |
| Desktop (.exe) distribution | **GitHub Releases**, public repo (2 GiB/file) | Packaged with `vpk` (Velopack), real in-place auto-update **live and verified** — see "Done — desktop patcher pipeline" below |
| Multiplayer | **Unity Relay/Sessions** (already integrated; free ≤50 avg CCU) | WebGL requires WebSocket transport — done, see below |
| Auto-updates | Desktop: **Velopack** (`UpdateManager` + `GithubSource`, real check/download/delta-apply/restart). WebGL: `version.json` manifest → hard page reload | Card sets ship by bumping `assetsVersion` — no rebuild, either platform |
| Legal posture | Fan project, non-monetized | Bandai actively protects the IP (OPTCGSim survives so far, bundled art and all, but tolerance ≠ safety). Art layer is separable so a DMCA on images doesn't kill the game |

Rejected along the way: itch.io (user preference), GitHub Pages (no Brotli headers),
Git LFS (1 GB/mo bandwidth), jsDelivr (50 MB cap), Photon (Unity Relay already integrated),
Netlify/Vercel (100 GB/mo bandwidth caps).

## ✅ Done (code — all committed to the project)

- **`Assets/Scripts/Updater/UpdateChecker.cs`** — launch-time version check.
  WebGL reloads the page on a new build; desktop raises `OnUpdateAvailable(manifest, mandatory)`.
  `minSupportedBuildNumber` = force-update floor (bump after netcode protocol changes).
- **`Assets/Scripts/Updater/CardAssets.cs`** — all card art/data IO goes through this.
  Desktop/editor: same synchronous `File` reads as always (`UseCdn=false`).
  WebGL: HTTP from R2, `index.json` manifest replaces `File.Exists`.
- **`Assets/Scripts/Updater/Plugins/WebGLUpdater.jslib`** — page reload + open-URL for WebGL.
- **`NetworkBootstrap.cs`** — `UseWebSockets = true` on WebGL (Relay requirement in browsers).
- **All three managers migrated** to CardAssets (compiled + smoke-tested ✅):
  - `MainMenuManager` — coalesced re-render in `Update` when async art lands; boot sequence in `Start()` (`UpdateChecker.CheckAsync` → `CardAssets.InitAsync`).
  - `GameManager` — async sprite pipeline into existing caches; re-render gated on `!isDraggingHandCard && !isDraggingAttack`; DON back falls back to regular back while loading.
  - `DeckBuilderManager` — fetch coroutines already URI-based, so only path resolution changed; face-detect heuristic is desktop-only (face-data.json covers web).
- **`Deploy/`** — `_headers` (Pages Brotli+cache config), `version.json` (starter manifest),
  `upload_assets.ps1` (R2 upload + index.json generation), `DEPLOY.md` (full walkthrough),
  `MIGRATION.md` (call-site map, now historical).

## ✅ Done — infrastructure (2026-07-05)

1. ~~Cloudflare account + R2 bucket~~ — bucket `optcg-assets` created, CORS set,
   Public Development URL enabled: `https://pub-1c84d77f14ce4cd7a7152662d1e2db58.r2.dev`
   (rate-limited dev URL — swap for a custom domain before real release).
   Pages project not yet created (first `wrangler pages deploy` will create it).
2. ~~Tooling~~ — rclone configured with bucket-scoped R2 API token (remote name `r2`).
   NOTE: token keys were pasted into a chat once — rotate the token when convenient.
3. ~~Upload~~ — **v1 = 5,313 files / 2.48 GiB** live on R2, `index.json` manifest included.
   (Script learned `--s3-no-check-bucket` — scoped tokens can't HeadBucket.)
4. Placeholders: `CardAssets.CdnBaseUrl` ✅ filled. Still open:
   - `UpdateChecker.ManifestBaseUrl` → Pages domain (known after first deploy)
   - `Deploy/version.json` `downloads.*` → GitHub repo releases URL
5. **Still open — delete local junk:** `StreamingAssets/Cards/_tmp_fix`, `_tmp_unblend`,
   `_tmp_verify`, `face-debug`; delete `RemoteCardArt.cs` if still present.

## ✅ Done — first WebGL release (2026-07-05 evening, now paused — see pivot below)

6. **Build settings** (DEPLOY.md §1): Compression = Brotli, Decompression Fallback = OFF,
   Name Files As Hashes = ON. Built successfully (`Assets/Editor/WebGLBuildProcessor.cs`
   auto-excludes `StreamingAssets/Cards` from WebGL builds; recovery menu: Tools > OPTCG >
   Restore StreamingAssets Cards).
7. `.data.br` = 5.5 MiB, `.wasm.br` = 11.3 MiB — both under the 25 MiB Pages cap, no R2 offload
   needed.
8. Copied `Deploy/_headers` + `Deploy/version.json` into the build output; `wrangler pages deploy`
   created the `optcg-sim` Pages project and deployed. `UpdateChecker.ManifestBaseUrl` filled in
   (`https://optcg-sim.pages.dev`).
9. **Test:** found and fixed a real bug — `index.json` had been generated with `Set-Content
   -Encoding UTF8` (Windows PowerShell 5.1 always adds a BOM with that flag), which silently broke
   `JsonUtility.FromJson` in `CardAssets.InitAsync` → empty index → **all card art failed to
   load**. Fixed `upload_assets.ps1` to write BOM-less UTF-8 and re-uploaded `index.json`.
   Multiplayer (Relay web↔web / web↔desktop) test **not done** — deprioritized by the pivot below
   before getting to it.

**Status: paused, not reverted.** The Pages deployment and R2 bucket are left live (free tier, no
cost/harm sitting idle) but not being actively maintained — see pivot section below for why.

## 🔀 Pivot: desktop-only client with Velopack patcher (2026-07-05, later same session)

**Decision:** stop investing further in the WebGL/R2-CDN path and go all-in on a downloadable,
self-patching Windows client — closer to how the real OPTCGSim (optcgsim.com) works. Reasoning:

- User wants the "install once, patch incrementally, everything local" experience, not "always
  fetch over HTTP." OPTCGSim's own patch notes confirm this is the standard approach for this kind
  of app (mini-patches per platform, new cards + fixes each release).
- Original blocker was GitHub Releases' 2 GiB/file cap vs. the 2.3 GB `OfficialById` art folder.
  Solved: 1,668 of 2,649 official card images were confirmed fully opaque (alpha channel uniformly
  255 — no real transparency used) and re-encoded PNG→JPEG q85, shrinking `OfficialById` from
  2.3 GB → 1.2 GB. The remaining 981 files have genuine per-pixel transparency (die-cut/alt-art,
  concentrated in newer sets — OP13-16, EB03, ST27/29 are almost entirely this type) and correctly
  stayed PNG. Originals backed up in full at
  `C:\Users\Nperr\CardArtBackup\OfficialById_PNG_original\` before any conversion.
  `CardAssets.ArtCandidates()` updated to try `.jpg` before `.png` per card.
- Compared against the actual OPTCGSim client's own asset files (found locally at
  `C:\Users\Nperr\Games\1.34a_Windows\Builds_Windows\OPTCGSim_Data\StreamingAssets\Cards`,
  435 MB total for their whole library) to see if swapping in any of their files would save more
  space. Verdict: **no swap** — every one of their non-"_small" files is capped at ~480px width
  vs. our 600-717px; it's a uniform resolution downgrade across their whole library, not a more
  efficient encode of the same fidelity. Not worth the quality loss.
- At ~1.2 GB, bundling the full card library directly into a Velopack-packaged installer is
  realistic (single GitHub Release asset, well under the 2 GiB cap), so **no CDN dependency is
  needed for desktop at all** — `CardAssets.UseCdn` stays `false` for desktop as it already was;
  `StreamingAssets/Cards` ships in the build normally (only `WebGLBuildProcessor` ever stripped it,
  and only for the WebGL target).
- Their own patcher (`hpatchz.exe`, found in their install dir) is HDiffPatch, a lightweight
  open-source binary-diff tool — confirms a custom patcher isn't required, an off-the-shelf tool is
  the norm here too. Chose **Velopack** over building something bespoke: it already sits on top of
  GitHub Releases (already set up), auto-generates delta patches, and needs no custom server/port
  (avoids the exact firewall/port-7777 complaints in OPTCGSim's own patch notes).
- **Known friction:** Velopack's update/restart logic depends on `System.Diagnostics.Process`,
  which doesn't work under Unity's IL2CPP scripting backend (the Windows Standalone default).
  Fix: switch the Windows Standalone scripting backend to **Mono** instead — avoids needing a
  separate native launcher exe. Trade-off: slightly slower + less obfuscation than IL2CPP, judged
  acceptable for a non-performance-critical fan project.

## ✅ Done — desktop patcher pipeline (2026-07-06/07)

10. ~~Player Settings: switch Windows Standalone scripting backend from IL2CPP → Mono.~~ Done.
11. ~~Velopack setup~~ — Done. `vpk` installed as a global dotnet tool (`dotnet tool install -g
    vpk`; the .NET 8 SDK itself had to be installed first via winget — `vpk` has no standalone
    binary release, only a dotnet-tool nupkg). `Velopack.dll` + its `Microsoft.Win32.Registry.dll`
    dependency dropped into `Assets/Plugins` (Newtonsoft.Json already satisfied by Unity's own
    `com.unity.nuget.newtonsoft-json` package — no duplicate added).
    `Assets/Scripts/Updater/VelopackBootstrap.cs` calls `VelopackApp.Build().Run()` via
    `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]` (earliest
    hook Unity's scripting layer exposes — see known limitation below). Real update
    check/download/apply/restart wired into `UpdateChecker.CheckAndApplyDesktopUpdateAsync()`
    using `Velopack.UpdateManager` + `Velopack.Sources.GithubSource` pointed at
    `github.com/valrentcg/optcg-sim`, called from `MainMenuManager.BootUpdateAndAssetsOnce()`.
    Additive — the original `version.json`/`buildNumber` manifest check stayed untouched (still
    used for WebGL reload + `assetsVersion` CDN gating).
12. ~~Build~~ — Done, `Builds\optcg-windows` (Windows Standalone, Mono backend). Along the way,
    found and fixed a real pre-existing bug surfaced only by an actual Standalone build (Editor
    Play Mode never strips shaders): `CardHoverGlow.shader`, `RoundedCardUI.shader`, and
    `CardDissolve.shader` are only ever reached via `Shader.Find("...")` at runtime with no
    material asset referencing them, so Unity's build-time shader stripping dropped all three —
    crashed `GameManager.Awake()` → `BuildShell()` → `CreateRimGlowMaterial()` with a null-shader
    `ArgumentNullException`, manifesting as no glow effects + a black screen on Play Solo vs Self.
    Fixed by adding all three to **Always Included Shaders** in
    `ProjectSettings/GraphicsSettings.asset` (7 → 10 entries).
13. ~~Package + publish~~ — Done. `vpk pack --packId OPTCGSim --packVersion <ver> --packDir
    <build folder> --mainExe "One Piece TCG Simulator.exe" --packAuthors valrentcg --packTitle
    "One Piece TCG Simulator" --outputDir Builds\optcg-releases`, then a GitHub Release created +
    all generated files (Setup.exe, full nupkg, delta nupkg if present, portable zip, `RELEASES`,
    `releases.win.json`, `assets.win.json`) uploaded as release assets via the GitHub REST API
    (`gh` CLI failed to install via winget — MSI error 1603, likely needs elevation — never
    revisited; REST API + a Bearer token worked fine as a substitute). **The repo had to be made
    public** — it was private, which made both the release-asset downloads and Velopack's
    `GithubSource` update-check fail (unauthenticated calls to a private repo/its assets both come
    back 404, indistinguishable from "doesn't exist"). Confirmed going public doesn't newly expose
    anything: no committed secrets found in git history, and the stats-forgeability question is an
    unrelated, already-documented client-trust issue (see `StatsStore.cs`'s own comment) that
    exists at the shipped-binary level regardless of source visibility.
14. ~~Test~~ — Done, fully verified: fresh install via `OPTCGSim-win-Setup.exe` (correct
    `%LocalAppData%\OPTCGSim\` layout, shortcuts created, game runs clean), then a real v1.0.0 →
    v1.0.1 release cycle — installed client detected the update, downloaded **only the 2.9MB
    delta** (vs. 710MB full package), applied it, and relaunched correctly into 1.0.1 (confirmed
    via `sq.version` and a version-number debug log line).
15. **`UpdateChecker.cs` reconciliation — decided:** kept both systems, additive rather than
    replacing. The original `version.json` check still drives WebGL reload + `assetsVersion`; the
    new `CheckAndApplyDesktopUpdateAsync()` handles real desktop auto-update via Velopack. No
    conflict since they're gated to different platforms already.
16. R2 token rotation — still not done, still pending, carried over (unrelated to this pipeline).

**Known limitation, not fixed:** Unity has no fast hook-only process entry point. When Velopack
launches the exe for an install/update lifecycle hook, the full Unity engine boots before `Run()`
gets a chance to detect the hook and exit early — the game window briefly opens during
install (produces Setup.exe's "Install Partially Succeeded" warning even though the file install
itself is fine), and the auto-restart-after-update's Player.log looks truncated (though the update
demonstrably still applies and the relaunched process is healthy). Cosmetic; matches a known
community-reported Velopack+Unity friction point (GitHub issue velopack/velopack#164 — no official
Unity package exists for this yet, the "proper" fix is a separate native launcher exe).

## 📋 How to publish a new release (the actual method, once code changes are ready)

1. Bump the version in **two places**: `UpdateChecker.CurrentBuildNumber` (constant in
   `Assets/Scripts/Updater/UpdateChecker.cs`) and Player Settings → Version (or edit
   `ProjectSettings/ProjectSettings.asset`'s `bundleVersion` directly).
2. Rebuild: File → Build Profiles → Windows → **Build** → `Builds\optcg-windows` (double-check the
   save dialog's path each time — it doesn't reliably remember the subfolder, see
   [[optcg-sim-release-gotchas]]).
3. Package + publish: `Deploy\publish_release.ps1 -Version <version>` does both steps 3 and 4 in
   one shot (packs with `vpk`, creates the GitHub Release, uploads everything). Requires
   `GITHUB_TOKEN` already set (see path-forward section below) — run it in your own terminal, or
   have Claude run it with `dangerouslyDisableSandbox: true`. Doing it by hand instead: `vpk pack`
   (see command earlier in this doc) with the new version number, pointing `--outputDir` at the
   **same** `Builds\optcg-releases` folder used last time (so `vpk` can see the previous version's
   cached package and generate a delta automatically), then create a GitHub Release tagged
   `v<version>` and upload every file it produced (Setup.exe, full nupkg, delta nupkg, portable
   zip, `RELEASES`, `releases.win.json`, `assets.win.json` — the manifest files are cumulative and
   regenerated each pack, so always upload the freshest copies to the newest release).
4. Verify: launch an older installed copy and confirm it detects + downloads + applies the update
   (check `%LocalAppData%\OPTCGSim\current\sq.version` for the new version number, and Player.log
   for `[UpdateChecker] Velopack update found: ...`).

## 🔑 GitHub token workflow — path forward (avoiding re-pasting every release)

This session used a manual, one-token-per-publish workflow: paste a token into a local file →
Claude extracts it → makes the API calls → deletes the file. That's fine for occasional use but
tedious for frequent releases. Two ways to fix it, not mutually exclusive:

1. **Set a persistent token once** (recommended if publishing solo, without `gh`): in your own
   terminal (not pasted through chat), run
   `[System.Environment]::SetEnvironmentVariable('GITHUB_TOKEN','<token>','User')`. This writes it
   to the Windows user profile permanently, so any future *fresh* process (including a new Claude
   Code session, since each `dangerouslyDisableSandbox` call is a fresh process that reads current
   env vars at startup) can read `$env:GITHUB_TOKEN` directly — no re-pasting. Trade-off: the token
   sits in the registry indefinitely rather than existing only transiently on disk for a few
   minutes.
2. **Fix the `gh` CLI install** (the more "correct" long-term fix): the winget install failed with
   MSI error 1603 this session, likely needs an elevated (Run as Administrator) PowerShell, or a
   direct download of the `.msi` from `github.com/cli/cli/releases`. Once installed, `gh auth
   login` stores credentials securely via Windows Credential Manager (not a plaintext file, not a
   raw env var), and the whole REST-API/curl dance in this doc becomes just `gh release create` +
   `gh release upload` — also generally more robust than the manual multipart uploads used this
   session.

Either way, a **fine-grained PAT scoped to just this repo, Contents: Read and write** is the right
scope (Metadata: Read-only is auto-required, can't be changed). Revoke/rotate it at
`github.com/settings/tokens` whenever it's no longer needed, or if it's ever pasted somewhere it
shouldn't be.

**Sandbox gotcha (read before debugging a "Bad credentials" error):** any Claude Code Bash/
PowerShell call that hits GitHub's authenticated API must pass `dangerouslyDisableSandbox: true` —
the default sandboxed execution silently corrupts the Authorization header, and three genuinely
valid fresh tokens all failed with a clean "Bad credentials" this session before that was
discovered. Full writeup in [[optcg-sim-release-gotchas]].

## ⚠ Open questions / known gaps

- **Deck saves on WebGL** — DeckBuilder persists decks via `File` to disk (untouched by the
  migration). Web players need PlayerPrefs/IndexedDB or Cloud Save (already a project
  dependency — likely the right answer). NOT YET INVESTIGATED.
- **Relay WebSocket mode untested** — code is in place; needs a real WebGL build to verify
  (also cross-play desktop↔web protocol compatibility).
- **R2 10 GB free cap** — versioned prefixes (`v1/`, `v2/`) duplicate storage; prune old
  versions once players have migrated (index.json short cache = safe within ~5 min).
- **WebGL heap** — thousands of decoded card textures could pressure memory in long deck-builder
  sessions; the existing tile-pool + thumb tiers mitigate. Watch in testing.
- **Bandai risk** — no monetization, no official branding, disclaimer on the site, be ready
  to take art down on notice.

## Session log

- **2026-07-06/07** — Desktop patcher pipeline fully built and verified end-to-end. Installed
  .NET 8 SDK + `vpk` global tool (neither was on the machine; `vpk` has no standalone binary,
  dotnet-tool-only). Wired real `VelopackApp.Build().Run()` + `UpdateManager`/`GithubSource` update
  flow into `UpdateChecker.cs`/`MainMenuManager.cs`. Found + fixed a real pre-existing bug only a
  real Standalone build surfaces: 3 runtime-`Shader.Find`-only shaders (glow/dissolve/rounded-card)
  were being stripped from the build, crashing `GameManager.Awake()` on Play Solo vs Self — fixed
  via Always Included Shaders. Packaged + published v1.0.0, then discovered the repo being private
  broke both downloads and the update-check (404s) — made it public after confirming no committed
  secrets and that the stats-forgeability question is a separate, already-documented, pre-existing
  client-trust issue unrelated to source visibility. Bumped to v1.0.1 and proved the actual payoff:
  delta patch was 2.9MB vs. a 710MB full package, applied and relaunched correctly on a real
  installed copy. Lost real time to two environment gotchas now written up in
  `project_optcg_sim_release_gotchas` memory: the sandboxed tool execution silently breaks
  authenticated GitHub API calls (needs `dangerouslyDisableSandbox: true`), and the main built
  `.exe` is a stub that doesn't change for script-only edits (check `Assembly-CSharp.dll` instead
  when verifying a rebuild actually happened). `gh` CLI install failed (MSI 1603, likely needs
  elevation) — worked around with raw REST API calls; worth revisiting for a cleaner token-storage
  story than the current re-paste-per-release workflow (see the two path-forward options above).
- **2026-07-05 (late evening)** — WebGL built + deployed to Cloudflare Pages (`optcg-sim.pages.dev`
  live). Found + fixed a real bug: BOM in `index.json` (from PowerShell `-Encoding UTF8`) broke
  `JsonUtility.FromJson`, silently emptying the card-art index — all art was invisible until fixed.
  Deep-dived a card-loading-speed question, which led to deciding to pivot: dropped WebGL/R2-CDN as
  the active path (left live, not reverted) in favor of a downloadable Windows client with a real
  Velopack patcher, modeled on how the real OPTCGSim works. Converted `OfficialById` PNG→JPEG where
  safe (2.3 GB → 1.2 GB, 1,668 opaque cards converted, 981 real-transparency cards left as PNG,
  full originals backed up), compared against OPTCGSim's own local install and found their assets
  are uniformly lower-resolution (not worth pulling in). Next session: switch Windows Standalone to
  Mono scripting backend, wire up Velopack, build + package + publish first patcher-based release.
- **2026-07-05 (evening)** — Infrastructure live: R2 bucket + CORS + dev URL, rclone token,
  v1 assets uploaded (5,313 files / 2.48 GiB), CdnBaseUrl wired in, WebGLBuildProcessor added.
  Next session: Unity WebGL build → `wrangler pages deploy` → fill ManifestBaseUrl → test.
- **2026-07-05** — Researched hosting (full writeup: `hosting-research.md` in chat history /
  see Architecture table). Chose Cloudflare stack. Built UpdateChecker/CardAssets/jslib,
  Deploy configs + scripts. Migrated MainMenuManager, GameManager, DeckBuilderManager to
  CardAssets (fixed CS0162 warnings via `static readonly UseCdn`). WebSocket transport wired
  into NetworkBootstrap. All compiled + game/art load confirmed by Nathan.
