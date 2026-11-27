#Requires -Version 7.6
Set-StrictMode -Version Latest

# Positional slots: 'Name' required, '[Name]' optional, 'Name...' / '[Name...]' a list that takes the remaining words.
# Parameters: switch, value (one word) or list (an array, or one word split on commas).
$script:LvCommands = [ordered]@{
    help                   = @{ Usage = 'lv | lv help | lv -Version'; Positional = @(); Parameters = @{} }
    start                  = @{ Usage = 'lv start [-Path <vault>]'; Positional = @(); Parameters = @{} }
    init                   = @{ Usage = 'lv init [-Path <vault>]'; Positional = @(); Parameters = @{} }
    list                   = @{ Usage = 'lv list [<scope>]'; Positional = @('[Scope]'); Parameters = @{} }
    get                    = @{ Usage = 'lv get <name>[#user|#totp|#url<n>] [-Show]   (secrets without -Show: into a pipe or a redirection only)'; Positional = @('Name'); Parameters = @{ Show = 'switch' } }
    clip                   = @{ Usage = 'lv clip <name>[#user|#totp|#url<n>]'; Positional = @('Name'); Parameters = @{} }
    set                    = @{ Usage = 'lv set <name> [-FilePath <path> | -Credential] [-Local] [-Hello]   or   lv set <name>#user|#password|#totp|#url<n>   or   lv set <name> -Reference <group/title> [-ReferenceStore <store>]'; Positional = @('Name'); Parameters = @{ FilePath = 'value'; Credential = 'switch'; Local = 'switch'; Hello = 'switch'; Reference = 'value'; ReferenceStore = 'value' } }
    require                = @{ Usage = 'lv require <name> [-Credential | -StoredPath <restore path, e.g. ~/.ssh/id_ed25519>] [-Note <where to get it>] [-Hello]'; Positional = @('Name'); Parameters = @{ Credential = 'switch'; StoredPath = 'value'; Note = 'value'; Hello = 'switch' } }
    mark                   = @{ Usage = 'lv mark <name> shared|local|hello|nohello'; Positional = @('Name', 'State'); Parameters = @{} }
    hello                  = @{ Usage = 'lv hello   (Windows Hello key state on this PC)'; Positional = @(); Parameters = @{} }
    rm                     = @{ Usage = 'lv rm <name>'; Positional = @('Name'); Parameters = @{} }
    reset                  = @{ Usage = 'lv reset <name> ... [-NoPrompt]'; Positional = @('Name...'); Parameters = @{ NoPrompt = 'switch' } }
    env                    = @{ Usage = 'lv env <VARIABLE>=<name> [<VARIABLE>=<name> ...]'; Positional = @('Map...'); Parameters = @{} }
    restore                = @{ Usage = 'lv restore [<name> ...] [-Overwrite]'; Positional = @('[Name...]'); Parameters = @{ Overwrite = 'switch' } }
    clear                  = @{ Usage = 'lv clear'; Positional = @(); Parameters = @{} }
    save                   = @{ Usage = 'lv save [-Store <name>] [-Replace a,b | -ReplaceAll]   (every vault of the repository; -Path for one)'; Positional = @(); Parameters = @{ Store = 'value'; Replace = 'list'; ReplaceAll = 'switch' } }
    load                   = @{ Usage = 'lv load [-Store <name>] [-Replace a,b | -ReplaceAll]   (every vault of the repository; -Path for one)'; Positional = @(); Parameters = @{ Store = 'value'; Replace = 'list'; ReplaceAll = 'switch' } }
    export                 = @{ Usage = 'lv export <file> [-Overwrite]'; Positional = @('Destination'); Parameters = @{ Overwrite = 'switch' } }
    import                 = @{ Usage = 'lv import <file> [-Replace a,b | -ReplaceAll] [-KeyFile <file>]'; Positional = @('Source'); Parameters = @{ Replace = 'list'; ReplaceAll = 'switch'; KeyFile = 'value' } }
    store                  = @{ Usage = 'lv store   (each vault''s store, and what this PC still needs)'; Positional = @(); Parameters = @{} }
    'store list'           = @{ Usage = 'lv store list [<name>]'; Positional = @('[Name]'); Parameters = @{} }
    'store add'            = @{ Usage = 'lv store add [<target>] [-Name <name>] [-KeyFile <file>] [-Default] [-UnlockWith <store>] [-Mirror] [-Personal] [-Project] [-Source]'; Positional = @('[Target]'); Parameters = @{ Name = 'value'; KeyFile = 'value'; Default = 'switch'; UnlockWith = 'value'; Mirror = 'switch'; Personal = 'switch'; Project = 'switch'; Source = 'switch' } }
    'store set'            = @{ Usage = 'lv store set <name> [-Mirror[:$false]] [-UnlockWith <store> | -NoUnlock] [-Default] [-Personal[:$false]] [-Project] [-Source[:$false]]'; Positional = @('Name'); Parameters = @{ Mirror = 'switch'; UnlockWith = 'value'; NoUnlock = 'switch'; Default = 'switch'; Personal = 'switch'; Project = 'switch'; Source = 'switch' } }
    'store rm'             = @{ Usage = 'lv store rm <name> [-Project]'; Positional = @('Name'); Parameters = @{ Project = 'switch' } }
    'store use'            = @{ Usage = 'lv store use <name>   (-Path: that vault only)'; Positional = @('Name'); Parameters = @{} }
    'store diff'           = @{ Usage = 'lv store diff [-Store <name>]'; Positional = @(); Parameters = @{ Store = 'value' } }
    'store copy'           = @{ Usage = 'lv store copy [<target | store name>] [-Store <name>] [-KeyFile <file>]'; Positional = @('[Target]'); Parameters = @{ Store = 'value'; KeyFile = 'value' } }
    template               = @{ Usage = 'lv template   (each templated file: written or not, up to date or not)'; Positional = @(); Parameters = @{} }
    'template add'         = @{ Usage = 'lv template add <file> [-Capture KEY,...]'; Positional = @('File'); Parameters = @{ Capture = 'list' } }
    'template update'      = @{ Usage = 'lv template update [<file>] [-AcceptChanges]'; Positional = @('[File]'); Parameters = @{ AcceptChanges = 'switch' } }
    'template render'      = @{ Usage = 'lv template render [<file>]'; Positional = @('[File]'); Parameters = @{} }
    'template clear'       = @{ Usage = 'lv template clear [<file>]'; Positional = @('[File]'); Parameters = @{} }
    'template rm'          = @{ Usage = 'lv template rm <file>'; Positional = @('File'); Parameters = @{} }
    run                    = @{ Usage = 'lv run [<VARIABLE>=<name> ...] [-Stdin <name>] <command> [<arguments> ...]'; Positional = @(); Parameters = @{ Stdin = 'value' }; PassThrough = $true }
    'ssh-key list'         = @{ Usage = 'lv ssh-key [list]'; Positional = @(); Parameters = @{} }
    'ssh-key new'          = @{ Usage = 'lv ssh-key new <name> [-HostName <user@host>,...] [-Shared] [-Type ed25519|rsa|ecdsa] [-Comment <text>] [-Overwrite] [-Replace]'; Positional = @('Name'); Parameters = @{ HostName = 'list'; Shared = 'switch'; Type = 'value'; Comment = 'value'; Overwrite = 'switch'; Replace = 'switch' } }
    'ssh-key hosts'        = @{ Usage = 'lv ssh-key hosts <name> [<user@host> ...]   (none clears them)'; Positional = @('Name', '[HostName...]'); Parameters = @{} }
    'ssh-key path'         = @{ Usage = 'lv ssh-key path <name>'; Positional = @('Name'); Parameters = @{} }
    'ssh-key tools'        = @{ Usage = 'lv ssh-key tools [<dir> | -Default]'; Positional = @('[Directory]'); Parameters = @{ Default = 'switch' } }
    'ssh-key include'      = @{ Usage = 'lv ssh-key include on|off'; Positional = @('State'); Parameters = @{} }
    'ssh-key clean'        = @{ Usage = 'lv ssh-key clean'; Positional = @(); Parameters = @{} }
    ssh                    = @{ Usage = 'lv ssh <name> [<user@host>] [<ssh arguments>]   (the host: a word with @, or one of the key''s hosts)'; Positional = @('Name'); Parameters = @{ Target = 'value' }; PassThrough = $true }
    scp                    = @{ Usage = 'lv scp <name> <scp arguments>'; Positional = @('Name'); Parameters = @{}; PassThrough = $true }
    'user-secrets list'    = @{ Usage = 'lv user-secrets [list] [<id|project>]'; Positional = @('[Target]'); Parameters = @{} }
    'user-secrets set'     = @{ Usage = 'lv user-secrets set <id|project> <key> [-Entry <name[#part]>] [-Personal]'; Positional = @('Target', 'Key'); Parameters = @{ Entry = 'value'; Personal = 'switch' } }
    'user-secrets get'     = @{ Usage = 'lv user-secrets get <id|project> <key> [-Show]'; Positional = @('Target', 'Key'); Parameters = @{ Show = 'switch' } }
    'user-secrets pick'    = @{ Usage = 'lv user-secrets pick'; Positional = @(); Parameters = @{} }
    'user-secrets untrack' = @{ Usage = 'lv user-secrets untrack <id|project> <key>'; Positional = @('Target', 'Key'); Parameters = @{} }
    'user-secrets load'    = @{ Usage = 'lv user-secrets load [<id|project>] [-Overwrite]'; Positional = @('[Target]'); Parameters = @{ Overwrite = 'switch' } }
    'user-secrets clear'   = @{ Usage = 'lv user-secrets clear [<id|project>]'; Positional = @('[Target]'); Parameters = @{} }
    update                 = @{ Usage = 'lv update [<tag>] [-Downgrade]'; Positional = @('[Tag]'); Parameters = @{ Downgrade = 'switch' } }
}
# Default subcommand when the word after the group isn't one of its subcommands.
$script:LvGroups = @{ store = 'store'; template = 'template'; 'ssh-key' = 'ssh-key list'; 'user-secrets' = 'user-secrets list' }
$script:LvEntryCommands = @('get', 'clip', 'rm', 'mark', 'set', 'restore', 'reset')
$script:LvTargetPattern = '[\\/]|\.kdbx$|^[A-Za-z][A-Za-z0-9]+:'
$script:LvOldOptions = @{
    path = '-Path'; file = '-FilePath'; replace = '-Replace'; 'replace-all' = '-ReplaceAll'; store = '-Store'; 'unlock-with' = '-UnlockWith'
    keyfile = '-KeyFile'; note = '-Note'; capture = '-Capture'; host = '-HostName'; type = '-Type'; comment = '-Comment'; stdin = '-Stdin'
    ref = '-Reference'; 'ref-store' = '-ReferenceStore'; entry = '-Entry'; overwrite = '-Overwrite'; downgrade = '-Downgrade'; default = '-Default'
    local = '-Local'; hello = '-Hello'; show = '-Show'; credential = '-Credential'; project = '-Project'; mirror = '-Mirror'
    'no-mirror' = '-Mirror:$false'; personal = '-Personal'; 'no-personal' = '-Personal:$false'; 'no-unlock' = '-NoUnlock'; accept = '-AcceptChanges'
    version = '-Version'; help = 'lv help'; shared = '-Shared'; source = '-Source'; 'no-source' = '-Source:$false'; 'no-ask' = '-NoPrompt'
}

