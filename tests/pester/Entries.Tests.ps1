#Requires -Version 7.6
Set-StrictMode -Version Latest

BeforeAll {
    . (Join-Path $PSScriptRoot 'TestSupport.ps1')
}

Describe 'Secret entries' {
    BeforeEach {
        $script:folder = Initialize-TestFolder
        $script:vault = Join-Path $script:folder 'secrets.local.lv'
    }
    AfterEach { Clear-TestFolder $script:folder }

    It 'round-trips a secret as SecureString and plain text' {
        Set-VaultEntry -Path $vault -Name 'os.password' -Value (ConvertTo-TestSecret "p$([char]0xE4)ssw$([char]0xF6)rd 1")

        $secure = Get-VaultEntry -Path $vault -Name 'OS.PASSWORD'
        $secure | Should -BeOfType [securestring]
        [Net.NetworkCredential]::new('', $secure).Password | Should -Be "p$([char]0xE4)ssw$([char]0xF6)rd 1"
        Get-VaultEntry -Path $vault -Name 'os.password' -AsPlainText | Should -Be "p$([char]0xE4)ssw$([char]0xF6)rd 1"
    }

    It 'keeps unrelated entries on every write' {
        Set-VaultEntry -Path $vault -Name 'a' -Value (ConvertTo-TestSecret '1')
        Set-VaultEntry -Path $vault -Name 'b' -Value (ConvertTo-TestSecret '2')
        Set-VaultEntry -Path $vault -Name 'a' -Value (ConvertTo-TestSecret '3')
        Remove-VaultEntry -Path $vault -Name 'b'
        Set-VaultEntry -Path $vault -Name 'c' -Value (ConvertTo-TestSecret '4')

        (Get-VaultEntryList -Path $vault).Name | Should -Be @('a', 'c')
        Get-VaultEntry -Path $vault -Name 'a' -AsPlainText | Should -Be '3'
    }

    It 'pipes a SecureString into Set-VaultEntry' {
        ConvertTo-TestSecret 'piped' | Set-VaultEntry -Path $vault -Name 'p'

        Get-VaultEntry -Path $vault -Name 'p' -AsPlainText | Should -Be 'piped'
    }

    It 'lists name, kind and age but never a value' {
        Set-VaultEntry -Path $vault -Name 'common/token' -Value (ConvertTo-TestSecret 'TOP-SECRET-VALUE')

        $list = Get-VaultEntryList -Path $vault -WarningAction SilentlyContinue
        $list.Name | Should -Be 'common/token'
        $list.Scope | Should -Be 'common'
        $list.Kind | Should -Be 'secret'
        $list.Age | Should -BeOfType [timespan]
        ($list | ConvertTo-Json) | Should -Not -Match 'TOP-SECRET-VALUE'
    }

    It 'filters the list by scope' {
        Set-VaultEntry -Path $vault -Name 'common/a' -Value (ConvertTo-TestSecret '1')
        Set-VaultEntry -Path $vault -Name 'prod/b' -Value (ConvertTo-TestSecret '2')

        (Get-VaultEntryList -Path $vault -Scope 'prod' -WarningAction SilentlyContinue).Name | Should -Be 'prod/b'
    }

    It 'warns that the vault was never exported' {
        Set-VaultEntry -Path $vault -Name 'a' -Value (ConvertTo-TestSecret '1')

        Get-VaultEntryList -Path $vault -WarningVariable warnings -WarningAction SilentlyContinue | Out-Null
        "$warnings" | Should -Match 'Only on this PC, never exported: a\.'
    }

    It 'warns only about entries without a copy in the store' {
        $savedLocalAppData = $env:LOCALAPPDATA
        try {
            $env:LOCALAPPDATA = Join-Path $folder 'appdata'
            Set-Content -LiteralPath (Join-Path $folder 'team.kdbx') -Value 'x'
            Register-VaultStore -Name 'team' -Target (Join-Path $folder 'team.kdbx') -Default | Out-Null
            Set-VaultEntry -Path $vault -Name 'shared' -Value (ConvertTo-TestSecret '1')
            Set-VaultEntry -Path $vault -Name 'mine' -Value (ConvertTo-TestSecret '2') -Local
            Set-VaultEntry -Path $vault -Name 'personal' -Required

            Get-VaultEntryList -Path $vault -WarningVariable warnings -WarningAction SilentlyContinue | Out-Null
            ($warnings | Where-Object { "$_" -like 'Only on this PC*' }) | Should -Match ': mine\.'

            Set-VaultEntry -Path $vault -Name 'mine' -Sharing Shared
            Get-VaultEntryList -Path $vault -WarningVariable warnings -WarningAction SilentlyContinue | Out-Null
            "$warnings" | Should -Not -Match 'Only on this PC'
        }
        finally {
            $env:LOCALAPPDATA = $savedLocalAppData
        }
    }

    It 'resets a personal value through lv and refuses a shared or unset one' {
        Set-VaultEntry -Path $vault -Name 'personal' -Required
        Set-VaultEntry -Path $vault -Name 'personal' -Value (ConvertTo-TestSecret 'mine')
        Set-VaultEntry -Path $vault -Name 'shared' -Value (ConvertTo-TestSecret 's')

        $said = lv @('reset', 'personal', '-NoPrompt', '-Path', $vault) 6>&1 | Out-String

        $said | Should -Match 'personal: your value is removed from this vault\.'
        (Get-VaultEntryList -Path $vault -WarningAction SilentlyContinue | Where-Object Name -EQ 'personal').Sync | Should -Be 'Missing'
        { Reset-VaultEntry -Path $vault -Name 'shared' } | Should -Throw '*not a required entry*'
        { Reset-VaultEntry -Path $vault -Name 'personal' } | Should -Throw '*no personal value*'
        Get-VaultEntry -Path $vault -Name 'shared' -AsPlainText | Should -Be 's'
    }

    It 'rejects an invalid name and writes nothing' {
        { Set-VaultEntry -Path $vault -Name 'a/b/c' -Value (ConvertTo-TestSecret '1') } | Should -Throw '*not a valid name*'
        Test-Path -LiteralPath $vault | Should -BeFalse
    }

    It 'fails for a missing entry and warns on removing one' {
        Set-VaultEntry -Path $vault -Name 'a' -Value (ConvertTo-TestSecret '1')

        { Get-VaultEntry -Path $vault -Name 'missing' } | Should -Throw "*No entry 'missing'*"
        Remove-VaultEntry -Path $vault -Name 'missing' -WarningVariable warnings -WarningAction SilentlyContinue
        $warnings | Should -Match 'No entry'
    }

    It 'respects -WhatIf' {
        Set-VaultEntry -Path $vault -Name 'a' -Value (ConvertTo-TestSecret '1') -WhatIf

        Test-Path -LiteralPath $vault | Should -BeFalse
    }
}

