---
document type: cmdlet
external help file: LocalVault.PowerShell.dll-Help.xml
HelpUri: https://github.com/GIT-S-A/LocalVault/blob/main/docs/LocalVault/Set-VaultEntry.md
Locale: en-US
Module Name: LocalVault
ms.date: 10/08/2026
PlatyPS schema version: 2024-05-01
title: Set-VaultEntry
---

# Set-VaultEntry

## SYNOPSIS

Adds or replaces a secret, a credential or a file in a vault.

## SYNTAX

### Secret (Default)

```
Set-VaultEntry [-Name] <string> [[-Value] <securestring>] [-Local] [-Path <string>] [-WhatIf]
 [-Confirm] [<CommonParameters>]
```

### File

```
Set-VaultEntry [-Name] <string> -FilePath <string> [-StoredPath <string>] [-Local] [-Path <string>]
 [-WhatIf] [-Confirm] [<CommonParameters>]
```

### Credential

```
Set-VaultEntry [-Name] <string> -Credential [-UserName <string>] [-Password <securestring>]
 [-Totp <securestring>] [-Url <string[]>] [-Local] [-Path <string>] [-WhatIf] [-Confirm]
 [<CommonParameters>]
```

### Sharing

```
Set-VaultEntry [-Name] <string> -Sharing <VaultEntrySharing> [-Path <string>] [-WhatIf] [-Confirm]
 [<CommonParameters>]
```

### Required

```
Set-VaultEntry [-Name] <string> -Required [-Note <string>] [-Path <string>] [-WhatIf] [-Confirm]
 [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

The `Set-VaultEntry` cmdlet writes one entry to the vault. If an entry with the same name exists,
the new value replaces it. Other entries don't change.

A secret entry holds a value. Pass it as a **SecureString** with the **Value** parameter or the
pipeline. Without a value, the cmdlet asks for it on the console, or reads one line from redirected
standard input when there is no console. An empty value is refused.

A credential entry keeps a user name, a password, an optional TOTP and URLs together, as KeePass
does. With the **Credential** parameter alone, the cmdlet asks for each part in turn: the user name,
the password (hidden), the TOTP as an `otpauth://` address or a base32 seed (hidden), then one URL
per line until an empty line. On redirected input, it reads the same lines in the same order. An
empty answer keeps the current part of an existing entry; on a secret, the current value becomes the
password. With any of **UserName**, **Password**, **Totp** or **Url**, the cmdlet asks nothing and
keeps the parts you don't give.

A name followed by a part, such as `github#user`, `github#totp` or `github#url2`, sets that part
only: the cmdlet asks for it (hidden for `#password` and `#totp`), or takes **Value**. An empty value
removes the part. On a secret, setting a part other than the password makes it a credential that
keeps the value as its password; on a name that doesn't exist, it creates a credential. Setting a
secret's value on a credential, with the name alone, changes its password and keeps the other parts.

A file entry holds the content of a file and the path to restore it to, relative to the folder of
the vault. Use the **FilePath** parameter, and the **StoredPath** parameter when the file is outside
that folder.

An entry is shared by default: `Export-VaultToStore` sends it to the vault's store. With the **Local** parameter,
a new value stays on this machine only. The **Sharing** parameter alone changes this choice for an
existing entry and keeps its value. Replacing a value keeps the entry's choice.

With the **Required** parameter, the cmdlet declares a personal value: each person sets their own,
and the **Note** says where to get it. `Export-VaultToStore` shares the declaration and the note,
never the value, and `Export-Vault` leaves the value out. Until a value is set on this machine, the
entry is `Missing` in `Get-VaultEntryList`, and reading it fails with the note. Setting a value with
this cmdlet then keeps it on this machine. Declaring an entry that has a value keeps the value.

When the vault doesn't exist, the cmdlet creates it. It fails when git doesn't ignore the vault
file. At the default path, it also fails when the repository has a vault file in another folder,
and asks before it creates the vault; with **Path**, a repository can hold several vaults.

When the vault folder contains `secrets.local.clixml`, the cmdlet moves its entries into the vault
and renames the file to `secrets.local.clixml.migrated-<time>`.

## EXAMPLES

### Example 1 - Store a secret that you type

```powershell
Set-VaultEntry -Name GrafanaAdminPassword
```

```Output
Value for GrafanaAdminPassword: ************
Stored GrafanaAdminPassword
```

### Example 2 - Store a scoped secret from a script

```powershell
$token = Read-Host -Prompt 'Push token' -AsSecureString
Set-VaultEntry -Name prod/loki.token -Value $token -Path .\infra\secrets.local.lv
```

### Example 3 - Store a file

```powershell
Set-VaultEntry -Name clouds.yaml -FilePath .\infra\clouds.yaml -Path .\infra\secrets.local.lv
```

