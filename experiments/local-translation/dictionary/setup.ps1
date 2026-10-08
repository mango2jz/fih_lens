# Explicit experiment asset setup; never called by the application.
$ErrorActionPreference = 'Stop'
$workspace = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$evidence = Join-Path $workspace '.omx/verification/local-dictionary-first/dictionary'
$assets = Join-Path $evidence 'assets'
New-Item -ItemType Directory -Force $assets | Out-Null
$downloads = @(
    @{
        Name = 'thichhoc-source.zip'
        Url = 'https://codeload.github.com/thichhoc-org/thichhoc-dict/zip/ec3fa199aebdc4da4ebcaa7a11ac4b9c44046f75'
        Sha256 = 'f65cab4dad91ac4bcc8cdd588dabb7d718eb9e50588d3ae221e468e433b6eaf1'
    },
    @{
        Name = 'ATTRIBUTION.txt'
        Url = 'https://github.com/thichhoc-org/thichhoc-dict/releases/download/v0.9.0/ATTRIBUTION.txt'
        Sha256 = 'd8476eecb2703a0fe9424de2bed178b04619da4c59f061638d11aba1a6870605'
    }
)
foreach ($download in $downloads) {
    $destination = Join-Path $assets $download.Name
    if (-not (Test-Path -LiteralPath $destination)) {
        Invoke-WebRequest -Uri $download.Url -OutFile $destination -TimeoutSec 180
    }
    if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $download.Sha256) {
        throw "Asset hash mismatch: $($download.Name). Inspect it before explicitly replacing it."
    }
}
& python (Join-Path $PSScriptRoot 'import_thichhoc.py') --self-check
if ($LASTEXITCODE -ne 0) { throw 'Dictionary parser self-check failed.' }
& python (Join-Path $PSScriptRoot 'import_thichhoc.py') --archive (Join-Path $assets 'thichhoc-source.zip') --attribution (Join-Path $assets 'ATTRIBUTION.txt') --output (Join-Path $evidence 'converted')
if ($LASTEXITCODE -ne 0) { throw 'Dictionary conversion failed.' }
