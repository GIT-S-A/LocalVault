---
document type: cmdlet
external help file: LocalVault.PowerShell.dll-Help.xml
HelpUri: https://github.com/GIT-S-A/LocalVault/blob/main/docs/LocalVault/Compare-VaultWithStore.md
Locale: en-US
Module Name: LocalVault
ms.date: 10/13/2026
PlatyPS schema version: 2024-05-01
title: Compare-VaultWithStore
---

# Compare-VaultWithStore

## SYNOPSIS

Compares the entries of a vault with its store.

## SYNTAX

### __AllParameterSets

```
Compare-VaultWithStore [-Store <string>] [-Path <string>] [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

The `Compare-VaultWithStore` cmdlet opens the vault's store, reads the vault's group, and returns the
state of each entry. It changes nothing. It handles one vault: the vault of **Path**, of the
`LOCALVAULT_PATH` environment variable, or of the current folder.

| State | Meaning |
| --- | --- |
| `Saved` | Same value in the vault and in the store. |
| `New` | Only in the vault; the next save adds it. |
| `Changed` | Changed in the vault; the next save sends it. |
| `ChangedInKeePass` | Changed in the store; the next load takes it. |
| `Conflict` | Changed in both places. |
| `OnlyInKeePass` | Only in the store; the next load adds it. |
| `RemovedInKeePass` | Removed from the store; the next load asks. |
| `RemovalPending` | Removed from the vault; the next save removes it from the store. |
| `Local` | Kept on this machine only. |

Each vault's store is chosen in this order: the **Store** parameter; the store that the project's
`localvault.json` names for that vault, a rare override; the project's primary store; the default
store of this PC; the only store of this PC. A store named in `localvault.json` is looked up among the project's own
stores first, then among this PC's stores by the id recorded with it, then by name. A store of type
KeePass is a `.kdbx` file. The cmdlet asks for its master password, unless the store was defined with
`Register-VaultStore -UnlockWith <store>`: the cmdlet then opens that other store first, and takes
the password from its `AutoOpen` group. When that group doesn't hold it yet, the cmdlet asks for the
database's password, then offers to keep it there.

## EXAMPLES

### Example 1 - See what a save or a load would do

```powershell
Compare-VaultWithStore
```

```Output
Opening store 'team' (KeePass D:\KeePass\team.kdbx).
KeePass master password for team: ************

Name                 Kind   State
----                 ----   -----
common/db.password   secret ChangedInKeePass
GrafanaAdminPassword secret Saved
clouds.yaml          file   New
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

Specifies the name of a store, defined in the project's `localvault.json` or on this PC, to compare
with. If you omit this parameter, the cmdlet uses the vault's store.

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

### LocalVault.PowerShell.StoreEntryState

This cmdlet returns one object for each entry, with its **Name**, **Kind** and **State**.

## NOTES

`lv store diff [-Store <name>]` runs this cmdlet.

## RELATED LINKS

- [Export-VaultToStore](Export-VaultToStore.md)
- [Import-VaultFromStore](Import-VaultFromStore.md)
- [Get-VaultStoreStatus](Get-VaultStoreStatus.md)
