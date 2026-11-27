---
name: localvault
description: Install, update or use LocalVault, the per-project DPAPI secret store (PowerShell module, `lv`). Use when a project needs secrets stored locally, when scripts use Get-VaultEntry/Set-VaultEntry/Use-VaultEnv or `lv`, when migrating scripts that keep secrets with Import-Clixml/Export-Clixml (DPAPI .clixml files), when setting up a new machine, or when secrets are shared through a store (KeePass).
---

# LocalVault

PowerShell 7.6+ module, Windows only. Source: https://github.com/GIT-S-A/LocalVault.
`Get-Help about_LocalVault` and `Get-Help <cmdlet> -Full` are the reference; `lv` alone lists the
commands; `lv start` in a project prints its state and the commands to run next.

## Rules

- Never print, log or echo a secret value, never put one on a command line, never write one to a
  file outside the vault. Read names with `Get-VaultEntryList`; never open a vault or .clixml file to
  look at values.
- Ask the person before: the first write to a real vault; `lv store use` and `lv store add --project`
  (they write `localvault.json`, a committed file); `lv save` / `Export-VaultToStore`; `Clear-Vault`;
  `-ReplaceAll`; `-Overwrite`; `-Downgrade`; deleting or renaming an old .clixml file; editing scripts
  that other people run.
- Install with the line below; update with `lv update`. Never run `git checkout`, `pull`, `reset` or
  `clean` in a LocalVault clone: it is someone's working copy.
- The vault file must be ignored by git before its first write; the module refuses otherwise.
  `lv init` adds the lines the repository's vaults and templated files need to `.gitignore` (commit
  it); only a committed rule counts, not a global or `.git/info/exclude` one.

## Facts that shape a migration

- **Vault files.** Default: `secrets.local.lv` at the git root. Any other place: `-Path` (cmdlets),
  `--path` (`lv`), or `$env:LOCALVAULT_PATH`. A repository may hold several vaults, each created
  with an explicit path, e.g. `infra\secrets.local.lv` and `migration\secrets.local.lv`. Without a
  path, creating a vault fails when the repository already has one elsewhere.
