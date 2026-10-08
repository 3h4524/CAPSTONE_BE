[CmdletBinding()]
param([int]$ApiPort = 5191, [int]$FrontendPort = 3000)

$ErrorActionPreference = 'Stop'
$videoBackendRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
# The frontend is either beside this repository or under a sibling Frontend folder; the first one found is used.
$videoFrontendCandidates = @('../CAPSTONE_FE', '../../Frontend/CAPSTONE_FE') | ForEach-Object { [IO.Path]::GetFullPath((Join-Path $videoBackendRoot $_)) }
$videoFrontendRoot = @($videoFrontendCandidates | Where-Object { Test-Path -LiteralPath $_ }) + $videoFrontendCandidates[-1] | Select-Object -First 1
$videoWorkerRoot = Join-Path $videoBackendRoot 'media-worker'
$videoOutputRoot = Join-Path $videoBackendRoot 'artifacts/video-dev'
$videoApiAssembly = Join-Path $videoBackendRoot 'API/bin/Release/net8.0/APCS.Api.dll'
$videoChrome = Join-Path $videoWorkerRoot 'node_modules/.remotion/chrome-headless-shell/win64/chrome-headless-shell-win64/chrome-headless-shell.exe'
$videoFfmpeg = Join-Path $videoWorkerRoot 'node_modules/ffmpeg-static/ffmpeg.exe'
$videoFfprobe = Join-Path $videoWorkerRoot 'node_modules/ffprobe-static/bin/win32/x64/ffprobe.exe'
$videoNode = (Get-Command node -ErrorAction Stop).Source
$videoDotnet = (Get-Command dotnet -ErrorAction Stop).Source
foreach ($videoFile in @($videoApiAssembly, $videoChrome, $videoFfmpeg, $videoFfprobe, (Join-Path $videoWorkerRoot 'node_modules/tsx/dist/cli.mjs'), (Join-Path $videoFrontendRoot 'node_modules/next/dist/bin/next'))) {
    if (-not (Test-Path -LiteralPath $videoFile)) { throw "Missing prerequisite: $videoFile. Build Release API, install frontend/worker dependencies and run npm run browser first." }
}
foreach ($videoPort in @($ApiPort, $FrontendPort)) {
    if ($videoPort -lt 1024 -or $videoPort -gt 65535) { throw 'Choose unprivileged valid ports.' }
    if (Get-NetTCPConnection -State Listen -LocalPort $videoPort -ErrorAction SilentlyContinue) { throw "Port $videoPort is occupied. Existing processes were not stopped." }
}
if ($ApiPort -eq $FrontendPort) { throw 'API and frontend must use different ports.' }
New-Item -ItemType Directory -Path $videoOutputRoot -Force | Out-Null
$videoKeyBytes = New-Object byte[] 32
$videoRandom = [Security.Cryptography.RandomNumberGenerator]::Create()
try { $videoRandom.GetBytes($videoKeyBytes) } finally { $videoRandom.Dispose() }
$videoWorkerKey = [BitConverter]::ToString($videoKeyBytes).Replace('-', '')
$videoPreviousEnvironment = @{}
$videoEnvironment = @{
    MediaWorker__ApiKey = $videoWorkerKey
    APCS_API_URL = "http://localhost:$ApiPort"
    CHROME_EXECUTABLE = $videoChrome
    FFMPEG_PATH = $videoFfmpeg
    FFPROBE_PATH = $videoFfprobe
    NEXT_PUBLIC_API_BASE_URL = "http://localhost:$ApiPort"
    ASPNETCORE_ENVIRONMENT = 'Development'
    'Logging__LogLevel__Microsoft.EntityFrameworkCore' = 'Warning'
}
$videoEnvironment.MEDIA_WORKER_API_KEY = $videoEnvironment.MediaWorker__ApiKey
$videoProcesses = [Collections.Generic.List[Diagnostics.Process]]::new()
try {
    foreach ($videoVariable in $videoEnvironment.Keys) {
        $videoPreviousEnvironment[$videoVariable] = [Environment]::GetEnvironmentVariable($videoVariable)
        [Environment]::SetEnvironmentVariable($videoVariable, $videoEnvironment[$videoVariable])
    }
    $videoApi = Start-Process -FilePath $videoDotnet -ArgumentList @('API/bin/Release/net8.0/APCS.Api.dll', '--urls', "http://localhost:$ApiPort", '--contentRoot', (Join-Path $videoBackendRoot 'API')) -WorkingDirectory $videoBackendRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $videoOutputRoot 'api.log') -RedirectStandardError (Join-Path $videoOutputRoot 'api-error.log')
    $videoProcesses.Add($videoApi)
    $videoHealthy = $false
    for ($videoAttempt = 0; $videoAttempt -lt 30; $videoAttempt++) {
        if ($videoApi.HasExited) { throw 'API stopped during startup. Inspect artifacts/video-dev/api-error.log.' }
        try { $videoHealthy = (Invoke-WebRequest "http://localhost:$ApiPort/health" -TimeoutSec 2).StatusCode -eq 200 } catch { }
        if ($videoHealthy) { break }
        Start-Sleep -Milliseconds 500
    }
    if (-not $videoHealthy) { throw 'API did not become healthy.' }
    $videoWorker = Start-Process -FilePath $videoNode -ArgumentList @('node_modules/tsx/dist/cli.mjs', 'src/worker.ts') -WorkingDirectory $videoWorkerRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $videoOutputRoot 'worker.log') -RedirectStandardError (Join-Path $videoOutputRoot 'worker-error.log')
    $videoProcesses.Add($videoWorker)
    [Environment]::SetEnvironmentVariable('MediaWorker__ApiKey', $videoPreviousEnvironment.MediaWorker__ApiKey)
    [Environment]::SetEnvironmentVariable('MEDIA_WORKER_API_KEY', $videoPreviousEnvironment.MEDIA_WORKER_API_KEY)
    $videoFrontend = Start-Process -FilePath $videoNode -ArgumentList @('node_modules/next/dist/bin/next', 'dev', '--port', "$FrontendPort") -WorkingDirectory $videoFrontendRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $videoOutputRoot 'frontend.log') -RedirectStandardError (Join-Path $videoOutputRoot 'frontend-error.log')
    $videoProcesses.Add($videoFrontend)
    [pscustomobject]@{ ApiPid = $videoApi.Id; WorkerPid = $videoWorker.Id; FrontendPid = $videoFrontend.Id; ApiUrl = "http://localhost:$ApiPort"; WorkflowUrl = "http://localhost:$FrontendPort/workflows"; Logs = $videoOutputRoot }
} catch {
    foreach ($videoProcess in $videoProcesses) { if (-not $videoProcess.HasExited) { Stop-Process -Id $videoProcess.Id -ErrorAction SilentlyContinue } }
    throw
} finally {
    foreach ($videoVariable in $videoPreviousEnvironment.Keys) { [Environment]::SetEnvironmentVariable($videoVariable, $videoPreviousEnvironment[$videoVariable]) }
}
