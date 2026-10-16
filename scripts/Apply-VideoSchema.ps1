[CmdletBinding()]
param([switch]$InspectOnly, [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskAssembly = Join-Path $taskRoot "API/bin/$Configuration/net8.0/Npgsql.dll"
if (-not (Test-Path -LiteralPath $taskAssembly)) { throw "Build API in $Configuration before applying the schema." }
Add-Type -Path $taskAssembly
$taskConnection = [Environment]::GetEnvironmentVariable('ConnectionStrings__DefaultConnection')
if (-not $taskConnection) {
    $taskEnvFile = Join-Path $taskRoot '.env'
    if (Test-Path -LiteralPath $taskEnvFile) {
        $taskEntry = Get-Content -LiteralPath $taskEnvFile | Where-Object { $_ -match '^ConnectionStrings__DefaultConnection=' } | Select-Object -First 1
        if ($taskEntry) { $taskConnection = ($taskEntry -split '=', 2)[1].Trim().Trim('"').Trim("'") }
    }
}
if (-not $taskConnection) { throw 'Set ConnectionStrings__DefaultConnection in the environment or ignored .env.' }
$taskDatabase = [Npgsql.NpgsqlConnection]::new($taskConnection)
try {
    $taskDatabase.Open()
    $taskCommand = $taskDatabase.CreateCommand()
    $taskCommand.CommandTimeout = 120
    if ($InspectOnly) {
        $taskCommand.CommandText = "SELECT count(*) FROM information_schema.tables WHERE table_schema='public' AND table_name IN ('workflows','workflow_runs','workflow_node_runs','media_jobs')"
        Write-Output ('Video workflow tables: ' + $taskCommand.ExecuteScalar())
    } else {
        $taskCommand.CommandText = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'sql/video-workflows.sql')
        $null = $taskCommand.ExecuteNonQuery()
        Write-Output 'Video workflow schema applied. Run scripts/Scaffold-Database.ps1 next.'
    }
} catch {
    throw ('Video schema operation failed: ' + $_.Exception.GetType().Name)
} finally { $taskDatabase.Dispose() }
