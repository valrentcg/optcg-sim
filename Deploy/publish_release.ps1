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

Write-Output "=== Packaging v$Version ==="
vpk pack `
  --packId $PackId `
  --packVersion $Version `
  --packDir $BuildDir `
  --mainExe $MainExe `
  --packAuthors $PackAuthors `
  --packTitle $PackTitle `
  --outputDir $OutputDir
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed" }

Write-Output "=== Creating GitHub release v$Version ==="
$notesPath = Join-Path $PSScriptRoot "RELEASE_NOTES_$Version.md"
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
