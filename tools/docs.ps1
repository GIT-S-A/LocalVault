#Requires -Version 7.6
[CmdletBinding()]
param(
    [switch]$Check
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module Microsoft.PowerShell.PlatyPS -RequiredVersion 1.0.3

$root = Split-Path -Parent $PSScriptRoot
$source = Join-Path $root 'docs/LocalVault'
$target = Join-Path $root 'docs/reference.md'

function ConvertTo-Anchor {
    [CmdletBinding()]
    [OutputType([string])]
    param([Parameter(Mandatory)][string]$Heading)

    '#' + ($Heading.ToLowerInvariant() -replace '[^a-z0-9 _-]', '' -replace ' ', '-')
}

function Get-ReferenceLink {
    [CmdletBinding()]
    [OutputType([string])]
    param([Parameter(Mandatory)][string]$Text)

    $Text -replace '\]\(about_LocalVault\.md\)', '](#concepts)' `
        -replace '\]\(([A-Za-z]+-[A-Za-z]+)\.md\)', { '](' + (ConvertTo-Anchor -Heading $_.Groups[1].Value) + ')' }
}

function Format-ParameterTable {
    [CmdletBinding()]
    [OutputType([string])]
    param([Parameter(Mandatory)]$Parameter)

    $sets = $Parameter.ParameterSets
    $position = ($sets | ForEach-Object Position | Select-Object -Unique) -join ', '
    $required = ($sets | ForEach-Object { if ($_.IsRequired) { "$($_.Name)" } }) -join ', '
    $pipeline = @(
        if ($sets | Where-Object ValueFromPipeline) { 'by value' }
        if ($sets | Where-Object ValueFromPipelineByPropertyName) { 'by property name' }
    ) -join ', '
    $rows = [ordered]@{
        'Type'           = $Parameter.Type
        'Parameter sets' = ($sets | ForEach-Object Name) -join ', '
        'Position'       = $position
        'Required'       = if ($required) { $required } else { 'False' }
        'Pipeline input' = if ($pipeline) { $pipeline } else { 'False' }
        'Default value'  = $Parameter.DefaultValue
        'Aliases'        = if ($Parameter.Aliases.Count) { $Parameter.Aliases -join ', ' } else { 'None' }
    }
    $lines = @('| Property | Value |', '| --- | --- |')
    foreach ($row in $rows.GetEnumerator()) {
        $lines += "| $($row.Key) | $($row.Value) |"
    }
    $lines -join "`n"
}

function Format-CommandSection {
    [CmdletBinding()]
    [OutputType([string])]
    param([Parameter(Mandatory)]$Help)

    $out = [System.Collections.Generic.List[string]]::new()
    $out.Add("## $($Help.Title)")
    $out.Add($Help.Synopsis.Trim())
    $out.Add('### Syntax')
    foreach ($syntax in $Help.Syntax) {
        $out.Add("``````text`n$($syntax.ToStringWithWrap())`n``````")
    }
    $out.Add('### Description')
    $out.Add((Get-ReferenceLink -Text $Help.Description.Trim()))
    $out.Add('### Examples')
    foreach ($example in $Help.Examples) {
        $out.Add("#### $($example.Title)")
        $out.Add((Get-ReferenceLink -Text $example.Remarks.Trim()))
    }
    $parameters = @($Help.Parameters)
    if ($parameters.Count -gt 0) {
        $out.Add('### Parameters')
        foreach ($parameter in $parameters | Sort-Object Name) {
            $out.Add("#### -$($parameter.Name)")
            $out.Add((Get-ReferenceLink -Text $parameter.Description.Trim()))
            $out.Add((Format-ParameterTable -Parameter $parameter))
        }
    }
    foreach ($section in @(@{ Title = 'Inputs'; Items = $Help.Inputs }, @{ Title = 'Outputs'; Items = $Help.Outputs })) {
        $out.Add("### $($section.Title)")
        foreach ($item in $section.Items) {
            $out.Add("**$($item.Typename)**: $($item.Description.Trim())")
        }
    }
    if ($Help.Notes) {
        $out.Add('### Notes')
        $out.Add((Get-ReferenceLink -Text $Help.Notes.Trim()))
    }
    $out -join "`n`n"
}

$module = Import-MarkdownModuleFile -Path (Join-Path $source 'LocalVault.md')
$commands = Measure-PlatyPSMarkdown -Path (Join-Path $source '*.md') |
    Where-Object FileType -Match 'CommandHelp' |
    ForEach-Object { Import-MarkdownCommandHelp -Path $_.FilePath } |
    Sort-Object Title

$about = Get-Content -LiteralPath (Join-Path $source 'about_LocalVault.md') -Raw
$concepts = [regex]::Match($about, '(?s)## Long description\s*(.*?)\s*## See also').Groups[1].Value

$page = [System.Collections.Generic.List[string]]::new()
$page.Add('# LocalVault reference')
$page.Add((Get-ReferenceLink -Text $module.Description.Trim()))
$page.Add('## Contents')
$page.Add((@('- [Concepts](#concepts)') + @($commands | ForEach-Object {
                "- [$($_.Title)]($(ConvertTo-Anchor -Heading $_.Title)): $($_.Synopsis.Trim())"
            })) -join "`n")
$page.Add('## Concepts')
$page.Add((Get-ReferenceLink -Text $concepts))
foreach ($command in $commands) {
    $page.Add((Format-CommandSection -Help $command))
}
$text = ($page -join "`n`n").Replace("`r`n", "`n") + "`n"

if ($Check) {
    $current = if (Test-Path -LiteralPath $target) { (Get-Content -LiteralPath $target -Raw).Replace("`r`n", "`n") } else { '' }
    if ($current -ne $text) {
        throw 'docs/reference.md is out of date. Run tools/docs.ps1.'
    }
    return
}

[System.IO.File]::WriteAllText($target, $text, [System.Text.UTF8Encoding]::new($false))
$target