- **Names.** `key` or `scope/key`, at most one `/`. Characters: letters, digits, `. _ : - @ +`, so an
  email address is a valid key. Case-insensitive, case kept. Not allowed: spaces, `\`, `$`, accents,
  a second `/`. A name that doesn't fit has to be renamed, and the scripts with it.
- **Kinds.** Secret (text), credential (user name, password, optional TOTP and URLs), file (bytes plus
  the path it is restored to), SSH key, required (declared by the project, value set by each person,
  see below).
- **Who sees what.** Every entry is *shared* by default: `lv save` copies it to the vault's store (a
  KeePass database). `--local` / `-Local` keeps an entry on this PC; `lv mark <name> local|shared`
  changes it later. `lv require <name> [--credential | --file <path>] --note "..."` declares a personal
  value (a user name and a password with `--credential`; a file under the profile with
  `--file ~/<path>`, written there by `lv load` with an owner-only ACL): the store gets the
  declaration, never the value; until set, the entry is `Missing`. A PC's personal store
  (`lv store set <name> --personal`) keeps each person's values: `lv save` writes them there,
  `lv load` fills them in, then asks in a console for those still missing. `lv reset <name>
  [--no-ask]` drops a personal value here and, at the next `lv save`, in the personal store, then
  asks for a new one.
- **References.** `lv set <name> --ref "<group/title>" [--ref-store <store>]` keeps a value that
  already lives in a KeePass entry outside `LocalVault/`: the store gets where it comes from, never
  the value, and `lv load` reads the entry again. Personal values: any store of the PC, default the
  personal store. Shared values: the vault's store, or a project store added with `--source`.
- **SSH keys.** For SSH to hosts, never for git. `lv ssh-key new <name> [--host user@host]...
  [--shared]` creates a pair in `~/.ssh/<repository>/<name>` (same path in every clone) and prints the
  public key, which someone with access must add to the hosts. Personal by default: declaration to
  the team store, key to the person's personal store; `--shared` puts the key in the team store. On
  another PC `lv load` asks, in a console, for an agent key, a KeePass-held key or a new pair, and
  takes a key another clone already wrote without asking. Scripts connect with `lv ssh <name>
  [user@host] [-- args]` / `lv scp <name> -- args` (exit code of ssh); `lv ssh-key include on` lets
  plain `ssh` find the keys. `--replace` keeps the old key as `<name>.old` until every host accepts
  the new one. `__SshPrivateKey` entries of old exports stay refused: create or pick the key instead.
- **.NET user secrets.** `lv user-secrets set <id|project> <key> [--personal]` keeps a key of a
  project's `secrets.json` in the vault and tracks it in `localvault.json`; `lv load` writes the
  tracked keys back, never touching other keys. `lv user-secrets` lists ids, projects and keys.
- **Stores.** A store is a KeePass `.kdbx` file. *Common* stores belong to the PC (`lv store add <name>
  <kdbx> [--default] [--unlock-with <store>] [--mirror]`, in `%LOCALAPPDATA%\LocalVault\stores.json`), read
  first; *project* stores belong to the repository (`lv store add <name> [<kdbx>] --project [--mirror]`,
  in `localvault.json`, no key file); a project entry without a target refers to each PC's common store
  of that name. `localvault.json` at the repo root lists the repository's vaults (filled automatically
  on write) and the project's primary store (`lv store use <name>`), by name and internal id; a vault's
  own primary (`lv store use <name> --path <vault>`) is a rare override. Commit it. A store marked
  `--mirror` (either level) is also written by every `lv save`; the vault's value wins there; an
  unreachable mirror only warns; `lv load` reads the primary only. `lv save` / `lv load`
  handle every listed vault at once, asking the password once per store; a vault whose store isn't
  defined on this PC is skipped with a message. In a console, reading an entry this PC lacks loads the
  vaults from their stores first. Each vault has its own KeePass group:
  `LocalVault/<repo>[/<vault folder>]`. `--unlock-with <store>` opens a store with the password kept
  in another store's `AutoOpen` group (usually the person's own database), so one password opens all.
- **Files that need secrets.** When a tool insists on a file (`clouds.yaml`, `terraform.tfvars`,
  cloud-init, a `.env` it reads itself), use a template rather than a file entry:
  `lv template add <file> [--capture KEY,...]` writes `<file>.template` (commit it, with
  `localvault.json`) with `{{name}}` placeholders and moves captured values into the vault; without
  the file it starts from `<file>.example`. `lv template render` / `clear` write or delete the real,
  git-ignored file; `lv template update [--accept]` carries edits of the real file into the template.
  `lv run [VAR=name ...] -- <command>` sets variables and writes the files only while the command
  runs. When the tool reads environment variables (`docker compose` `${VAR}`, `OS_PASSWORD`), prefer
  `Use-VaultEnv` / `lv run VAR=name` and no file at all.
- **The local vault is a cache**, sealed to the Windows account (DPAPI). It disappears with the
  folder; `lv load` rebuilds it.
- **Old files.** A `secrets.local.clixml` beside `secrets.local.lv` is read until the first write,
  which moves its entries into the vault and renames it `.migrated-<time>`. Other .clixml files are
  imported with `Import-Vault -Source <file>`; the file itself stays where it is.

- **Windows Hello.** `--hello` / `-Hello` seals an entry with Windows Hello on top of DPAPI. Every
  read of it (`get`, `env`, `run`, templates, `save` of a changed entry, `export`) shows a Windows
  Hello prompt that only the person at the console can answer. Run such commands in the person's
  console, not from an agent shell. `lv list` and `lv hello` never prompt; `Missing` in the Hello
  column means this PC's key can't open the entry, and `lv load` restores it.
- **Standard input.** `lv run --stdin <name> -- <program>` sends a value to a program's standard
  input as bytes. It replaces `Get-VaultEntry -AsPlainText | program`, which makes a string.

## Install or update

`$d=New-Item -ItemType Directory "$env:TEMP\lv-$(New-Guid)"; irm https://github.com/GIT-S-A/LocalVault/releases/latest/download/install.ps1 -OutFile "$d\install.ps1"; & "$d\install.ps1"`

