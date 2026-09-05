[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $CliPath,

    [Parameter(Mandatory = $true)]
    [string] $PristineZip,

    [Parameter(Mandatory = $true)]
    [string[]] $ModPath,

    [string] $ExpectedFailureModPath
)

$resolvedCli = (Resolve-Path -LiteralPath $CliPath -ErrorAction Stop).Path
$resolvedZip = (Resolve-Path -LiteralPath $PristineZip -ErrorAction Stop).Path
$failures = [System.Collections.Generic.List[string]]::new()

function Invoke-Cli {
    param(
        [Parameter(Mandatory = $true)] [string] $Name,
        [Parameter(Mandatory = $true)] [string[]] $Arguments,
        [Parameter(Mandatory = $true)] [int] $ExpectedExitCode,
        [Parameter(Mandatory = $true)] [scriptblock] $Validate
    )

    $output = (& $resolvedCli @Arguments 2>&1 | Out-String).Trim()
    $exitCode = $LASTEXITCODE

    if ($exitCode -ne $ExpectedExitCode) {
        $failures.Add("${Name}: expected exit $ExpectedExitCode, got $exitCode`n$output")
        return
    }

    try {
        & $Validate $output
        Write-Host "PASS $Name"
    }
    catch {
        $failures.Add("${Name}: $($_.Exception.Message)`n$output")
    }
}

Invoke-Cli -Name "pristine seam check" `
    -Arguments @('--seam-check-json', $resolvedZip) `
    -ExpectedExitCode 0 `
    -Validate {
        param($output)
        $result = $output | ConvertFrom-Json
        if ($result.ok -ne $true) { throw 'seam verifier did not report ok=true' }
        if ($result.seams -le 0) { throw 'seam verifier reported no seams' }
        if ($result.problems.Count -ne 0) { throw 'seam verifier reported problems' }
    }

foreach ($path in $ModPath) {
    $resolvedMod = (Resolve-Path -LiteralPath $path -ErrorAction Stop).Path
    $label = "lint $(Split-Path -Leaf $resolvedMod)"

    Invoke-Cli -Name $label `
        -Arguments @('--lint', $resolvedMod, $resolvedZip, '--compile-check', 'off') `
        -ExpectedExitCode 0 `
        -Validate {
            param($output)
            if ($output -notmatch 'RESULT:\s+OK') {
                throw 'lint did not report RESULT: OK'
            }
        }
}

if ($ExpectedFailureModPath) {
    $resolvedBadMod = (Resolve-Path -LiteralPath $ExpectedFailureModPath -ErrorAction Stop).Path

    Invoke-Cli -Name "compile-gate rejection $(Split-Path -Leaf $resolvedBadMod)" `
        -Arguments @('--lint', $resolvedBadMod, $resolvedZip, '--compile-check', 'require') `
        -ExpectedExitCode 1 `
        -Validate {
            param($output)
            if ($output -notmatch 'RESULT:\s+FAIL') {
                throw 'invalid GML did not produce RESULT: FAIL'
            }
            if ($output -notmatch 'Compile Error:') {
                throw 'invalid GML did not report a compiler-gate failure'
            }
        }
}

if ($failures.Count -gt 0) {
    Write-Error ("CLI asset smoke tests failed:`n- " + ($failures -join "`n- "))
    exit 1
}

Write-Host "CLI asset smoke tests passed."
exit 0
