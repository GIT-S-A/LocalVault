---
document type: cmdlet
external help file: LocalVault.PowerShell.dll-Help.xml
HelpUri: https://github.com/GIT-S-A/LocalVault/blob/main/docs/LocalVault/Invoke-LocalVault.md
Locale: en-US
Module Name: LocalVault
ms.date: 10/13/2026
PlatyPS schema version: 2024-05-01
title: Invoke-LocalVault
---

# Invoke-LocalVault

## SYNOPSIS

Runs a LocalVault command from a short command line.

## SYNTAX

### __AllParameterSets

```
Invoke-LocalVault [<command>] [<arguments>] [--path <vault>] [<options>]
```

## ALIASES

This function has the following aliases:

- `lv`

## DESCRIPTION

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
| `set` | `<name> [--file <path> \| --credential] [--local]` | `Set-VaultEntry` (`-FilePath` with `--file`, `-Credential` with `--credential`) |
| `set` | `<name>#<part>` | `Set-VaultEntry`: one part of a credential |
| `require` | `<name> [--credential \| --file <path>] [--note <text>]` | `Set-VaultEntry -Required [-Credential \| -StoredPath <path>]` |
| `mark` | `<name> shared\|local` | `Set-VaultEntry -Sharing` |
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

## EXAMPLES

### Example 1 - Store a secret in the vault of a subfolder

```powershell
lv set LOKI_PUSH_TOKEN --path infra/secrets.local.lv
```

`lv` asks for the value.

### Example 2 - Set environment variables for a tool

```powershell
lv env OS_PASSWORD=OsPassword AWS_SECRET_ACCESS_KEY=S3SecretKey
```

### Example 3 - Run docker compose with a templated .env that exists only while it runs

```powershell
lv run AZP_TOKEN=AZP_TOKEN -- docker compose up --build
```

### Example 4 - Import from another shell and check the exit code

```powershell
pwsh -NoProfile -Command 'lv import handover.export --replace-all'
if ($LASTEXITCODE -ne 0) { throw "Import failed with exit code $LASTEXITCODE." }
```

## PARAMETERS

### -CommandLine

Specifies the command and its words and options, as typed after `lv`. Tab completion offers the
commands, their subcommands, the options that each command takes, entry names of the vault,
`shared` and `local` after `mark <name>`, and store names after `--store`, `store rm`, `store use`
and `store copy`.

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
  ValueFromRemainingArguments: true
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### CommonParameters

This function supports the common parameters: -Debug, -ErrorAction, -ErrorVariable,
-InformationAction, -InformationVariable, -OutBuffer, -OutVariable, -PipelineVariable,
-ProgressAction, -Verbose, -WarningAction, and -WarningVariable. For more information, see
[about_CommonParameters](https://go.microsoft.com/fwlink/?LinkID=113216).
## INPUTS

### None

You can't pipe objects to this function.

## OUTPUTS

### System.Object

This function returns the output of the cmdlet it runs. `list`, `store`, `store list` and `store diff` return
formatted tables.

## NOTES

The function reads its words and `--` options as typed; options with one dash are PowerShell's common parameters.

## RELATED LINKS

- [Get-VaultEntryList](Get-VaultEntryList.md)
- [Set-VaultEntry](Set-VaultEntry.md)
- [Import-Vault](Import-Vault.md)
- [Export-VaultToStore](Export-VaultToStore.md)
- [Register-VaultStore](Register-VaultStore.md)
- [about_LocalVault](about_LocalVault.md)
