#Requires -Version 7.6
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$settings = Join-Path $root 'PSScriptAnalyzerSettings.psd1'
$scripts = @(Get-Item -LiteralPath (Join-Path $root 'install.ps1')) + @(
    Get-ChildItem -Path (Join-Path $root 'src'), (Join-Path $root 'tools'), (Join-Path $root 'tests') `
        -Recurse -File -Include '*.ps1', '*.psm1', '*.psd1' |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' })

$reports = $scripts | ForEach-Object -ThrottleLimit 4 -Parallel {
    $command = "Invoke-ScriptAnalyzer -Path '$($_.FullName)' -Settings '$using:settings' | " +
        "ForEach-Object { '{0}:{1} {2} {3}' -f `$_.ScriptName, `$_.Line, `$_.RuleName, `$_.Message }"
    $output = pwsh -NoProfile -NonInteractive -Command $command 2>&1
    [pscustomobject]@{ File = $_.Name; ExitCode = $LASTEXITCODE; Output = @($output) }
}

$failed = @($reports | Where-Object { $_.ExitCode -ne 0 -or $_.Output.Count -gt 0 })
if ($failed.Count -gt 0) {
    $failed | ForEach-Object { $_.Output } | Out-String | Write-Information -InformationAction Continue
    throw "PSScriptAnalyzer reported findings in $($failed.Count) file(s)."
}
