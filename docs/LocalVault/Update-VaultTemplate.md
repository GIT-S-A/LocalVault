---
document type: cmdlet
external help file: LocalVault.PowerShell.dll-Help.xml
HelpUri: https://github.com/GIT-S-A/LocalVault/blob/main/docs/LocalVault/Update-VaultTemplate.md
Locale: en-US
Module Name: LocalVault
ms.date: 10/13/2026
PlatyPS schema version: 2024-05-01
title: Update-VaultTemplate
---

# Update-VaultTemplate

## SYNOPSIS

Takes the edits of templated files back into their templates.

## SYNTAX

### __AllParameterSets

```
Update-VaultTemplate [[-File] <string>] [-AcceptChanges] [-Path <string>] [-WhatIf] [-Confirm]
 [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

The `Update-VaultTemplate` cmdlet compares each templated file with its template and rewrites the
template with the changes made to the file, such as a new address or a new setting. Without
**File**, it handles every template of the repository.

A line that holds a placeholder is recognized by the text around the placeholder. When the value
on that line differs from the vault, the secret itself changed: the cmdlet stores the new value in
the vault only with **AcceptChanges**, or when you answer yes in a console. Without a console and
without **AcceptChanges**, it fails and writes nothing. When the text around a placeholder changed,
the cmdlet fails and names the placeholder: edit the template itself for that line.

Every vault value of at least 6 characters that appears in a new line becomes its placeholder.
The cmdlet never writes a template that contains a vault value. Commit the updated template.

## EXAMPLES

### Example 1 - Take an edited .env back into its template

```powershell
Update-VaultTemplate .\infra\.env
```

### Example 2 - Store a password changed in the file, without a question

```powershell
Update-VaultTemplate -AcceptChanges
```

## PARAMETERS

### -File

Specifies one templated file. Without it, the cmdlet handles every template of the repository.

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

### -AcceptChanges

Indicates that secret values changed in the file are stored in the vault without a question.

```yaml
Type: System.Management.Automation.SwitchParameter
DefaultValue: False
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

This cmdlet writes messages only.

## NOTES

`lv template update [<file>] [--accept]` runs this cmdlet.

## RELATED LINKS

- [Add-VaultTemplate](Add-VaultTemplate.md)
- [Write-VaultTemplate](Write-VaultTemplate.md)
- [Clear-VaultTemplate](Clear-VaultTemplate.md)
- [Remove-VaultTemplate](Remove-VaultTemplate.md)
- [Get-VaultTemplate](Get-VaultTemplate.md)
- [about_LocalVault](about_LocalVault.md)
