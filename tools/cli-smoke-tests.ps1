[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $CliPath
)

$resolvedCli = (Resolve-Path -LiteralPath $CliPath -ErrorAction Stop).Path
$failures = [System.Collections.Generic.List[string]]::new()

function Invoke-CliCase {
    param(
        [Parameter(Mandatory = $true)] [string] $Name,
        [Parameter(Mandatory = $true)] [string[]] $Arguments,
        [Parameter(Mandatory = $true)] [int] $ExpectedExitCode,
        [Parameter(Mandatory = $true)] [string] $ExpectedOutput
    )

    $output = (& $resolvedCli @Arguments 2>&1 | Out-String).Trim()
    $exitCode = $LASTEXITCODE

    if ($exitCode -ne $ExpectedExitCode) {
        $failures.Add("${Name}: expected exit $ExpectedExitCode, got $exitCode")
    }

    if ($output -notmatch $ExpectedOutput) {
        $failures.Add("${Name}: output did not match /$ExpectedOutput/`n$output")
    }

    if ($failures.Count -eq 0 -or $failures[-1] -notlike "${Name}:*") {
        Write-Host "PASS $Name"
    }
}

Invoke-CliCase -Name "version" `
    -Arguments @('--version') `
    -ExpectedExitCode 0 `
    -ExpectedOutput '^[0-9]+\.[0-9]+'

Invoke-CliCase -Name "help" `
    -Arguments @('--help') `
    -ExpectedExitCode 0 `
    -ExpectedOutput '--list-mods'

Invoke-CliCase -Name "lint usage" `
    -Arguments @('--lint') `
    -ExpectedExitCode 2 `
    -ExpectedOutput '--lint requires a value'

Invoke-CliCase -Name "missing seam archive" `
    -Arguments @('--seam-check-json', 'C:\aim-cli-smoke\missing-pristine.zip') `
    -ExpectedExitCode 2 `
    -ExpectedOutput 'pristine backup not found'

Invoke-CliCase -Name "missing lint manifest" `
    -Arguments @('--lint', 'C:\aim-cli-smoke\missing-mod', 'C:\aim-cli-smoke\missing-pristine.zip') `
    -ExpectedExitCode 2 `
    -ExpectedOutput 'Could not find the manifest file'

Invoke-CliCase -Name "invalid compile-check value" `
    -Arguments @('--lint', 'C:\aim-cli-smoke\missing-mod', 'C:\aim-cli-smoke\missing-pristine.zip', '--compile-check', 'invalid') `
    -ExpectedExitCode 2 `
    -ExpectedOutput '--compile-check expects on, off, or require'

if ($failures.Count -gt 0) {
    Write-Error ("CLI smoke tests failed:`n- " + ($failures -join "`n- "))
    exit 1
}

Write-Host "CLI smoke tests passed."
exit 0