Describe 'Scopes and session environment' {
    BeforeAll {
        $script:folder = Initialize-TestFolder
        $script:vault = Join-Path $script:folder 'secrets.local.lv'
        Set-VaultEntry -Path $vault -Name 'common/licence' -Value (ConvertTo-TestSecret 'shared')
        Set-VaultEntry -Path $vault -Name 'common/db' -Value (ConvertTo-TestSecret 'shared-db')
        Set-VaultEntry -Path $vault -Name 'prod/db' -Value (ConvertTo-TestSecret 'prod-db')
    }
    AfterAll { Clear-TestFolder $script:folder }

    It 'prefers the scoped value' {
        Resolve-VaultEntry -Path $vault -Name 'db' -Scope 'prod' -AsPlainText | Should -Be 'prod-db'
    }

    It 'falls back to common' {
        Resolve-VaultEntry -Path $vault -Name 'licence' -Scope 'prod' -AsPlainText | Should -Be 'shared'
    }

    It 'names both tried names when neither exists' {
        { Resolve-VaultEntry -Path $vault -Name 'nope' -Scope 'prod' } | Should -Throw "*'prod/nope'*'common/nope'*"
    }

    It 'refuses a scoped name' {
        { Resolve-VaultEntry -Path $vault -Name 'prod/db' -Scope 'prod' } | Should -Throw '*already has a scope*'
    }

    It 'sets process environment variables only' {
        try {
            Use-VaultEnv -Path $vault -Map @{ LV_TEST_DB = 'db'; LV_TEST_LICENCE = 'common/licence' } -Scope 'prod'

            $env:LV_TEST_DB | Should -Be 'prod-db'
            $env:LV_TEST_LICENCE | Should -Be 'shared'
            [Environment]::GetEnvironmentVariable('LV_TEST_DB', 'User') | Should -BeNullOrEmpty
        }
        finally {
            Remove-Item Env:\LV_TEST_DB, Env:\LV_TEST_LICENCE -ErrorAction SilentlyContinue
        }
    }

    It 'warns for a missing mapping and sets the others' {
        try {
            Use-VaultEnv -Path $vault -Map @{ LV_TEST_X = 'missing'; LV_TEST_Y = 'prod/db' } -WarningVariable warnings -WarningAction SilentlyContinue

            $warnings | Should -Match 'LV_TEST_X'
            $env:LV_TEST_Y | Should -Be 'prod-db'
        }
        finally {
            Remove-Item Env:\LV_TEST_Y -ErrorAction SilentlyContinue
        }
    }
}

