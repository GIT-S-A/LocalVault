---
document type: cmdlet
external help file: LocalVault.PowerShell.dll-Help.xml
HelpUri: https://github.com/GIT-S-A/LocalVault/blob/main/docs/LocalVault/Copy-VaultEntry.md
Locale: en-US
Module Name: LocalVault
ms.date: 10/09/2026
PlatyPS schema version: 2024-05-01
title: Copy-VaultEntry
---

# Copy-VaultEntry

## SYNOPSIS

Copies a secret to the clipboard for a short time.

## SYNTAX

### __AllParameterSets

```
Copy-VaultEntry [-Name] <string> [-Seconds <int>] [-Path <string>] [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

The `Copy-VaultEntry` cmdlet puts the value of a secret entry on the Windows clipboard without
showing it. The copy is marked so that Windows keeps it out of the clipboard history and doesn't
sync it to other devices. After **Seconds**, or when the PowerShell session ends first, the cmdlet
empties the clipboard, unless something else was copied in the meantime. File entries are refused.

For a credential entry, the name alone copies the password; `<name>#user`, `<name>#url` or
`<name>#totp` copies that part, the TOTP as its current code.

## EXAMPLES

### Example 1 - Paste a password into a login form

```powershell
Copy-VaultEntry GrafanaAdminPassword
```

```Output
GrafanaAdminPassword is on the clipboard for 20 s, kept out of the clipboard history.
```

## PARAMETERS

### -Name

Specifies the name of the secret or credential entry, optionally followed by a part:
`#user`, `#password`, `#totp`, `#url`, `#url2` and so on.

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

### -Seconds

Specifies how long the value stays on the clipboard, from 1 to 3600 seconds. The default is 20.

```yaml
Type: System.Int32
DefaultValue: 20
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

You can pipe objects with a **Name** property to this cmdlet.

## OUTPUTS

### None

This cmdlet writes a message only.

## NOTES

When the process ends abruptly, the clipboard isn't emptied.

`lv clip <name>` runs this cmdlet.

## RELATED LINKS

- [Get-VaultEntry](Get-VaultEntry.md)
- [Use-VaultEnv](Use-VaultEnv.md)