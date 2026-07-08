# One Piece TCG Simulator - Velopack release publisher.
#
# Packages a Windows Standalone build with vpk and publishes it (+ delta patch,
# if a previous version is cached in $OutputDir) to GitHub Releases.
#
# Prereqs (one-time):
#   - .NET SDK + `vpk` global tool installed (dotnet tool install -g vpk)
#   - GITHUB_TOKEN env var set persistently:
#       [System.Environment]::SetEnvironmentVariable('GITHUB_TOKEN','<token>','User')
#     (fine-grained PAT scoped to this repo, Contents: Read and write)
#   - A completed Windows Standalone build already sitting in $BuildDir, with the
#     version already bumped in UpdateChecker.CurrentBuildNumber and
#     ProjectSettings bundleVersion.
#
# Usage:
#   .\Deploy\publish_release.ps1 -Version 1.0.2
#
# If Claude Code is running this (not the user directly in their own terminal),
# it must pass dangerouslyDisableSandbox: true - the default sandboxed execution
# silently breaks the GitHub API's Authorization header. See
# Deploy/RELEASE_ROADMAP.md and the project_optcg_sim_release_gotchas memory.

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

if (-not $env:GITHUB_TOKEN) {
    Write-Error "GITHUB_TOKEN is not set in this session. Set it persistently once with:`n  [System.Environment]::SetEnvironmentVariable('GITHUB_TOKEN','<token>','User')`nthen open a fresh terminal/session."
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
# Release body: use Deploy\RELEASE_NOTES_<version>.md if present (fallback to
# generic text), so the GitHub release page describes what actually changed.
$notesPath = Join-Path $PSScriptRoot "RELEASE_NOTES_$Version.md"
$releaseBody = if (Test-Path $notesPath) { Get-Content $notesPath -Raw } else { "Automated release." }
$headers = @{ Authorization = "Bearer $($env:GITHUB_TOKEN)"; Accept = "application/vnd.github+json"; "X-GitHub-Api-Version" = "2022-11-28" }
$bodyJson = @{ tag_name = "v$Version"; name = "v$Version"; body = $releaseBody; draft = $false; prerelease = $false } | ConvertTo-Json
$release = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases" -Method Post -Headers $headers -Body $bodyJson -ContentType "application/json"
Write-Output "Release created: $($release.html_url)"

$uploadUrlBase = "https://uploads.github.com/repos/$Repo/releases/$($release.id)/assets"

$candidates = @(
  @{ Name = "$PackId-win-Setup.exe"; Type = "application/octet-stream" },
  @{ Name = "$PackId-$Version-full.nupkg"; Type = "application/octet-stream" },
  @{ Name = "$PackId-$Version-delta.nupkg"; Type = "application/octet-stream" },
  @{ Name = "$PackId-win-Portable.zip"; Type = "application/zip" },
  @{ Name = "RELEASES"; Type = "text/plain" },
  @{ Name = "releases.win.json"; Type = "application/json" },
  @{ Name = "assets.win.json"; Type = "application/json" }
)

foreach ($f in $candidates) {
  $path = Join-Path $OutputDir $f.Name
  if (-not (Test-Path $path)) {
    Write-Output "Skipping $($f.Name) (not present - expected for delta on a first release)"
    continue
  }
  $uploadUrl = "$uploadUrlBase`?name=$($f.Name)"
  $uploadHeaders = $headers.Clone()
  $uploadHeaders["Content-Type"] = $f.Type
  Write-Output "Uploading $($f.Name) ($([math]::Round((Get-Item $path).Length/1MB,1)) MB)..."
  try {
    $result = Invoke-RestMethod -Uri $uploadUrl -Method Post -Headers $uploadHeaders -InFile $path
    Write-Output "  OK: $($result.name)"
  } catch {
    Write-Output "  FAILED: $($_.Exception.Message)"
    if ($_.ErrorDetails) { Write-Output "  $($_.ErrorDetails.Message)" }
  }
}

Write-Output "=== Done. Release: $($release.html_url) ==="
