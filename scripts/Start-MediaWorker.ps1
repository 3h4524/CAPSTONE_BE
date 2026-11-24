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
$queueMode = $settings["MediaQueue__Mode"]
if (-not [string]::IsNullOrWhiteSpace($queueMode)) { $env:MEDIA_QUEUE_MODE = $queueMode }
$queueUrl = $settings["MEDIA_QUEUE_REDIS_URL"]
if (-not [string]::IsNullOrWhiteSpace($queueUrl)) { $env:MEDIA_QUEUE_REDIS_URL = $queueUrl }
$queueStream = $settings["MediaQueue__Stream"]
if (-not [string]::IsNullOrWhiteSpace($queueStream)) { $env:MEDIA_QUEUE_STREAM = $queueStream }
if (-not [string]::IsNullOrWhiteSpace($aiSceneConcurrency)) {
    $env:AI_SCENE_CONCURRENCY = $aiSceneConcurrency
}
foreach ($name in "AI_SHOT_FIRST_POLL_SECONDS", "AI_SHOT_POLL_SECONDS", "AI_SHOT_TIMEOUT_MINUTES") {
    if (-not [string]::IsNullOrWhiteSpace($settings[$name])) { Set-Item -Path "Env:$name" -Value $settings[$name] }
}
# Remotion's own Chrome Headless Shell draws video frames far faster than a full Chrome (about 30 s against 47 s
# for the same 300 frames in a measured render), so it is used here, as Start-VideoDev.ps1 does.
$headlessShell = Join-Path $workerDir "node_modules\.remotion\chrome-headless-shell\win64\chrome-headless-shell-win64\chrome-headless-shell.exe"
if (-not (Test-Path -LiteralPath $headlessShell)) {
    throw "Chrome Headless Shell is missing: $headlessShell. Run 'npm run browser' in $workerDir first."
}
$env:CHROME_EXECUTABLE = $headlessShell
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
