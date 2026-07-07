# Uploads StreamingAssets/Cards to Cloudflare R2 under a version prefix and
# generates the index.json manifest that CardAssets.Exists() relies on in WebGL.
#
# Prereqs: rclone configured with an "r2" remote (rclone config: type=s3,
# provider=Cloudflare, + R2 API token creds), run from the project root.
#
# Usage:  .\Deploy\upload_assets.ps1 -AssetsVersion 1 -Bucket optcg-assets

param(
    [Parameter(Mandatory = $true)][int]$AssetsVersion,
    [string]$Bucket = "optcg-assets",
    [string]$CardsDir = "Assets\StreamingAssets\Cards"
)

$ErrorActionPreference = "Stop"
$prefix = "v$AssetsVersion"

# Folders that must never ship (build-time tooling output).
$exclude = @("_tmp_fix/**", "_tmp_unblend/**", "_tmp_verify/**", "face-debug/**", "*.meta")

Write-Host "== Generating index.json (manifest of every shipped file) =="
$root = (Resolve-Path $CardsDir).Path
$files = Get-ChildItem $root -Recurse -File |
    Where-Object {
        $rel = $_.FullName.Substring($root.Length + 1).Replace('\', '/')
        -not ($rel -like "_tmp_*" -or $rel -like "face-debug/*" -or $rel -like "*.meta")
    } |
    ForEach-Object { $_.FullName.Substring($root.Length + 1).Replace('\', '/') }

$manifest = @{ files = @($files) } | ConvertTo-Json -Compress
$indexPath = Join-Path $env:TEMP "index.json"
# Windows PowerShell 5.1's -Encoding UTF8 always prepends a BOM, which breaks
# Unity's JsonUtility.FromJson (silently, via a caught exception) -> empty
# index -> every CardAssets.Exists() check fails. Write BOM-less UTF-8 instead.
[System.IO.File]::WriteAllText($indexPath, $manifest, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "   $($files.Count) files listed."

Write-Host "== Uploading assets to r2:$Bucket/$prefix (long immutable cache) =="
$excludeArgs = $exclude | ForEach-Object { @("--exclude", $_) }
# --s3-no-check-bucket: bucket-scoped R2 tokens may not HeadBucket/CreateBucket
rclone copy $root "r2:$Bucket/$prefix" @excludeArgs `
    --transfers 32 --checkers 32 --s3-no-check-bucket `
    --header-upload "Cache-Control: public, max-age=31536000, immutable" `
    --progress

Write-Host "== Uploading index.json (short cache - it changes each version) =="
rclone copyto $indexPath "r2:$Bucket/$prefix/index.json" `
    --s3-no-check-bucket `
    --header-upload "Cache-Control: public, max-age=300"

Write-Host "Done. Now bump assetsVersion to $AssetsVersion in version.json and redeploy it to Pages."
