[CmdletBinding()]
param(
    [ValidateSet("migrate", "info", "validate", "repair")]
    [string]$Command = "migrate",
    [string]$EnvFile = ".env",
    [string]$ConnectionVariable = "ConnectionStrings__DefaultConnection",
    [switch]$Scaffold
)

$ErrorActionPreference = "Stop"

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$migrationRole = "apcs_migrator"
$flywayImage = if ($env:FLYWAY_IMAGE) { $env:FLYWAY_IMAGE } else { "flyway/flyway:11" }

function Get-ConnectionString {
    $fromEnvironment = [Environment]::GetEnvironmentVariable($ConnectionVariable)
    if ($fromEnvironment) { return $fromEnvironment }

    $envPath = Join-Path $repositoryRoot $EnvFile
    if (-not (Test-Path -LiteralPath $envPath)) {
        throw "No $ConnectionVariable in the environment and no $EnvFile at the repository root."
    }

    $line = Get-Content -LiteralPath $envPath |
        Where-Object { $_ -match "^\s*$([regex]::Escape($ConnectionVariable))\s*=" } |
        Select-Object -Last 1
    if (-not $line) { throw "$ConnectionVariable is not set in $EnvFile." }

    return ($line -replace "^\s*$([regex]::Escape($ConnectionVariable))\s*=\s*", "").Trim().Trim('"', "'")
}

function ConvertFrom-NpgsqlConnectionString([string]$ConnectionString) {
    $settings = @{}
    foreach ($part in $ConnectionString -split ";") {
        $pair = $part -split "=", 2
        if ($pair.Count -eq 2 -and $pair[0].Trim()) {
            $settings[($pair[0].Trim() -replace "\s", "").ToLowerInvariant()] = $pair[1].Trim()
        }
    }

    $hostName = @($settings["host"], $settings["server"]) | Where-Object { $_ } | Select-Object -First 1
    $userName = @($settings["username"], $settings["userid"], $settings["user"]) | Where-Object { $_ } | Select-Object -First 1
    $port = if ($settings["port"]) { $settings["port"] } else { "5432" }
    $sslMode = switch (($settings["sslmode"] + "").ToLowerInvariant()) {
        "" { "require" }
        "verifyca" { "verify-ca" }
        "verifyfull" { "verify-full" }
        default { $_ }
    }

    if (-not $hostName -or -not $settings["database"] -or -not $userName -or -not $settings["password"]) {
        throw "$ConnectionVariable must contain Host, Database, Username, and Password."
    }

    return [pscustomobject]@{
        Url      = "jdbc:postgresql://${hostName}:${port}/$($settings["database"])?sslmode=$sslMode"
        User     = $userName
        Password = $settings["password"]
    }
}

$connection = ConvertFrom-NpgsqlConnectionString (Get-ConnectionString)
Write-Host "Flyway $Command on $($connection.Url) as $($connection.User) (SET ROLE $migrationRole)"

$env:FLYWAY_URL = $connection.Url
$env:FLYWAY_USER = $connection.User
$env:FLYWAY_PASSWORD = $connection.Password
$env:FLYWAY_INIT_SQL = "SET ROLE $migrationRole"
$env:FLYWAY_INSTALLED_BY = $connection.User
$env:FLYWAY_IGNORE_MIGRATION_PATTERNS = "*:missing,*:future"

try {
    & docker run --rm `
        -e FLYWAY_URL -e FLYWAY_USER -e FLYWAY_PASSWORD `
        -e FLYWAY_INIT_SQL -e FLYWAY_INSTALLED_BY -e FLYWAY_IGNORE_MIGRATION_PATTERNS `
        -v "$(Join-Path $repositoryRoot "migrations"):/flyway/sql:ro" `
        -v "$(Join-Path $repositoryRoot "deploy/flyway/flyway.toml"):/flyway/conf/flyway.toml:ro" `
        $flywayImage $Command

    if ($LASTEXITCODE -ne 0) { throw "Flyway $Command failed with exit code $LASTEXITCODE." }
}
finally {
    foreach ($name in "FLYWAY_URL", "FLYWAY_USER", "FLYWAY_PASSWORD", "FLYWAY_INIT_SQL", "FLYWAY_INSTALLED_BY", "FLYWAY_IGNORE_MIGRATION_PATTERNS") {
        Remove-Item "Env:$name" -ErrorAction SilentlyContinue
    }
}

if ($Scaffold -and $Command -eq "migrate") {
    & (Join-Path $PSScriptRoot "Scaffold-Database.ps1")
}
