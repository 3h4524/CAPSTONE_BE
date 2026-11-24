<#
.SYNOPSIS
  Copies the video composition of the media worker into the frontend, so the browser plays a video with the code that renders it.

.DESCRIPTION
  The source is in this repository:
    - shared/video-composition/src/index.ts        the pure helpers (motion, transitions, crop geometry)
    - media-worker/src/video-composition.tsx       the composition (Showcase, ExportScene, ...)
  The frontend keeps identical copies:
    - packages/video-composition/src/index.ts
    - src/components/workflows/media/video-composition.tsx
  Both import the helpers from "@apcs/video-composition". After the helpers change, run `pnpm install` in the frontend: it takes
  that package from a file: dependency, which pnpm copies.

.PARAMETER FrontendRoot
  The frontend repository (default: ../../Frontend/CAPSTONE_FE next to this repository).

.PARAMETER Check
  Change nothing; exit with 1 when the frontend copies differ from the source.
#>
[CmdletBinding()]
param(
    [string]$FrontendRoot,
    [switch]$Check
)

$ErrorActionPreference = "Stop"

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
if (-not $FrontendRoot) { $FrontendRoot = Join-Path $repositoryRoot "..\..\Frontend\CAPSTONE_FE" }
$frontend = (Resolve-Path -LiteralPath $FrontendRoot).Path
$package = Join-Path $frontend "packages\video-composition\src"
$mediaFolder = Join-Path $frontend "src\components\workflows\media"
foreach ($folder in @($package, $mediaFolder)) {
    if (-not (Test-Path -LiteralPath $folder)) { throw "The frontend folder was not found: $folder" }
}

# Lines end the same way on every machine, so the copies are compared on what they say.
function Read-Normalized([string]$path) { (Get-Content -LiteralPath $path -Raw) -replace "`r`n", "`n" }

$copies = @(
    @{ Name = "packages/video-composition/src/index.ts"; Target = (Join-Path $package "index.ts")
       Content = (Read-Normalized (Join-Path $repositoryRoot "shared\video-composition\src\index.ts")) },
    @{ Name = "src/components/workflows/media/video-composition.tsx"; Target = (Join-Path $mediaFolder "video-composition.tsx")
       Content = (Read-Normalized (Join-Path $repositoryRoot "media-worker\src\video-composition.tsx")) }
)

$different = @()
foreach ($copy in $copies) {
    $current = if (Test-Path -LiteralPath $copy.Target) { Read-Normalized $copy.Target } else { $null }
    if ($current -ne $copy.Content) {
        $different += $copy.Name
        if (-not $Check) { [System.IO.File]::WriteAllText($copy.Target, $copy.Content, (New-Object System.Text.UTF8Encoding($false))) }
    }
}

if ($Check) {
    if ($different.Count -gt 0) {
        Write-Host "The frontend video composition is out of date: $($different -join ', '). Run ./scripts/Sync-VideoComposition.ps1"
        exit 1
    }
    Write-Host "The frontend video composition matches its source."
    exit 0
}

if ($different.Count -eq 0) { Write-Host "Already in sync." }
else { Write-Host "Copied: $($different -join ', '). If the helpers changed, run 'pnpm install' in the frontend so it takes the package." }
