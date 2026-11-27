---
document type: cmdlet
external help file: LocalVault.PowerShell.dll-Help.xml
HelpUri: https://github.com/GIT-S-A/LocalVault/blob/main/docs/LocalVault/Invoke-LocalVault.md
Locale: en-US
Module Name: LocalVault
ms.date: 11/27/2026
PlatyPS schema version: 2024-05-01
title: Invoke-LocalVault
---

# Invoke-LocalVault

## SYNOPSIS

Runs a LocalVault command from a short command line.

## SYNTAX

### __AllParameterSets

```
Invoke-LocalVault [<command>] [<words>] [-Path <vault>] [-<Parameter> [<value>] ...]
```

## ALIASES

This function has the following aliases:

- `lv`

## DESCRIPTION

The `Invoke-LocalVault` function runs a LocalVault cmdlet from a short command. Its parameters follow
the PowerShell syntax:

- A parameter is `-Name`, case-insensitive, or any prefix of it that no other parameter of the
  command shares.
- A switch is `-Mirror`; `-Mirror:$false` turns the setting off.
- A list is `-Replace a,b`.
- A parameter name is the name of the cmdlet's parameter, except `-Show` and `-Version`, which only
  `lv` has.
- A word without a dash is positional. Only required words, and at most one optional word or list
  at the end, are positional. Each positional word can also be given by its parameter name, such
  as `lv get -Name db.password`.

Every command takes `-Path <vault>`. A word that starts with `--` fails, names the new form of the
option when it is a known one, such as `--keyfile is now -KeyFile`, and prints the usage line.

Without a command, or with `help`, the function shows the installed release, lists the commands
and names the vault that they would use, without opening it. `lv -Version` prints the installed
release, such as `LocalVault 1.0.0-beta20`. `store` alone shows each vault's store and what isn't
saved to it yet, followed by a `Stores:` line that lists every store with its level and flags.
`template` alone shows each templated file and whether it is written and up to date. `start` reads
the vault, the repository and the stores, without opening a store, and prints the state and the
commands to run next, ready to paste. When `list` finds no entry, it says that the vault is
missing or empty.

| Command | Words and parameters | Cmdlet |
| --- | --- | --- |
| `help` | | |
| `start` | | where the project stands and the commands to run next |
| `init` | | `Initialize-Vault` |
| `list` | `[<scope>]` | `Get-VaultEntryList` |
| `get` | `<name>[#<part>] [-Show]` | `Get-VaultEntry -AsPlainText` |
| `clip` | `<name>[#<part>]` | `Copy-VaultEntry` |
| `set` | `<name> [-FilePath <path> \| -Credential] [-Local] [-Hello]` | `Set-VaultEntry` |
| `set` | `<name>#<part>` | `Set-VaultEntry`: one part of a credential |
| `set` | `<name> -Reference <group/title> [-ReferenceStore <store>]` | `Set-VaultEntry -Reference` |
| `require` | `<name> [-Credential \| -StoredPath <path>] [-Note <text>] [-Hello]` | `Set-VaultEntry -Required` |
| `mark` | `<name> shared\|local` | `Set-VaultEntry -Sharing` |
| `mark` | `<name> hello\|nohello` | `Set-VaultEntry -Protection Hello\|Dpapi` |
| `hello` | | `Get-VaultHelloStatus` |
| `rm` | `<name>` | `Remove-VaultEntry` |
| `reset` | `<name> ... [-NoPrompt]` | `Reset-VaultEntry` |
| `env` | `<variable>=<name> ...` | `Use-VaultEnv` |
| `restore` | `[<name> ...] [-Overwrite]` | `Restore-VaultFile` |
| `clear` | | `Clear-Vault` |
| `save` | `[-Store <name>] [-Replace a,b] [-ReplaceAll]` | `Export-VaultToStore`, every vault of the repository |
| `load` | `[-Store <name>] [-Replace a,b] [-ReplaceAll]` | `Import-VaultFromStore`, every vault of the repository |
| `export` | `<file> [-Overwrite]` | `Export-Vault` |
| `import` | `<file> [-Replace a,b] [-ReplaceAll] [-KeyFile <file>]` | `Import-Vault` |
| `store` | | `Get-VaultStoreStatus` |
| `store list` | `[<name>]` | `Get-VaultStore` |
| `store add` | `[<target>] [-Name <name>] [-KeyFile <file>] [-Default] [-UnlockWith <store>] [-Mirror] [-Personal] [-Project] [-Source]` | `Register-VaultStore`; with `-Project` and no target, the name refers to each PC's store of that name |
| `store set` | `<name> [-Mirror[:$false]] [-UnlockWith <store> \| -NoUnlock] [-Default] [-Personal[:$false]] [-Project] [-Source[:$false]]` | `Set-VaultStore`: changes the settings given only |
| `store rm` | `<name> [-Project]` | `Unregister-VaultStore` |
| `store use` | `<name>` | `Set-VaultProjectStore`: the project's primary store (one vault only with `-Path`) |
| `store diff` | `[-Store <name>]` | `Compare-VaultWithStore` |
| `store copy` | `[<target \| store name>] [-Store <name>] [-KeyFile <file>]` | `Copy-VaultToStore` |
| `template` | | `Get-VaultTemplate` |
| `template add` | `<file> [-Capture KEY,...]` | `Add-VaultTemplate` |
| `template update` | `[<file>] [-AcceptChanges]` | `Update-VaultTemplate` |
| `template render` | `[<file>]` | `Write-VaultTemplate` |
| `template clear` | `[<file>]` | `Clear-VaultTemplate` |
| `template rm` | `<file>` | `Remove-VaultTemplate` |
| `run` | `[<variable>=<name> ...] <command> [<arguments> ...]` | sets the variables for the command only, writes every templated file of the repository, runs the command, then deletes the written files and restores the variables, even when the command fails; `$LASTEXITCODE` is the command's |
| `run` | `[<variable>=<name> ...] -Stdin <name> <program> [<arguments> ...]` | as `run`, and also writes the value to the program's standard input, then closes it: a secret as UTF-8, a file as its bytes, a credential's password; the value never becomes a string, a file or a word of the command line; the program must be an executable file |
| `ssh-key` | | `Get-VaultSshKey` |
| `ssh-key new` | `<name> [-HostName <user@host>,...] [-Shared] [-Type ed25519\|rsa\|ecdsa] [-Comment <text>] [-Overwrite] [-Replace]` | `New-VaultSshKey` |
| `ssh-key hosts` | `<name> [<user@host> ...]` | `Set-VaultSshKeyHost`; no host removes them |
| `ssh-key path` | `<name>` | `Get-VaultSshKeyPath` |
| `ssh-key tools` | `[<dir> \| -Default]` | `Set-VaultSshTools` |
| `ssh-key include` | `on\|off` | `Set-VaultSshInclude` |
| `ssh-key clean` | | `Remove-VaultSshOrphan` (asks one confirmation) |
| `ssh` | `<name> [<user@host>] [<ssh arguments>]` | `Invoke-VaultSsh`: the second word is `-Target` when it holds `@` or is one of the key's hosts; the other words are `-ArgumentList` |
| `scp` | `<name> <scp arguments>` | `Invoke-VaultScp` (`-ArgumentList`) |
| `user-secrets` | `[list] [<id\|project>]` | `Get-VaultUserSecretList` |
| `user-secrets set` | `<id\|project> <key> [-Entry <name[#part]>] [-Personal]` | `Set-VaultUserSecret`; the value is asked, or read from redirected input |
| `user-secrets get` | `<id\|project> <key> [-Show]` | `Get-VaultUserSecret`, with the output rules of `get` |
| `user-secrets pick` | | in a console, lists the ids with their projects, then the keys of that `secrets.json` not tracked yet and a new key; runs `Set-VaultUserSecret` with the choice |
| `user-secrets untrack` | `<id\|project> <key>` | `Remove-VaultUserSecret` |
| `user-secrets load` | `[<id\|project>] [-Overwrite]` | `Write-VaultUserSecret`; exit code 2 when values differ without a console |
| `user-secrets clear` | `[<id\|project>]` | `Clear-VaultUserSecret` |
| `update` | `[<tag>] [-Downgrade]` | downloads the newest GitHub release (or the tag), checks its SHA256 and signatures, and installs it with the installed module's `install.ps1`; says it is up to date and does nothing when that release is installed; refuses an older release without `-Downgrade` |

