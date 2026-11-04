---
document type: cmdlet
external help file: LocalVault.PowerShell.dll-Help.xml
HelpUri: https://github.com/GIT-S-A/LocalVault/blob/main/docs/LocalVault/Get-VaultStore.md
Locale: en-US
Module Name: LocalVault
ms.date: 10/13/2026
PlatyPS schema version: 2024-05-01
title: Get-VaultStore
---

# Get-VaultStore

## SYNOPSIS

Gets the stores of the project and of this PC.

## SYNTAX

### __AllParameterSets

```
Get-VaultStore [[-Name] <string>] [-Path <string>] [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

The `Get-VaultStore` cmdlet gets the stores of the project's `localvault.json`, then the stores
defined on this PC for the current Windows account, and whether each target exists. A project entry
that names a store without a target has the type `reference`; its target is the one of this PC's store
of that name, or `(not on this PC)`. Outside a git repository, the cmdlet gets this PC's stores only.
It opens no store.

## EXAMPLES

### Example 1 - List the stores

```powershell
Get-VaultStore
```

```Output
Name    Scope    Type      Target                       KeyFile      Default UnlockWith Mirror Exists
----    -----    ----      ------                       -------      ------- ---------- ------ ------
team    Project  reference D:\KeePass\it.kdbx                             True             False   True
archive Project  keepass   I:\Archive\proj.kdbx                          False             True   True
team    Computer keepass   D:\KeePass\it.kdbx                             True mine       False   True
mine    Computer keepass   %OneDrive%\KeePass\mine.kdbx E:\mine.keyx   False             True   True
```

### Example 2 - Find stores whose file is missing

```powershell
Get-VaultStore | Where-Object -Not Exists
```

## PARAMETERS

### -Name

Specifies the name of a store. If you omit this parameter, the cmdlet gets all stores.

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

Specifies the path to the vault file. A relative path is resolved from the current location. If
you omit this parameter, the cmdlet uses the `LOCALVAULT_PATH` environment variable, and then
`secrets.local.lv` at the root of the git repository that contains the current location.
The vault's repository gives the project's stores.

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

### LocalVault.PowerShell.VaultStoreInfo

This cmdlet returns one object for each store, with its **Name**, **Scope** (`Project` or
`Computer`), **Type** (`reference` for a project entry that names a store of each PC), **Target**,
**KeyFile**, whether it's the **Default** of its scope (for the project, its primary store), the store
that holds its password (**UnlockWith**), whether it's a **Mirror**, whether the target
**Exists**, and whether it's this PC's **Personal** store.

## NOTES

`lv store list` runs this cmdlet.

## RELATED LINKS

- [Register-VaultStore](Register-VaultStore.md)
- [Unregister-VaultStore](Unregister-VaultStore.md)
- [Get-VaultStoreStatus](Get-VaultStoreStatus.md)
