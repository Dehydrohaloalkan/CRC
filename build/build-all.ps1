#Requires -Version 7
<#
.SYNOPSIS
  Build all CRC-32 projects and collect artefacts into ./dist/

.DESCRIPTION
  Runs tests, then publishes CLI (win-x64 + linux-x64), API, Desktop (win-x64 + linux-x64)
  and packs the NuGet package. All output lands in ./dist/.

.PARAMETER Configuration
  Build configuration: Release (default) or Debug.

.PARAMETER SkipTests
  Skip dotnet test step.

.EXAMPLE
  # From repo root:
  ./build/build-all.ps1

  ./build/build-all.ps1 -SkipTests
  ./build/build-all.ps1 -Configuration Debug
#>
param(
    [string]$Configuration = "Release",
    [switch]$SkipTests
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Locate repository root (one level above this script).
$Root  = Split-Path $PSScriptRoot -Parent
$Dist  = Join-Path $Root "dist"

function Step([string]$msg) { Write-Host "`n==> $msg" -ForegroundColor Cyan }
function Ok([string]$msg)   { Write-Host "    OK: $msg" -ForegroundColor Green }

# -----------------------------------------------------------------------
Step "Clean dist/"
if (Test-Path $Dist) { Remove-Item $Dist -Recurse -Force }
New-Item $Dist -ItemType Directory | Out-Null

# -----------------------------------------------------------------------
if (-not $SkipTests) {
    Step "Run tests"
    dotnet test "$Root/tests/Crc.Core.Tests/Crc.Core.Tests.csproj" -c $Configuration --nologo
    Ok "All tests passed"
}

# -----------------------------------------------------------------------
Step "Pack NuGet (Crc.Core)"
dotnet pack "$Root/src/Crc.Core/Crc.Core.csproj" -c $Configuration -o "$Dist/nuget" --nologo
Ok "NuGet → dist/nuget/"

# -----------------------------------------------------------------------
Step "Publish CLI (win-x64)"
dotnet publish "$Root/src/Crc.Cli/Crc.Cli.csproj" -c $Configuration `
    -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -o "$Dist/cli/win-x64" --nologo
Ok "CLI win-x64 → dist/cli/win-x64/"

Step "Publish CLI (linux-x64)"
dotnet publish "$Root/src/Crc.Cli/Crc.Cli.csproj" -c $Configuration `
    -r linux-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -o "$Dist/cli/linux-x64" --nologo
Ok "CLI linux-x64 → dist/cli/linux-x64/"

# -----------------------------------------------------------------------
Step "Publish API"
dotnet publish "$Root/src/Crc.Api/Crc.Api.csproj" -c $Configuration `
    -o "$Dist/api" --nologo
Ok "API → dist/api/"

# -----------------------------------------------------------------------
Step "Publish Desktop (win-x64)"
dotnet publish "$Root/src/Crc.Desktop/Crc.Desktop.csproj" -c $Configuration `
    -r win-x64 --self-contained true `
    -o "$Dist/desktop/win-x64" --nologo
Ok "Desktop win-x64 → dist/desktop/win-x64/"

Step "Publish Desktop (linux-x64)"
dotnet publish "$Root/src/Crc.Desktop/Crc.Desktop.csproj" -c $Configuration `
    -r linux-x64 --self-contained true `
    -o "$Dist/desktop/linux-x64" --nologo
Ok "Desktop linux-x64 → dist/desktop/linux-x64/"

# -----------------------------------------------------------------------
Step "Done"
Write-Host "`nArtefacts in: $Dist" -ForegroundColor Green
Get-ChildItem $Dist -Recurse -File | Select-Object FullName | Format-Table -HideTableHeaders
