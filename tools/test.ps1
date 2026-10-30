#Requires -Version 7.6
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot

$modulePath = & (Join-Path $PSScriptRoot 'build.ps1') | Select-Object -Last 1

dotnet test --solution (Join-Path $root 'LocalVault.sln') --configuration Release
if ($LASTEXITCODE -ne 0) {
    throw "dotnet test failed with exit code $LASTEXITCODE."
}

pwsh -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot 'lint.ps1')
if ($LASTEXITCODE -ne 0) {
    throw "Lint failed with exit code $LASTEXITCODE."
}

pwsh -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot 'docs.ps1') -Check
if ($LASTEXITCODE -ne 0) {
    throw "Reference page check failed with exit code $LASTEXITCODE."
}

$pester = @'
$configuration = New-PesterConfiguration
$configuration.Run.Path = $env:LOCALVAULT_PESTER_PATH
$configuration.Run.Exit = $true
$configuration.Output.Verbosity = 'Detailed'
Invoke-Pester -Configuration $configuration
'@
$env:LOCALVAULT_MODULE_PATH = $modulePath
$env:LOCALVAULT_PESTER_PATH = Join-Path $root 'tests/pester'
@() | pwsh -NoProfile -NonInteractive -Command $pester
if ($LASTEXITCODE -ne 0) {
    throw "Pester failed with exit code $LASTEXITCODE."
}
