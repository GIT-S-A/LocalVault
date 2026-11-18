---
document type: cmdlet
external help file: LocalVault.PowerShell.dll-Help.xml
HelpUri: https://github.com/GIT-S-A/LocalVault/blob/main/docs/LocalVault/Get-VaultUserSecretList.md
Locale: en-US
Module Name: LocalVault
ms.date: 11/18/2026
PlatyPS schema version: 2024-05-01
title: Get-VaultUserSecretList
---

# Get-VaultUserSecretList

## SYNOPSIS

Gets the .NET user secrets of the repository and the state of each key.

## SYNTAX

### __AllParameterSets

```
Get-VaultUserSecretList [[-Target] <string>] [-Path <string>] [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

The `Get-VaultUserSecretList` cmdlet lists each **UserSecretsId** used by a project of the repository, and each id that `localvault.json` still tracks. For each id, it returns one row per tracked key, one row per key of its `secrets.json` that no vault tracks, and one row without a key when there is neither.

The projects come from the `.sln` and `.slnx` files of the repository. Without a solution file, every `.csproj` outside `bin`, `obj` and folders whose name starts with a dot counts. A project without a `UserSecretsId` of its own takes the one in the nearest `Directory.Build.props`.

The cmdlet never returns a value. It reads no Windows Hello entry, so it never prompts.

## EXAMPLES

### Example 1 - List the user secrets of the repository

```powershell
Get-VaultUserSecretList | Format-Table Id, Projects, Key, Entry, State [<CommonParameters>]
```

```Output
Id                                   Projects Key                       Entry                     State
--                                   -------- ---                       -----                     -----
0f4c2a9e-55b1-4c7e-9a43-2f8e1d6b7c10 {Api}    ConnectionStrings:Default ConnectionStrings:Default Written
0f4c2a9e-55b1-4c7e-9a43-2f8e1d6b7c10 {Api}    Logging:Seq:ApiKey                                  Untracked
```

## PARAMETERS

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

Specifies the user secrets by the **UserSecretsId** of a project of the repository, or by the name of a project, which is its `.csproj` file name without the extension. Without it, the cmdlet lists every id.

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

### LocalVault.PowerShell.VaultUserSecretInfo

This cmdlet returns the **Id**, the **Projects** that use it, the **Key**, the vault **Entry** that fills it and the **State**: `Written`, `Differs`, `Absent`, `NoValue` or `Untracked`. `NoValue` means the vault has no value for the entry on this PC.

## NOTES

`lv user-secrets [<id|project>]` runs this cmdlet.

## RELATED LINKS

- [Get-VaultUserSecretList](Get-VaultUserSecretList.md)
- [Set-VaultUserSecret](Set-VaultUserSecret.md)
- [Get-VaultUserSecret](Get-VaultUserSecret.md)
- [Remove-VaultUserSecret](Remove-VaultUserSecret.md)
- [Write-VaultUserSecret](Write-VaultUserSecret.md)
- [Clear-VaultUserSecret](Clear-VaultUserSecret.md)
- [about_LocalVault](about_LocalVault.md)
