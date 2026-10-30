---
document type: cmdlet
external help file: LocalVault.PowerShell.dll-Help.xml
HelpUri: https://github.com/GIT-S-A/LocalVault/blob/main/docs/LocalVault/Write-VaultTemplate.md
Locale: en-US
Module Name: LocalVault
ms.date: 10/13/2026
PlatyPS schema version: 2024-05-01
title: Write-VaultTemplate
---

# Write-VaultTemplate

## SYNOPSIS

Writes templated files from their templates and the vault.

## SYNTAX

### __AllParameterSets

```
Write-VaultTemplate [[-File] <string>] [-Path <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

The `Write-VaultTemplate` cmdlet writes each templated file from its template, replacing every
placeholder with the value of the vault entry it names. Without **File**, it writes every template
of the repository; each template is filled from the vault recorded with it.

The cmdlet writes nothing when git doesn't ignore a target file, or when a placeholder names an
entry that has no value. For a required value not set on this PC, the error gives the note that
says where to get it. In a console, when the vault lacks an entry, the cmdlet first loads the
repository's vaults from their stores.

A file that already has the right content is left as it is.

## EXAMPLES

### Example 1 - Write every templated file of the repository

```powershell
Write-VaultTemplate
```

```Output
Path                                      Action
----                                      ------
C:\dev\proj\infra\.env                    Written
C:\dev\proj\infra\clouds.yaml             Identical
```

## PARAMETERS

### -File

Specifies one templated file. Without it, the cmdlet writes every template of the repository.

```yaml
Type: System.String
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: 0
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

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

### -Confirm

Prompts you for confirmation before running the cmdlet.

```yaml
Type: System.Management.Automation.SwitchParameter
DefaultValue: False
SupportsWildcards: false
Aliases: 
- cf
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

### -WhatIf

Shows what would happen if the cmdlet runs. The cmdlet isn't run.

```yaml
Type: System.Management.Automation.SwitchParameter
DefaultValue: False
SupportsWildcards: false
Aliases: 
- wi
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

### LocalVault.PowerShell.RenderedTemplate

This cmdlet returns the **Path** of each file and the **Action**: `Written` or `Identical`.

## NOTES

`lv template render [<file>]` runs this cmdlet. `lv run` runs it before the command, and
`Clear-VaultTemplate` after.

## RELATED LINKS

- [Add-VaultTemplate](Add-VaultTemplate.md)
- [Update-VaultTemplate](Update-VaultTemplate.md)
- [Clear-VaultTemplate](Clear-VaultTemplate.md)
- [Remove-VaultTemplate](Remove-VaultTemplate.md)
- [Get-VaultTemplate](Get-VaultTemplate.md)
- [about_LocalVault](about_LocalVault.md)
