<#
  Make-UpscaleFolders.ps1
  Creates an empty subfolder for every card set found in OfficialById, inside the
  destination folder. Existing subfolders are left untouched (only missing ones
  are created). Nothing is deleted.

  Usage (PowerShell):
     powershell -ExecutionPolicy Bypass -File .\Make-UpscaleFolders.ps1
#>

param(
    [string]$Source = "C:\Users\Nperr\One Piece TCG Simulator\Assets\StreamingAssets\Cards\OfficialById",
    [string]$Dest   = "C:\Users\Nperr\Pictures\Upscayld OPTCG"
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $Source)) {
    Write-Host "Source not found: $Source" -ForegroundColor Red
    return
}

if (-not (Test-Path -LiteralPath $Dest)) {
    New-Item -ItemType Directory -Path $Dest -Force | Out-Null
    Write-Host "Created destination: $Dest" -ForegroundColor Cyan
}

$sets = Get-ChildItem -LiteralPath $Source -Directory | Select-Object -ExpandProperty Name | Sort-Object
Write-Host ("Found {0} sets in OfficialById." -f $sets.Count) -ForegroundColor Cyan

$created = 0; $skipped = 0
foreach ($s in $sets) {
    $path = Join-Path $Dest $s
    if (Test-Path -LiteralPath $path) {
        $skipped++
        Write-Host ("  skip   {0} (already exists)" -f $s) -ForegroundColor DarkGray
    } else {
        New-Item -ItemType Directory -Path $path -Force | Out-Null
        $created++
        Write-Host ("  create {0}" -f $s) -ForegroundColor Green
    }
}

Write-Host ("`nDone. Created {0}, skipped {1}. Destination: {2}" -f $created, $skipped, $Dest) -ForegroundColor Cyan
