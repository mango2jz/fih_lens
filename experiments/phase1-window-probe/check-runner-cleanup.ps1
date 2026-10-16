$ErrorActionPreference = 'Stop'
$tokens = $null
$errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'run.ps1'), [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw 'Runner has PowerShell parse errors.' }
$outerTry = $ast.EndBlock.Statements | Where-Object { $_ -is [Management.Automation.Language.TryStatementAst] } | Select-Object -First 1
# Execute the runner's actual finally body, not a copied cleanup implementation.
$finallyText = $outerTry.Finally.Extent.Text.Trim()
$cleanup = [scriptblock]::Create($finallyText.Substring(1, $finallyText.Length - 2))
$originalOutput = $env:FIHLENS_WINDOW_PROBE_OUTPUT
$originalMixed = $env:FIHLENS_WINDOW_PROBE_MIXED
$originalLocation = (Get-Location).Path
try {
    foreach ($scenario in @('timeout', 'already-exited', 'start-failed')) {
        $probe = $null
        $probeId = $null
        $previousOutput = 'owned-cleanup-sentinel'
        $previousMixed = 'owned-mixed-sentinel'
        $env:FIHLENS_WINDOW_PROBE_OUTPUT = 'temporary-probe-output'
        $env:FIHLENS_WINDOW_PROBE_MIXED = '1'
        Push-Location $PSScriptRoot
        try {
            if ($scenario -ne 'start-failed') {
                $childCommand = if ($scenario -eq 'timeout') { 'Start-Sleep -Seconds 30' } else { 'exit 0' }
                $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($childCommand))
                $probe = Start-Process -FilePath (Get-Process -Id $PID).Path -ArgumentList @('-NoProfile', '-EncodedCommand', $encoded) -WindowStyle Hidden -PassThru
                $probeId = $probe.Id
                if ($scenario -eq 'already-exited' -and -not $probe.WaitForExit(5000)) { throw 'Harmless child failed to exit.' }
            }
            try { throw "Expected fixture error: $scenario" }
            finally { . $cleanup }
        }
        catch {
            if ($_.Exception.Message -ne "Expected fixture error: $scenario") { throw }
        }
        if ($probeId -and (Get-Process -Id $probeId -ErrorAction SilentlyContinue)) { throw "Owned child still alive after $scenario cleanup." }
        if ($env:FIHLENS_WINDOW_PROBE_OUTPUT -ne $previousOutput) { throw "Environment not restored after $scenario." }
        if ($env:FIHLENS_WINDOW_PROBE_MIXED -ne $previousMixed) { throw "Mixed environment not restored after $scenario." }
        if ((Get-Location).Path -ne $originalLocation) { throw "Location not restored after $scenario." }
        Write-Output "$scenario cleanup passed"
    }
}
finally {
    $env:FIHLENS_WINDOW_PROBE_OUTPUT = $originalOutput
    $env:FIHLENS_WINDOW_PROBE_MIXED = $originalMixed
    Set-Location -LiteralPath $originalLocation
}
