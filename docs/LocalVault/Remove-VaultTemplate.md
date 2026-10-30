---
document type: cmdlet
external help file: LocalVault.PowerShell.dll-Help.xml
HelpUri: https://github.com/GIT-S-A/LocalVault/blob/main/docs/LocalVault/Remove-VaultTemplate.md
Locale: en-US
Module Name: LocalVault
ms.date: 10/13/2026
PlatyPS schema version: 2024-05-01
title: Remove-VaultTemplate
---

# Remove-VaultTemplate

## SYNOPSIS

Stops managing a templated file.

## SYNTAX

### __AllParameterSets

```
Remove-VaultTemplate [-File] <string> [-Path <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

The `Remove-VaultTemplate` cmdlet removes a templated file from `localvault.json`. The template file
and the real file stay where they are; delete them yourself if you no longer need them, and commit
`localvault.json`.

## EXAMPLES

### Example 1 - Stop managing a file

```powershell
Remove-VaultTemplate .\infra\clouds.yaml
```

## PARAMETERS

### -File

Specifies the templated file.

```yaml
Type: System.String
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: 0
  IsRequired: true
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

### None

This cmdlet writes a warning when the file isn't a template of the repository.

## NOTES

`lv template rm <file>` runs this cmdlet.

## RELATED LINKS

- [Add-VaultTemplate](Add-VaultTemplate.md)
- [Update-VaultTemplate](Update-VaultTemplate.md)
- [Write-VaultTemplate](Write-VaultTemplate.md)
- [Clear-VaultTemplate](Clear-VaultTemplate.md)
- [Get-VaultTemplate](Get-VaultTemplate.md)
- [about_LocalVault](about_LocalVault.md)
