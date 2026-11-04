#Requires -Version 7.6
Set-StrictMode -Version Latest

BeforeAll {
    . (Join-Path $PSScriptRoot 'TestSupport.ps1')
    $script:master = 'pester master 1'
    $script:runInChild = {
        param([string]$Command, [string]$Master = $script:master)
        $prefix = "Import-Module '$(Join-Path $env:LOCALVAULT_MODULE_PATH 'LocalVault.psd1')'; "
        $output = $Master | pwsh -NoProfile -NonInteractive -Command ($prefix + "try { $Command } catch { Write-Error `$_; exit 1 }; exit `$LASTEXITCODE") 2>&1
        [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = ($output | Out-String) }
    }
}

Describe 'Shared entries in KeePass' {
    BeforeEach {
        $script:folder = Initialize-TestFolder
        $script:savedLocalAppData = $env:LOCALAPPDATA
        $env:LOCALAPPDATA = Join-Path $script:folder 'appdata'
        $script:database = Join-Path $script:folder 'team.kdbx'
        Initialize-TestKeePass -Path $script:database -Master $script:master
        Register-VaultStore -Name 'team' -Target $script:database | Out-Null
        $script:mine = Join-Path $script:folder 'me/proj/secrets.local.lv'
        $script:theirs = Join-Path $script:folder 'them/proj/secrets.local.lv'
        Set-VaultEntry -Path $mine -Name 'common/db' -Value (ConvertTo-TestSecret 'kp-1')
        Set-VaultEntry -Path $mine -Name 'mine' -Value (ConvertTo-TestSecret 'only here') -Local
    }
    AfterEach {
        $env:LOCALAPPDATA = $script:savedLocalAppData
        Clear-TestFolder $script:folder
    }

    It 'saves shared entries and a teammate loads them' {
        $saved = & $runInChild "Export-VaultToStore -Path '$mine'"
        $saved.ExitCode | Should -Be 0 -Because $saved.Output
        $saved.Output | Should -Match '1 added'
        $saved.Output | Should -Not -Match 'pester master'
        (Get-VaultEntryList -Path $mine | Where-Object Name -EQ 'common/db').Sync | Should -Be 'Saved'

        $loaded = & $runInChild "lv load --path '$theirs'"
        $loaded.ExitCode | Should -Be 0 -Because $loaded.Output

        Get-VaultEntry -Path $theirs -Name 'common/db' -AsPlainText | Should -Be 'kp-1'
        { Get-VaultEntry -Path $theirs -Name 'mine' } | Should -Throw
    }

    It 'takes a teammate change without asking and refuses an unresolved conflict' {
        (& $runInChild "Export-VaultToStore -Path '$mine'").ExitCode | Should -Be 0
        (& $runInChild "Import-VaultFromStore -Path '$theirs'").ExitCode | Should -Be 0
        Set-VaultEntry -Path $theirs -Name 'common/db' -Value (ConvertTo-TestSecret 'kp-2')
        (& $runInChild "Export-VaultToStore -Path '$theirs'").ExitCode | Should -Be 0

        (& $runInChild "Import-VaultFromStore -Path '$mine'").ExitCode | Should -Be 0
        Get-VaultEntry -Path $mine -Name 'common/db' -AsPlainText | Should -Be 'kp-2'

        Set-VaultEntry -Path $mine -Name 'common/db' -Value (ConvertTo-TestSecret 'mine-3')
        Set-VaultEntry -Path $theirs -Name 'common/db' -Value (ConvertTo-TestSecret 'theirs-3')
        (& $runInChild "Export-VaultToStore -Path '$theirs'").ExitCode | Should -Be 0

        $conflict = & $runInChild "lv load --path '$mine'"
        $conflict.ExitCode | Should -Be 2 -Because $conflict.Output
        Get-VaultEntry -Path $mine -Name 'common/db' -AsPlainText | Should -Be 'mine-3'

        $saveConflict = & $runInChild "lv save --path '$mine'"
        $saveConflict.Output | Should -Match 'Not saved'

        (& $runInChild "lv load --path '$mine' --replace common/db").ExitCode | Should -Be 0
        Get-VaultEntry -Path $mine -Name 'common/db' -AsPlainText | Should -Be 'theirs-3'
    }

    It 'removes in KeePass what was removed here' {
        (& $runInChild "Export-VaultToStore -Path '$mine'").ExitCode | Should -Be 0
        Remove-VaultEntry -Path $mine -Name 'common/db' -InformationVariable told
        "$told" | Should -Match 'removes it from the store'

        (& $runInChild "Export-VaultToStore -Path '$mine'").Output | Should -Match '1 removed'
        (& $runInChild "Import-VaultFromStore -Path '$theirs'").ExitCode | Should -Be 0
        { Get-VaultEntry -Path $theirs -Name 'common/db' } | Should -Throw
    }

    It 'marks an entry local or shared without retyping it' {
        $arguments = 'mark', 'common/db', 'local', '--path', $mine
        lv @arguments 6>$null
        (Get-VaultEntryList -Path $mine | Where-Object Name -EQ 'common/db').Sync | Should -Be 'Local'
        (& $runInChild "Export-VaultToStore -Path '$mine'").Output | Should -Match '0 added'

        $arguments = 'mark', 'mine', 'shared', '--path', $mine
        lv @arguments 6>$null
        (& $runInChild "Export-VaultToStore -Path '$mine'").Output | Should -Match '1 added'
        Get-VaultEntry -Path $mine -Name 'mine' -AsPlainText | Should -Be 'only here'
    }

    It 'reports what is not saved without asking for the password' {
        Get-VaultEntryList -Path $mine -WarningVariable warnings -WarningAction SilentlyContinue | Out-Null
        "$warnings" | Should -Match 'lv save'

        $status = Get-VaultStoreStatus -Path $mine
        $status.Group | Should -Be 'LocalVault/proj'
        $status.Store | Should -Be 'team'
        $status.Scope | Should -Be 'Computer'
        $status.Pending | Should -Be 1
        $status.Local | Should -Be 1

        $arguments = 'store', '--path', $mine
        (lv @arguments | Out-String -Width 400) | Should -Match 'team\s+Computer\s+LocalVault/proj\s+1'
    }

    It 'lists the state of each entry against KeePass' {
        (& $runInChild "Export-VaultToStore -Path '$mine'").ExitCode | Should -Be 0
        Set-VaultEntry -Path $mine -Name 'new' -Value (ConvertTo-TestSecret 'n')

        $listed = & $runInChild "Compare-VaultWithStore -Path '$mine' | ForEach-Object { '{0}={1}' -f `$_.Name, `$_.State }"

        $listed.Output | Should -Match 'common/db=Saved'
        $listed.Output | Should -Match 'new=New'
        $listed.Output | Should -Match 'mine=Local'
    }

    It 'copies the shared entries once to another database' {
        (& $runInChild "Export-VaultToStore -Path '$mine'").ExitCode | Should -Be 0
        $other = Join-Path $folder 'other.kdbx'
        Initialize-TestKeePass -Path $other -Master $master

        $copied = & $runInChild "lv store copy '$other' --path '$mine'"
        $copied.ExitCode | Should -Be 0 -Because $copied.Output
        $copied.Output | Should -Match 'Copied 1 shared entries'

        Register-VaultStore -Name 'other' -Target $other | Out-Null
        (& $runInChild "Import-VaultFromStore -Path '$theirs' -Store other").ExitCode | Should -Be 0
        Get-VaultEntry -Path $theirs -Name 'common/db' -AsPlainText | Should -Be 'kp-1'
        (Get-VaultEntryList -Path $mine | Where-Object Name -EQ 'common/db').Sync | Should -Be 'Saved'
        (& $runInChild "Compare-VaultWithStore -Path '$mine' -Store team | ForEach-Object { '{0}={1}' -f `$_.Name, `$_.State }").Output | Should -Match 'common/db=Saved'
    }
    It 'shares a required declaration and keeps each value personal' {
        $arguments = 'require', 'os/password', '--note', 'your own account', '--path', $mine
        lv @arguments 6>$null
        (Get-VaultEntryList -Path $mine 3>$null | Where-Object Name -EQ 'os/password').Sync | Should -Be 'Missing'
        { Get-VaultEntry -Path $mine -Name 'os/password' } | Should -Throw '*required but not set*your own account*'

        Set-VaultEntry -Path $mine -Name 'os/password' -Value (ConvertTo-TestSecret 'mine-os')
        (Get-VaultEntryList -Path $mine 3>$null | Where-Object Name -EQ 'os/password').Sync | Should -Be 'Personal'
        (& $runInChild "Export-VaultToStore -Path '$mine'").ExitCode | Should -Be 0
        (& $runInChild "Import-VaultFromStore -Path '$theirs'").ExitCode | Should -Be 0

        (Get-VaultEntryList -Path $theirs 3>$null | Where-Object Name -EQ 'os/password').Sync | Should -Be 'Missing'
        $arguments = 'start', '--path', $theirs
        (lv @arguments | Out-String) | Should -Match 'lv set os/password .*your own account'
    }

    It 'names the project store and opens it with the password kept in another store' {
        $repository = Join-Path $folder 'repo'
        git init --quiet $repository
        Set-Content -LiteralPath (Join-Path $repository '.gitignore') -Value 'secrets.local.*'
        $vault = Join-Path $repository 'secrets.local.lv'
        Set-VaultEntry -Path $vault -Name 'x' -Value (ConvertTo-TestSecret 'x-1')
        $ring = Join-Path $folder 'ring.kdbx'
        Initialize-TestKeePass -Path $ring -Master 'ring master 1'
        Register-VaultStore -Target $ring | Out-Null
        Register-VaultStore -Name 'team' -Target $database -UnlockWith ring | Out-Null

        $used = & $runInChild "Set-Location '$repository'; lv store use team" "ring master 1`n$master"
        $used.ExitCode | Should -Be 0 -Because $used.Output
        $project = Get-Content -LiteralPath (Join-Path $repository 'localvault.json') -Raw | ConvertFrom-Json
        $project.default.store | Should -Be 'team'

        $saved = & $runInChild "lv save --path '$vault'" "ring master 1`n$master"
        $saved.ExitCode | Should -Be 0 -Because $saved.Output
        $saved.Output | Should -Match "'ring' doesn't hold its password yet"

        $keyring = [LocalVault.Core.KeePassVault]::Open($ring, $null, 'ring master 1')
        try {
            $keyring.RememberAutoOpen('team', $database, $null, $master, $project.default.id)
            $keyring.Save()
        }
        finally { $keyring.Dispose() }
        Set-VaultEntry -Path $vault -Name 'y' -Value (ConvertTo-TestSecret 'y-1')

        $again = & $runInChild "lv save --path '$vault'" 'ring master 1'
        $again.ExitCode | Should -Be 0 -Because $again.Output
        $again.Output | Should -Match "with the password kept in 'ring'"
        $again.Output | Should -Match '1 added'

        $project.default.id = 'ffffffffffffffffffffffffffffffff'
        $project | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $repository 'localvault.json')
        Register-VaultStore -Name 'other' -Target (Join-Path $folder 'team.kdbx') | Out-Null
        $wrong = & $runInChild "lv save --path '$vault'" 'ring master 1'
        $wrong.ExitCode | Should -Not -Be 0
        $wrong.Output | Should -Match 'is another database'
    }

    It 'opens a chain of stores with one password' {
        $ring = Join-Path $folder 'ring.kdbx'
        $personal = Join-Path $folder 'personal.kdbx'
        Initialize-TestKeePass -Path $ring -Master 'ring master 1'
        Initialize-TestKeePass -Path $personal -Master 'personal master 1'
        Register-VaultStore -Target $personal | Out-Null
        Register-VaultStore -Target $ring -UnlockWith personal | Out-Null
        Register-VaultStore -Name 'team' -Target $database -UnlockWith ring | Out-Null
        foreach ($pair in @(@($personal, 'personal master 1', $ring, 'ring master 1'), @($ring, 'ring master 1', $database, $master))) {
            $holder = [LocalVault.Core.KeePassVault]::Open($pair[0], $null, $pair[1])
            try {
                $target = [LocalVault.Core.KeePassVault]::Open($pair[2], $null, $pair[3])
                try { $id = $target.DatabaseId } finally { $target.Dispose() }
                $holder.RememberAutoOpen((Split-Path -LeafBase $pair[2]), $pair[2], $null, $pair[3], $id)
                $holder.Save()
            }
            finally { $holder.Dispose() }
        }

        $saved = & $runInChild "Export-VaultToStore -Path '$mine' -Store team" 'personal master 1'

        $saved.ExitCode | Should -Be 0 -Because $saved.Output
        $saved.Output | Should -Match "Opening store 'personal'"
        $saved.Output | Should -Match "Opening store 'ring' .*kept in 'personal'"
        $saved.Output | Should -Match "Opening store 'team' .*kept in 'ring'"
        $saved.Output | Should -Match '1 added'
        { Register-VaultStore -Target $personal -UnlockWith team } | Should -Throw '*already unlocks it*'
        { Unregister-VaultStore -Name ring } | Should -Throw '*unlocks team*'
    }

    It 'fails cleanly with a wrong master password' {
        $result = & $runInChild "Export-VaultToStore -Path '$mine'" 'wrong master'

        $result.ExitCode | Should -Not -Be 0
        $result.Output | Should -Match 'does not open'
        $result.Output | Should -Not -Match 'wrong master'
        (Get-VaultEntryList -Path $mine | Where-Object Name -EQ 'common/db').Sync | Should -Be 'New'
    }
}