In `store copy`, a word that holds a path separator, ends in `.kdbx` or starts with a `type:` prefix
is a target; any other word is a store name.

`run`, `ssh` and `scp` pass words on to another program. Their own parameters, `-Path`, `-Stdin`
and `-Target`, are recognized by their full name and only before the first word passed on; every
word after it goes to the program as typed.

A credential keeps a user name, a password, a TOTP and URLs as one entry. `<name>#user`,
`#password`, `#totp`, `#url`, `#url2` and so on name one part in `get`, `clip`, `set`, `env`, `run`
and template placeholders (`{{github#user}}`); `#totp` is the current code, and the name alone is the
password.

`get` prints a value on screen only with `-Show`, or when it's a user name or a URL; otherwise its
output must be a pipeline or a redirection. A command given too many or too few words, a parameter
that it doesn't take, or an unknown subcommand fails, does nothing, and prints its usage line.
Secret values are never words of a command.

Tab completion offers the commands, their subcommands, the parameters of the command after `-`,
entry names of the vault, the states after `mark <name>`, and store names after `-Store`,
`-UnlockWith`, `-ReferenceStore`, `store rm`, `store use`, `store set` and `store copy`.

The function sets `$LASTEXITCODE` to `0` on success, `1` on an error, and `2` when an import has
unanswered differences, from a file or from a store.

## EXAMPLES

### Example 1 - Store a secret in the vault of a subfolder

```powershell
lv set LOKI_PUSH_TOKEN -Path infra/secrets.local.lv
```

`lv` asks for the value.

### Example 2 - Add a personal KeePass database that mirrors every save

```powershell
lv store add C:\Users\me\OneDrive\KeePass\mine.kdbx -KeyFile D:\keys\mine.key -Mirror -Personal
```

### Example 3 - Run docker compose with a templated .env that exists only while it runs

```powershell
lv run AZP_TOKEN=AZP_TOKEN docker compose up --build
```

### Example 4 - Import from another shell and check the exit code

```powershell
pwsh -NoProfile -Command 'lv import handover.export -ReplaceAll'
if ($LASTEXITCODE -ne 0) { throw "Import failed with exit code $LASTEXITCODE." }
```

## PARAMETERS

## INPUTS

### None

You can't pipe objects to this function.

## OUTPUTS

### System.Object

This function returns the output of the cmdlet it runs. `list`, `store`, `store list` and `store diff` return
formatted tables.

## NOTES

`Invoke-LocalVault` reads every word itself. It has no parameter of its own, so PowerShell's common
parameters don't apply to it.

## RELATED LINKS

- [Get-VaultEntryList](Get-VaultEntryList.md)
- [Set-VaultEntry](Set-VaultEntry.md)
- [Import-Vault](Import-Vault.md)
- [Export-VaultToStore](Export-VaultToStore.md)
- [Register-VaultStore](Register-VaultStore.md)
- [about_LocalVault](about_LocalVault.md)
