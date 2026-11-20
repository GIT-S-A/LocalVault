---
document type: cmdlet
external help file: LocalVault.PowerShell.dll-Help.xml
HelpUri: https://github.com/GIT-S-A/LocalVault/blob/main/docs/LocalVault/Remove-VaultSshOrphan.md
Locale: en-US
Module Name: LocalVault
ms.date: 11/20/2026
PlatyPS schema version: 2024-05-01
title: Remove-VaultSshOrphan
---

# Remove-VaultSshOrphan

## SYNOPSIS

Deletes the SSH keys of clones that no longer exist on this PC.

## SYNTAX

### __AllParameterSets

```
Remove-VaultSshOrphan [-WhatIf] [-Confirm] [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

Each folder `~/.ssh/<repository>/` that holds SSH keys of LocalVault has a marker that lists, for
each key, the vaults that use it. A key is orphaned when none of these vault files exists any more,
for example after a clone was deleted without `Clear-Vault`.

The `Remove-VaultSshOrphan` cmdlet lists the orphaned keys of every such folder, with the repository,
the key and the time it was last written, and asks one confirmation for all of them. It then removes
the missing vaults from the markers and deletes each orphaned key with the rules of `Clear-Vault`:
a key file changed since LocalVault wrote it stays, with a warning. A folder left without keys is
deleted. Nothing is deleted without the confirmation.

`Get-VaultEntryList`, `lv start` and `Clear-Vault` report orphaned keys; they never delete them.

## EXAMPLES

### Example 1 - Delete orphaned keys after a confirmation

```powershell
Remove-VaultSshOrphan
```

### Example 2 - List orphaned keys without deleting them

```powershell
Remove-VaultSshOrphan -WhatIf
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

### LocalVault.PowerShell.VaultSshOrphanInfo

This cmdlet returns one object per orphaned key: the **Repository** folder, the **Key** file name,
the **LastWrite** time and the **Action** taken, `Deleted` or `Kept`.

## NOTES

`lv ssh-key clean` runs this cmdlet.

## RELATED LINKS

- [Clear-Vault](Clear-Vault.md)
- [New-VaultSshKey](New-VaultSshKey.md)
