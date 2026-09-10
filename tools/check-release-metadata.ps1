[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Join-Path $PSScriptRoot '..'
$propsPath = Join-Path $repositoryRoot 'Directory.Build.props'
$version = ([xml](Get-Content -LiteralPath $propsPath)).Project.PropertyGroup.Version

if ([string]::IsNullOrWhiteSpace($version)) {
    throw 'Directory.Build.props does not define a release version.'
}

$checks = @(
    @{ Path = 'README.md'; Pattern = "(?m)^# AIM .* $([regex]::Escape($version))$"; Description = 'README heading' },
    @{ Path = 'CHANGELOG.md'; Pattern = "(?m)^## $([regex]::Escape($version)) - "; Description = 'current changelog heading' },
    @{ Path = 'ROADMAP.md'; Pattern = "(?m)^## Current release: $([regex]::Escape($version))$"; Description = 'roadmap current release' },
    @{ Path = 'docs/NEXUS_DESCRIPTION.bbcode'; Pattern = "AIM $([regex]::Escape($version))"; Description = 'Nexus description' }
)

$failed = [System.Collections.Generic.List[string]]::new()
foreach ($check in $checks) {
    $path = Join-Path $repositoryRoot $check.Path
    $content = Get-Content -LiteralPath $path -Raw
    if ($content -notmatch $check.Pattern) {
        $failed.Add("$($check.Description) ($($check.Path))")
    }
}

if ($failed.Count -gt 0) {
    throw "Release metadata does not match Directory.Build.props version ${version}:`n- " + ($failed -join "`n- ")
}

Write-Host "Release metadata matches version $version."
