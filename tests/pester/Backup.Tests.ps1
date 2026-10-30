#Requires -Version 7.6
Set-StrictMode -Version Latest

BeforeAll {
    . (Join-Path $PSScriptRoot 'TestSupport.ps1')
    $script:secret = ConvertTo-TestSecret 'backup passphrase'
}
Describe 'Manual backups' {
    BeforeEach {
        $script:folder = Initialize-TestFolder
        $script:vault = Join-Path $script:folder 'v/secrets.local.lv'
        $script:destination = Join-Path $script:folder 'backups'
        $script:keyFile = Join-Path $script:folder 'kp/backup.lvkey'
        Set-VaultEntry -Path $vault -Name 'a' -Value (ConvertTo-TestSecret '1')
        Enable-VaultBackup -Path $vault -Manual -Destination $destination -KeyFileOut $keyFile -Passphrase $secret -WarningAction SilentlyContinue | Out-Null
    }
    AfterEach { Clear-TestFolder $script:folder }

    It 'writes a first backup and a new one on every write' {
        @(Get-ChildItem -LiteralPath $destination -Filter '*.lvbak').Count | Should -Be 1
        Start-Sleep -Milliseconds 5
        Set-VaultEntry -Path $vault -Name 'b' -Value (ConvertTo-TestSecret '2')

        @(Get-ChildItem -LiteralPath $destination -Filter '*.lvbak').Count | Should -Be 2
        (Get-Item -LiteralPath $keyFile).Length | Should -Be 32
    }

    It 'opens the newest backup with the keyfile and passphrase given by a person' {
        Set-VaultEntry -Path $vault -Name 'b' -Value (ConvertTo-TestSecret '2')

        (Test-VaultBackup -Path $vault -Manual -KeyFile $keyFile -Passphrase $secret).Entries | Should -Be 2
        { Test-VaultBackup -Path $vault -Manual -KeyFile $keyFile -Passphrase (ConvertTo-TestSecret 'wrong passphrase') } | Should -Throw
    }

    It 'restores a backup into another vault' {
        $other = Join-Path $folder 'w/secrets.local.lv'
        $newest = Get-ChildItem -LiteralPath $destination -Filter '*.lvbak' | Sort-Object Name | Select-Object -Last 1

        Import-Vault -Path $other -Source $newest.FullName -KeyFile $keyFile -Passphrase $secret | Out-Null

        Get-VaultEntry -Path $other -Name 'a' -AsPlainText | Should -Be '1'
        Get-VaultEntryList -Path $other -WarningVariable warnings -WarningAction SilentlyContinue | Out-Null
        $warnings | Should -Match 'never been exported'
    }

    It 'asks for the keyfile when importing a backup without one' {
        $newest = Get-ChildItem -LiteralPath $destination -Filter '*.lvbak' | Select-Object -First 1

        { Import-Vault -Path (Join-Path $folder 'w/secrets.local.lv') -Source $newest.FullName -Passphrase $secret } | Should -Throw '*-KeyFile*'
    }

    It 'keeps saving when the backup fails, and says so' {
        Remove-Item -LiteralPath $destination -Recurse
        Set-Content -LiteralPath $destination -Value 'not a folder'

        Set-VaultEntry -Path $vault -Name 'b' -Value (ConvertTo-TestSecret '2') -WarningVariable warnings -WarningAction SilentlyContinue

        $warnings | Should -Match 'backup failed'
        Get-VaultEntry -Path $vault -Name 'b' -AsPlainText | Should -Be '2'
        Get-VaultEntryList -Path $vault -WarningVariable listWarnings -WarningAction SilentlyContinue | Out-Null
        $listWarnings | Should -Match 'last backup failed'
    }

    It 'names backups with the vault id and leaves stray and foreign files alone' {
        $stray = Join-Path $destination '20261399T000000000Z.lvbak'
        $foreign = Join-Path $destination '20200101T000000000Z-fedcba9876543210.lvbak'
        Set-Content -LiteralPath $stray, $foreign -Value 'x'

        Set-VaultEntry -Path $vault -Name 'b' -Value (ConvertTo-TestSecret '2') -WarningVariable warnings

        $warnings | Should -BeNullOrEmpty
        Get-VaultEntry -Path $vault -Name 'b' -AsPlainText | Should -Be '2'
        Test-Path -LiteralPath $stray, $foreign | Should -Be $true, $true
        @(Get-ChildItem -LiteralPath $destination -Filter '*.lvbak' | Where-Object Name -Match '^\d{8}T\d{9}Z-[0-9a-f]{16}\.lvbak$').Count | Should -Be 3
    }

    It 'disables backups and keeps the files' {
        Disable-VaultBackup -Path $vault

        Set-VaultEntry -Path $vault -Name 'b' -Value (ConvertTo-TestSecret '2')
        @(Get-ChildItem -LiteralPath $destination -Filter '*.lvbak').Count | Should -Be 1
        { Invoke-VaultBackup -Path $vault } | Should -Throw '*not enabled*'
    }

    It 'runs a backup on demand' {
        Start-Sleep -Milliseconds 5
        (Invoke-VaultBackup -Path $vault).Newest | Should -Not -BeNullOrEmpty
        @(Get-ChildItem -LiteralPath $destination -Filter '*.lvbak').Count | Should -Be 2
    }

    It 'keeps backups on Clear-Vault when nobody can be asked' {
        Clear-Vault -Path $vault -Confirm:$false

        @(Get-ChildItem -LiteralPath $destination -Filter '*.lvbak').Count | Should -Be 1
    }

    It 'refuses to reuse an existing keyfile output path' {
        Disable-VaultBackup -Path $vault -Confirm:$false
        { Enable-VaultBackup -Path $vault -Manual -Destination $destination -KeyFileOut $keyFile -Passphrase $secret } | Should -Throw '*already exists*'
    }
}

