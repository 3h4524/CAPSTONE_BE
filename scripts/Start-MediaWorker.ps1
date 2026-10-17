$ErrorActionPreference = "Stop"

$backendDir = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$workerDir = Join-Path $backendDir "media-worker"
$envFile = Join-Path $backendDir ".env"

$settings = @{}

foreach ($line in Get-Content -LiteralPath $envFile) {
    if ($line -match '^\s*([^#=]+)=(.*)$') {
        $settings[$matches[1].Trim()] = $matches[2].Trim().Trim('"').Trim("'")
    }
}

$workerKey = $settings["MediaWorker__ApiKey"]
$aiSceneConcurrency = $settings["AI_SCENE_CONCURRENCY"]

if ([string]::IsNullOrWhiteSpace($workerKey)) {
    throw "MediaWorker__ApiKey is missing from $envFile"
}

$env:APCS_API_URL = "http://localhost:5191"
$env:MEDIA_WORKER_API_KEY = $workerKey
if (-not [string]::IsNullOrWhiteSpace($aiSceneConcurrency)) {
    $env:AI_SCENE_CONCURRENCY = $aiSceneConcurrency
}
$env:CHROME_EXECUTABLE = "C:\Program Files\Google\Chrome\Application\chrome.exe"
$env:FFMPEG_PATH = (
    Resolve-Path -LiteralPath (
        Join-Path $workerDir "node_modules\ffmpeg-static\ffmpeg.exe"
    )
).Path
$env:FFPROBE_PATH = (
    Resolve-Path -LiteralPath (
        Join-Path $workerDir "node_modules\ffprobe-static\bin\win32\x64\ffprobe.exe"
    )
).Path

Write-Host "Starting APCS media worker..."
Write-Host "API: $env:APCS_API_URL"
Write-Host "FFmpeg: $env:FFMPEG_PATH"
Write-Host "FFprobe: $env:FFPROBE_PATH"

Push-Location $workerDir

try {
    & npm.cmd start

    if ($LASTEXITCODE -ne 0) {
        throw "Media worker exited with code $LASTEXITCODE"
    }
}
finally {
    Pop-Location
}
