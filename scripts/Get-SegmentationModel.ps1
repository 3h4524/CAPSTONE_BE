[CmdletBinding()]
param(
    [switch]$Force
)

# Downloads the BiRefNet (lite) model that cuts the product out of mock-up base photos
# (Mockups:SegmentationModelPath). The file is ~180 MB, so it is not kept in the repository. The API
# downloads it by itself on startup when it is missing; this script does the same by hand (for a
# machine without internet access at startup, or to fetch it again with -Force).

$ErrorActionPreference = "Stop"

$url = "https://huggingface.co/senty-au/BiRefNet_lite-ONNX-dynamic/resolve/main/onnx/model.onnx"
$expectedSha256 = "1e0da42f0fde010e32e938bad388457ecefe35806fde9d923421997861ae9391"

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$modelsFolder = Join-Path $repositoryRoot "models"
$target = Join-Path $modelsFolder "birefnet-lite-cpu.onnx"

if ((Test-Path $target) -and -not $Force) {
    Write-Host "Model already present: $target (use -Force to download again)."
    return
}

New-Item -ItemType Directory -Force -Path $modelsFolder | Out-Null
$temporary = "$target.download"

Write-Host "Downloading $url ..."
Invoke-WebRequest -Uri $url -OutFile $temporary -UseBasicParsing

$actual = (Get-FileHash -Path $temporary -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actual -ne $expectedSha256) {
    Remove-Item -Path $temporary -Force
    throw "Downloaded file has an unexpected checksum ($actual). Nothing was installed."
}

Move-Item -Path $temporary -Destination $target -Force
Write-Host "Saved $target"
