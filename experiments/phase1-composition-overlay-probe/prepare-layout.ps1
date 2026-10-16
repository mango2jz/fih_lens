$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$buildRoot = Join-Path $projectRoot 'bin/x64/Debug/net10.0-windows10.0.19041.0/win-x64'
$layoutRoot = [IO.Path]::GetFullPath((Join-Path $buildRoot 'AppX'))
if (-not $layoutRoot.StartsWith($projectRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Layout must remain inside this experiment.'
}
[xml]$recipe = Get-Content -LiteralPath (Join-Path $buildRoot 'Phase1CompositionOverlayProbe.build.appxrecipe') -Raw
$namespace = [Xml.XmlNamespaceManager]::new($recipe.NameTable)
$namespace.AddNamespace('m', 'http://schemas.microsoft.com/developer/msbuild/2003')
$files = $recipe.SelectNodes('//m:AppXManifest | //m:AppxPackagedFile', $namespace)
if ($files.Count -eq 0) { throw 'Build recipe contains no files.' }
foreach ($file in $files) {
    $destination = [IO.Path]::GetFullPath((Join-Path $layoutRoot $file.PackagePath))
    if (-not $destination.StartsWith($layoutRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Package file escapes layout.'
    }
    $null = New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($destination))
    Copy-Item -LiteralPath $file.Include -Destination $destination -Force
}
Write-Output "Prepared $($files.Count) package files: $layoutRoot"
Write-Output 'No package registered and no process launched.'
$handoffRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot '../../docs/handoffs/phase1-figma-native-ui/composition-overlay-probe'))
$sources = @(Get-ChildItem -LiteralPath $projectRoot -File | Where-Object { $_.Extension -in '.cs', '.csproj', '.xaml', '.manifest', '.appxmanifest', '.ps1', '.md' })
$sources += @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'checks') -File | Where-Object { $_.Extension -in '.cs', '.csproj' })
$hashes = @($sources | ForEach-Object { [pscustomobject]@{ Kind = 'source'; Path = $_.FullName; SHA256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } })
$hashes += @($files | ForEach-Object {
    $destination = [IO.Path]::GetFullPath((Join-Path $layoutRoot $_.PackagePath))
    [pscustomobject]@{ Kind = 'layout'; Path = $destination; SHA256 = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash }
})
$hashes += [pscustomobject]@{ Kind = 'recipe'; Path = (Join-Path $buildRoot 'Phase1CompositionOverlayProbe.build.appxrecipe'); SHA256 = (Get-FileHash -LiteralPath (Join-Path $buildRoot 'Phase1CompositionOverlayProbe.build.appxrecipe') -Algorithm SHA256).Hash }
$null = New-Item -ItemType Directory -Force -Path $handoffRoot
$hashes | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $handoffRoot 'ready-file-hashes.json')
Write-Output "Source, recipe and all layout SHA256 values: $handoffRoot/ready-file-hashes.json"