Describe 'File entries' {
    BeforeEach {
        $script:folder = Initialize-TestFolder
        $script:vault = Join-Path $script:folder 'secrets.local.lv'
        $script:source = Join-Path $script:folder 'clouds.yaml'
        [IO.File]::WriteAllBytes($script:source, [byte[]](99, 58, 13, 10, 0, 255))
        Set-VaultEntry -Path $vault -Name 'clouds.yaml' -FilePath $script:source
    }
    AfterEach { Clear-TestFolder $script:folder }

    It 'stores bytes and the path relative to the vault' {
        $bytes = Get-VaultEntry -Path $vault -Name 'clouds.yaml'

        , $bytes | Should -BeOfType [byte[]]
        $bytes | Should -Be @(99, 58, 13, 10, 0, 255)
    }

    It 'restores a missing file' {
        Remove-Item -LiteralPath $source

        (Restore-VaultFile -Path $vault -All).Action | Should -Be 'Written'
        [IO.File]::ReadAllBytes($source) | Should -Be @(99, 58, 13, 10, 0, 255)
    }

    It 'is silent for an identical file' {
        (Restore-VaultFile -Path $vault -Name 'clouds.yaml').Action | Should -Be 'Identical'
    }

    It 'keeps a different file unless told to overwrite it' {
        Set-Content -LiteralPath $source -Value 'changed' -NoNewline

        (Restore-VaultFile -Path $vault -Name 'clouds.yaml' -WarningAction SilentlyContinue).Action | Should -Be 'Kept'
        Get-Content -LiteralPath $source -Raw | Should -Be 'changed'

        (Restore-VaultFile -Path $vault -Name 'clouds.yaml' -Overwrite).Action | Should -Be 'Written'
        [IO.File]::ReadAllBytes($source) | Should -Be @(99, 58, 13, 10, 0, 255)
    }

    It 'restores under another destination' {
        $other = Join-Path $folder 'elsewhere'

        Restore-VaultFile -Path $vault -All -Destination $other | Out-Null

        Test-Path -LiteralPath (Join-Path $other 'clouds.yaml') | Should -BeTrue
    }

    It 'refuses a stored path escaping the folder' {
        { Set-VaultEntry -Path $vault -Name 'evil' -FilePath $source -StoredPath '../evil.txt' } | Should -Throw '*not a valid stored path*'
    }

    It 'refuses a file outside the vault folder without -StoredPath' {
        $outside = Initialize-TestFolder
        try {
            $file = Join-Path $outside 'x.txt'
            Set-Content -LiteralPath $file -Value 'x'
            { Set-VaultEntry -Path $vault -Name 'x' -FilePath $file } | Should -Throw '*Give -StoredPath*'
        }
        finally { Clear-TestFolder $outside }
    }

    It 'refuses to return a file entry as a secret' {
        { Resolve-VaultEntry -Path $vault -Name 'clouds.yaml' -Scope 'x' -Fallback 'y' } | Should -Throw
        Set-VaultEntry -Path $vault -Name 'common/f' -FilePath $source -StoredPath 'f'
        { Resolve-VaultEntry -Path $vault -Name 'f' -Scope 'prod' } | Should -Throw '*not a secret*'
    }
}

