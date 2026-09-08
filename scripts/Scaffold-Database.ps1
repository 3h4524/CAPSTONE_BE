[CmdletBinding()]
param(
    [string]$ConnectionName = "ConnectionStrings:DefaultConnection"
)

$ErrorActionPreference = "Stop"

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$entitiesTarget = Join-Path $repositoryRoot "Domain\Entities\Generated"
$contextTarget = Join-Path $repositoryRoot "Infrastructure\Persistence\Generated"
$stagingRoot = Join-Path $repositoryRoot ("artifacts\scaffold\" + [Guid]::NewGuid().ToString("N"))
$stagingEntities = Join-Path $stagingRoot "Entities"
$stagingContext = Join-Path $stagingRoot "Context"

function Assert-RepositoryChild([string]$Path) {
    $fullPath = [IO.Path]::GetFullPath($Path)
    $rootPrefix = $repositoryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar

    if (-not $fullPath.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify a path outside the repository: $fullPath"
    }
}

Assert-RepositoryChild $entitiesTarget
Assert-RepositoryChild $contextTarget
Assert-RepositoryChild $stagingRoot

New-Item -ItemType Directory -Force -Path $stagingEntities, $stagingContext | Out-Null

Push-Location $repositoryRoot
try {
    & dotnet ef dbcontext scaffold "Name=$ConnectionName" Npgsql.EntityFrameworkCore.PostgreSQL `
        --project Infrastructure `
        --startup-project API `
        --output-dir "..\artifacts\scaffold\$([IO.Path]::GetFileName($stagingRoot))\Entities" `
        --context-dir "..\artifacts\scaffold\$([IO.Path]::GetFileName($stagingRoot))\Context" `
        --context AppDbContext `
        --namespace APCS.Domain.Entities `
        --context-namespace APCS.Infrastructure.Persistence `
        --no-onconfiguring `
        --schema public `
        --force `
        --no-color

    if ($LASTEXITCODE -ne 0) {
        throw "EF Core database scaffolding failed with exit code $LASTEXITCODE."
    }

    $generatedEntities = @(Get-ChildItem -LiteralPath $stagingEntities -Filter "*.cs" -File)
    $generatedContext = Join-Path $stagingContext "AppDbContext.cs"
    if ($generatedEntities.Count -eq 0 -or -not (Test-Path -LiteralPath $generatedContext)) {
        throw "EF Core scaffolding produced an incomplete database model; existing generated files were preserved."
    }

    if (Test-Path -LiteralPath $entitiesTarget) {
        Remove-Item -LiteralPath $entitiesTarget -Recurse -Force
    }

    if (Test-Path -LiteralPath $contextTarget) {
        Remove-Item -LiteralPath $contextTarget -Recurse -Force
    }

    Move-Item -LiteralPath $stagingEntities -Destination $entitiesTarget
    Move-Item -LiteralPath $stagingContext -Destination $contextTarget
}
finally {
    Pop-Location

    if (Test-Path -LiteralPath $stagingRoot) {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
}
