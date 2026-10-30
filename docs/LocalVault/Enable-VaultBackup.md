---
document type: cmdlet
external help file: LocalVault.PowerShell.dll-Help.xml
HelpUri: https://github.com/GIT-S-A/LocalVault/blob/main/docs/LocalVault/Enable-VaultBackup.md
Locale: en-US
Module Name: LocalVault
ms.date: 10/08/2026
PlatyPS schema version: 2024-05-01
title: Enable-VaultBackup
---

# Enable-VaultBackup

## SYNOPSIS

Turns on automatic backups of a vault.

## SYNTAX

### Generated (Default)

```
Enable-VaultBackup [-Destination <string>] [-Path <string>] [<CommonParameters>]
```

### Manual

```
Enable-VaultBackup -Manual [-Destination <string>] [-KeyFile <string>] [-KeyFileOut <string>]
 [-Passphrase <securestring>] [-Path <string>] [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

The `Enable-VaultBackup` cmdlet stores backup settings in the vault and writes a first backup.
After that, every write to the vault also writes a backup file, `<time>-<backup id>.lvbak`, to the
destination folder. The backup id is random and is stored in the backup settings. The folder keeps
the 10 newest backups of the vault and its newest backup of each of the last 30 days. Files of other
vaults, and files with other names, stay.

A backup is sealed with a passphrase and a 32-byte keyfile, the key of the backups. By default, the
cmdlet generates both and keeps them in the vault, and asks for nothing. The backups are this
machine's automatic saves: they cover a deleted vault file or a cleaned folder. To share entries or
recover them on another machine, use `Export-VaultToStore`.

With the **Manual** parameter, you give the passphrase, and an existing keyfile or a path for a new
one. You keep both yourself.

When backups are already on, the cmdlet shows their state and changes nothing. To change the
folder or the key, run `Disable-VaultBackup` first.

A failed backup doesn't stop the write to the vault: the cmdlet that wrote writes a warning, and
`Get-VaultEntryList` warns until a backup succeeds.

## EXAMPLES

### Example 1 - Back up to OneDrive

```powershell
Enable-VaultBackup
```

```Output
Backups on. Every change to the vault now also writes a backup to C:\Users\me\OneDrive\LocalVault\obs-1a2b3c4d-PC042.
First backup: C:\Users\me\OneDrive\LocalVault\obs-1a2b3c4d-PC042\20261008T091240512Z-0ec6df6e042491df.lvbak
```

### Example 2 - Back up to a network share

```powershell
Enable-VaultBackup -Destination \\nas\backup\obs
```

### Example 3 - Back up with your own passphrase and keyfile

```powershell
Enable-VaultBackup -Manual -KeyFileOut E:\keys\obs.lvkey
```

The cmdlet writes a new keyfile to `E:\keys\obs.lvkey` and asks for a passphrase twice.
## PARAMETERS

### -Destination

Specifies the folder for the backup files. If you omit this parameter, the cmdlet uses
`%OneDrive%\LocalVault\<project>-<id>-<machine>`, and fails when OneDrive isn't set up.

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

### -KeyFile

Specifies an existing keyfile to seal the backups with.

```yaml
Type: System.String
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: Manual
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -KeyFileOut

Specifies where to write a new keyfile. The cmdlet fails when the file exists. If you omit both
this parameter and **KeyFile**, the cmdlet asks for the path.

```yaml
Type: System.String
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: Manual
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -Manual

Indicates that you supply the passphrase and the keyfile instead of the cmdlet generating them.

```yaml
Type: System.Management.Automation.SwitchParameter
DefaultValue: False
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: Manual
  Position: Named
  IsRequired: true
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -Passphrase

Specifies the backup passphrase as a **SecureString** of at least 12 characters. If you omit this
parameter, the cmdlet asks for it twice.

```yaml
Type: System.Security.SecureString
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: Manual
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

### CommonParameters

This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable,
-InformationAction, -InformationVariable, -OutBuffer, -OutVariable, -PipelineVariable,
-ProgressAction, -Verbose, -WarningAction, and -WarningVariable. For more information, see
[about_CommonParameters](https://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

### None

You can't pipe objects to this cmdlet.

## OUTPUTS

### None

This cmdlet writes messages only. To read the state of the backups, use `Get-VaultBackup`.

## NOTES

When a backup fails, the write still succeeds. The vault records the error, and
`Get-VaultEntryList` writes a warning.

`lv backup enable [--destination <folder>]` and
`lv backup enable --manual [--keyfile <file> | --keyfile-out <file>]` run this cmdlet.

## RELATED LINKS

- [Get-VaultBackup](Get-VaultBackup.md)
- [Test-VaultBackup](Test-VaultBackup.md)
- [Invoke-VaultBackup](Invoke-VaultBackup.md)
- [Disable-VaultBackup](Disable-VaultBackup.md)
- [Import-Vault](Import-Vault.md)
- [about_LocalVault](about_LocalVault.md)