Describe 'Vault location' {
    BeforeEach { $script:repo = Initialize-TestFolder -Git }
    AfterEach {
        Pop-Location -ErrorAction SilentlyContinue
        Clear-TestFolder $script:repo
    }

    It 'refuses to create the default vault without a person to confirm' {
        Push-Location $repo
        { Set-VaultEntry -Name 'a' -Value (ConvertTo-TestSecret '1') } | Should -Throw '*Pass -Path*'
        Test-Path -LiteralPath (Join-Path $repo 'secrets.local.lv') | Should -BeFalse
    }

    It 'uses an existing vault at the git root' {
        Set-VaultEntry -Path (Join-Path $repo 'secrets.local.lv') -Name 'a' -Value (ConvertTo-TestSecret '1')
        New-Item -ItemType Directory -Path (Join-Path $repo 'sub') | Out-Null
        Push-Location (Join-Path $repo 'sub')

        Get-VaultEntry -Name 'a' -AsPlainText | Should -Be '1'
    }

    It 'uses LOCALVAULT_PATH' {
        $path = Join-Path $repo 'secrets.local.lv'
        $env:LOCALVAULT_PATH = $path
        try {
            Set-VaultEntry -Name 'a' -Value (ConvertTo-TestSecret '1')
            Test-Path -LiteralPath $path | Should -BeTrue
        }
        finally { Remove-Item Env:\LOCALVAULT_PATH }
    }

    It 'refuses to create a vault git does not ignore' {
        Remove-Item -LiteralPath (Join-Path $repo '.gitignore')

        { Set-VaultEntry -Path (Join-Path $repo 'secrets.local.lv') -Name 'a' -Value (ConvertTo-TestSecret '1') } |
            Should -Throw '*does not ignore*'
    }

    It 'adds the vault to .gitignore with lv init, ignoring a rule that is not committed' {
        Remove-Item -LiteralPath (Join-Path $repo '.gitignore')
        Set-Content -LiteralPath (Join-Path $repo '.git\info\exclude') -Value 'secrets.local.*'
        $vault = Join-Path $repo 'secrets.local.lv'

        $said = lv @('init', '-Path', $vault) 6>&1 | Out-String

        $said | Should -Match 'Commit \.gitignore\.'
        Get-Content -LiteralPath (Join-Path $repo '.gitignore') | Should -Be @('# LocalVault', 'secrets.local.*')
        lv @('init', '-Path', $vault) 6>&1 | Out-String | Should -Match 'Nothing to add\.'
    }

    It 'names lv init when it refuses to create a vault git does not ignore' {
        Remove-Item -LiteralPath (Join-Path $repo '.gitignore')

        { Set-VaultEntry -Path (Join-Path $repo 'infra\team.lv') -Name 'a' -Value (ConvertTo-TestSecret '1') } |
            Should -Throw "*Run lv init, or add '/infra/team.lv*' to .gitignore*"
    }

    It 'creates further vaults only at an explicit path' {
        Set-VaultEntry -Path (Join-Path $repo 'infra/secrets.local.lv') -Name 'a' -Value (ConvertTo-TestSecret '1')

        Set-VaultEntry -Path (Join-Path $repo 'migration/secrets.local.lv') -Name 'b' -Value (ConvertTo-TestSecret '2')
        Get-VaultEntry -Path (Join-Path $repo 'migration/secrets.local.lv') -Name 'b' -AsPlainText | Should -Be '2'
        Push-Location $repo
        try {
            { Set-VaultEntry -Name 'c' -Value (ConvertTo-TestSecret '3') -Confirm:$false } | Should -Throw '*already has vault files*'
        }
        finally { Pop-Location }
    }
}

