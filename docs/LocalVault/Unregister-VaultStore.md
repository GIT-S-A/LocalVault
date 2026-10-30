---
document type: cmdlet
external help file: LocalVault.PowerShell.dll-Help.xml
HelpUri: https://github.com/GIT-S-A/LocalVault/blob/main/docs/LocalVault/Unregister-VaultStore.md
Locale: en-US
Module Name: LocalVault
ms.date: 10/13/2026
PlatyPS schema version: 2024-05-01
title: Unregister-VaultStore
---

# Unregister-VaultStore

## SYNOPSIS

Removes a store from this PC or from the project.

## SYNTAX

### __AllParameterSets

```
Unregister-VaultStore [-Name] <string> [-Project] [-Path <string>] [-WhatIf] [-Confirm]
 [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

The `Unregister-VaultStore` cmdlet removes the named store from this PC's list, or with **Project**,
from the project's `localvault.json`. The store's database and its entries don't change. When the
store was this PC's default, there is no default afterwards. A store that unlocks other stores
(`Register-VaultStore -UnlockWith`) isn't removed: the cmdlet fails and names them; add them again
without **UnlockWith**, or with another store, first. When the name isn't defined, the cmdlet writes
a warning.

A project store, or a project entry that refers to a store of each PC, that is still the project's
primary store, or that a vault uses, isn't removed: the cmdlet fails and names them. Give them another
store with `Set-VaultProjectStore` first. A mirror is removed like any other store; saves stop
writing to it.

## EXAMPLES

### Example 1 - Forget a store on this PC

```powershell
Unregister-VaultStore home
```

### Example 2 - Remove a store from the project

```powershell
Unregister-VaultStore old -Project
```

## PARAMETERS

### -Name

Specifies the name of the store.

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

Specifies the path to the vault file. A relative path is resolved from the current location. If
you omit this parameter, the cmdlet uses the `LOCALVAULT_PATH` environment variable, and then
`secrets.local.lv` at the root of the git repository that contains the current location.
With **Project**, the vault's repository is the project.

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

### -Project

Indicates that the store is removed from the project's `localvault.json` instead of from this PC.

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

This cmdlet returns no output.

## NOTES

`lv store rm <name> [--project]` runs this cmdlet.

## RELATED LINKS

- [Register-VaultStore](Register-VaultStore.md)
- [Get-VaultStore](Get-VaultStore.md)
