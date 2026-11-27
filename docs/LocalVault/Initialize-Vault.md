---
document type: cmdlet
external help file: LocalVault.PowerShell.dll-Help.xml
HelpUri: https://github.com/GIT-S-A/LocalVault/blob/main/docs/LocalVault/Initialize-Vault.md
Locale: en-US
Module Name: LocalVault
ms.date: 11/27/2026
PlatyPS schema version: 2024-05-01
title: Initialize-Vault
---

# Initialize-Vault

## SYNOPSIS

Adds the repository's vaults and the files written from templates to `.gitignore`.

## SYNTAX

### __AllParameterSets

```
Initialize-Vault [-Path <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

The `Initialize-Vault` cmdlet checks with `git check-ignore` that git ignores these files:

- the vault named by `-Path`, `LOCALVAULT_PATH` or the default location;
- `secrets.local.lv` at the root of the repository;
- every vault listed in `localvault.json`;
- every file that `Write-VaultTemplate` writes from a template.

Only a rule in a `.gitignore` file of the repository counts, because that file is committed and
applies to every clone. A rule in the global git ignore file or in `.git/info/exclude` applies to
this PC only.

For each file that isn't ignored, the cmdlet appends a line to `.gitignore` at the root of the
repository, under a `# LocalVault` line. The `# LocalVault` line is written once; later lines are
added below it.

| File | Line |
| --- | --- |
| Vault named `secrets.local.lv` | `secrets.local.*` |
| Other vault | `/<folder>/<file name>*` |
| File written from a template | `/<folder>/<file name>` |

Paths are relative to the root of the repository. The cmdlet creates `.gitignore` when it's
missing, keeps its existing lines and line endings, and never commits. It doesn't create a vault.
It ends with `Commit .gitignore.`, or `Nothing to add.` when every file is already ignored.

The cmdlet fails outside a git repository.

## EXAMPLES

### Example 1 - Prepare a repository for LocalVault

```powershell
Initialize-Vault
```

```Output
Line            Path
----            ----
secrets.local.* C:\src\obs\secrets.local.lv
Commit .gitignore.
```

### Example 2 - Show the lines without writing them

```powershell
Initialize-Vault -WhatIf
```

## PARAMETERS

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

### LocalVault.PowerShell.VaultGitIgnoreLine

This cmdlet returns one object per line added, with the **Line** and the **Path** of the file it
ignores.

## NOTES

`lv init` runs this cmdlet. In a console, a command that creates a vault git doesn't ignore offers to
add its line first.

## RELATED LINKS

- [Set-VaultEntry](Set-VaultEntry.md)
- [Write-VaultTemplate](Write-VaultTemplate.md)
- [about_LocalVault](about_LocalVault.md)
