[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[a-z][a-z0-9_]*$')]
    [string]$Name
)

$ErrorActionPreference = "Stop"

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$migrationsRoot = Join-Path $repositoryRoot "migrations"

$version = [DateTime]::UtcNow.AddHours(7).ToString("yyyyMMddHHmm")
$fileName = "V${version}__${Name}.sql"
$path = Join-Path $migrationsRoot $fileName

if (Test-Path $path) { throw "$fileName already exists; wait a minute or choose another name." }

New-Item -ItemType File -Path $path | Out-Null

Write-Host "Created migrations/$fileName"
Write-Host "Next: write the SQL, open a pull request (CI replays every migration on an empty database),"
Write-Host "and after it reaches the integration branch run ./scripts/Scaffold-Database.ps1 against the dev database."
