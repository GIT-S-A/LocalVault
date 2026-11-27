---
document type: cmdlet
external help file: LocalVault.PowerShell.dll-Help.xml
HelpUri: https://github.com/GIT-S-A/LocalVault/blob/main/docs/LocalVault/Invoke-VaultSsh.md
Locale: en-US
Module Name: LocalVault
ms.date: 11/20/2026
PlatyPS schema version: 2024-05-01
title: Invoke-VaultSsh
---

# Invoke-VaultSsh

## SYNOPSIS

Runs ssh with the key of an SSH key entry.

## SYNTAX

### __AllParameterSets

```
Invoke-VaultSsh [-Name] <string> [[-Target] <string>] [-ArgumentList <string[]>] [-Path <string>]
 [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

The `Invoke-VaultSsh` cmdlet runs `ssh.exe` from the OpenSSH folder that `Set-VaultSshTools` shows,
with the key file of an SSH key entry. The command line is:

`ssh -i <key file> [-i <old key file>] -o IdentitiesOnly=yes [-p <port>] <user@host> <arguments>`

- The old key file is given while the entry keeps a replaced key, so a host that doesn't have the
  new public key yet still accepts the connection.
- For a key held by an SSH agent, the key file is its `.pub` file; the agent signs.
- The host is **Target**, else the only host of the entry. An entry with several hosts and no
  **Target** gives an error that lists them. A host that the entry doesn't list is allowed.
- ssh reads and writes the console directly: standard input, output and error aren't redirected
  by the cmdlet. `$LASTEXITCODE` is ssh's exit code.

When the key file is missing, the cmdlet writes it first. In an interactive console, a personal key
not set on this PC is asked for, as `Import-VaultFromStore` does. The cmdlet never prints the path
of a private key.

## EXAMPLES

### Example 1 - Open a session on the key's host

```powershell
Invoke-VaultSsh -Name deploy
```

### Example 2 - Run a command on one of the key's hosts

```powershell
Invoke-VaultSsh -Name deploy -Target deploy@srv2.example.com -ArgumentList 'systemctl', 'status', 'nginx'
```

## PARAMETERS

### -ArgumentList

Specifies the arguments placed after the host: ssh options, then the remote command.

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

### -Target

Specifies the host as `[user@]host[:port]`. A port becomes `-p <port>`.

```yaml
Type: System.String
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: 1
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

This cmdlet returns no output. ssh writes to the console, and `$LASTEXITCODE` holds its exit code.

## NOTES

`lv ssh <name> [<user@host>] [<ssh arguments>]` runs this cmdlet. The second word is the host when it holds `@` or is one of the key's hosts; the other words go to ssh.

## RELATED LINKS

- [Invoke-VaultScp](Invoke-VaultScp.md)
- [New-VaultSshKey](New-VaultSshKey.md)
- [Set-VaultSshInclude](Set-VaultSshInclude.md)
