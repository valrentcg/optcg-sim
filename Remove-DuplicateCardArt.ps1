<#
  Remove-DuplicateCardArt.ps1
  Deletes the two UNUSED duplicate card-art folders from the Unity project:
     Assets\StreamingAssets\Cards\OfficialLibrary   (full duplicate, keyed by numeric set IDs)
     Assets\StreamingAssets\Cards\Official           (leftover partial copy: ST01/ST02 only)
  ...along with their sibling .meta files. The game loader only reads
  'OfficialById', so removing these does not affect the app.

  CLOSE UNITY before running. Unity rebuilds its asset database on next open.

  Usage (PowerShell):
     cd "C:\Users\Nperr\One Piece TCG Simulator\Assets\StreamingAssets\Cards"
     .\Remove-DuplicateCardArt.ps1            # dry run: shows sizes, deletes nothing
     .\Remove-DuplicateCardArt.ps1 -Confirm   # actually deletes
#>

param([switch]$Confirm)

$ErrorActionPreference = 'Stop'
$cards = "C:\Users\Nperr\One Piece TCG Simulator\Assets\StreamingAssets\Cards"

# Folder targets and their Unity .meta sidecars.
$targets = @(
    (Join-Path $cards 'OfficialLibrary'),
    (Join-Path $cards 'Official')
)
$metas = @(
    (Join-Path $cards 'OfficialLibrary.meta'),
    (Join-Path $cards 'Official.meta')
)

function Get-FolderSizeMB($path) {
    if (-not (Test-Path $path)) { return 0 }
    $bytes = (Get-ChildItem -LiteralPath $path -Recurse -File -Force |
              Measure-Object -Property Length -Sum).Sum
    if ($null -eq $bytes) { return 0 }
    return [math]::Round($bytes / 1MB, 1)
}

Write-Host "Scanning duplicate card-art folders..." -ForegroundColor Cyan
$total = 0
foreach ($t in $targets) {
    if (Test-Path $t) {
        $mb = Get-FolderSizeMB $t
        $total += $mb
        Write-Host ("  {0,-14} {1,8} MB" -f (Split-Path $t -Leaf), $mb)
    } else {
        Write-Host ("  {0,-14} (not found)" -f (Split-Path $t -Leaf)) -ForegroundColor DarkGray
    }
}
Write-Host ("  --------------------------") -ForegroundColor DarkGray
Write-Host ("  Reclaimable: {0} MB" -f $total) -ForegroundColor Green

if (-not $Confirm) {
    Write-Host "`nDRY RUN. Nothing deleted. Re-run with -Confirm to delete:" -ForegroundColor Yellow
    Write-Host "    .\Remove-DuplicateCardArt.ps1 -Confirm"
    return
}

Write-Host "`nMake sure Unity is CLOSED. Deleting in 5 seconds (Ctrl+C to abort)..." -ForegroundColor Yellow
Start-Sleep -Seconds 5

foreach ($t in $targets) {
    if (Test-Path $t) {
        Remove-Item -LiteralPath $t -Recurse -Force
        Write-Host ("Deleted folder: {0}" -f $t) -ForegroundColor Green
    }
}
foreach ($m in $metas) {
    if (Test-Path $m) {
        Remove-Item -LiteralPath $m -Force
        Write-Host ("Deleted meta:   {0}" -f $m) -ForegroundColor Green
    }
}

Write-Host "`nDone. Reopen Unity; it will refresh the asset database." -ForegroundColor Cyan
