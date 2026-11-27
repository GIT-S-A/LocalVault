---
document type: cmdlet
external help file: LocalVault.PowerShell.dll-Help.xml
HelpUri: https://github.com/GIT-S-A/LocalVault/blob/main/docs/LocalVault/Invoke-VaultScp.md
Locale: en-US
Module Name: LocalVault
ms.date: 11/20/2026
PlatyPS schema version: 2024-05-01
title: Invoke-VaultScp
---

# Invoke-VaultScp

## SYNOPSIS

Runs scp with the key of an SSH key entry.

## SYNTAX

### __AllParameterSets

```
Invoke-VaultScp [-Name] <string> -ArgumentList <string[]> [-Path <string>] [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

The `Invoke-VaultScp` cmdlet runs `scp.exe` from the OpenSSH folder that `Set-VaultSshTools` shows,
with the key file of an SSH key entry:

`scp -i <key file> [-i <old key file>] -o IdentitiesOnly=yes <arguments>`

The key files, the missing-key prompt and the exit code follow the rules of
[Invoke-VaultSsh](Invoke-VaultSsh.md). The arguments name the files and the hosts; a port is given
with `-P <port>`. The cmdlet fails when the OpenSSH folder has no `scp.exe`.

## EXAMPLES

### Example 1 - Copy a file to a host

```powershell
Invoke-VaultScp -Name deploy -ArgumentList 'backup.tar', 'deploy@srv1.example.com:/srv/backup/'
```

## PARAMETERS

### -ArgumentList

Specifies the scp arguments: options, sources and destination.

```yaml
Type: System.String[]
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: true
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -Name

Specifies the name of the SSH key entry.

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

### None

This cmdlet returns no output. scp writes to the console, and `$LASTEXITCODE` holds its exit code.

## NOTES

`lv scp <name> <scp arguments>` runs this cmdlet.

## RELATED LINKS

- [Invoke-VaultSsh](Invoke-VaultSsh.md)
- [Get-VaultSshKeyPath](Get-VaultSshKeyPath.md)
