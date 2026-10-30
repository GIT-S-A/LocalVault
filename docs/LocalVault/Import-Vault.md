---
document type: cmdlet
external help file: LocalVault.PowerShell.dll-Help.xml
HelpUri: https://github.com/GIT-S-A/LocalVault/blob/main/docs/LocalVault/Import-Vault.md
Locale: en-US
Module Name: LocalVault
ms.date: 10/08/2026
PlatyPS schema version: 2024-05-01
title: Import-Vault
---

# Import-Vault

## SYNOPSIS

Merges entries from an export, a backup or a `secrets.local.clixml` file into a vault.

## SYNTAX

### Source (Default)

```
Import-Vault [-Source] <string> [-Passphrase <securestring>] [-KeyFile <string>]
 [-Replace <string[]>] [-ReplaceAll] [-Path <string>] [<CommonParameters>]
```


## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

The `Import-Vault` cmdlet reads entries from a source and merges them into the vault. The cmdlet
recognizes the source from its content:

- A `LOCALVAULT-2` export from `Export-Vault`. The cmdlet asks for its passphrase.
- A `LOCALVAULT-2` backup (`.lvbak`). Without the **KeyFile** parameter, the cmdlet opens it with the
  backup key kept in this vault; with it, it also asks for the backup passphrase.
- A `SAAS-VAULT-1` export. `__File:<name>` entries become file entries. SSH keys and other names
  that start with `__` aren't imported; the cmdlet writes a warning for each.
- A `secrets.local.clixml` file of the current Windows account. When it is the file beside the
  vault and every entry ends up identical in the vault, the cmdlet renames it to
  `secrets.local.clixml.migrated-<time>`. It leaves any other `.clixml` file in place.


The cmdlet doesn't delete entries. It skips entries that are identical in both places, and adds
entries that are only in the source. For each entry that differs, it asks; the default answer
keeps the entry of the vault. The **Replace** and **ReplaceAll** parameters answer in advance.
When a source holds names that differ only by case, the cmdlet asks which one to import and says
when it replaces an entry of the vault. **ReplaceAll** doesn't choose among them; **Replace** takes
the exact name.
Without a console, when a difference has no answer, the cmdlet writes nothing and fails with the
error ID `LocalVault.ImportConflict`.

The cmdlet writes the vault once, after all answers.

An export or a backup whose content is damaged, such as an entry with a stored path outside the
vault folder, into a `.git` folder or onto a vault file, isn't imported.

## EXAMPLES

### Example 1 - Import an export from a colleague

```powershell
Import-Vault -Source E:\handover\obs.export
```

```Output
Passphrase for E:\handover\obs.export: ****************
'GrafanaAdminPassword' differs from the vault. Replace it with the imported value?
[Y] Yes  [N] No  [?] Help (default is "N"): y
From E:\handover\obs.export: 3 added, 1 replaced, 2 identical, 0 left as they were.

Added Replaced Identical Skipped Refused
----- -------- --------- ------- -------
    3        1         2       0       0
```

### Example 2 - Go back to the newest automatic backup of this vault

```powershell
$newest = Get-VaultBackup | Select-Object -ExpandProperty Newest
Import-Vault -Source $newest -ReplaceAll
```
### Example 3 - Import in a script and replace two named entries

```powershell
$passphrase = Read-Host -Prompt 'Passphrase' -AsSecureString
$importVaultSplat = @{
    Source     = '.\obs.export'
    Passphrase = $passphrase
    Replace    = 'prod/db.password', 'prod/api.key'
}
Import-Vault @importVaultSplat
```

Other entries that differ keep their value in the vault.

### Example 4 - Import a secrets.local.clixml file from another folder

```powershell
Import-Vault -Source .\tools\secrets.local.clixml -Path .\secrets.local.lv
```

After the import, the cmdlet renames the source file to `secrets.local.clixml.migrated-<time>`.

## PARAMETERS

### -KeyFile

Specifies the keyfile of a backup sealed with a key that this vault doesn't hold, such as a backup made with `Enable-VaultBackup -Manual` or by another vault.

```yaml
Type: System.String
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: Source
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -Passphrase

Specifies the passphrase of the export or backup as a **SecureString**. If you omit this
parameter, the cmdlet asks for it, or reads one line from redirected standard input.

```yaml
Type: System.Security.SecureString
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: Source
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

Specifies the names of entries that the imported value replaces when it differs. Other entries that
differ are asked about, or kept without a console.

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

### -ReplaceAll

Indicates that the imported value replaces every entry that differs, without asking.

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

### -Source

Specifies the file to import: an export, a backup, a `SAAS-VAULT-1` export or a
`secrets.local.clixml` vault.

```yaml
Type: System.String
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: Source
  Position: 0
  IsRequired: true
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

### LocalVault.PowerShell.ImportSummary

This cmdlet returns one object with the number of entries **Added**, **Replaced**, **Identical**,
**Skipped** (differing entries that kept their value) and **Refused** (entries that can't be
imported).

## NOTES

When names in a `secrets.local.clixml` or `SAAS-VAULT-1` source differ only by case, the cmdlet
asks which one to import. Without a console, it fails unless **Replace** names one of them.

`lv import <file>` runs this cmdlet. `lv` exits with code `2` when an import has
unanswered differences.

## RELATED LINKS

- [Export-Vault](Export-Vault.md)
- [Enable-VaultBackup](Enable-VaultBackup.md)
- [Import-VaultFromStore](Import-VaultFromStore.md)
- [about_LocalVault](about_LocalVault.md)
