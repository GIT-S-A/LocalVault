---
document type: cmdlet
external help file: LocalVault.PowerShell.dll-Help.xml
HelpUri: https://github.com/GIT-S-A/LocalVault/blob/main/docs/LocalVault/Restore-VaultFile.md
Locale: en-US
Module Name: LocalVault
ms.date: 10/08/2026
PlatyPS schema version: 2024-05-01
title: Restore-VaultFile
---

# Restore-VaultFile

## SYNOPSIS

Writes file entries of a vault back to disk.

## SYNTAX

### Name (Default)

```
Restore-VaultFile [-Name] <string[]> [-Destination <string>] [-Overwrite] [-SkipGitIgnoreCheck]
 [-Path <string>]
 [<CommonParameters>]
```

### All

```
Restore-VaultFile -All [-Destination <string>] [-Overwrite] [-SkipGitIgnoreCheck] [-Path <string>]
 [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

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

## EXAMPLES

### Example 1 - Restore every file entry

```powershell
Restore-VaultFile -All -Path .\infra\secrets.local.lv
```

```Output
Name           Path                                Action
----           ----                                ------
clouds.yaml    C:\src\obs\infra\clouds.yaml        Written
s3-backend.tf  C:\src\obs\infra\s3-backend.tf      Identical
```

### Example 2 - Restore one file into another folder

```powershell
Restore-VaultFile -Name clouds.yaml -Destination $env:TEMP\check
```

### Example 3 - Overwrite files that differ, and list what changed

```powershell
Restore-VaultFile -All -Overwrite | Where-Object Action -EQ 'Written'
```

## PARAMETERS

### -All

Indicates that this cmdlet restores every file entry of the vault.

```yaml
Type: System.Management.Automation.SwitchParameter
DefaultValue: False
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: All
  Position: Named
  IsRequired: true
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -Destination

Specifies the folder that stored paths are relative to. If you omit this parameter, the cmdlet
uses the folder of the vault.

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

### -Overwrite

Indicates that the cmdlet overwrites files that differ from the vault, without asking.

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

Specifies the names of the file entries to restore. The cmdlet fails when a name doesn't exist or
refers to a secret.

```yaml
Type: System.String[]
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: Name
  Position: 0
  IsRequired: true
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

### -SkipGitIgnoreCheck

Indicates that the cmdlet writes files that git doesn't ignore inside a repository.

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

### CommonParameters

This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable,
-InformationAction, -InformationVariable, -OutBuffer, -OutVariable, -PipelineVariable,
-ProgressAction, -Verbose, -WarningAction, and -WarningVariable. For more information, see
[about_CommonParameters](https://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

### None

You can't pipe objects to this cmdlet.

## OUTPUTS

### LocalVault.PowerShell.RestoredFile

This cmdlet returns one object for each file, with the **Name** of the entry, the full **Path** of
the file and the **Action**: `Written`, `Identical` or `Kept`.

## NOTES

`lv restore [<name> ...] [--overwrite]` runs this cmdlet. Without a name, it restores every file
entry.

## RELATED LINKS

- [Set-VaultEntry](Set-VaultEntry.md)
- [Get-VaultEntry](Get-VaultEntry.md)
- [Import-Vault](Import-Vault.md)
