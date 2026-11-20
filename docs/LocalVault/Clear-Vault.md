---
document type: cmdlet
external help file: LocalVault.PowerShell.dll-Help.xml
HelpUri: https://github.com/GIT-S-A/LocalVault/blob/main/docs/LocalVault/Clear-Vault.md
Locale: en-US
Module Name: LocalVault
ms.date: 10/08/2026
PlatyPS schema version: 2024-05-01
title: Clear-Vault
---

# Clear-Vault

## SYNOPSIS

Deletes a vault file and its previous version.

## SYNTAX

### __AllParameterSets

```
Clear-Vault [-Path <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

The `Clear-Vault` cmdlet deletes the vault file and its previous version `<vault>.bak`. For a vault
named `secrets.local.lv`, it also deletes a `secrets.local.clixml` file in the same folder, and the
confirmation message names it.

The cmdlet asks for confirmation unless you use `-Confirm:$false`. It also deletes the vault's old
automatic backups: the files named by the backup settings kept in the vault, and every backup in
the vault's default folder `%OneDrive%\LocalVault\<project>-<id>-<machine>`. Files of other vaults
stay. Folders left empty are removed. When the vault file is already gone, the cmdlet still deletes
the backups in the default folder.

Files written from the repository's templates with this vault's values (see `Write-VaultTemplate`)
hold those values in plain text. In a console, the cmdlet asks whether to delete them too; without
a console, it deletes them. The templates stay.

The .NET user secrets tracked for this vault hold its values in plain text too. In a console, the
cmdlet asks whether to remove them, as `Clear-VaultUserSecret` does; without a console, it removes
them. Values changed in `secrets.json` stay.

For each SSH key of the vault, the cmdlet removes the vault from the clones that the key folder's
marker lists:

| Case | Result |
| --- | --- |
| Another clone on this PC still uses the key | The key files stay; the cmdlet names the clones. |
| No clone uses it, and the file is the one LocalVault wrote | The key file, its `.pub` and its `.old` files are deleted. |
| No clone uses it, and the file was changed | The files stay, with a warning. |

A key folder left without keys loses its marker and `lv.config`, and is deleted when empty. Files
that the marker doesn't list stay, and `~/.ssh` itself stays. Last, the cmdlet warns about orphaned
SSH keys, as `Get-VaultEntryList` does.

## EXAMPLES

### Example 1 - Delete the vault of the current repository

```powershell
Clear-Vault
```

```Output
Confirm
Are you sure you want to perform this action?
Performing the operation "Delete the vault, its previous generation and its 12 old automatic backup(s)" on target
"C:\src\obs\secrets.local.lv".
[Y] Yes  [A] Yes to All  [N] No  [L] No to All  [S] Suspend  [?] Help (default is "Y"): y
Deleted C:\src\obs\secrets.local.lv
Deleted 12 old automatic backup(s) in C:\Users\me\OneDrive\LocalVault\obs-1a2b3c4d-PC042
```

### Example 2 - Delete a test vault without a question

```powershell
Clear-Vault -Path $env:TEMP\test\secrets.local.lv -Confirm:$false
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

### None

You can't pipe objects to this cmdlet.

## OUTPUTS

### None

This cmdlet returns no output.

## NOTES

The cmdlet doesn't delete the vault's entries in its store.

`lv clear` runs this cmdlet.

## RELATED LINKS

- [Export-Vault](Export-Vault.md)
- [Remove-VaultEntry](Remove-VaultEntry.md)
