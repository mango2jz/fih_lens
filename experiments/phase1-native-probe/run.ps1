$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$evidenceRoot = Join-Path $repoRoot 'docs/handoffs/phase1-figma-native-ui/native-resource-probe'
New-Item -ItemType Directory -Path $evidenceRoot -Force | Out-Null
Push-Location $repoRoot
$previousOutput = $env:FIHLENS_PROBE_OUTPUT
$probe = $null
try {
    dotnet build experiments/phase1-native-probe/Phase1NativeProbe.csproj -p:Platform=x64 -v:minimal 2>&1 |
        Tee-Object -FilePath (Join-Path $evidenceRoot 'build.log')
    if ($LASTEXITCODE -ne 0) { throw 'Native probe build failed.' }
    $resourcePaths = @('FihLens.WinUI/Resources/Phase1Tokens.xaml', 'FihLens.WinUI/Resources/Phase1Styles.xaml')
    $resourceHashes = @($resourcePaths | ForEach-Object { [ordered]@{ path = $_; sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant() } })
    foreach ($name in @('result.json', 'failure.txt', 'unhandled.txt', 'light.png', 'dark.png', 'light-return.png')) {
        $target = Join-Path $evidenceRoot $name
        if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target }
    }
    $env:FIHLENS_PROBE_OUTPUT = $evidenceRoot
    $executable = Join-Path $PSScriptRoot 'bin/x64/Debug/net10.0-windows10.0.19041.0/win-x64/Phase1NativeProbe.exe'
    $probe = Start-Process -FilePath $executable -WindowStyle Hidden -PassThru
    if (-not $probe.WaitForExit(20000)) { throw 'Native probe did not exit in 20 seconds.' }
    if ($probe.ExitCode -ne 0) { throw "Native probe exit code: $($probe.ExitCode)" }
    foreach ($name in @('failure.txt', 'unhandled.txt')) {
        $failure = Join-Path $evidenceRoot $name
        if (Test-Path -LiteralPath $failure) { throw (Get-Content -LiteralPath $failure -Raw) }
    }
    $result = Get-Content -LiteralPath (Join-Path $evidenceRoot 'result.json') -Raw | ConvertFrom-Json
    if (-not $result.success -or $result.themes.Count -ne 3 -or ($result.themes.requestedTheme -join ',') -ne 'Light,Dark,Light') { throw 'Missing successful retained-control Light/Dark/Light native render evidence.' }
    foreach ($resource in $resourceHashes) {
        if ((Get-FileHash -LiteralPath $resource.path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $resource.sha256) { throw 'Production resource changed during native probe.' }
    }
    $result | Add-Member -NotePropertyName resourceHashes -NotePropertyValue $resourceHashes
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
        $env:FIHLENS_PROBE_OUTPUT = $previousOutput
        Pop-Location
    }
}
