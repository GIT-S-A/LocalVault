#Requires -Version 7.6
Set-StrictMode -Version Latest

$script:LvSyntax = @{
    help    = @{ Usage = 'lv'; Min = 0; Max = 0; Options = @() }
    start   = @{ Usage = 'lv start [--path <vault>]'; Min = 0; Max = 0; Options = @() }
    list    = @{ Usage = 'lv list [<scope>]'; Min = 0; Max = 1; Options = @() }
    get     = @{ Usage = 'lv get <name>[#user|#totp|#url<n>] [--show]   (secrets without --show: into a pipe or a redirection only)'; Min = 1; Max = 1; Options = @('show') }
    clip    = @{ Usage = 'lv clip <name>[#user|#totp|#url<n>]'; Min = 1; Max = 1; Options = @() }
    set     = @{ Usage = 'lv set <name> [--file <path> | --credential] [--local] [--hello]   or   lv set <name>#user|#password|#totp|#url<n>   or   lv set <name> --ref <group/title> [--ref-store <store>]'; Min = 1; Max = 1; Options = @('file', 'local', 'credential', 'hello', 'ref', 'ref-store') }
    require = @{ Usage = 'lv require <name> [--credential | --file <restore path, e.g. ~/.ssh/id_ed25519>] [--note <where to get it>] [--hello]'; Min = 1; Max = 1; Options = @('note', 'credential', 'file', 'hello') }
    mark    = @{ Usage = 'lv mark <name> shared|local|hello|nohello'; Min = 2; Max = 2; Options = @() }
    hello   = @{ Usage = 'lv hello   (Windows Hello key state on this PC)'; Min = 0; Max = 0; Options = @() }
    rm      = @{ Usage = 'lv rm <name>'; Min = 1; Max = 1; Options = @() }
    env     = @{ Usage = 'lv env <VARIABLE>=<name> [<VARIABLE>=<name> ...]'; Min = 1; Max = [int]::MaxValue; Options = @() }
    restore = @{ Usage = 'lv restore [<name> ...] [--overwrite]'; Min = 0; Max = [int]::MaxValue; Options = @('overwrite') }
    clear   = @{ Usage = 'lv clear'; Min = 0; Max = 0; Options = @() }
    save    = @{ Usage = 'lv save [--store <name>] [--replace a,b | --replace-all]   (every vault of the repository; --path for one)'; Min = 0; Max = 0; Options = @('store', 'replace', 'replace-all') }
    load    = @{ Usage = 'lv load [--store <name>] [--replace a,b | --replace-all]   (every vault of the repository; --path for one)'; Min = 0; Max = 0; Options = @('store', 'replace', 'replace-all') }
    export  = @{ Usage = 'lv export <file> [--overwrite]'; Min = 1; Max = 1; Options = @('overwrite') }
    import  = @{ Usage = 'lv import <file> [--replace a,b | --replace-all] [--keyfile <file>]'; Min = 1; Max = 1; Options = @('replace', 'replace-all', 'keyfile') }
    store   = @{ Usage = 'lv store [list | add [<name>] <target> [<keyfile>] [--default] [--unlock-with <store>] [--mirror] [--personal] | add <name> [<target>] --project [--default] [--mirror] [--source] | set <name> [--mirror | --no-mirror] [--unlock-with <store> | --no-unlock] [--default] [--personal | --no-personal] [--project] [--source | --no-source] | rm <name> [--project] | use <name> | diff [--store <name>] | copy <target | name> [--keyfile <file>]]'; Min = 0; Max = 4; Options = @('project', 'default', 'unlock-with', 'mirror', 'no-mirror', 'personal', 'no-personal', 'no-unlock', 'store', 'keyfile', 'source', 'no-source') }
    update  = @{ Usage = 'lv update [<tag>] [--downgrade]'; Min = 0; Max = 1; Options = @('downgrade') }
    template = @{ Usage = 'lv template [add <file> [--capture KEY,...] | update [<file>] [--accept] | render [<file>] | clear [<file>] | rm <file>]'; Min = 0; Max = 2; Options = @('capture', 'accept') }
    run     = @{ Usage = 'lv run [<VARIABLE>=<name> ...] [--stdin <name>] [--] <command> [<arguments> ...]'; Min = 0; Max = [int]::MaxValue; Options = @('stdin') }
    'ssh-key' = @{ Usage = 'lv ssh-key [list | new <name> [--host <user@host>]... [--shared] [--type ed25519|rsa|ecdsa] [--comment <text>] [--overwrite] [--replace] | hosts <name> [<user@host> ...] | path <name> | tools [<dir> | --default] | include on|off]'; Min = 0; Max = [int]::MaxValue; Options = @('host', 'shared', 'type', 'comment', 'overwrite', 'replace', 'default') }
    ssh     = @{ Usage = 'lv ssh <name> [<user@host>] [-- <ssh arguments>]   (the host: a word with @, or one of the key''s hosts)'; Min = 1; Max = [int]::MaxValue; Options = @() }
    scp     = @{ Usage = 'lv scp <name> -- <scp arguments>'; Min = 1; Max = [int]::MaxValue; Options = @() }
    'user-secrets' = @{ Usage = 'lv user-secrets [list [<id|project>] | set <id|project> <key> [--entry <name[#part]>] [--personal] | get <id|project> <key> [--show] | pick | untrack <id|project> <key> | load [<id|project>] [--overwrite] | clear [<id|project>]]'; Min = 0; Max = 3; Options = @('entry', 'personal', 'show', 'overwrite') }
}
$script:LvValueOptions = @('path', 'file', 'replace', 'store', 'unlock-with', 'keyfile', 'note', 'capture', 'host', 'type', 'comment', 'stdin', 'ref', 'ref-store', 'entry')
$script:LvListOptions = @('host')
$script:LvFlagOptions = @('replace-all', 'overwrite', 'downgrade', 'default', 'local', 'hello', 'show', 'credential', 'project', 'mirror', 'no-mirror', 'personal', 'no-personal', 'no-unlock', 'accept', 'version', 'help', 'shared', 'source', 'no-source')
$script:LvSubcommands = @{
    store  = @('list', 'add', 'set', 'rm', 'use', 'diff', 'copy')
    template = @('add', 'update', 'render', 'clear', 'rm')
    'ssh-key' = @('list', 'new', 'hosts', 'path', 'tools', 'include')
    'user-secrets' = @('list', 'set', 'get', 'pick', 'untrack', 'load', 'clear')
}
$script:LvEntryCommands = @('get', 'clip', 'rm', 'mark', 'set', 'restore')
$script:LvTargetPattern = '[\\/]|\.kdbx$|^[A-Za-z][A-Za-z0-9]+:'