Afterwards `lv update` (or `lv update <tag>`); it does nothing when the newest release is already
installed. `lv --version` prints the installed release. Releases are signed by GIT; the installer
refuses unsigned files. Only active in new PowerShell windows.
Check: `Get-Module LocalVault -ListAvailable | Select-Object Version, Path`. The module lives in
`%LOCALAPPDATA%\LocalVault\Modules` (on the user `PSModulePath`), never in OneDrive `Documents`.

## Migrating scripts that use DPAPI .clixml files

1. **Inventory, names only.** Find the .clixml files and the scripts that read or write them
   (`Import-Clixml`, `Export-Clixml`, `SecureString`). For each file, count and list its keys
   without values: import it into a throwaway vault and list it, then delete the throwaway folder.
   ```powershell
   $t = Join-Path $env:TEMP "lv-inventory-$(Get-Random)"
   Import-Vault -Path "$t\v.lv" -Source <file.clixml> | Out-Null   # prints "Not imported: <name> (<reason>)" for misfits
   Get-VaultEntryList -Path "$t\v.lv" | Select-Object Name, Kind
   Remove-Item -Recurse -Force $t
   ```
2. **Check the names** against the rules above; list the ones that need renaming, and the keys that
   start with `__` (`__File:<name>` becomes a file entry; `__SshPrivateKey` is refused: the person
   creates or picks the key with `lv ssh-key new` or `lv load`).
3. **Decide with the person**, per old file: which vault it becomes (one vault per old file, with an
   explicit path), and per entry: shared (default), local, or required personal value.
4. **Rehearse** steps 5 and 6 on a copy of the repository folder, never on the real one.
5. **Create each vault** with its path: for a `secrets.local.clixml` beside it, the first write
   migrates it; otherwise `Import-Vault -Path <vault> -Source <old file>`. Then apply the decisions:
   `lv mark <name> local --path <vault>`, `lv require <name> --note "..." --path <vault>`.
6. **Rewrite the scripts** (patterns below), pin the module, and run them once with the person.
7. **Stores**, with the person: `lv store use <name>` once per repository (the primary store;
   `--path <vault>` only for a vault that goes to another store, e.g. a personal one), mirrors if the
   person wants extra copies (`lv store add <name> <kdbx> --project --mirror`), then `lv save` (all
   vaults, one password per store). Commit `localvault.json`.
8. Leave the old .clixml files in place until the person decides; they are the fallback.

## Script patterns

```powershell
#Requires -Modules @{ ModuleName = 'LocalVault'; ModuleVersion = '1.0.0' }
$vault = Join-Path $PSScriptRoot 'secrets.local.lv'           # or the vault this script uses

# Before: $s = Import-Clixml $file; $s['OsPassword']           (a SecureString)
$secure = Get-VaultEntry -Path $vault -Name 'OsPassword'

# Before: [Net.NetworkCredential]::new('', $s['Key']).Password
$plain = Get-VaultEntry -Path $vault -Name 'Key' -AsPlainText  # only where a tool needs plain text

# Before: environment variables filled from the hashtable
Use-VaultEnv -Path $vault -Map @{ OS_PASSWORD = 'OsPassword'; AWS_SECRET_ACCESS_KEY = 'S3SecretKey' }

# Before: $s['Key'] = Read-Host -AsSecureString; $s | Export-Clixml $file
Set-VaultEntry -Path $vault -Name 'Key'                        # asks; never pass a value as text

# Before: files embedded as base64 ('__File:clouds.yaml')
Restore-VaultFile -Path $vault -Name 'clouds.yaml'             # writes it back to its stored path

# One mailbox per entry, scoped
foreach ($mailbox in $mailboxes) { $pw = Get-VaultEntry -Path $vault -Name "migration/$mailbox" }

# Before: a user name kept beside the password; now one credential entry
Set-VaultEntry -Path $vault -Name 'smtp' -Credential            # asks user, password, TOTP, URLs
$credential = Get-VaultEntry -Path $vault -Name 'smtp' -AsCredential
$code = Get-VaultEntry -Path $vault -Name 'portal#totp' -AsPlainText
```

