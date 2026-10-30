---
document type: cmdlet
external help file: LocalVault.PowerShell.dll-Help.xml
HelpUri: https://github.com/GIT-S-A/LocalVault/blob/main/docs/LocalVault/Get-VaultTemplate.md
Locale: en-US
Module Name: LocalVault
ms.date: 10/13/2026
PlatyPS schema version: 2024-05-01
title: Get-VaultTemplate
---

# Get-VaultTemplate

## SYNOPSIS

Gets the templated files of the repository and their state.

## SYNTAX

### __AllParameterSets

```
Get-VaultTemplate [-Path <string>] [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

The `Get-VaultTemplate` cmdlet lists the templated files recorded in `localvault.json`. For each
one, it says whether the real file is written on this PC and whether its content matches what
`Write-VaultTemplate` would write now. The cmdlet doesn't show any value.

## EXAMPLES

### Example 1 - See which files are written

```powershell
Get-VaultTemplate | Format-Table Path, Rendered, UpToDate
```

## PARAMETERS

### -Path

Specifies the path to the vault file, which also names the git repository whose templates the
cmdlet handles. A relative path is resolved from the current location. If you omit this
parameter, the cmdlet uses the `LOCALVAULT_PATH` environment variable, and then
`secrets.local.lv` at the root of the git repository that contains the current location.

```yaml
Type: System.String
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### CommonParameters

This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable,
-InformationAction, -InformationVariable, -OutBuffer, -OutVariable, -PipelineVariable,
-ProgressAction, -Verbose, -WarningAction, and -WarningVariable. For more information, see
[about_CommonParameters](https://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

### None

You can't pipe objects to this cmdlet.

## OUTPUTS

### LocalVault.PowerShell.VaultTemplateInfo

This cmdlet returns the **Path** of each file, its **Template**, the **Vault** that fills it,
whether the file is **Rendered**, and **UpToDate**: `$true` or `$false` when the file is written,
empty when it isn't or when a value is missing.

## NOTES

`lv template` runs this cmdlet.

## RELATED LINKS

- [Add-VaultTemplate](Add-VaultTemplate.md)
- [Update-VaultTemplate](Update-VaultTemplate.md)
- [Write-VaultTemplate](Write-VaultTemplate.md)
- [Clear-VaultTemplate](Clear-VaultTemplate.md)
- [Remove-VaultTemplate](Remove-VaultTemplate.md)
- [about_LocalVault](about_LocalVault.md)
