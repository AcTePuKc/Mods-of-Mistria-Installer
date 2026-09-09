param(
    [string] $Language,
    [int] $BatchSize = 48,
    [switch] $AllMissing,
    [switch] $Deferred,
    [switch] $Remaining
)

$ErrorActionPreference = 'Stop'

if ($Deferred -and $Remaining) {
    throw 'Use either -Deferred or -Remaining, not both.'
}

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

function Test-DeferredKey([string] $key) {
    return $key.StartsWith('GUIResearch') -or $key.StartsWith('GUICrash')
}

$scopeName = if ($Deferred) { 'deferred' } elseif ($Remaining) { 'remaining' } else { 'core' }
$englishKeys = @(Get-ResourceKeys $englishPath | Where-Object {
    if ($Deferred) {
        Test-DeferredKey $_
    } elseif ($Remaining) {
        -not (Test-CoreKey $_) -and -not (Test-DeferredKey $_)
    } else {
        Test-CoreKey $_
    }
})
$localeFiles = @(Get-ChildItem -LiteralPath $languageDirectory -Filter 'Resources.*.resx' | Sort-Object Name)
$languages = @()

foreach ($file in $localeFiles) {
    $locale = $file.BaseName.Substring('Resources.'.Length)
    $translated = @(Get-ResourceKeys $file.FullName)
    $missing = @($englishKeys | Where-Object { $_ -notin $translated })
    $languages += [pscustomobject]@{
        language = $locale
        coreMissing = if ($Deferred) { $null } else { $missing.Count }
        coreComplete = if ($Deferred) { $null } else { $missing.Count -eq 0 }
        deferredMissing = if ($Deferred) { $missing.Count } else { $null }
        deferredComplete = if ($Deferred) { $missing.Count -eq 0 } else { $null }
        remainingMissing = if ($Remaining) { $missing.Count } else { $null }
        remainingComplete = if ($Remaining) { $missing.Count -eq 0 } else { $null }
    }
}

$selected = if ($Language) {
    @($languages | Where-Object { $_.language -eq $Language })
} else {
    if ($Deferred) {
        @($languages | Where-Object { -not $_.deferredComplete } | Select-Object -First 1)
    } elseif ($Remaining) {
        @($languages | Where-Object { -not $_.remainingComplete } | Select-Object -First 1)
    } else {
        @($languages | Where-Object { -not $_.coreComplete } | Select-Object -First 1)
    }
}

if ($selected.Count -eq 0) {
    $state = [pscustomobject]@{
        generatedAt = (Get-Date).ToUniversalTime().ToString('o')
        scope = $scopeName
        coreKeyCount = $englishKeys.Count
        nextLanguage = $null
        languages = $languages
    }
    $state | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $statePath -Encoding utf8
    Write-Output ("{0} localization is complete for every language." -f $scopeName)
    exit 0
}

$current = $selected[0]
$selectedFile = Join-Path $languageDirectory ("Resources.{0}.resx" -f $current.language)
$selectedKeys = @(Get-ResourceKeys $selectedFile)
$missingKeys = @($englishKeys | Where-Object { $_ -notin $selectedKeys })
$nextBatch = if ($AllMissing) { $missingKeys } else { @($missingKeys | Select-Object -First ([Math]::Max(1, $BatchSize))) }

$state = [pscustomobject]@{
    generatedAt = (Get-Date).ToUniversalTime().ToString('o')
    scope = $scopeName
    coreKeyCount = $englishKeys.Count
    nextLanguage = $current.language
    nextBatch = $nextBatch
    languages = $languages
}
$state | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $statePath -Encoding utf8

Write-Output ("Current language: {0}; {1} missing={2}; batch={3}" -f $current.language, $scopeName, $missingKeys.Count, $nextBatch.Count)
Write-Output ("Progress file: {0}" -f $statePath)
Write-Output ($nextBatch -join [Environment]::NewLine)
