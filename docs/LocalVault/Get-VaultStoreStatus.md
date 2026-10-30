---
document type: cmdlet
external help file: LocalVault.PowerShell.dll-Help.xml
HelpUri: https://github.com/GIT-S-A/LocalVault/blob/main/docs/LocalVault/Get-VaultStoreStatus.md
Locale: en-US
Module Name: LocalVault
ms.date: 10/13/2026
PlatyPS schema version: 2024-05-01
title: Get-VaultStoreStatus
---

# Get-VaultStoreStatus

## SYNOPSIS

Gets the store of each vault of the repository and what isn't saved yet.

## SYNTAX

### __AllParameterSets

```
Get-VaultStoreStatus [-Store <string>] [-Path <string>] [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

The `Get-VaultStoreStatus` cmdlet reads the vaults, `localvault.json` and this PC's stores. It doesn't
open any store and asks for nothing.

It returns one object for each vault of the repository: the vaults listed in `localvault.json` and
the vault of the current folder. With **Path**, or the `LOCALVAULT_PATH` environment variable, it
returns that vault only. Each object holds the vault's KeePass group, the store it uses and where
that store is defined (`Project` or `Computer`), the store's target, the problem that keeps the vault
from its store, if any, the store that holds its password (**UnlockWith**), the mirrors that each
save also writes (**Mirrors**, marked `(not on this PC)` when this PC doesn't define one), and how many
shared entries are saved, how many changed since the last save or load, how many are local, and how
many removals wait for the next save.

A vault whose store isn't defined on this PC has a **Problem** such as `migration/secrets.local.lv
uses store 'mine', which isn't defined on this PC`. `lv save` and `lv load` skip such a vault.

## EXAMPLES

### Example 1 - Check before leaving the machine

```powershell
Get-VaultStoreStatus | Format-Table Vault, Store, Scope, Pending, Problem
```

```Output
Vault                      Store Scope    Pending Problem
-----                      ----- -----    ------- -------
secrets.local.lv           team  Project        1
infra/secrets.local.lv     team  Project        0
migration/secrets.local.lv mine                 0 migration/secrets.local.lv uses store 'mine', which isn't defined on this PC. ...
```

## PARAMETERS

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

### -Store

Specifies the name of a store, defined in the project's `localvault.json` or on this PC, as if it
were given to `Export-VaultToStore`. If you omit this parameter, each vault uses its own store.

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

### LocalVault.PowerShell.VaultStoreStatus

This cmdlet returns one object for each vault, with its **Vault** (relative to the repository),
**Group**, **Store**, **Scope**, **Target**, **Problem**, **UnlockWith**, **Mirrors**, and the counts **Shared**,
**Saved**, **Pending**, **Local** and **RemovalsPending**.

## NOTES

`lv store` without a subcommand shows this status.

## RELATED LINKS

- [Export-VaultToStore](Export-VaultToStore.md)
- [Compare-VaultWithStore](Compare-VaultWithStore.md)
- [Set-VaultProjectStore](Set-VaultProjectStore.md)
- [Get-VaultStore](Get-VaultStore.md)
