param(
    [switch]$FailOnMissing,
    [switch]$FailOnEmDash,
    [switch]$FixEmDash
)

$ErrorActionPreference = 'Stop'
$languageDirectory = Join-Path $PSScriptRoot '..\ModsOfMistriaInstallerLib\Lang'
$englishPath = Join-Path $languageDirectory 'Resources.resx'

function Get-ResourceKeys([string] $path) {
    ([xml](Get-Content -LiteralPath $path)).root.data | ForEach-Object { $_.name }
}

$englishKeys = @(Get-ResourceKeys $englishPath)
$hasMissing = $false
$hasEmDash = $false

if ($FixEmDash) {
    foreach ($file in Get-ChildItem -LiteralPath $languageDirectory -Filter 'Resources*.resx' | Sort-Object Name) {
        $raw = Get-Content -LiteralPath $file.FullName -Raw
        if (-not $raw.Contains('—')) { continue }

        # Keep the existing XML formatting and line endings; this is deliberately a mechanical
        # punctuation cleanup rather than a resource reserialization.
        $raw = $raw.Replace('—', '-')
        Set-Content -LiteralPath $file.FullName -Value $raw -NoNewline -Encoding utf8
        Write-Output ("{0}: replaced em dashes" -f $file.Name)
    }
}

foreach ($file in Get-ChildItem -LiteralPath $languageDirectory -Filter 'Resources.*.resx' | Sort-Object Name) {
    $raw = Get-Content -LiteralPath $file.FullName -Raw
    $emDashCount = ([regex]::Matches($raw, '—')).Count
    if ($emDashCount -gt 0) {
        $hasEmDash = $true
        Write-Output ("{0}: em dashes={1}" -f $file.BaseName, $emDashCount)
    }

    $translatedKeys = @(Get-ResourceKeys $file.FullName)
    $missing = @($englishKeys | Where-Object { $_ -notin $translatedKeys })
    $extra = @($translatedKeys | Where-Object { $_ -notin $englishKeys })
    $hasMissing = $hasMissing -or $missing.Count -gt 0 -or $extra.Count -gt 0

    $status = if ($missing.Count -eq 0 -and $extra.Count -eq 0) { 'complete' } else { 'fallbacks pending' }
    Write-Output ("{0}: {1}; missing={2}; extra={3}" -f $file.BaseName, $status, $missing.Count, $extra.Count)

    if ($missing.Count -gt 0) {
        Write-Output ('  missing: ' + ($missing -join ', '))
    }
    if ($extra.Count -gt 0) {
        Write-Output ('  extra: ' + ($extra -join ', '))
    }
}

if ($FailOnMissing -and $hasMissing) {
    exit 1
}

if ($FailOnEmDash -and $hasEmDash) {
    exit 1
}
