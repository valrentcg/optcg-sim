# One Piece TCG Simulator - Velopack release publisher.
#
# Packages a Windows Standalone build with vpk and publishes it (+ delta patch,
# if a previous version is cached in $OutputDir) to GitHub Releases.
#
# Prereqs (one-time):
#   - .NET SDK + `vpk` global tool installed (dotnet tool install -g vpk)
#   - GitHub CLI installed + authenticated: gh auth login
#     (credentials stored in Windows Credential Manager - no token pasting,
#     no per-session re-auth needed)
#   - A completed Windows Standalone build already sitting in $BuildDir, with the
#     version already bumped in UpdateChecker.CurrentBuildNumber and
#     ProjectSettings bundleVersion.
#
# Usage:
#   .\Deploy\publish_release.ps1 -Version 1.0.2

param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$BuildDir = "C:\Users\Nperr\Builds\optcg-windows",
    [string]$OutputDir = "C:\Users\Nperr\Builds\optcg-releases",
    [string]$Repo = "valrentcg/optcg-sim",
    [string]$PackId = "OPTCGSim",
    [string]$MainExe = "One Piece TCG Simulator.exe",
    [string]$PackAuthors = "valrentcg",
    [string]$PackTitle = "One Piece TCG Simulator"
)

$ErrorActionPreference = "Stop"

$gh = "C:\Program Files\GitHub CLI\gh.exe"
if (-not (Test-Path $gh)) { $gh = "gh" }

& $gh auth status *> $null
if ($LASTEXITCODE -ne 0) {
    Write-Error "gh is not authenticated. Run 'gh auth login' first."
    exit 1
}

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

$candidates = @(
  "$PackId-win-Setup.exe",
  "$PackId-$Version-full.nupkg",
  "$PackId-$Version-delta.nupkg",
  "$PackId-win-Portable.zip",
  "RELEASES",
  "releases.win.json",
  "assets.win.json"
)

$assetArgs = @()
foreach ($name in $candidates) {
  $path = Join-Path $OutputDir $name
  if (Test-Path $path) {
    $assetArgs += $path
  } else {
    Write-Output "Skipping $name (not present - expected for delta on a first release)"
  }
}

& $gh release create "v$Version" @assetArgs --repo $Repo --title "v$Version" --notes "Automated release."
if ($LASTEXITCODE -ne 0) { throw "gh release create failed" }

$url = & $gh release view "v$Version" --repo $Repo --json url -q .url
Write-Output "=== Done. Release: $url ==="
