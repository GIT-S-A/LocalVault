#Requires -Version 7.6
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$manifest = Join-Path $root 'src/LocalVault.PowerShell/LocalVault.psd1'
$version = (Import-PowerShellDataFile -LiteralPath $manifest).ModuleVersion
$output = Join-Path $root "out/LocalVault/$version"

if (Test-Path -LiteralPath $output) {
    Remove-Item -LiteralPath $output -Recurse -Force
}

dotnet publish (Join-Path $root 'src/LocalVault.PowerShell/LocalVault.PowerShell.csproj') `
    --configuration $Configuration --output $output --nologo
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}
Copy-Item -LiteralPath (Join-Path $root 'install.ps1') -Destination $output

Import-Module Microsoft.PowerShell.PlatyPS -RequiredVersion 1.0.3
$docs = Join-Path $root 'docs/LocalVault'
$culture = New-Item -Path (Join-Path $output 'en-US') -ItemType Directory -Force
$staging = Join-Path ([System.IO.Path]::GetTempPath()) ([guid]::NewGuid())
try {
    Measure-PlatyPSMarkdown -Path (Join-Path $docs '*.md') |
        Where-Object FileType -Match 'CommandHelp' |
        ForEach-Object { Import-MarkdownCommandHelp -Path $_.FilePath } |
        Export-MamlCommandHelp -OutputFolder $staging |
        Move-Item -Destination $culture
}
finally {
    Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
}

$about = Get-Content -LiteralPath (Join-Path $docs 'about_LocalVault.md') -Raw
Set-Content -LiteralPath (Join-Path $culture 'about_LocalVault.help.txt') -Encoding utf8 `
    -Value ($about -replace '(?s)\A---.*?---\s*', '')

$output
