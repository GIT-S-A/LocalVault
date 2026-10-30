---
document type: cmdlet
external help file: LocalVault.PowerShell.dll-Help.xml
HelpUri: https://github.com/GIT-S-A/LocalVault/blob/main/docs/LocalVault/Export-VaultToStore.md
Locale: en-US
Module Name: LocalVault
ms.date: 10/13/2026
PlatyPS schema version: 2024-05-01
title: Export-VaultToStore
---

# Export-VaultToStore

## SYNOPSIS

Saves the shared entries of the repository's vaults to their stores.

## SYNTAX

### __AllParameterSets

```
Export-VaultToStore [-Store <string>] [-Replace <string[]>] [-ReplaceAll] [-Path <string>]
 [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

The `Export-VaultToStore` cmdlet writes the shared entries of each vault to the vault's store, where
people who use the same store can read them with KeePass or load them with `Import-VaultFromStore`.
Entries marked local with `Set-VaultEntry -Sharing Local` stay out. Required values declared with
`Set-VaultEntry -Required` send their declaration, never a person's value.

The entries go to the KeePass group `LocalVault/<repository>`, followed by the folder of the vault
inside the repository and by its file name when it isn't `secrets.local.lv`. For example,
`infra/secrets.local.lv` in the repository `webshop` uses
`LocalVault/webshop/infra`. Each entry keeps its full name as its title, scope included,
such as `common/db.password`. A secret is the entry's password. A file is an attachment, with its
stored path in the field `LocalVault.Path`.

Before it asks for a password, the cmdlet names the store and its file. It doesn't keep the password.
For each shared entry, the vault remembers the value that it last had in the store, and the cmdlet
compares the three values:

- An entry that isn't in the store yet is added.
- An entry changed only in the vault replaces the store's value. KeePass keeps the previous value in
  the entry's history.
- An entry changed only in the store isn't saved; load it first with `Import-VaultFromStore`.
- An entry changed in both places isn't saved unless you answer yes, or name it with **Replace**, or
  use **ReplaceAll**.
- An entry removed from the vault with `Remove-VaultEntry` is removed from the store, into its
  recycle bin when the database has one, unless someone changed it in the store since.
- An entry removed from the store by someone else isn't saved; `Import-VaultFromStore` asks whether
  to remove it here too.

The cmdlet also keeps two guide entries for people who find the group in KeePass. `Repository
origin`, in `LocalVault/<repository>`, has the repository's `origin` URL without credentials, and
its notes say how to clone the repository and load the vault. `Install instructions for localVault`,
in `LocalVault`, says how to install LocalVault; the cmdlet adds it when it's missing and never
changes it. Neither one is read as a vault entry.

Apart from these, the cmdlet doesn't touch entries outside the group of the vault. When the database
file changed since the cmdlet opened it, the cmdlet merges both versions before it saves.

After the primary store, the cmdlet writes the same shared entries to every mirror: the stores marked
with `Register-VaultStore -Mirror`, in the project's `localvault.json` and on this PC, except the
vault's primary store itself. In a mirror, the vault's value always wins: an entry changed or removed
directly in the mirror is written again and reported; an entry that exists only in the mirror is left
as it is and reported. A mirror that can't be opened or saved gives a warning and the save goes on; a
mirror that this PC doesn't define is skipped with a message. With **Store**, the cmdlet writes that
store only, without mirrors.

Each store is opened and saved at most once per run, whether it is the primary store of one vault, a
mirror of another, or both; then each vault is written once, recording what was saved to all its
stores.

Without **Path**, and without the `LOCALVAULT_PATH` environment variable, the cmdlet handles every
vault of the repository: the vaults listed in `localvault.json`, which LocalVault fills each time it
writes a vault of the repository, and the vault of the current folder. It opens each store once for
the whole run, so a password is asked at most once per store. A vault whose
store isn't defined on this PC is skipped with a warning; the cmdlet fails only when no vault could
be reached. With **Path**, it handles that vault only.

Each vault's store is chosen in this order: the **Store** parameter; the store that the project's
`localvault.json` names for that vault, a rare override; the project's primary store; the default
store of this PC; the only store of this PC. A store named in `localvault.json` is looked up among the project's own
stores first, then among this PC's stores by the id recorded with it, then by name. A store of type
KeePass is a `.kdbx` file. The cmdlet asks for its master password, unless the store was defined with
`Register-VaultStore -UnlockWith <store>`: the cmdlet then opens that other store first, and takes
the password from its `AutoOpen` group. When that group doesn't hold it yet, the cmdlet asks for the
database's password, then offers to keep it there.

## EXAMPLES

### Example 1 - Save every vault of the repository

```powershell
Export-VaultToStore
```

```Output
Opening store 'team' (KeePass D:\KeePass\team.kdbx).
KeePass master password for team: ************
secrets.local.lv -> store 'team', LocalVault/webshop: 2 added, 1 updated, 0 removed, 0 not saved.
infra/secrets.local.lv -> store 'team', LocalVault/webshop/infra: 0 added, 1 updated, 0 removed, 0 not saved.
If KeePass 2 has team.kdbx open, reload it there before saving, or these changes can be lost.

Added Updated Removed Skipped
----- ------- ------- -------
    2       2       0       0
```

### Example 2 - Overwrite a value that a teammate changed too

```powershell
Export-VaultToStore -Replace common/db.password
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

Specifies the names of entries changed both in the vault and in the store whose vault value replaces the store's value.

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

Indicates that the vault value replaces the store's value of every entry changed in both places.

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

### LocalVault.PowerShell.StoreSaveSummary

This cmdlet returns the number of entries **Added**, **Updated** and **Removed** in the stores, and
the number **Skipped**.

## NOTES

If KeePass 2 has the same database open, reload it there before saving, or it may overwrite these
changes.

`lv save [--store <name>] [--replace a,b | --replace-all]` runs this cmdlet.

## RELATED LINKS

- [Import-VaultFromStore](Import-VaultFromStore.md)
- [Copy-VaultToStore](Copy-VaultToStore.md)
- [Compare-VaultWithStore](Compare-VaultWithStore.md)
- [Get-VaultStoreStatus](Get-VaultStoreStatus.md)
- [Set-VaultProjectStore](Set-VaultProjectStore.md)
- [Register-VaultStore](Register-VaultStore.md)
- [Set-VaultEntry](Set-VaultEntry.md)
