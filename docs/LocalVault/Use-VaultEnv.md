---
document type: cmdlet
external help file: LocalVault.PowerShell.dll-Help.xml
HelpUri: https://github.com/GIT-S-A/LocalVault/blob/main/docs/LocalVault/Use-VaultEnv.md
Locale: en-US
Module Name: LocalVault
ms.date: 10/08/2026
PlatyPS schema version: 2024-05-01
title: Use-VaultEnv
---

# Use-VaultEnv

## SYNOPSIS

Copies vault secrets into environment variables of the current process.

## SYNTAX

### __AllParameterSets

```
Use-VaultEnv [-Map] <hashtable> [-Scope <string>] [-Fallback <string>] [-Path <string>]
 [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

The `Use-VaultEnv` cmdlet sets one environment variable for each pair in the **Map** parameter, to
the value of the named secret. The variables exist only in the current process and in the programs
it starts; the user and machine environment don't change.

A value can name one part of a credential: `github#user`, `github#url`, or `github#totp` for the
current code. The name alone gives the password.

When a secret is missing, the cmdlet writes a warning and sets the other variables. When a name
refers to a file entry, the cmdlet fails.

## EXAMPLES

### Example 1 - Set the variables that a tool reads

```powershell
Use-VaultEnv -Map @{ OS_PASSWORD = 'OsPassword'; AWS_SECRET_ACCESS_KEY = 'S3SecretKey' }
terraform plan
```

```Output
OS_PASSWORD: set from OsPassword
AWS_SECRET_ACCESS_KEY: set from S3SecretKey
Set for this window only.
```

### Example 2 - Set the variables of one deployment

```powershell
$map = @{
    DB_PASSWORD = 'db.password'
    API_KEY     = 'api.key'
}
Use-VaultEnv -Map $map -Scope prod -Path .\deploy\secrets.local.lv
```

Each variable gets `prod/<name>`, or `common/<name>` when the vault has no `prod/<name>`. A name
in the map that already has a scope is used as it is.

## PARAMETERS

### -Fallback

Specifies the scope to use when `<scope>/<name>` doesn't exist. The parameter has an effect only
with the **Scope** parameter.

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

### -Map

Specifies a hash table. Each key is the name of an environment variable and each value is the name
of a secret in the vault, or of a part of a credential, such as `github#user`.

```yaml
Type: System.Collections.Hashtable
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

Specifies a scope. For each unscoped name in the map, the cmdlet uses `<scope>/<name>`, or
`<fallback>/<name>` when the scoped entry doesn't exist. If you omit this parameter, the names are
used as they are.

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

This cmdlet returns no output. It writes the names of the variables it set to the information
stream, never their values.

## NOTES

To remove a variable, use `Remove-Item -Path Env:<name>`.

`lv env <variable>=<name> ...` runs this cmdlet.

## RELATED LINKS

- [Resolve-VaultEntry](Resolve-VaultEntry.md)
- [Get-VaultEntry](Get-VaultEntry.md)
- [about_LocalVault](about_LocalVault.md)
