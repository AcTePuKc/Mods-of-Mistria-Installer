<#
    Builds a single-file AIM executable for Windows, the same way the release workflow does.

    Usage (from a PowerShell window in the repository folder):

        ./build-windows-exe.ps1

    The result is Release/ModsOfMistriaInstaller.exe - a self-contained executable that needs no
    installed .NET runtime. Requires the .NET 10 SDK: https://dotnet.microsoft.com/download

    ImageSharp 4.x refuses to build without a Six Labors license key, so this script checks for one
    first and explains the options rather than letting the build fail three steps later. CI does the
    same thing with the SIXLABORS_LICENSE secret (see .github/workflows/compile.yml).

        -UseImageSharp3   Build against ImageSharp 3.1.5, which needs no license key. Local builds
                          only: it writes a temporary Directory.Build.targets, removes it afterwards,
                          and does not change what the repository ships.
#>

[CmdletBinding()]
param(
    [string] $Runtime = 'win-x64',
    [string] $Configuration = 'Release',
    [string] $OutputDirectory = 'Release',
    [switch] $SkipTests,
    [switch] $UseImageSharp3
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

$overridePath = Join-Path $PSScriptRoot 'Directory.Build.targets'
$wroteOverride = $false

if ($UseImageSharp3) {
    if (Test-Path $overridePath) {
        throw "A Directory.Build.targets already exists at $overridePath. Remove or rename it before using -UseImageSharp3."
    }

    Write-Host 'Building against ImageSharp 3.1.5 (no license key needed). This is a local build only.' -ForegroundColor Yellow
    @'
<!-- Written by build-windows-exe.ps1 -UseImageSharp3 and deleted when it finishes.
     Imported after each project, so this Update overrides the version in the csproj. -->
<Project>
  <ItemGroup>
    <PackageReference Update="SixLabors.ImageSharp" Version="3.1.5" />
  </ItemGroup>
</Project>
'@ | Set-Content -Path $overridePath -Encoding UTF8
    $wroteOverride = $true
}
elseif (-not $hasLicence) {
    Write-Host ''
    Write-Warning @'
This fork builds against SixLabors.ImageSharp 4.x, which refuses to compile without a license key.
You have three options:

  1. Get your own key (free for open-source and non-commercial use) from
     https://licensing.sixlabors.com and save it to:
         ModsOfMistriaInstallerLib\sixlabors.lic
     That path is already in .gitignore, so it will never be committed.

  2. Set the key for one session instead:
         $env:SixLaborsLicenseKey = "<your key>"

  3. Build against ImageSharp 3.1.5, which needs no key, for a local test build:
         ./build-windows-exe.ps1 -UseImageSharp3
'@
    Write-Host ''
    throw 'No Six Labors license key found - see the options above.'
}

# ── Build ─────────────────────────────────────────────────────────────────────

try {
    Write-Host 'Restoring packages...' -ForegroundColor Cyan
    dotnet restore ModsOfMistriaInstaller.sln
    if ($LASTEXITCODE -ne 0) { throw "Restore failed (exit code $LASTEXITCODE)." }

    if (-not $SkipTests) {
        Write-Host 'Running tests...' -ForegroundColor Cyan
        dotnet test ModsOfMistriaInstaller.sln --configuration $Configuration
        if ($LASTEXITCODE -ne 0) { throw "Tests failed (exit code $LASTEXITCODE)." }
    }

    Write-Host "Publishing the GUI for $Runtime..." -ForegroundColor Cyan
    dotnet publish ModsOfMistriaGUI/ModsOfMistriaGUI.csproj `
        --configuration $Configuration `
        --runtime $Runtime `
        --self-contained true `
        -p:PublishSingleFile=true `
        --output $OutputDirectory
    if ($LASTEXITCODE -ne 0) { throw "Publish failed (exit code $LASTEXITCODE)." }
}
finally {
    if ($wroteOverride -and (Test-Path $overridePath)) { Remove-Item $overridePath -Force }
}

$executable = Join-Path $OutputDirectory 'ModsOfMistriaInstaller.exe'
if (Test-Path $executable) {
    Write-Host "Done: $((Resolve-Path $executable).Path)" -ForegroundColor Green
    Write-Host 'Run it once, then use the gear menu -> Nexus downloads to set your API key and register "Mod Manager Download" links.'
} else {
    Write-Warning "Publish finished but $executable was not found. Check the output above."
}
