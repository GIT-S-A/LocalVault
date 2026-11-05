---
document type: cmdlet
external help file: LocalVault.PowerShell.dll-Help.xml
HelpUri: https://github.com/GIT-S-A/LocalVault/blob/main/docs/LocalVault/Get-VaultSshKey.md
Locale: en-US
Module Name: LocalVault
ms.date: 11/05/2026
PlatyPS schema version: 2024-05-01
title: Get-VaultSshKey
---

# Get-VaultSshKey

## SYNOPSIS

Gets the SSH keys of a vault, without their private keys.

## SYNTAX

### __AllParameterSets

```
Get-VaultSshKey [[-Name] <string>] [-Path <string>] [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

The `Get-VaultSshKey` cmdlet gets the SSH key entries of a vault: personal or shared, the type, the
fingerprint, the hosts, whether a previous key is kept, the key file and the public key. A personal key
not set on this PC has the source `missing`. The cmdlet never returns a private key.

## EXAMPLES

### Example 1 - List the keys

```powershell
Get-VaultSshKey
```

```Output
Name    Sharing  Source Type    Fingerprint                                        Hosts
----    -------  ------ ----    -----------                                        -----
backup  Shared   file   ed25519 SHA256:ygwjj+x/jcbcUNmwhazPNZzoRvNv7XDnLrscAy0ZQ0M {backup@nas.example.com}
deploy  Personal file   ed25519 SHA256:3tHh0kM9mHq9c8l3yS6Gm8yJb0Zr1y2Q4c7W5n8e1sA {deploy@srv1.example.com}
```

### Example 2 - Get the public key of one key

```powershell
(Get-VaultSshKey -Name deploy).PublicKey
```

## PARAMETERS

### -Name

Specifies the name of an SSH key entry. If you omit this parameter, the cmdlet gets every SSH key of
the vault.

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

### LocalVault.PowerShell.VaultSshKeyInfo

This cmdlet returns the key's **Name**, **Sharing** (`Personal` or `Shared`), **Source** (`file`,
`agent`, or `missing` for a personal key not set on this PC), **Type**, **Fingerprint** in the
`SHA256:` form of OpenSSH, **Hosts**, whether a **Previous** key is kept, the key file **Path** and
the **PublicKey**.

## NOTES

`lv ssh-key` runs this cmdlet.

## RELATED LINKS

- [New-VaultSshKey](New-VaultSshKey.md)
- [Get-VaultSshKeyPath](Get-VaultSshKeyPath.md)
