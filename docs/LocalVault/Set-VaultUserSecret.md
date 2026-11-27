---
document type: cmdlet
external help file: LocalVault.PowerShell.dll-Help.xml
HelpUri: https://github.com/GIT-S-A/LocalVault/blob/main/docs/LocalVault/Set-VaultUserSecret.md
Locale: en-US
Module Name: LocalVault
ms.date: 11/18/2026
PlatyPS schema version: 2024-05-01
title: Set-VaultUserSecret
---

# Set-VaultUserSecret

## SYNOPSIS

Starts tracking a .NET user secret, or changes its value.

## SYNTAX

### Value (Default)

```
Set-VaultUserSecret [-Target] <string> [-Key] <string> [-Value <securestring>] [-Personal]
 [-Path <string>] [<CommonParameters>]
```

### Entry

```
Set-VaultUserSecret [-Target] <string> [-Key] <string> -Entry <string> [-Path <string>] [<CommonParameters>]
```

### FromFile

```
Set-VaultUserSecret [-Target] <string> [-Key] <string> -FromFile [-Personal] [-Path <string>] [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

The `Set-VaultUserSecret` cmdlet keeps the value of one configuration key of a project's user secrets in the vault, records the key in `localvault.json`, and writes it to the `secrets.json` of the **UserSecretsId**. The file is `%APPDATA%\Microsoft\UserSecrets\<id>\secrets.json`. Other keys of the file stay as they are.

Without **Entry** or **FromFile**, the cmdlet asks for the value, or takes **Value**. A key already tracked by this vault gets its new value in the entry it is tracked with. A new key gets a new entry named after the key. When the key isn't a valid entry name or the name is taken, the cmdlet asks for another name in a console. In a console, it also asks whether the value is shared with the team or personal. A personal value becomes a required entry: each person sets their own, and `lv save` copies it to the personal store.

The **Entry** parameter tracks an existing entry, or one part of a credential, without changing its value. The **FromFile** parameter takes the value already in `secrets.json` into the vault and leaves the file unchanged.

When `secrets.json` holds another value for the key, the cmdlet asks before replacing it. Without a console, it writes nothing to the file and fails. Windows Hello entries, file entries, SSH keys and `#totp` parts are refused. A key that another vault of the repository tracks for the same id is refused.

## EXAMPLES

### Example 1 - Track a new connection string

```powershell
Set-VaultUserSecret -Target Api -Key ConnectionStrings:Default [<CommonParameters>]
```

The cmdlet asks for the value, stores it in the vault and writes it to the user secrets of the `Api` project.

### Example 2 - Take a value already in secrets.json

```powershell
Set-VaultUserSecret -Target Api -Key Logging:Seq:ApiKey -FromFile -Personal [<CommonParameters>]
```

### Example 3 - Track the user name of a credential

```powershell
Set-VaultUserSecret -Target Api -Key Smtp:User -Entry smtp#user [<CommonParameters>]
```

## PARAMETERS

### -Entry

Specifies an existing entry, or one part of a credential such as `smtp#user`, whose value fills the key.

```yaml
Type: System.String
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: Entry
  Position: Named
  IsRequired: true
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -FromFile

Takes the value of the key from `secrets.json` into a new vault entry.

```yaml
Type: System.Management.Automation.SwitchParameter
DefaultValue: False
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: FromFile
  Position: Named
  IsRequired: true
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -Key

Specifies the configuration key, such as `ConnectionStrings:Default`. Keys are compared without regard to case.

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

### -Personal

Keeps a new value as a personal value without asking. Without a console and without this parameter, a new value is shared.

```yaml
Type: System.Management.Automation.SwitchParameter
DefaultValue: False
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: Value
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
- Name: FromFile
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

### -Value

Specifies the value as a **SecureString**. Without it, the cmdlet asks for the value, or reads one line of redirected input.

```yaml
Type: System.Security.SecureString
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: Value
  Position: Named
  IsRequired: false
  ValueFromPipeline: true
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

### System.Security.SecureString

You can pipe the value to this cmdlet.

## OUTPUTS

### None

This cmdlet returns no output.

## NOTES

`lv user-secrets set <id|project> <key> [-Entry <name[#part]>] [-Personal]` runs this cmdlet. `lv user-secrets pick` runs it with **FromFile** or with a new key. Commit `localvault.json` so that `lv load` writes the key on every clone.

## RELATED LINKS

- [Get-VaultUserSecretList](Get-VaultUserSecretList.md)
- [Set-VaultUserSecret](Set-VaultUserSecret.md)
- [Get-VaultUserSecret](Get-VaultUserSecret.md)
- [Remove-VaultUserSecret](Remove-VaultUserSecret.md)
- [Write-VaultUserSecret](Write-VaultUserSecret.md)
- [Clear-VaultUserSecret](Clear-VaultUserSecret.md)
- [about_LocalVault](about_LocalVault.md)
