---
document type: cmdlet
external help file: LocalVault.PowerShell.dll-Help.xml
HelpUri: https://github.com/GIT-S-A/LocalVault/blob/main/docs/LocalVault/Set-VaultSshInclude.md
Locale: en-US
Module Name: LocalVault
ms.date: 11/20/2026
PlatyPS schema version: 2024-05-01
title: Set-VaultSshInclude
---

# Set-VaultSshInclude

## SYNOPSIS

Adds or removes the line of `~/.ssh/config` that gives other OpenSSH tools the repository keys.

## SYNTAX

### __AllParameterSets

```
Set-VaultSshInclude [-Enabled] <bool> [-WhatIf] [-Confirm] [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

Each folder `~/.ssh/<repository>/` that holds SSH keys of LocalVault also holds `lv.config`, written
again with every key file. For each host of each key, it has a `Match host <host> [user <user>]`
block with the key file, the old key file while one is kept, and `IdentitiesOnly yes`.

The `Set-VaultSshInclude` cmdlet adds `Include ~/.ssh/*/lv.config` as the first line of
`~/.ssh/config`, so `ssh`, `scp` and other OpenSSH tools use these keys without `-i`. It creates the
file when it is missing, and keeps the access rules of an existing file. With `-Enabled $false`, it
removes the line. The choice is kept as `sshInclude` in `%LOCALAPPDATA%\LocalVault\settings.json`.

The first time a key file is written on a PC without that setting, LocalVault asks the question in
a console; the default answer is yes. Without a console, it asks nothing and adds nothing.

## EXAMPLES

### Example 1 - Let other OpenSSH tools use the keys

```powershell
Set-VaultSshInclude -Enabled $true
```

### Example 2 - Remove the line

```powershell
Set-VaultSshInclude -Enabled $false
```

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

### -Enabled

Specifies whether `~/.ssh/config` has the `Include` line.

```yaml
Type: System.Boolean
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

### None

You can't pipe objects to this cmdlet.

## OUTPUTS

### LocalVault.PowerShell.VaultSshIncludeInfo

This cmdlet returns whether the line is **Enabled**, and the **ConfigPath** of `~/.ssh/config`.

## NOTES

`lv ssh-key include on|off` runs this cmdlet.

## RELATED LINKS

- [Invoke-VaultSsh](Invoke-VaultSsh.md)
- [Set-VaultSshTools](Set-VaultSshTools.md)
