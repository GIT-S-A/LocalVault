---
document type: cmdlet
external help file: LocalVault.PowerShell.dll-Help.xml
HelpUri: https://github.com/GIT-S-A/LocalVault/blob/main/docs/LocalVault/Reset-VaultEntry.md
Locale: en-US
Module Name: LocalVault
ms.date: 11/27/2026
PlatyPS schema version: 2024-05-01
title: Reset-VaultEntry
---

# Reset-VaultEntry

## SYNOPSIS

Removes the personal value of required entries so that a new one is asked.

## SYNTAX

### __AllParameterSets

```
Reset-VaultEntry [-Name] <string[]> [-NoPrompt] [-Path <string>] [-WhatIf] [-Confirm]
 [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

The `Reset-VaultEntry` cmdlet removes the personal value of each named required entry from the vault.
The declaration stays, so the entry is missing again. It also removes the value's previous key and
its KeePass reference. No store is opened.

The cmdlet refuses a shared entry, an entry with no personal value on this PC and an unknown name.
Nothing is changed when one of the names is refused.

| Step | Result |
| --- | --- |
| Next `Export-VaultToStore` (`lv save`) | The value is deleted from the personal store. When the personal store is the project's store, only the value is removed and the declaration stays. |
| Next `Import-VaultFromStore` (`lv load`) | The value in the personal store isn't loaded back; a console asks for a new one. |
| New value set before the next save | The save replaces the value in the personal store. |

In a console, the cmdlet asks for the new values at once, as `lv load` does for missing values.
`-NoPrompt` skips the question.

For an SSH key, the key files under `~/.ssh` stay. The key isn't taken back from its file when the
vault reads the entry again. The next key, from the menu or from `New-VaultSshKey`, replaces it
without a confirmation: the old key becomes the fallback key, kept as `<file>.old` until every host
of the entry accepts the new key.

## EXAMPLES

### Example 1 - Replace a personal password

```powershell
Reset-VaultEntry -Name db.password
```

The cmdlet removes the value and asks for a new one. The next `lv save` writes it to the personal
store.

### Example 2 - Drop a value without asking for a new one

```powershell
Reset-VaultEntry -Name deploy-key -NoPrompt
```

The next `lv load` in a console offers the SSH key menu for `deploy-key`.

## PARAMETERS

### -Confirm

Prompts you for confirmation before running the cmdlet.

```yaml
Type: System.Management.Automation.SwitchParameter
DefaultValue: False
SupportsWildcards: false
Aliases:
- cf
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

Specifies the names of the required entries whose personal value is removed.

```yaml
Type: System.String[]
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

### -NoPrompt

Indicates that the cmdlet doesn't ask for the new values. The next `lv load` in a console asks for
them.

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

### -WhatIf

Shows what would happen if the cmdlet runs. The cmdlet isn't run.

```yaml
Type: System.Management.Automation.SwitchParameter
DefaultValue: False
SupportsWildcards: false
Aliases:
- wi
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

You can pipe objects that have a **Name** property to this cmdlet.

## OUTPUTS

### None

This cmdlet returns no output.

## NOTES

`lv reset <name> ... [-NoPrompt]` runs this cmdlet.

## RELATED LINKS

- [Set-VaultEntry](Set-VaultEntry.md)
- [New-VaultSshKey](New-VaultSshKey.md)
- [Import-VaultFromStore](Import-VaultFromStore.md)
