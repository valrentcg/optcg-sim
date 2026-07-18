# Verifies the Velopack install-hook fix on a freshly built Windows player.
#
# Reproduces exactly what the Velopack installer does: launches the built exe
# with "--veloapp-install <ver>" and checks that it exits with code 0 in well
# under Velopack's 30s hook window. A PASS here means setup will report
# "Install Succeeded" instead of "Install Partially Succeeded".
#
# Usage (after building the Windows player):
#   .\Deploy\verify_install_hook.ps1
#   .\Deploy\verify_install_hook.ps1 -BuildDir "C:\Users\Nperr\Builds\optcg-windows"

param(
    [string]$BuildDir = "C:\Users\Nperr\Builds\optcg-windows",
    [string]$MainExe  = "One Piece TCG Simulator.exe",
    [int]$TimeoutSecs = 30
)

$exe = Join-Path $BuildDir $MainExe
if (-not (Test-Path $exe)) { Write-Error "Build not found: $exe`nBuild the Windows player into $BuildDir first."; exit 1 }

foreach ($verb in @("--veloapp-install", "--veloapp-updated", "--veloapp-uninstall")) {
    Write-Host "Testing hook $verb ..." -NoNewline
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $p = Start-Process -FilePath $exe -ArgumentList $verb, "1.0.99" -PassThru
    if (-not $p.WaitForExit($TimeoutSecs * 1000)) {
        $sw.Stop()
        try { $p.Kill() } catch {}
        Write-Host " FAIL (timed out >${TimeoutSecs}s - would show 'Install Partially Succeeded')" -ForegroundColor Red
        exit 1
    }
    $sw.Stop()
    if ($p.ExitCode -ne 0) {
        Write-Host " FAIL (exit code $($p.ExitCode) after $([math]::Round($sw.Elapsed.TotalSeconds,1))s)" -ForegroundColor Red
        exit 1
    }
    Write-Host " PASS (exit 0 in $([math]::Round($sw.Elapsed.TotalSeconds,1))s)" -ForegroundColor Green
}

Write-Host "`nAll hooks exit cleanly. Safe to package + publish." -ForegroundColor Green
