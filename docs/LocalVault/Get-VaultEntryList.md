---
document type: cmdlet
external help file: LocalVault.PowerShell.dll-Help.xml
HelpUri: https://github.com/GIT-S-A/LocalVault/blob/main/docs/LocalVault/Get-VaultEntryList.md
Locale: en-US
Module Name: LocalVault
ms.date: 10/08/2026
PlatyPS schema version: 2024-05-01
title: Get-VaultEntryList
---

# Get-VaultEntryList

## SYNOPSIS

Gets the names, kinds and ages of the entries in a vault.

## SYNTAX

### __AllParameterSets

```
Get-VaultEntryList [[-Scope] <string>] [-Path <string>] [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

The `Get-VaultEntryList` cmdlet gets one **VaultEntryInfo** object for each entry in the vault,
sorted by name. Each object has the full name, the scope, the key, the kind (`secret` or `file`),
the store state (**Sync**), the creation time and the age of the entry. The objects don't contain
values. **Sync** is `Local` for an entry kept on this machine, `New` for a shared entry never saved to
the store, `Changed` when it changed since the last save or load, and `Saved` otherwise. A required personal value is `Missing` until it's set on this machine, then `Personal`; the object's **Note** says where to get it.

The cmdlet writes a warning when the vault has a store and shared entries aren't saved to it. It
also warns about the entries that exist only on this machine when the vault wasn't exported in the
last 30 days: local entries and personal values, or every entry when the vault has no store.

When the vault file doesn't exist and the same folder contains `secrets.local.clixml`, the cmdlet
reads that file.

## EXAMPLES

### Example 1 - List the entries of the vault of the current repository

```powershell
Get-VaultEntryList
```

```Output
Name    : common/db.password
Scope   : common
Key     : db.password
Kind    : secret
Sync    : Saved
Created : 06/10/2026 09:12:40 +00:00
Age     : 2.03:41:10.5237714

Name    : GrafanaAdminPassword
Scope   :
Key     : GrafanaAdminPassword
Kind    : secret
Sync    : Saved
Created : 01/09/2026 17:02:11 +00:00
Age     : 37.16:51:39.1408315
```

### Example 2 - List the entries of one scope in a given vault as a table

```powershell
Get-VaultEntryList -Scope prod -Path .\infra\secrets.local.lv | Format-Table Name, Kind, Age
```

```Output
Name             Kind   Age
----             ----   ---
prod/clouds.yaml file   2.03:38:48.0911204
prod/db.password secret 2.03:40:02.7765131
```

The **Scope** parameter keeps only the entries whose name starts with `prod/`.

### Example 3 - Find entries older than a year

```powershell
Get-VaultEntryList | Where-Object Age -gt (New-TimeSpan -Days 365) | Select-Object Name, Created
```

## PARAMETERS

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

Specifies a scope. The cmdlet gets only the entries named `<scope>/<key>`. The comparison isn't
case-sensitive. If you omit this parameter, the cmdlet gets every entry.

```yaml
Type: System.String
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: 0
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

### LocalVault.PowerShell.VaultEntryInfo

This cmdlet returns one object for each entry. The object has the **Name**, **Scope**, **Key**,
**Kind**, **Created** and **Age** properties. For a credential, the **User** property holds its user
name, which isn't a secret.

## NOTES

`lv list [<scope>]` runs this cmdlet and formats the result as a table.

## RELATED LINKS

- [Get-VaultEntry](Get-VaultEntry.md)
- [Set-VaultEntry](Set-VaultEntry.md)
- [about_LocalVault](about_LocalVault.md)
