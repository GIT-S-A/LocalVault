---
document type: cmdlet
external help file: LocalVault.PowerShell.dll-Help.xml
HelpUri: https://github.com/GIT-S-A/LocalVault/blob/main/docs/LocalVault/Set-VaultStore.md
Locale: en-US
Module Name: LocalVault
ms.date: 10/13/2026
PlatyPS schema version: 2024-05-01
title: Set-VaultStore
---

# Set-VaultStore

## SYNOPSIS

Changes one setting of a store without defining it again.

## SYNTAX

### __AllParameterSets

```
Set-VaultStore [-Name] <string> [-Mirror] [-UnlockWith <string>] [-NoUnlock] [-Default] [-Project]
 [-Path <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

The `Set-VaultStore` cmdlet changes the settings that you name, and keeps the store's target, key
file and other settings. `Register-VaultStore` defines a store anew, and forgets what you don't
repeat.

**Mirror** makes the store a mirror: every `Export-VaultToStore` also writes it. `-Mirror:$false`
makes it an ordinary store again. **UnlockWith** names the store that keeps its password;
**NoUnlock** removes it. **Default** makes it the default store of this PC, or, with **Project**,
the project's primary store.

Without **Project**, the cmdlet changes a store of this PC in `stores.json`. With **Project**, it
changes an entry of the project's `localvault.json`; an unlocking store is personal and isn't
accepted there.

## EXAMPLES

### Example 1 - Make your personal store a mirror of everything saved on this PC

```powershell
Set-VaultStore mine -Mirror
```

### Example 2 - Open the team store with the password kept in your personal store

```powershell
Set-VaultStore team -UnlockWith mine
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

### -Mirror

Makes the store a mirror. `-Mirror:$false` makes it an ordinary store. Without this parameter,
the setting stays.

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

### -UnlockWith

Specifies the store whose `AutoOpen` group keeps the password of this store. Not allowed with
**Project** or **NoUnlock**.

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

### -NoUnlock

Removes the store's unlocking store; its password is asked again.

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

### -Default

Makes the store this PC's default store, or with **Project**, the project's primary store.

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

### -Project

Indicates that the store is an entry of the project's `localvault.json`.

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

### LocalVault.PowerShell.VaultStoreInfo

This cmdlet returns the store after the change: **Name**, **Scope**, **Type**, **Target**,
**KeyFile**, **Default**, **UnlockWith**, **Mirror** and **Exists**.

## NOTES

`lv store set <name> [--mirror | --no-mirror] [--unlock-with <store> | --no-unlock] [--default]
[--project]` runs this cmdlet.

## RELATED LINKS

- [Register-VaultStore](Register-VaultStore.md)
- [Get-VaultStore](Get-VaultStore.md)
- [Unregister-VaultStore](Unregister-VaultStore.md)
- [Export-VaultToStore](Export-VaultToStore.md)
