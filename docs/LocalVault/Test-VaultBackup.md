---
document type: cmdlet
external help file: LocalVault.PowerShell.dll-Help.xml
HelpUri: https://github.com/GIT-S-A/LocalVault/blob/main/docs/LocalVault/Test-VaultBackup.md
Locale: en-US
Module Name: LocalVault
ms.date: 10/08/2026
PlatyPS schema version: 2024-05-01
title: Test-VaultBackup
---

# Test-VaultBackup

## SYNOPSIS

Verifies that the newest backup of a vault opens.

## SYNTAX

### VaultKey (Default)

```
Test-VaultBackup [-Path <string>] [<CommonParameters>]
```

### Manual

```
Test-VaultBackup -Manual -KeyFile <string> [-Passphrase <securestring>] [-Path <string>]
 [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

The `Test-VaultBackup` cmdlet finds the newest backup file of the vault in the backup folder and
opens it. By default, it uses the backup key kept in the vault and asks for nothing. With the
**Manual** parameter, you give the keyfile and the passphrase, which checks the copy that you keep
yourself.

The cmdlet doesn't change the vault and doesn't import anything. It fails when backups aren't
enabled, when there is no backup file, or when the backup doesn't open.

## EXAMPLES

### Example 1 - Test the newest backup

```powershell
Test-VaultBackup
```

```Output
C:\Users\me\OneDrive\LocalVault\obs-1a2b3c4d-PC042\20261008T101502117Z-0ec6df6e042491df.lvbak opens with the key kept in this vault: 7 entries.

Backup                                                                                        Entries Taken
------                                                                                        ------- -----
C:\Users\me\OneDrive\LocalVault\obs-1a2b3c4d-PC042\20261008T101502117Z-0ec6df6e042491df.lvbak       7 08/10/2026 10:15:02
```

### Example 2 - Test with a keyfile you keep yourself

```powershell
Test-VaultBackup -Manual -KeyFile E:\keys\obs.lvkey
```

The cmdlet asks for the backup passphrase.
## PARAMETERS

### -KeyFile

Specifies the keyfile of the backups.

```yaml
Type: System.String
DefaultValue: None
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

### -Manual

Indicates that you supply the passphrase and the keyfile instead of the backup key kept in the vault.

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

Specifies the backup passphrase as a **SecureString**. If you omit this parameter, the cmdlet asks
for it, or reads one line from redirected standard input.

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

### LocalVault.PowerShell.BackupTestResult

This cmdlet returns the path of the **Backup** it opened, the number of **Entries** in it, and the
time the backup was **Taken**.

## NOTES

`lv backup test` and `lv backup test --manual --keyfile <file>` run this cmdlet.

## RELATED LINKS

- [Enable-VaultBackup](Enable-VaultBackup.md)
- [Import-Vault](Import-Vault.md)
