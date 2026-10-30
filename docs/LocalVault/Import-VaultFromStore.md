---
document type: cmdlet
external help file: LocalVault.PowerShell.dll-Help.xml
HelpUri: https://github.com/GIT-S-A/LocalVault/blob/main/docs/LocalVault/Import-VaultFromStore.md
Locale: en-US
Module Name: LocalVault
ms.date: 10/13/2026
PlatyPS schema version: 2024-05-01
title: Import-VaultFromStore
---

# Import-VaultFromStore

## SYNOPSIS

Loads the shared entries of the repository's vaults from their stores.

## SYNTAX

### __AllParameterSets

```
Import-VaultFromStore [-Store <string>] [-Replace <string[]>] [-ReplaceAll] [-Path <string>]
 [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

The `Import-VaultFromStore` cmdlet reads the entries of each vault's group in its store and merges
them into the vault. On a new machine, it creates the vaults from their stores.

The entries go to the KeePass group `LocalVault/<repository>`, followed by the folder of the vault
inside the repository and by its file name when it isn't `secrets.local.lv`. For example,
`infra/secrets.local.lv` in the repository `webshop` uses
`LocalVault/webshop/infra`. Each entry keeps its full name as its title, scope included,
such as `common/db.password`. A secret is the entry's password. A file is an attachment, with its
stored path in the field `LocalVault.Path`.

The cmdlet asks for the store's password and doesn't keep it. For each shared entry, the vault
remembers the value that it last had in the store, and the cmdlet compares the three values:

- An entry only in the store is added to the vault.
- An entry changed only in the store replaces the vault value, without a question.
- An entry changed only in the vault stays; `Export-VaultToStore` sends it.
- An entry changed in both places stays unless you answer yes, or name it with **Replace**, or use
  **ReplaceAll**. Without a console and without these parameters, the cmdlet leaves that vault as it
  was, loads the others, and fails listing the entries.
- An entry removed from the store is removed from the vault only if you answer yes. Kept, it goes
  back to the store with the next save.
- An entry that you removed here and that the store still holds unchanged isn't added back.

Entries marked local stay as they are, even when the store has an entry with the same name. A
required value set on this PC stays. A KeePass entry added by hand, without the `LocalVault.Kind`
field, is read as a secret. Entries whose title isn't a valid vault name, or whose title appears
twice, are left out with a warning.

Without **Path**, and without the `LOCALVAULT_PATH` environment variable, the cmdlet handles every
vault of the repository: the vaults listed in `localvault.json`, which LocalVault fills each time it
writes a vault of the repository, and the vault of the current folder. It opens each store once for
the whole run, so a password is asked at most once per store. A vault whose
store isn't defined on this PC is skipped with a warning; the cmdlet fails only when no vault could
be reached. With **Path**, it handles that vault only. A listed vault that doesn't exist on this PC is created.

Each vault's store is chosen in this order: the **Store** parameter; the store that the project's
`localvault.json` names for that vault, a rare override; the project's primary store; the default
store of this PC; the only store of this PC. A store named in `localvault.json` is looked up among the project's own
stores first, then among this PC's stores by the id recorded with it, then by name. A store of type
KeePass is a `.kdbx` file. The cmdlet asks for its master password, unless the store was defined with
`Register-VaultStore -UnlockWith <store>`: the cmdlet then opens that other store first, and takes
the password from its `AutoOpen` group. When that group doesn't hold it yet, the cmdlet asks for the
database's password, then offers to keep it there.

The cmdlet reads the primary store only, never the mirrors that `Export-VaultToStore` also writes. To
read a mirror, for example when the primary store can't be reached, name it with **Store**.

## EXAMPLES

### Example 1 - Get the shared entries of a project on a new machine

```powershell
Import-VaultFromStore
```

```Output
Opening store 'team' (KeePass D:\KeePass\team.kdbx).
KeePass master password for team: ************
secrets.local.lv <- store 'team': 5 added, 0 updated, 0 kept as they were, 0 removed.
infra/secrets.local.lv <- store 'team': 3 added, 0 updated, 0 kept as they were, 0 removed.
```

### Example 2 - Take the store's value of an entry changed in both places

```powershell
Import-VaultFromStore -Replace common/db.password
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

### -Replace

Specifies the names of entries changed both in the vault and in the store whose store value replaces the vault value.

```yaml
Type: System.String[]
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

### -ReplaceAll

Indicates that the store's value replaces the vault value of every entry changed in both places.

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

### -Store

Specifies the name of a store, defined in the project's `localvault.json` or on this PC, that
every vault handled by the cmdlet uses. If you omit this parameter, each vault uses its own store.

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

### LocalVault.PowerShell.StoreLoadSummary

This cmdlet returns the number of entries **Added**, **Updated** from the stores, **Kept** as they
were, and **Removed**.

## NOTES

`lv load [--store <name>] [--replace a,b | --replace-all]` runs this cmdlet. `lv` exits with code `2`
when entries changed in both places weren't resolved.

In a console, a command that reads an entry or a vault this PC doesn't have yet runs this cmdlet
first, keeping the local value of every entry changed in both places.

## RELATED LINKS

- [Export-VaultToStore](Export-VaultToStore.md)
- [Compare-VaultWithStore](Compare-VaultWithStore.md)
- [Get-VaultStoreStatus](Get-VaultStoreStatus.md)
- [Register-VaultStore](Register-VaultStore.md)
