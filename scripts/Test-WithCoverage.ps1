[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$artifactsParent = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot "artifacts"))
$coverageRoot = [System.IO.Path]::GetFullPath((Join-Path $artifactsParent "coverage"))

if (-not $coverageRoot.StartsWith($artifactsParent, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Coverage output must remain inside the repository artifacts directory."
}

if (Test-Path -LiteralPath $coverageRoot) {
    Remove-Item -LiteralPath $coverageRoot -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $coverageRoot | Out-Null
$rawResults = Join-Path $coverageRoot "raw"
$fullReport = Join-Path $coverageRoot "full"
$gateReport = Join-Path $coverageRoot "gate"

Push-Location $repositoryRoot
try {
    & dotnet tool restore
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to restore local .NET tools."
    }

    if (-not $NoBuild) {
        & dotnet build Capstone.sln -c $Configuration
        if ($LASTEXITCODE -ne 0) {
            throw "Solution build failed."
        }
    }

    & dotnet test Capstone.sln `
        -c $Configuration `
        --no-build `
        -- `
        --results-directory $rawResults `
        --coverage `
        --coverage-output-format cobertura `
        --report-trx
    if ($LASTEXITCODE -ne 0) {
        throw "Unit tests failed."
    }

    $reports = Join-Path $rawResults "*.cobertura.xml"
    $productionAssemblies = "+APCS.*;-APCS.*.UnitTests"

    & dotnet tool run reportgenerator `
        "-reports:$reports" `
        "-targetdir:$fullReport" `
        "-reporttypes:Html;Cobertura" `
        "-assemblyfilters:$productionAssemblies"
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to generate the full coverage report."
    }

    $behaviouralClasses = @(
        "+APCS.Api.Controllers.*",
        "+APCS.Api.Extensions.ResultExtensions",
        "+APCS.Api.Middleware.*",
        "+APCS.Application.Common.Behaviours.*",
        "+APCS.Application.Features.Auth.*",
        "+APCS.Common.*",
        "+APCS.Domain.Common.SoftDeletableEntity",
        "+APCS.Domain.Entities.AuthToken",
        "+APCS.Domain.Entities.User",
        "+APCS.Domain.ValueObjects.Email",
        "+APCS.Infrastructure.Services.*"
    ) -join ";"

    & dotnet tool run reportgenerator `
        "-reports:$reports" `
        "-targetdir:$gateReport" `
        "-reporttypes:Html;Cobertura;MarkdownSummaryGithub" `
        "-assemblyfilters:$productionAssemblies" `
        "-classfilters:$behaviouralClasses" `
        "minimumCoverageThresholds:lineCoverage=80" `
        "minimumCoverageThresholds:branchCoverage=70"
    if ($LASTEXITCODE -ne 0) {
        throw "Coverage is below the required 80% line / 70% branch thresholds."
    }
}
finally {
    Pop-Location
}