Writes merge: several scripts can share one vault. A script never rebuilds a whole vault.

## New machine

1. Install (above). Clone the project.
2. `lv start`. Only when it says the project's store isn't defined on this PC:
   `lv store add <name> <path to .kdbx> --default` (name as `lv start` shows it). Optional, to type
   one password: `lv store add mine <personal.kdbx>`, then `lv store add <name> <path to .kdbx>
   --unlock-with mine`. If the person keeps their own values in it: `lv store set mine --personal`.
3. `lv load` (every vault of the repository); the person types the KeePass password, then, in their
   console, each required value still missing. Exit code 2: entries changed on both sides; ask, then
   `--replace name,...`.
4. `lv list`: entries still marked `Missing` are personal values to set with `lv set <name>`, or SSH
   keys to set with `lv ssh-key new <name>`.

## Commands

Cmdlets: `Get-VaultEntryList`, `Get-VaultEntry`, `Copy-VaultEntry`, `Set-VaultEntry`,
`Remove-VaultEntry`, `Resolve-VaultEntry`, `Use-VaultEnv`, `Restore-VaultFile`, `Get-VaultHelloStatus`, `Export-Vault`,
`Import-Vault`, `Clear-Vault`,
`Export-VaultToStore`, `Import-VaultFromStore`, `Compare-VaultWithStore`, `Get-VaultStoreStatus`,
`Copy-VaultToStore`, `Set-VaultProjectStore`, `Register-VaultStore`, `Set-VaultStore`,
`Unregister-VaultStore`, `Get-VaultStore`, `Add-VaultTemplate`, `Update-VaultTemplate`,
`Write-VaultTemplate`, `Clear-VaultTemplate`, `Remove-VaultTemplate`, `Get-VaultTemplate`,
`Initialize-Vault`, `Reset-VaultEntry`, `New-VaultSshKey`, `Set-VaultSshKeyHost`, `Get-VaultSshKey`,
`Get-VaultSshKeyPath`, `Set-VaultSshTools`, `Invoke-VaultSsh`, `Invoke-VaultScp`,
`Set-VaultSshInclude`, `Remove-VaultSshOrphan`, `Get-VaultUserSecretList`, `Set-VaultUserSecret`,
`Get-VaultUserSecret`, `Remove-VaultUserSecret`, `Write-VaultUserSecret`, `Clear-VaultUserSecret`.

`lv`: `start`, `init`, `list`, `get [--show]`, `clip`, `set [--file <path> | --credential | --ref <group/title>] [--local] [--hello]`, `require [--credential | --file <path>] [--note] [--hello]`,
`mark <name> shared|local|hello|nohello`, `hello`, `rm`, `reset [--no-ask]`, `env`, `restore [--overwrite]`, `clear`, `save`, `load`,
`export [--overwrite]`, `import`,
`store [list|add|set|rm|use|diff|copy]`, `template [add|update|render|clear|rm]`,
`ssh-key [list|new|hosts|path|tools|include|clean]`, `ssh`, `scp`,
`user-secrets [list|set|get|pick|untrack|load|clear]`,
`run [VAR=name ...] [--stdin <name>] -- <command>`, `update [<tag>] [--downgrade]`, `--version`.
`lv store set <name> --mirror` (or `--unlock-with <store>`, `--no-mirror`, `--no-unlock`, `--default`, `--personal`, `--no-personal`)
changes one setting without retyping the target and key file.
Every vault command takes `--path <vault>`; `save`, `load` and `store diff` take `--store <name>`.
`<name>#user`, `#password`, `#totp` (current code), `#url`, `#url2` name one part of a credential in
`get`, `clip`, `set`, `env`, `run` and templates (`{{name#user}}`); the bare name is the password.
