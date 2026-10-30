---
document type: cmdlet
external help file: LocalVault.PowerShell.dll-Help.xml
HelpUri: https://github.com/GIT-S-A/LocalVault/blob/main/docs/LocalVault/Resolve-VaultEntry.md
Locale: en-US
Module Name: LocalVault
ms.date: 10/08/2026
PlatyPS schema version: 2024-05-01
title: Resolve-VaultEntry
---

# Resolve-VaultEntry

## SYNOPSIS

Gets a secret for a scope, or the shared secret when the scope has none.

## SYNTAX

### __AllParameterSets

```
Resolve-VaultEntry [-Name] <string> [-Scope] <string> [-Fallback <string>] [-AsPlainText]
 [-Path <string>] [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

The `Resolve-VaultEntry` cmdlet gets the secret `<scope>/<key>`. When that entry doesn't exist, it
gets `<fallback>/<key>`, which is `common/<key>` by default. The cmdlet fails when neither entry
exists, or when the entry it finds is a file.

## EXAMPLES

### Example 1 - Get the database password of a deployment

```powershell
$password = Resolve-VaultEntry -Name db.password -Scope prod
```

The cmdlet returns `prod/db.password`, or `common/db.password` when the vault has no
`prod/db.password`.

### Example 2 - Use another fallback scope

```powershell
Resolve-VaultEntry -Name api.key -Scope customer-a -Fallback defaults -AsPlainText |
    Set-Content -Path Env:API_KEY
```

## PARAMETERS

### -AsPlainText

Indicates that this cmdlet returns the secret as a **String** instead of a **SecureString**.

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

### -Fallback

Specifies the scope to use when `<scope>/<key>` doesn't exist.

```yaml
Type: System.String
DefaultValue: common
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

### -Name

Specifies the key, without a scope. A name that already has a scope is refused. The key can be
followed by a part of a credential, such as `#user` or `#totp`.

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

### -Scope

Specifies the scope to look in first, such as the name of a deployment.

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

### CommonParameters

This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable,
-InformationAction, -InformationVariable, -OutBuffer, -OutVariable, -PipelineVariable,
-ProgressAction, -Verbose, -WarningAction, and -WarningVariable. For more information, see
[about_CommonParameters](https://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

### None

You can't pipe objects to this cmdlet.

## OUTPUTS

### System.Security.SecureString

By default, this cmdlet returns the secret as a **SecureString**.

### System.String

When you use the **AsPlainText** parameter, this cmdlet returns the secret as a **String**.

## NOTES

`Use-VaultEnv` applies the same rule when you use its **Scope** parameter.

## RELATED LINKS

- [Get-VaultEntry](Get-VaultEntry.md)
- [Use-VaultEnv](Use-VaultEnv.md)
- [about_LocalVault](about_LocalVault.md)