function Get-LocalVaultRelease {
    $manifest = Import-PowerShellDataFile -LiteralPath (Join-Path $PSScriptRoot 'LocalVault.psd1')
    $prerelease = $manifest.PrivateData.PSData['Prerelease']
    if ($prerelease) { "$($manifest.ModuleVersion)-$prerelease" } else { $manifest.ModuleVersion }
}

function Get-LocalVaultCompletion {
    param([string]$WordToComplete, [System.Management.Automation.Language.CommandAst]$CommandAst)

    $typed = @($CommandAst.CommandElements | Select-Object -Skip 1 | ForEach-Object { $_.Extent.Text.Trim("'", '"') })
    if ($WordToComplete -ne '' -and $typed.Count -gt 0 -and $typed[-1] -eq $WordToComplete) {
        $typed = @($typed | Select-Object -SkipLast 1)
    }

    $positional = [System.Collections.Generic.List[string]]::new()
    $vaultPath = $null
    for ($i = 0; $i -lt $typed.Count; $i++) {
        if ($typed[$i] -match '^--(.+)$') {
            if ($script:LvValueOptions -contains $Matches[1] -and $i + 1 -lt $typed.Count -and -not ($Matches[1] -eq 'replace' -and $positional.Count -gt 0 -and $positional[0] -eq 'ssh-key')) {
                if ($Matches[1] -eq 'path') { $vaultPath = $typed[$i + 1] }
                $i++
            }
        }
        else { $positional.Add($typed[$i]) }
    }

    $previous = if ($typed.Count -gt 0) { $typed[-1] } else { '' }
    $command = if ($positional.Count -gt 0) { $positional[0] } else { $null }
    $candidates = @(
        if ($previous -in '--store', '--unlock-with' -or ($command -eq 'store' -and $positional.Count -eq 2 -and $positional[1] -in 'rm', 'use', 'copy', 'set')) {
            $vault = if ($vaultPath) { @{ Path = $vaultPath } } else { @{} }
            Get-VaultStore @vault -ErrorAction SilentlyContinue | ForEach-Object Name | Select-Object -Unique
        }
        elseif ($command -eq 'mark' -and $positional.Count -eq 2) {
            'shared', 'local', 'hello', 'nohello'
        }
        elseif ($previous -match '^--(.+)$' -and $script:LvValueOptions -contains $Matches[1]) {
        }
        elseif ($WordToComplete.StartsWith('-')) {
            $allowed = if ($command -and $script:LvSyntax.ContainsKey($command)) { $script:LvSyntax[$command].Options } else { @('help') }
            @($allowed) + 'path' | ForEach-Object { "--$_" }
        }
        elseif ($null -eq $command) {
            $script:LvSyntax.Keys | Where-Object { $_ -ne 'help' } | Sort-Object
        }
        elseif ($positional.Count -eq 1 -and $script:LvSubcommands.ContainsKey($command)) {
            $script:LvSubcommands[$command]
        }
        elseif (($command -eq 'ssh-key' -and $positional.Count -eq 2 -and $positional[1] -in 'hosts', 'path') -or ($command -in 'ssh', 'scp' -and $positional.Count -eq 1)) {
            [LocalVault.PowerShell.LocalVaultCompletion]::SshKeyNames($vaultPath, (Get-Location -PSProvider FileSystem).ProviderPath)
        }
        elseif ($script:LvEntryCommands -contains $command -and ($positional.Count -eq 1 -or $command -eq 'restore')) {
            [LocalVault.PowerShell.LocalVaultCompletion]::EntryNames($vaultPath, (Get-Location -PSProvider FileSystem).ProviderPath, $command -eq 'restore')
        }
    )

    $candidates | Where-Object { $_ -like "$WordToComplete*" } | ForEach-Object {
        [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
    }
}

function Invoke-LocalVault {
    # .ExternalHelp LocalVault.PowerShell.dll-Help.xml
    [Alias('lv')]
    param(
        [Parameter(ValueFromRemainingArguments)]
        [string[]]$CommandLine
    )

    $Arguments = @($CommandLine | Where-Object { $null -ne $_ })
    $options = @{}
    $words = [System.Collections.Generic.List[string]]::new()
    $Command = 'help'
    $vault = @{}
    $badOptions = [System.Collections.Generic.List[string]]::new()

    $usage = @'
lv <command> [<words>] [--path <vault>]      lv --version: the installed release

Entries
  start                                where this project stands, and what to run next
  list [<scope>]                       names, kind, store state, age; never a value
  get <name> [--show]                  prints a value into a pipe; --show prints it on screen
  clip <name>                          copies a value to the clipboard for 20 s
  set <name> [--file <path>] [--local] asks for the value (or reads redirected input); --file carries a file
  set <name> --credential              asks for a user name, password, TOTP and URLs, kept as one entry
  set <name> --ref <group/title>       takes the value from a KeePass entry outside LocalVault's groups;
    [--ref-store <store>]              lv save writes where it comes from, never the value. Searched in
                                       --ref-store, else your personal store (personal values) or the
                                       vault's store (shared values)
  <name>#user #totp #url #url2 ...     one part of a credential, for get, clip, set, env, run and templates;
                                       #totp is the current code
  require <name> [--note <text>]       declares a personal value each person sets; the store gets the declaration only
  require <name> --credential          the same, for a user name and a password
  require <name> --file <path>         the same, for a file such as an SSH key; ~/ is your profile
  mark <name> shared|local             sends the entry to the store, or keeps it on this PC only
  mark <name> hello|nohello            reads need Windows Hello (face, fingerprint or PIN), or DPAPI only
  set|require <name> ... --hello       the same, when the value is set
  hello                                Windows Hello on this PC: key, TPM or software, entries not readable here
  rm <name>                            removes an entry
  env <VARIABLE>=<name> ...            sets environment variables in this window
  restore [<name> ...] [--overwrite]   writes file entries back to disk
  clear                                deletes the vault

Moving entries
  save | load                          every vault of the repository to / from its store, one password per store
  export <file> [--overwrite]          writes a passphrase-sealed export
  import <file> [--replace a,b] [--replace-all] [--keyfile <file>]

Stores
  store                                each vault's store, and what this PC still needs
  store list                           stores of this PC and of the project
  store add [<name>] <target> [<keyfile>] [--default] [--unlock-with <store>] [--mirror] [--personal]
                                       a store of this PC; --mirror: every save on this PC also goes there;
                                       --personal: keeps your own values of required entries
  store add <name> [<target>] --project [--default] [--mirror] [--source]
                                       a store of the project; a name alone refers to each PC's store of that name;
                                       --source: shared entries may take their values from it with --ref
  store set <name> [--mirror|--no-mirror] [--unlock-with <store>|--no-unlock] [--default] [--personal|--no-personal] [--project] [--source|--no-source]
                                       changes one setting of a store
  store rm <name> [--project]          forgets a store
  store use <name>                     the project's primary store: lv load reads it (--path: one vault only)
  store diff                           compares a vault with its store
  store copy <target | name>           one-off copy of the shared entries to another store

Files that need secrets
  template                             each templated file: written or not, up to date or not
  template add <file> [--capture KEY,...]   turns a file (or its .example) into <file>.template, secrets as {{name}}
  template update [<file>] [--accept]  takes your edits of the real file into its template
  template render [<file>]             writes the real files from their templates and the vault
  template clear [<file>]              deletes the written files
  template rm <file>                   stops managing a file
  run [VAR=name ...] [--] <command>    sets the variables, writes the templated files, runs, then deletes them
  run --stdin <name> [--] <program>    also sends the value to the program's standard input, never as text

SSH keys (for SSH to hosts; files in ~/.ssh/<repository>/)
  ssh-key                              the repository's keys: personal or shared, fingerprint, hosts
  ssh-key new <name> [--host <user@host>]... [--shared] [--type ed25519|rsa|ecdsa]
                                       creates a key pair: personal (your own, in your personal store) or
                                       --shared (one key for the team, in the project's store);
                                       --replace replaces an existing key, the old one kept as fallback
  ssh-key hosts <name> [<user@host> ...]   the hosts a key is for; none clears them
  ssh-key path <name>                  the key file, for tools that take -i
  ssh-key tools [<dir> | --default]    the OpenSSH folder in use; <dir> replaces Windows OpenSSH on this PC
  ssh-key include on|off               adds or removes 'Include ~/.ssh/*/lv.config' in ~/.ssh/config, so other
                                       OpenSSH tools use the keys for their hosts
  ssh <name> [<user@host>] [-- <args>] connects with the key; the host may be left out when the key has one.
                                       The second word is the host when it holds @ or is one of the key's hosts;
                                       the other words go to ssh
  scp <name> -- <scp arguments>        copies files with the key

.NET user secrets (secrets.json of each UserSecretsId; <id|project>: the id, or a project's name)
  user-secrets [<id|project>]          each id, its projects, tracked keys and their state, untracked keys
  user-secrets pick                    choose an id, then a key already in its secrets.json or a new one
  user-secrets set <id|project> <key>  asks for the value, keeps it in the vault, tracks it, writes it;
    [--entry <name[#part]>] [--personal]   --entry tracks an existing entry; --personal: each person's own
  user-secrets get <id|project> <key> [--show]   the tracked value, as lv get
  user-secrets untrack <id|project> <key>   stops tracking a key; its vault entry stays
  user-secrets load [<id|project>] [--overwrite]   writes the tracked values; lv load does it too
  user-secrets clear [<id|project>]    removes the tracked values that still match the vault

  update [<tag>] [--downgrade]         installs the newest LocalVault release, or the given tag

Details: Get-Help Invoke-LocalVault -Full

'@

    $syntax = $script:LvSyntax
    $valueOptions = $script:LvValueOptions
    $flagOptions = $script:LvFlagOptions

    $fail = {
        param([string]$Message)
        $line = if ($syntax.ContainsKey($Command)) { "`nUsage: $($syntax[$Command].Usage)" } else { '' }
        throw "$Message$line"
    }

    $location = {
        [LocalVault.Engine.VaultLocation]::Resolve(
            $options['path'], $env:LOCALVAULT_PATH, (Get-Location -PSProvider FileSystem).ProviderPath)
    }

    try {
        $runCommand = @()
        $start = 0
        while ($start + 1 -lt $Arguments.Count -and $Arguments[$start] -eq '--path') { $start += 2 }
        if ($start -lt $Arguments.Count -and $Arguments[$start] -eq 'run') {
            $start++
            while ($start -lt $Arguments.Count -and ($Arguments[$start] -match '^[A-Za-z_][A-Za-z0-9_]*=' -or ($Arguments[$start] -in '--path', '--stdin' -and $start + 1 -lt $Arguments.Count))) {
                $start += if ($Arguments[$start] -in '--path', '--stdin') { 2 } else { 1 }
            }
            $runCommand = @($Arguments | Select-Object -Skip $start)
            if ($runCommand.Count -gt 0 -and $runCommand[0] -eq '--') { $runCommand = @($runCommand | Select-Object -Skip 1) }
            $Arguments = @($Arguments | Select-Object -First $start)
        }
        elseif ($start -lt $Arguments.Count -and $Arguments[$start] -in 'ssh', 'scp') {
            $dash = [Array]::IndexOf([string[]]$Arguments, '--', $start)
            if ($dash -ge 0) {
                $runCommand = @($Arguments | Select-Object -Skip ($dash + 1))
                $Arguments = @($Arguments | Select-Object -First $dash)
            }
        }
        for ($i = 0; $i -lt $Arguments.Count; $i++) {
            $argument = $Arguments[$i]
            if ($argument -notmatch '^--(.+)$') {
                $words.Add($argument)
            }
            elseif ($Matches[1] -eq 'replace' -and $words.Count -gt 0 -and $words[0] -eq 'ssh-key') {
                $options['replace'] = $true
            }
            elseif ($valueOptions -contains $Matches[1]) {
                if ($i + 1 -ge $Arguments.Count -or $Arguments[$i + 1].StartsWith('--')) { $badOptions.Add("$argument needs a value.") }
                elseif ($script:LvListOptions -contains $Matches[1]) { $options[$Matches[1]] = @($options[$Matches[1]] | Where-Object { $null -ne $_ }) + $Arguments[++$i] }
                else { $options[$Matches[1]] = $Arguments[++$i] }
            }
            elseif ($flagOptions -contains $Matches[1]) {
                $options[$Matches[1]] = $true
            }
            else {
                $badOptions.Add("Unknown option $argument.")
            }
        }

        if ($options['version'] -and $words.Count -eq 0 -and $badOptions.Count -eq 0) {
            "LocalVault $(Get-LocalVaultRelease)"
            Set-Variable -Name LASTEXITCODE -Scope Global -Value 0
            return
        }
        if ($options['help']) { $words.Clear() }
        elseif ($words.Count -gt 0) {
            $Command = $words[0]
            $words.RemoveAt(0)
        }
        if ($Command -eq '-h') { $Command = 'help' }
        if (-not $syntax.ContainsKey($Command)) {
            throw "Unknown command '$Command'. Commands: start list get clip set require mark rm env restore clear save load export import store template run ssh-key ssh scp user-secrets hello update. Run lv alone for details."
        }

        $rule = $syntax[$Command]
        if ($badOptions.Count -gt 0) { & $fail $badOptions[0] }
        foreach ($name in $options.Keys) {
            if ($name -notin 'path', 'help' -and $rule.Options -notcontains $name) { & $fail "lv $Command doesn't take --$name." }
        }
        if ($words.Count -gt $rule.Max) {
            & $fail "Too many words for lv $($Command): $($words.Count), at most $($rule.Max). Nothing was done. lv asks for secret values; they never go on the command line."
        }
        if ($words.Count -lt $rule.Min) { & $fail "lv $Command needs more words." }

        if ($options.ContainsKey('path')) { $vault.Path = $options.path }
        $store = if ($options.ContainsKey('store')) { @{ Store = $options.store } } else { @{} }
        $replace = if ($options.ContainsKey('replace')) { @{ Replace = $options.replace -split ',' } } else { @{} }

        switch ($Command) {
            'help' {
                "LocalVault $(Get-LocalVaultRelease)"
                ''
                $usage
                try {
                    $resolved = & $location
                    $state = if (Test-Path -LiteralPath $resolved.Path -PathType Leaf) { 'exists' }
                    elseif ($resolved.LegacyPath -and (Test-Path -LiteralPath $resolved.LegacyPath -PathType Leaf)) {
                        "not created yet; $([LocalVault.Engine.VaultLocation]::LegacyFileName) moves into it on the first write"
                    }
                    else { 'not created yet' }
                    $from = if ($resolved.Source -eq 'Environment') { ", from `$env:$([LocalVault.Engine.VaultLocation]::EnvironmentVariable)" } else { '' }
                    "Vault: $($resolved.Path) ($state$from)"
                }
                catch [LocalVault.Core.VaultException] {
                    "Vault: none. This folder isn't in a git repository; use --path or `$env:$([LocalVault.Engine.VaultLocation]::EnvironmentVariable)."
                }
            }
            'start' {
                try { $resolved = & $location }
                catch [LocalVault.Core.VaultException] {
                    'This folder is not in a git repository.'
                    '  cd <project folder>; lv start      or      lv start --path <vault file>'
                    break
                }

                $next = [System.Collections.Generic.List[string]]::new()
                $stores = @(Get-VaultStore @vault)
                $vaults = @(Get-VaultStoreStatus @vault 3>$null 6>$null)
                $currentLabel = if ($resolved.GitRoot) { [IO.Path]::GetRelativePath($resolved.GitRoot, $resolved.Path) -replace '\\', '/' } else { $resolved.Path }
                $current = $vaults | Where-Object Vault -EQ $currentLabel | Select-Object -First 1
                $exists = Test-Path -LiteralPath $resolved.Path -PathType Leaf
                $legacy = $resolved.LegacyPath -and (Test-Path -LiteralPath $resolved.LegacyPath -PathType Leaf)
                $unlocked = @($stores | Where-Object UnlockWith).Count -gt 0
                $reachable = @($vaults | Where-Object { -not $_.Problem }).Count -gt 0
                $pending = [int]($vaults | Measure-Object -Property Pending -Sum).Sum + [int]($vaults | Measure-Object -Property RemovalsPending -Sum).Sum
                $others = @(if ($resolved.GitRoot) {
                        $resolved.FindOtherVaultFiles() | Where-Object { $_ -like '*.lv' } | ForEach-Object {
                            [IO.Path]::GetRelativePath((Get-Location -PSProvider FileSystem).ProviderPath, $_)
                        }
                    })
                $ignored = $true
                if ($resolved.GitRoot) {
                    try { $ignored = [LocalVault.Engine.GitIgnore]::IsIgnored($resolved.Path) } catch { $ignored = $false }
                }

                $state = if ($exists) {
                    $null = Get-VaultSshKey @vault
                    $entries = @(Get-VaultEntryList @vault 3>$null 6>$null)
                    "$($entries.Count) entries$(if ($current) { ", $($current.Pending + $current.RemovalsPending) change(s) not in the store" })"
                }
                elseif ($legacy) { "not created yet; $([LocalVault.Engine.VaultLocation]::LegacyFileName) is here" }
                else { 'not created yet' }
                if ($resolved.GitRoot) { "Project: $($resolved.GitRoot)" }
                "Vault:   $($resolved.Path) ($state)"
                foreach ($item in $vaults) {
                    $where = if ($item.Problem) { "no store: $($item.Problem)" } else { "store '$($item.Store)' ($($item.Scope)), $($item.Group)" }
                    "  $($item.Vault) -> $where"
                }
                "Stores:  $(if ($stores.Count -gt 0) { ($stores | ForEach-Object { "$($_.Name) ($($_.Scope)$(if ($_.Default) { ', default' })$(if ($_.Personal) { ', personal' })$(if ($_.UnlockWith) { ", unlocked with $($_.UnlockWith)" }))" }) -join ', ' } else { 'none' })"

                $missing = @($vaults | Where-Object { $_.Problem -and $_.Store } | Select-Object -ExpandProperty Store -Unique)
                $unassigned = @($vaults | Where-Object { $_.Problem -and -not $_.Store })
                if ($stores.Count -eq 0) { $next.Add('lv store add <name> <path to the team .kdbx> --default   once per PC; with --project: for this project, in every clone') }
                foreach ($name in $missing) { $next.Add("lv store add $name <path to its .kdbx>   used by this repository, not defined on this PC") }
                if ($stores.Count -gt 0 -and $unassigned.Count -gt 0) { $next.Add('lv store use <name>   choose the store of this repository''s vaults') }
                elseif ($resolved.GitRoot -and $stores.Count -gt 0 -and -not (Test-Path -LiteralPath (Join-Path $resolved.GitRoot 'localvault.json'))) { $next.Add('lv store use <name>   name the store in localvault.json, then commit it') }
                if (@($stores | Where-Object Scope -EQ 'Computer').Count -gt 1 -and -not $unlocked) { $next.Add('lv store add <name> <target> --unlock-with <your personal store>   optional: open that store with the password kept in your personal one') }
                if (-not $ignored) { $next.Add("add the line secrets.local.* to $(Join-Path $resolved.GitRoot '.gitignore')   before anything else") }
                if (-not $exists -and $legacy) { $next.Add("lv list   reads $([LocalVault.Engine.VaultLocation]::LegacyFileName); the first change moves it into the vault") }
                elseif (-not $exists -and $others.Count -gt 0 -and -not $options.ContainsKey('path')) {
                    $next.Add("lv start --path $($others[0])   the vault of this repository is in a subfolder")
                    $others = @($others | Select-Object -Skip 1)
                }
                elseif (-not $exists) {
                    if ($reachable) { $next.Add("lv load   create the repository's vaults from their stores") }
                    $next.Add('lv set <name>   create the vault with a first secret')
                }
                else {
                    if ($reachable) {
                        $next.Add('lv load   get the team''s changes, every vault of the repository')
                        if ($pending -gt 0) { $next.Add("lv save   send $pending change(s) to the stores") }
                    }
                    foreach ($entry in $entries | Where-Object Sync -EQ 'Missing') {
                        $next.Add("lv set $($entry.Name)   required, not set on this PC$(if ($entry.Note) { ": $($entry.Note)" })")
                    }
                    if (@($entries | Where-Object Sync -EQ 'Personal').Count -gt 0 -and -not @($stores | Where-Object Personal)) {
                        $next.Add('lv store set <name> --personal   keep your own values in a store of yours; lv load then fills them in on another PC')
                    }
                    $next.Add('lv list   names of the entries')
                }
                $here = (Get-Location -PSProvider FileSystem).ProviderPath
                $listed = @($vaults | ForEach-Object { if ($resolved.GitRoot) { [IO.Path]::GetFullPath((Join-Path $resolved.GitRoot $_.Vault)) } })
                foreach ($other in $others) {
                    if ($listed -notcontains [IO.Path]::GetFullPath((Join-Path $here $other))) { $next.Add("lv start --path $other   another vault of this repository") }
                }
                $next.Add('lv   all commands')
                $suffix = if ($options.ContainsKey('path')) { " --path $($options.path)" } else { '' }
                $vaultCommands = 'lv list', 'lv set <name>'
                $rows = $next | ForEach-Object {
                    $commandText, $why = $_ -split '   ', 2
                    if ($vaultCommands -contains $commandText -or $commandText -like 'lv set *') { $commandText += $suffix }
                    [pscustomobject]@{ Command = $commandText; Why = $why }
                }
                $width = ($rows | ForEach-Object { $_.Command.Length } | Measure-Object -Maximum).Maximum
                ''
                'Next:'
                $rows | ForEach-Object { "  $($_.Command.PadRight($width))   $($_.Why)".TrimEnd() }
            }
            'list' {
                $scope = if ($words.Count -gt 0) { @{ Scope = $words[0] } } else { @{} }
                $entries = @(Get-VaultEntryList @vault @scope)
                if ($entries.Count -gt 0) {
                    $columns = @('Name', 'Kind') + @(if (@($entries | Where-Object User).Count -gt 0) { 'User' }) + @(if (@($entries | Where-Object Hello).Count -gt 0) { 'Hello' }) + @(if (@($entries | Where-Object UserSecret).Count -gt 0) { 'UserSecret' }) + @('Sync', 'Created', 'Age')
                    $entries | Format-Table $columns -AutoSize
                }
                else {
                    $resolved = & $location
                    $message = if (Test-Path -LiteralPath $resolved.Path -PathType Leaf) {
                        if ($scope.Count -gt 0) { "No entries in scope '$($words[0])' of $($resolved.Path)." } else { "No entries in $($resolved.Path)." }
                    }
                    else { "No vault at $($resolved.Path)." }
                    Write-Information -MessageData $message -InformationAction Continue
                }
            }
            'set' {
                if ($options.ContainsKey('ref')) {
                    if ($options.Keys | Where-Object { $_ -in 'file', 'credential', 'local', 'hello' }) { & $fail 'lv set --ref takes no other option than --ref-store.' }
                    $from = if ($options.ContainsKey('ref-store')) { @{ ReferenceStore = $options['ref-store'] } } else { @{} }
                    Set-VaultEntry @vault -Name $words[0] -Reference $options.ref @from
                    break
                }
                $sharing = if ($options['local']) { @{ Local = $true } } else { @{} }
                $file = if ($options.ContainsKey('file')) { @{ FilePath = $options.file } } else { @{} }
                if ($options['credential']) {
                    if ($file.Count -gt 0) { & $fail 'Give --file or --credential, not both.' }
                    $file = @{ Credential = $true }
                }
                Set-VaultEntry @vault -Name $words[0] @file @sharing -Hello:([bool]$options['hello'])
            }
            'require' {
                $note = if ($options.ContainsKey('note')) { @{ Note = $options.note } } else { @{} }
                $file = if ($options.ContainsKey('file')) { @{ StoredPath = $options.file } } else { @{} }
                Set-VaultEntry @vault -Name $words[0] -Required -Credential:([bool]$options['credential']) @note @file -Hello:([bool]$options['hello'])
            }
            'mark' {
                switch ($words[1]) {
                    'shared' { Set-VaultEntry @vault -Name $words[0] -Sharing Shared }
                    'local' { Set-VaultEntry @vault -Name $words[0] -Sharing Local }
                    'hello' { Set-VaultEntry @vault -Name $words[0] -Protection Hello }
                    'nohello' { Set-VaultEntry @vault -Name $words[0] -Protection Dpapi }
                    default { & $fail "lv mark takes shared, local, hello or nohello, not '$($words[1])'." }
                }
            }
            'hello' {
                $status = Get-VaultHelloStatus @vault
                $hardware = switch ($status.Hardware) { $true { ', TPM' } $false { ', software only' } default { '' } }
                "Windows Hello: $($status.State)$hardware$(if ($status.Reason) { " ($($status.Reason))" })"
                "Key:           $(if ($status.KeyId) { $status.KeyId.Substring(0, 16) } else { 'none' }) in $($status.Provider)"
                "This vault:    $($status.Entries) Hello entr$(if ($status.Entries -eq 1) { 'y' } else { 'ies' })$(if ($status.Missing.Count -gt 0) { "; not readable here: $($status.Missing -join ', ') (lv load restores them)" })"
            }
            'get' {
                $plainPart = $words[0] -match '#(user|url\d*)$'
                if (-not ($plainPart -or $options['show'] -or [Console]::IsOutputRedirected -or $MyInvocation.PipelinePosition -lt $MyInvocation.PipelineLength)) {
                    & $fail 'lv get prints a value on screen only with --show. lv clip <name> copies it to the clipboard instead.'
                }
                Get-VaultEntry @vault -Name $words[0] -AsPlainText
            }
            'clip' { Copy-VaultEntry @vault -Name $words[0] }
            'rm' { Remove-VaultEntry @vault -Name $words[0] }
            'env' {
                $map = @{}
                foreach ($pair in $words) {
                    $variable, $name = $pair -split '=', 2
                    if (-not $name) { & $fail "lv env takes VARIABLE=name pairs, not '$pair'." }
                    $map[$variable] = $name
                }
                Use-VaultEnv @vault -Map $map
            }
            'export' {
                Export-Vault @vault -Destination $words[0] -Overwrite:([bool]$options['overwrite'])
            }
            'import' {
                $keyFile = if ($options.ContainsKey('keyfile')) { @{ KeyFile = $options.keyfile } } else { @{} }
                Import-Vault @vault -Source $words[0] @replace @keyFile -ReplaceAll:([bool]$options['replace-all'])
            }
            'restore' {
                if ($words.Count -gt 0) {
                    Restore-VaultFile @vault -Name $words.ToArray() -Overwrite:([bool]$options['overwrite'])
                }
                else {
                    Restore-VaultFile @vault -All -Overwrite:([bool]$options['overwrite'])
                }
            }
            'load' { Import-VaultFromStore @vault @store @replace -ReplaceAll:([bool]$options['replace-all']) | Out-Null }
            'save' { Export-VaultToStore @vault @store @replace -ReplaceAll:([bool]$options['replace-all']) | Out-Null }
            'store' {
                $sub = if ($words.Count -eq 0) { 'status' } else { $words[0] }
                $rest = @($words | Select-Object -Skip 1)
                $allowed = @{
                    status = @(); list = @(); add = @('project', 'default', 'unlock-with', 'mirror', 'personal', 'source'); rm = @('project'); use = @()
                    set = @('project', 'default', 'unlock-with', 'mirror', 'no-mirror', 'personal', 'no-personal', 'no-unlock', 'source', 'no-source')
                    diff = @('store'); copy = @('keyfile')
                }
                if (-not $allowed.ContainsKey($sub)) { & $fail "Unknown store command '$sub'." }
                foreach ($name in $options.Keys) {
                    if ($name -notin 'path', 'help' -and $allowed[$sub] -notcontains $name) { & $fail "lv store $sub doesn't take --$name." }
                }
                $expected = @{ status = 0; list = 0; rm = 1; use = 1; diff = 0; copy = 1; set = 1 }
                if ($expected.ContainsKey($sub) -and $rest.Count -ne $expected[$sub]) {
                    & $fail "lv store $sub takes $($expected[$sub]) word(s) after '$sub'."
                }
                if ($sub -eq 'add' -and $rest.Count -gt 3) { & $fail 'lv store add takes at most a name, a target and a key file.' }
                switch ($sub) {
                    'status' {
                        $statuses = @(Get-VaultStoreStatus @vault 3>$null)
                        $statuses | Format-Table Vault, Store, Mirrors, Scope, UnlockWith, Group, @{ Label = 'To save'; Expression = { $_.Pending + $_.RemovalsPending } } -AutoSize
                        foreach ($item in $statuses | Where-Object Problem) { "$($item.Problem)" }
                        $stores = @(Get-VaultStore @vault)
                        "Stores: $(if ($stores.Count -gt 0) { ($stores | ForEach-Object { "$($_.Name) ($($_.Scope)$(if ($_.Default) { ', default' })$(if ($_.Mirror) { ', mirror' })$(if ($_.Personal) { ', personal' })$(if ($_.UnlockWith) { ", unlocked with $($_.UnlockWith)" }))" }) -join ', ' } else { 'none (lv store add <name> <target>)' })"
                    }
                    'list' { Get-VaultStore @vault | Format-Table Name, Scope, Type, Target, KeyFile, Default, UnlockWith, Mirror, Personal, Exists -AutoSize }
                    'add' {
                        $register = @{ Default = [bool]$options['default']; Project = [bool]$options['project']; Mirror = [bool]$options['mirror']; Personal = [bool]$options['personal']; Source = [bool]$options['source'] }
                        if ($options.ContainsKey('unlock-with')) { $register.UnlockWith = $options['unlock-with'] }
                        switch ($rest.Count) {
                            1 {
                                if ($options['project'] -and $rest[0] -notmatch $script:LvTargetPattern) { $register.Name = $rest[0] }
                                else { $register.Target = $rest[0] }
                            }
                            2 {
                                if ($rest[0] -match $script:LvTargetPattern) { $register.Target = $rest[0]; $register.KeyFile = $rest[1] }
                                else { $register.Name = $rest[0]; $register.Target = $rest[1] }
                            }
                            3 { $register.Name = $rest[0]; $register.Target = $rest[1]; $register.KeyFile = $rest[2] }
                            default { & $fail 'lv store add needs a target, e.g. a .kdbx file.' }
                        }
                        Register-VaultStore @vault @register | Format-Table Name, Scope, Type, Target, KeyFile, Default, UnlockWith, Mirror, Personal, Exists -AutoSize
                    }
                    'rm' { Unregister-VaultStore @vault -Name $rest[0] -Project:([bool]$options['project']) }
                    'set' {
                        if ($options['mirror'] -and $options['no-mirror']) { & $fail 'Give --mirror or --no-mirror, not both.' }
                        if ($options['personal'] -and $options['no-personal']) { & $fail 'Give --personal or --no-personal, not both.' }
                        if ($options['source'] -and $options['no-source']) { & $fail 'Give --source or --no-source, not both.' }
                        $change = @{ Name = $rest[0]; Default = [bool]$options['default']; Project = [bool]$options['project']; NoUnlock = [bool]$options['no-unlock'] }
                        if ($options['mirror']) { $change.Mirror = $true }
                        if ($options['no-mirror']) { $change.Mirror = $false }
                        if ($options['personal']) { $change.Personal = $true }
                        if ($options['no-personal']) { $change.Personal = $false }
                        if ($options['source']) { $change.Source = $true }
                        if ($options['no-source']) { $change.Source = $false }
                        if ($options.ContainsKey('unlock-with')) { $change.UnlockWith = $options['unlock-with'] }
                        Set-VaultStore @vault @change | Format-Table Name, Scope, Type, Target, KeyFile, Default, UnlockWith, Mirror, Personal, Exists -AutoSize
                    }
                    'use' { Set-VaultProjectStore @vault -Name $rest[0] }
                    'diff' { Compare-VaultWithStore @vault @store | Format-Table Name, Kind, State -AutoSize }
                    'copy' {
                        $target = if ($rest[0] -match $script:LvTargetPattern) { @{ Target = $rest[0] } } else { @{ Store = $rest[0] } }
                        $keyFile = if ($options.ContainsKey('keyfile')) { @{ KeyFile = $options.keyfile } } else { @{} }
                        Copy-VaultToStore @vault @target @keyFile | Out-Null
                    }
                }
            }
            'clear' { Clear-Vault @vault }
            'template' {
                $sub = if ($words.Count -eq 0) { 'status' } else { $words[0] }
                $rest = @($words | Select-Object -Skip 1)
                $allowed = @{ status = @(); add = @('capture'); update = @('accept'); render = @(); clear = @(); rm = @() }
                if (-not $allowed.ContainsKey($sub)) { & $fail "Unknown template command '$sub'." }
                foreach ($name in $options.Keys) {
                    if ($name -notin 'path', 'help' -and $allowed[$sub] -notcontains $name) { & $fail "lv template $sub doesn't take --$name." }
                }
                if ($sub -in 'add', 'rm' -and $rest.Count -ne 1) { & $fail "lv template $sub takes one file." }
                if ($sub -eq 'status' -and $rest.Count -gt 0) { & $fail 'lv template alone takes no file.' }
                $file = if ($rest.Count -gt 0) { @{ File = $rest[0] } } else { @{} }
                switch ($sub) {
                    'status' {
                        $templates = @(Get-VaultTemplate @vault)
                        if ($templates.Count -eq 0) { 'No templates in this repository. lv template add <file> starts one.' }
                        else {
                            $templates | Format-Table @{ Label = 'File'; Expression = { [IO.Path]::GetRelativePath((Get-Location -PSProvider FileSystem).ProviderPath, $_.Path) } },
                                @{ Label = 'Written'; Expression = { $_.Rendered } },
                                @{ Label = 'Up to date'; Expression = { if ($null -eq $_.UpToDate) { '' } else { $_.UpToDate } } } -AutoSize
                        }
                    }
                    'add' {
                        $capture = if ($options.ContainsKey('capture')) { @{ Capture = $options.capture -split ',' } } else { @{} }
                        Add-VaultTemplate @vault @file @capture | Out-Null
                    }
                    'update' { Update-VaultTemplate @vault @file -AcceptChanges:([bool]$options['accept']) }
                    'render' { Write-VaultTemplate @vault @file | Format-Table Path, Action -AutoSize }
                    'clear' { Clear-VaultTemplate @vault @file | Format-Table Path, Action -AutoSize }
                    'rm' { Remove-VaultTemplate @vault @file }
                }
            }
            'run' {
                if ($runCommand.Count -eq 0) { & $fail 'lv run needs a command.' }
                $map = @{}
                foreach ($pair in $words) {
                    $variable, $name = $pair -split '=', 2
                    if (-not $name) { & $fail "lv run takes VARIABLE=name pairs before the command, not '$pair'." }
                    $map[$variable] = $name
                }
                $previous = @{}
                foreach ($variable in $map.Keys) { $previous[$variable] = [Environment]::GetEnvironmentVariable($variable) }
                $resolved = & $location
                $templates = @(if ($resolved.GitRoot) { Get-VaultTemplate @vault })
                try {
                    if ($map.Count -gt 0) { Use-VaultEnv @vault -Map $map 6>$null }
                    if ($templates.Count -gt 0) { Write-VaultTemplate @vault | Out-Null }
                    $commandName = $runCommand[0]
                    $commandArguments = @($runCommand | Select-Object -Skip 1)
                    if ($options['stdin']) {
                        $program = Get-Command -Name $commandName -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
                        if (-not $program) { & $fail "lv run --stdin runs a program; '$commandName' is not one." }
                        $sendToStdin = [System.Management.Automation.CmdletInfo]::new('Invoke-VaultStdin', [LocalVault.PowerShell.InvokeVaultStdinCommand])
                        $runExit = & $sendToStdin @vault -Name $options['stdin'] -FilePath $program.Source -ArgumentList $commandArguments
                    }
                    else {
                        & $commandName @commandArguments
                        $runExit = $LASTEXITCODE
                    }
                }
                finally {
                    if ($templates.Count -gt 0) { Clear-VaultTemplate @vault -Confirm:$false | Out-Null }
                    foreach ($variable in $previous.Keys) { [Environment]::SetEnvironmentVariable($variable, $previous[$variable]) }
                }
                Set-Variable -Name LASTEXITCODE -Scope Global -Value $(if ($null -eq $runExit) { 0 } else { $runExit })
                return
            }
            'ssh-key' {
                $sub = if ($words.Count -eq 0) { 'list' } else { $words[0] }
                $rest = @($words | Select-Object -Skip 1)
                $allowed = @{ list = @(); new = @('host', 'shared', 'type', 'comment', 'overwrite', 'replace'); hosts = @(); path = @(); tools = @('default'); include = @() }
                if (-not $allowed.ContainsKey($sub)) { & $fail "Unknown ssh-key command '$sub'." }
                foreach ($name in $options.Keys) {
                    if ($name -notin 'path', 'help' -and $allowed[$sub] -notcontains $name) { & $fail "lv ssh-key $sub doesn't take --$name." }
                }
                $expected = @{ list = 0; new = 1; path = 1; include = 1 }
                if ($expected.ContainsKey($sub) -and $rest.Count -ne $expected[$sub]) { & $fail "lv ssh-key $sub takes $($expected[$sub]) word(s) after '$sub'." }
                if ($sub -eq 'hosts' -and $rest.Count -lt 1) { & $fail 'lv ssh-key hosts needs the key name.' }
                if ($sub -eq 'tools' -and ($rest.Count -gt 1 -or ($rest.Count -eq 1 -and $options['default']))) { & $fail 'lv ssh-key tools takes a folder or --default, not both.' }
                switch ($sub) {
                    'list' {
                        $keys = @(Get-VaultSshKey @vault)
                        if ($keys.Count -eq 0) { 'No SSH keys in this vault. lv ssh-key new <name> creates one.' }
                        else { $keys | Format-Table Name, Sharing, Source, Type, Fingerprint, @{ Label = 'Hosts'; Expression = { $_.Hosts -join ', ' } }, Previous -AutoSize }
                    }
                    'new' {
                        $create = @{ Name = $rest[0]; Shared = [bool]$options['shared']; Overwrite = [bool]$options['overwrite']; Replace = [bool]$options['replace'] }
                        if ($options.ContainsKey('host')) { $create.HostName = @($options.host) }
                        if ($options.ContainsKey('type')) { $create.Type = $options.type }
                        if ($options.ContainsKey('comment')) { $create.Comment = $options.comment }
                        New-VaultSshKey @vault @create | Out-Null
                    }
                    'hosts' { Set-VaultSshKeyHost @vault -Name $rest[0] -HostName @($rest | Select-Object -Skip 1) }
                    'path' { Get-VaultSshKeyPath @vault -Name $rest[0] }
                    'tools' {
                        $choice = if ($rest.Count -eq 1) { @{ Directory = $rest[0] } } elseif ($options['default']) { @{ Default = $true } } else { @{} }
                        $tools = Set-VaultSshTools @choice
                        "OpenSSH: $($tools.Directory)$(if ($tools.IsDefault) { ' (Windows OpenSSH)' } else { " (from $($tools.Settings))" })"
                    }
                    'include' {
                        if ($rest[0] -notin 'on', 'off') { & $fail 'lv ssh-key include takes on or off.' }
                        $include = Set-VaultSshInclude -Enabled:($rest[0] -eq 'on')
                        $line = [LocalVault.Engine.SshConfigFile]::IncludeLine
                        if ($include.Enabled) { "Added '$line' to $($include.ConfigPath)." } else { "Removed '$line' from $($include.ConfigPath)." }
                    }
                }
            }
            'ssh' {
                $rest = @($words | Select-Object -Skip 1) + @($runCommand)
                $target = @{}
                if ($words.Count -gt 1) {
                    $hosts = @(Get-VaultSshKey @vault -Name $words[0] -ErrorAction SilentlyContinue | ForEach-Object Hosts)
                    if ($runCommand.Count -gt 0 -or $words[1].Contains('@') -or $hosts -contains $words[1]) {
                        $target.Target = $words[1]
                        $rest = @($rest | Select-Object -Skip 1)
                    }
                }
                Invoke-VaultSsh @vault @target -Name $words[0] -ArgumentList $rest
                return
            }
            'scp' {
                $rest = @($words | Select-Object -Skip 1) + @($runCommand)
                if ($rest.Count -eq 0) { & $fail 'lv scp needs the scp arguments: files and host:path.' }
                Invoke-VaultScp @vault -Name $words[0] -ArgumentList $rest
                return
            }
            'user-secrets' {
                $sub = if ($words.Count -eq 0) { 'list' } else { $words[0] }
                $rest = @($words | Select-Object -Skip 1)
                $allowed = @{ list = @(); set = @('entry', 'personal'); get = @('show'); pick = @(); untrack = @(); load = @('overwrite'); clear = @() }
                if (-not $allowed.ContainsKey($sub)) { & $fail "Unknown user-secrets command '$sub'." }
                foreach ($name in $options.Keys) {
                    if ($name -notin 'path', 'help' -and $allowed[$sub] -notcontains $name) { & $fail "lv user-secrets $sub doesn't take --$name." }
                }
                $counts = @{ list = 0, 1; set = 2, 2; get = 2, 2; pick = 0, 0; untrack = 2, 2; load = 0, 1; clear = 0, 1 }
                if ($rest.Count -lt $counts[$sub][0] -or $rest.Count -gt $counts[$sub][1]) {
                    & $fail "lv user-secrets $sub takes $(if ($counts[$sub][0] -eq $counts[$sub][1]) { $counts[$sub][0] } else { "$($counts[$sub][0]) to $($counts[$sub][1])" }) word(s) after '$sub'."
                }
                $target = if ($rest.Count -gt 0) { @{ Target = $rest[0] } } else { @{} }
                switch ($sub) {
                    'list' {
                        $rows = @(Get-VaultUserSecretList @vault @target)
                        if ($rows.Count -eq 0) { "No project of this repository has a UserSecretsId. dotnet user-secrets init --project <project> adds one." }
                        else { $rows | Format-Table Id, @{ Label = 'Projects'; Expression = { $_.Projects -join ', ' } }, Key, Entry, State -AutoSize }
                    }
                    'set' {
                        $source = if ($options.ContainsKey('entry')) {
                            if ($options['personal']) { & $fail 'Give --entry or --personal, not both.' }
                            @{ Entry = $options.entry }
                        }
                        else { @{ Personal = [bool]$options['personal'] } }
                        Set-VaultUserSecret @vault -Target $rest[0] -Key $rest[1] @source
                    }
                    'get' {
                        if (-not ($options['show'] -or [Console]::IsOutputRedirected -or $MyInvocation.PipelinePosition -lt $MyInvocation.PipelineLength)) {
                            & $fail 'lv user-secrets get prints a value on screen only with --show.'
                        }
                        Get-VaultUserSecret @vault -Target $rest[0] -Key $rest[1] -AsPlainText
                    }
                    'pick' {
                        if (-not [Environment]::UserInteractive -or [Console]::IsInputRedirected) { & $fail 'lv user-secrets pick needs a console. Use lv user-secrets set <id|project> <key> instead.' }
                        $groups = @(Get-VaultUserSecretList @vault | Where-Object { $_.Projects.Count -gt 0 } | Group-Object Id)
                        if ($groups.Count -eq 0) { 'No project of this repository has a UserSecretsId. dotnet user-secrets init --project <project> adds one.'; break }
                        for ($n = 0; $n -lt $groups.Count; $n++) { "  $($n + 1)  $($groups[$n].Name)  $($groups[$n].Group[0].Projects -join ', ')" }
                        $typed = Read-Host 'User secrets to add a key to (number)'
                        $number = 0
                        if (-not [int]::TryParse($typed, [ref]$number) -or $number -lt 1 -or $number -gt $groups.Count) { & $fail "'$typed' is not one of the numbers shown." }
                        $id = $groups[$number - 1].Name
                        $untracked = @($groups[$number - 1].Group | Where-Object State -EQ 'Untracked' | ForEach-Object Key)
                        for ($n = 0; $n -lt $untracked.Count; $n++) { "  $($n + 1)  $($untracked[$n])   already in secrets.json, not tracked" }
                        "  $($untracked.Count + 1)  new key"
                        $typed = Read-Host 'Key (number)'
                        if (-not [int]::TryParse($typed, [ref]$number) -or $number -lt 1 -or $number -gt $untracked.Count + 1) { & $fail "'$typed' is not one of the numbers shown." }
                        if ($number -le $untracked.Count) {
                            Set-VaultUserSecret @vault -Target $id -Key $untracked[$number - 1] -FromFile
                        }
                        else {
                            $key = Read-Host 'New key, such as ConnectionStrings:Default'
                            Set-VaultUserSecret @vault -Target $id -Key $key
                        }
                    }
                    'untrack' { Remove-VaultUserSecret @vault -Target $rest[0] -Key $rest[1] }
                    'load' { Write-VaultUserSecret @vault @target -Overwrite:([bool]$options['overwrite']) }
                    'clear' { Clear-VaultUserSecret @vault @target }
                }
            }
            'update' {
                $installer = Join-Path $PSScriptRoot 'install.ps1'
                if (-not (Test-Path -LiteralPath $installer)) {
                    throw "This copy of LocalVault has no installer. Install it once with the line in the README; lv update works after that."
                }
                $release = if ($words.Count -gt 0) { @{ Version = $words[0] } } else { @{ Latest = $true } }
                if ($options['downgrade']) { $release.Downgrade = $true }
                & $installer @release
            }
        }
        Set-Variable -Name LASTEXITCODE -Scope Global -Value 0
    }
    catch {
        Set-Variable -Name LASTEXITCODE -Scope Global -Value $(if ($_.FullyQualifiedErrorId -like 'LocalVault.ImportConflict*') { 2 } else { 1 })
        Write-Error -ErrorRecord $_
    }
}

Register-ArgumentCompleter -CommandName Invoke-LocalVault, lv -ParameterName CommandLine -ScriptBlock {
    Get-LocalVaultCompletion -WordToComplete $args[2] -CommandAst $args[3]

}