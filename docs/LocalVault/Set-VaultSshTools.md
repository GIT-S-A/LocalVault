---
document type: cmdlet
external help file: LocalVault.PowerShell.dll-Help.xml
HelpUri: https://github.com/GIT-S-A/LocalVault/blob/main/docs/LocalVault/Set-VaultSshTools.md
Locale: en-US
Module Name: LocalVault
ms.date: 11/05/2026
PlatyPS schema version: 2024-05-01
title: Set-VaultSshTools
---

# Set-VaultSshTools

## SYNOPSIS

Sets or shows the OpenSSH folder that LocalVault uses on this PC.

## SYNTAX

### Show (Default)

```
Set-VaultSshTools [-WhatIf] [-Confirm] [<CommonParameters>]
```

### Directory

```
Set-VaultSshTools [-Directory] <string> [-WhatIf] [-Confirm] [<CommonParameters>]
```

### Default

```
Set-VaultSshTools -Default [-WhatIf] [-Confirm] [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

The `Set-VaultSshTools` cmdlet sets the folder that holds `ssh.exe`, `ssh-keygen.exe` and
`ssh-add.exe` for LocalVault on this PC, for the current Windows account. The folder is kept as
`sshDir` in `%LOCALAPPDATA%\LocalVault\settings.json`; a project never sets it. Without a folder
set, LocalVault uses Windows OpenSSH in `%SystemRoot%\System32\OpenSSH`. LocalVault never looks for
these tools on `PATH`. The cmdlet refuses a folder without the three tools, and keeps the previous
setting.

Without parameters, the cmdlet shows the folder in use.

## EXAMPLES

### Example 1 - Show the folder in use

```powershell
Set-VaultSshTools
```

### Example 2 - Use the OpenSSH of Git for Windows

```powershell
Set-VaultSshTools -Directory 'C:\Program Files\Git\usr\bin'
```

### Example 3 - Go back to Windows OpenSSH

```powershell
Set-VaultSshTools -Default
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

### -Default

Indicates that the cmdlet removes the folder setting, so LocalVault uses Windows OpenSSH.

```yaml
Type: System.Management.Automation.SwitchParameter
DefaultValue: False
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: Default
  Position: Named
  IsRequired: true
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -Directory

Specifies the folder that holds `ssh.exe`, `ssh-keygen.exe` and `ssh-add.exe`. A relative path is
resolved from the current location and kept as a full path.

```yaml
Type: System.String
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: Directory
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

### LocalVault.PowerShell.VaultSshToolsInfo

This cmdlet returns the **Directory** in use, whether it's Windows OpenSSH (**IsDefault**), and the
**Settings** file.

## NOTES

`lv ssh-key tools [<dir> | --default]` runs this cmdlet.

## RELATED LINKS

- [New-VaultSshKey](New-VaultSshKey.md)
