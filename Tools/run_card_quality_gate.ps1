[CmdletBinding()]
param(
    [string]$UnityPath,
    [string]$ResultsRoot,
    [switch]$SkipUnity,
    [switch]$SkipAudits
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
if ([string]::IsNullOrWhiteSpace($ResultsRoot)) {
    $ResultsRoot = Join-Path $repoRoot "Results/card-quality/$stamp"
}
$ResultsRoot = [IO.Path]::GetFullPath($ResultsRoot)
New-Item -ItemType Directory -Path $ResultsRoot -Force | Out-Null

$failures = [Collections.Generic.List[string]]::new()
$evidence = [Collections.Generic.List[string]]::new()

function Invoke-DotnetStep {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string[]]$ModeArguments
    )

    $logPath = Join-Path $ResultsRoot ($Name + '.log')
    Push-Location $repoRoot
    try {
        & dotnet run --project Tools/Sim/Sim.csproj -c Release -- @ModeArguments 2>&1 |
            Tee-Object -FilePath $logPath
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    $evidence.Add("$Name exit=$exitCode log=$logPath")
    if ($exitCode -ne 0) { $failures.Add("$Name exited $exitCode") }
    return $exitCode
}

Invoke-DotnetStep -Name 'engine-gate' -ModeArguments @('gate') | Out-Null

if (-not $SkipAudits) {
    $effectReport = Join-Path $ResultsRoot 'effect-coverage.md'
    $conditionReport = Join-Path $ResultsRoot 'condition-coverage.md'
    Invoke-DotnetStep -Name 'effect-coverage' -ModeArguments @('effect-coverage-audit', $effectReport) | Out-Null
    Invoke-DotnetStep -Name 'condition-coverage' -ModeArguments @('condition-audit', $conditionReport) | Out-Null

    if (Test-Path -LiteralPath $effectReport) {
        $effectText = Get-Content -LiteralPath $effectReport -Raw
        $resolverMatch = [regex]::Match($effectText, 'RESOLVER GAPS[^:]*:\s*(\d+)', 'IgnoreCase')
        $triggerMatch = [regex]::Match($effectText, 'TRIGGER GAPS:\s*(\d+)', 'IgnoreCase')
        if (-not $resolverMatch.Success -or -not $triggerMatch.Success) {
            $failures.Add('effect coverage report could not be parsed')
        }
        else {
            $resolverGaps = [int]$resolverMatch.Groups[1].Value
            $triggerGaps = [int]$triggerMatch.Groups[1].Value
            $evidence.Add("effect coverage resolverGaps=$resolverGaps triggerGaps=$triggerGaps report=$effectReport")
            if ($resolverGaps -ne 0 -or $triggerGaps -ne 0) {
                $failures.Add("effect coverage has $resolverGaps resolver gap(s) and $triggerGaps trigger gap(s)")
            }
        }
    }

    if (Test-Path -LiteralPath $conditionReport) {
        $conditionText = Get-Content -LiteralPath $conditionReport -Raw
        $conditionMatch = [regex]::Match($conditionText, 'Unrecognized \(fail-closed\) conditions:\s*(\d+)', 'IgnoreCase')
        if (-not $conditionMatch.Success) {
            $failures.Add('condition coverage report could not be parsed')
        }
        else {
            $conditionGaps = [int]$conditionMatch.Groups[1].Value
            $evidence.Add("condition coverage unrecognized=$conditionGaps report=$conditionReport")
            if ($conditionGaps -ne 0) { $failures.Add("condition coverage has $conditionGaps unrecognized condition(s)") }
        }
    }
}

if (-not $SkipUnity) {
    if ([string]::IsNullOrWhiteSpace($UnityPath)) {
        $projectVersionPath = Join-Path $repoRoot 'ProjectSettings/ProjectVersion.txt'
        $versionLine = Get-Content -LiteralPath $projectVersionPath | Select-Object -First 1
        $editorVersion = ($versionLine -replace '^m_EditorVersion:\s*', '').Trim()
        $UnityPath = "C:\Program Files\Unity\Hub\Editor\$editorVersion\Editor\Unity.exe"
    }

    if (-not (Test-Path -LiteralPath $UnityPath)) {
        $failures.Add("Unity editor was not found at $UnityPath")
    }
    else {
        $testResults = Join-Path $ResultsRoot 'playmode-results.xml'
        $unityLog = Join-Path $ResultsRoot 'playmode.log'
        # Start-Process flattens string arrays before handing them to a native executable under
        # Windows PowerShell 5.1. Quote paths explicitly so a repository such as "One Piece TCG
        # Simulator" is not truncated to its first whitespace-delimited segment.
        $unityArgs = '-batchmode -projectPath "{0}" -runTests -testPlatform PlayMode -testResults "{1}" -logFile "{2}"' -f `
            $repoRoot, $testResults, $unityLog
        $unity = Start-Process -FilePath $UnityPath -ArgumentList $unityArgs -Wait -PassThru
        $evidence.Add("unity-playmode exit=$($unity.ExitCode) results=$testResults log=$unityLog")
        if ($unity.ExitCode -ne 0) { $failures.Add("Unity PlayMode runner exited $($unity.ExitCode)") }

        if (-not (Test-Path -LiteralPath $testResults)) {
            $failures.Add('Unity PlayMode runner did not produce test results')
        }
        else {
            [xml]$testXml = Get-Content -LiteralPath $testResults -Raw
            $run = $testXml.'test-run'
            if ($null -eq $run) {
                $failures.Add('Unity PlayMode result XML has no test-run root')
            }
            else {
                $evidence.Add("unity-playmode total=$($run.total) passed=$($run.passed) failed=$($run.failed) skipped=$($run.skipped)")
                if ([int]$run.failed -ne 0) { $failures.Add("Unity PlayMode has $($run.failed) failed test(s)") }
            }
        }
    }
}

$summaryPath = Join-Path $ResultsRoot 'summary.md'
$summary = [Text.StringBuilder]::new()
[void]$summary.AppendLine('# Card quality gate')
[void]$summary.AppendLine()
[void]$summary.AppendLine("- Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')")
[void]$summary.AppendLine("- Repository: $repoRoot")
[void]$summary.AppendLine("- Result: $(if ($failures.Count -eq 0) { 'PASS' } else { 'FAIL' })")
[void]$summary.AppendLine()
[void]$summary.AppendLine('## Evidence')
[void]$summary.AppendLine()
foreach ($line in $evidence) { [void]$summary.AppendLine("- $line") }
[void]$summary.AppendLine()
[void]$summary.AppendLine('## Failures')
[void]$summary.AppendLine()
if ($failures.Count -eq 0) { [void]$summary.AppendLine('- None') }
else { foreach ($line in $failures) { [void]$summary.AppendLine("- $line") } }
[void]$summary.AppendLine()
[void]$summary.AppendLine('This gate proves deterministic engine suites, static resolver/condition coverage, and Unity PlayMode behavior. It does not by itself prove a packaged Windows build or a two-client network session.')
[IO.File]::WriteAllText($summaryPath, $summary.ToString())
Write-Host "Card quality summary: $summaryPath"

if ($failures.Count -gt 0) { exit 1 }
exit 0
