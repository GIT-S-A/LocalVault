# LocalVault

One secret store per project, on the machine where you work. Values are sealed with Windows DPAPI
to your account on this PC; copying the file elsewhere gives nothing. Exports carry secrets between
machines and people under a passphrase. Shared entries live in a store, a KeePass database, that
the team reads and a new machine loads from.

Windows only. PowerShell 7.6 or later.

## Install

```powershell
$d=New-Item -ItemType Directory "$env:TEMP\lv-$(New-Guid)"; irm https://github.com/GIT-S-A/LocalVault/releases/latest/download/install.ps1 -OutFile "$d\install.ps1"; & "$d\install.ps1"
```

It downloads `install.ps1` from the newest GitHub release; that script downloads the release's
`LocalVault.zip`, checks it against its SHA256 file, refuses any DLL or PowerShell file not signed by
GIT Gestion Et Informatique Pour Tous S.A., and installs. Later, `lv update` installs the newest
release, `lv update <tag>` a given one; neither replaces a newer install with an older release unless
you add `-Downgrade`, and neither does anything when that release is installed already. Then, in a
project folder, `lv start` tells you what to run.

The module goes in `%LOCALAPPDATA%\LocalVault\Modules\LocalVault\<version>`, `%LOCALAPPDATA%\LocalVault\Modules`
is added to your `PSModulePath` (user scope), and, when `~\.claude` exists, the Claude skill goes in
`~\.claude\skills\localvault`. The module stays out of `Documents`, which OneDrive syncs to your other
PCs; a copy left there by an earlier release is removed, since it would load first.

Versions install side by side; scripts pin the one they were written for:

```powershell
#Requires -Modules @{ ModuleName = 'LocalVault'; ModuleVersion = '1.0.0' }
```

## Documentation

- [Getting started](docs/getting-started.md): first vault, team store, personal values and SSH keys, new machine.
- [LocalVault reference](docs/reference.md): concepts and every command on one page.
- [LocalVault module](docs/LocalVault/LocalVault.md): one page per command.
- [about_LocalVault](docs/LocalVault/about_LocalVault.md): concepts, file layout, use from scripts.
- In PowerShell: `Get-Help <command> -Full`, `Get-Help about_LocalVault`.

## The vault file

- Default: `secrets.local.lv` at the root of the git repository you are in. Elsewhere: `-Path`, or
  `$env:LOCALVAULT_PATH`.
- Git must ignore it. `lv init` adds one line to the project's `.gitignore`, to commit:

  ```gitignore
  secrets.local.*
  ```

  That also covers `secrets.local.lv.bak` (previous version), `.lock`, and an old
  `secrets.local.clixml` with its `.migrated-*` copies.
- Several scripts of one project may share one vault; every write merges, under a lock. A repository
  can also hold several vaults, each created with `-Path` (e.g. `infra\secrets.local.lv`).
- A `secrets.local.clixml` from the older scripts is read as it is and becomes `secrets.local.lv`
  on the first write. Names are kept. SSH keys in it are not migrated; their place is KeePass.

## Names

`key` or `scope/key`. Letters, digits and `. _ : - @ +` (an email address is a valid key); one `/` at most. Case is kept, but `Key` and
`key` are the same entry. `Resolve-VaultEntry -Name db -Scope prod` returns `prod/db`, or `common/db`
when there is no `prod/db`.

## Commands

