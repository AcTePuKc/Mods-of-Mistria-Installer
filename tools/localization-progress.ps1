param(
    [string] $Language,
    [int] $BatchSize = 24,
    [switch] $AllMissing
)

$ErrorActionPreference = 'Stop'

$languageDirectory = Join-Path $PSScriptRoot '..\ModsOfMistriaInstallerLib\Lang'
$englishPath = Join-Path $languageDirectory 'Resources.resx'
$statePath = Join-Path $PSScriptRoot 'localization-progress.json'

function Get-ResourceKeys([string] $path) {
    @(([xml](Get-Content -LiteralPath $path)).root.data | ForEach-Object { $_.name })
}

function Test-CoreKey([string] $key) {
    return $key.StartsWith('GUI') -and
        -not $key.StartsWith('GUIResearch') -and
        -not $key.StartsWith('GUICrash')
}

$englishKeys = @(Get-ResourceKeys $englishPath | Where-Object { Test-CoreKey $_ })
$localeFiles = @(Get-ChildItem -LiteralPath $languageDirectory -Filter 'Resources.*.resx' | Sort-Object Name)
$languages = @()

foreach ($file in $localeFiles) {
    $locale = $file.BaseName.Substring('Resources.'.Length)
    $translated = @(Get-ResourceKeys $file.FullName)
    $missing = @($englishKeys | Where-Object { $_ -notin $translated })
    $languages += [pscustomobject]@{
        language = $locale
        coreMissing = $missing.Count
        coreComplete = $missing.Count -eq 0
    }
}

$selected = if ($Language) {
    @($languages | Where-Object { $_.language -eq $Language })
} else {
    @($languages | Where-Object { -not $_.coreComplete } | Select-Object -First 1)
}

if ($selected.Count -eq 0) {
    $state = [pscustomobject]@{
        generatedAt = (Get-Date).ToUniversalTime().ToString('o')
        coreKeyCount = $englishKeys.Count
        nextLanguage = $null
        languages = $languages
    }
    $state | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $statePath -Encoding utf8
    Write-Output "Core UI localization is complete for every language."
    exit 0
}

$current = $selected[0]
$selectedFile = Join-Path $languageDirectory ("Resources.{0}.resx" -f $current.language)
$selectedKeys = @(Get-ResourceKeys $selectedFile)
$missingKeys = @($englishKeys | Where-Object { $_ -notin $selectedKeys })
$nextBatch = if ($AllMissing) { $missingKeys } else { @($missingKeys | Select-Object -First ([Math]::Max(1, $BatchSize))) }

$state = [pscustomobject]@{
    generatedAt = (Get-Date).ToUniversalTime().ToString('o')
    coreKeyCount = $englishKeys.Count
    nextLanguage = $current.language
    nextBatch = $nextBatch
    languages = $languages
}
$state | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $statePath -Encoding utf8

Write-Output ("Current language: {0}; core missing={1}; batch={2}" -f $current.language, $current.coreMissing, $nextBatch.Count)
Write-Output ("Progress file: {0}" -f $statePath)
Write-Output ($nextBatch -join [Environment]::NewLine)
