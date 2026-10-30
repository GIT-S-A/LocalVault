---
document type: cmdlet
external help file: LocalVault.PowerShell.dll-Help.xml
HelpUri: https://github.com/GIT-S-A/LocalVault/blob/main/docs/LocalVault/Get-VaultEntry.md
Locale: en-US
Module Name: LocalVault
ms.date: 10/08/2026
PlatyPS schema version: 2024-05-01
title: Get-VaultEntry
---

# Get-VaultEntry

## SYNOPSIS

Gets the value of one vault entry.

## SYNTAX

### Value (Default)

```
Get-VaultEntry [-Name] <string> [-AsPlainText] [-Path <string>] [<CommonParameters>]
```

### Credential

```
Get-VaultEntry [-Name] <string> -AsCredential [-Path <string>] [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

The `Get-VaultEntry` cmdlet gets the value of the named entry. For a secret entry, it returns a
**SecureString** by default, or a **String** when you use the **AsPlainText** parameter. For a file
entry, it returns the content of the file as a **Byte[]**.

For a credential entry, the name alone gets the password. Add a part to get another one:
`<name>#user` and `<name>#url` (or `#url2`, `#url3`...) return a **String**; `<name>#totp` returns the
current time-based code, computed from the seed kept in the vault, as a **SecureString** or a
**String**. The **AsCredential** parameter returns the user name and the password as a
**PSCredential**.

The cmdlet fails when the entry doesn't exist. To get `<scope>/<key>` with a fallback to
`common/<key>`, use `Resolve-VaultEntry`.

## EXAMPLES

### Example 1 - Get a secret as a SecureString

```powershell
$password = Get-VaultEntry -Name GrafanaAdminPassword
$credential = [pscredential]::new('admin', $password)
```

### Example 2 - Pass a secret to a native program on its standard input

```powershell
Get-VaultEntry -Name registry.token -AsPlainText |
    docker login registry.example.com --username ci --password-stdin
```

### Example 3 - Get the content of a file entry

```powershell
$content = Get-VaultEntry -Name prod/clouds.yaml -Path .\infra\secrets.local.lv
Set-Content -Path $env:TEMP\clouds.yaml -Value $content -AsByteStream
```

To write a file entry to its stored path, use `Restore-VaultFile`.

### Example 4 - Use the parts of a credential

```powershell
$credential = Get-VaultEntry -Name github -AsCredential
$code = Get-VaultEntry -Name github#totp -AsPlainText
Get-VaultEntry -Name github#url
```

## PARAMETERS

### -AsCredential

Indicates that this cmdlet returns the user name and the password of a credential entry as a
**PSCredential**. The entry must have a user name, and the name must not name a part.

```yaml
Type: System.Management.Automation.SwitchParameter
DefaultValue: False
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: Credential
  Position: Named
  IsRequired: true
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -AsPlainText

Indicates that this cmdlet returns a secret, a password or a TOTP code as a **String** instead of a
**SecureString**. The parameter has no effect on a file entry, a user name or a URL.

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
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -Name

Specifies the name of the entry, as `key` or `scope/key`, optionally followed by a part of a
credential: `#user`, `#password`, `#totp`, `#url`, `#url2` and so on. The name isn't case-sensitive.

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
  ValueFromPipelineByPropertyName: true
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

### System.String

You can pipe an object that has a **Name** property to this cmdlet.

## OUTPUTS

### System.Security.SecureString

By default, this cmdlet returns a secret as a **SecureString**.

### System.String

When you use the **AsPlainText** parameter, this cmdlet returns a secret as a **String**. A user
name and a URL are always returned as a **String**.

### System.Management.Automation.PSCredential

When you use the **AsCredential** parameter, this cmdlet returns the user name and the password.

### System.Byte[]

For a file entry, this cmdlet returns the content of the file.

## NOTES

`lv get <name>` runs this cmdlet with **AsPlainText**. It fails when its output isn't a pipeline
or a redirection, unless you add `--show`, or the name ends with `#user` or `#url`.

## RELATED LINKS

- [Resolve-VaultEntry](Resolve-VaultEntry.md)
- [Set-VaultEntry](Set-VaultEntry.md)
- [Use-VaultEnv](Use-VaultEnv.md)
- [Restore-VaultFile](Restore-VaultFile.md)
- [about_LocalVault](about_LocalVault.md)
