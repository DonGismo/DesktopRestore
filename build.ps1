<#
.SYNOPSIS
  Testet, veröffentlicht (self-contained, eine EXE) und baut das MSI.
.EXAMPLE
  .\build.ps1 -Version 1.0.1
#>
param(
    [string]$Version = "1.0.0",
    [string]$Configuration = "Release",
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$artifacts = Join-Path $root 'artifacts'
$publishDir = Join-Path $artifacts 'publish'

function Invoke-Dotnet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($args[0]) fehlgeschlagen (Exitcode $LASTEXITCODE)" }
}

if (-not $SkipTests) {
    Invoke-Dotnet test (Join-Path $root 'DesktopRestore.sln') -c $Configuration --nologo
}

if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
Invoke-Dotnet publish (Join-Path $root 'src\DesktopRestore.App\DesktopRestore.App.csproj') `
    -c $Configuration -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none `
    "-p:Version=$Version" -o $publishDir --nologo

# Argumente mit Variablen in Anfuehrungszeichen: PowerShell 7 zerlegt -p:Name=$wert sonst in zwei Argumente
Invoke-Dotnet build (Join-Path $root 'installer\DesktopRestore.Installer\DesktopRestore.Installer.wixproj') `
    -c $Configuration "-p:Version=$Version" "-p:AppPublishDir=$publishDir" -o $artifacts --nologo

$msi = Join-Path $artifacts "DesktopRestore-$Version.msi"
Write-Host "MSI erstellt: $msi" -ForegroundColor Green
