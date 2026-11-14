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
Set-VaultEntry [-Name] <string> [[-Value] <securestring>] [-Hello] [-Local] [-Path <string>]
 [-WhatIf] [-Confirm] [<CommonParameters>]
```

### File

```
Set-VaultEntry [-Name] <string> -FilePath <string> [-StoredPath <string>] [-Hello] [-Local]
 [-Path <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

### Credential

```
Set-VaultEntry [-Name] <string> -Credential [-UserName <string>] [-Password <securestring>]
 [-Totp <securestring>] [-Url <string[]>] [-Hello] [-Local] [-Path <string>] [-WhatIf] [-Confirm]
 [<CommonParameters>]
```

### Sharing

```
Set-VaultEntry [-Name] <string> -Sharing <VaultEntrySharing> [-Path <string>] [-WhatIf] [-Confirm]
 [<CommonParameters>]
```

### Required

```
Set-VaultEntry [-Name] <string> -Required [-Credential] [-StoredPath <string>] [-Note <string>]
 [-Hello] [-Path <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

### Protection

```
Set-VaultEntry [-Name] <string> -Protection <VaultEntryProtection> [-Path <string>] [-WhatIf]
 [-Confirm] [<CommonParameters>]
```

### Reference

```
Set-VaultEntry [-Name] <string> -Reference <string> [-Path <string>] [-WhatIf] [-Confirm]
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
With **Required** and **Credential**, the declaration asks for a user name and a password: setting
the value with the name alone asks for the parts of a credential. With **Required** and
**StoredPath**, the declaration asks for a file, such as an SSH private key, and **StoredPath** is
where it goes on each machine. A path that starts with `~/` is under the user's profile, such as
`~/.ssh/id_ed25519`. Each person then sets the value with **FilePath**; the entry keeps the declared
path unless **StoredPath** gives another one, or takes the `~/` form of **FilePath** when it is under
the profile. Only a required entry's own value can have a `~/` path.

`Export-VaultToStore` also writes each value set here to the machine's personal store, when one is
defined, and `Import-VaultFromStore` fills it in on another machine.

With the **Reference** parameter, a personal value comes from an entry of the personal store outside
the `LocalVault` group, found by its group path and title, or by its title alone. The cmdlet opens
the personal store, which asks for its password, and stores the value it reads. A secret takes the
entry's password; a credential takes its user name, password, URL and TOTP; a file or SSH key takes
its attachment. `Export-VaultToStore` then writes KeePass references to that entry, never the value,
and `Import-VaultFromStore` takes a change made in KeePass without a question. A value changed on
this machine stays on this machine, with a warning, and `Export-VaultToStore` refuses to save it.
Shared entries can't be references.

With the **Hello** parameter, the entry is also sealed with Windows Hello. Reading its value then
asks for Windows Hello: face, fingerprint or PIN. One command asks once, whatever the number of
Windows Hello entries it reads. Sealing asks for nothing, except the first time on a PC, when the
cmdlet creates the user's Windows Hello key. A new value for an entry sealed with Windows Hello is
sealed too. The **Protection** parameter alone seals an existing entry, or removes its seal, and
keeps its value. Windows Hello seals secrets, credentials and files, not SSH keys. A credential is
sealed whole: its user name and URLs are hidden too. When Windows Hello is unavailable, the cmdlet
fails and writes nothing: it never stores such an entry with DPAPI only.

The cmdlet refuses an SSH key entry, except with **Sharing**: a key is created with
[New-VaultSshKey](New-VaultSshKey.md), and replaced with its **Replace** parameter.

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

### Example 5 - Declare a personal SSH key and store yours

```powershell
Set-VaultEntry -Name ssh/deploy -Required -StoredPath '~/.ssh/id_deploy' -Note 'your deploy key'
Set-VaultEntry -Name ssh/deploy -FilePath $HOME\.ssh\id_deploy
```

The key stays out of the project store. `Export-VaultToStore` writes it to the personal store, and
`Import-VaultFromStore` writes it to `~/.ssh/id_deploy` on another machine.

### Example 6 - Seal a secret with Windows Hello

```powershell
Set-VaultEntry -Name registry.token -Hello
Get-VaultEntry -Name registry.token -AsPlainText | docker login registry.example.com --username ci --password-stdin
```

The first command asks for the value. The second asks for Windows Hello, then passes the value to
the standard input of `docker login`.

### Example 7 - Remove the Windows Hello seal

```powershell
Set-VaultEntry -Name registry.token -Protection Dpapi
```

### Example 8 - Copy a secret from another store

```powershell
Get-Secret -Name CiToken -Vault SecretStore | Set-VaultEntry -Name ci.token
```

### Example 9 - Take a personal value from an entry of the personal store

```powershell
Set-VaultEntry -Name github.token -Reference Internet/GitHub
```

The required entry `github.token` takes the password of `Internet/GitHub` in the personal store.
`Export-VaultToStore` writes `{REF:P@I:<uuid>}` to the personal store instead of the token.

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

Indicates that the entry is a credential: a user name, a password, an optional TOTP and URLs. With
**Required**, it declares that each person's value is a user name and a password.

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

### -Hello

Indicates that the entry is sealed with Windows Hello. Reading its value then asks for Windows Hello.
With **Required**, the declaration asks each person's value to be sealed with Windows Hello.

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

### -Protection

Specifies how the entry is protected, and keeps its value. `Hello` seals the entry with Windows
Hello. `Dpapi` removes the Windows Hello seal, which asks for Windows Hello once. The kind, the value,
the creation time and the sharing of the entry don't change.

```yaml
Type: LocalVault.PowerShell.VaultEntryProtection
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: Protection
  Position: Named
  IsRequired: true
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues:
- Hello
- Dpapi
HelpMessage: ''
```

### -Reference

Specifies the entry of the personal store that holds the personal value: its group path and title,
such as `Internet/GitHub`, or its title alone when only one entry has it. Entries under the
`LocalVault` group are not searched.

```yaml
Type: System.String
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: Reference
  Position: Named
  IsRequired: true
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
vault, or under the user's profile when it starts with `~/` (required entries only). Use forward or
back slashes; a rooted path or `..` is refused. If you omit this parameter, the cmdlet uses the
declared path of a required entry, then the `~/` form of **FilePath** for a required entry, then the
path of **FilePath** relative to the folder of the vault, and fails when the file is outside that
folder. With **Required**, it declares the entry as a file restored to this path.

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

`lv set <name> [--file <path> | --credential] [--local]`, `lv set <name>#<part>`,
`lv require <name> [--credential | --file <path>] [--note <text>]` and
`lv mark <name> shared|local` run this cmdlet.

## RELATED LINKS

- [Get-VaultEntry](Get-VaultEntry.md)
- [Remove-VaultEntry](Remove-VaultEntry.md)
- [Restore-VaultFile](Restore-VaultFile.md)
- [about_LocalVault](about_LocalVault.md)
