---
document type: cmdlet
external help file: LocalVault.PowerShell.dll-Help.xml
HelpUri: https://github.com/GIT-S-A/LocalVault/blob/main/docs/LocalVault/Write-VaultUserSecret.md
Locale: en-US
Module Name: LocalVault
ms.date: 11/18/2026
PlatyPS schema version: 2024-05-01
title: Write-VaultUserSecret
---

# Write-VaultUserSecret

## SYNOPSIS

Writes the tracked values into the .NET user secrets.

## SYNTAX

### __AllParameterSets

```
Write-VaultUserSecret [[-Target] <string>] [-Overwrite] [-Path <string>] [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

The `Write-VaultUserSecret` cmdlet writes the vault value of every tracked key into the `secrets.json` of its **UserSecretsId**. Keys that aren't tracked stay as they are. A file that already holds every value is not written.

When the file holds another value for a tracked key, the cmdlet asks before replacing it. Without a console, it writes nothing for that id and fails; `lv` then exits with code 2. The **Overwrite** parameter replaces those values without asking. A key whose entry has no value on this PC is skipped and named. An id that no project of the repository uses any more is skipped with a warning.

`Import-VaultFromStore` runs the same write after it loads the vaults. There, a conflict without a console is reported and the load succeeds.

## EXAMPLES

### Example 1 - Write every tracked value

```powershell
Write-VaultUserSecret
```

### Example 2 - Replace values changed by hand

```powershell
Write-VaultUserSecret -Target Api -Overwrite [<CommonParameters>]
```

## PARAMETERS

### -Overwrite

Replaces values that differ from the vault without asking.

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

Specifies the user secrets by the **UserSecretsId** of a project of the repository, or by the name of a project, which is its `.csproj` file name without the extension. Without it, the cmdlet writes every tracked id.

```yaml
Type: System.String
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: 0
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

This cmdlet returns no output. It reports the keys it writes.

## NOTES

`lv user-secrets load [<id|project>] [-Overwrite]` runs this cmdlet.

## RELATED LINKS

- [Get-VaultUserSecretList](Get-VaultUserSecretList.md)
- [Set-VaultUserSecret](Set-VaultUserSecret.md)
- [Get-VaultUserSecret](Get-VaultUserSecret.md)
- [Remove-VaultUserSecret](Remove-VaultUserSecret.md)
- [Write-VaultUserSecret](Write-VaultUserSecret.md)
- [Clear-VaultUserSecret](Clear-VaultUserSecret.md)
- [about_LocalVault](about_LocalVault.md)
