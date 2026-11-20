---
description: Describes the LocalVault vault file, entry names, writes, exports, old backups and use from scripts.
Locale: en-US
ms.date: 10/13/2026
title: about_LocalVault
---

# about_LocalVault

## Short description

Describes the LocalVault vault file, entry names, writes, exports, old
backups, and use from scripts.

## Long description

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
missing on this PC, showing its note; an empty answer skips it. A missing
SSH key is taken from a file that another clone wrote, or chosen from the SSH
agent, a KeePass entry or a new key pair. `lv load` then offers to save the
values to the personal store. Without one, it offers to make one of this
PC's stores the personal store, or warns that the values stay on this PC. A command that reads a missing required
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

### .NET user secrets

A .NET project with a `UserSecretsId` reads its secrets from
`%APPDATA%\Microsoft\UserSecrets\<id>\secrets.json`. LocalVault keeps chosen
keys of that file in the vault and writes them back to it.

- The projects are those listed in the repository's `.sln` and `.slnx`
  files. Without a solution file, every `.csproj` outside `bin`, `obj` and
  folders starting with `.` counts.
- The id is the project's `<UserSecretsId>`, else the one in the nearest
  `Directory.Build.props` up to the repository root. An id built from MSBuild
  properties, such as `$(Name)`, is refused.
- Several projects can share one id. Commands take the id or a project name.
- Keys are flat, as `dotnet user-secrets` writes them: `ConnectionStrings:Main`.

`lv user-secrets set <id|project> <key>` (`Set-VaultUserSecret`) asks for the
value, keeps it in an entry named after the key, records the key in
`localvault.json`, and writes it to `secrets.json`. `--entry <name[#part]>`
uses an existing entry instead. In a console, it asks whether the value is
shared or personal; `--personal` makes the entry a required entry, filled by
each person. `lv user-secrets pick` lists the ids with their projects, then
takes a key already in the chosen `secrets.json` or a new key.

`localvault.json` holds the tracked keys and the entry of each, never a value.
Two vaults can't track the same key of the same id. Windows Hello entries and
`#totp` parts can't be tracked.

Only `lv user-secrets set`, `pick`, `load` and `lv load` write `secrets.json`.
A write changes the tracked keys and leaves the other keys as they are. When
a tracked key holds a value that differs from the vault's:

| Command | Behaviour |
| --- | --- |
| `lv user-secrets load`, in a console | Asks once per id; the default keeps the file. |
| `lv user-secrets load`, without a console | Writes nothing for that id and fails with exit code 2. |
| `lv user-secrets load --overwrite` | Writes the vault's values. |
| `lv load` | Writes nothing for that id and writes a warning. |

`lv user-secrets clear` (`Clear-VaultUserSecret`) and `Clear-Vault` remove the
tracked keys whose value still equals the vault's, and name the others. A
`secrets.json` left empty is deleted with its folder. `lv user-secrets
untrack` stops tracking a key; the vault entry stays.

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

## See also

- [LocalVault Module](LocalVault.md)
- [Set-VaultEntry](Set-VaultEntry.md)
- [Get-VaultHelloStatus](Get-VaultHelloStatus.md)
- [Get-VaultEntry](Get-VaultEntry.md)
- [Use-VaultEnv](Use-VaultEnv.md)
- [Import-Vault](Import-Vault.md)
- [Export-VaultToStore](Export-VaultToStore.md)
- [Import-VaultFromStore](Import-VaultFromStore.md)
- [Register-VaultStore](Register-VaultStore.md)
- [Invoke-LocalVault](Invoke-LocalVault.md)