| Cmdlet | `lv` | Does |
|---|---|---|
| `Initialize-Vault` | `lv init` | adds the repository's vaults and the files written from templates to `.gitignore` |
| `Get-VaultEntryList [-Scope]` | `lv list [scope]` | names, kind, store state, age; never a value |
| `Set-VaultEntry -Name n [-Value <SecureString>] [-Local]` | `lv set n [-Local]` | asks, or reads one line from redirected input |
| `Set-VaultEntry -Name n -FilePath p [-StoredPath r]` | `lv set n -FilePath p` | carries a file; restored to `r` beside the vault |
| `Set-VaultEntry -Name n -Credential [-UserName u] [-Password s] [-Totp s] [-Url a,b]` | `lv set n -Credential` | user name, password, TOTP and URLs as one entry; asks for each part |
| `Set-VaultEntry -Name n#part [-Value s]` | `lv set n#user` | one part: `#user`, `#password`, `#totp`, `#url`, `#url2`...; empty removes it |
| `Set-VaultEntry -Name n -Required [-Credential \| -StoredPath p] [-Note text]` | `lv require n [-Credential \| -StoredPath p] [-Note text]` | personal value each person sets (a user name and a password with `-Credential`, a file under the profile with `-StoredPath ~/<path>`); the store gets the declaration only |
| `Set-VaultEntry -Name n -Reference "group/title" [-ReferenceStore s]` | `lv set n -Reference "group/title" [-ReferenceStore s]` | the value stays in that KeePass entry; the store gets where it comes from |
| `Reset-VaultEntry -Name n[] [-NoPrompt]` | `lv reset n... [-NoPrompt]` | drops a personal value here and, at the next save, in the personal store; asks for a new one |
| `Set-VaultEntry -Name n -Hello`, `Get-VaultHelloStatus` | `lv set n -Hello`, `lv mark n hello\|nohello`, `lv hello` | reads need Windows Hello on top of DPAPI; the state of Windows Hello on this PC |
| `Set-VaultEntry -Name n -Sharing Shared\|Local` | `lv mark n shared\|local` | send to the store or keep on this PC |
| `Get-VaultEntry -Name n[#part] [-AsPlainText] \| -AsCredential` | `lv get n[#part] [-Show]` | SecureString by default; `lv get` into a pipe, or on screen with `-Show`; `#totp` is the current code, `#user` and `#url` print anywhere |
| `Copy-VaultEntry -Name n [-Seconds 20]` | `lv clip n` | clipboard for 20 s, out of the clipboard history |
| `Remove-VaultEntry -Name n` | `lv rm n` | |
| `Resolve-VaultEntry -Name key -Scope s [-Fallback common]` | | scoped value wins |
| `Use-VaultEnv -Map @{ ENV = 'name' } [-Scope s]` | `lv env ENV=name ...` | this window only |
| `Restore-VaultFile [-Name n \| -All] [-Destination d] [-Overwrite]` | `lv restore [n] [-Overwrite]` | asks before overwriting a different file |
| `Export-Vault -Destination f [-Overwrite]` | `lv export f [-Overwrite]` | passphrase of 12+ characters, asked twice |
| `Import-Vault -Source f [-Replace a,b \| -ReplaceAll]` | `lv import f` | export, backup, `SAAS-VAULT-1` export or old `.clixml` |
| `Export-VaultToStore [-Store name] [-Replace a,b \| -ReplaceAll]` | `lv save` | shared entries of every vault → its store |
| `Import-VaultFromStore [-Store name] [-Replace a,b \| -ReplaceAll]` | `lv load` | store → vaults; on a new machine too |
| `Get-VaultStoreStatus` | `lv store` | each vault's store and what isn't saved; opens nothing |
| `Get-VaultStore` | `lv store list` | stores of the project and of this PC |
| `Register-VaultStore [-Name n] [[-Target] kdbx] [-KeyFile k] [-Default] [-UnlockWith s] [-Mirror] [-Personal] [-Project]`, `Unregister-VaultStore` | `lv store add kdbx [-Name n] [-KeyFile k]`, `lv store add -Name n -Project`, `lv store rm n` | defines a store on this PC, or with `-Project` in localvault.json (a name alone refers to each PC's store); `-Mirror`: every save also writes it; `-Personal`: keeps your own values of required entries; name defaults to the file name |
| `Set-VaultProjectStore [-Name] n` | `lv store use n` | the project's primary store, read by `lv load` (`-Path`: one vault, rarely); written to localvault.json |
| `Compare-VaultWithStore` | `lv store diff` | state of each entry against the store |
| `Copy-VaultToStore [-Target] kdbx \| -Store n` | `lv store copy kdbx` | one-off copy to another store; records nothing |
| `Set-VaultStore [-Name] n [-Mirror] [-UnlockWith s \| -NoUnlock] [-Default] [-Personal] [-Project]` | `lv store set n [-Mirror[:$false]] [-UnlockWith s\|-NoUnlock] [-Default] [-Personal[:$false]]` | changes those settings only; target and key file stay |
| `Add-VaultTemplate [-File] f [-Capture KEY,...]` | `lv template add f [-Capture KEY,...]` | `f.template` beside `f`, secrets as `{{name}}`; from `f.example` when `f` doesn't exist |
| `Update-VaultTemplate [[-File] f] [-AcceptChanges]` | `lv template update [f] [-AcceptChanges]` | your edits of `f` go into its template; a changed secret is stored only when accepted |
| `Write-VaultTemplate [[-File] f]`, `Clear-VaultTemplate [[-File] f]` | `lv template render [f]`, `lv template clear [f]` | writes or deletes the real files (git must ignore them) |
| `Get-VaultTemplate`, `Remove-VaultTemplate [-File] f` | `lv template`, `lv template rm f` | state of each templated file; stop managing one |
| | `lv run [VAR=name ...] <command>` | variables and templated files exist only while the command runs |
| `Get-VaultUserSecretList [[-Target] t]` | `lv user-secrets [t]` | each UserSecretsId of the repository's .NET projects, its projects, tracked keys and their state; `t` = id or project name |
| `Set-VaultUserSecret [-Target] t [-Key] k [-Entry n[#part] \| -FromFile] [-Personal]` | `lv user-secrets set t k [-Entry n] [-Personal]`, `lv user-secrets pick` | keeps the value of key `k` in the vault, tracks it in localvault.json and writes it to the id's `secrets.json` |
| `Get-VaultUserSecret [-Target] t [-Key] k`, `Remove-VaultUserSecret [-Target] t [-Key] k` | `lv user-secrets get t k`, `lv user-secrets untrack t k` | the tracked value; stop tracking a key |
| `Write-VaultUserSecret [[-Target] t] [-Overwrite]`, `Clear-VaultUserSecret [[-Target] t]` | `lv user-secrets load [t] [-Overwrite]`, `lv user-secrets clear [t]` | writes the tracked values, or removes those still equal to the vault's; `lv load` writes them too |
| `New-VaultSshKey -Name n [-HostName h,...] [-Shared] [-Replace]` | `lv ssh-key new n [-HostName h]... [-Shared] [-Replace]` | SSH key pair in `~/.ssh/<repository>/`; personal unless `-Shared` |
| `Get-VaultSshKey`, `Get-VaultSshKeyPath -Name n`, `Set-VaultSshKeyHost -Name n -HostName h,...` | `lv ssh-key list`, `lv ssh-key path n`, `lv ssh-key hosts n h...` | keys and fingerprints, never a private key; the key file to use; the hosts of a key |
| `Invoke-VaultSsh -Name n [-Target h]`, `Invoke-VaultScp -Name n -ArgumentList ...` | `lv ssh n [h] [args]`, `lv scp n args` | ssh and scp with the key, and the old key while it is kept |
| `Set-VaultSshInclude -Enabled $true`, `Set-VaultSshTools [-Directory d]` | `lv ssh-key include on\|off`, `lv ssh-key tools [d]` | `Include ~/.ssh/*/lv.config` in `~/.ssh/config`; the OpenSSH folder |
| `Remove-VaultSshOrphan` | `lv ssh-key clean` | deletes the keys of clones that no longer exist, after one confirmation |
| `Clear-Vault` | `lv clear` | deletes the vault and its previous version, the files written from its templates, its values in .NET user secrets and the SSH keys no other clone uses |

`lv` alone shows the installed release, lists its commands and names the vault it would use (`lv -Version`: the release only); a command used with a wrong syntax prints its usage. Parameters follow PowerShell: `-Name`, any unique prefix, `-Switch:$false`, lists as `a,b`. Tab completes commands, parameters and entry names. Every vault cmdlet takes `-Path`;
`lv` takes `-Path`. `lv` exits 0, 1 on error, 2 when an import
has conflicts nobody resolved.

Secrets never go on a command line: pass a SecureString, pipe one, answer the prompt, or redirect
input. Values are never printed, logged or written outside the sealed files.

## Import

Entries that are identical are skipped silently, missing ones are added. For each entry that
differs you are asked, and the default is to keep yours. `-Replace a,b` and `-ReplaceAll` answer in
advance. Without a console and with an unanswered conflict, nothing is written.

## Stores

A store holds what the team shares; today a store is a KeePass database. Every entry is shared unless
set with `-Local` (`lv mark <name> local` later).

```powershell
Register-VaultStore -Name team -Target D:\KeePass\team.kdbx [-KeyFile D:\KeePass\team.keyx] -Default
Set-VaultProjectStore team             # lv store use team: written to localvault.json, commit it
Export-VaultToStore                    # lv save: every vault of the repository, one password per store
Import-VaultFromStore                  # lv load: on a new machine, or to get a teammate's changes
```

Stores are defined on two levels, read in this order. Common stores belong to this PC (`lv store add`,
kept in `%LOCALAPPDATA%\LocalVault\stores.json`): your team database, your personal one. Project
stores belong to the project (kept in `localvault.json`), so every clone has them: either a name and a
target (`lv store add <kdbx> -Name <name> -Project`), or a name only (`lv store add -Name <name> -Project`), which
refers to the common store of that name on each PC. Key files, if any, come from a common store with
the same target.

Any store, at either level, can be a mirror (`-Mirror`): every `lv save` also writes it, after the
primary store. A mirror of this PC receives every vault saved here (`lv store add <kdbx> -Name mine
-Mirror`: a private copy of all your projects); a mirror of the project receives the project's
vaults on every PC that defines it. In a mirror the vault's value always wins; changes made directly
there are overwritten and reported. An unreachable mirror gives a warning and the save goes on.
`lv load` reads the primary store only (`-Store <mirror>` to read a mirror). Each KeePass file is
opened and saved once per run, and each vault written once.

```powershell
lv store add -Name team -Project -Default            # each PC's own "team" store is the primary
lv store add I:\Archive\proj.kdbx -Name archive -Project -Mirror
```

`localvault.json` lists the vaults of the repository (LocalVault adds each vault it writes) and the
project's primary store (`lv store use <name>`), recorded by name and internal id, so that a
colleague's differently named copy of the same database is found. A single vault can have its own
primary store (`lv store use <name> -Path <vault>`), for the rare vault that belongs elsewhere. `lv save` and `lv load` handle every vault,
opening each store once; a vault whose store isn't defined on this PC is skipped with a message. A
command run in a console that needs an entry this PC doesn't have loads the vaults from their stores
first. A store defined with `-UnlockWith <store>` (`lv store add <kdbx> -Name team -UnlockWith mine`)
opens with the password kept in that other store's `AutoOpen` group, the same convention KeePassXC
uses, so that only your own password is asked; LocalVault offers to add it there the first time.
Chains work: `proj` unlocked by `team`, `team` by `mine`.

Required entries (`lv require <name> [-Credential]`) share their declaration only. Each person's
value goes to their personal store, when the PC names one (`lv store set mine -Personal`, usually
together with `-Mirror`): `lv save` writes it there, `lv load` reads it back. After loading, `lv load`
asks in a console for each required value still missing and offers to save what was typed to the
personal store. A required file under your profile (`lv require kube -StoredPath ~/.kube/config-webshop`,
then `lv set kube -FilePath ~/.kube/config-webshop`) is written there by `lv load` when missing, readable
by your account only. A new machine then needs `lv load` and one password.

Entries become ordinary KeePass entries in `LocalVault/<repository>[/<folder of the vault>]`, titled
with their full name (`common/db.password`), readable without LocalVault. A change made on one side
only goes across without a question; a change made on both sides is asked about (`-Replace`,
`-ReplaceAll` answer in advance). Removals go both ways; a removal in the store is asked about. KeePass
keeps earlier values in each entry's history. `lv list` warns while shared changes aren't saved.

KeePass access is built in (KeePassLib from KeePass 2, GPL-2.0-or-later, in `vendor/KeePassLib`).
If KeePass 2 has the same database open, reload it there before saving, or it may overwrite the
changes.

## Files that need secrets

A tool that reads its secrets from a file (`.env`, `clouds.yaml`, `terraform.tfvars`) gets a
template: `lv template add infra/.env -Capture OS_PASSWORD` writes `infra/.env.template`, to commit,
with `{{OS_PASSWORD}}` where the value was, and moves the value into the vault. The real file stays
git-ignored; `lv template render` writes it, `lv template clear` deletes it, and
`lv run terraform apply` keeps it only while the command runs. Edit the real file and run
`lv template update` to carry the change into the template.

## SSH keys

`lv ssh-key new deploy -HostName deploy@web1.example.com` creates a key pair for SSH to the project's
hosts, never for git. The files are `~/.ssh/<repository>/deploy` and `deploy.pub`, the same path in
every clone; the public key is printed and copied to the clipboard. A personal key, the default, goes
to each person's personal store and its declaration to the team store; `lv load` on another PC asks
for a key from the SSH agent, a private key in a KeePass entry, or a new pair, and uses without a
question a key that another clone already wrote. A shared key (`-Shared`) goes to the team store.
`lv ssh deploy` and `lv scp deploy ...` connect with the key; `lv ssh-key include on` lets plain
`ssh` find it. `-Replace` keeps the old key as `deploy.old` until `lv load` finds that every host
accepts the new one. `lv clear` deletes the keys no other clone uses; `lv ssh-key clean` deletes the
keys of clones deleted without it.

## Old backups

LocalVault makes no automatic backups. Releases up to 1.0.0-beta14 could write them to
`%OneDrive%\LocalVault\<project>-<id>-<machine>\`. Every command that opens a vault looks for them;
in a console it offers to delete them, without one it warns. `lv clear` deletes them with the vault.
`Import-Vault -Source <file.lvbak>` restores one until then.
## How a vault gets lost

The vault is one file in the project folder, readable only by your Windows account on this PC.
It is gone, with every secret in it, when:

- `git clean -fdx` runs, the folder is deleted, or the project is cloned again or used from another
  worktree;
- the disk or the PC is lost;
- an administrator resets your Windows password (a change you make yourself is fine), or your
  profile is rebuilt: DPAPI can then no longer open it.

Shared entries survive in the store, personal values in a personal store. For the rest, export after every change
and keep the export off this machine. Until then `Get-VaultEntryList` warns.

## Development

```powershell
.\tools\test.ps1    # build, xUnit, PSScriptAnalyzer, Pester
```

The assemblies form three layers. `LocalVault.Core` is the shared contract and references no other
LocalVault assembly. `LocalVault.Engine` and each store assembly reference Core only.
`LocalVault.PowerShell` references all of them and picks the store implementation by store type.

| Folder | Holds |
|---|---|
| `src/LocalVault.Core/Entries` | entry names, `VaultEntry`, TOTP, exceptions |
| `src/LocalVault.Core/Stores` | store interfaces and the types they exchange, store types |
| `src/LocalVault.Core/Ssh` | SSH key information and OpenSSH private key parsing |
| `src/LocalVault.Engine/Vault` | body, the vault file and its location |
| `src/LocalVault.Engine/Files` | envelope format, project file, PC settings, templates, .NET projects and user secrets, other files LocalVault writes |
| `src/LocalVault.Engine/Transfer` | export, import, `.clixml` migration |
| `src/LocalVault.Engine/Stores` | store registry and resolution, sync |
| `src/LocalVault.Engine/Hello` | Windows Hello keys and sealing |
| `src/LocalVault.Engine/Ssh` | SSH key files, generation, adoption, SSH agent keys, ssh and scp command lines, `lv.config` |
| `src/LocalVault.Engine/Processes` | git and other external programs |
| `src/LocalVault.KeePass` | KeePass store over KeePassLib |
| `src/LocalVault.PowerShell/Cmdlets` | cmdlets, one file per area |
| `src/LocalVault.PowerShell/Stores` | opening stores, save and load across a repository's vaults |
| `src/LocalVault.PowerShell/Support` | clipboard, secret conversion, completion, legacy reader |
| `src/LocalVault.PowerShell` | `LocalVault.psd1`, `LocalVault.Cli.psm1` (the `lv` command) |
| `tests/LocalVault.Engine.Tests` | xUnit for Engine and Core, same folders |
| `tests/LocalVault.KeePass.Tests` | xUnit against temporary `.kdbx` files |
| `tests/pester` | end-to-end tests of the module and `lv` |

Each assembly has one namespace, named after it. Folders don't add namespaces.

The build needs Microsoft.PowerShell.PlatyPS 1.0.3. Help is written in `docs/LocalVault/` and compiled
into the module at build time; a new command or parameter needs its text there, or the help tests
fail. `tools/docs.ps1` regenerates `docs/reference.md` from those pages; the tests fail when it is
out of date.

Releasing: raise `Prerelease` (or `ModuleVersion`) in `src/LocalVault.PowerShell/LocalVault.psd1`,
commit, push, then:

```powershell
.\tools\release.ps1    # tests and builds the pushed commit in a clean checkout, signs, tags, publishes
```

The release is built from a clean checkout, so a file that works here but was never committed fails there.
`release.ps1` signs every DLL and PowerShell file through Azure Key Vault and uploads `LocalVault.zip`, its
SHA-256 and `install.ps1` to the GitHub release. `install.ps1` checks the hash and refuses files not signed
by GIT. Releasing needs `az login`, `azuresigntool` and `gh`. There is no CI pipeline.

Design and invariants: [SPEC.md](SPEC.md).

## Licence

LocalVault is free software: you can redistribute it and/or modify it under the terms of the GNU
General Public License as published by the Free Software Foundation, either version 3 of the License,
or (at your option) any later version. See [LICENSE](LICENSE).

It includes KeePassLib from [KeePass](https://keepass.info) by Dominik Reichl, GPL-2.0-or-later,
unmodified in `vendor/KeePassLib` (see its `License.txt`).