The file is stored with the path `clouds.yaml`, relative to the `infra` folder of the vault.
`Restore-VaultFile -Name clouds.yaml` writes it back there.

### Example 4 - Store a credential and change its URL

```powershell
Set-VaultEntry -Name github -Credential
Set-VaultEntry -Name github#url
```

```Output
User name for github: jdoe
Password for github: ********
TOTP for github: otpauth:// address or base32 seed (empty for none): ****************
URL 1 for github (empty to finish): https://github.com
URL 2 for github (empty to finish):
Stored github
URL (empty removes it) for github: https://github.example.com
Stored github#url
```

### Example 5 - Copy a secret from another store

```powershell
Get-Secret -Name CiToken -Vault SecretStore | Set-VaultEntry -Name ci.token
```

`Get-Secret` from the **Microsoft.PowerShell.SecretManagement** module returns a **SecureString**,
which this cmdlet takes from the pipeline.

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

### -Credential

Indicates that the entry is a credential: a user name, a password, an optional TOTP and URLs.

```yaml
Type: System.Management.Automation.SwitchParameter
DefaultValue: False
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: Credential
  Position: Named
  IsRequired: true
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -FilePath

Specifies the file to store. A relative path is resolved from the current location.

```yaml
Type: System.String
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: File
  Position: Named
  IsRequired: true
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -Local

Indicates that the new entry stays on this machine: `Export-VaultToStore` leaves it out.

```yaml
Type: System.Management.Automation.SwitchParameter
DefaultValue: False
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: Secret
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
- Name: File
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
- Name: Credential
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

Specifies the name of the entry, as `key` or `scope/key`. A name uses letters, digits and the
characters `.`, `_`, `:`, `-`, `@` and `+`, with at most one `/`. The name isn't case-sensitive, but
its case is kept. Followed by `#user`, `#password`, `#totp`, `#url`, `#url2` and so on, it names one
part of a credential.

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

### -Note

Specifies where each person gets the value of a required entry. KeePass shows it in the entry's notes.

```yaml
Type: System.String
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: Required
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```
### -Password

Specifies the password of a credential. If you omit it with other parts, the current password is kept.

```yaml
Type: System.Security.SecureString
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: Credential
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

### -Required

Indicates that the entry is a personal value that each person sets; only its declaration is shared.

```yaml
Type: System.Management.Automation.SwitchParameter
DefaultValue: False
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: Required
  Position: Named
  IsRequired: true
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```
### -Sharing

Specifies whether the existing entry is `Shared` (sent to the store by `Export-VaultToStore`) or `Local`. The value doesn't change.

```yaml
Type: LocalVault.PowerShell.VaultEntrySharing
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: Sharing
  Position: Named
  IsRequired: true
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: 
- Shared
- Local
HelpMessage: ''
```
### -StoredPath

Specifies the path that `Restore-VaultFile` writes the file to, relative to the folder of the
vault. Use forward or back slashes; a rooted path or `..` is refused. If you omit this parameter,
the cmdlet uses the path of **FilePath** relative to the folder of the vault, and fails when the
file is outside that folder.

```yaml
Type: System.String
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: File
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -Totp

Specifies the TOTP of a credential, as an `otpauth://totp/` address or a base32 seed. Only time-based codes are supported.

```yaml
Type: System.Security.SecureString
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: Credential
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -Url

Specifies the URLs of a credential, in order. They replace the current ones.

```yaml
Type: System.String[]
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: Credential
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -UserName

Specifies the user name of a credential. An empty string removes it. A user name is not a secret: `Get-VaultEntryList` shows it.

```yaml
Type: System.String
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: Credential
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -Value

Specifies the secret, or the part named after `#`, as a **SecureString**. If you omit this
parameter, the cmdlet asks for the value.

```yaml
Type: System.Security.SecureString
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: Secret
  Position: 1
  IsRequired: false
  ValueFromPipeline: true
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

### System.Security.SecureString

You can pipe the secret to this cmdlet.

## OUTPUTS

### None

This cmdlet returns no output. It writes the entry name to the information stream.

## NOTES

When automatic backups are on, the cmdlet also writes a backup. If the backup fails, the cmdlet
writes a warning and the vault keeps the new entry.

`lv set <name> [--file <path> | --credential] [--local]`, `lv set <name>#<part>`,
`lv require <name> [--note <text>]` and
`lv mark <name> shared|local` run this cmdlet.

## RELATED LINKS

- [Get-VaultEntry](Get-VaultEntry.md)
- [Remove-VaultEntry](Remove-VaultEntry.md)
- [Restore-VaultFile](Restore-VaultFile.md)
- [about_LocalVault](about_LocalVault.md)
