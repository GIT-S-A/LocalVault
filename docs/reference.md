# LocalVault reference

The LocalVault module stores the secrets and files of a project in one vault file in the project
folder. The file is sealed with Windows DPAPI for the current user on the current machine. Exports
are sealed with a passphrase. Shared entries are saved to a store, a KeePass database, defined on the PC or in the
project, where the team reads them and from which a new machine loads them.

The module requires Windows and PowerShell 7.6 or later. For concepts, see
[about_LocalVault](#concepts).

## Contents

- [Concepts](#concepts)
- [Add-VaultTemplate](#add-vaulttemplate): Turns a file that holds secrets into a committed template.
- [Clear-Vault](#clear-vault): Deletes a vault file and its previous version.
- [Clear-VaultTemplate](#clear-vaulttemplate): Deletes the files written from templates.
- [Compare-VaultWithStore](#compare-vaultwithstore): Compares the entries of a vault with its store.
- [Copy-VaultEntry](#copy-vaultentry): Copies a secret to the clipboard for a short time.
- [Copy-VaultToStore](#copy-vaulttostore): Copies the shared entries of a vault once to another store.
- [Export-Vault](#export-vault): Exports every entry of a vault to a file sealed with a passphrase.
- [Export-VaultToStore](#export-vaulttostore): Saves the shared entries of the repository's vaults to their stores.
- [Get-VaultEntry](#get-vaultentry): Gets the value of one vault entry.
- [Get-VaultEntryList](#get-vaultentrylist): Gets the names, kinds and ages of the entries in a vault.
- [Get-VaultHelloStatus](#get-vaulthellostatus): Gets the state of the Windows Hello key on this PC and of the vault's Windows Hello entries.
- [Get-VaultSshKey](#get-vaultsshkey): Gets the SSH keys of a vault, without their private keys.
- [Get-VaultSshKeyPath](#get-vaultsshkeypath): Gets the path of the key file of an SSH key entry.
- [Get-VaultStore](#get-vaultstore): Gets the stores of the project and of this PC.
- [Get-VaultStoreStatus](#get-vaultstorestatus): Gets the store of each vault of the repository and what isn't saved yet.
- [Get-VaultTemplate](#get-vaulttemplate): Gets the templated files of the repository and their state.
- [Import-Vault](#import-vault): Merges entries from an export, a backup or a `secrets.local.clixml` file into a vault.
- [Import-VaultFromStore](#import-vaultfromstore): Loads the shared entries of the repository's vaults from their stores.
- [Invoke-LocalVault](#invoke-localvault): Runs a LocalVault command from a short command line.
- [New-VaultSshKey](#new-vaultsshkey): Creates an SSH key pair for the hosts of a project and stores it in the vault.
- [Register-VaultStore](#register-vaultstore): Defines a store on this PC or in the project.
- [Remove-VaultEntry](#remove-vaultentry): Deletes one entry from a vault.
- [Remove-VaultTemplate](#remove-vaulttemplate): Stops managing a templated file.
- [Resolve-VaultEntry](#resolve-vaultentry): Gets a secret for a scope, or the shared secret when the scope has none.
- [Restore-VaultFile](#restore-vaultfile): Writes file entries of a vault back to disk.
- [Set-VaultEntry](#set-vaultentry): Adds or replaces a secret, a credential or a file in a vault.
- [Set-VaultProjectStore](#set-vaultprojectstore): Names the project's primary store.
- [Set-VaultSshKeyHost](#set-vaultsshkeyhost): Sets the hosts an SSH key of the vault is for.
- [Set-VaultSshTools](#set-vaultsshtools): Sets or shows the OpenSSH folder that LocalVault uses on this PC.
- [Set-VaultStore](#set-vaultstore): Changes one setting of a store without defining it again.
- [Unregister-VaultStore](#unregister-vaultstore): Removes a store from this PC or from the project.
- [Update-VaultTemplate](#update-vaulttemplate): Takes the edits of templated files back into their templates.
- [Use-VaultEnv](#use-vaultenv): Copies vault secrets into environment variables of the current process.
- [Write-VaultTemplate](#write-vaulttemplate): Writes templated files from their templates and the vault.

## Concepts

A LocalVault vault is one file in a project folder that holds secrets and
files. The file is sealed with Windows DPAPI for the current user on the
current machine. Other users and other machines can't open it.

### Vault path

The cmdlets use the first of these paths:

1. The **Path** parameter.
1. The `LOCALVAULT_PATH` environment variable.
1. `secrets.local.lv` at the root of the git repository that contains the
   current location.

A relative path is resolved from the current location.

### Vault files

| File | Content |
| --- | --- |
| `secrets.local.lv` | The vault. |
| `secrets.local.lv.bak` | The vault before the last write. |
| `secrets.local.lv.lock` | The lock of a write in progress. |
| `secrets.local.clixml` | A vault in the `Export-Clixml` format. |
| `secrets.local.clixml.migrated-<time>` | A `secrets.local.clixml` file after migration. |

Git must ignore these files. The `.gitignore` line `secrets.local.*` covers
all of them.

A write creates the vault when it doesn't exist. It fails when git doesn't
ignore the vault file. At the default path, it also fails when the
repository has a vault file in another folder, and asks before it creates
the vault. A repository can hold several vaults, each created with **Path**.
Without a console, it fails; pass **Path** to create the vault.

### Entries

An entry is a secret, a credential or a file. A secret holds a value. A
credential holds a user name, a password, an optional TOTP and URLs, as a
KeePass entry does. A file holds the content of a file and a path relative to
the vault folder. Every entry records its creation time. `Restore-VaultFile`
writes file entries back to disk. Inside a git repository, git must ignore
each file it writes.

`<name>#user`, `#password`, `#totp`, `#url`, `#url2` and so on name one part of
a credential, wherever a name is read: `Get-VaultEntry`, `Copy-VaultEntry`,
`Use-VaultEnv`, `Resolve-VaultEntry`, `Set-VaultEntry` and templates. The
name alone is the password. `#totp` is the current code; the seed stays in the
vault. User names and URLs aren't secrets. In KeePass, a credential uses the
UserName, Password and URL fields, `KP2A_URL_<n>` for more URLs, and both
KeePass 2's `TimeOtp-*` fields and KeePassXC's `otp` field for the TOTP. A
vault with credentials needs 1.0.0-beta12 or later.

### Names

A name is `key` or `scope/key`. Names contain letters, digits and the
characters `.`, `_`, `:`, `-`, `@` and `+`, with at most one `/`, so an email
address is a valid key. A vault that uses `@` or `+` needs 1.0.0-beta6 or
later. Names aren't
case-sensitive. The module keeps the case of a name as written.

`Resolve-VaultEntry` and `Use-VaultEnv -Scope` look up `<scope>/<key>`, then
`common/<key>`.

### Writes

A write takes the lock file, reads the vault, applies the change, writes a
new file, verifies it, and replaces the vault. A second writer waits up to
10 seconds for the lock. Entries that the write doesn't name don't change.
When a write fails, the vault doesn't change.

Reads don't change the vault.

### Migration

When a vault named `secrets.local.lv` doesn't exist and its folder contains
`secrets.local.clixml`, the cmdlets read that file. The first write moves its
entries into the vault and renames it to
`secrets.local.clixml.migrated-<time>`. Git must ignore the new vault file. An
entry named `__File:<name>` becomes a file entry. Other entries whose names
start with `__`, such as SSH keys, and file names that aren't valid stored
paths, aren't migrated.

When both files exist and the old file holds entries that the vault doesn't,
reads use the vault and write a warning, and writes fail. Merge the old file
with `Import-Vault -Source`.

### Format versions

Every LocalVault release reads the vaults, exports and backups of all earlier
releases. A write converts the vault to the format of the running module and
keeps the previous file as `<vault>.bak`. The cmdlet reports the conversion.

An earlier module can't change a vault, an export or a backup in a newer
format. It fails with the message
`It was written by a newer LocalVault. Update the module.` and writes
nothing.

### Exports

`Export-Vault` writes every entry to a file sealed with a passphrase of at
least 12 characters. `Import-Vault` merges an export into a vault: it adds
missing entries, skips identical entries, and asks about entries that
differ. It doesn't delete entries. `Import-Vault` also reads `SAAS-VAULT-1`
exports.

### Windows Hello

An entry can also be sealed with Windows Hello: `Set-VaultEntry -Hello`,
`lv set <name> --hello`, or `lv mark <name> hello` for an existing entry.
Reading its value then needs a Windows Hello gesture. Secrets, credentials
and files can be sealed; SSH keys can't.

- The seal sits inside the DPAPI seal of the vault file.
- Each Windows account has one Windows Hello key, kept by the Microsoft
  Passport Key Storage Provider. The first seal on a PC creates it, after one
  Windows Hello prompt.
- Sealing never asks. A command that reads Windows Hello entries asks once,
  whatever their number. The prompt names the vault and the entries.
- Windows Hello always offers the PIN beside face and fingerprint. A seal
  that accepts only biometrics isn't possible with this key provider.
- Without a console, in a remote desktop session, or when Windows Hello
  isn't set up, reads fail and nothing is asked.
- A key without a TPM gives a warning in each command that seals. `Get-VaultHelloStatus`
  (`lv hello`) shows the key state and the entries this PC can't read.

After a PIN reset, a Windows Hello reset or a copy of the vault to another PC,
the key no longer opens the vault's entries. They are listed as `Missing`.
`lv load` takes them from the store and seals them again with the current
key; the ones that the store doesn't hold stay `Missing` and are reported.

| Operation | Windows Hello prompt |
| --- | --- |
| `lv list`, `lv hello`, `lv start`, `lv store diff` | None |
| `lv set`, `lv load`, `lv import` | None, except to create the key |
| `lv set <name>#<part>` on a sealed credential | One |
| `lv save`, `lv store copy` | One per vault, when a sealed entry is written to the store |
| `lv export` | One, when the vault holds a sealed entry |
| `lv get`, `lv clip`, `lv env`, `lv restore`, `lv template render` | One per command |
| `lv run` | One each for the variables, the templated files and `--stdin`, when they read a sealed entry |

A store holds a sealed entry as an ordinary KeePass entry of its kind, with
the custom string `LocalVault.Hello` set to `true`. `lv load` seals every
entry that carries it. An entry sealed on this PC stays sealed when the store
lacks the flag; `lv save` then sets it. `lv mark <name> nohello` removes the
flag at the next `lv save`. A declaration made with `--hello` carries
`LocalVault.RequiredHello`, and each person's value is then sealed.

An export holds sealed entries in plain form, marked; `Import-Vault` seals
them again, and fails without writing when Windows Hello is unavailable.

A vault with a Windows Hello entry needs 1.0.0-beta17 or later. Earlier
releases read the vault but don't change it, and leave its sealed entries
out of `lv save` and `lv load`.

### Old backups

LocalVault makes no automatic backups. Releases up to 1.0.0-beta14 wrote
them, by default to `%OneDrive%\LocalVault\<project>-<id>-<machine>`, sealed
with a key kept in the vault. Every command that opens a vault looks for
them: the folder named by the backup settings in the vault, and the vault's
default folder. In a console, the command asks once per session whether to
delete them; on yes, it deletes the files, the folders left empty and the
backup key, including the copy in the vault's previous generation
(`<vault>.bak`). Without a console, it writes a warning. `Clear-Vault` deletes
them with the vault.

`Import-Vault` restores an old backup until it is deleted.

### Stores

A store holds the entries that a team shares, beyond this PC. Today a store
is a KeePass database. Each entry is shared unless it was set with `-Local`
or changed with `Set-VaultEntry -Sharing Local`. `Export-VaultToStore`
(`lv save`) writes the shared entries as ordinary KeePass entries, readable
in KeePass, in the group `LocalVault/<repository>[/<folder>]`, the scope kept
in the title. `Import-VaultFromStore` (`lv load`) reads them back. Both ask
for the master password and never keep it.

The vault remembers, for each entry, the value that it last had in the store.
A change made on one side only goes to the other side without a question; a
change made on both sides is asked about. Removals go both ways, and a
removal in the store is asked about. KeePass keeps earlier values in each
entry's history.

Stores are defined at two levels:

- Common stores belong to this PC, for the current Windows account, in
  `%LOCALAPPDATA%\LocalVault\stores.json`: the team database, your personal
  one. Every project on this PC can use them by name. The databases of an
  earlier `keepass.json` are carried over the first time.
- Project stores belong to the project, in `localvault.json` at the root of
  the repository. Every clone has them. A project entry is a name and a
  target, or a name only: a reference to the common store of that name on
  each PC. They hold no key file and no password; a common store with the
  same id or path supplies the key file and the store that unlocks it.

LocalVault reads the common stores first, then the project's; a name defined
at both levels takes the project's target. The project names one primary
store, which `lv load` reads.

Any store, at either level, can be a mirror (`-Mirror`, `--mirror`): every
`lv save` also writes it, after the primary store. A mirror of this PC
receives every vault saved on this PC, such as a private copy of all your
projects; a mirror of the project receives the project's vaults on every PC
that defines it. In a mirror, the vault's value always wins; changes made
directly in a mirror are overwritten and reported, entries found only there
are left and reported. A mirror that can't be reached gives a warning and the
save goes on. `lv load` never reads mirrors, unless one is named with
`--store`. Each KeePass file is opened and saved at most once per run, and
each vault written once.

```powershell
Register-VaultStore -Name team -Target D:\KeePass\team.kdbx -Default   # lv store add team <kdbx> --default
Register-VaultStore -Name mine -Target D:\KeePass\mine.kdbx -Mirror   # lv store add mine <kdbx> --mirror
Register-VaultStore -Name team -Project -Default                        # lv store add team --project --default
Set-VaultProjectStore team                                              # lv store use team
Export-VaultToStore                                                     # lv save
Import-VaultFromStore                                                   # lv load, on another machine or for a teammate
```

`localvault.json` also lists the vaults of the repository, and the store of
each: LocalVault adds a vault each time it writes it, and
`Set-VaultProjectStore` names the project's primary store, or, rarely, a
vault's own primary store, by name and by internal id. Commit it. A vault
uses, in this order: the store given with **Store**; its own store in
`localvault.json`; the project's primary store; the default store of this PC;
the only store of this PC. A
store named in `localvault.json` is found among the project's stores, then
among this PC's stores by its id, then by name. A database whose id differs
from the recorded one is refused.

`lv save` and `lv load` handle every vault of the repository at once, opening
each store once. A vault whose store isn't defined on this PC is skipped with
a message. When a command run in a console needs an entry or a vault that this
PC doesn't have, LocalVault loads the repository's vaults from their stores
first, after asking for the password. Without a console, it doesn't.

Each store asks for its own password, unless it names the store that holds
it: `Register-VaultStore -UnlockWith <store>` (`lv store add ... --unlock-with
<store>`). LocalVault then opens that store first and takes the password from
its `AutoOpen` group, the convention that KeePassXC and KeePass 2's
KeeAutoExec plugin use. The unlocking store can be unlocked by another one in
turn, so that a single password, usually your personal database's, opens all
of them. A store's password typed once can be kept in its unlocking store's
`AutoOpen` group; LocalVault asks first. A `keyring` set by beta7 becomes the
unlocking store of every other store of this PC.

KeePass access uses KeePassLib, the library of KeePass 2, built into the
module. No other KeePass program is needed.

### Personal values

`Set-VaultEntry -Required` (`lv require <name>`) declares an entry whose
value each person sets: the store gets the declaration and its note, never a
value. With `-Credential` (`--credential`), the value is a user name and a
password. With `-StoredPath <path>` (`--file <path>`), the value is a file,
such as an SSH private key, and the path says where it goes; `~/` is the
user's profile, as in `~/.ssh/id_ed25519`. Only these personal values can be
written under the profile. `lv load` writes them there when the file is
missing, readable by the user, SYSTEM and Administrators only, and never
replaces an existing file.

A PC can name one personal store (`Register-VaultStore -Personal`,
`lv store set <name> --personal`), usually the person's own KeePass
database. `lv save` then writes each value of a required entry set on this
PC to that store, in the same group as in the project store, with the same
rules as shared entries. `lv load` reads them back. A personal store that is
also a mirror holds a copy of the shared entries as well.

After it loads, `lv load` asks in a console for each required value still
missing on this PC, showing its note; an empty answer skips it. It then
offers to save the values typed to the personal store, or warns that they
stay on this PC when there is none. A command that reads a missing required
value asks for it the same way. Without a console, the missing names are
listed and nothing is asked.

When the personal store is also a vault's primary store, everyone who opens
that database can read the values. `lv save` warns and asks first; the
declaration and the value then share one KeePass entry. Without a console,
the values are left out of it.

A vault with a credential declaration needs 1.0.0-beta16 or later.

### Files that need secrets

Some tools read their secrets from a file: a `.env` file, `clouds.yaml`,
`terraform.tfvars`, a cloud-init file. LocalVault keeps such a file as a
template: `<file>.template`, committed beside the real file, holds the
structure, with a placeholder `{{name}}` (or `{{scope/key}}`) for each secret.
The real file must be ignored by git.

- `lv template add <file> [--capture KEY,...]` (`Add-VaultTemplate`) makes the
  template from the working file, replacing every vault value with its
  placeholder; `--capture` moves the value of a `KEY=value` line into a new
  vault entry. Without a working file, it starts from `<file>.example`.
- `lv template render` (`Write-VaultTemplate`) writes the real files from
  their templates and the vault; `lv template clear` (`Clear-VaultTemplate`)
  deletes them. `Clear-Vault` deletes them with the vault.
- `lv template update` (`Update-VaultTemplate`) takes your edits of the real
  file back into its template. A secret changed in the file is stored in the
  vault only after you agree.
- A template never holds a vault value; LocalVault refuses to write one that
  would.

`lv run [VAR=name ...] -- <command>` sets the variables for the command only,
writes every templated file of the repository, runs the command, then deletes
the written files, even when the command fails. The plain-text files exist
only while the command runs:

```powershell
lv run OS_PASSWORD=OsPassword -- terraform apply
```

Prefer variables when the tool reads them: `docker compose` reads `${VAR}`
from the environment, and the OpenStack client reads `OS_PASSWORD`.

### Data loss

The secrets in a vault are lost when:

- the vault file is deleted, for example by `git clean -fdx`;
- the disk or the machine is lost;
- an administrator resets the Windows password of the user, or the user
  profile is rebuilt.

An export is the only other copy of the entries that aren't in a store, and of
the required values that aren't in a personal store.

### Scripts

Pin the module version:

```powershell
#Requires -Modules @{ ModuleName = 'LocalVault'; ModuleVersion = '1.0.0' }
```

Get secrets as a **SecureString**:

```powershell
$vault = Join-Path -Path $PSScriptRoot -ChildPath 'secrets.local.lv'
$password = Get-VaultEntry -Path $vault -Name GrafanaAdminPassword
$credential = [pscredential]::new('admin', $password)
```

Set environment variables for a tool:

```powershell
Use-VaultEnv -Path $vault -Map @{ OS_PASSWORD = 'OsPassword' }
```

The cmdlets write entry names to the output and the information stream,
never values.

### Without a console

In a process started with `-NonInteractive`, or with redirected standard
input, the cmdlets don't ask questions:

- A secret, a passphrase or the KeePass master password that isn't passed as
  a parameter is read from one line of redirected standard input. Without
  redirected input, the cmdlet fails.
- `Import-Vault` fails and writes nothing when an entry differs and neither
  **Replace** nor **ReplaceAll** names it.
- `Import-VaultFromStore` lists the required values still missing and asks
  for none.
- `Restore-VaultFile` keeps files that differ, unless you use **Overwrite**.

### Errors

All errors are terminating. The **FullyQualifiedErrorId** starts with one of
these IDs:

| Error ID | Cause |
| --- | --- |
| `LocalVault` | The operation isn't possible, for example a missing entry. |
| `LocalVault.ImportConflict` | An import has differences without an answer. |
| `LocalVault.File` | A file can't be read or written. |

### Tests

To use a temporary vault in tests, set `LOCALVAULT_PATH`:

```powershell
$folder = Join-Path -Path ([System.IO.Path]::GetTempPath()) -ChildPath (New-Guid)
$env:LOCALVAULT_PATH = Join-Path -Path $folder -ChildPath 'secrets.local.lv'
```

A vault outside a git repository doesn't need a `.gitignore` entry.

### lv

`lv` is the alias of `Invoke-LocalVault`. It runs the cmdlets from short
commands, such as `lv list`, `lv set <name>` and `lv env VAR=name`.
`lv --version` prints the installed release; `lv update` installs the newest
one, and does nothing when it is already installed.

## Add-VaultTemplate

Turns a file that holds secrets into a committed template.

### Syntax

```text
Add-VaultTemplate [-File] <string> [-Capture <string[]>] [-Path <string>] [-WhatIf] [-Confirm]
 [<CommonParameters>]
```

### Description

The `Add-VaultTemplate` cmdlet writes `<file>.template` beside a file that a tool needs with
secrets in it, such as a `.env` file, a `clouds.yaml` file or a `terraform.tfvars` file. The
template keeps the structure of the file, and replaces each secret with a placeholder `{{name}}`, or
`{{scope/key}}` for a scoped name. Commit the template; the real file stays out of git and is
written from the vault by `Write-VaultTemplate`. The cmdlet records the pair in `localvault.json`
at the root of the repository, together with the vault that fills the placeholders.

When the real file exists, git must ignore it. Every value of the vault that is at least 6
characters long becomes its placeholder. **Capture** names keys whose values aren't in the vault
yet: on a `KEY=value`, `KEY: value` or `export KEY="value"` line, the value moves into a new vault
entry named after the key, and the line keeps a placeholder.

When the real file doesn't exist but `<file>.example` does, the template starts from the example.
A line whose key is the name of a vault entry gets that entry's placeholder. A captured key also
gets a placeholder, and the vault declares it as a required value that each person sets with
`lv set <name>`.

The cmdlet never writes a template that contains a vault value. It fails when the template exists
already: edit the real file and run `Update-VaultTemplate` instead.

### Examples

#### Example 1 - Make a template of a .env file and move a token into the vault

```powershell
Add-VaultTemplate .\infra\.env -Capture OS_PASSWORD
git add infra/.env.template localvault.json
```

#### Example 2 - Start from a .env.example

```powershell
Add-VaultTemplate .env -Capture API_KEY
lv set API_KEY
```

### Parameters

#### -Capture

Specifies keys whose values move from the file into new vault entries named after the keys. With
an `.example` file, the keys are declared as required values instead.

| Property | Value |
| --- | --- |
| Type | System.String[] |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -Confirm

Prompts you for confirmation before running the cmdlet.

| Property | Value |
| --- | --- |
| Type | System.Management.Automation.SwitchParameter |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | False |
| Aliases | cf |

#### -File

Specifies the file that holds secrets. The template is written beside it with the `.template`
extension. The file must be inside the git repository.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | 0 |
| Required | (All) |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -Path

Specifies the path to the vault file, which also names the git repository whose templates the
cmdlet handles. A relative path is resolved from the current location. If you omit this
parameter, the cmdlet uses the `LOCALVAULT_PATH` environment variable, and then
`secrets.local.lv` at the root of the git repository that contains the current location.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -WhatIf

Shows what would happen if the cmdlet runs. The cmdlet isn't run.

| Property | Value |
| --- | --- |
| Type | System.Management.Automation.SwitchParameter |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | False |
| Aliases | wi |

### Inputs

**None**: You can't pipe objects to this cmdlet.

### Outputs

**LocalVault.PowerShell.VaultTemplateInfo**: This cmdlet returns the **Path** of the file, its **Template**, the **Vault** that fills it, whether
the file is **Rendered**, and **UpToDate**, which is empty here.

### Notes

`lv template add <file> [--capture KEY,...]` runs this cmdlet.

## Clear-Vault

Deletes a vault file and its previous version.

### Syntax

```text
Clear-Vault [-Path <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

### Description

The `Clear-Vault` cmdlet deletes the vault file and its previous version `<vault>.bak`. For a vault
named `secrets.local.lv`, it also deletes a `secrets.local.clixml` file in the same folder, and the
confirmation message names it.

The cmdlet asks for confirmation unless you use `-Confirm:$false`. It also deletes the vault's old
automatic backups: the files named by the backup settings kept in the vault, and every backup in
the vault's default folder `%OneDrive%\LocalVault\<project>-<id>-<machine>`. Files of other vaults
stay. Folders left empty are removed. When the vault file is already gone, the cmdlet still deletes
the backups in the default folder.

Files written from the repository's templates with this vault's values (see `Write-VaultTemplate`)
hold those values in plain text. In a console, the cmdlet asks whether to delete them too; without
a console, it deletes them. The templates stay.

### Examples

#### Example 1 - Delete the vault of the current repository

```powershell
Clear-Vault
```

```Output
Confirm
Are you sure you want to perform this action?
Performing the operation "Delete the vault, its previous generation and its 12 old automatic backup(s)" on target
"C:\src\obs\secrets.local.lv".
[Y] Yes  [A] Yes to All  [N] No  [L] No to All  [S] Suspend  [?] Help (default is "Y"): y
Deleted C:\src\obs\secrets.local.lv
Deleted 12 old automatic backup(s) in C:\Users\me\OneDrive\LocalVault\obs-1a2b3c4d-PC042
```

#### Example 2 - Delete a test vault without a question

```powershell
Clear-Vault -Path $env:TEMP\test\secrets.local.lv -Confirm:$false
```

### Parameters

#### -Confirm

Prompts you for confirmation before running the cmdlet.

| Property | Value |
| --- | --- |
| Type | System.Management.Automation.SwitchParameter |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | False |
| Aliases | cf |

#### -Path

Specifies the path to the vault file. A relative path is resolved from the current location. If
you omit this parameter, the cmdlet uses the `LOCALVAULT_PATH` environment variable, and then
`secrets.local.lv` at the root of the git repository that contains the current location.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -WhatIf

Shows what would happen if the cmdlet runs. The cmdlet isn't run.

| Property | Value |
| --- | --- |
| Type | System.Management.Automation.SwitchParameter |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | False |
| Aliases | wi |

### Inputs

**None**: You can't pipe objects to this cmdlet.

### Outputs

**None**: This cmdlet returns no output.

### Notes

The cmdlet doesn't delete the vault's entries in its store.

`lv clear` runs this cmdlet.

## Clear-VaultTemplate

Deletes the files written from templates.

### Syntax

```text
Clear-VaultTemplate [[-File] <string>] [-Path <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

### Description

The `Clear-VaultTemplate` cmdlet deletes the real files that `Write-VaultTemplate` writes, so that
their secrets don't stay on disk in plain text. The templates and the declarations in
`localvault.json` stay. Without **File**, it deletes every written file of the repository.

`Clear-Vault` deletes the files written from the vault it deletes, and `lv run` deletes the files it
wrote when its command ends.

### Examples

#### Example 1 - Remove the plain-text files after work

```powershell
Clear-VaultTemplate
```

### Parameters

#### -Confirm

Prompts you for confirmation before running the cmdlet.

| Property | Value |
| --- | --- |
| Type | System.Management.Automation.SwitchParameter |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | False |
| Aliases | cf |

#### -File

Specifies one templated file. Without it, the cmdlet deletes every written file of the repository.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | 0 |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -Path

Specifies the path to the vault file, which also names the git repository whose templates the
cmdlet handles. A relative path is resolved from the current location. If you omit this
parameter, the cmdlet uses the `LOCALVAULT_PATH` environment variable, and then
`secrets.local.lv` at the root of the git repository that contains the current location.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -WhatIf

Shows what would happen if the cmdlet runs. The cmdlet isn't run.

| Property | Value |
| --- | --- |
| Type | System.Management.Automation.SwitchParameter |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | False |
| Aliases | wi |

### Inputs

**None**: You can't pipe objects to this cmdlet.

### Outputs

**LocalVault.PowerShell.RenderedTemplate**: This cmdlet returns the **Path** of each deleted file and the **Action** `Deleted`.

### Notes

`lv template clear [<file>]` runs this cmdlet.

## Compare-VaultWithStore

Compares the entries of a vault with its store.

### Syntax

```text
Compare-VaultWithStore [-Store <string>] [-Path <string>] [<CommonParameters>]
```

### Description

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

### Examples

#### Example 1 - See what a save or a load would do

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

### Parameters

#### -Path

Specifies the path to the vault file. A relative path is resolved from the current location. If
you omit this parameter, the cmdlet uses the `LOCALVAULT_PATH` environment variable, and then
`secrets.local.lv` at the root of the git repository that contains the current location.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -Store

Specifies the name of a store, defined in the project's `localvault.json` or on this PC, to compare
with. If you omit this parameter, the cmdlet uses the vault's store.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

### Inputs

**None**: You can't pipe objects to this cmdlet.

### Outputs

**LocalVault.PowerShell.StoreEntryState**: This cmdlet returns one object for each entry, with its **Name**, **Kind** and **State**.

### Notes

`lv store diff [--store <name>]` runs this cmdlet.

## Copy-VaultEntry

Copies a secret to the clipboard for a short time.

### Syntax

```text
Copy-VaultEntry [-Name] <string> [-Seconds <int>] [-Path <string>] [<CommonParameters>]
```

### Description

The `Copy-VaultEntry` cmdlet puts the value of a secret entry on the Windows clipboard without
showing it. The copy is marked so that Windows keeps it out of the clipboard history and doesn't
sync it to other devices. After **Seconds**, or when the PowerShell session ends first, the cmdlet
empties the clipboard, unless something else was copied in the meantime. File entries are refused.

For a credential entry, the name alone copies the password; `<name>#user`, `<name>#url` or
`<name>#totp` copies that part, the TOTP as its current code.

### Examples

#### Example 1 - Paste a password into a login form

```powershell
Copy-VaultEntry GrafanaAdminPassword
```

```Output
GrafanaAdminPassword is on the clipboard for 20 s, kept out of the clipboard history.
```

### Parameters

#### -Name

Specifies the name of the secret or credential entry, optionally followed by a part:
`#user`, `#password`, `#totp`, `#url`, `#url2` and so on.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | 0 |
| Required | (All) |
| Pipeline input | by property name |
| Default value | None |
| Aliases | None |

#### -Path

Specifies the path to the vault file. A relative path is resolved from the current location. If
you omit this parameter, the cmdlet uses the `LOCALVAULT_PATH` environment variable, and then
`secrets.local.lv` at the root of the git repository that contains the current location.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -Seconds

Specifies how long the value stays on the clipboard, from 1 to 3600 seconds. The default is 20.

| Property | Value |
| --- | --- |
| Type | System.Int32 |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | 20 |
| Aliases | None |

### Inputs

**System.String**: You can pipe objects with a **Name** property to this cmdlet.

### Outputs

**None**: This cmdlet writes a message only.

### Notes

When the process ends abruptly, the clipboard isn't emptied.

`lv clip <name>` runs this cmdlet.

## Copy-VaultToStore

Copies the shared entries of a vault once to another store.

### Syntax

```text
Copy-VaultToStore [[-Target] <string>] [-KeyFile <string>] [-Store <string>] [-Path <string>]
 [<CommonParameters>]
```

### Description

The `Copy-VaultToStore` cmdlet writes every shared entry of the vault to another store, in the same
group and with the same layout as `Export-VaultToStore`. Give the store's file with **Target** (and
**KeyFile** when the database needs one), or a defined store with **Store**. The cmdlet asks for the
store's password and doesn't keep it.

It adds entries that the group doesn't have and overwrites the others; KeePass keeps the previous
values in each entry's history. It doesn't remove anything, and it leaves local entries out. It
writes the `Repository origin` and install instructions entries as `Export-VaultToStore` does.

The copy is one-off: the vault records nothing about it, so the next `Export-VaultToStore` and
`Import-VaultFromStore` with the vault's own store work as before.

Entries sealed with Windows Hello are written as `Export-VaultToStore` writes them, after one
Windows Hello prompt.

### Examples

#### Example 1 - Copy the shared entries to another KeePass file

```powershell
Copy-VaultToStore S:\shared\KeePass\team.kdbx
```

```Output
Opening store 'team' (KeePass S:\shared\KeePass\team.kdbx).
KeePass master password for team: ************
Copied 5 shared entries to S:\shared\KeePass\team.kdbx, group LocalVault/webshop. The vault records nothing about this copy.
```

#### Example 2 - Copy to a defined store

```powershell
Copy-VaultToStore -Store mine
```

### Parameters

#### -KeyFile

Specifies the key file of the database given with **Target**, when it needs one.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -Path

Specifies the path to the vault file. A relative path is resolved from the current location. If
you omit this parameter, the cmdlet uses the `LOCALVAULT_PATH` environment variable, and then
`secrets.local.lv` at the root of the git repository that contains the current location.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -Store

Specifies the name of a store, defined in the project's `localvault.json` or on this PC, to copy
to. Use either this parameter or **Target**.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -Target

Specifies the store to copy to, such as the path of a KeePass `.kdbx` file. A relative path is
resolved from the current location. Use either this parameter or **Store**.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | 0 |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

### Inputs

**None**: You can't pipe objects to this cmdlet.

### Outputs

**LocalVault.PowerShell.StoreCopySummary**: This cmdlet returns the **Target** path, the **Group**, and the number of entries **Written**.

### Notes

`lv store copy <target | name> [--keyfile <file>]` runs this cmdlet.

## Export-Vault

Exports every entry of a vault to a file sealed with a passphrase.

### Syntax

```text
Export-Vault [-Destination] <string> [-Passphrase <securestring>] [-Overwrite] [-Path <string>]
 [<CommonParameters>]
```

### Description

The `Export-Vault` cmdlet writes all secrets and files of the vault to one file in the
`LOCALVAULT-2` export format, sealed with a passphrase of at least 12 characters. `Import-Vault`
opens the file on any machine.

The cmdlet asks for the passphrase twice. Without a console, it reads the **Passphrase** parameter
or one line of redirected standard input. The cmdlet verifies the file after it writes it, and
records the time of the export in the vault.

An entry sealed with Windows Hello is written in plain form, marked as such. When the vault holds
one, the cmdlet asks for Windows Hello once. An entry that this PC can't unseal makes the cmdlet
fail before it writes the file; `Import-VaultFromStore` restores it first.

### Examples

#### Example 1 - Export the vault to a USB drive

```powershell
Export-Vault -Destination E:\handover\obs.export
```

```Output
Passphrase: at least 12 characters. It cannot be recovered; store it in KeePass now.
Passphrase: ****************
Passphrase again: ****************
Written: E:\handover\obs.export (7 entries). Keep it off this machine and out of git.
```

#### Example 2 - Overwrite an export with a passphrase from a variable

```powershell
$passphrase = Read-Host -Prompt 'Export passphrase' -AsSecureString
Export-Vault -Destination \\nas\keys\obs.export -Passphrase $passphrase -Overwrite
```

### Parameters

#### -Destination

Specifies the path of the export file. A relative path is resolved from the current location. The
cmdlet fails when the file exists, unless you use the **Overwrite** parameter.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | 0 |
| Required | (All) |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -Overwrite

Indicates that the cmdlet overwrites an existing export file.

| Property | Value |
| --- | --- |
| Type | System.Management.Automation.SwitchParameter |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | False |
| Aliases | None |

#### -Passphrase

Specifies the passphrase as a **SecureString** of at least 12 characters. If you omit this
parameter, the cmdlet asks for it twice.

| Property | Value |
| --- | --- |
| Type | System.Security.SecureString |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -Path

Specifies the path to the vault file. A relative path is resolved from the current location. If
you omit this parameter, the cmdlet uses the `LOCALVAULT_PATH` environment variable, and then
`secrets.local.lv` at the root of the git repository that contains the current location.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

### Inputs

**None**: You can't pipe objects to this cmdlet.

### Outputs

**None**: This cmdlet returns no output.

### Notes

The passphrase can't be recovered.

The cmdlet fails when the vault is empty.

`lv export <file> [--overwrite]` runs this cmdlet.

## Export-VaultToStore

Saves the shared entries of the repository's vaults to their stores.

### Syntax

```text
Export-VaultToStore [-Store <string>] [-Replace <string[]>] [-ReplaceAll] [-Path <string>]
 [<CommonParameters>]
```

### Description

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

When this PC has a personal store (`Register-VaultStore -Personal`), the cmdlet also writes there
each value of a required entry set on this PC, in the vault's group, with the same rules as shared
entries; the project store and the mirrors get the declaration only. A personal store that is also a
mirror gets the shared entries too. When the personal store is also the vault's primary store, the
cmdlet warns that everyone who opens it can read the values and asks first; the declaration and the
value then share one entry. Without a console, it leaves the values out. A personal store that can't
be opened or saved gives a warning and the save goes on. With **Store**, the cmdlet doesn't write the
personal store.

Each store is opened and saved at most once per run, whether it is the primary store of one vault, a
mirror of another, the personal store, or all of these; then each vault is written once, recording
what was saved to all its stores.

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

An entry sealed with Windows Hello is written as an ordinary KeePass entry of its kind, with the
custom string `LocalVault.Hello` set to `true`. The cmdlet asks for Windows Hello once per vault,
and only when such an entry must be written. An entry whose content is unchanged gets its flag set
or removed without a prompt. A sealed entry that this PC can't unseal isn't saved, and the cmdlet
writes a warning. A declaration made with **Hello** carries `LocalVault.RequiredHello`.

### Examples

#### Example 1 - Save every vault of the repository

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

#### Example 2 - Overwrite a value that a teammate changed too

```powershell
Export-VaultToStore -Replace common/db.password
```

### Parameters

#### -Path

Specifies the path to the vault file. A relative path is resolved from the current location. If
you omit this parameter, the cmdlet uses the `LOCALVAULT_PATH` environment variable, and then
`secrets.local.lv` at the root of the git repository that contains the current location.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -Replace

Specifies the names of entries changed both in the vault and in the store whose vault value replaces the store's value.

| Property | Value |
| --- | --- |
| Type | System.String[] |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -ReplaceAll

Indicates that the vault value replaces the store's value of every entry changed in both places.

| Property | Value |
| --- | --- |
| Type | System.Management.Automation.SwitchParameter |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | False |
| Aliases | None |

#### -Store

Specifies the name of a store, defined in the project's `localvault.json` or on this PC, that
every vault handled by the cmdlet uses. If you omit this parameter, each vault uses its own store.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

### Inputs

**None**: You can't pipe objects to this cmdlet.

### Outputs

**LocalVault.PowerShell.StoreSaveSummary**: This cmdlet returns the number of entries **Added**, **Updated** and **Removed** in the stores, and
the number **Skipped**.

### Notes

If KeePass 2 has the same database open, reload it there before saving, or it may overwrite these
changes.

`lv save [--store <name>] [--replace a,b | --replace-all]` runs this cmdlet.

## Get-VaultEntry

Gets the value of one vault entry.

### Syntax

```text
Get-VaultEntry [-Name] <string> [-AsPlainText] [-Path <string>] [<CommonParameters>]
```

```text
Get-VaultEntry [-Name] <string> -AsCredential [-Path <string>] [<CommonParameters>]
```

### Description

The `Get-VaultEntry` cmdlet gets the value of the named entry. For a secret entry, it returns a
**SecureString** by default, or a **String** when you use the **AsPlainText** parameter. For a file
entry, it returns the content of the file as a **Byte[]**.

For a credential entry, the name alone gets the password. Add a part to get another one:
`<name>#user` and `<name>#url` (or `#url2`, `#url3`...) return a **String**; `<name>#totp` returns the
current time-based code, computed from the seed kept in the vault, as a **SecureString** or a
**String**. The **AsCredential** parameter returns the user name and the password as a
**PSCredential**.

The cmdlet fails when the entry doesn't exist. To get `<scope>/<key>` with a fallback to
`common/<key>`, use `Resolve-VaultEntry`.

### Examples

#### Example 1 - Get a secret as a SecureString

```powershell
$password = Get-VaultEntry -Name GrafanaAdminPassword
$credential = [pscredential]::new('admin', $password)
```

#### Example 2 - Pass a secret to a native program on its standard input

```powershell
Get-VaultEntry -Name registry.token -AsPlainText |
    docker login registry.example.com --username ci --password-stdin
```

#### Example 3 - Get the content of a file entry

```powershell
$content = Get-VaultEntry -Name prod/clouds.yaml -Path .\infra\secrets.local.lv
Set-Content -Path $env:TEMP\clouds.yaml -Value $content -AsByteStream
```

To write a file entry to its stored path, use `Restore-VaultFile`.

#### Example 4 - Use the parts of a credential

```powershell
$credential = Get-VaultEntry -Name github -AsCredential
$code = Get-VaultEntry -Name github#totp -AsPlainText
Get-VaultEntry -Name github#url
```

### Parameters

#### -AsCredential

Indicates that this cmdlet returns the user name and the password of a credential entry as a
**PSCredential**. The entry must have a user name, and the name must not name a part.

| Property | Value |
| --- | --- |
| Type | System.Management.Automation.SwitchParameter |
| Parameter sets | Credential |
| Position | Named |
| Required | Credential |
| Pipeline input | False |
| Default value | False |
| Aliases | None |

#### -AsPlainText

Indicates that this cmdlet returns a secret, a password or a TOTP code as a **String** instead of a
**SecureString**. The parameter has no effect on a file entry, a user name or a URL.

| Property | Value |
| --- | --- |
| Type | System.Management.Automation.SwitchParameter |
| Parameter sets | Value |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | False |
| Aliases | None |

#### -Name

Specifies the name of the entry, as `key` or `scope/key`, optionally followed by a part of a
credential: `#user`, `#password`, `#totp`, `#url`, `#url2` and so on. The name isn't case-sensitive.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | 0 |
| Required | (All) |
| Pipeline input | by property name |
| Default value | None |
| Aliases | None |

#### -Path

Specifies the path to the vault file. A relative path is resolved from the current location. If
you omit this parameter, the cmdlet uses the `LOCALVAULT_PATH` environment variable, and then
`secrets.local.lv` at the root of the git repository that contains the current location.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

### Inputs

**System.String**: You can pipe an object that has a **Name** property to this cmdlet.

### Outputs

**System.Security.SecureString**: By default, this cmdlet returns a secret as a **SecureString**.

**System.String**: When you use the **AsPlainText** parameter, this cmdlet returns a secret as a **String**. A user
name and a URL are always returned as a **String**.

**System.Management.Automation.PSCredential**: When you use the **AsCredential** parameter, this cmdlet returns the user name and the password.

**System.Byte[]**: For a file entry, this cmdlet returns the content of the file.

### Notes

`lv get <name>` runs this cmdlet with **AsPlainText**. It fails when its output isn't a pipeline
or a redirection, unless you add `--show`, or the name ends with `#user` or `#url`.

## Get-VaultEntryList

Gets the names, kinds and ages of the entries in a vault.

### Syntax

```text
Get-VaultEntryList [[-Scope] <string>] [-Path <string>] [<CommonParameters>]
```

### Description

The `Get-VaultEntryList` cmdlet gets one **VaultEntryInfo** object for each entry in the vault,
sorted by name. Each object has the full name, the scope, the key, the kind (`secret` or `file`),
the store state (**Sync**), the creation time and the age of the entry. The objects don't contain
values. **Sync** is `Local` for an entry kept on this machine, `New` for a shared entry never saved to
the store, `Changed` when it changed since the last save or load, and `Saved` otherwise. A required personal value is `Missing` until it's set on this machine, then `Personal`; the object's **Note** says where to get it.

The cmdlet writes a warning when the vault has a store and shared entries aren't saved to it. It
also warns about the entries that exist only on this machine when the vault wasn't exported in the
last 30 days: local entries and personal values, or every entry when the vault has no store.

When the vault file doesn't exist and the same folder contains `secrets.local.clixml`, the cmdlet
reads that file.

An SSH key whose file another clone of the repository on this PC replaced with a newer key is updated in the vault first, and reported. The vault's key becomes the previous key when the clone kept it as `<file>.old`.

### Examples

#### Example 1 - List the entries of the vault of the current repository

```powershell
Get-VaultEntryList
```

```Output
Name    : common/db.password
Scope   : common
Key     : db.password
Kind    : secret
Sync    : Saved
Created : 06/10/2026 09:12:40 +00:00
Age     : 2.03:41:10.5237714

Name    : GrafanaAdminPassword
Scope   :
Key     : GrafanaAdminPassword
Kind    : secret
Sync    : Saved
Created : 01/09/2026 17:02:11 +00:00
Age     : 37.16:51:39.1408315
```

#### Example 2 - List the entries of one scope in a given vault as a table

```powershell
Get-VaultEntryList -Scope prod -Path .\infra\secrets.local.lv | Format-Table Name, Kind, Age
```

```Output
Name             Kind   Age
----             ----   ---
prod/clouds.yaml file   2.03:38:48.0911204
prod/db.password secret 2.03:40:02.7765131
```

The **Scope** parameter keeps only the entries whose name starts with `prod/`.

#### Example 3 - Find entries older than a year

```powershell
Get-VaultEntryList | Where-Object Age -gt (New-TimeSpan -Days 365) | Select-Object Name, Created
```

### Parameters

#### -Path

Specifies the path to the vault file. A relative path is resolved from the current location. If
you omit this parameter, the cmdlet uses the `LOCALVAULT_PATH` environment variable, and then
`secrets.local.lv` at the root of the git repository that contains the current location.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -Scope

Specifies a scope. The cmdlet gets only the entries named `<scope>/<key>`. The comparison isn't
case-sensitive. If you omit this parameter, the cmdlet gets every entry.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | 0 |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

### Inputs

**None**: You can't pipe objects to this cmdlet.

### Outputs

**LocalVault.PowerShell.VaultEntryInfo**: This cmdlet returns one object for each entry. The object has the **Name**, **Scope**, **Key**,
**Kind**, **Created** and **Age** properties. For a credential, the **User** property holds its user
name, which isn't a secret.

### Notes

`lv list [<scope>]` runs this cmdlet and formats the result as a table.

## Get-VaultHelloStatus

Gets the state of the Windows Hello key on this PC and of the vault's Windows Hello entries.

### Syntax

```text
Get-VaultHelloStatus [-Path <string>] [<CommonParameters>]
```

### Description

The `Get-VaultHelloStatus` cmdlet reports whether this PC can seal and read Windows Hello entries.
It never asks for Windows Hello.

The Windows Hello key is an RSA key of the current Windows user in the Microsoft Passport Key
Storage Provider, named `LocalVault-Hello`. Windows can't export it. The first entry sealed with
Windows Hello creates it, which asks for Windows Hello once.

**State** takes one of these values:

| State | Meaning |
|---|---|
| `Ready` | The key exists and is held by the TPM. |
| `Software` | The key exists but isn't held by a TPM. |
| `None` | The key doesn't exist yet. |
| `Unavailable` | Windows Hello can't be used: it isn't set up, the session is a remote desktop session, or the key provider fails. **Reason** says which. |
| `KeyChanged` | The vault's Windows Hello entries were sealed for another key. A PIN or Windows Hello reset, or another PC, replaces the key. |

**Missing** lists the vault's Windows Hello entries that this PC can't read. `Import-VaultFromStore`
seals them again from the store.

### Examples

#### Example 1 - Check Windows Hello before sealing an entry

```powershell
Get-VaultHelloStatus
```

```Output
State      : Ready
Hardware   : True
Provider   : Microsoft Passport Key Storage Provider
KeyId      : bc1838ae770b812b...
VaultKeyId : bc1838ae770b812b...
Entries    : 1
Missing    : {}
Reason     :
```

### Parameters

#### -Path

Specifies the path to the vault file. A relative path is resolved from the current location. If
you omit this parameter, the cmdlet uses the `LOCALVAULT_PATH` environment variable, and then
`secrets.local.lv` at the root of the git repository that contains the current location.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

### Inputs

**None**: You can't pipe objects to this cmdlet.

### Outputs

**LocalVault.PowerShell.VaultHelloStatus**: This cmdlet returns one object with the **State**, **Hardware**, **Provider**, **KeyId** of the
Windows Hello key, the **VaultKeyId** the vault's entries were sealed for, the number of Windows
Hello **Entries**, the names of the **Missing** ones and the **Reason** when Windows Hello is
unavailable.

### Notes

`lv hello` shows this status. A software key produces a warning.

Windows Hello always offers the PIN as well as face or fingerprint recognition. The key provider
can't require biometrics only.

## Get-VaultSshKey

Gets the SSH keys of a vault, without their private keys.

### Syntax

```text
Get-VaultSshKey [[-Name] <string>] [-Path <string>] [<CommonParameters>]
```

### Description

The `Get-VaultSshKey` cmdlet gets the SSH key entries of a vault: personal or shared, the type, the
fingerprint, the hosts, whether a previous key is kept, the key file and the public key. A personal key
not set on this PC has the source `missing`. The cmdlet never returns a private key.

An SSH key whose file another clone of the repository on this PC replaced with a newer key is updated in the vault first, and reported. The vault's key becomes the previous key when the clone kept it as `<file>.old`.

### Examples

#### Example 1 - List the keys

```powershell
Get-VaultSshKey
```

```Output
Name    Sharing  Source Type    Fingerprint                                        Hosts
----    -------  ------ ----    -----------                                        -----
backup  Shared   file   ed25519 SHA256:ygwjj+x/jcbcUNmwhazPNZzoRvNv7XDnLrscAy0ZQ0M {backup@nas.example.com}
deploy  Personal file   ed25519 SHA256:3tHh0kM9mHq9c8l3yS6Gm8yJb0Zr1y2Q4c7W5n8e1sA {deploy@srv1.example.com}
```

#### Example 2 - Get the public key of one key

```powershell
(Get-VaultSshKey -Name deploy).PublicKey
```

### Parameters

#### -Name

Specifies the name of an SSH key entry. If you omit this parameter, the cmdlet gets every SSH key of
the vault.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | 0 |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -Path

Specifies the path to the vault file. A relative path is resolved from the current location. If
you omit this parameter, the cmdlet uses the `LOCALVAULT_PATH` environment variable, and then
`secrets.local.lv` at the root of the git repository that contains the current location.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

### Inputs

**None**: You can't pipe objects to this cmdlet.

### Outputs

**LocalVault.PowerShell.VaultSshKeyInfo**: This cmdlet returns the key's **Name**, **Sharing** (`Personal` or `Shared`), **Source** (`file`,
`agent`, or `missing` for a personal key not set on this PC), **Type**, **Fingerprint** in the
`SHA256:` form of OpenSSH, **Hosts**, whether a **Previous** key is kept, the key file **Path** and
the **PublicKey**.

### Notes

`lv ssh-key` runs this cmdlet.

## Get-VaultSshKeyPath

Gets the path of the key file of an SSH key entry.

### Syntax

```text
Get-VaultSshKeyPath [-Name] <string> [-Path <string>] [<CommonParameters>]
```

### Description

The `Get-VaultSshKeyPath` cmdlet gets the full path of the current key file of an SSH key entry, for
tools that take the key with `-i`. For a key held by an SSH agent, the path is the `.pub` file. When
the file is missing, the cmdlet writes it first, with the same checks as
[Restore-VaultFile](#restore-vaultfile). In an interactive console, a personal key not set on this
PC is loaded from the stores first, as other reads do.

An SSH key whose file another clone of the repository on this PC replaced with a newer key is updated in the vault first, and reported. The vault's key becomes the previous key when the clone kept it as `<file>.old`.

### Examples

#### Example 1 - Run scp with the key

```powershell
scp -i (Get-VaultSshKeyPath -Name deploy) -o IdentitiesOnly=yes backup.tar deploy@srv1.example.com:
```

### Parameters

#### -Name

Specifies the name of the SSH key entry.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | 0 |
| Required | (All) |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -Path

Specifies the path to the vault file. A relative path is resolved from the current location. If
you omit this parameter, the cmdlet uses the `LOCALVAULT_PATH` environment variable, and then
`secrets.local.lv` at the root of the git repository that contains the current location.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

### Inputs

**None**: You can't pipe objects to this cmdlet.

### Outputs

**System.String**: This cmdlet returns the full path of the key file.

### Notes

`lv ssh-key path <name>` runs this cmdlet.

## Get-VaultStore

Gets the stores of the project and of this PC.

### Syntax

```text
Get-VaultStore [[-Name] <string>] [-Path <string>] [<CommonParameters>]
```

### Description

The `Get-VaultStore` cmdlet gets the stores of the project's `localvault.json`, then the stores
defined on this PC for the current Windows account, and whether each target exists. A project entry
that names a store without a target has the type `reference`; its target is the one of this PC's store
of that name, or `(not on this PC)`. Outside a git repository, the cmdlet gets this PC's stores only.
It opens no store.

### Examples

#### Example 1 - List the stores

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

#### Example 2 - Find stores whose file is missing

```powershell
Get-VaultStore | Where-Object -Not Exists
```

### Parameters

#### -Name

Specifies the name of a store. If you omit this parameter, the cmdlet gets all stores.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | 0 |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -Path

Specifies the path to the vault file. A relative path is resolved from the current location. If
you omit this parameter, the cmdlet uses the `LOCALVAULT_PATH` environment variable, and then
`secrets.local.lv` at the root of the git repository that contains the current location.
The vault's repository gives the project's stores.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

### Inputs

**None**: You can't pipe objects to this cmdlet.

### Outputs

**LocalVault.PowerShell.VaultStoreInfo**: This cmdlet returns one object for each store, with its **Name**, **Scope** (`Project` or
`Computer`), **Type** (`reference` for a project entry that names a store of each PC), **Target**,
**KeyFile**, whether it's the **Default** of its scope (for the project, its primary store), the store
that holds its password (**UnlockWith**), whether it's a **Mirror**, whether the target
**Exists**, and whether it's this PC's **Personal** store.

### Notes

`lv store list` runs this cmdlet.

## Get-VaultStoreStatus

Gets the store of each vault of the repository and what isn't saved yet.

### Syntax

```text
Get-VaultStoreStatus [-Store <string>] [-Path <string>] [<CommonParameters>]
```

### Description

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

### Examples

#### Example 1 - Check before leaving the machine

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

### Parameters

#### -Path

Specifies the path to the vault file. A relative path is resolved from the current location. If
you omit this parameter, the cmdlet uses the `LOCALVAULT_PATH` environment variable, and then
`secrets.local.lv` at the root of the git repository that contains the current location.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -Store

Specifies the name of a store, defined in the project's `localvault.json` or on this PC, as if it
were given to `Export-VaultToStore`. If you omit this parameter, each vault uses its own store.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

### Inputs

**None**: You can't pipe objects to this cmdlet.

### Outputs

**LocalVault.PowerShell.VaultStoreStatus**: This cmdlet returns one object for each vault, with its **Vault** (relative to the repository),
**Group**, **Store**, **Scope**, **Target**, **Problem**, **UnlockWith**, **Mirrors**, and the counts **Shared**,
**Saved**, **Pending**, **Local** and **RemovalsPending**.

### Notes

`lv store` without a subcommand shows this status.

## Get-VaultTemplate

Gets the templated files of the repository and their state.

### Syntax

```text
Get-VaultTemplate [-Path <string>] [<CommonParameters>]
```

### Description

The `Get-VaultTemplate` cmdlet lists the templated files recorded in `localvault.json`. For each
one, it says whether the real file is written on this PC and whether its content matches what
`Write-VaultTemplate` would write now. The cmdlet doesn't show any value.

### Examples

#### Example 1 - See which files are written

```powershell
Get-VaultTemplate | Format-Table Path, Rendered, UpToDate
```

### Parameters

#### -Path

Specifies the path to the vault file, which also names the git repository whose templates the
cmdlet handles. A relative path is resolved from the current location. If you omit this
parameter, the cmdlet uses the `LOCALVAULT_PATH` environment variable, and then
`secrets.local.lv` at the root of the git repository that contains the current location.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

### Inputs

**None**: You can't pipe objects to this cmdlet.

### Outputs

**LocalVault.PowerShell.VaultTemplateInfo**: This cmdlet returns the **Path** of each file, its **Template**, the **Vault** that fills it,
whether the file is **Rendered**, and **UpToDate**: `$true` or `$false` when the file is written,
empty when it isn't or when a value is missing.

### Notes

`lv template` runs this cmdlet.

## Import-Vault

Merges entries from an export, a backup or a `secrets.local.clixml` file into a vault.

### Syntax

```text
Import-Vault [-Source] <string> [-Passphrase <securestring>] [-KeyFile <string>]
 [-Replace <string[]>] [-ReplaceAll] [-Path <string>] [<CommonParameters>]
```

### Description

The `Import-Vault` cmdlet reads entries from a source and merges them into the vault. The cmdlet
recognizes the source from its content:

- A `LOCALVAULT-2` export from `Export-Vault`. The cmdlet asks for its passphrase.
- A `LOCALVAULT-2` backup (`.lvbak`). Without the **KeyFile** parameter, the cmdlet opens it with the
  backup key kept in this vault; with it, it also asks for the backup passphrase.
- A `SAAS-VAULT-1` export. `__File:<name>` entries become file entries. SSH keys and other names
  that start with `__` aren't imported; the cmdlet writes a warning for each.
- A `secrets.local.clixml` file of the current Windows account. When it is the file beside the
  vault and every entry ends up identical in the vault, the cmdlet renames it to
  `secrets.local.clixml.migrated-<time>`. It leaves any other `.clixml` file in place.


The cmdlet doesn't delete entries. It skips entries that are identical in both places, and adds
entries that are only in the source. For each entry that differs, it asks; the default answer
keeps the entry of the vault. The **Replace** and **ReplaceAll** parameters answer in advance.
When a source holds names that differ only by case, the cmdlet asks which one to import and says
when it replaces an entry of the vault. **ReplaceAll** doesn't choose among them; **Replace** takes
the exact name.
Without a console, when a difference has no answer, the cmdlet writes nothing and fails with the
error ID `LocalVault.ImportConflict`.

The cmdlet writes the vault once, after all answers.

An export or a backup whose content is damaged, such as an entry with a stored path outside the
vault folder, into a `.git` folder or onto a vault file, isn't imported.

An entry marked as sealed with Windows Hello in the export is sealed again in this vault, as is an
imported entry that replaces a sealed one. Sealing doesn't ask for Windows Hello, except to create
the key. When Windows Hello is unavailable, the cmdlet fails and writes nothing.

### Examples

#### Example 1 - Import an export from a colleague

```powershell
Import-Vault -Source E:\handover\obs.export
```

```Output
Passphrase for E:\handover\obs.export: ****************
'GrafanaAdminPassword' differs from the vault. Replace it with the imported value?
[Y] Yes  [N] No  [?] Help (default is "N"): y
From E:\handover\obs.export: 3 added, 1 replaced, 2 identical, 0 left as they were.

Added Replaced Identical Skipped Refused
----- -------- --------- ------- -------
    3        1         2       0       0
```

#### Example 2 - Go back to an old automatic backup of this vault

```powershell
$backup = 'C:\Users\me\OneDrive\LocalVault\obs-1a2b3c4d-PC042\20261008T101502117Z-0ec6df6e042491df.lvbak'
Import-Vault -Source $backup -ReplaceAll
```

#### Example 3 - Import in a script and replace two named entries

```powershell
$passphrase = Read-Host -Prompt 'Passphrase' -AsSecureString
$importVaultSplat = @{
    Source     = '.\obs.export'
    Passphrase = $passphrase
    Replace    = 'prod/db.password', 'prod/api.key'
}
Import-Vault @importVaultSplat
```

Other entries that differ keep their value in the vault.

#### Example 4 - Import a secrets.local.clixml file from another folder

```powershell
Import-Vault -Source .\tools\secrets.local.clixml -Path .\secrets.local.lv
```

After the import, the cmdlet renames the source file to `secrets.local.clixml.migrated-<time>`.

### Parameters

#### -KeyFile

Specifies the keyfile of a backup sealed with a key that this vault doesn't hold, such as a backup made in manual mode or by another vault.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | Source |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -Passphrase

Specifies the passphrase of the export or backup as a **SecureString**. If you omit this
parameter, the cmdlet asks for it, or reads one line from redirected standard input.

| Property | Value |
| --- | --- |
| Type | System.Security.SecureString |
| Parameter sets | Source |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -Path

Specifies the path to the vault file. A relative path is resolved from the current location. If
you omit this parameter, the cmdlet uses the `LOCALVAULT_PATH` environment variable, and then
`secrets.local.lv` at the root of the git repository that contains the current location.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -Replace

Specifies the names of entries that the imported value replaces when it differs. Other entries that
differ are asked about, or kept without a console.

| Property | Value |
| --- | --- |
| Type | System.String[] |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -ReplaceAll

Indicates that the imported value replaces every entry that differs, without asking.

| Property | Value |
| --- | --- |
| Type | System.Management.Automation.SwitchParameter |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | False |
| Aliases | None |

#### -Source

Specifies the file to import: an export, a backup, a `SAAS-VAULT-1` export or a
`secrets.local.clixml` vault.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | Source |
| Position | 0 |
| Required | Source |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

### Inputs

**None**: You can't pipe objects to this cmdlet.

### Outputs

**LocalVault.PowerShell.ImportSummary**: This cmdlet returns one object with the number of entries **Added**, **Replaced**, **Identical**,
**Skipped** (differing entries that kept their value) and **Refused** (entries that can't be
imported).

### Notes

When names in a `secrets.local.clixml` or `SAAS-VAULT-1` source differ only by case, the cmdlet
asks which one to import. Without a console, it fails unless **Replace** names one of them.

`lv import <file>` runs this cmdlet. `lv` exits with code `2` when an import has
unanswered differences.

## Import-VaultFromStore

Loads the shared entries of the repository's vaults from their stores.

### Syntax

```text
Import-VaultFromStore [-Store <string>] [-Replace <string[]>] [-ReplaceAll] [-Path <string>]
 [<CommonParameters>]
```

### Description

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

When this PC has a personal store (`Register-VaultStore -Personal`), the cmdlet then reads each
vault's group there and fills in the values of the vault's required entries, with the same rules as
shared entries. It takes nothing else from that group. A personal store that can't be opened gives a
warning and the load goes on.

Last, in a console, the cmdlet asks for each required value still missing, showing its note: a user
name and a password for a credential declaration, a hidden value otherwise. An empty answer skips it.
It then offers to save the values typed to the personal store, or warns that they stay on this PC
when there is none. Without a console, it lists the missing names and asks nothing. For a file
declaration, it asks for the path of the file; an empty answer takes the declared path when that
file exists.

Then the cmdlet writes each required file whose path is under the user's profile (`~/`), such as an
SSH private key, when the file doesn't exist yet, readable by the current user, SYSTEM and
Administrators only. It never replaces an existing file; a different one gives a warning, and
`Restore-VaultFile -Overwrite` replaces it.

An SSH key whose file another clone of the repository on this PC replaced with a newer key is updated in the vault first, and reported. The vault's key becomes the previous key when the clone kept it as `<file>.old`.

An entry with the custom string `LocalVault.Hello` set to `true`, and an entry already sealed in the
vault, is sealed with Windows Hello when it is taken. Sealing doesn't ask for Windows Hello, except
to create the key. When Windows Hello is unavailable, that entry isn't loaded, the cmdlet writes a
warning, and the other entries load. Entries sealed here that the current Windows Hello key can't
open, after a PIN reset or on another PC, are taken from the store whatever their state, and sealed
again with the current key; the ones that the store doesn't hold are reported.

### Examples

#### Example 1 - Get the shared entries of a project on a new machine

```powershell
Import-VaultFromStore
```

```Output
Opening store 'team' (KeePass D:\KeePass\team.kdbx).
KeePass master password for team: ************
secrets.local.lv <- store 'team': 5 added, 0 updated, 0 kept as they were, 0 removed.
infra/secrets.local.lv <- store 'team': 3 added, 0 updated, 0 kept as they were, 0 removed.
```

#### Example 2 - Take the store's value of an entry changed in both places

```powershell
Import-VaultFromStore -Replace common/db.password
```

### Parameters

#### -Path

Specifies the path to the vault file. A relative path is resolved from the current location. If
you omit this parameter, the cmdlet uses the `LOCALVAULT_PATH` environment variable, and then
`secrets.local.lv` at the root of the git repository that contains the current location.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -Replace

Specifies the names of entries changed both in the vault and in the store whose store value replaces the vault value.

| Property | Value |
| --- | --- |
| Type | System.String[] |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -ReplaceAll

Indicates that the store's value replaces the vault value of every entry changed in both places.

| Property | Value |
| --- | --- |
| Type | System.Management.Automation.SwitchParameter |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | False |
| Aliases | None |

#### -Store

Specifies the name of a store, defined in the project's `localvault.json` or on this PC, that
every vault handled by the cmdlet uses. If you omit this parameter, each vault uses its own store.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

### Inputs

**None**: You can't pipe objects to this cmdlet.

### Outputs

**LocalVault.PowerShell.StoreLoadSummary**: This cmdlet returns the number of entries **Added**, **Updated** from the stores, **Kept** as they
were, and **Removed**.

### Notes

`lv load [--store <name>] [--replace a,b | --replace-all]` runs this cmdlet. `lv` exits with code `2`
when entries changed in both places weren't resolved.

In a console, a command that reads an entry or a vault this PC doesn't have yet runs this cmdlet
first, keeping the local value of every entry changed in both places. A command that reads a
required value still missing asks for it, as this cmdlet does.

## Invoke-LocalVault

Runs a LocalVault command from a short command line.

### Syntax

```text
Invoke-LocalVault [-<command>] [-<arguments>] [-path <vault>] [-<options>] [<CommonParameters>]
```

### Description

The `Invoke-LocalVault` function runs a LocalVault cmdlet from a short command. Options start with
`--`. Every command takes `--path <vault>`. Without a command, or with `help`, `--help` or `-h`,
the function shows the installed release, lists the commands and names the vault that they would
use, without opening it. `--version` alone prints the installed release, such as
`LocalVault 1.0.0-beta11`.
`store` alone shows each vault's store and what
isn't saved to it yet, followed by a `Stores:` line that lists every store with its level and
flags, and `template` alone shows each templated file and whether it is written and up to date. `start` reads the vault, the repository and the stores, without opening a
store, and prints the state and the commands to run next, ready to paste. When `list` finds no entry,
it says that the vault is missing or empty.

| Command | Arguments and options | Cmdlet |
| --- | --- | --- |
| `help` | | |
| `start` | | where the project stands and the commands to run next |
| `list` | `[<scope>]` | `Get-VaultEntryList` |
| `get` | `<name>[#<part>] [--show]` | `Get-VaultEntry -AsPlainText` |
| `clip` | `<name>[#<part>]` | `Copy-VaultEntry` |
| `set` | `<name> [--file <path> \| --credential] [--local] [--hello]` | `Set-VaultEntry` (`-FilePath` with `--file`, `-Credential` with `--credential`, `-Hello` with `--hello`) |
| `set` | `<name>#<part>` | `Set-VaultEntry`: one part of a credential |
| `require` | `<name> [--credential \| --file <path>] [--note <text>] [--hello]` | `Set-VaultEntry -Required [-Credential \| -StoredPath <path>] [-Hello]` |
| `mark` | `<name> shared\|local` | `Set-VaultEntry -Sharing` |
| `mark` | `<name> hello\|nohello` | `Set-VaultEntry -Protection Hello\|Dpapi` |
| `hello` | | `Get-VaultHelloStatus` |
| `rm` | `<name>` | `Remove-VaultEntry` |
| `env` | `<variable>=<name> ...` | `Use-VaultEnv` |
| `restore` | `[<name> ...] [--overwrite]` | `Restore-VaultFile` |
| `clear` | | `Clear-Vault` |
| `save` | `[--store <name>] [--replace a,b] [--replace-all]` | `Export-VaultToStore`, every vault of the repository |
| `load` | `[--store <name>] [--replace a,b] [--replace-all]` | `Import-VaultFromStore`, every vault of the repository |
| `export` | `<file> [--overwrite]` | `Export-Vault` |
| `import` | `<file> [--replace a,b] [--replace-all] [--keyfile <file>]` | `Import-Vault` |
| `store` | | `Get-VaultStoreStatus` |
| `store list` | | `Get-VaultStore` |
| `store add` | `[<name>] <target> [<keyfile>] [--default] [--unlock-with <store>] [--mirror] [--personal]` | `Register-VaultStore`, a store of this PC |
| `store add` | `<name> [<target>] --project [--default] [--mirror]` | `Register-VaultStore -Project`; a name alone refers to each PC's store of that name |
| `store set` | `<name> [--mirror \| --no-mirror] [--unlock-with <store> \| --no-unlock] [--default] [--personal \| --no-personal] [--project]` | `Set-VaultStore`: changes those settings only |
| `store rm` | `<name> [--project]` | `Unregister-VaultStore` |
| `store use` | `<name>` | `Set-VaultProjectStore`: the project's primary store (one vault only with `--path`) |
| `store diff` | `[--store <name>]` | `Compare-VaultWithStore` |
| `store copy` | `<target \| name> [--keyfile <file>]` | `Copy-VaultToStore` |
| `template` | | `Get-VaultTemplate` |
| `template add` | `<file> [--capture KEY,...]` | `Add-VaultTemplate` |
| `template update` | `[<file>] [--accept]` | `Update-VaultTemplate` (`-AcceptChanges` with `--accept`) |
| `template render` | `[<file>]` | `Write-VaultTemplate` |
| `template clear` | `[<file>]` | `Clear-VaultTemplate` |
| `template rm` | `<file>` | `Remove-VaultTemplate` |
| `run` | `[<variable>=<name> ...] [--] <command> [<arguments> ...]` | sets the variables for the command only, writes every templated file of the repository, runs the command, then deletes the written files and restores the variables, even when the command fails; `$LASTEXITCODE` is the command's |
| `run` | `[<variable>=<name> ...] --stdin <name> [--] <program> [<arguments> ...]` | as `run`, and also writes the value to the program's standard input, then closes it: a secret as UTF-8, a file as its bytes, a credential's password; the value never becomes a string, a file or a word of the command line; the program must be an executable file |
| `ssh-key` | | `Get-VaultSshKey` |
| `ssh-key new` | `<name> [--host <user@host>]... [--shared] [--type ed25519\|rsa\|ecdsa] [--comment <text>] [--overwrite] [--replace]` | `New-VaultSshKey` (`-HostName` with each `--host`) |
| `ssh-key hosts` | `<name> [<user@host> ...]` | `Set-VaultSshKeyHost`; no host removes them |
| `ssh-key path` | `<name>` | `Get-VaultSshKeyPath` |
| `ssh-key tools` | `[<dir> \| --default]` | `Set-VaultSshTools` |
| `update` | `[<tag>] [--downgrade]` | downloads the newest GitHub release (or the tag), checks its SHA256 and signatures, and installs it with the installed module's `install.ps1`; says it is up to date and does nothing when that release is installed; refuses an older release without `--downgrade` |
| `--version` | | prints the installed release |

In `store add` with two words, the first is the target when it looks like one (a path, a `.kdbx`
file, or a `type:` prefix), and the second is then its key file; otherwise they are the name and the
target. With `--project` and one word that doesn't look like a target, the word is a store name that
refers to each PC's store of that name. `--mirror` makes the store a mirror: `save` also writes it. In `store copy`, a word that looks like a target is a target; otherwise it's a store name.

A credential keeps a user name, a password, a TOTP and URLs as one entry. `<name>#user`,
`#password`, `#totp`, `#url`, `#url2` and so on name one part in `get`, `clip`, `set`, `env`, `run`
and template placeholders (`{{github#user}}`); `#totp` is the current code, and the name alone is the
password.

`get` prints a value on screen only with `--show`, or when it's a user name or a URL; otherwise its output must be a pipeline or a redirection. A command given too many or too few
words, an option that it doesn't take, or an unknown subcommand fails, does nothing, and prints its
usage line. Secret values are never words of a command.

The function sets `$LASTEXITCODE` to `0` on success, `1` on an error, and `2` when an import has
unanswered differences, from a file or from a store.

### Examples

#### Example 1 - Store a secret in the vault of a subfolder

```powershell
lv set LOKI_PUSH_TOKEN --path infra/secrets.local.lv
```

`lv` asks for the value.

#### Example 2 - Set environment variables for a tool

```powershell
lv env OS_PASSWORD=OsPassword AWS_SECRET_ACCESS_KEY=S3SecretKey
```

#### Example 3 - Run docker compose with a templated .env that exists only while it runs

```powershell
lv run AZP_TOKEN=AZP_TOKEN -- docker compose up --build
```

#### Example 4 - Import from another shell and check the exit code

```powershell
pwsh -NoProfile -Command 'lv import handover.export --replace-all'
if ($LASTEXITCODE -ne 0) { throw "Import failed with exit code $LASTEXITCODE." }
```

### Parameters

#### -CommandLine

Specifies the command and its words and options, as typed after `lv`. Tab completion offers the
commands, their subcommands, the options that each command takes, entry names of the vault,
`shared` and `local` after `mark <name>`, and store names after `--store`, `store rm`, `store use`
and `store copy`.

| Property | Value |
| --- | --- |
| Type | System.String[] |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

### Inputs

**None**: You can't pipe objects to this function.

### Outputs

**System.Object**: This function returns the output of the cmdlet it runs. `list`, `store`, `store list` and `store diff` return
formatted tables.

### Notes

The function reads its words and `--` options as typed; options with one dash are PowerShell's common parameters.

## New-VaultSshKey

Creates an SSH key pair for the hosts of a project and stores it in the vault.

### Syntax

```text
New-VaultSshKey [-Name] <string> [-HostName <string[]>] [-Shared] [-Type <string>]
 [-Comment <string>] [-Overwrite] [-Replace] [-Path <string>] [-WhatIf] [-Confirm]
 [<CommonParameters>]
```

### Description

The `New-VaultSshKey` cmdlet runs `ssh-keygen` from Windows OpenSSH, or from the folder set with
[Set-VaultSshTools](#set-vaultsshtools), to create a key pair without a passphrase. The private key
becomes a vault entry and is written to `~/.ssh/<repository>/<name>`, readable by the current user,
SYSTEM and Administrators only, next to `<name>.pub`. `<repository>` is the last segment of the
`origin` URL without `.git`, or the folder name of the git root when there is no `origin`. A `/`
in the entry name becomes `.` in the file name, and `:` becomes `_`.

By default the key is personal: the entry is declared as a required value of shape `ssh-key`, the
declaration and its hosts go to the project's store, and the key goes only to the personal store of
each person. With `-Shared`, the key is one shared entry that every teammate loads from the
project's store.

The file `.localvault` in the key folder records the repository, the hash of each key and the vaults
that use it. The cmdlet refuses a folder that belongs to another repository, a file that LocalVault
didn't write, and a key that another clone of the repository wrote, unless `-Overwrite` is given.

An entry that already holds a key gets a new key pair only with confirmation, or with `-Replace`
outside a console. The replacement requires the store that carries the entry: the personal store for
a personal key, the project's store for a shared one. The old key becomes the entry's previous key,
written to `<name>.old`, and replaces any previous key kept before. The old public key stays
registered on the hosts.

The cmdlet prints the public key and copies it to the clipboard for 2 minutes. The key is usable once
the public key is added to the hosts it is for.

### Examples

#### Example 1 - Create a personal key for a host

```powershell
New-VaultSshKey -Name deploy -HostName deploy@srv1.example.com
```

#### Example 2 - Create a key shared by the team

```powershell
New-VaultSshKey -Name backup -HostName backup@nas.example.com -Shared
```

#### Example 3 - Create an RSA key for a host that refuses ed25519

```powershell
New-VaultSshKey -Name legacy -HostName root@old.example.com -Type rsa
```

#### Example 4 - Replace a key from a script

```powershell
New-VaultSshKey -Name backup -Replace
```

### Parameters

#### -Comment

Specifies the comment of the public key. The default is `<user>@<computer> <repository>/<name>`.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -Confirm

Prompts you for confirmation before running the cmdlet.

| Property | Value |
| --- | --- |
| Type | System.Management.Automation.SwitchParameter |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | False |
| Aliases | cf |

#### -HostName

Specifies the hosts the key is for, as `[user@]host[:port]`.

| Property | Value |
| --- | --- |
| Type | System.String[] |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -Name

Specifies the name of the entry.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | 0 |
| Required | (All) |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -Overwrite

Indicates that the cmdlet replaces a key file that LocalVault didn't write or that another clone
of the repository wrote.

| Property | Value |
| --- | --- |
| Type | System.Management.Automation.SwitchParameter |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | False |
| Aliases | None |

#### -Path

Specifies the path to the vault file. A relative path is resolved from the current location. If
you omit this parameter, the cmdlet uses the `LOCALVAULT_PATH` environment variable, and then
`secrets.local.lv` at the root of the git repository that contains the current location.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -Replace

Indicates that the cmdlet replaces the key of an entry that already holds one, without asking.

| Property | Value |
| --- | --- |
| Type | System.Management.Automation.SwitchParameter |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | False |
| Aliases | None |

#### -Shared

Indicates that the key is shared: its private key is readable by everyone with access to the
project's store.

| Property | Value |
| --- | --- |
| Type | System.Management.Automation.SwitchParameter |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | False |
| Aliases | None |

#### -Type

Specifies the key type: `ed25519` (the default), `rsa` (4096 bits) or `ecdsa`.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | ed25519 |
| Aliases | None |

#### -WhatIf

Shows what would happen if the cmdlet runs. The cmdlet isn't run.

| Property | Value |
| --- | --- |
| Type | System.Management.Automation.SwitchParameter |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | False |
| Aliases | wi |

### Inputs

**None**: You can't pipe objects to this cmdlet.

### Outputs

**LocalVault.PowerShell.VaultSshKeyInfo**: This cmdlet returns the key's **Name**, **Sharing** (`Personal` or `Shared`), **Source** (`file`,
`agent`, or `missing` for a personal key not set on this PC), **Type**, **Fingerprint** in the
`SHA256:` form of OpenSSH, **Hosts**, whether a **Previous** key is kept, the key file **Path** and
the **PublicKey**.

### Notes

`lv ssh-key new <name> [--host <user@host>]... [--shared] [--type <type>] [--comment <text>] [--overwrite] [--replace]`
runs this cmdlet.

## Register-VaultStore

Defines a store on this PC or in the project.

### Syntax

```text
Register-VaultStore [[-Target] <string>] [-Name <string>] [-KeyFile <string>] [-Default]
 [-UnlockWith <string>] [-Mirror] [-Personal] [-Project] [-Path <string>] [-WhatIf] [-Confirm]
 [<CommonParameters>]
```

### Description

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

### Examples

#### Example 1 - Define the team database on this PC and make it the default

```powershell
Register-VaultStore -Name team -Target D:\KeePass\it.kdbx -Default
```

```Output
Name Scope    Type    Target             KeyFile Default UnlockWith Exists
---- -----    ----    ------             ------- ------- ---------- ------
team Computer keepass D:\KeePass\it.kdbx           True              True
```

#### Example 2 - Define the project's store for every clone

```powershell
Register-VaultStore -Name proj -Target I:\Projects\X\x.kdbx -Project -Default
git add localvault.json
```

#### Example 3 - Refer to the team store that each PC defines, and keep an archive mirror

```powershell
Register-VaultStore -Name team -Project -Default
Register-VaultStore -Name archive -Target I:\Archive\proj.kdbx -Project -Mirror
git add localvault.json
```

Each PC defines its own `team` store, with its own path and key file. Every save writes `team` and
`I:\Archive\proj.kdbx`.

#### Example 4 - Copy every project you save into your personal database

```powershell
Register-VaultStore -Name mine -Target '%OneDrive%\KeePass\mine.kdbx' -KeyFile E:\mine.keyx -Mirror
```

#### Example 5 - Keep your own values and a copy of every project in your personal database

```powershell
Register-VaultStore -Name mine -Target '%OneDrive%\KeePass\mine.kdbx' -Mirror -Personal
```

#### Example 6 - Open the team database with the password kept in your personal one

```powershell
Register-VaultStore -Name mine -Target '%OneDrive%\KeePass\mine.kdbx' -KeyFile E:\mine.keyx
Register-VaultStore -Name team -Target D:\KeePass\it.kdbx -UnlockWith mine
```

LocalVault asks for the password of `mine` only, once the `AutoOpen` group of `mine` holds the
password of `team`. The first time, it asks for both, and offers to keep the password of `team`
there.

### Parameters

#### -Confirm

Prompts you for confirmation before running the cmdlet.

| Property | Value |
| --- | --- |
| Type | System.Management.Automation.SwitchParameter |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | False |
| Aliases | cf |

#### -Default

Indicates that this store becomes the default of this PC, or with **Project**, of the project.

| Property | Value |
| --- | --- |
| Type | System.Management.Automation.SwitchParameter |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | False |
| Aliases | None |

#### -KeyFile

Specifies the key file of the database. Not allowed with **Project**.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -Mirror

Indicates that the store is a mirror: every save also writes it, in addition to the primary store.
On this PC, it mirrors every vault saved here; with **Project**, the project's vaults.

| Property | Value |
| --- | --- |
| Type | System.Management.Automation.SwitchParameter |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | False |
| Aliases | None |

#### -Name

Specifies the name of the store. Use letters, digits and `.`, `_`, `-`. If you omit this
parameter, the name is the target's file name without its extension. With **Project** and no
**Target**, the name refers to the store of that name defined on each PC.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -Path

Specifies the path to the vault file. A relative path is resolved from the current location. If
you omit this parameter, the cmdlet uses the `LOCALVAULT_PATH` environment variable, and then
`secrets.local.lv` at the root of the git repository that contains the current location.
With **Project**, the vault's repository is the project.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -Personal

Indicates that the store is this PC's personal store: it keeps your own values of required entries.

| Property | Value |
| --- | --- |
| Type | System.Management.Automation.SwitchParameter |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | False |
| Aliases | None |

#### -Project

Indicates that the store is defined in the project's `localvault.json` instead of on this PC.

| Property | Value |
| --- | --- |
| Type | System.Management.Automation.SwitchParameter |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | False |
| Aliases | None |

#### -Target

Specifies the store: the path of a KeePass `.kdbx` file. A relative path is resolved from the
current location. Required, except with **Project** and **Name**, which refers to each PC's store of
that name.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | 0 |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -UnlockWith

Specifies the name of another store on this PC that holds this database's master password in its
`AutoOpen` group. LocalVault opens that store first, then this one with the password found there.
Without this parameter, LocalVault asks for this database's password. Not allowed with **Project**.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -WhatIf

Shows what would happen if the cmdlet runs. The cmdlet isn't run.

| Property | Value |
| --- | --- |
| Type | System.Management.Automation.SwitchParameter |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | False |
| Aliases | wi |

### Inputs

**None**: You can't pipe objects to this cmdlet.

### Outputs

**LocalVault.PowerShell.VaultStoreInfo**: This cmdlet returns the store, as `Get-VaultStore` does.

### Notes

`lv store add [<name>] <target> [<keyfile>] [--default] [--unlock-with <store>] [--mirror] [--personal]` and
`lv store add <name> [<target>] --project [--default] [--mirror]` run this cmdlet.

## Remove-VaultEntry

Deletes one entry from a vault.

### Syntax

```text
Remove-VaultEntry [-Name] <string> [-Path <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

### Description

The `Remove-VaultEntry` cmdlet deletes the named entry and keeps every other entry. When the entry
doesn't exist, the cmdlet writes a warning and doesn't change the vault.

The cmdlet keeps the previous version of the vault as `<vault>.bak`.

When the entry is shared and was saved to its store, the vault remembers the removal, and the next
`Export-VaultToStore` removes the entry from the store too.

### Examples

#### Example 1 - Delete an entry

```powershell
Remove-VaultEntry -Name prod/loki.token
```

```Output
Removed prod/loki.token
```

#### Example 2 - Delete every entry of a scope

```powershell
Get-VaultEntryList -Scope staging | Remove-VaultEntry
```

The **Name** property of each object from `Get-VaultEntryList` binds to the **Name** parameter.

#### Example 3 - Show what would be deleted

```powershell
Get-VaultEntryList -Scope staging | Remove-VaultEntry -WhatIf
```

### Parameters

#### -Confirm

Prompts you for confirmation before running the cmdlet.

| Property | Value |
| --- | --- |
| Type | System.Management.Automation.SwitchParameter |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | False |
| Aliases | cf |

#### -Name

Specifies the name of the entry, as `key` or `scope/key`. The name isn't case-sensitive.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | 0 |
| Required | (All) |
| Pipeline input | by property name |
| Default value | None |
| Aliases | None |

#### -Path

Specifies the path to the vault file. A relative path is resolved from the current location. If
you omit this parameter, the cmdlet uses the `LOCALVAULT_PATH` environment variable, and then
`secrets.local.lv` at the root of the git repository that contains the current location.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -WhatIf

Shows what would happen if the cmdlet runs. The cmdlet isn't run.

| Property | Value |
| --- | --- |
| Type | System.Management.Automation.SwitchParameter |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | False |
| Aliases | wi |

### Inputs

**System.String**: You can pipe an object that has a **Name** property to this cmdlet.

### Outputs

**None**: This cmdlet returns no output.

### Notes

`lv rm <name>` runs this cmdlet.

## Remove-VaultTemplate

Stops managing a templated file.

### Syntax

```text
Remove-VaultTemplate [-File] <string> [-Path <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

### Description

The `Remove-VaultTemplate` cmdlet removes a templated file from `localvault.json`. The template file
and the real file stay where they are; delete them yourself if you no longer need them, and commit
`localvault.json`.

### Examples

#### Example 1 - Stop managing a file

```powershell
Remove-VaultTemplate .\infra\clouds.yaml
```

### Parameters

#### -Confirm

Prompts you for confirmation before running the cmdlet.

| Property | Value |
| --- | --- |
| Type | System.Management.Automation.SwitchParameter |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | False |
| Aliases | cf |

#### -File

Specifies the templated file.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | 0 |
| Required | (All) |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -Path

Specifies the path to the vault file, which also names the git repository whose templates the
cmdlet handles. A relative path is resolved from the current location. If you omit this
parameter, the cmdlet uses the `LOCALVAULT_PATH` environment variable, and then
`secrets.local.lv` at the root of the git repository that contains the current location.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -WhatIf

Shows what would happen if the cmdlet runs. The cmdlet isn't run.

| Property | Value |
| --- | --- |
| Type | System.Management.Automation.SwitchParameter |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | False |
| Aliases | wi |

### Inputs

**None**: You can't pipe objects to this cmdlet.

### Outputs

**None**: This cmdlet writes a warning when the file isn't a template of the repository.

### Notes

`lv template rm <file>` runs this cmdlet.

## Resolve-VaultEntry

Gets a secret for a scope, or the shared secret when the scope has none.

### Syntax

```text
Resolve-VaultEntry [-Name] <string> [-Scope] <string> [-Fallback <string>] [-AsPlainText]
 [-Path <string>] [<CommonParameters>]
```

### Description

The `Resolve-VaultEntry` cmdlet gets the secret `<scope>/<key>`. When that entry doesn't exist, it
gets `<fallback>/<key>`, which is `common/<key>` by default. The cmdlet fails when neither entry
exists, or when the entry it finds is a file.

### Examples

#### Example 1 - Get the database password of a deployment

```powershell
$password = Resolve-VaultEntry -Name db.password -Scope prod
```

The cmdlet returns `prod/db.password`, or `common/db.password` when the vault has no
`prod/db.password`.

#### Example 2 - Use another fallback scope

```powershell
Resolve-VaultEntry -Name api.key -Scope customer-a -Fallback defaults -AsPlainText |
    Set-Content -Path Env:API_KEY
```

### Parameters

#### -AsPlainText

Indicates that this cmdlet returns the secret as a **String** instead of a **SecureString**.

| Property | Value |
| --- | --- |
| Type | System.Management.Automation.SwitchParameter |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | False |
| Aliases | None |

#### -Fallback

Specifies the scope to use when `<scope>/<key>` doesn't exist.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | common |
| Aliases | None |

#### -Name

Specifies the key, without a scope. A name that already has a scope is refused. The key can be
followed by a part of a credential, such as `#user` or `#totp`.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | 0 |
| Required | (All) |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -Path

Specifies the path to the vault file. A relative path is resolved from the current location. If
you omit this parameter, the cmdlet uses the `LOCALVAULT_PATH` environment variable, and then
`secrets.local.lv` at the root of the git repository that contains the current location.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -Scope

Specifies the scope to look in first, such as the name of a deployment.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | 1 |
| Required | (All) |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

### Inputs

**None**: You can't pipe objects to this cmdlet.

### Outputs

**System.Security.SecureString**: By default, this cmdlet returns the secret as a **SecureString**.

**System.String**: When you use the **AsPlainText** parameter, this cmdlet returns the secret as a **String**.

### Notes

`Use-VaultEnv` applies the same rule when you use its **Scope** parameter.

## Restore-VaultFile

Writes file entries of a vault back to disk.

### Syntax

```text
Restore-VaultFile [-Name] <string[]> [-Destination <string>] [-Overwrite] [-SkipGitIgnoreCheck]
 [-Path <string>] [<CommonParameters>]
```

```text
Restore-VaultFile -All [-Destination <string>] [-Overwrite] [-SkipGitIgnoreCheck] [-Path <string>]
 [<CommonParameters>]
```

### Description

The `Restore-VaultFile` cmdlet writes the content of file entries to their stored paths, relative
to the folder of the vault, or relative to the **Destination** folder. It creates missing folders.

A stored path that starts with `~/`, which only a required entry's own value can have, is under the
user's profile, such as `~/.ssh/id_ed25519`, whatever the **Destination**. The cmdlet writes such a
file readable by the current user, SYSTEM and Administrators only, without inherited permissions, as
OpenSSH requires for a private key, and creates missing folders the same way.

A stored path can't contain a `.git` folder or end with a file named `secrets.local.*`. Inside a git
repository, git must ignore every target file. Otherwise the cmdlet fails and writes nothing. The
**SkipGitIgnoreCheck** parameter skips the git check.

For each file, the cmdlet compares the existing file with the entry:

- No file: the cmdlet writes it.
- Same content: the cmdlet leaves it.
- Different content: the cmdlet asks before it overwrites the file. Without a console, it keeps the
  file and writes a warning. The **Overwrite** parameter overwrites without asking.

### Examples

#### Example 1 - Restore every file entry

```powershell
Restore-VaultFile -All -Path .\infra\secrets.local.lv
```

```Output
Name           Path                                Action
----           ----                                ------
clouds.yaml    C:\src\obs\infra\clouds.yaml        Written
s3-backend.tf  C:\src\obs\infra\s3-backend.tf      Identical
```

#### Example 2 - Restore one file into another folder

```powershell
Restore-VaultFile -Name clouds.yaml -Destination $env:TEMP\check
```

#### Example 3 - Overwrite files that differ, and list what changed

```powershell
Restore-VaultFile -All -Overwrite | Where-Object Action -EQ 'Written'
```

### Parameters

#### -All

Indicates that this cmdlet restores every file entry of the vault.

| Property | Value |
| --- | --- |
| Type | System.Management.Automation.SwitchParameter |
| Parameter sets | All |
| Position | Named |
| Required | All |
| Pipeline input | False |
| Default value | False |
| Aliases | None |

#### -Destination

Specifies the folder that stored paths are relative to. If you omit this parameter, the cmdlet
uses the folder of the vault.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -Name

Specifies the names of the file entries to restore. The cmdlet fails when a name doesn't exist or
refers to a secret.

| Property | Value |
| --- | --- |
| Type | System.String[] |
| Parameter sets | Name |
| Position | 0 |
| Required | Name |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -Overwrite

Indicates that the cmdlet overwrites files that differ from the vault, without asking.

| Property | Value |
| --- | --- |
| Type | System.Management.Automation.SwitchParameter |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | False |
| Aliases | None |

#### -Path

Specifies the path to the vault file. A relative path is resolved from the current location. If
you omit this parameter, the cmdlet uses the `LOCALVAULT_PATH` environment variable, and then
`secrets.local.lv` at the root of the git repository that contains the current location.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -SkipGitIgnoreCheck

Indicates that the cmdlet writes files that git doesn't ignore inside a repository.

| Property | Value |
| --- | --- |
| Type | System.Management.Automation.SwitchParameter |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | False |
| Aliases | None |

### Inputs

**None**: You can't pipe objects to this cmdlet.

### Outputs

**LocalVault.PowerShell.RestoredFile**: This cmdlet returns one object for each file, with the **Name** of the entry, the full **Path** of
the file and the **Action**: `Written`, `Identical` or `Kept`.

### Notes

`lv restore [<name> ...] [--overwrite]` runs this cmdlet. Without a name, it restores every file
entry.

## Set-VaultEntry

Adds or replaces a secret, a credential or a file in a vault.

### Syntax

```text
Set-VaultEntry [-Name] <string> [[-Value] <securestring>] [-Hello] [-Local] [-Path <string>]
 [-WhatIf] [-Confirm] [<CommonParameters>]
```

```text
Set-VaultEntry [-Name] <string> -FilePath <string> [-StoredPath <string>] [-Hello] [-Local]
 [-Path <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

```text
Set-VaultEntry [-Name] <string> -Credential [-UserName <string>] [-Password <securestring>]
 [-Totp <securestring>] [-Url <string[]>] [-Hello] [-Local] [-Path <string>] [-WhatIf] [-Confirm]
 [<CommonParameters>]
```

```text
Set-VaultEntry [-Name] <string> -Sharing <VaultEntrySharing> [-Path <string>] [-WhatIf] [-Confirm]
 [<CommonParameters>]
```

```text
Set-VaultEntry [-Name] <string> -Required [-Credential] [-StoredPath <string>] [-Note <string>]
 [-Hello] [-Path <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

```text
Set-VaultEntry [-Name] <string> -Protection <VaultEntryProtection> [-Path <string>] [-WhatIf]
 [-Confirm] [<CommonParameters>]
```

```text
Set-VaultEntry [-Name] <string> -Reference <string> [-Path <string>] [-WhatIf] [-Confirm]
 [<CommonParameters>]
```

### Description

The `Set-VaultEntry` cmdlet writes one entry to the vault. If an entry with the same name exists,
the new value replaces it. Other entries don't change.

A secret entry holds a value. Pass it as a **SecureString** with the **Value** parameter or the
pipeline. Without a value, the cmdlet asks for it on the console, or reads one line from redirected
standard input when there is no console. An empty value is refused.

A credential entry keeps a user name, a password, an optional TOTP and URLs together, as KeePass
does. With the **Credential** parameter alone, the cmdlet asks for each part in turn: the user name,
the password (hidden), the TOTP as an `otpauth://` address or a base32 seed (hidden), then one URL
per line until an empty line. On redirected input, it reads the same lines in the same order. An
empty answer keeps the current part of an existing entry; on a secret, the current value becomes the
password. With any of **UserName**, **Password**, **Totp** or **Url**, the cmdlet asks nothing and
keeps the parts you don't give.

A name followed by a part, such as `github#user`, `github#totp` or `github#url2`, sets that part
only: the cmdlet asks for it (hidden for `#password` and `#totp`), or takes **Value**. An empty value
removes the part. On a secret, setting a part other than the password makes it a credential that
keeps the value as its password; on a name that doesn't exist, it creates a credential. Setting a
secret's value on a credential, with the name alone, changes its password and keeps the other parts.

A file entry holds the content of a file and the path to restore it to, relative to the folder of
the vault. Use the **FilePath** parameter, and the **StoredPath** parameter when the file is outside
that folder.

An entry is shared by default: `Export-VaultToStore` sends it to the vault's store. With the **Local** parameter,
a new value stays on this machine only. The **Sharing** parameter alone changes this choice for an
existing entry and keeps its value. Replacing a value keeps the entry's choice.

With the **Required** parameter, the cmdlet declares a personal value: each person sets their own,
and the **Note** says where to get it. `Export-VaultToStore` shares the declaration and the note,
never the value, and `Export-Vault` leaves the value out. Until a value is set on this machine, the
entry is `Missing` in `Get-VaultEntryList`, and reading it fails with the note. Setting a value with
this cmdlet then keeps it on this machine. Declaring an entry that has a value keeps the value.
With **Required** and **Credential**, the declaration asks for a user name and a password: setting
the value with the name alone asks for the parts of a credential. With **Required** and
**StoredPath**, the declaration asks for a file, such as an SSH private key, and **StoredPath** is
where it goes on each machine. A path that starts with `~/` is under the user's profile, such as
`~/.ssh/id_ed25519`. Each person then sets the value with **FilePath**; the entry keeps the declared
path unless **StoredPath** gives another one, or takes the `~/` form of **FilePath** when it is under
the profile. Only a required entry's own value can have a `~/` path.

`Export-VaultToStore` also writes each value set here to the machine's personal store, when one is
defined, and `Import-VaultFromStore` fills it in on another machine.

With the **Reference** parameter, a personal value comes from an entry of the personal store outside
the `LocalVault` group, found by its group path and title, or by its title alone. The cmdlet opens
the personal store, which asks for its password, and stores the value it reads. A secret takes the
entry's password; a credential takes its user name, password, URL and TOTP; a file or SSH key takes
its attachment. `Export-VaultToStore` then writes KeePass references to that entry, never the value,
and `Import-VaultFromStore` takes a change made in KeePass without a question. A value changed on
this machine stays on this machine, with a warning, and `Export-VaultToStore` refuses to save it.
Shared entries can't be references.

With the **Hello** parameter, the entry is also sealed with Windows Hello. Reading its value then
asks for Windows Hello: face, fingerprint or PIN. One command asks once, whatever the number of
Windows Hello entries it reads. Sealing asks for nothing, except the first time on a PC, when the
cmdlet creates the user's Windows Hello key. A new value for an entry sealed with Windows Hello is
sealed too. The **Protection** parameter alone seals an existing entry, or removes its seal, and
keeps its value. Windows Hello seals secrets, credentials and files, not SSH keys. A credential is
sealed whole: its user name and URLs are hidden too. When Windows Hello is unavailable, the cmdlet
fails and writes nothing: it never stores such an entry with DPAPI only.

The cmdlet refuses an SSH key entry, except with **Sharing**: a key is created with
[New-VaultSshKey](#new-vaultsshkey), and replaced with its **Replace** parameter.

When the vault doesn't exist, the cmdlet creates it. It fails when git doesn't ignore the vault
file. At the default path, it also fails when the repository has a vault file in another folder,
and asks before it creates the vault; with **Path**, a repository can hold several vaults.

When the vault folder contains `secrets.local.clixml`, the cmdlet moves its entries into the vault
and renames the file to `secrets.local.clixml.migrated-<time>`.

### Examples

#### Example 1 - Store a secret that you type

```powershell
Set-VaultEntry -Name GrafanaAdminPassword
```

```Output
Value for GrafanaAdminPassword: ************
Stored GrafanaAdminPassword
```

#### Example 2 - Store a scoped secret from a script

```powershell
$token = Read-Host -Prompt 'Push token' -AsSecureString
Set-VaultEntry -Name prod/loki.token -Value $token -Path .\infra\secrets.local.lv
```

#### Example 3 - Store a file

```powershell
Set-VaultEntry -Name clouds.yaml -FilePath .\infra\clouds.yaml -Path .\infra\secrets.local.lv
```

The file is stored with the path `clouds.yaml`, relative to the `infra` folder of the vault.
`Restore-VaultFile -Name clouds.yaml` writes it back there.

#### Example 4 - Store a credential and change its URL

```powershell
Set-VaultEntry -Name github -Credential
Set-VaultEntry -Name github#url
```

```Output
User name for github: jdoe
Password for github: ********
TOTP for github: otpauth:// address or base32 seed (empty for none): ****************
URL 1 for github (empty to finish): https://github.com
URL 2 for github (empty to finish):
Stored github
URL (empty removes it) for github: https://github.example.com
Stored github#url
```

#### Example 5 - Declare a personal SSH key and store yours

```powershell
Set-VaultEntry -Name ssh/deploy -Required -StoredPath '~/.ssh/id_deploy' -Note 'your deploy key'
Set-VaultEntry -Name ssh/deploy -FilePath $HOME\.ssh\id_deploy
```

The key stays out of the project store. `Export-VaultToStore` writes it to the personal store, and
`Import-VaultFromStore` writes it to `~/.ssh/id_deploy` on another machine.

#### Example 6 - Seal a secret with Windows Hello

```powershell
Set-VaultEntry -Name registry.token -Hello
Get-VaultEntry -Name registry.token -AsPlainText | docker login registry.example.com --username ci --password-stdin
```

The first command asks for the value. The second asks for Windows Hello, then passes the value to
the standard input of `docker login`.

#### Example 7 - Remove the Windows Hello seal

```powershell
Set-VaultEntry -Name registry.token -Protection Dpapi
```

#### Example 8 - Copy a secret from another store

```powershell
Get-Secret -Name CiToken -Vault SecretStore | Set-VaultEntry -Name ci.token
```

#### Example 9 - Take a personal value from an entry of the personal store

```powershell
Set-VaultEntry -Name github.token -Reference Internet/GitHub
```

The required entry `github.token` takes the password of `Internet/GitHub` in the personal store.
`Export-VaultToStore` writes `{REF:P@I:<uuid>}` to the personal store instead of the token.

`Get-Secret` from the **Microsoft.PowerShell.SecretManagement** module returns a **SecureString**,
which this cmdlet takes from the pipeline.

### Parameters

#### -Confirm

Prompts you for confirmation before running the cmdlet.

| Property | Value |
| --- | --- |
| Type | System.Management.Automation.SwitchParameter |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | False |
| Aliases | cf |

#### -Credential

Indicates that the entry is a credential: a user name, a password, an optional TOTP and URLs. With
**Required**, it declares that each person's value is a user name and a password.

| Property | Value |
| --- | --- |
| Type | System.Management.Automation.SwitchParameter |
| Parameter sets | Credential, Required |
| Position | Named |
| Required | Credential |
| Pipeline input | False |
| Default value | False |
| Aliases | None |

#### -FilePath

Specifies the file to store. A relative path is resolved from the current location.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | File |
| Position | Named |
| Required | File |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -Hello

Indicates that the entry is sealed with Windows Hello. Reading its value then asks for Windows Hello.
With **Required**, the declaration asks each person's value to be sealed with Windows Hello.

| Property | Value |
| --- | --- |
| Type | System.Management.Automation.SwitchParameter |
| Parameter sets | Secret, File, Credential, Required |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | False |
| Aliases | None |

#### -Local

Indicates that the new entry stays on this machine: `Export-VaultToStore` leaves it out.

| Property | Value |
| --- | --- |
| Type | System.Management.Automation.SwitchParameter |
| Parameter sets | Secret, File, Credential |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | False |
| Aliases | None |

#### -Name

Specifies the name of the entry, as `key` or `scope/key`. A name uses letters, digits and the
characters `.`, `_`, `:`, `-`, `@` and `+`, with at most one `/`. The name isn't case-sensitive, but
its case is kept. Followed by `#user`, `#password`, `#totp`, `#url`, `#url2` and so on, it names one
part of a credential.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | 0 |
| Required | (All) |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -Note

Specifies where each person gets the value of a required entry. KeePass shows it in the entry's notes.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | Required |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -Password

Specifies the password of a credential. If you omit it with other parts, the current password is kept.

| Property | Value |
| --- | --- |
| Type | System.Security.SecureString |
| Parameter sets | Credential |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -Path

Specifies the path to the vault file. A relative path is resolved from the current location. If
you omit this parameter, the cmdlet uses the `LOCALVAULT_PATH` environment variable, and then
`secrets.local.lv` at the root of the git repository that contains the current location.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -Protection

Specifies how the entry is protected, and keeps its value. `Hello` seals the entry with Windows
Hello. `Dpapi` removes the Windows Hello seal, which asks for Windows Hello once. The kind, the value,
the creation time and the sharing of the entry don't change.

| Property | Value |
| --- | --- |
| Type | LocalVault.PowerShell.VaultEntryProtection |
| Parameter sets | Protection |
| Position | Named |
| Required | Protection |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -Reference

Specifies the entry of the personal store that holds the personal value: its group path and title,
such as `Internet/GitHub`, or its title alone when only one entry has it. Entries under the
`LocalVault` group are not searched.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | Reference |
| Position | Named |
| Required | Reference |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -Required

Indicates that the entry is a personal value that each person sets; only its declaration is shared.

| Property | Value |
| --- | --- |
| Type | System.Management.Automation.SwitchParameter |
| Parameter sets | Required |
| Position | Named |
| Required | Required |
| Pipeline input | False |
| Default value | False |
| Aliases | None |

#### -Sharing

Specifies whether the existing entry is `Shared` (sent to the store by `Export-VaultToStore`) or `Local`. The value doesn't change.

| Property | Value |
| --- | --- |
| Type | LocalVault.PowerShell.VaultEntrySharing |
| Parameter sets | Sharing |
| Position | Named |
| Required | Sharing |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -StoredPath

Specifies the path that `Restore-VaultFile` writes the file to, relative to the folder of the
vault, or under the user's profile when it starts with `~/` (required entries only). Use forward or
back slashes; a rooted path or `..` is refused. If you omit this parameter, the cmdlet uses the
declared path of a required entry, then the `~/` form of **FilePath** for a required entry, then the
path of **FilePath** relative to the folder of the vault, and fails when the file is outside that
folder. With **Required**, it declares the entry as a file restored to this path.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | File, Required |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -Totp

Specifies the TOTP of a credential, as an `otpauth://totp/` address or a base32 seed. Only time-based codes are supported.

| Property | Value |
| --- | --- |
| Type | System.Security.SecureString |
| Parameter sets | Credential |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -Url

Specifies the URLs of a credential, in order. They replace the current ones.

| Property | Value |
| --- | --- |
| Type | System.String[] |
| Parameter sets | Credential |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -UserName

Specifies the user name of a credential. An empty string removes it. A user name is not a secret: `Get-VaultEntryList` shows it.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | Credential |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -Value

Specifies the secret, or the part named after `#`, as a **SecureString**. If you omit this
parameter, the cmdlet asks for the value.

| Property | Value |
| --- | --- |
| Type | System.Security.SecureString |
| Parameter sets | Secret |
| Position | 1 |
| Required | False |
| Pipeline input | by value |
| Default value | None |
| Aliases | None |

#### -WhatIf

Shows what would happen if the cmdlet runs. The cmdlet isn't run.

| Property | Value |
| --- | --- |
| Type | System.Management.Automation.SwitchParameter |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | False |
| Aliases | wi |

### Inputs

**System.Security.SecureString**: You can pipe the secret to this cmdlet.

### Outputs

**None**: This cmdlet returns no output. It writes the entry name to the information stream.

### Notes

`lv set <name> [--file <path> | --credential] [--local]`, `lv set <name>#<part>`,
`lv require <name> [--credential | --file <path>] [--note <text>]` and
`lv mark <name> shared|local` run this cmdlet.

## Set-VaultProjectStore

Names the project's primary store.

### Syntax

```text
Set-VaultProjectStore [-Name] <string> [-Path <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

### Description

The `Set-VaultProjectStore` cmdlet writes `localvault.json` at the root of the git repository. Without
**Path**, and without the `LOCALVAULT_PATH` environment variable, the store becomes the project's
primary store: `Import-VaultFromStore` reads every vault of the repository from it, and
`Export-VaultToStore` writes them there, and also to the mirrors (see `Register-VaultStore -Mirror`).
Stores named for single vaults are dropped. With **Path**, only that vault uses the store as its
primary store, and the other vaults keep theirs; it is meant for the rare vault that belongs
elsewhere, such as a personal one.

The store is looked up among the project's stores, then among this PC's stores. The cmdlet opens it
once to read its internal id, which is the same in every copy of a `.kdbx` file whatever its local
name or path, so it asks for its master password, or for the password of the store that unlocks it
(see `Register-VaultStore -UnlockWith`). The id goes into
`localvault.json` beside the name: on another PC, LocalVault finds the store by that id even when it
has another name there, and refuses a database with another id.

Commit the file: everyone then saves into and loads from the same store. The file holds no secret.

### Examples

#### Example 1 - Use the team store for every vault of the repository

```powershell
Set-VaultProjectStore team
git add localvault.json
```

#### Example 2 - Keep one vault in a personal store

```powershell
Set-VaultProjectStore mine -Path .\migration\secrets.local.lv
```

### Parameters

#### -Confirm

Prompts you for confirmation before running the cmdlet.

| Property | Value |
| --- | --- |
| Type | System.Management.Automation.SwitchParameter |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | False |
| Aliases | cf |

#### -Name

Specifies the name of the store, defined in the project's `localvault.json` or on this PC.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | 0 |
| Required | (All) |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -Path

Specifies the path to the vault file. A relative path is resolved from the current location. If
you omit this parameter, the cmdlet uses the `LOCALVAULT_PATH` environment variable, and then
`secrets.local.lv` at the root of the git repository that contains the current location.
With this parameter, only this vault uses the store.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -WhatIf

Shows what would happen if the cmdlet runs. The cmdlet isn't run.

| Property | Value |
| --- | --- |
| Type | System.Management.Automation.SwitchParameter |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | False |
| Aliases | wi |

### Inputs

**None**: You can't pipe objects to this cmdlet.

### Outputs

**None**: This cmdlet writes a message only.

### Notes

`lv store use <name> [--path <vault>]` runs this cmdlet.

## Set-VaultSshKeyHost

Sets the hosts an SSH key of the vault is for.

### Syntax

```text
Set-VaultSshKeyHost [-Name] <string> [-HostName] <string[]> [-Path <string>] [-WhatIf] [-Confirm]
 [<CommonParameters>]
```

### Description

The `Set-VaultSshKeyHost` cmdlet replaces the hosts of an SSH key entry. The hosts of a personal key
belong to its declaration and go to the project's store with it; the hosts of a shared key belong to
the entry. Each host is `[user@]host[:port]`; any other text is refused. An empty list removes the
hosts.

### Examples

#### Example 1 - Set two hosts

```powershell
Set-VaultSshKeyHost -Name deploy -HostName deploy@srv1.example.com, deploy@srv2.example.com
```

#### Example 2 - Remove the hosts

```powershell
Set-VaultSshKeyHost -Name deploy -HostName @()
```

### Parameters

#### -Confirm

Prompts you for confirmation before running the cmdlet.

| Property | Value |
| --- | --- |
| Type | System.Management.Automation.SwitchParameter |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | False |
| Aliases | cf |

#### -HostName

Specifies the hosts, as `[user@]host[:port]`.

| Property | Value |
| --- | --- |
| Type | System.String[] |
| Parameter sets | (All) |
| Position | 1 |
| Required | (All) |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -Name

Specifies the name of the SSH key entry.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | 0 |
| Required | (All) |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -Path

Specifies the path to the vault file. A relative path is resolved from the current location. If
you omit this parameter, the cmdlet uses the `LOCALVAULT_PATH` environment variable, and then
`secrets.local.lv` at the root of the git repository that contains the current location.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -WhatIf

Shows what would happen if the cmdlet runs. The cmdlet isn't run.

| Property | Value |
| --- | --- |
| Type | System.Management.Automation.SwitchParameter |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | False |
| Aliases | wi |

### Inputs

**None**: You can't pipe objects to this cmdlet.

### Outputs

**None**: This cmdlet returns no output.

### Notes

`lv ssh-key hosts <name> [<user@host> ...]` runs this cmdlet.

## Set-VaultSshTools

Sets or shows the OpenSSH folder that LocalVault uses on this PC.

### Syntax

```text
Set-VaultSshTools [-WhatIf] [-Confirm] [<CommonParameters>]
```

```text
Set-VaultSshTools [-Directory] <string> [-WhatIf] [-Confirm] [<CommonParameters>]
```

```text
Set-VaultSshTools -Default [-WhatIf] [-Confirm] [<CommonParameters>]
```

### Description

The `Set-VaultSshTools` cmdlet sets the folder that holds `ssh.exe`, `ssh-keygen.exe` and
`ssh-add.exe` for LocalVault on this PC, for the current Windows account. The folder is kept as
`sshDir` in `%LOCALAPPDATA%\LocalVault\settings.json`; a project never sets it. Without a folder
set, LocalVault uses Windows OpenSSH in `%SystemRoot%\System32\OpenSSH`. LocalVault never looks for
these tools on `PATH`. The cmdlet refuses a folder without the three tools, and keeps the previous
setting.

Without parameters, the cmdlet shows the folder in use.

### Examples

#### Example 1 - Show the folder in use

```powershell
Set-VaultSshTools
```

#### Example 2 - Use the OpenSSH of Git for Windows

```powershell
Set-VaultSshTools -Directory 'C:\Program Files\Git\usr\bin'
```

#### Example 3 - Go back to Windows OpenSSH

```powershell
Set-VaultSshTools -Default
```

### Parameters

#### -Confirm

Prompts you for confirmation before running the cmdlet.

| Property | Value |
| --- | --- |
| Type | System.Management.Automation.SwitchParameter |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | False |
| Aliases | cf |

#### -Default

Indicates that the cmdlet removes the folder setting, so LocalVault uses Windows OpenSSH.

| Property | Value |
| --- | --- |
| Type | System.Management.Automation.SwitchParameter |
| Parameter sets | Default |
| Position | Named |
| Required | Default |
| Pipeline input | False |
| Default value | False |
| Aliases | None |

#### -Directory

Specifies the folder that holds `ssh.exe`, `ssh-keygen.exe` and `ssh-add.exe`. A relative path is
resolved from the current location and kept as a full path.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | Directory |
| Position | 0 |
| Required | Directory |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -WhatIf

Shows what would happen if the cmdlet runs. The cmdlet isn't run.

| Property | Value |
| --- | --- |
| Type | System.Management.Automation.SwitchParameter |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | False |
| Aliases | wi |

### Inputs

**None**: You can't pipe objects to this cmdlet.

### Outputs

**LocalVault.PowerShell.VaultSshToolsInfo**: This cmdlet returns the **Directory** in use, whether it's Windows OpenSSH (**IsDefault**), and the
**Settings** file.

### Notes

`lv ssh-key tools [<dir> | --default]` runs this cmdlet.

## Set-VaultStore

Changes one setting of a store without defining it again.

### Syntax

```text
Set-VaultStore [-Name] <string> [-Mirror] [-UnlockWith <string>] [-NoUnlock] [-Default] [-Personal]
 [-Project] [-Path <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

### Description

The `Set-VaultStore` cmdlet changes the settings that you name, and keeps the store's target, key
file and other settings. `Register-VaultStore` defines a store anew, and forgets what you don't
repeat.

**Mirror** makes the store a mirror: every `Export-VaultToStore` also writes it. `-Mirror:$false`
makes it an ordinary store again. **UnlockWith** names the store that keeps its password;
**NoUnlock** removes it. **Default** makes it the default store of this PC, or, with **Project**,
the project's primary store. **Personal** makes it this PC's personal store, which keeps your own
values of required entries, in place of any other; `-Personal:$false` leaves this PC without one.
**Personal** isn't accepted with **Project**.

Without **Project**, the cmdlet changes a store of this PC in `stores.json`. With **Project**, it
changes an entry of the project's `localvault.json`; an unlocking store is personal and isn't
accepted there.

### Examples

#### Example 1 - Make your personal store a mirror of everything saved on this PC

```powershell
Set-VaultStore mine -Mirror
```

#### Example 2 - Keep your own values of required entries in your personal store

```powershell
Set-VaultStore mine -Personal
```

#### Example 3 - Open the team store with the password kept in your personal store

```powershell
Set-VaultStore team -UnlockWith mine
```

### Parameters

#### -Confirm

Prompts you for confirmation before running the cmdlet.

| Property | Value |
| --- | --- |
| Type | System.Management.Automation.SwitchParameter |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | False |
| Aliases | cf |

#### -Default

Makes the store this PC's default store, or with **Project**, the project's primary store.

| Property | Value |
| --- | --- |
| Type | System.Management.Automation.SwitchParameter |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | False |
| Aliases | None |

#### -Mirror

Makes the store a mirror. `-Mirror:$false` makes it an ordinary store. Without this parameter,
the setting stays.

| Property | Value |
| --- | --- |
| Type | System.Management.Automation.SwitchParameter |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | False |
| Aliases | None |

#### -Name

Specifies the name of the store.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | 0 |
| Required | (All) |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -NoUnlock

Removes the store's unlocking store; its password is asked again.

| Property | Value |
| --- | --- |
| Type | System.Management.Automation.SwitchParameter |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | False |
| Aliases | None |

#### -Path

Specifies the path to the vault file. A relative path is resolved from the current location. If
you omit this parameter, the cmdlet uses the `LOCALVAULT_PATH` environment variable, and then
`secrets.local.lv` at the root of the git repository that contains the current location.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -Personal

Makes the store this PC's personal store. `-Personal:$false` leaves this PC without one. Without this
parameter, the setting stays.

| Property | Value |
| --- | --- |
| Type | System.Management.Automation.SwitchParameter |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | False |
| Aliases | None |

#### -Project

Indicates that the store is an entry of the project's `localvault.json`.

| Property | Value |
| --- | --- |
| Type | System.Management.Automation.SwitchParameter |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | False |
| Aliases | None |

#### -UnlockWith

Specifies the store whose `AutoOpen` group keeps the password of this store. Not allowed with
**Project** or **NoUnlock**.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -WhatIf

Shows what would happen if the cmdlet runs. The cmdlet isn't run.

| Property | Value |
| --- | --- |
| Type | System.Management.Automation.SwitchParameter |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | False |
| Aliases | wi |

### Inputs

**None**: You can't pipe objects to this cmdlet.

### Outputs

**LocalVault.PowerShell.VaultStoreInfo**: This cmdlet returns the store after the change: **Name**, **Scope**, **Type**, **Target**,
**KeyFile**, **Default**, **UnlockWith**, **Mirror**, **Exists** and **Personal**.

### Notes

`lv store set <name> [--mirror | --no-mirror] [--unlock-with <store> | --no-unlock] [--default]
[--personal | --no-personal] [--project]` runs this cmdlet.

## Unregister-VaultStore

Removes a store from this PC or from the project.

### Syntax

```text
Unregister-VaultStore [-Name] <string> [-Project] [-Path <string>] [-WhatIf] [-Confirm]
 [<CommonParameters>]
```

### Description

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

### Examples

#### Example 1 - Forget a store on this PC

```powershell
Unregister-VaultStore home
```

#### Example 2 - Remove a store from the project

```powershell
Unregister-VaultStore old -Project
```

### Parameters

#### -Confirm

Prompts you for confirmation before running the cmdlet.

| Property | Value |
| --- | --- |
| Type | System.Management.Automation.SwitchParameter |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | False |
| Aliases | cf |

#### -Name

Specifies the name of the store.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | 0 |
| Required | (All) |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -Path

Specifies the path to the vault file. A relative path is resolved from the current location. If
you omit this parameter, the cmdlet uses the `LOCALVAULT_PATH` environment variable, and then
`secrets.local.lv` at the root of the git repository that contains the current location.
With **Project**, the vault's repository is the project.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -Project

Indicates that the store is removed from the project's `localvault.json` instead of from this PC.

| Property | Value |
| --- | --- |
| Type | System.Management.Automation.SwitchParameter |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | False |
| Aliases | None |

#### -WhatIf

Shows what would happen if the cmdlet runs. The cmdlet isn't run.

| Property | Value |
| --- | --- |
| Type | System.Management.Automation.SwitchParameter |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | False |
| Aliases | wi |

### Inputs

**None**: You can't pipe objects to this cmdlet.

### Outputs

**None**: This cmdlet returns no output.

### Notes

`lv store rm <name> [--project]` runs this cmdlet.

## Update-VaultTemplate

Takes the edits of templated files back into their templates.

### Syntax

```text
Update-VaultTemplate [[-File] <string>] [-AcceptChanges] [-Path <string>] [-WhatIf] [-Confirm]
 [<CommonParameters>]
```

### Description

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

### Examples

#### Example 1 - Take an edited .env back into its template

```powershell
Update-VaultTemplate .\infra\.env
```

#### Example 2 - Store a password changed in the file, without a question

```powershell
Update-VaultTemplate -AcceptChanges
```

### Parameters

#### -AcceptChanges

Indicates that secret values changed in the file are stored in the vault without a question.

| Property | Value |
| --- | --- |
| Type | System.Management.Automation.SwitchParameter |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | False |
| Aliases | None |

#### -Confirm

Prompts you for confirmation before running the cmdlet.

| Property | Value |
| --- | --- |
| Type | System.Management.Automation.SwitchParameter |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | False |
| Aliases | cf |

#### -File

Specifies one templated file. Without it, the cmdlet handles every template of the repository.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | 0 |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -Path

Specifies the path to the vault file, which also names the git repository whose templates the
cmdlet handles. A relative path is resolved from the current location. If you omit this
parameter, the cmdlet uses the `LOCALVAULT_PATH` environment variable, and then
`secrets.local.lv` at the root of the git repository that contains the current location.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -WhatIf

Shows what would happen if the cmdlet runs. The cmdlet isn't run.

| Property | Value |
| --- | --- |
| Type | System.Management.Automation.SwitchParameter |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | False |
| Aliases | wi |

### Inputs

**None**: You can't pipe objects to this cmdlet.

### Outputs

**None**: This cmdlet writes messages only.

### Notes

`lv template update [<file>] [--accept]` runs this cmdlet.

## Use-VaultEnv

Copies vault secrets into environment variables of the current process.

### Syntax

```text
Use-VaultEnv [-Map] <hashtable> [-Scope <string>] [-Fallback <string>] [-Path <string>]
 [<CommonParameters>]
```

### Description

The `Use-VaultEnv` cmdlet sets one environment variable for each pair in the **Map** parameter, to
the value of the named secret. The variables exist only in the current process and in the programs
it starts; the user and machine environment don't change.

A value can name one part of a credential: `github#user`, `github#url`, or `github#totp` for the
current code. The name alone gives the password.

When a secret is missing, the cmdlet writes a warning and sets the other variables. When a name
refers to a file entry, the cmdlet fails.

### Examples

#### Example 1 - Set the variables that a tool reads

```powershell
Use-VaultEnv -Map @{ OS_PASSWORD = 'OsPassword'; AWS_SECRET_ACCESS_KEY = 'S3SecretKey' }
terraform plan
```

```Output
OS_PASSWORD: set from OsPassword
AWS_SECRET_ACCESS_KEY: set from S3SecretKey
Set for this window only.
```

#### Example 2 - Set the variables of one deployment

```powershell
$map = @{
    DB_PASSWORD = 'db.password'
    API_KEY     = 'api.key'
}
Use-VaultEnv -Map $map -Scope prod -Path .\deploy\secrets.local.lv
```

Each variable gets `prod/<name>`, or `common/<name>` when the vault has no `prod/<name>`. A name
in the map that already has a scope is used as it is.

### Parameters

#### -Fallback

Specifies the scope to use when `<scope>/<name>` doesn't exist. The parameter has an effect only
with the **Scope** parameter.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | common |
| Aliases | None |

#### -Map

Specifies a hash table. Each key is the name of an environment variable and each value is the name
of a secret in the vault, or of a part of a credential, such as `github#user`.

| Property | Value |
| --- | --- |
| Type | System.Collections.Hashtable |
| Parameter sets | (All) |
| Position | 0 |
| Required | (All) |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -Path

Specifies the path to the vault file. A relative path is resolved from the current location. If
you omit this parameter, the cmdlet uses the `LOCALVAULT_PATH` environment variable, and then
`secrets.local.lv` at the root of the git repository that contains the current location.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -Scope

Specifies a scope. For each unscoped name in the map, the cmdlet uses `<scope>/<name>`, or
`<fallback>/<name>` when the scoped entry doesn't exist. If you omit this parameter, the names are
used as they are.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

### Inputs

**None**: You can't pipe objects to this cmdlet.

### Outputs

**None**: This cmdlet returns no output. It writes the names of the variables it set to the information
stream, never their values.

### Notes

To remove a variable, use `Remove-Item -Path Env:<name>`.

`lv env <variable>=<name> ...` runs this cmdlet.

## Write-VaultTemplate

Writes templated files from their templates and the vault.

### Syntax

```text
Write-VaultTemplate [[-File] <string>] [-Path <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

### Description

The `Write-VaultTemplate` cmdlet writes each templated file from its template, replacing every
placeholder with the value of the vault entry it names. Without **File**, it writes every template
of the repository; each template is filled from the vault recorded with it.

The cmdlet writes nothing when git doesn't ignore a target file, or when a placeholder names an
entry that has no value. For a required value not set on this PC, the error gives the note that
says where to get it. In a console, when the vault lacks an entry, the cmdlet first loads the
repository's vaults from their stores.

A file that already has the right content is left as it is.

### Examples

#### Example 1 - Write every templated file of the repository

```powershell
Write-VaultTemplate
```

```Output
Path                                      Action
----                                      ------
C:\dev\proj\infra\.env                    Written
C:\dev\proj\infra\clouds.yaml             Identical
```

### Parameters

#### -Confirm

Prompts you for confirmation before running the cmdlet.

| Property | Value |
| --- | --- |
| Type | System.Management.Automation.SwitchParameter |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | False |
| Aliases | cf |

#### -File

Specifies one templated file. Without it, the cmdlet writes every template of the repository.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | 0 |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -Path

Specifies the path to the vault file, which also names the git repository whose templates the
cmdlet handles. A relative path is resolved from the current location. If you omit this
parameter, the cmdlet uses the `LOCALVAULT_PATH` environment variable, and then
`secrets.local.lv` at the root of the git repository that contains the current location.

| Property | Value |
| --- | --- |
| Type | System.String |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | None |
| Aliases | None |

#### -WhatIf

Shows what would happen if the cmdlet runs. The cmdlet isn't run.

| Property | Value |
| --- | --- |
| Type | System.Management.Automation.SwitchParameter |
| Parameter sets | (All) |
| Position | Named |
| Required | False |
| Pipeline input | False |
| Default value | False |
| Aliases | wi |

### Inputs

**None**: You can't pipe objects to this cmdlet.

### Outputs

**LocalVault.PowerShell.RenderedTemplate**: This cmdlet returns the **Path** of each file and the **Action**: `Written` or `Identical`.

### Notes

`lv template render [<file>]` runs this cmdlet. `lv run` runs it before the command, and
`Clear-VaultTemplate` after.