function Get-LocalVaultRelease {
    $manifest = Import-PowerShellDataFile -LiteralPath (Join-Path $PSScriptRoot 'LocalVault.psd1')
    $prerelease = $manifest.PrivateData.PSData['Prerelease']
    if ($prerelease) { "$($manifest.ModuleVersion)-$prerelease" } else { $manifest.ModuleVersion }
}

function Get-LvParameterSet {
    param([AllowNull()][string]$Key)

    $set = @{ Path = 'value' }
    if (-not $Key) { $set.Version = 'switch'; return $set }
    if (-not $script:LvCommands.Contains($Key)) { return $set }
    $spec = $script:LvCommands[$Key]
    foreach ($name in $spec.Parameters.Keys) { $set[$name] = $spec.Parameters[$name] }
    if (-not $spec['PassThrough']) {
        foreach ($slot in $spec.Positional) {
            $name = $slot.Trim('[', ']').TrimEnd('.')
            $set[$name] = if ($slot.EndsWith('...') -or $slot.EndsWith('...]')) { 'list' } else { 'value' }
        }
    }
    $set
}

function Resolve-LvParameterName {
    param([string]$Typed, [string[]]$Names, [string]$Command, [switch]$Exact)

    $exactName = @($Names | Where-Object { $_ -eq $Typed })
    if ($exactName.Count -eq 1) { return $exactName[0] }
    if ($Exact) { return $null }
    $prefixed = @($Names | Where-Object { $_ -like "$Typed*" } | Sort-Object)
    if ($prefixed.Count -gt 1) { throw "lv $($Command): -$Typed is ambiguous: $(($prefixed | ForEach-Object { "-$_" }) -join ', ')." }
    if ($prefixed.Count -eq 1) { return $prefixed[0] }
    $null
}

