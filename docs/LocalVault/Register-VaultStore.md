---
document type: cmdlet
external help file: LocalVault.PowerShell.dll-Help.xml
HelpUri: https://github.com/GIT-S-A/LocalVault/blob/main/docs/LocalVault/Register-VaultStore.md
Locale: en-US
Module Name: LocalVault
ms.date: 10/13/2026
PlatyPS schema version: 2024-05-01
title: Register-VaultStore
---

# Register-VaultStore

## SYNOPSIS

Defines a store on this PC or in the project.

## SYNTAX

### __AllParameterSets

```
Register-VaultStore [[-Target] <string>] [-Name <string>] [-KeyFile <string>] [-Default]
 [-UnlockWith <string>] [-Mirror] [-Personal] [-Project] [-Source] [-Path <string>] [-WhatIf]
 [-Confirm] [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

The `Register-VaultStore` cmdlet records a store: a name, a type, and a target. The target says the
type: a `.kdbx` file is a KeePass database. Targets with a prefix such as `azkv:` are reserved for
other types and refused for now. Without **Name**, the name is the target's file name without its
extension, such as `it` for `it.kdbx`.

Without **Project**, the store belongs to this PC: the current Windows account on this machine keeps
it in `%LOCALAPPDATA%\LocalVault\stores.json`, with the path of its key file, if any. Every project
on this PC can use it by name. The list holds paths only, never a password. The target and key file
must exist. A path that contains an environment variable is stored as typed and expanded each time
it's used. When this PC still has a `keepass.json` from an earlier version, its databases are carried
over into `stores.json` the first time; `keepass.json` itself isn't changed.

The cmdlet also records the database's internal id, which stays the same when the file moves or is
renamed. `Export-VaultToStore` writes the id into the unencrypted header of the database on every
save, so the cmdlet reads it there without a password. A database that LocalVault has never saved is
opened once to read the id. Without a console, the store is added without it, and the id is recorded
the first time the store opens.

Every opening checks the id. A file that another database replaced is refused until the store is
added again.

With **Project**, the store belongs to the project: the cmdlet writes it into `localvault.json` at the
root of the git repository, so every clone has it. A project entry is either a name and a target,
which every clone uses, or, with **Name** and no **Target**, a name only: a reference to the store
that each PC defines under that name. LocalVault reads this PC's stores first, then the project's, so
a name defined in both places takes the project's target. A project store can't name a key file or
an unlocking store; those are personal. Define the same target on this PC with them instead:
LocalVault takes the key file, the unlocking store, and the path when the project's path isn't
reachable here, from the PC's store with the same id or target.

With **Default**, the store becomes the default: this PC's default for vaults that `localvault.json`
doesn't assign, or with **Project**, the project's primary store, which `Import-VaultFromStore` reads
(see `Set-VaultProjectStore`).

With **Mirror**, the store is a mirror: every `Export-VaultToStore` (`lv save`) also writes the shared
entries there, in addition to the primary store. A mirror of this PC receives every vault saved on
this PC, whatever the project, such as a private copy of all your projects; a mirror of the project
receives the project's vaults, on every PC that can reach it. A mirror that can't be opened gives a
warning and the save continues. `Import-VaultFromStore` doesn't read mirrors unless you name one with
its **Store** parameter.

With **Personal**, the store becomes this PC's personal store: `Export-VaultToStore` writes each
value of a required entry set on this PC there, in the same group as the vault's group in the project
store, and `Import-VaultFromStore` fills in the required values from it. A PC has at most one
personal store; naming another one replaces it. A personal store that is also a mirror holds a copy
of the shared entries as well. **Personal** isn't accepted with **Project**.

With **Project** and **Source**, the store becomes a source of shared values: a shared entry may take
its value from one of its entries with `Set-VaultEntry -Reference -ReferenceStore`. The project file
records the store's database id, which LocalVault reads from the database header or by opening the
store once. A shared value comes only from the vault's store or from a source store. The personal
store and a store that unlocks other stores are never a source, even when marked.

With **UnlockWith**, the store is opened with a password kept in another store of this PC, usually
your personal KeePass database: LocalVault opens that other store first, asking for its password,
and looks in its `AutoOpen` group for this database, by its internal id, then by its path. That group
follows the convention that KeePassXC and the KeeAutoExec plugin of KeePass 2 also use: each entry
has the path of a database in its URL (absolute, relative to the holding database, or with
`{DB_DIR}`), its master password as password, the path of its key file, if any, as user name, and
optionally an `IfDevice` field. When the group doesn't hold the password yet, LocalVault asks for it,
then offers to keep it there. The unlocking store may itself be unlocked by another one; a store that
would end up unlocking itself is refused. The unlocking store is opened only when a store that names
it is needed.

When the name is already defined at that level, the cmdlet replaces its target, key file, unlocking
store and mirror flag; a store added again without **UnlockWith** asks for its own password from then
on, and one added again without **Mirror** is no longer a mirror.

## EXAMPLES

### Example 1 - Define the team database on this PC and make it the default

```powershell
Register-VaultStore -Name team -Target D:\KeePass\it.kdbx -Default
```

```Output
Name Scope    Type    Target             KeyFile Default UnlockWith Exists
---- -----    ----    ------             ------- ------- ---------- ------
team Computer keepass D:\KeePass\it.kdbx           True              True
```

### Example 2 - Define the project's store for every clone

```powershell
Register-VaultStore -Name proj -Target I:\Projects\X\x.kdbx -Project -Default
git add localvault.json
```

### Example 3 - Refer to the team store that each PC defines, and keep an archive mirror

```powershell
Register-VaultStore -Name team -Project -Default
Register-VaultStore -Name archive -Target I:\Archive\proj.kdbx -Project -Mirror
git add localvault.json
```

Each PC defines its own `team` store, with its own path and key file. Every save writes `team` and
`I:\Archive\proj.kdbx`.

### Example 4 - Copy every project you save into your personal database

```powershell
Register-VaultStore -Name mine -Target '%OneDrive%\KeePass\mine.kdbx' -KeyFile E:\mine.keyx -Mirror
```

### Example 5 - Keep your own values and a copy of every project in your personal database

```powershell
Register-VaultStore -Name mine -Target '%OneDrive%\KeePass\mine.kdbx' -Mirror -Personal
```

### Example 6 - Open the team database with the password kept in your personal one

```powershell
Register-VaultStore -Name mine -Target '%OneDrive%\KeePass\mine.kdbx' -KeyFile E:\mine.keyx
Register-VaultStore -Name team -Target D:\KeePass\it.kdbx -UnlockWith mine
```

LocalVault asks for the password of `mine` only, once the `AutoOpen` group of `mine` holds the
password of `team`. The first time, it asks for both, and offers to keep the password of `team`
there.

## PARAMETERS

### -Default

Indicates that this store becomes the default of this PC, or with **Project**, of the project.

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

### -KeyFile

Specifies the key file of the database. Not allowed with **Project**.

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

### -Mirror

Indicates that the store is a mirror: every save also writes it, in addition to the primary store.
On this PC, it mirrors every vault saved here; with **Project**, the project's vaults.

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

### -Name

Specifies the name of the store. Use letters, digits and `.`, `_`, `-`. If you omit this
parameter, the name is the target's file name without its extension. With **Project** and no
**Target**, the name refers to the store of that name defined on each PC.

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

### -Personal

Indicates that the store is this PC's personal store: it keeps your own values of required entries.

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

Indicates that the store is defined in the project's `localvault.json` instead of on this PC.

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

### -Source

Indicates that shared entries of the project may take their values from this store. Accepted with
**Project** only.

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

### -Target

Specifies the store: the path of a KeePass `.kdbx` file. A relative path is resolved from the
current location. Required, except with **Project** and **Name**, which refers to each PC's store of
that name.

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

### -UnlockWith

Specifies the name of another store on this PC that holds this database's master password in its
`AutoOpen` group. LocalVault opens that store first, then this one with the password found there.
Without this parameter, LocalVault asks for this database's password. Not allowed with **Project**.

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

This cmdlet returns the store, as `Get-VaultStore` does.

## NOTES

`lv store add [<target>] [-Name <name>] [-KeyFile <file>] [-Default] [-UnlockWith <store>] [-Mirror]
[-Personal] [-Project] [-Source]` runs this cmdlet.

## RELATED LINKS

- [Get-VaultStore](Get-VaultStore.md)
- [Unregister-VaultStore](Unregister-VaultStore.md)
- [Set-VaultProjectStore](Set-VaultProjectStore.md)
- [Export-VaultToStore](Export-VaultToStore.md)
