<#
.SYNOPSIS
    Builds and publishes the Bitbound.Analyzers.MemberOrder NuGet package to a local cache.

.DESCRIPTION
    Packs the .Package project with version 99.0.0 and publishes the resulting .nupkg
    to $HOME/.cache/nuget_repo/ for local testing without publishing to NuGet.org.

    After running this script, add the local feed to your project with:
        dotnet nuget add source "$HOME/.cache/nuget_repo" --name "local-cache"

    Then reference the package normally:
        dotnet add package Bitbound.Analyzers.MemberOrder --version 99.0.0

.EXAMPLE
    pwsh .scripts/publish-local.ps1
#>

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

# Resolve paths
$repoRoot = Resolve-Path "$PSScriptRoot/.."
$packageProject = Join-Path $repoRoot "Bitbound.Analyzers.MemberOrder/Bitbound.Analyzers.MemberOrder.Package/Bitbound.Analyzers.MemberOrder.Package.csproj"
$outputDir = Join-Path $HOME ".cache/nuget_repo"
$version = "99.0.0"

# Ensure output directory exists
if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
    Write-Host "Created output directory: $outputDir"
}

Write-Host "=== Bitbound.Analyzers.MemberOrder Local Publish ===" -ForegroundColor Cyan
Write-Host "Repository: $repoRoot"
Write-Host "Version:     $version"
Write-Host "Output:      $outputDir"
Write-Host ""

# Clean any prior 99.0.0 packages for this ID so old versions don't accumulate
Get-ChildItem $outputDir -Filter "Bitbound.Analyzers.MemberOrder.$version.nupkg" -ErrorAction SilentlyContinue |
    ForEach-Object { Remove-Item $_.FullName -Force }

# Pack the project with the local version
Write-Host "Packing project..." -ForegroundColor Yellow
dotnet pack "$packageProject" `
    --configuration Release `
    --output "$outputDir" `
    /p:Version="$version" `
    /p:PackageVersion="$version" `
    /p:GeneratePackageOnBuild=false

if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet pack failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

Write-Host ""
Write-Host "✅ Published successfully!" -ForegroundColor Green
Write-Host ""
Write-Host "Package:" -ForegroundColor Cyan
Get-ChildItem $outputDir -Filter "*.nupkg" | ForEach-Object {
    Write-Host "  $($_.Name) ($([math]::Round($_.Length / 1KB)) KB)"
}
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "  1. Add the local NuGet source (one-time):"
Write-Host "     dotnet nuget add source ""$outputDir"" --name ""local-cache"""
Write-Host ""
Write-Host "  2. Add the package to your test project:"
Write-Host "     dotnet add package Bitbound.Analyzers.MemberOrder --version $version"
Write-Host ""
Write-Host "  3. Restore & build to see the analyzer in action:"
Write-Host "     dotnet build"
