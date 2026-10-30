---
document type: cmdlet
external help file: LocalVault.PowerShell.dll-Help.xml
HelpUri: https://github.com/GIT-S-A/LocalVault/blob/main/docs/LocalVault/Clear-VaultTemplate.md
Locale: en-US
Module Name: LocalVault
ms.date: 10/13/2026
PlatyPS schema version: 2024-05-01
title: Clear-VaultTemplate
---

# Clear-VaultTemplate

## SYNOPSIS

Deletes the files written from templates.

## SYNTAX

### __AllParameterSets

```
Clear-VaultTemplate [[-File] <string>] [-Path <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

The `Clear-VaultTemplate` cmdlet deletes the real files that `Write-VaultTemplate` writes, so that
their secrets don't stay on disk in plain text. The templates and the declarations in
`localvault.json` stay. Without **File**, it deletes every written file of the repository.

`Clear-Vault` deletes the files written from the vault it deletes, and `lv run` deletes the files it
wrote when its command ends.

## EXAMPLES

### Example 1 - Remove the plain-text files after work

```powershell
Clear-VaultTemplate
```

## PARAMETERS

### -File

Specifies one templated file. Without it, the cmdlet deletes every written file of the repository.

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

This cmdlet returns the **Path** of each deleted file and the **Action** `Deleted`.

## NOTES

`lv template clear [<file>]` runs this cmdlet.

## RELATED LINKS

- [Add-VaultTemplate](Add-VaultTemplate.md)
- [Update-VaultTemplate](Update-VaultTemplate.md)
- [Write-VaultTemplate](Write-VaultTemplate.md)
- [Remove-VaultTemplate](Remove-VaultTemplate.md)
- [Get-VaultTemplate](Get-VaultTemplate.md)
- [about_LocalVault](about_LocalVault.md)
