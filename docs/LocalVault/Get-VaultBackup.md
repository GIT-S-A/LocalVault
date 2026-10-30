---
document type: cmdlet
external help file: LocalVault.PowerShell.dll-Help.xml
HelpUri: https://github.com/GIT-S-A/LocalVault/blob/main/docs/LocalVault/Get-VaultBackup.md
Locale: en-US
Module Name: LocalVault
ms.date: 10/09/2026
PlatyPS schema version: 2024-05-01
title: Get-VaultBackup
---

# Get-VaultBackup
## SYNOPSIS

Gets the state of a vault's automatic backups.

## SYNTAX

### __AllParameterSets

```
Get-VaultBackup [-Path <string>] [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

The `Get-VaultBackup` cmdlet reads the backup settings of the vault: the backup folder, the newest
backup and the last error. It asks for nothing. When backups are off, it returns nothing and says
so.

## EXAMPLES

### Example 1 - Find the newest backup

```powershell
(Get-VaultBackup).Newest
```
## PARAMETERS
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
### CommonParameters

This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable,
-InformationAction, -InformationVariable, -OutBuffer, -OutVariable, -PipelineVariable,
-ProgressAction, -Verbose, -WarningAction, and -WarningVariable. For more information, see
[about_CommonParameters](https://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

### None

You can't pipe objects to this cmdlet.
## OUTPUTS

### LocalVault.PowerShell.BackupStatus

This cmdlet returns the backup **Destination**, the **Newest** backup file, the time of the
**LastBackup**, and the **LastError** if the last backup failed.

## NOTES

`lv backup` without a subcommand shows this state.
## RELATED LINKS

- [Enable-VaultBackup](Enable-VaultBackup.md)
- [Test-VaultBackup](Test-VaultBackup.md)