Describe 'Stores of the PC and of the project' {
    BeforeEach {
        $script:folder = Initialize-TestFolder
        $script:savedLocalAppData = $env:LOCALAPPDATA
        $env:LOCALAPPDATA = Join-Path $script:folder 'appdata'
        $script:repository = Join-Path $script:folder 'repo'
        git init --quiet $script:repository
        Set-Content -LiteralPath (Join-Path $script:repository '.gitignore') -Value 'secrets.local.*'
        $script:projectDb = Join-Path $script:folder 'project.kdbx'
        $script:personalDb = Join-Path $script:folder 'personal.kdbx'
        Initialize-TestKeePass -Path $script:projectDb -Master $script:master
        Initialize-TestKeePass -Path $script:personalDb -Master $script:master
        $script:infra = Join-Path $script:repository 'infra/secrets.local.lv'
        $script:migration = Join-Path $script:repository 'migration/secrets.local.lv'
        Set-VaultEntry -Path $script:infra -Name 'infra-key' -Value (ConvertTo-TestSecret 'i-1') 6>$null
        Set-VaultEntry -Path $script:migration -Name 'me@example.com' -Value (ConvertTo-TestSecret 'm-1') 6>$null
    }
    AfterEach {
        $env:LOCALAPPDATA = $script:savedLocalAppData
        Clear-TestFolder $script:folder
    }

    It 'saves each vault to its own store, opening each store once' {
        Push-Location $repository
        try {
            $arguments = 'store', 'add', 'proj', $projectDb, '--project', '--default'
            lv @arguments 6>$null | Out-Null
            $arguments = 'store', 'add', 'mine', $personalDb
            lv @arguments | Out-Null
        }
        finally { Pop-Location }
        $project = Get-Content -LiteralPath (Join-Path $repository 'localvault.json') -Raw | ConvertFrom-Json
        $project.schema | Should -Be 2
        $project.stores[0].name | Should -Be 'proj'
        $project.default.store | Should -Be 'proj'

        $used = & $runInChild "lv store use mine --path '$migration'"
        $used.ExitCode | Should -Be 0 -Because $used.Output
        git -C $repository remote add origin 'https://someone:pat@dev.example/org/_git/repo'

        $saved = & $runInChild "Set-Location '$repository'; lv save" "$master`n$master"
        $saved.ExitCode | Should -Be 0 -Because $saved.Output
        ([regex]::Matches($saved.Output, 'Opening store')).Count | Should -Be 2
        $saved.Output | Should -Match "infra/secrets.local.lv -> store 'proj'.*1 added"
        $saved.Output | Should -Match "migration/secrets.local.lv -> store 'mine'.*1 added"

        $raw = [KeePassLib.PwDatabase]::new()
        $key = [KeePassLib.Keys.CompositeKey]::new(); $key.AddUserKey([KeePassLib.Keys.KcpPassword]::new($master))
        $raw.Open([KeePassLib.Serialization.IOConnectionInfo]::FromPath($personalDb), $key, $null)
        try {
            $root = $raw.RootGroup.FindCreateGroup('LocalVault', $false)
            @($root.Entries).ForEach({ $_.Strings.ReadSafe('Title') }) | Should -Be 'Install instructions for localVault'
            $group = $root.FindCreateGroup('repo', $false)
            $group.FindCreateGroup('infra', $false) | Should -BeNullOrEmpty
            @($group.FindCreateGroup('migration', $false).Entries).Count | Should -Be 1
            $origin = @($group.Entries)
            $origin.Count | Should -Be 1
            $origin[0].Strings.ReadSafe('URL') | Should -Be 'https://dev.example/org/_git/repo'
            $origin[0].Strings.ReadSafe('Notes') | Should -Match 'git clone https://dev.example/org/_git/repo repo'
        }
        finally { $raw.Close() }
    }

    It 'saves to the primary and every mirror, opening and writing each file once' {
        $offsite = Join-Path $folder 'offsite.kdbx'
        Initialize-TestKeePass -Path $offsite -Master $master
        Register-VaultStore -Name 'mine' -Target $personalDb -Mirror | Out-Null
        Push-Location $repository
        try {
            $arguments = 'store', 'add', 'proj', $projectDb, '--project', '--default'
            lv @arguments 6>$null | Out-Null
            $arguments = 'store', 'add', 'offsite', $offsite, '--project', '--mirror'
            lv @arguments 6>$null | Out-Null
        }
        finally { Pop-Location }
        (& $runInChild "lv store use mine --path '$migration'").ExitCode | Should -Be 0
        $before = [IO.File]::ReadAllBytes($infra)

        $saved = & $runInChild "Set-Location '$repository'; lv save" "$master`n$master`n$master"

        $saved.ExitCode | Should -Be 0 -Because $saved.Output
        ([regex]::Matches($saved.Output, 'Opening store')).Count | Should -Be 3
        $saved.Output | Should -Match "infra/secrets.local.lv -> store 'proj'.*1 added"
        $saved.Output | Should -Match "infra/secrets.local.lv -> mirror 'offsite'.*1 added"
        $saved.Output | Should -Match "infra/secrets.local.lv -> mirror 'mine'.*1 added"
        $saved.Output | Should -Match "migration/secrets.local.lv -> store 'mine'.*1 added"
        $saved.Output | Should -Match "migration/secrets.local.lv -> mirror 'offsite'.*1 added"
        [IO.File]::ReadAllBytes("$infra.bak") | Should -Be $before
        (Get-VaultEntryList -Path $infra 3>$null).Sync | Should -Be 'Saved'

        Set-VaultEntry -Path $infra -Name 'infra-2' -Value (ConvertTo-TestSecret 'i-2') 6>$null
        Move-Item -LiteralPath $offsite -Destination "$offsite.away"
        $partial = & $runInChild "Set-Location '$repository'; lv save --path '$infra'" "$master`n$master"
        $partial.ExitCode | Should -Be 0 -Because $partial.Output
        $partial.Output | Should -Match "mirror 'offsite' not saved"
        $partial.Output | Should -Match "-> store 'proj'.*1 added"
        Move-Item -LiteralPath "$offsite.away" -Destination $offsite

        Remove-Item -LiteralPath $infra
        $loaded = & $runInChild "Set-Location '$repository'; lv load" "$master`n$master"
        $loaded.ExitCode | Should -Be 0 -Because $loaded.Output
        $loaded.Output | Should -Not -Match 'offsite'
        Get-VaultEntry -Path $infra -Name 'infra-2' -AsPlainText | Should -Be 'i-2'
    }

    It 'a clone without the personal store loads the project store and skips the other vault' {
        Push-Location $repository
        try {
            $arguments = 'store', 'add', 'proj', $projectDb, '--project', '--default'
            lv @arguments 6>$null | Out-Null
            Register-VaultStore -Name 'mine' -Target $personalDb | Out-Null
        }
        finally { Pop-Location }
        (& $runInChild "lv store use mine --path '$migration'").ExitCode | Should -Be 0
        (& $runInChild "Set-Location '$repository'; lv save" "$master`n$master").ExitCode | Should -Be 0

        Unregister-VaultStore -Name 'mine'
        Remove-Item -LiteralPath $infra, $migration

        $loaded = & $runInChild "Set-Location '$repository'; lv load"
        $loaded.ExitCode | Should -Be 0 -Because $loaded.Output
        $loaded.Output | Should -Match "migration/secrets.local.lv uses store 'mine', which isn't defined on this PC"
        Get-VaultEntry -Path $infra -Name 'infra-key' -AsPlainText | Should -Be 'i-1'
        Test-Path -LiteralPath $migration | Should -BeFalse

        $status = @(Get-VaultStoreStatus -Path $migration)
        $status[0].Store | Should -Be 'mine'
        $status[0].Problem | Should -Match "isn't defined on this PC"
        $start = & $runInChild "Set-Location '$repository'; lv start"
        $start.Output | Should -Match 'lv store add mine <path to its .kdbx>'
    }

    It 'refuses personal details in a project store and removing a store still in use' {
        Push-Location $repository
        try {
            { Register-VaultStore -Name 'proj' -Target $projectDb -Project -UnlockWith x } | Should -Throw '*personal*'
            Register-VaultStore -Name 'proj' -Target $projectDb -Project -Default 6>$null | Out-Null
            { Unregister-VaultStore -Name 'proj' -Project } | Should -Throw '*used by the project default*'
            (Get-VaultStore -Name 'proj').Scope | Should -Be 'Project'
        }
        finally { Pop-Location }
    }

    It 'keeps personal values in the personal store and fills them in on another PC' {
        Push-Location $repository
        try {
            $arguments = 'store', 'add', 'proj', $projectDb, '--project', '--default'
            lv @arguments 6>$null | Out-Null
            $arguments = 'store', 'add', 'mine', $personalDb, '--personal'
            (lv @arguments 6>&1 | Out-String) | Should -Match "'mine' is this PC's personal store"
            $arguments = 'require', 'os', '--credential', '--note', 'domain account', '--path', $infra
            lv @arguments 6>$null
        }
        finally { Pop-Location }
        (Get-VaultStore -Name 'mine').Personal | Should -BeTrue
        Set-VaultEntry -Path $infra -Name 'os' -Credential -UserName 'jdoe' -Password (ConvertTo-TestSecret 'os-pass') 6>$null

        $saved = & $runInChild "Set-Location '$repository'; lv save" "$master`n$master"
        $saved.ExitCode | Should -Be 0 -Because $saved.Output
        $saved.Output | Should -Match "infra/secrets.local.lv -> personal store 'mine'.*1 of your values added"
        $saved.Output | Should -Not -Match 'os-pass'

        Remove-Item -LiteralPath $infra
        $loaded = & $runInChild "Set-Location '$repository'; lv load" "$master`n$master"
        $loaded.ExitCode | Should -Be 0 -Because $loaded.Output
        $loaded.Output | Should -Match "infra/secrets.local.lv <- personal store 'mine': 1 of your values filled in"
        Get-VaultEntry -Path $infra -Name 'os#user' -AsPlainText | Should -Be 'jdoe'
        Get-VaultEntry -Path $infra -Name 'os' -AsPlainText | Should -Be 'os-pass'
        (Get-VaultEntryList -Path $infra 3>$null | Where-Object Name -EQ 'os').Sync | Should -Be 'Personal'

        Set-VaultStore -Name 'mine' -Personal:$false | Out-Null
        Remove-Item -LiteralPath $infra
        $missing = & $runInChild "Set-Location '$repository'; lv load"
        $missing.ExitCode | Should -Be 0 -Because $missing.Output
        $missing.Output | Should -Match 'infra/secrets.local.lv: required values not set on this PC: os'
    }

    It 'carries a personal SSH key to another PC and writes it readable by its owner only' {
        $home1 = Join-Path $folder 'home1'
        $home2 = Join-Path $folder 'home2'
        New-Item -ItemType Directory -Path (Join-Path $home1 '.ssh') -Force | Out-Null
        [IO.File]::WriteAllText((Join-Path $home1 '.ssh/id_test'), "-----BEGIN OPENSSH PRIVATE KEY-----`nfake`n")
        Push-Location $repository
        try {
            $arguments = 'store', 'add', 'proj', $projectDb, '--project', '--default'
            lv @arguments 6>$null | Out-Null
            $arguments = 'store', 'add', 'mine', $personalDb, '--personal'
            lv @arguments 6>$null | Out-Null
        }
        finally { Pop-Location }

        $declared = & $runInChild "`$env:USERPROFILE = '$home1'; lv require ssh/id --file '~/.ssh/id_test' --note 'your key' --path '$infra'; lv set ssh/id --file '$home1\.ssh\id_test' --path '$infra'; Set-Location '$repository'; lv save" "$master`n$master"
        $declared.ExitCode | Should -Be 0 -Because $declared.Output
        $declared.Output | Should -Match "infra/secrets.local.lv -> personal store 'mine'.*1 of your values added"
        $declared.Output | Should -Not -Match 'fake'
        { Set-VaultEntry -Path $infra -Name 'shared-key' -FilePath (Join-Path $home1 '.ssh/id_test') -StoredPath '~/.ssh/other' } | Should -Throw '*only a required entry*'

        Remove-Item -LiteralPath $infra
        $loaded = & $runInChild "`$env:USERPROFILE = '$home2'; Set-Location '$repository'; lv load" "$master`n$master"
        $loaded.ExitCode | Should -Be 0 -Because $loaded.Output
        $key = Join-Path $home2 '.ssh/id_test'
        $loaded.Output | Should -Match ([regex]::Escape("Wrote your ssh/id to $key"))
        [IO.File]::ReadAllText($key) | Should -Be "-----BEGIN OPENSSH PRIVATE KEY-----`nfake`n"

        $acl = Get-Acl -LiteralPath $key
        $acl.AreAccessRulesProtected | Should -BeTrue
        $allowed = @(
            [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
            'S-1-5-18'
            'S-1-5-32-544'
        )
        $acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]) | ForEach-Object {
            $_.IdentityReference.Value | Should -BeIn $allowed
        }

        [IO.File]::WriteAllText($key, 'changed')
        $again = & $runInChild "`$env:USERPROFILE = '$home2'; Set-Location '$repository'; lv load" "$master`n$master"
        $again.Output | Should -Match 'lv restore ssh/id --overwrite'
        [IO.File]::ReadAllText($key) | Should -Be 'changed'
    }

    It 'leaves personal values out of a personal store that is also the project store, without a console' {
        Push-Location $repository
        try {
            $arguments = 'store', 'add', 'proj', $projectDb, '--project', '--default'
            lv @arguments 6>$null | Out-Null
            $arguments = 'store', 'add', 'proj', $projectDb
            lv @arguments 6>$null | Out-Null
            $arguments = 'store', 'set', 'proj', '--personal'
            lv @arguments 6>$null | Out-Null
            { Set-VaultStore -Name 'proj' -Personal -Project } | Should -Throw '*personal store belongs to this PC*'
        }
        finally { Pop-Location }
        Set-VaultEntry -Path $infra -Name 'os' -Required 6>$null
        Set-VaultEntry -Path $infra -Name 'os' -Value (ConvertTo-TestSecret 'os-pass') 6>$null

        $saved = & $runInChild "Set-Location '$repository'; lv save"
        $saved.ExitCode | Should -Be 0 -Because $saved.Output
        $saved.Output | Should -Match "Personal store 'proj' is also the project's store"

        $raw = [KeePassLib.PwDatabase]::new()
        $key = [KeePassLib.Keys.CompositeKey]::new(); $key.AddUserKey([KeePassLib.Keys.KcpPassword]::new($master))
        $raw.Open([KeePassLib.Serialization.IOConnectionInfo]::FromPath($projectDb), $key, $null)
        try {
            $entry = @($raw.RootGroup.GetEntries($true) | Where-Object { $_.Strings.ReadSafe('Title') -eq 'os' })
            $entry.Count | Should -Be 1
            $entry[0].Strings.ReadSafe('LocalVault.Kind') | Should -Be 'required'
            $entry[0].Strings.ReadSafe('Password') | Should -BeNullOrEmpty
        }
        finally { $raw.Close() }
    }

    It 'carries the registrations of keepass.json over to stores.json' {
        $appFolder = Join-Path $env:LOCALAPPDATA 'LocalVault'
        New-Item -ItemType Directory -Path $appFolder -Force | Out-Null
        @{ schema = 1; default = 'team'; databases = @(@{ name = 'team'; path = $projectDb }) } |
            ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $appFolder 'keepass.json')

        $stores = @(Get-VaultStore -Path $infra)

        $stores.Name | Should -Be @('team')
        $stores[0].Default | Should -BeTrue
        Test-Path -LiteralPath (Join-Path $appFolder 'stores.json') | Should -BeTrue
        Test-Path -LiteralPath (Join-Path $appFolder 'keepass.json') | Should -BeTrue
    }
}
