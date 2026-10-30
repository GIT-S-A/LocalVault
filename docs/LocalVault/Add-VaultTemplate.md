---
document type: cmdlet
external help file: LocalVault.PowerShell.dll-Help.xml
HelpUri: https://github.com/GIT-S-A/LocalVault/blob/main/docs/LocalVault/Add-VaultTemplate.md
Locale: en-US
Module Name: LocalVault
ms.date: 10/13/2026
PlatyPS schema version: 2024-05-01
title: Add-VaultTemplate
---

# Add-VaultTemplate

## SYNOPSIS

Turns a file that holds secrets into a committed template.

## SYNTAX

### __AllParameterSets

```
Add-VaultTemplate [-File] <string> [-Capture <string[]>] [-Path <string>] [-WhatIf] [-Confirm]
 [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

The `Add-VaultTemplate` cmdlet writes `<file>.template` beside a file that a tool needs with
secrets in it, such as a `.env` file, a `clouds.yaml` file or a `terraform.tfvars` file. The
template keeps the structure of the file, and replaces each secret with a placeholder `{{name}}`, or
`{{scope/key}}` for a scoped name. Commit the template; the real file stays out of git and is
written from the vault by `Write-VaultTemplate`. The cmdlet records the pair in `localvault.json`
at the root of the repository, together with the vault that fills the placeholders.

When the real file exists, git must ignore it. Every value of the vault that is at least 6
characters long becomes its placeholder. **Capture** names keys whose values aren't in the vault
yet: on a `KEY=value`, `KEY: value` or `export KEY="value"` line, the value moves into a new vault
entry named after the key, and the line keeps a placeholder.

When the real file doesn't exist but `<file>.example` does, the template starts from the example.
A line whose key is the name of a vault entry gets that entry's placeholder. A captured key also
gets a placeholder, and the vault declares it as a required value that each person sets with
`lv set <name>`.

The cmdlet never writes a template that contains a vault value. It fails when the template exists
already: edit the real file and run `Update-VaultTemplate` instead.

## EXAMPLES

### Example 1 - Make a template of a .env file and move a token into the vault

```powershell
Add-VaultTemplate .\infra\.env -Capture OS_PASSWORD
git add infra/.env.template localvault.json
```

### Example 2 - Start from a .env.example

```powershell
Add-VaultTemplate .env -Capture API_KEY
lv set API_KEY
```

## PARAMETERS

### -File

Specifies the file that holds secrets. The template is written beside it with the `.template`
extension. The file must be inside the git repository.

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

### -Capture

Specifies keys whose values move from the file into new vault entries named after the keys. With
an `.example` file, the keys are declared as required values instead.

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

### -Path

Specifies the path to the vault file, which also names the git repository whose templates the
cmdlet handles. A relative path is resolved from the current location. If you omit this
parameter, the cmdlet uses the `LOCALVAULT_PATH` environment variable, and then
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

### LocalVault.PowerShell.VaultTemplateInfo

This cmdlet returns the **Path** of the file, its **Template**, the **Vault** that fills it, whether
the file is **Rendered**, and **UpToDate**, which is empty here.

## NOTES

`lv template add <file> [--capture KEY,...]` runs this cmdlet.

## RELATED LINKS

- [Update-VaultTemplate](Update-VaultTemplate.md)
- [Write-VaultTemplate](Write-VaultTemplate.md)
- [Clear-VaultTemplate](Clear-VaultTemplate.md)
- [Remove-VaultTemplate](Remove-VaultTemplate.md)
- [Get-VaultTemplate](Get-VaultTemplate.md)
- [about_LocalVault](about_LocalVault.md)
