---
document type: cmdlet
external help file: LocalVault.PowerShell.dll-Help.xml
HelpUri: https://github.com/GIT-S-A/LocalVault/blob/main/docs/LocalVault/Export-Vault.md
Locale: en-US
Module Name: LocalVault
ms.date: 10/08/2026
PlatyPS schema version: 2024-05-01
title: Export-Vault
---

# Export-Vault

## SYNOPSIS

Exports every entry of a vault to a file sealed with a passphrase.

## SYNTAX

### __AllParameterSets

```
Export-Vault [-Destination] <string> [-Passphrase <securestring>] [-Overwrite] [-Path <string>]
 [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

The `Export-Vault` cmdlet writes all secrets and files of the vault to one file in the
`LOCALVAULT-2` export format, sealed with a passphrase of at least 12 characters. `Import-Vault`
opens the file on any machine.

The cmdlet asks for the passphrase twice. Without a console, it reads the **Passphrase** parameter
or one line of redirected standard input. The cmdlet verifies the file after it writes it, and
records the time of the export in the vault.

An entry sealed with Windows Hello is written in plain form, marked as such. When the vault holds
one, the cmdlet asks for Windows Hello once. An entry that this PC can't unseal makes the cmdlet
fail before it writes the file; `Import-VaultFromStore` restores it first.

## EXAMPLES

### Example 1 - Export the vault to a USB drive

```powershell
Export-Vault -Destination E:\handover\obs.export
```

```Output
Passphrase: at least 12 characters. It cannot be recovered; store it in KeePass now.
Passphrase: ****************
Passphrase again: ****************
Written: E:\handover\obs.export (7 entries). Keep it off this machine and out of git.
```

### Example 2 - Overwrite an export with a passphrase from a variable

```powershell
$passphrase = Read-Host -Prompt 'Export passphrase' -AsSecureString
Export-Vault -Destination \\nas\keys\obs.export -Passphrase $passphrase -Overwrite
```

## PARAMETERS

### -Destination

Specifies the path of the export file. A relative path is resolved from the current location. The
cmdlet fails when the file exists, unless you use the **Overwrite** parameter.

```yaml
Type: System.String
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: 0
  IsRequired: true
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -Overwrite

Indicates that the cmdlet overwrites an existing export file.

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

### -Passphrase

Specifies the passphrase as a **SecureString** of at least 12 characters. If you omit this
parameter, the cmdlet asks for it twice.

```yaml
Type: System.Security.SecureString
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

This cmdlet returns no output.

## NOTES

The passphrase can't be recovered.

The cmdlet fails when the vault is empty.

`lv export <file> [-Overwrite]` runs this cmdlet.

## RELATED LINKS

- [Import-Vault](Import-Vault.md)
- [about_LocalVault](about_LocalVault.md)
