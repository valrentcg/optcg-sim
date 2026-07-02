<#
  Remove-UnusedCardJson.ps1
  Deletes leftover import/working files from Assets\StreamingAssets\Cards that the
  game does NOT read at runtime, plus two orphaned .meta files from folders you
  already removed. The only JSON the game needs - official-card-library.json - is
  explicitly protected and never deleted.

  CLOSE UNITY before running. Unity refreshes its asset database on next open.

  Usage (PowerShell):
     cd "C:\Users\Nperr\One Piece TCG Simulator\Assets\StreamingAssets\Cards"
     .\Remove-UnusedCardJson.ps1            # dry run: lists what WOULD be deleted
     .\Remove-UnusedCardJson.ps1 -Confirm   # actually deletes
#>

param([switch]$Confirm)

$ErrorActionPreference = 'Stop'
$cards = "C:\Users\Nperr\One Piece TCG Simulator\Assets\StreamingAssets\Cards"

# NEVER delete these (the game loads them).
$protect = @(
    'official-card-library.json',
    'optcg_card_back.jpg',
    'donCardAltArt.png',
    'CardBackDon.png'
)

# Explicit unused working files.
$explicit = @(
    'official-card-definitions.json',
    'official-series-index.json',
    'official-st01-st02.json',
    'oppp-final-image-status.json',
    'oppp-image-import-report.json'
)

# Orphaned .meta files left behind by the deleted duplicate folders.
$orphanMeta = @(
    'Official.meta',
    'OfficialLibrary.meta'
)

# Build the delete list: explicit files + every oppp-*.json + all their .meta sidecars + orphan metas.
$toDelete = New-Object System.Collections.Generic.List[string]

foreach ($name in $explicit) {
    $p = Join-Path $cards $name
    if (Test-Path $p) { $toDelete.Add($p) }
}
foreach ($f in Get-ChildItem -LiteralPath $cards -File -Filter 'oppp-*.json') {
    $toDelete.Add($f.FullName)
}
# .meta sidecars for whatever we're deleting so far
foreach ($p in @($toDelete.ToArray())) {
    $m = "$p.meta"
    if (Test-Path $m) { $toDelete.Add($m) }
}
foreach ($name in $orphanMeta) {
    $p = Join-Path $cards $name
    if (Test-Path $p) { $toDelete.Add($p) }
}

# Safety: drop anything protected (matches the base name, with or without .meta).
$toDelete = $toDelete | Where-Object {
    $leaf = Split-Path $_ -Leaf
    $base = $leaf -replace '\.meta$',''
    -not ($protect -contains $base)
} | Sort-Object -Unique

if (-not $toDelete -or $toDelete.Count -eq 0) {
    Write-Host "Nothing to clean up - already tidy." -ForegroundColor Green
    return
}

$bytes = ($toDelete | Where-Object { Test-Path $_ } |
          ForEach-Object { (Get-Item -LiteralPath $_).Length } |
          Measure-Object -Sum).Sum
$mb = [math]::Round(($bytes / 1MB), 2)

Write-Host "Files to remove ($($toDelete.Count)):" -ForegroundColor Cyan
$toDelete | ForEach-Object { Write-Host ("  " + (Split-Path $_ -Leaf)) }
Write-Host ("Reclaimable: {0} MB" -f $mb) -ForegroundColor Green
Write-Host "Protected (kept): $($protect -join ', ')" -ForegroundColor DarkGray

if (-not $Confirm) {
    Write-Host "`nDRY RUN. Nothing deleted. Re-run with -Confirm to delete:" -ForegroundColor Yellow
    Write-Host "    .\Remove-UnusedCardJson.ps1 -Confirm"
    return
}

Write-Host "`nMake sure Unity is CLOSED. Deleting in 5 seconds (Ctrl+C to abort)..." -ForegroundColor Yellow
Start-Sleep -Seconds 5
foreach ($p in $toDelete) {
    if (Test-Path $p) { Remove-Item -LiteralPath $p -Force; Write-Host ("Deleted: " + (Split-Path $p -Leaf)) -ForegroundColor Green }
}
Write-Host "`nDone. Reopen Unity; it will refresh the asset database." -ForegroundColor Cyan
