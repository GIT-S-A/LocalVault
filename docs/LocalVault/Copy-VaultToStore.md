---
document type: cmdlet
external help file: LocalVault.PowerShell.dll-Help.xml
HelpUri: https://github.com/GIT-S-A/LocalVault/blob/main/docs/LocalVault/Copy-VaultToStore.md
Locale: en-US
Module Name: LocalVault
ms.date: 10/10/2026
PlatyPS schema version: 2024-05-01
title: Copy-VaultToStore
---

# Copy-VaultToStore

## SYNOPSIS

Copies the shared entries of a vault once to another store.

## SYNTAX

### __AllParameterSets

```
Copy-VaultToStore [[-Target] <string>] [-KeyFile <string>] [-Store <string>] [-Path <string>]
 [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

The `Copy-VaultToStore` cmdlet writes every shared entry of the vault to another store, in the same
group and with the same layout as `Export-VaultToStore`. Give the store's file with **Target** (and
**KeyFile** when the database needs one), or a defined store with **Store**. The cmdlet asks for the
store's password and doesn't keep it.

It adds entries that the group doesn't have and overwrites the others; KeePass keeps the previous
values in each entry's history. It doesn't remove anything, and it leaves local entries out. It
writes the `Repository origin` and install instructions entries as `Export-VaultToStore` does.

The copy is one-off: the vault records nothing about it, so the next `Export-VaultToStore` and
`Import-VaultFromStore` with the vault's own store work as before.

Entries sealed with Windows Hello are written as `Export-VaultToStore` writes them, after one
Windows Hello prompt.

## EXAMPLES

### Example 1 - Copy the shared entries to another KeePass file

```powershell
Copy-VaultToStore S:\shared\KeePass\team.kdbx
```

```Output
Opening store 'team' (KeePass S:\shared\KeePass\team.kdbx).
KeePass master password for team: ************
Copied 5 shared entries to S:\shared\KeePass\team.kdbx, group LocalVault/webshop. The vault records nothing about this copy.
```

### Example 2 - Copy to a defined store

```powershell
Copy-VaultToStore -Store mine
```

## PARAMETERS

### -KeyFile

Specifies the key file of the database given with **Target**, when it needs one.

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

### -Store

Specifies the name of a store, defined in the project's `localvault.json` or on this PC, to copy
to. Use either this parameter or **Target**.

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

### -Target

Specifies the store to copy to, such as the path of a KeePass `.kdbx` file. A relative path is
resolved from the current location. Use either this parameter or **Store**.

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

### LocalVault.PowerShell.StoreCopySummary

This cmdlet returns the **Target** path, the **Group**, and the number of entries **Written**.

## NOTES

`lv store copy <target | name> [--keyfile <file>]` runs this cmdlet.

## RELATED LINKS

- [Export-VaultToStore](Export-VaultToStore.md)
- [Register-VaultStore](Register-VaultStore.md)
