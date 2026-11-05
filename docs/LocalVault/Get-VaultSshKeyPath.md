---
document type: cmdlet
external help file: LocalVault.PowerShell.dll-Help.xml
HelpUri: https://github.com/GIT-S-A/LocalVault/blob/main/docs/LocalVault/Get-VaultSshKeyPath.md
Locale: en-US
Module Name: LocalVault
ms.date: 11/05/2026
PlatyPS schema version: 2024-05-01
title: Get-VaultSshKeyPath
---

# Get-VaultSshKeyPath

## SYNOPSIS

Gets the path of the key file of an SSH key entry.

## SYNTAX

### __AllParameterSets

```
Get-VaultSshKeyPath [-Name] <string> [-Path <string>] [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

The `Get-VaultSshKeyPath` cmdlet gets the full path of the current key file of an SSH key entry, for
tools that take the key with `-i`. For a key held by an SSH agent, the path is the `.pub` file. When
the file is missing, the cmdlet writes it first, with the same checks as
[Restore-VaultFile](Restore-VaultFile.md). In an interactive console, a personal key not set on this
PC is loaded from the stores first, as other reads do.

## EXAMPLES

### Example 1 - Run scp with the key

```powershell
scp -i (Get-VaultSshKeyPath -Name deploy) -o IdentitiesOnly=yes backup.tar deploy@srv1.example.com:
```

## PARAMETERS

### -Name

Specifies the name of the SSH key entry.

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

### System.String

This cmdlet returns the full path of the key file.

## NOTES

`lv ssh-key path <name>` runs this cmdlet.

## RELATED LINKS

- [Get-VaultSshKey](Get-VaultSshKey.md)
- [New-VaultSshKey](New-VaultSshKey.md)