function Resolve-LvCommandKey {
    param([string]$Word, [AllowNull()][object]$Next)

    $word = $Word.ToLowerInvariant()
    if (-not $script:LvGroups.ContainsKey($word)) { return @{ Key = $word; ConsumedNext = $false } }
    if ($Next -is [string] -and $script:LvCommands.Contains("$word $($Next.ToLowerInvariant())")) {
        return @{ Key = "$word $($Next.ToLowerInvariant())"; ConsumedNext = $true }
    }
    @{ Key = $script:LvGroups[$word]; ConsumedNext = $false }
}

function Get-LocalVaultCompletion {
    param([string]$WordToComplete, [System.Management.Automation.Language.CommandAst]$CommandAst)

    $typed = @($CommandAst.CommandElements | Select-Object -Skip 1 | ForEach-Object { $_.Extent.Text.Trim("'", '"') })
    if ($WordToComplete -ne '' -and $typed.Count -gt 0 -and $typed[-1] -eq $WordToComplete) {
        $typed = @($typed | Select-Object -SkipLast 1)
    }

    $key = $null
    $group = $null
    $words = [System.Collections.Generic.List[string]]::new()
    $vaultPath = $null
    $pending = $null
    for ($i = 0; $i -lt $typed.Count; $i++) {
        $pending = $null
        if ($typed[$i] -match '^-([A-Za-z][A-Za-z0-9]*):?$') {
            $set = Get-LvParameterSet $key
            $name = try { Resolve-LvParameterName -Typed $Matches[1] -Names @($set.Keys) -Command $key } catch { $null }
            if ($name -and $set[$name] -ne 'switch') {
                if ($i + 1 -lt $typed.Count) {
                    if ($name -eq 'Path') { $vaultPath = $typed[$i + 1] }
                    $i++
                }
                else { $pending = $name }
            }
        }
        elseif ($null -eq $key) {
            $next = if ($i + 1 -lt $typed.Count) { $typed[$i + 1] } else { $null }
            $resolved = Resolve-LvCommandKey -Word $typed[$i] -Next $next
            $key = $resolved.Key
            if ($script:LvGroups.ContainsKey($typed[$i].ToLowerInvariant())) { $group = $typed[$i].ToLowerInvariant() }
            if ($resolved.ConsumedNext) { $i++; $group = $null }
        }
        else { $words.Add($typed[$i]) }
    }

    $here = (Get-Location -PSProvider FileSystem).ProviderPath
    $candidates = @(
        if ($pending -in 'Store', 'UnlockWith', 'ReferenceStore' -or ($key -in 'store rm', 'store use', 'store set', 'store copy' -and $words.Count -eq 0 -and -not $WordToComplete.StartsWith('-'))) {
            $vault = if ($vaultPath) { @{ Path = $vaultPath } } else { @{} }
            Get-VaultStore @vault -ErrorAction SilentlyContinue | ForEach-Object Name | Select-Object -Unique
        }
        elseif ($pending) {
        }
        elseif ($WordToComplete.StartsWith('-')) {
            if ($null -eq $key -or $script:LvCommands.Contains($key)) {
                (Get-LvParameterSet $key).Keys | Sort-Object | ForEach-Object { "-$_" }
            }
        }
        elseif ($null -eq $key) {
            $script:LvCommands.Keys | ForEach-Object { ($_ -split ' ')[0] } | Where-Object { $_ -ne 'help' } | Select-Object -Unique | Sort-Object
        }
        elseif ($group -and $words.Count -eq 0) {
            $script:LvCommands.Keys | Where-Object { $_ -like "$group *" } | ForEach-Object { ($_ -split ' ', 2)[1] }
        }
        elseif ($key -eq 'mark' -and $words.Count -eq 1) {
            'shared', 'local', 'hello', 'nohello'
        }
        elseif ($key -eq 'ssh-key include' -and $words.Count -eq 0) {
            'on', 'off'
        }
        elseif (($key -in 'ssh-key hosts', 'ssh-key path', 'ssh-key new', 'ssh', 'scp') -and $words.Count -eq 0) {
            if ($key -ne 'ssh-key new') { [LocalVault.PowerShell.LocalVaultCompletion]::SshKeyNames($vaultPath, $here) }
        }
        elseif ($script:LvEntryCommands -contains $key -and ($words.Count -eq 0 -or $key -in 'restore', 'reset')) {
            [LocalVault.PowerShell.LocalVaultCompletion]::EntryNames($vaultPath, $here, $key -eq 'restore')
        }
    )

    $candidates | Where-Object { $_ -like "$WordToComplete*" } | ForEach-Object {
        $type = if ($_.StartsWith('-')) { 'ParameterName' } else { 'ParameterValue' }
        [System.Management.Automation.CompletionResult]::new($_, $_, $type, $_)
    }
}