Describe 'Generated backups' {
    BeforeEach {
        $script:folder = Initialize-TestFolder
        $script:vault = Join-Path $script:folder 'v/secrets.local.lv'
        $script:destination = Join-Path $script:folder 'backups'
        Set-VaultEntry -Path $vault -Name 'a' -Value (ConvertTo-TestSecret '1')
    }
    AfterEach { Clear-TestFolder $script:folder }

    It 'turns backups on without asking anything' {
        Enable-VaultBackup -Path $vault -Destination $destination -InformationVariable told

        "$told" | Should -Match 'Backups on'
        @(Get-ChildItem -LiteralPath $destination -Filter '*.lvbak').Count | Should -Be 1
        (Get-VaultBackup -Path $vault).Newest | Should -Not -BeNullOrEmpty
        Get-VaultEntryList -Path $vault -WarningVariable warnings -WarningAction SilentlyContinue | Out-Null
        $warnings | Should -BeNullOrEmpty
    }

    It 'changes nothing when backups are already on' {
        Enable-VaultBackup -Path $vault -Destination $destination
        $before = Get-VaultBackup -Path $vault

        Enable-VaultBackup -Path $vault -Destination (Join-Path $folder 'elsewhere') -InformationVariable told

        "$told" | Should -Match 'already on'
        (Get-VaultBackup -Path $vault).Destination | Should -Be $before.Destination
        Test-Path -LiteralPath (Join-Path $folder 'elsewhere') | Should -BeFalse
        @(Get-ChildItem -LiteralPath $destination -Filter '*.lvbak').Count | Should -Be 1
    }

    It 'tests and restores the newest backup with the key kept in the vault' {
        Enable-VaultBackup -Path $vault -Destination $destination
        Set-VaultEntry -Path $vault -Name 'b' -Value (ConvertTo-TestSecret '2')
        $newest = Get-ChildItem -LiteralPath $destination -Filter '*.lvbak' | Sort-Object Name | Select-Object -Last 1

        (Test-VaultBackup -Path $vault).Entries | Should -Be 2
        Remove-VaultEntry -Path $vault -Name 'b'
        Import-Vault -Path $vault -Source $newest.FullName | Out-Null

        Get-VaultEntry -Path $vault -Name 'b' -AsPlainText | Should -Be '2'
    }

    It 'says when backups are off' {
        Get-VaultBackup -Path $vault -InformationVariable told | Should -BeNullOrEmpty
        "$told" | Should -Match 'off'
    }
}

Describe 'Store registry' {
    BeforeEach {
        $script:folder = Initialize-TestFolder
        $script:savedLocalAppData = $env:LOCALAPPDATA
        $env:LOCALAPPDATA = Join-Path $script:folder 'appdata'
        Set-Content -LiteralPath (Join-Path $script:folder 'a.kdbx') -Value 'x'
    }
    AfterEach {
        $env:LOCALAPPDATA = $script:savedLocalAppData
        Clear-TestFolder $script:folder
    }

    It 'registers, lists and unregisters stores' {
        Register-VaultStore -Name 'work' -Target (Join-Path $folder 'a.kdbx') -Default | Out-Null

        $listed = Get-VaultStore
        $listed.Name | Should -Be 'work'
        $listed.Default | Should -BeTrue
        $listed.Exists | Should -BeTrue

        Unregister-VaultStore -Name 'work'
        Get-VaultStore | Should -BeNullOrEmpty
    }

    It 'points store commands at lv store add when no store is defined' {
        $vault = Join-Path $folder 'secrets.local.lv'
        Set-VaultEntry -Path $vault -Name 'a' -Value (ConvertTo-TestSecret '1')

        { Export-VaultToStore -Path $vault } | Should -Throw '*lv store add*'
        Get-VaultEntryList -Path $vault -WarningVariable warnings -WarningAction SilentlyContinue | Out-Null
        "$warnings" | Should -Not -Match 'store'
    }
}