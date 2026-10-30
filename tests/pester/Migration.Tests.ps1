#Requires -Version 7.6
Set-StrictMode -Version Latest

BeforeAll {
    . (Join-Path $PSScriptRoot 'TestSupport.ps1')
    $script:master = 'pester master 1'
    $script:runInChild = {
        param([string]$Command)
        $prefix = "Import-Module '$(Join-Path $env:LOCALVAULT_MODULE_PATH 'LocalVault.psd1')'; "
        $output = $script:master | pwsh -NoProfile -NonInteractive -Command ($prefix + "try { $Command } catch { Write-Error `$_; exit 1 }; exit `$LASTEXITCODE") 2>&1
        [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = ($output | Out-String) }
    }
}

Describe 'Skill walkthrough: migrating two clixml vaults' {
    BeforeAll {
        $script:folder = Initialize-TestFolder
        $script:savedLocalAppData = $env:LOCALAPPDATA
        $env:LOCALAPPDATA = Join-Path $script:folder 'appdata'
        $script:repo = Join-Path $script:folder 'repo'
        git init --quiet $script:repo
        Set-Content -LiteralPath (Join-Path $script:repo '.gitignore') -Value 'secrets.local.*'
        foreach ($sub in 'infra', 'migration') { New-Item -ItemType Directory -Path (Join-Path $script:repo $sub) | Out-Null }
        Initialize-LegacyVault -Folder (Join-Path $script:repo 'infra') -Values @{ OsPassword = 'os-1'; GrafanaAdminPassword = 'gr-1' } | Out-Null
        $mailboxes = Initialize-LegacyVault -Folder (Join-Path $script:repo 'migration') -Values @{
            'john.doe@example.com' = 'm-1'; 'jane+test@example.org' = 'm-2'; 'bad key!' = 'm-3'
        }
        $script:oldMailboxes = Join-Path $script:repo 'migration/mailboxes.clixml'
        Move-Item -LiteralPath $mailboxes -Destination $script:oldMailboxes
        $script:infra = Join-Path $script:repo 'infra/secrets.local.lv'
        $script:migration = Join-Path $script:repo 'migration/secrets.local.lv'
        $script:database = Join-Path $script:folder 'mine.kdbx'
        Initialize-TestKeePass -Path $script:database -Master $script:master
    }
    AfterAll {
        $env:LOCALAPPDATA = $script:savedLocalAppData
        Clear-TestFolder $script:folder
    }

    It '1-2. inventories names without values and reports misfits' {
        $throwaway = Join-Path $folder 'inventory'
        Import-Vault -Path (Join-Path $throwaway 'v.lv') -Source $oldMailboxes -WarningVariable misfits -WarningAction SilentlyContinue 6>$null | Out-Null
        $names = (Get-VaultEntryList -Path (Join-Path $throwaway 'v.lv') 3>$null).Name

        $names | Sort-Object | Should -Be @('jane+test@example.org', 'john.doe@example.com')
        "$misfits" | Should -Match 'bad key!'
        Remove-Item -LiteralPath $throwaway -Recurse -Force
        Test-Path -LiteralPath $oldMailboxes | Should -BeTrue
    }

    It '5. creates one vault per old file at explicit paths' {
        Set-VaultEntry -Path $infra -Name 'Placeholder' -Value (ConvertTo-TestSecret 'p') 6>$null
        Import-Vault -Path $migration -Source $oldMailboxes -WarningAction SilentlyContinue 6>$null | Out-Null

        Get-VaultEntry -Path $infra -Name 'OsPassword' -AsPlainText | Should -Be 'os-1'
        Get-VaultEntry -Path $migration -Name 'john.doe@example.com' -AsPlainText | Should -Be 'm-1'
        Test-Path -LiteralPath (Join-Path $repo 'infra/secrets.local.clixml') | Should -BeFalse
        Test-Path -LiteralPath $oldMailboxes | Should -BeTrue
    }

    It '5. applies the per-entry decisions' {
        $arguments = 'mark', 'Placeholder', 'local', '--path', $infra
        lv @arguments 6>$null
        $arguments = 'require', 'OsPassword', '--note', 'your own OpenStack account', '--path', $infra
        lv @arguments 6>$null

        $list = Get-VaultEntryList -Path $infra 3>$null
        ($list | Where-Object Name -EQ 'Placeholder').Sync | Should -Be 'Local'
        ($list | Where-Object Name -EQ 'OsPassword').Sync | Should -Be 'Personal'
    }

    It '7. names the database and saves both vaults to their own groups' {
        Register-VaultStore -Target $database -Default | Out-Null
        (& $runInChild "Set-Location '$repo'; lv store use mine").ExitCode | Should -Be 0
        Test-Path -LiteralPath (Join-Path $repo 'localvault.json') | Should -BeTrue

        (& $runInChild "lv save --path '$infra'").ExitCode | Should -Be 0
        (& $runInChild "lv save --path '$migration'").ExitCode | Should -Be 0

        $raw = [KeePassLib.PwDatabase]::new()
        $key = [KeePassLib.Keys.CompositeKey]::new(); $key.AddUserKey([KeePassLib.Keys.KcpPassword]::new($master))
        $raw.Open([KeePassLib.Serialization.IOConnectionInfo]::FromPath($database), $key, $null)
        try {
            $root = $raw.RootGroup.FindCreateGroup('LocalVault', $false).FindCreateGroup('repo', $false)
            @($root.FindCreateGroup('migration', $false).Entries | ForEach-Object { $_.Strings.ReadSafe('Title') }) |
                Sort-Object | Should -Be @('jane+test@example.org', 'john.doe@example.com')
            $infraTitles = @($root.FindCreateGroup('infra', $false).Entries | ForEach-Object { $_.Strings.ReadSafe('Title') })
            $infraTitles | Should -Contain 'OsPassword'
            $infraTitles | Should -Not -Contain 'Placeholder'
            ($root.FindCreateGroup('infra', $false).Entries | Where-Object { $_.Strings.ReadSafe('Title') -eq 'OsPassword' }).Strings.ReadSafe('Password') |
                Should -BeNullOrEmpty
        }
        finally { $raw.Close() }
    }

    It 'lists every vault it writes in localvault.json' {
        $project = Get-Content -LiteralPath (Join-Path $repo 'localvault.json') -Raw | ConvertFrom-Json
        $project.vaults.path | Sort-Object | Should -Be @('infra/secrets.local.lv', 'migration/secrets.local.lv')
    }

    It 'saves every vault of the repository with one password' {
        Set-VaultEntry -Path $infra -Name 'NewInfra' -Value (ConvertTo-TestSecret 'n-1') 6>$null
        Set-VaultEntry -Path $migration -Name 'new@example.com' -Value (ConvertTo-TestSecret 'n-2') 6>$null

        $saved = & $runInChild "Set-Location '$repo'; lv save"
        $saved.ExitCode | Should -Be 0 -Because $saved.Output
        ([regex]::Matches($saved.Output, 'Opening store')).Count | Should -Be 1
        $saved.Output | Should -Match "infra/secrets.local.lv -> store 'mine'.*1 added"
        $saved.Output | Should -Match "migration/secrets.local.lv -> store 'mine'.*1 added"
    }

    It 'New machine: rebuilds both vaults from KeePass; personal values are Missing' {
        Remove-Item -LiteralPath $infra, $migration

        $loaded = & $runInChild "Set-Location '$repo'; lv load"
        $loaded.ExitCode | Should -Be 0 -Because $loaded.Output
        Get-VaultEntry -Path $infra -Name 'NewInfra' -AsPlainText | Should -Be 'n-1'

        Get-VaultEntry -Path $migration -Name 'jane+test@example.org' -AsPlainText | Should -Be 'm-2'
        Get-VaultEntry -Path $infra -Name 'GrafanaAdminPassword' -AsPlainText | Should -Be 'gr-1'
        (Get-VaultEntryList -Path $infra 3>$null | Where-Object Name -EQ 'OsPassword').Sync | Should -Be 'Missing'
        { Get-VaultEntry -Path $infra -Name 'OsPassword' } | Should -Throw '*your own OpenStack account*'
    }
}
