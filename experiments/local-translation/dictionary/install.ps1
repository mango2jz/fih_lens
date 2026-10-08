# Explicit local data installation; never called by application startup.
[CmdletBinding()]
param(
    [string]$SourceRoot,
    [string]$AssetRoot = (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'FihLens/local-assets')
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $PSBoundParameters.ContainsKey('SourceRoot')) {
    $SourceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../.omx/verification/local-dictionary-first/dictionary/converted'))
}

function Assert-LocalPath([string]$Path) {
    if ($Path -notmatch '^[A-Za-z]:[\\/]' -or $Path.Substring(2) -match '[:*?"<>|\x00-\x1F]' -or
        $Path -match '(^|[\\/])\.{1,2}([\\/]|$)' -or $Path -match '[. ]([\\/]|$)' -or
        $Path -match '(^|[\\/])(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.[^\\/]*)?([\\/]|$)') {
        throw 'Use a fully qualified local drive path without traversal or device syntax.'
    }
    $full = [IO.Path]::GetFullPath($Path)
    $drive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($full))
    if ($drive.DriveType -notin @([IO.DriveType]::Fixed, [IO.DriveType]::Removable, [IO.DriveType]::Ram) -or -not $drive.IsReady) {
        throw 'Asset paths must use an available local drive, never a mapped network drive.'
    }
    $ancestors = [Collections.Generic.Stack[string]]::new()
    $current = $full
    while ($current) {
        $ancestors.Push($current)
        $current = [IO.Path]::GetDirectoryName($current)
    }
    foreach ($current in $ancestors) {
        if (Test-Path -LiteralPath $current) {
            if (((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw 'Reparse points are not allowed in asset paths.'
            }
        }
    }
    return $full
}

$SourceRoot = Assert-LocalPath $SourceRoot
$AssetRoot = Assert-LocalPath $AssetRoot
$manifestPath = Assert-LocalPath (Join-Path $SourceRoot 'dictionary-manifest.json')
if ((Get-Item -LiteralPath $manifestPath).Length -gt 1MB) { throw 'Manifest exceeds 1 MiB.' }
$manifestBytes = [IO.File]::ReadAllBytes($manifestPath)
$manifest = [Text.Encoding]::UTF8.GetString($manifestBytes).TrimStart([char]0xFEFF) | ConvertFrom-Json
$sourceId = 'thichhoc-en-vi-v0.9.0'
$revision = 'ec3fa199aebdc4da4ebcaa7a11ac4b9c44046f75'
$corpusHash = 'f2726c2e435e262eb87b051d0c1cf159a309d3f4a25f77a9ca762136117a6b4c'
$reviewName = 'human-review-20261008.json'
$reviewHash = 'edf6fba01dc062ce3e20fe2b2595311d02623540f42d3ebe34b333bc7c4e6047'
$approvedHashes = @{
    'dictionary.jsonl' = $corpusHash
    'LICENSE-DATA' = '1a0af5e6a0da3bbab502e9a71269d30af2fa97a09721190233cd4a7d26b72430'
    'UPSTREAM-CODE-LICENSE' = '13669abc66e169b4513f1e62927e92e13ab5ea657647c47a13d4b9b1b17553c8'
    'UPSTREAM-README.md' = '96d5354f504676f04b95291dd7eb680f19658a513baab62351b0d085d0969625'
    'ATTRIBUTION.txt' = 'd8476eecb2703a0fe9424de2bed178b04619da4c59f061638d11aba1a6870605'
}
if ($manifest.schemaVersion -ne 1 -or $manifest.sourceId -cne $sourceId -or
    $manifest.revision -cne $revision -or $manifest.dictionaryFile -cne 'dictionary.jsonl' -or
    $manifest.dictionarySha256 -cne $corpusHash -or $manifest.dictionaryBytes -ne 32050247 -or
    $manifest.entryCount -ne 155139 -or $manifest.license -cne 'CC-BY-SA-4.0' -or
    $manifest.humanReviewFile -cne $reviewName -or
    $manifest.qualityStatus -cne 'sample-reviewed-user-report; generated Vietnamese senses' -or
    $manifest.querySampleSha256 -cne '63319ee978a572e12f16b498b09781e84237134b712df0761984b1214c7460ee') {
    throw 'Manifest does not describe the approved reviewed corpus.'
}
$files = @($manifest.files)
if ($files.Count -ne $approvedHashes.Count) { throw 'Manifest must retain exactly the approved corpus and four notices.' }
$seen = @{}
foreach ($file in $files) {
    $name = [string]$file.path
    if (-not $approvedHashes.ContainsKey($name) -or $seen.ContainsKey($name) -or
        $approvedHashes.Keys -cnotcontains $name -or
        $file.sha256 -cne $approvedHashes[$name] -or $file.bytes -le 0 -or $file.bytes -gt 500MB) {
        throw 'Manifest contains an unexpected name, duplicate, size or hash.'
    }
    $seen[$name] = $true
}
$reviewPath = Assert-LocalPath (Join-Path $SourceRoot $reviewName)
if ((Get-Item -LiteralPath $reviewPath).Length -gt 1MB -or
    (Get-FileHash -LiteralPath $reviewPath -Algorithm SHA256).Hash -ne $reviewHash) {
    throw 'Human review report differs from the approved report.'
}
$review = Get-Content -LiteralPath $reviewPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($review.sourceId -cne $sourceId -or $review.revision -cne $revision -or
    $review.corpusSha256 -cne $corpusHash -or $review.querySampleSha256 -cne $manifest.querySampleSha256 -or
    $review.reportedDate -cne '2026-10-08' -or $review.reviewedQueries -ne 100 -or
    $review.acceptableResults -ne 96 -or $review.missingResults -ne 4 -or
    $review.criticalMisleadingMeaningsReported -ne 0) { throw 'Human review is not bound to this corpus and sample.' }
$files += [pscustomobject]@{ path = $reviewName; sha256 = $reviewHash; bytes = (Get-Item -LiteralPath $reviewPath).Length }
$sha = [Security.Cryptography.SHA256]::Create()
try { $manifestHash = [BitConverter]::ToString($sha.ComputeHash($manifestBytes)).Replace('-', '').ToLowerInvariant() }
finally { $sha.Dispose() }
$allFiles = $files + [pscustomobject]@{ path = 'dictionary-manifest.json'; sha256 = $manifestHash; bytes = $manifestBytes.Length }
# Validate every source and destination before creating or copying anything.
foreach ($file in $allFiles) {
    $source = Assert-LocalPath (Join-Path $SourceRoot $file.path)
    $target = Assert-LocalPath (Join-Path $AssetRoot $file.path)
    $item = Get-Item -LiteralPath $source
    if ($item.PSIsContainer -or $item.Length -ne $file.bytes -or
        (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $file.sha256) {
        throw "Source size/hash mismatch: $($file.path)."
    }
    if (Test-Path -LiteralPath $target) {
        $existing = Get-Item -LiteralPath $target
        if ($existing.PSIsContainer -or $existing.Length -ne $file.bytes -or
            (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $file.sha256) {
            throw "Refusing to overwrite a differing asset: $($file.path). Inspect and move it explicitly first."
        }
    }
}
if ($manifestHash -cne $review.manifestSha256After) {
    throw 'Manifest bytes differ from the exact reviewed manifest.'
}
[IO.Directory]::CreateDirectory($AssetRoot) | Out-Null
foreach ($file in $files) {
    $source = Assert-LocalPath (Join-Path $SourceRoot $file.path)
    $target = Assert-LocalPath (Join-Path $AssetRoot $file.path)
    if (-not (Test-Path -LiteralPath $target)) { [IO.File]::Copy($source, $target, $false) }
    if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $file.sha256) {
        throw "Copied asset hash mismatch: $($file.path). Manifest has not been installed."
    }
}
# Publish the ready manifest last, only after all retained data matches.
$targetManifest = Assert-LocalPath (Join-Path $AssetRoot 'dictionary-manifest.json')
if (-not (Test-Path -LiteralPath $targetManifest)) {
    $stream = [IO.File]::Open($targetManifest, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $stream.Write($manifestBytes, 0, $manifestBytes.Length) }
    catch { $stream.Dispose(); [IO.File]::Delete($targetManifest); throw }
    finally { $stream.Dispose() }
}
Write-Output "Installed reviewed dictionary data and retained notices into $AssetRoot"