function Invoke-LocalVault {
    # .ExternalHelp LocalVault.PowerShell.dll-Help.xml
    [Alias('lv')]
    param()

    # A plain function: PowerShell binds no parameter of its own, so every -Name word reaches lv.
    # lv @('list', '-Path', $v) passes the whole command line as one array.
    $given = if ($args.Count -eq 1 -and $args[0] -is [array]) { $args[0] } else { $args }
    $tokens = @($given | Where-Object { $null -ne $_ })
    $bound = @{}
    $words = [System.Collections.Generic.List[object]]::new()
    $passThrough = [System.Collections.Generic.List[object]]::new()
    $oldOptions = [System.Collections.Generic.List[string]]::new()
    $Command = $null
    $vault = @{}

    $usage = @'
lv <command> [<words>] [-Path <vault>]      lv -Version: the installed release
Parameters as in PowerShell: -Name, any unique prefix, switches (-Mirror, -Mirror:$false), lists (-Replace a,b)

Entries
  start                                where this project stands, and what to run next
  init                                 adds the vaults and the files written from templates to .gitignore
  list [<scope>]                       names, kind, store state, age; never a value
  get <name> [-Show]                   prints a value into a pipe; -Show prints it on screen
  clip <name>                          copies a value to the clipboard for 20 s
  set <name> [-FilePath <path>] [-Local]   asks for the value (or reads redirected input); -FilePath carries a file
  set <name> -Credential               asks for a user name, password, TOTP and URLs, kept as one entry
  set <name> -Reference <group/title>  takes the value from a KeePass entry outside LocalVault's groups;
    [-ReferenceStore <store>]          lv save writes where it comes from, never the value. Searched in
                                       -ReferenceStore, else your personal store (personal values) or the
                                       vault's store (shared values)
  <name>#user #totp #url #url2 ...     one part of a credential, for get, clip, set, env, run and templates;
                                       #totp is the current code
  require <name> [-Note <text>]        declares a personal value each person sets; the store gets the declaration only
  require <name> -Credential           the same, for a user name and a password
  require <name> -StoredPath <path>    the same, for a file such as an SSH key; ~/ is your profile
  mark <name> shared|local             sends the entry to the store, or keeps it on this PC only
  mark <name> hello|nohello            reads need Windows Hello (face, fingerprint or PIN), or DPAPI only
  set|require <name> ... -Hello        the same, when the value is set
  hello                                Windows Hello on this PC: key, TPM or software, entries not readable here
  rm <name>                            removes an entry
  reset <name> ... [-NoPrompt]         drops your personal value here and, at lv save, from your personal store;
                                       asks for a new one unless -NoPrompt
  env <VARIABLE>=<name> ...            sets environment variables in this window
  restore [<name> ...] [-Overwrite]    writes file entries back to disk
  clear                                deletes the vault

Moving entries
  save | load                          every vault of the repository to / from its store, one password per store
  export <file> [-Overwrite]           writes a passphrase-sealed export
  import <file> [-Replace a,b] [-ReplaceAll] [-KeyFile <file>]

Stores
  store                                each vault's store, and what this PC still needs
  store list [<name>]                  stores of this PC and of the project
  store add <target> [-Name <name>] [-KeyFile <file>] [-Default] [-UnlockWith <store>] [-Mirror] [-Personal]
                                       a store of this PC; -Mirror: every save on this PC also goes there;
                                       -Personal: keeps your own values of required entries
  store add [<target>] -Name <name> -Project [-Default] [-Mirror] [-Source]
                                       a store of the project; a name alone refers to each PC's store of that name;
                                       -Source: shared entries may take their values from it with -Reference
  store set <name> [-Mirror[:$false]] [-UnlockWith <store> | -NoUnlock] [-Default] [-Personal[:$false]] [-Project] [-Source[:$false]]
                                       changes the settings given
  store rm <name> [-Project]           forgets a store
  store use <name>                     the project's primary store: lv load reads it (-Path: one vault only)
  store diff                           compares a vault with its store
  store copy <target | store name>     one-off copy of the shared entries to another store

Files that need secrets
  template                             each templated file: written or not, up to date or not
  template add <file> [-Capture KEY,...]   turns a file (or its .example) into <file>.template, secrets as {{name}}
  template update [<file>] [-AcceptChanges]   takes your edits of the real file into its template
  template render [<file>]             writes the real files from their templates and the vault
  template clear [<file>]              deletes the written files
  template rm <file>                   stops managing a file
  run [VAR=name ...] <command>         sets the variables, writes the templated files, runs, then deletes them
  run -Stdin <name> <program>          also sends the value to the program's standard input, never as text;
                                       lv's parameters come before the command, by full name

SSH keys (for SSH to hosts; files in ~/.ssh/<repository>/)
  ssh-key                              the repository's keys: personal or shared, fingerprint, hosts
  ssh-key new <name> [-HostName <user@host>,...] [-Shared] [-Type ed25519|rsa|ecdsa]
                                       creates a key pair: personal (your own, in your personal store) or
                                       -Shared (one key for the team, in the project's store);
                                       -Replace replaces an existing key, the old one kept as fallback
  ssh-key hosts <name> [<user@host> ...]   the hosts a key is for; none clears them
  ssh-key path <name>                  the key file, for tools that take -i
  ssh-key tools [<dir> | -Default]     the OpenSSH folder in use; <dir> replaces Windows OpenSSH on this PC
  ssh-key clean                        deletes the keys of clones that no longer exist, after one confirmation
  ssh-key include on|off               adds or removes 'Include ~/.ssh/*/lv.config' in ~/.ssh/config, so other
                                       OpenSSH tools use the keys for their hosts
  ssh <name> [<user@host>] [<args>]    connects with the key; the host may be left out when the key has one.
                                       The second word is the host when it holds @ or is one of the key's hosts;
                                       the other words go to ssh
  scp <name> <scp arguments>           copies files with the key

.NET user secrets (secrets.json of each UserSecretsId; <id|project>: the id, or a project's name)
  user-secrets [<id|project>]          each id, its projects, tracked keys and their state, untracked keys
  user-secrets pick                    choose an id, then a key already in its secrets.json or a new one
  user-secrets set <id|project> <key>  asks for the value, keeps it in the vault, tracks it, writes it;
    [-Entry <name[#part]>] [-Personal]   -Entry tracks an existing entry; -Personal: each person's own
  user-secrets get <id|project> <key> [-Show]   the tracked value, as lv get
  user-secrets untrack <id|project> <key>   stops tracking a key; its vault entry stays
  user-secrets load [<id|project>] [-Overwrite]   writes the tracked values; lv load does it too
  user-secrets clear [<id|project>]    removes the tracked values that still match the vault

  update [<tag>] [-Downgrade]          installs the newest LocalVault release, or the given tag

Details: Get-Help Invoke-LocalVault -Full

'@

    $commands = $script:LvCommands

    $fail = {
        param([string]$Message)
        $line = if ($Command -and $commands.Contains($Command)) { "`nUsage: $($commands[$Command].Usage)" } else { '' }
        throw "$Message$line"
    }

    $location = {
        [LocalVault.Engine.VaultLocation]::Resolve(
            $bound['Path'], $env:LOCALVAULT_PATH, (Get-Location -PSProvider FileSystem).ProviderPath)
    }

    try {
        $spec = $null
        $passing = $false
        $badParameter = $null
        $named = @{}
        for ($i = 0; $i -lt $tokens.Count; $i++) {
            $token = $tokens[$i]
            if ($passing) { $passThrough.Add($token); continue }
            $text = if ($token -is [string]) { $token } else { $null }
            if ($null -ne $text -and $text -eq '--' -and $spec -and $spec['PassThrough']) { $passing = $true; continue }
            if ($null -ne $text -and $text -match '^--(.*)$') {
                $old = $Matches[1].ToLowerInvariant()
                $new = if ($old -eq 'file' -and $Command -eq 'require') { '-StoredPath' } elseif ($script:LvOldOptions.ContainsKey($old)) { $script:LvOldOptions[$old] } else { $null }
                $oldOptions.Add($(if ($new) { "$text is now $new" } else { "$text is not a parameter" }))
                continue
            }
            if ($null -ne $text -and $text -match '^-([A-Za-z][A-Za-z0-9]*)(:?)$') {
                $set = Get-LvParameterSet $Command
                $isPassThrough = $spec -and $spec['PassThrough']
                $name = Resolve-LvParameterName -Typed $Matches[1] -Names @($set.Keys) -Command $(if ($Command) { $Command } else { '' }) -Exact:([bool]$isPassThrough)
                if (-not $name) {
                    if ($isPassThrough) { $passing = $true; $passThrough.Add($token); continue }
                    if ($null -eq $badParameter) { $badParameter = $Matches[1] }
                    continue
                }
                if ($bound.ContainsKey($name) -or $named.ContainsKey($name)) { & $fail "-$name is given more than once." }
                $colon = $Matches[2] -eq ':'
                if ($set[$name] -eq 'switch') {
                    $bound[$name] = if ($colon -and $i + 1 -lt $tokens.Count) { [bool]$tokens[++$i] } else { $true }
                    continue
                }
                if ($i + 1 -ge $tokens.Count) { & $fail "-$name needs a value." }
                $value = $tokens[++$i]
                if ($set[$name] -eq 'list') {
                    $value = @(foreach ($item in @($value)) { "$item" -split ',' | Where-Object { $_ -ne '' } })
                }
                elseif ($value -is [array]) { & $fail "-$name takes one value." }
                else { $value = "$value" }
                if ($spec -and ($spec.Positional | Where-Object { $_.Trim('[', ']').TrimEnd('.') -eq $name })) { $named[$name] = $value }
                else { $bound[$name] = $value }
                continue
            }
            if ($null -eq $Command) {
                $next = if ($i + 1 -lt $tokens.Count) { $tokens[$i + 1] } else { $null }
                $resolved = Resolve-LvCommandKey -Word "$token" -Next $next
                $Command = $resolved.Key
                if (-not $commands.Contains($Command)) {
                    throw "Unknown command '$token'. Commands: start init list get clip set require mark rm reset env restore clear save load export import store template run ssh-key ssh scp user-secrets hello update. Run lv alone for details."
                }
                if ($resolved.ConsumedNext) { $i++ }
                elseif ($script:LvGroups.ContainsKey("$token".ToLowerInvariant()) -and $next -is [string] -and $next -notmatch '^-' -and $Command -notlike 'user-secrets*') {
                    $group = "$token".ToLowerInvariant()
                    $Command = $null
                    throw "Unknown $group command '$next'.`nUsage: $((@($commands.Keys | Where-Object { $_ -eq $group -or $_ -like "$group *" }) | ForEach-Object { $commands[$_].Usage }) -join "`n       ")"
                }
                $spec = $commands[$Command]
                continue
            }
            if ($spec['PassThrough']) {
                if ($Command -eq 'run') {
                    if ($null -ne $text -and $text -match '^[A-Za-z_][A-Za-z0-9_]*=') { $words.Add($text) }
                    else { $passing = $true; $passThrough.Add($token) }
                }
                elseif ($words.Count -lt $spec.Positional.Count) { $words.Add("$token") }
                else { $passing = $true; $passThrough.Add($token) }
                continue
            }
            foreach ($item in @($token)) { $words.Add("$item") }
        }

        if ($oldOptions.Count -gt 0) {
            & $fail "lv takes PowerShell parameters: $($oldOptions -join '; ')."
        }
        if ($bound['Version'] -and $null -eq $Command) {
            "LocalVault $(Get-LocalVaultRelease)"
            Set-Variable -Name LASTEXITCODE -Scope Global -Value 0
            return
        }
        if ($null -eq $Command) { $Command = 'help'; $spec = $commands.help }
        if ($null -ne $badParameter) { & $fail "lv $Command doesn't take -$badParameter." }

        $p = @{}
        $remaining = [System.Collections.Generic.Queue[object]]::new([object[]]$words.ToArray())
        foreach ($slot in $(if ($spec['PassThrough']) { @() } else { $spec.Positional })) {
            $name = $slot.Trim('[', ']').TrimEnd('.')
            $isList = $slot.EndsWith('...') -or $slot.EndsWith('...]')
            if ($named.ContainsKey($name)) { $p[$name] = $named[$name] }
            elseif ($isList) {
                if ($remaining.Count -gt 0) { $p[$name] = @($remaining.ToArray()); $remaining.Clear() }
            }
            elseif ($remaining.Count -gt 0) { $p[$name] = $remaining.Dequeue() }
            if (-not $slot.StartsWith('[') -and -not $p.ContainsKey($name)) { & $fail "lv $Command needs more words." }
        }
        if ($spec['PassThrough']) {
            for ($n = 0; $n -lt $spec.Positional.Count; $n++) {
                if ($words.Count -le $n) { & $fail "lv $Command needs more words." }
                $p[$spec.Positional[$n]] = $words[$n]
            }
        }
        elseif ($remaining.Count -gt 0) {
            & $fail "Too many words for lv $($Command): $($words.Count). Nothing was done. lv asks for secret values; they never go on the command line."
        }

        if ($bound.ContainsKey('Path')) { $vault.Path = $bound.Path }
        $store = if ($bound.ContainsKey('Store')) { @{ Store = $bound.Store } } else { @{} }
        $replace = if ($bound.ContainsKey('Replace')) { @{ Replace = $bound.Replace } } else { @{} }
        $pick = {
            param([string[]]$Names)
            $selected = @{}
            foreach ($name in $Names) { if ($bound.ContainsKey($name)) { $selected[$name] = $bound[$name] } }
            $selected
        }

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
                    "Vault: none. This folder isn't in a git repository; use -Path or `$env:$([LocalVault.Engine.VaultLocation]::EnvironmentVariable)."
                }
            }
            'start' {
                try { $resolved = & $location }
                catch [LocalVault.Core.VaultException] {
                    'This folder is not in a git repository.'
                    '  cd <project folder>; lv start      or      lv start -Path <vault file>'
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
                    try { $ignored = [LocalVault.Engine.GitIgnoreFile]::Missing($resolved).Count -eq 0 } catch { $ignored = $false }
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
                if ($stores.Count -eq 0) { $next.Add('lv store add <path to the team .kdbx> -Name <name> -Default   once per PC; with -Project: for this project, in every clone') }
                foreach ($name in $missing) { $next.Add("lv store add <path to its .kdbx> -Name $name   used by this repository, not defined on this PC") }
                if ($stores.Count -gt 0 -and $unassigned.Count -gt 0) { $next.Add('lv store use <name>   choose the store of this repository''s vaults') }
                elseif ($resolved.GitRoot -and $stores.Count -gt 0 -and -not (Test-Path -LiteralPath (Join-Path $resolved.GitRoot 'localvault.json'))) { $next.Add('lv store use <name>   name the store in localvault.json, then commit it') }
                if (@($stores | Where-Object Scope -EQ 'Computer').Count -gt 1 -and -not $unlocked) { $next.Add('lv store set <name> -UnlockWith <your personal store>   optional: open that store with the password kept in your personal one') }
                if (-not $ignored) { $next.Add('lv init   adds the vaults to .gitignore; before anything else, then commit .gitignore') }
                if (-not $exists -and $legacy) { $next.Add("lv list   reads $([LocalVault.Engine.VaultLocation]::LegacyFileName); the first change moves it into the vault") }
                elseif (-not $exists -and $others.Count -gt 0 -and -not $bound.ContainsKey('Path')) {
                    $next.Add("lv start -Path $($others[0])   the vault of this repository is in a subfolder")
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
                        $next.Add('lv store set <name> -Personal   keep your own values in a store of yours; lv load then fills them in on another PC')
                    }
                    $next.Add('lv list   names of the entries')
                }
                $here = (Get-Location -PSProvider FileSystem).ProviderPath
                $listed = @($vaults | ForEach-Object { if ($resolved.GitRoot) { [IO.Path]::GetFullPath((Join-Path $resolved.GitRoot $_.Vault)) } })
                foreach ($other in $others) {
                    if ($listed -notcontains [IO.Path]::GetFullPath((Join-Path $here $other))) { $next.Add("lv start -Path $other   another vault of this repository") }
                }
                $orphans = @([LocalVault.Engine.SshKeyCleanup]::FindOrphans((Join-Path ([LocalVault.Core.StoredPathRules]::HomeDirectory) '.ssh')))
                if ($orphans.Count -gt 0) { $next.Add("lv ssh-key clean   $($orphans.Count) orphaned SSH keys: their clones were deleted") }
                $next.Add('lv   all commands')
                $suffix = if ($bound.ContainsKey('Path')) { " -Path $($bound.Path)" } else { '' }
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
                $scope = if ($p.ContainsKey('Scope')) { @{ Scope = $p.Scope } } else { @{} }
                $entries = @(Get-VaultEntryList @vault @scope)
                if ($entries.Count -gt 0) {
                    $columns = @('Name', 'Kind') + @(if (@($entries | Where-Object User).Count -gt 0) { 'User' }) + @(if (@($entries | Where-Object Hello).Count -gt 0) { 'Hello' }) + @(if (@($entries | Where-Object UserSecret).Count -gt 0) { 'UserSecret' }) + @('Sync', 'Created', 'Age')
                    $entries | Format-Table $columns -AutoSize
                }
                else {
                    $resolved = & $location
                    $message = if (Test-Path -LiteralPath $resolved.Path -PathType Leaf) {
                        if ($scope.Count -gt 0) { "No entries in scope '$($p.Scope)' of $($resolved.Path)." } else { "No entries in $($resolved.Path)." }
                    }
                    else { "No vault at $($resolved.Path)." }
                    Write-Information -MessageData $message -InformationAction Continue
                }
            }
            'set' {
                if ($bound.ContainsKey('Reference')) {
                    if ($bound.Keys | Where-Object { $_ -in 'FilePath', 'Credential', 'Local', 'Hello' }) { & $fail 'lv set -Reference takes no other parameter than -ReferenceStore.' }
                    $reference = & $pick 'Reference', 'ReferenceStore'
                    Set-VaultEntry @vault -Name $p.Name @reference
                    break
                }
                if ($bound.ContainsKey('ReferenceStore')) { & $fail '-ReferenceStore goes with -Reference.' }
                if ($bound['Credential'] -and $bound.ContainsKey('FilePath')) { & $fail 'Give -FilePath or -Credential, not both.' }
                $shape = & $pick 'FilePath'
                if ($bound['Credential']) { $shape = @{ Credential = $true } }
                $sharing = if ($bound['Local']) { @{ Local = $true } } else { @{} }
                Set-VaultEntry @vault -Name $p.Name @shape @sharing -Hello:([bool]$bound['Hello'])
            }
            'require' {
                $extra = & $pick 'Note', 'StoredPath'
                Set-VaultEntry @vault -Name $p.Name -Required -Credential:([bool]$bound['Credential']) @extra -Hello:([bool]$bound['Hello'])
            }
            'mark' {
                switch ($p.State) {
                    'shared' { Set-VaultEntry @vault -Name $p.Name -Sharing Shared }
                    'local' { Set-VaultEntry @vault -Name $p.Name -Sharing Local }
                    'hello' { Set-VaultEntry @vault -Name $p.Name -Protection Hello }
                    'nohello' { Set-VaultEntry @vault -Name $p.Name -Protection Dpapi }
                    default { & $fail "lv mark takes shared, local, hello or nohello, not '$($p.State)'." }
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
                $plainPart = $p.Name -match '#(user|url\d*)$'
                if (-not ($plainPart -or $bound['Show'] -or [Console]::IsOutputRedirected -or $MyInvocation.PipelinePosition -lt $MyInvocation.PipelineLength)) {
                    & $fail 'lv get prints a value on screen only with -Show. lv clip <name> copies it to the clipboard instead.'
                }
                Get-VaultEntry @vault -Name $p.Name -AsPlainText
            }
            'clip' { Copy-VaultEntry @vault -Name $p.Name }
            'init' {
                $added = @(Initialize-Vault @vault)
                if ($added.Count -gt 0) { $added | Format-Table Line, Path -AutoSize }
            }
            'rm' { Remove-VaultEntry @vault -Name $p.Name }
            'reset' { Reset-VaultEntry @vault -Name ([string[]]$p.Name) -NoPrompt:([bool]$bound['NoPrompt']) }
            'env' {
                $map = @{}
                foreach ($pair in $p.Map) {
                    $variable, $name = $pair -split '=', 2
                    if (-not $name) { & $fail "lv env takes VARIABLE=name pairs, not '$pair'." }
                    $map[$variable] = $name
                }
                Use-VaultEnv @vault -Map $map
            }
            'export' {
                Export-Vault @vault -Destination $p.Destination -Overwrite:([bool]$bound['Overwrite'])
            }
            'import' {
                $keyFile = & $pick 'KeyFile'
                Import-Vault @vault -Source $p.Source @replace @keyFile -ReplaceAll:([bool]$bound['ReplaceAll'])
            }
            'restore' {
                if ($p.ContainsKey('Name')) {
                    Restore-VaultFile @vault -Name ([string[]]$p.Name) -Overwrite:([bool]$bound['Overwrite'])
                }
                else {
                    Restore-VaultFile @vault -All -Overwrite:([bool]$bound['Overwrite'])
                }
            }
            'load' { Import-VaultFromStore @vault @store @replace -ReplaceAll:([bool]$bound['ReplaceAll']) | Out-Null }
            'save' { Export-VaultToStore @vault @store @replace -ReplaceAll:([bool]$bound['ReplaceAll']) | Out-Null }
            'store' {
                $statuses = @(Get-VaultStoreStatus @vault 3>$null)
                $statuses | Format-Table Vault, Store, Mirrors, Scope, UnlockWith, Group, @{ Label = 'To save'; Expression = { $_.Pending + $_.RemovalsPending } } -AutoSize
                foreach ($item in $statuses | Where-Object Problem) { "$($item.Problem)" }
                $stores = @(Get-VaultStore @vault)
                "Stores: $(if ($stores.Count -gt 0) { ($stores | ForEach-Object { "$($_.Name) ($($_.Scope)$(if ($_.Default) { ', default' })$(if ($_.Mirror) { ', mirror' })$(if ($_.Personal) { ', personal' })$(if ($_.UnlockWith) { ", unlocked with $($_.UnlockWith)" }))" }) -join ', ' } else { 'none (lv store add <target> -Name <name>)' })"
            }
            'store list' {
                $name = if ($p.ContainsKey('Name')) { @{ Name = $p.Name } } else { @{} }
                Get-VaultStore @vault @name | Format-Table Name, Scope, Type, Target, KeyFile, Default, UnlockWith, Mirror, Personal, Exists -AutoSize
            }
            'store add' {
                $register = & $pick 'Name', 'KeyFile', 'UnlockWith', 'Default', 'Mirror', 'Personal', 'Project', 'Source'
                if ($p.ContainsKey('Target')) { $register.Target = $p.Target }
                if (-not $register.ContainsKey('Target') -and -not ($register['Project'] -and $register.ContainsKey('Name'))) {
                    & $fail 'lv store add needs a target, e.g. a .kdbx file, or -Name with -Project.'
                }
                Register-VaultStore @vault @register | Format-Table Name, Scope, Type, Target, KeyFile, Default, UnlockWith, Mirror, Personal, Exists -AutoSize
            }
            'store set' {
                $change = & $pick 'Mirror', 'UnlockWith', 'NoUnlock', 'Default', 'Personal', 'Project', 'Source'
                Set-VaultStore @vault -Name $p.Name @change | Format-Table Name, Scope, Type, Target, KeyFile, Default, UnlockWith, Mirror, Personal, Exists -AutoSize
            }
            'store rm' { Unregister-VaultStore @vault -Name $p.Name -Project:([bool]$bound['Project']) }
            'store use' { Set-VaultProjectStore @vault -Name $p.Name }
            'store diff' { Compare-VaultWithStore @vault @store | Format-Table Name, Kind, State -AutoSize }
            'store copy' {
                $target = if (-not $p.ContainsKey('Target')) { @{} } elseif ($p.Target -match $script:LvTargetPattern) { @{ Target = $p.Target } } else { @{ Store = $p.Target } }
                if ($bound.ContainsKey('Store')) {
                    if ($target.Count -gt 0) { & $fail 'Give a target or -Store, not both.' }
                    $target = $store
                }
                if ($target.Count -eq 0) { & $fail 'lv store copy needs a target, e.g. a .kdbx file, or a store name.' }
                $keyFile = & $pick 'KeyFile'
                Copy-VaultToStore @vault @target @keyFile | Out-Null
            }
            'clear' { Clear-Vault @vault }
            'template' {
                $templates = @(Get-VaultTemplate @vault)
                if ($templates.Count -eq 0) { 'No templates in this repository. lv template add <file> starts one.' }
                else {
                    $templates | Format-Table @{ Label = 'File'; Expression = { [IO.Path]::GetRelativePath((Get-Location -PSProvider FileSystem).ProviderPath, $_.Path) } },
                        @{ Label = 'Written'; Expression = { $_.Rendered } },
                        @{ Label = 'Up to date'; Expression = { if ($null -eq $_.UpToDate) { '' } else { $_.UpToDate } } } -AutoSize
                }
            }
            { $_ -like 'template *' } {
                $file = if ($p.ContainsKey('File')) { @{ File = $p.File } } else { @{} }
                switch ($Command) {
                    'template add' {
                        $capture = & $pick 'Capture'
                        Add-VaultTemplate @vault @file @capture | Out-Null
                    }
                    'template update' { Update-VaultTemplate @vault @file -AcceptChanges:([bool]$bound['AcceptChanges']) }
                    'template render' { Write-VaultTemplate @vault @file | Format-Table Path, Action -AutoSize }
                    'template clear' { Clear-VaultTemplate @vault @file | Format-Table Path, Action -AutoSize }
                    'template rm' { Remove-VaultTemplate @vault @file }
                }
            }
            'run' {
                if ($passThrough.Count -eq 0) { & $fail 'lv run needs a command.' }
                $map = @{}
                foreach ($pair in $words) {
                    $variable, $name = $pair -split '=', 2
                    $map[$variable] = $name
                }
                $previous = @{}
                foreach ($variable in $map.Keys) { $previous[$variable] = [Environment]::GetEnvironmentVariable($variable) }
                $resolved = & $location
                $templates = @(if ($resolved.GitRoot) { Get-VaultTemplate @vault })
                try {
                    if ($map.Count -gt 0) { Use-VaultEnv @vault -Map $map 6>$null }
                    if ($templates.Count -gt 0) { Write-VaultTemplate @vault | Out-Null }
                    $commandName = $passThrough[0]
                    $commandArguments = @($passThrough | Select-Object -Skip 1)
                    if ($bound.ContainsKey('Stdin')) {
                        $program = Get-Command -Name $commandName -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
                        if (-not $program) { & $fail "lv run -Stdin runs a program; '$commandName' is not one." }
                        $sendToStdin = [System.Management.Automation.CmdletInfo]::new('Invoke-VaultStdin', [LocalVault.PowerShell.InvokeVaultStdinCommand])
                        $runExit = & $sendToStdin @vault -Name $bound.Stdin -FilePath $program.Source -ArgumentList ([string[]]$commandArguments)
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
            'ssh-key list' {
                $keys = @(Get-VaultSshKey @vault)
                if ($keys.Count -eq 0) { 'No SSH keys in this vault. lv ssh-key new <name> creates one.' }
                else { $keys | Format-Table Name, Sharing, Source, Type, Fingerprint, @{ Label = 'Hosts'; Expression = { $_.Hosts -join ', ' } }, Previous -AutoSize }
            }
            'ssh-key new' {
                $create = & $pick 'HostName', 'Shared', 'Type', 'Comment', 'Overwrite', 'Replace'
                New-VaultSshKey @vault -Name $p.Name @create | Out-Null
            }
            'ssh-key hosts' {
                $hosts = [string[]]@(if ($p.ContainsKey('HostName')) { $p.HostName })
                Set-VaultSshKeyHost @vault -Name $p.Name -HostName $hosts
            }
            'ssh-key path' { Get-VaultSshKeyPath @vault -Name $p.Name }
            'ssh-key tools' {
                if ($p.ContainsKey('Directory') -and $bound['Default']) { & $fail 'lv ssh-key tools takes a folder or -Default, not both.' }
                $choice = if ($p.ContainsKey('Directory')) { @{ Directory = $p.Directory } } elseif ($bound['Default']) { @{ Default = $true } } else { @{} }
                $tools = Set-VaultSshTools @choice
                "OpenSSH: $($tools.Directory)$(if ($tools.IsDefault) { ' (Windows OpenSSH)' } else { " (from $($tools.Settings))" })"
            }
            'ssh-key clean' {
                $removed = @(Remove-VaultSshOrphan)
                if ($removed.Count -eq 0) { 'No orphaned SSH keys.' }
                else { $removed | Format-Table Repository, Key, LastWrite, Action -AutoSize }
            }
            'ssh-key include' {
                if ($p.State -notin 'on', 'off') { & $fail 'lv ssh-key include takes on or off.' }
                $include = Set-VaultSshInclude -Enabled:($p.State -eq 'on')
                $line = [LocalVault.Engine.SshConfigFile]::IncludeLine
                if ($include.Enabled) { "Added '$line' to $($include.ConfigPath)." } else { "Removed '$line' from $($include.ConfigPath)." }
            }
            'ssh' {
                $rest = [string[]]@($passThrough | ForEach-Object { "$_" })
                $target = & $pick 'Target'
                if ($target.Count -eq 0 -and $rest.Count -gt 0) {
                    $hosts = @(Get-VaultSshKey @vault -Name $p.Name -ErrorAction SilentlyContinue | ForEach-Object Hosts)
                    if ($rest[0].Contains('@') -or $hosts -contains $rest[0]) {
                        $target.Target = $rest[0]
                        $rest = [string[]]@($rest | Select-Object -Skip 1)
                    }
                }
                Invoke-VaultSsh @vault @target -Name $p.Name -ArgumentList $rest
                return
            }
            'scp' {
                $rest = [string[]]@($passThrough | ForEach-Object { "$_" })
                if ($rest.Count -eq 0) { & $fail 'lv scp needs the scp arguments: files and host:path.' }
                Invoke-VaultScp @vault -Name $p.Name -ArgumentList $rest
                return
            }
            'user-secrets list' {
                $target = if ($p.ContainsKey('Target')) { @{ Target = $p.Target } } else { @{} }
                $rows = @(Get-VaultUserSecretList @vault @target)
                if ($rows.Count -eq 0) { "No project of this repository has a UserSecretsId. dotnet user-secrets init --project <project> adds one." }
                else { $rows | Format-Table Id, @{ Label = 'Projects'; Expression = { $_.Projects -join ', ' } }, Key, Entry, State -AutoSize }
            }
            'user-secrets set' {
                $source = if ($bound.ContainsKey('Entry')) {
                    if ($bound['Personal']) { & $fail 'Give -Entry or -Personal, not both.' }
                    @{ Entry = $bound.Entry }
                }
                else { @{ Personal = [bool]$bound['Personal'] } }
                Set-VaultUserSecret @vault -Target $p.Target -Key $p.Key @source
            }
            'user-secrets get' {
                if (-not ($bound['Show'] -or [Console]::IsOutputRedirected -or $MyInvocation.PipelinePosition -lt $MyInvocation.PipelineLength)) {
                    & $fail 'lv user-secrets get prints a value on screen only with -Show.'
                }
                Get-VaultUserSecret @vault -Target $p.Target -Key $p.Key -AsPlainText
            }
            'user-secrets pick' {
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
            'user-secrets untrack' { Remove-VaultUserSecret @vault -Target $p.Target -Key $p.Key }
            'user-secrets load' {
                $target = if ($p.ContainsKey('Target')) { @{ Target = $p.Target } } else { @{} }
                Write-VaultUserSecret @vault @target -Overwrite:([bool]$bound['Overwrite'])
            }
            'user-secrets clear' {
                $target = if ($p.ContainsKey('Target')) { @{ Target = $p.Target } } else { @{} }
                Clear-VaultUserSecret @vault @target
            }
            'update' {
                $installer = Join-Path $PSScriptRoot 'install.ps1'
                if (-not (Test-Path -LiteralPath $installer)) {
                    throw "This copy of LocalVault has no installer. Install it once with the line in the README; lv update works after that."
                }
                $release = if ($p.ContainsKey('Tag')) { @{ Version = $p.Tag } } else { @{ Latest = $true } }
                if ($bound['Downgrade']) { $release.Downgrade = $true }
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

Register-ArgumentCompleter -CommandName Invoke-LocalVault, lv -Native -ScriptBlock {
    Get-LocalVaultCompletion -WordToComplete $args[0] -CommandAst $args[1]

}
