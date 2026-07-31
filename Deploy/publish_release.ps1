# One Piece TCG Simulator - Velopack release publisher.
#
# Packages a Windows Standalone build with vpk and publishes it (+ delta patch, if a
# previous version is cached in $OutputDir) to GitHub Releases via the gh CLI.
#
# Prereqs (one-time):
#   - .NET SDK + `vpk` global tool  (dotnet tool install -g vpk)
#   - GitHub CLI `gh` installed and authenticated (`gh auth login`). gh manages its own
#     token - NO GITHUB_TOKEN env var is needed, and it runs fine under Claude Code's
#     normal sandbox (unlike the old raw-REST path, which repeatedly 400'd on JSON).
#   - A completed Windows Standalone build sitting in $BuildDir.
#   - A Deploy/RELEASE_NOTES_<version>.md for the release body.
#
# Usage:
#   .\Deploy\publish_release.ps1 -Version 1.0.15
#   .\Deploy\publish_release.ps1 -Version 1.0.15 -Target advanced-bot-search-knee   # tag the released commit
#
# VERSIONING NOTE: desktop auto-update is driven by Velopack SEMVER (this -Version) vs the
# latest GitHub release - NOT by UpdateChecker.CurrentBuildNumber (which is compiled into the
# build). Keep version.json's buildNumber == the build's CurrentBuildNumber so a freshly-
# updated client never sees a false "update available". Bump CurrentBuildNumber BEFORE building
# if you want the build number to increment. (In practice a bare -Version bump ships fine.)

param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$BuildDir = "C:\Users\Nperr\Builds\optcg-windows",
    [string]$OutputDir = "C:\Users\Nperr\Builds\optcg-releases",
    [string]$Repo = "valrentcg/optcg-sim",
    [string]$PackId = "OPTCGSim",
    [string]$MainExe = "One Piece TCG Simulator.exe",
    [string]$PackAuthors = "valrentcg",
    [string]$PackTitle = "One Piece TCG Simulator",
    [string]$Target = ""   # branch or commit the build came from; tags the release there (else gh defaults to main)
)

$ErrorActionPreference = "Stop"

# Fail fast if gh isn't logged in (before spending a minute+ on vpk pack).
gh auth status 2>$null | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Error "gh CLI is not authenticated. Run 'gh auth login' first."; exit 1 }

# ── ANTI-DESYNC PREFLIGHT ────────────────────────────────────────────────────────────────────
# MatchStartPayload.build carries UpdateChecker.CurrentBuildNumber and the guest ABORTS on a
# mismatch, because both clients replay the same GameCommand log and divergent engine logic
# silently desyncs into different boards. That guard is only as good as the const, which used to
# be maintained by a human reading a comment. These two checks make it mechanical.
$repoRoot   = Split-Path -Parent $PSScriptRoot
$checkerSrc = Join-Path $repoRoot "Assets\Scripts\Updater\UpdateChecker.cs"
$verJsonP   = Join-Path $PSScriptRoot "version.json"

$constMatch = Select-String -Path $checkerSrc -Pattern 'CurrentBuildNumber\s*=\s*(\d+)' | Select-Object -First 1
if (-not $constMatch) { Write-Error "Could not read CurrentBuildNumber from $checkerSrc"; exit 1 }
$constBuild = [int]$constMatch.Matches[0].Groups[1].Value
$jsonBuild  = [int]((Get-Content $verJsonP -Raw | ConvertFrom-Json).buildNumber)

# 1. The compiled const and the deployed manifest must agree, or a freshly-updated client either
#    sees a false "update available" or believes it is current when it is not.
if ($constBuild -ne $jsonBuild) {
    Write-Error "Build number mismatch: UpdateChecker.CurrentBuildNumber=$constBuild but Deploy/version.json buildNumber=$jsonBuild. Make them equal before releasing."
    exit 1
}

# 2. If the ENGINE changed since the const last moved, the const is stale and two builds with
#    different rules would both report the same number — exactly the desync `build` exists to stop.
$lastCheckerCommit = (git log -1 --format=%H -- "Assets/Scripts/Updater/UpdateChecker.cs" 2>$null)
if ($LASTEXITCODE -eq 0 -and $lastCheckerCommit) {
    $engineSince = (git log --oneline "$lastCheckerCommit..HEAD" -- "Assets/Scripts/Engine/" 2>$null)
    if ($engineSince) {
        $n = ($engineSince | Measure-Object -Line).Lines
        Write-Error "CurrentBuildNumber ($constBuild) is STALE: $n engine commit(s) landed since it was last bumped. Engine changes are replay-affecting - bump CurrentBuildNumber AND Deploy/version.json buildNumber, then re-run."
        exit 1
    }
}
$dirtyEngine = (git status --porcelain -- "Assets/Scripts/Engine/" 2>$null)
if ($dirtyEngine) { Write-Warning "Uncommitted engine changes present - make sure CurrentBuildNumber ($constBuild) accounts for them." }
Write-Output "Anti-desync preflight OK (build $constBuild, engine in sync)."

# Release notes are resolved ONCE and used for both the packed release and the GitHub release,
# so the in-app "What's New" panel and the releases page can never disagree.
$notesPath = Join-Path $PSScriptRoot "RELEASE_NOTES_$Version.md"
if (-not (Test-Path $notesPath)) {
  Write-Warning "No RELEASE_NOTES_$Version.md - the updater's WHAT'S NEW panel will be blank for this release."
}

Write-Output "=== Packaging v$Version ==="
# --releaseNotes is what puts the notes INSIDE the Velopack release, which is where the in-app
# updater reads them from (UpdateChecker looks for NotesMarkdown/NotesHtml on the target release).
# It was never passed, so every release shipped with empty notes and the WHAT'S NEW panel came up
# blank on every update. The version.json fallback could not cover it either: that manifest is
# served from the Pages site, which is only redeployed with the WebGL build.
$packNotesArg = if (Test-Path $notesPath) { @('--releaseNotes', $notesPath) } else { @() }
vpk pack `
  --packId $PackId `
  --packVersion $Version `
  --packDir $BuildDir `
  --mainExe $MainExe `
  --packAuthors $PackAuthors `
  --packTitle $PackTitle `
  --outputDir $OutputDir `
  @packNotesArg
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed" }

Write-Output "=== Creating GitHub release v$Version ==="
$notesArg = if (Test-Path $notesPath) { @('--notes-file', $notesPath) } else { @('--notes', 'Automated release.') }
$targetArg = if ($Target) { @('--target', $Target) } else { @() }

# Velopack asset set (matches every prior release). Skip any not present - the delta is
# absent on a first release; Portable.zip is optional.
$assetNames = @(
  "$PackId-win-Setup.exe",
  "$PackId-$Version-full.nupkg",
  "$PackId-$Version-delta.nupkg",
  "$PackId-win-Portable.zip",
  "RELEASES",
  "releases.win.json",
  "assets.win.json"
)
$assetPaths = @()
foreach ($n in $assetNames) {
  $p = Join-Path $OutputDir $n
  if (Test-Path $p) { $assetPaths += $p } else { Write-Output "Skipping $n (not present)" }
}

# gh CLI creates the release + uploads all assets in one call (a ~2 GB upload can take
# minutes). Use gh - NOT Invoke-RestMethod, whose hand-built JSON 400'd repeatedly.
gh release create "v$Version" --repo $Repo --title "v$Version" --latest @targetArg @notesArg @assetPaths
if ($LASTEXITCODE -ne 0) { throw "gh release create failed" }

Write-Output "=== Done. Release: https://github.com/$Repo/releases/tag/v$Version ==="
