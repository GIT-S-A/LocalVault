---
document type: cmdlet
external help file: LocalVault.PowerShell.dll-Help.xml
HelpUri: https://github.com/GIT-S-A/LocalVault/blob/main/docs/LocalVault/Get-VaultHelloStatus.md
Locale: en-US
Module Name: LocalVault
ms.date: 11/13/2026
PlatyPS schema version: 2024-05-01
title: Get-VaultHelloStatus
---

# Get-VaultHelloStatus

## SYNOPSIS

Gets the state of the Windows Hello key on this PC and of the vault's Windows Hello entries.

## SYNTAX

### __AllParameterSets

```
Get-VaultHelloStatus [-Path <string>] [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

The `Get-VaultHelloStatus` cmdlet reports whether this PC can seal and read Windows Hello entries.
It never asks for Windows Hello.

The Windows Hello key is an RSA key of the current Windows user in the Microsoft Passport Key
Storage Provider, named `LocalVault-Hello`. Windows can't export it. The first entry sealed with
Windows Hello creates it, which asks for Windows Hello once.

**State** takes one of these values:

| State | Meaning |
|---|---|
| `Ready` | The key exists and is held by the TPM. |
| `Software` | The key exists but isn't held by a TPM. |
| `None` | The key doesn't exist yet. |
| `Unavailable` | Windows Hello can't be used: it isn't set up, the session is a remote desktop session, or the key provider fails. **Reason** says which. |
| `KeyChanged` | The vault's Windows Hello entries were sealed for another key. A PIN or Windows Hello reset, or another PC, replaces the key. |

**Missing** lists the vault's Windows Hello entries that this PC can't read. `Import-VaultFromStore`
seals them again from the store.

## EXAMPLES

### Example 1 - Check Windows Hello before sealing an entry

```powershell
Get-VaultHelloStatus
```

```Output
State      : Ready
Hardware   : True
Provider   : Microsoft Passport Key Storage Provider
KeyId      : bc1838ae770b812b...
VaultKeyId : bc1838ae770b812b...
Entries    : 1
Missing    : {}
Reason     :
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

### CommonParameters

This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable,
-InformationAction, -InformationVariable, -OutBuffer, -OutVariable, -PipelineVariable,
-ProgressAction, -Verbose, -WarningAction, and -WarningVariable. For more information, see
[about_CommonParameters](https://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

### None

You can't pipe objects to this cmdlet.

## OUTPUTS

### LocalVault.PowerShell.VaultHelloStatus

This cmdlet returns one object with the **State**, **Hardware**, **Provider**, **KeyId** of the
Windows Hello key, the **VaultKeyId** the vault's entries were sealed for, the number of Windows
Hello **Entries**, the names of the **Missing** ones and the **Reason** when Windows Hello is
unavailable.

## NOTES

`lv hello` shows this status. A software key produces a warning.

Windows Hello always offers the PIN as well as face or fingerprint recognition. The key provider
can't require biometrics only.

## RELATED LINKS

- [Set-VaultEntry](Set-VaultEntry.md)
- [Get-VaultEntryList](Get-VaultEntryList.md)
- [about_LocalVault](about_LocalVault.md)
