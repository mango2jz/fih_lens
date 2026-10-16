param([switch]$Mixed, [switch]$Activation, [switch]$Content, [string]$EvidenceName = '')
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$evidenceRoot = Join-Path $repoRoot 'docs/handoffs/phase1-figma-native-ui/window-probe'
if (([int]$Mixed.IsPresent + [int]$Activation.IsPresent + [int]$Content.IsPresent) -gt 1) { throw 'Select one experiment mode.' }
if ($Mixed -or $Activation -or $Content) {
    if ($EvidenceName -notmatch '^[a-z0-9][a-z0-9-]{0,60}$') { throw 'Mixed run needs a simple unique evidence name.' }
    $evidenceRoot = Join-Path $evidenceRoot $EvidenceName
    if (Test-Path -LiteralPath $evidenceRoot) { throw 'Preserve existing mixed-run evidence; use a new evidence name.' }
}
New-Item -ItemType Directory -Path $evidenceRoot -Force | Out-Null
Push-Location $repoRoot
$previousOutput = $env:FIHLENS_WINDOW_PROBE_OUTPUT
$previousMixed = $env:FIHLENS_WINDOW_PROBE_MIXED
$probe = $null
try {
    dotnet build experiments/phase1-window-probe/Phase1WindowProbe.csproj --no-restore -p:Platform=x64 -v:minimal 2>&1 |
        Tee-Object -FilePath (Join-Path $evidenceRoot 'build.log')
    if ($LASTEXITCODE -ne 0) { throw 'Native probe build failed.' }
    $resourcePaths = @('FihLens.WinUI/Resources/Phase1Tokens.xaml', 'FihLens.WinUI/Resources/Phase1Styles.xaml')
    $resourceHashes = @($resourcePaths | ForEach-Object { [ordered]@{ path = $_; sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant() } })
    foreach ($name in @('result.json', 'cleanup.json', 'failure.txt', 'unhandled.txt', 'compact.png', 'reopened.png', 'expanded.png', 'second-session-after-close.png')) {
        $target = Join-Path $evidenceRoot $name
        if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target }
    }
    $env:FIHLENS_WINDOW_PROBE_OUTPUT = $evidenceRoot
    $env:FIHLENS_WINDOW_PROBE_MIXED = if ($Mixed) { '1' } elseif ($Activation) { 'activation' } elseif ($Content) { 'content' } else { $null }
    $executable = Join-Path $PSScriptRoot 'bin/x64/Debug/net10.0-windows10.0.19041.0/win-x64/Phase1WindowProbe.exe'
    $probe = Start-Process -FilePath $executable -WindowStyle Hidden -PassThru
    $runtimeLimit = if ($Activation) { 150000 } else { 20000 }
    if (-not $probe.WaitForExit($runtimeLimit)) { throw "Native probe did not exit in $runtimeLimit milliseconds." }
    if ($probe.ExitCode -ne 0) { throw "Native probe exit code: $($probe.ExitCode)" }
    foreach ($name in @('failure.txt', 'unhandled.txt')) {
        $failure = Join-Path $evidenceRoot $name
        if (Test-Path -LiteralPath $failure) { throw (Get-Content -LiteralPath $failure -Raw) }
    }
    $result = Get-Content -LiteralPath (Join-Path $evidenceRoot 'result.json') -Raw | ConvertFrom-Json
    if ($Content) {
        if (-not $result.success -or $result.fixtures.Count -ne 6 -or $result.createdWindows -ne 1) { throw 'Missing successful actual-content native evidence.' }
    } elseif (-not $result.success -or $result.windows.Count -lt 4 -or $result.createdWindows -ne 4 -or $result.closedTransientsBeforeLauncher -ne 3) { throw 'Missing successful native window lifetime evidence.' }
    $cleanup = Get-Content -LiteralPath (Join-Path $evidenceRoot 'cleanup.json') -Raw | ConvertFrom-Json
    $expectedWindows = if ($Content) { 1 } else { 4 }
    if (-not $cleanup.allClosed -or $cleanup.created -ne $expectedWindows -or $cleanup.closed -ne $expectedWindows) { throw 'Owned native windows did not all close.' }
    foreach ($resource in $resourceHashes) {
        if ((Get-FileHash -LiteralPath $resource.path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $resource.sha256) { throw 'Production resource changed during native probe.' }
    }
    $result | Add-Member -NotePropertyName resourceHashes -NotePropertyValue $resourceHashes
    $sourcePaths = @('experiments/phase1-window-probe/Phase1WindowProbe.csproj', 'experiments/phase1-window-probe/App.xaml', 'experiments/phase1-window-probe/App.xaml.cs', 'experiments/phase1-window-probe/MixedWindowProbe.cs', 'experiments/phase1-window-probe/ActivationWindowProbe.cs', 'experiments/phase1-window-probe/app.manifest', 'experiments/phase1-window-probe/run.ps1', 'experiments/phase1-window-probe/check-runner-cleanup.ps1')
    if ($Content) { $sourcePaths += @('experiments/phase1-window-probe/ContentWindowProbe.cs', 'FihLens.WinUI/Views/TranslationPopupContent.xaml', 'FihLens.WinUI/Views/TranslationPopupContent.xaml.cs', 'FihLens.WinUI/ViewModels/TranslationPopupViewModel.cs', 'FihLens.WinUI/DesignPreview/Phase1PreviewData.cs') }
    $sourceHashes = @($sourcePaths | ForEach-Object { [ordered]@{ path = $_; sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant() } })
    $result | Add-Member -NotePropertyName sourceHashes -NotePropertyValue $sourceHashes
    $result | Add-Member -NotePropertyName executableSha256 -NotePropertyValue (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash.ToLowerInvariant()
    $result | Add-Member -NotePropertyName assemblySha256 -NotePropertyValue (Get-FileHash -LiteralPath ([IO.Path]::ChangeExtension($executable, '.dll')) -Algorithm SHA256).Hash.ToLowerInvariant()
    $result | Add-Member -NotePropertyName dotnetSdk -NotePropertyValue (dotnet --version | Out-String).Trim()
    $resultJson = $result | ConvertTo-Json -Depth 8
    [IO.File]::WriteAllText((Join-Path $evidenceRoot 'result.json'), $resultJson, (New-Object Text.UTF8Encoding($false)))
    $resultJson
}
finally {
    try {
        if ($null -ne $probe) {
            try {
                if (-not $probe.HasExited) {
                    $probe.Kill()
                    if (-not $probe.WaitForExit(5000)) { throw 'Owned native probe did not terminate after cleanup.' }
                }
            }
            finally { $probe.Dispose() }
        }
    }
    finally {
        $env:FIHLENS_WINDOW_PROBE_OUTPUT = $previousOutput
        $env:FIHLENS_WINDOW_PROBE_MIXED = $previousMixed
        Pop-Location
    }
}
