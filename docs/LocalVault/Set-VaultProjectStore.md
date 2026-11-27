---
document type: cmdlet
external help file: LocalVault.PowerShell.dll-Help.xml
HelpUri: https://github.com/GIT-S-A/LocalVault/blob/main/docs/LocalVault/Set-VaultProjectStore.md
Locale: en-US
Module Name: LocalVault
ms.date: 10/13/2026
PlatyPS schema version: 2024-05-01
title: Set-VaultProjectStore
---

# Set-VaultProjectStore

## SYNOPSIS

Names the project's primary store.

## SYNTAX

### __AllParameterSets

```
Set-VaultProjectStore [-Name] <string> [-Path <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

The `Set-VaultProjectStore` cmdlet writes `localvault.json` at the root of the git repository. Without
**Path**, and without the `LOCALVAULT_PATH` environment variable, the store becomes the project's
primary store: `Import-VaultFromStore` reads every vault of the repository from it, and
`Export-VaultToStore` writes them there, and also to the mirrors (see `Register-VaultStore -Mirror`).
Stores named for single vaults are dropped. With **Path**, only that vault uses the store as its
primary store, and the other vaults keep theirs; it is meant for the rare vault that belongs
elsewhere, such as a personal one.

The store is looked up among the project's stores, then among this PC's stores. The cmdlet writes
the store's internal id into `localvault.json` beside the name. The id is the same in every copy of a
`.kdbx` file, whatever its local name or path. On another PC, LocalVault finds the store by that id
even when it has another name there, and refuses a database with another id.

The id comes from this PC's record of the store, made by `Register-VaultStore`, or from the header of
the file. When neither holds it, the cmdlet opens the store and asks for its master password, or for
the password of the store that unlocks it, as `Register-VaultStore -UnlockWith` describes.

Commit the file: everyone then saves into and loads from the same store. The file holds no secret.

## EXAMPLES

### Example 1 - Use the team store for every vault of the repository

```powershell
Set-VaultProjectStore team
git add localvault.json
```

### Example 2 - Keep one vault in a personal store

```powershell
Set-VaultProjectStore mine -Path .\migration\secrets.local.lv
```

## PARAMETERS

### -Name

Specifies the name of the store, defined in the project's `localvault.json` or on this PC.

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
With this parameter, only this vault uses the store.

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

### None

This cmdlet writes a message only.

## NOTES

`lv store use <name> [-Path <vault>]` runs this cmdlet.

## RELATED LINKS

- [Register-VaultStore](Register-VaultStore.md)
- [Get-VaultStoreStatus](Get-VaultStoreStatus.md)
- [Export-VaultToStore](Export-VaultToStore.md)
