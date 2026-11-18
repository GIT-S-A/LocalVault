# Getting started with LocalVault

This guide covers the usual path: one vault on one PC, then a team store, then personal values,
SSH keys and .NET user secrets, then a new machine. Each step names the `lv` command; the cmdlet that runs it is in the
[reference](reference.md).

Requirements: Windows, PowerShell 7.6 or later, git. A store needs a KeePass database (`.kdbx`); no
KeePass program is needed on the PC.

## 1. Installation

```powershell
$d=New-Item -ItemType Directory "$env:TEMP\lv-$(New-Guid)"; irm https://github.com/GIT-S-A/LocalVault/releases/latest/download/install.ps1 -OutFile "$d\install.ps1"; & "$d\install.ps1"
```

The installer checks the download's SHA256 and the GIT signature of every file. `lv update` installs
later releases. In any project folder, `lv start` shows the state of the project and the commands to
run next.

## 2. A first vault

```powershell
cd C:\src\webshop
lv set db/password                  # asks for the value, hidden
lv set api.token --local            # stays on this PC, never sent to a store
lv set github --credential          # user name, password, optional TOTP and URLs
lv list                             # names, kinds and ages; never a value
```

| Item | Value |
|---|---|
| Vault file | `secrets.local.lv` at the root of the git repository |
| Sealing | Windows DPAPI, current user on this PC |
| Requirement | git ignores the file: `.gitignore` holds `secrets.local.*` |
| Names | `key` or `scope/key`, such as `prod/db.password` |

The vault is created by the first `lv set`, after a confirmation. A copy of the file is unreadable on
another PC or under another account.

## 3. Use from commands and scripts

```powershell
lv get db/password | mytool --password-stdin   # into a pipe only; lv get --show prints it
lv clip github                               # clipboard for 20 s, kept out of its history
lv env DB_PASSWORD=db/password               # environment variable of this window only
lv run DB_PASSWORD=db/password -- terraform apply   # for one command only
```

In a script:

```powershell
#Requires -Modules @{ ModuleName = 'LocalVault'; ModuleVersion = '1.0.0' }
$password = Get-VaultEntry -Name db/password          # SecureString
$credential = Get-VaultEntry -Name github -AsCredential
```

`github#user`, `github#totp` and `github#url` name one part of a credential wherever a name is read.
Files that a tool reads with secrets inside are handled by templates: `lv template add <file>`.

## 4. A team store

A store is a KeePass database that the team shares. Each entry is shared unless it was set with
`--local`. Defining the store happens once per PC; naming it in the project happens once per
project.

```powershell
lv store add team \\files\it\team.kdbx --default     # this PC: the team database
lv store add team --project --default                # the project: each PC's "team" store
lv save                                              # every vault of the repository -> store
git add localvault.json; git commit -m "chore: name the LocalVault store"
```

| Command | Effect |
|---|---|
| `lv save` | writes shared entries to the store, group `LocalVault/<repository>[/<folder>]` |
| `lv load` | reads them back; a teammate's change is taken without a question |
| `lv store` | each vault's store, and the changes not saved yet |
| `lv store diff` | the state of each entry against the store |

The KeePass master password is asked on every `lv save` and `lv load` and never kept. A change made
on both sides is asked about.

## 5. A personal store

A personal store is the person's own KeePass database. It opens the team store with a password kept
in its `AutoOpen` group, keeps a copy of every shared entry as a mirror, and keeps the person's own
values.

```powershell
lv store add mine $env:OneDrive\KeePass\mine.kdbx --mirror --personal
lv store set team --unlock-with mine
```

After this, `lv save` and `lv load` ask for one password: the personal database's. The first time,
the team database's password is asked once more and offered a place in the personal database.

## 6. Personal values

A required entry is declared by the project and filled by each person. The team store gets the
declaration and its note; the value goes to each person's personal store only.

```powershell
lv require db/admin --credential --note "your own database account"
lv require ssh/deploy --file ~/.ssh/id_deploy --note "your deploy key"
lv set db/admin                                # asks for the user name and the password
lv set ssh/deploy --file ~/.ssh/id_deploy      # reads the key file
lv save
```

| Declaration | Value asked for | Written on another PC |
|---|---|---|
| `lv require <name>` | a hidden value | into the vault |
| `lv require <name> --credential` | a user name and a password | into the vault |
| `lv require <name> --file <path>` | a file, such as an SSH private key | into the vault, and to `<path>` |

`~/` is the user's profile. A file written there, such as `~/.ssh/id_deploy`, is readable by the
user, SYSTEM and Administrators only, as OpenSSH requires. `lv load` writes it when it is missing and
never replaces an existing file; `lv restore <name> --overwrite` replaces it. Only a person's own
value can be written under the profile; a file in the team store with such a path is never loaded.

## 7. .NET user secrets

A .NET project with a `UserSecretsId` reads `%APPDATA%\Microsoft\UserSecrets\<id>\secrets.json`.
LocalVault keeps chosen keys of that file in the vault and writes them back on every PC.

```powershell
lv user-secrets                                       # each id, its projects, tracked and untracked keys
lv user-secrets set Webshop.Api ConnectionStrings:Main   # asks for the value: shared or personal
lv user-secrets pick                                  # choose an id, then a key from its secrets.json
git add localvault.json; git commit -m "chore: track user secrets"
```

| Command | Effect |
|---|---|
| `lv load` | writes the tracked keys into each `secrets.json`; a key set by hand to another value is kept, with a warning |
| `lv user-secrets load --overwrite` | replaces such values with the vault's |
| `lv user-secrets clear` | removes the tracked keys still equal to the vault's |

Other keys of `secrets.json` are never changed.

## 8. A new machine

```powershell
# 1. installation, as in step 1
git clone https://dev.example/org/webshop.git; cd webshop
lv store add mine $env:OneDrive\KeePass\mine.kdbx --mirror --personal   # once per PC
lv store add team \\files\it\team.kdbx --unlock-with mine               # once per PC
lv load
```

`lv load` asks for the personal database's password, creates every vault of the repository from the
team store, fills in the personal values from the personal store, and writes personal files such as
SSH keys. In a console, it then asks for each required value still missing, showing its note, and
offers to save what was typed to the personal store. It then writes the tracked .NET user secrets.
`lv start` lists anything left to do.

## 9. A copy outside the stores

Entries marked local exist on this PC only. `lv list` warns when such entries were not exported in the
last 30 days.

```powershell
lv export D:\backup\webshop.export         # asks for a passphrase of at least 12 characters, twice
lv import D:\backup\webshop.export         # on any PC, with the passphrase
```

## Further reading

- [README](../README.md): every command in one table, the vault file, stores and mirrors.
- [about_LocalVault](LocalVault/about_LocalVault.md): concepts, file layout, scripts, data loss.
- [Reference](reference.md): every command, its parameters and examples.