Describe 'Clear-Vault' {
    It 'deletes the vault and its previous generation' {
        $folder = Initialize-TestFolder
        try {
            $vault = Join-Path $folder 'secrets.local.lv'
            Set-VaultEntry -Path $vault -Name 'a' -Value (ConvertTo-TestSecret '1')
            Set-VaultEntry -Path $vault -Name 'a' -Value (ConvertTo-TestSecret '2')
            Test-Path -LiteralPath "$vault.bak" | Should -BeTrue

            Clear-Vault -Path $vault -Confirm:$false

            Test-Path -LiteralPath $vault | Should -BeFalse
            Test-Path -LiteralPath "$vault.bak" | Should -BeFalse
        }
        finally { Clear-TestFolder $folder }
    }
}

Describe 'Restoring files in a git repository' {
    BeforeEach {
        $script:folder = Initialize-TestFolder -Git
        $script:vault = Join-Path $script:folder 'secrets.local.lv'
        $script:source = Join-Path $script:folder 'clouds.yaml'
        Set-Content -LiteralPath $script:source -Value 'clouds' -NoNewline
        Set-VaultEntry -Path $script:vault -Name 'clouds.yaml' -FilePath $script:source
        Remove-Item -LiteralPath $script:source
    }
    AfterEach { Clear-TestFolder $script:folder }

    It 'refuses a target that git does not ignore and writes nothing' {
        { Restore-VaultFile -Path $vault -All } | Should -Throw '*git does not ignore*'
        Test-Path -LiteralPath $source | Should -BeFalse
    }

    It 'writes a target that git ignores' {
        Add-Content -LiteralPath (Join-Path $folder '.gitignore') -Value 'clouds.yaml'

        (Restore-VaultFile -Path $vault -All).Action | Should -Be 'Written'
    }

    It 'writes a target that git does not ignore with -SkipGitIgnoreCheck' {
        (Restore-VaultFile -Path $vault -All -SkipGitIgnoreCheck).Action | Should -Be 'Written'
    }
}

Describe 'Clipboard' {
    BeforeDiscovery {
        $marker = [guid]::NewGuid().ToString('N')
        Set-Clipboard -Value $marker -ErrorAction SilentlyContinue
        $script:noClipboard = (Get-Clipboard -ErrorAction SilentlyContinue) -ne $marker
    }

    BeforeEach {
        $script:folder = Initialize-TestFolder
        $script:vault = Join-Path $script:folder 'secrets.local.lv'
        Set-VaultEntry -Path $vault -Name 'a' -Value (ConvertTo-TestSecret 'clip-1')
        Set-VaultEntry -Path $vault -Name 'f' -FilePath (New-Item -Path (Join-Path $folder 'f.txt') -Value 'x').FullName
    }
    AfterEach { Clear-TestFolder $script:folder }

    It 'copies a secret and clears it unless something else was copied' -Skip:$script:noClipboard {
        Copy-VaultEntry -Path $vault -Name 'a' -Seconds 1
        Get-Clipboard | Should -Be 'clip-1'
        Start-Sleep -Milliseconds 1600
        Get-Clipboard | Should -BeNullOrEmpty

        Copy-VaultEntry -Path $vault -Name 'a' -Seconds 1
        Set-Clipboard -Value 'copied meanwhile'
        Start-Sleep -Milliseconds 1600
        Get-Clipboard | Should -Be 'copied meanwhile'
    }

    It 'refuses a file entry' {
        { Copy-VaultEntry -Path $vault -Name 'f' } | Should -Throw '*not a secret*'
    }
}