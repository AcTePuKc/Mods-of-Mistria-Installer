<#
    Builds a single-file AIM executable for Windows, the same way the release workflow does.

    Usage (from a PowerShell window in the repository folder):

        ./build-windows-exe.ps1

    The result is Release/AIM.exe plus the README and license notices - a self-contained
    executable that needs no installed .NET runtime. Requires the .NET 10 SDK:
    https://dotnet.microsoft.com/download

    ImageSharp 4.x refuses to build without a Six Labors license key, so this script checks for one
    before starting rather than letting the build fail three steps later. Supply it either as
    ModsOfMistriaInstallerLib\sixlabors.lic (git-ignored) or through the SixLaborsLicenseKey
    environment variable. Never commit the file or the key: it is personal to the license holder.
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

# ── ImageSharp licensing ──────────────────────────────────────────────────────

$licenceFile = Join-Path $PSScriptRoot 'ModsOfMistriaInstallerLib/sixlabors.lic'
$hasLicence = (Test-Path $licenceFile) -or
              (-not [string]::IsNullOrWhiteSpace($env:SixLaborsLicenseKey)) -or
              (-not [string]::IsNullOrWhiteSpace($env:SixLaborsLicenseFile))

# Only ImageSharp 4.0.0 and later demand a key, so the pinned version decides whether the check
# below applies at all.
$libraryProject = Join-Path $PSScriptRoot 'ModsOfMistriaInstallerLib/ModsOfMistriaInstallerLib.csproj'
$pinnedImageSharp = [regex]::Match(
    (Get-Content -Raw -Path $libraryProject),
    'SixLabors\.ImageSharp"\s+Version="([^"]+)"').Groups[1].Value

$needsLicence = $false
if ($pinnedImageSharp -match '^(\d+)') { $needsLicence = [int]$Matches[1] -ge 4 }

if ($needsLicence -and -not $hasLicence) {
    Write-Host ''
    Write-Warning @'
This project builds against SixLabors.ImageSharp 4.x, which refuses to compile without a license
key. Supply one of the following:

  1. A license file at:
         ModsOfMistriaInstallerLib\sixlabors.lic
     That path is in .gitignore. Never commit it - keys are personal to the license holder.

  2. The key itself, for one session:
         $env:SixLaborsLicenseKey = Get-Content -Raw path\to\sixlabors.lic

Community licenses are free for open-source and non-commercial projects and can be requested at
https://licensing.sixlabors.com. They last a year, so an expired key produces this same error.
'@
    Write-Host ''
    throw 'No Six Labors license key found - see the options above.'
}

if ($needsLicence) {
    $source = if (Test-Path $licenceFile) { 'ModsOfMistriaInstallerLib\sixlabors.lic' } else { 'the environment' }
    Write-Host "Using the Six Labors license from $source for ImageSharp $pinnedImageSharp." -ForegroundColor DarkGray
}

# ── Build ─────────────────────────────────────────────────────────────────────

Write-Host 'Restoring packages...' -ForegroundColor Cyan
dotnet restore ModsOfMistriaInstaller.sln
if ($LASTEXITCODE -ne 0) { throw "Restore failed (exit code $LASTEXITCODE)." }

if (-not $SkipTests) {
    Write-Host 'Running tests...' -ForegroundColor Cyan
    dotnet test ModsOfMistriaInstaller.sln --configuration $Configuration
    if ($LASTEXITCODE -ne 0) { throw "Tests failed (exit code $LASTEXITCODE). Use -SkipTests to publish anyway." }
}

Write-Host "Publishing the GUI for $Runtime..." -ForegroundColor Cyan
dotnet publish ModsOfMistriaGUI/ModsOfMistriaGUI.csproj `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    --output $OutputDirectory
if ($LASTEXITCODE -ne 0) { throw "Publish failed (exit code $LASTEXITCODE)." }

# Keep the notices beside the single-file executable. The published binary embeds the MMAPI
# framework, so distributing the project and MMAPI license texts with the executable preserves
# the notices for users who download only the release artifact.
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination (Join-Path $OutputDirectory 'README.md') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENCE.txt') -Destination (Join-Path $OutputDirectory 'LICENCE.txt') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ModsOfMistriaInstallerLib/Seam/Payload/mmapi/LICENSE') -Destination (Join-Path $OutputDirectory 'MMAPI-LICENSE.txt') -Force
Write-Host "Copied README and license notices to $OutputDirectory." -ForegroundColor DarkGray

# The GUI project renames its assembly per runtime identifier (AIM, AIM-win-x86, AIM-linux,
# AIM-osx), so the produced file is whichever executable publish has just written.
$guiProject = Join-Path $PSScriptRoot 'ModsOfMistriaGUI/ModsOfMistriaGUI.csproj'
$guiProjectText = Get-Content -Raw -Path $guiProject

$assemblyNames = [regex]::Matches($guiProjectText, '<AssemblyName>([^<]+)</AssemblyName>') |
    ForEach-Object { $_.Groups[1].Value }

$executable = $assemblyNames |
    ForEach-Object { Join-Path $OutputDirectory "$_.exe" } |
    Where-Object { Test-Path $_ } |
    Sort-Object { (Get-Item $_).LastWriteTime } -Descending |
    Select-Object -First 1

if (-not $executable) {
    $executable = Get-ChildItem -Path $OutputDirectory -Filter *.exe -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}

if ($executable) {
    $size = [math]::Round((Get-Item $executable).Length / 1MB)
    Write-Host "Done: $((Resolve-Path $executable).Path) (${size} MB)" -ForegroundColor Green
    Write-Host 'Run it once. Nexus OAuth uses AIM''s public PKCE client registration; sign in from Gear menu -> Nexus downloads.'
} else {
    Write-Warning "Publish finished but no executable was found in $OutputDirectory. Check the output above."
}
