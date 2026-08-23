<#
    Builds a single-file AIM executable for Windows, the same way the release workflow does.

    Usage (from a PowerShell window in the repository folder):

        ./build-windows-exe.ps1

    The result is Release/ModsOfMistriaInstaller.exe - a self-contained executable that needs no
    installed .NET runtime. Requires the .NET 10 SDK: https://dotnet.microsoft.com/download
#>

[CmdletBinding()]
param(
    [string] $Runtime = 'win-x64',
    [string] $Configuration = 'Release',
    [string] $OutputDirectory = 'Release',
    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'
Set-Location -Path $PSScriptRoot

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet was not found. Install the .NET 10 SDK from https://dotnet.microsoft.com/download and reopen PowerShell.'
}

Write-Host 'Restoring packages...' -ForegroundColor Cyan
dotnet restore ModsOfMistriaInstaller.sln

if (-not $SkipTests) {
    Write-Host 'Running tests...' -ForegroundColor Cyan
    dotnet test ModsOfMistriaInstaller.sln --configuration $Configuration
}

Write-Host "Publishing the GUI for $Runtime..." -ForegroundColor Cyan
dotnet publish ModsOfMistriaGUI/ModsOfMistriaGUI.csproj `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    --output $OutputDirectory

$executable = Join-Path $OutputDirectory 'ModsOfMistriaInstaller.exe'
if (Test-Path $executable) {
    Write-Host "Done: $((Resolve-Path $executable).Path)" -ForegroundColor Green
    Write-Host 'Run it once and use the gear menu -> Nexus downloads to register "Mod Manager Download" links.'
} else {
    Write-Warning "Publish finished but $executable was not found. Check the output above."
}
