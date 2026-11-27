---
document type: cmdlet
external help file: LocalVault.PowerShell.dll-Help.xml
HelpUri: https://github.com/GIT-S-A/LocalVault/blob/main/docs/LocalVault/New-VaultSshKey.md
Locale: en-US
Module Name: LocalVault
ms.date: 11/05/2026
PlatyPS schema version: 2024-05-01
title: New-VaultSshKey
---

# New-VaultSshKey

## SYNOPSIS

Creates an SSH key pair for the hosts of a project and stores it in the vault.

## SYNTAX

### __AllParameterSets

```
New-VaultSshKey [-Name] <string> [-HostName <string[]>] [-Shared] [-Type <string>] [-Comment <string>]
 [-Overwrite] [-Replace] [-Path <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

The `New-VaultSshKey` cmdlet runs `ssh-keygen` from Windows OpenSSH, or from the folder set with
[Set-VaultSshTools](Set-VaultSshTools.md), to create a key pair without a passphrase. The private key
becomes a vault entry and is written to `~/.ssh/<repository>/<name>`, readable by the current user,
SYSTEM and Administrators only, next to `<name>.pub`. `<repository>` is the last segment of the
`origin` URL without `.git`, or the folder name of the git root when there is no `origin`. A `/`
in the entry name becomes `.` in the file name, and `:` becomes `_`.

By default the key is personal: the entry is declared as a required value of shape `ssh-key`, the
declaration and its hosts go to the project's store, and the key goes only to the personal store of
each person. With `-Shared`, the key is one shared entry that every teammate loads from the
project's store.

The file `.localvault` in the key folder records the repository, the hash of each key and the vaults
that use it. The cmdlet refuses a folder that belongs to another repository, a file that LocalVault
didn't write, and a key that another clone of the repository wrote, unless `-Overwrite` is given.

An entry that already holds a key gets a new key pair only with confirmation, or with `-Replace`
outside a console. The replacement requires the store that carries the entry: the personal store for
a personal key, the project's store for a shared one. The old key becomes the entry's previous key,
written to `<name>.old`, and replaces any previous key kept before. The old public key stays
registered on the hosts.

After `Reset-VaultEntry`, the key still in the file becomes the previous key in the same way, without
a confirmation.

The cmdlet prints the public key and copies it to the clipboard for 2 minutes. The key is usable once
the public key is added to the hosts it is for.

## EXAMPLES

### Example 1 - Create a personal key for a host

```powershell
New-VaultSshKey -Name deploy -HostName deploy@srv1.example.com
```

### Example 2 - Create a key shared by the team

```powershell
New-VaultSshKey -Name backup -HostName backup@nas.example.com -Shared
```

### Example 3 - Create an RSA key for a host that refuses ed25519

```powershell
New-VaultSshKey -Name legacy -HostName root@old.example.com -Type rsa
```

### Example 4 - Replace a key from a script

```powershell
New-VaultSshKey -Name backup -Replace
```

## PARAMETERS

### -Comment

Specifies the comment of the public key. The default is `<user>@<computer> <repository>/<name>`.

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

### -HostName

Specifies the hosts the key is for, as `[user@]host[:port]`.

```yaml
Type: System.String[]
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

### -Name

Specifies the name of the entry.

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

### -Overwrite

Indicates that the cmdlet replaces a key file that LocalVault didn't write or that another clone
of the repository wrote.

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

### -Replace

Indicates that the cmdlet replaces the key of an entry that already holds one, without asking.

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

### -Shared

Indicates that the key is shared: its private key is readable by everyone with access to the
project's store.

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

### -Type

Specifies the key type: `ed25519` (the default), `rsa` (4096 bits) or `ecdsa`.

```yaml
Type: System.String
DefaultValue: ed25519
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
AcceptedValues:
- ed25519
- rsa
- ecdsa
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

### LocalVault.PowerShell.VaultSshKeyInfo

This cmdlet returns the key's **Name**, **Sharing** (`Personal` or `Shared`), **Source** (`file`,
`agent`, or `missing` for a personal key not set on this PC), **Type**, **Fingerprint** in the
`SHA256:` form of OpenSSH, **Hosts**, whether a **Previous** key is kept, the key file **Path** and
the **PublicKey**.

## NOTES

`lv ssh-key new <name> [--host <user@host>]... [--shared] [--type <type>] [--comment <text>] [--overwrite] [--replace]`
runs this cmdlet.

## RELATED LINKS

- [Get-VaultSshKey](Get-VaultSshKey.md)
- [Set-VaultSshKeyHost](Set-VaultSshKeyHost.md)
- [Get-VaultSshKeyPath](Get-VaultSshKeyPath.md)
- [Set-VaultSshTools](Set-VaultSshTools.md)
