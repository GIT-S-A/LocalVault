---
document type: cmdlet
external help file: LocalVault.PowerShell.dll-Help.xml
HelpUri: https://github.com/GIT-S-A/LocalVault/blob/main/docs/LocalVault/Remove-VaultEntry.md
Locale: en-US
Module Name: LocalVault
ms.date: 10/08/2026
PlatyPS schema version: 2024-05-01
title: Remove-VaultEntry
---

# Remove-VaultEntry

## SYNOPSIS

Deletes one entry from a vault.

## SYNTAX

### __AllParameterSets

```
Remove-VaultEntry [-Name] <string> [-Path <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

The `Remove-VaultEntry` cmdlet deletes the named entry and keeps every other entry. When the entry
doesn't exist, the cmdlet writes a warning and doesn't change the vault.

The cmdlet keeps the previous version of the vault as `<vault>.bak`.

When the entry is shared and was saved to its store, the vault remembers the removal, and the next
`Export-VaultToStore` removes the entry from the store too.

## EXAMPLES

### Example 1 - Delete an entry

```powershell
Remove-VaultEntry -Name prod/loki.token
```

```Output
Removed prod/loki.token
```

### Example 2 - Delete every entry of a scope

```powershell
Get-VaultEntryList -Scope staging | Remove-VaultEntry
```

The **Name** property of each object from `Get-VaultEntryList` binds to the **Name** parameter.

### Example 3 - Show what would be deleted

```powershell
Get-VaultEntryList -Scope staging | Remove-VaultEntry -WhatIf
```

## PARAMETERS

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

### -Name

Specifies the name of the entry, as `key` or `scope/key`. The name isn't case-sensitive.

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
  ValueFromPipelineByPropertyName: true
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -Path

Specifies the path to the vault file. A relative path is resolved from the current location. If
you omit this parameter, the cmdlet uses the `LOCALVAULT_PATH` environment variable, and then
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

### System.String

You can pipe an object that has a **Name** property to this cmdlet.

## OUTPUTS

### None

This cmdlet returns no output.

## NOTES

`lv rm <name>` runs this cmdlet.

## RELATED LINKS

- [Set-VaultEntry](Set-VaultEntry.md)
- [Get-VaultEntryList](Get-VaultEntryList.md)
- [Clear-Vault](Clear-Vault.md)
