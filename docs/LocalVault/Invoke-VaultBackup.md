---
document type: cmdlet
external help file: LocalVault.PowerShell.dll-Help.xml
HelpUri: https://github.com/GIT-S-A/LocalVault/blob/main/docs/LocalVault/Invoke-VaultBackup.md
Locale: en-US
Module Name: LocalVault
ms.date: 10/08/2026
PlatyPS schema version: 2024-05-01
title: Invoke-VaultBackup
---

# Invoke-VaultBackup

## SYNOPSIS

Writes a backup of a vault now.

## SYNTAX

### __AllParameterSets

```
Invoke-VaultBackup [-Path <string>] [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

The `Invoke-VaultBackup` cmdlet writes a backup with the settings that `Enable-VaultBackup` stored.
No entry changes. The cmdlet fails when backups aren't enabled.

## EXAMPLES

### Example 1 - Write a backup and check the result

```powershell
Invoke-VaultBackup | Select-Object Newest, LastError
```

```Output
Newest                                                                     LastError
------                                                                     ---------
C:\Users\me\OneDrive\LocalVault\obs-1a2b3c4d-PC042\20261008T101502117Z.lvbak
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
**LastBackup**, and the **LastError** if the backup failed.

## NOTES

When the backup fails, the cmdlet writes a warning and **LastError** holds the error.

`lv backup now` runs this cmdlet.

## RELATED LINKS

- [Enable-VaultBackup](Enable-VaultBackup.md)
- [Test-VaultBackup](Test-VaultBackup.md)
