$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$buildRoot = Join-Path $projectRoot 'bin/x64/Debug/net10.0-windows10.0.19041.0/win-x64'
$layoutRoot = [IO.Path]::GetFullPath((Join-Path $buildRoot 'AppX'))
if (-not $layoutRoot.StartsWith($projectRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Layout must remain inside this experiment.'
}
[xml]$recipe = Get-Content -LiteralPath (Join-Path $buildRoot 'Phase1SelectionProbe.build.appxrecipe') -Raw
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

