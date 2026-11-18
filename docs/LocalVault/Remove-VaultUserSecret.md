---
document type: cmdlet
external help file: LocalVault.PowerShell.dll-Help.xml
HelpUri: https://github.com/GIT-S-A/LocalVault/blob/main/docs/LocalVault/Remove-VaultUserSecret.md
Locale: en-US
Module Name: LocalVault
ms.date: 11/18/2026
PlatyPS schema version: 2024-05-01
title: Remove-VaultUserSecret
---

# Remove-VaultUserSecret

## SYNOPSIS

Stops tracking a .NET user secret.

## SYNTAX

### __AllParameterSets

```
Remove-VaultUserSecret [-Target] <string> [-Key] <string> [-Path <string>] [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

The `Remove-VaultUserSecret` cmdlet removes a key from the tracking of this vault in `localvault.json`. When `secrets.json` still holds the vault value of the key, the cmdlet removes the key from the file. A value changed in the file is kept.

The vault entry stays.

## EXAMPLES

### Example 1 - Stop tracking a key

```powershell
Remove-VaultUserSecret -Target Api -Key Logging:Seq:ApiKey [<CommonParameters>]
```

## PARAMETERS

### -Key

Specifies the tracked configuration key.

```yaml
Type: System.String
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: 1
  IsRequired: true
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -Path

Specifies the path to the vault file, which also names the git repository whose user secrets the cmdlet handles. A relative path is resolved from the current location. If you omit this parameter, the cmdlet uses the `LOCALVAULT_PATH` environment variable, and then `secrets.local.lv` at the root of the git repository that contains the current location. With this parameter or the environment variable, the cmdlet handles only the keys tracked by that vault.

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

### -Target

Specifies the user secrets by the **UserSecretsId** of a project of the repository, or by the name of a project, which is its `.csproj` file name without the extension.

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

`lv user-secrets untrack <id|project> <key>` runs this cmdlet.

## RELATED LINKS

- [Get-VaultUserSecretList](Get-VaultUserSecretList.md)
- [Set-VaultUserSecret](Set-VaultUserSecret.md)
- [Get-VaultUserSecret](Get-VaultUserSecret.md)
- [Remove-VaultUserSecret](Remove-VaultUserSecret.md)
- [Write-VaultUserSecret](Write-VaultUserSecret.md)
- [Clear-VaultUserSecret](Clear-VaultUserSecret.md)
- [about_LocalVault](about_LocalVault.md)
