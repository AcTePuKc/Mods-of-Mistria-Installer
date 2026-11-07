param(
    [ValidateSet('Low', 'Moderate', 'High', 'Critical')]
    [string] $MinimumSeverity = 'High'
)

$ErrorActionPreference = 'Stop'

$severityRank = @{
    Low = 1
    Moderate = 2
    High = 3
    Critical = 4
}

$solutionPath = Join-Path $PSScriptRoot '..\ModsOfMistriaInstaller.sln'
$report = (& dotnet list $solutionPath package --vulnerable --include-transitive --format json) -join [Environment]::NewLine
if ($LASTEXITCODE -ne 0) {
    throw 'dotnet list package failed; the dependency vulnerability audit could not complete.'
}

try {
    $projects = ((ConvertFrom-Json -InputObject $report).projects)
}
catch {
    throw "dotnet list package returned invalid JSON: $($_.Exception.Message)"
}

$minimumRank = $severityRank[$MinimumSeverity]
$findings = @()

foreach ($project in $projects) {
    foreach ($framework in $project.frameworks) {
        $packages = @($framework.topLevelPackages) + @($framework.transitivePackages)
        foreach ($package in $packages) {
            foreach ($vulnerability in @($package.vulnerabilities)) {
                if ($null -eq $vulnerability) { continue }

                $severity = [string]$vulnerability.severity
                if (-not $severityRank.ContainsKey($severity) -or $severityRank[$severity] -lt $minimumRank) {
                    continue
                }

                $findings += [pscustomobject]@{
                    Project = $project.path
                    Framework = $framework.framework
                    Package = $package.id
                    Version = $package.resolvedVersion
                    Severity = $severity
                    Advisory = $vulnerability.advisoryurl
                }
            }
        }
    }
}

if ($findings.Count -eq 0) {
    Write-Output "No resolved package vulnerabilities at or above $MinimumSeverity were found."
    exit 0
}

Write-Error "Resolved package vulnerabilities at or above $MinimumSeverity were found:"
$findings |
    Sort-Object Severity, Package, Advisory |
    Format-Table Project, Framework, Package, Version, Severity, Advisory -AutoSize |
    Out-String |
    Write-Error
exit 1
