#Requires -Version 7.6
Set-StrictMode -Version Latest

BeforeAll {
    . (Join-Path $PSScriptRoot 'TestSupport.ps1')
}
Describe 'Leftover backups' {
    BeforeEach {
        $script:folder = Initialize-TestFolder
        $script:savedOneDrive = $env:OneDrive
        $env:OneDrive = Join-Path $script:folder 'OneDrive'
        $script:vault = Join-Path $script:folder 'v/secrets.local.lv'
        $script:destination = Join-Path $script:folder 'backups'
        $script:fixtures = Join-Path $PSScriptRoot '../fixtures'
        Set-VaultEntry -Path $vault -Name 'a' -Value (ConvertTo-TestSecret '1')
        $script:location = [LocalVault.Core.VaultLocation]::Resolve($vault, $null, $script:folder)
        $script:addBackupSettings = {
            param([string]$Destination)
            $keyFile = [IO.File]::ReadAllBytes((Join-Path $fixtures 'localvault-2-beta1.lvkey'))
            $settings = [LocalVault.Core.BackupSettings]::Create('0123456789abcdef', $Destination, 'fixture passphrase 2', $keyFile)
            $store = [LocalVault.Core.VaultStore]::new($location, $null)
            $store.Update([Action[LocalVault.Core.VaultBody]] { param($body) $body.Backup = $settings }, $null) | Out-Null
        }
        $script:addBackup = {
            param([string]$Folder, [string]$Name = '20261008T120000000Z-0123456789abcdef.lvbak')
            New-Item -ItemType Directory -Path $Folder -Force | Out-Null
            $path = Join-Path $Folder $Name
            Copy-Item -LiteralPath (Join-Path $fixtures 'localvault-2-beta1.lvbak') -Destination $path
            $path
        }
    }
    AfterEach {
        $env:OneDrive = $script:savedOneDrive
        Clear-TestFolder $script:folder
    }

    It 'warns and keeps them when nobody can be asked' {
        & $addBackupSettings $destination
        $backup = & $addBackup $destination

        Get-VaultEntry -Path $vault -Name 'a' -AsPlainText -WarningVariable warnings -WarningAction SilentlyContinue | Should -Be '1'

        "$warnings" | Should -Match 'no longer makes automatic backups'
        "$warnings" | Should -Match '1 old automatic backup'
        Test-Path -LiteralPath $backup | Should -BeTrue
    }

    It 'finds backups in the default OneDrive folder without settings' {
        $default = [LocalVault.Core.LeftoverBackups]::DefaultFolder($location, [Environment]::MachineName, $env:OneDrive)
        & $addBackup $default | Out-Null

        Get-VaultEntryList -Path $vault -WarningVariable warnings -WarningAction SilentlyContinue | Out-Null

        "$warnings" | Should -Match 'no longer makes automatic backups'
    }

    It 'says nothing when there are none' {
        Get-VaultEntry -Path $vault -Name 'a' -AsPlainText -WarningVariable warnings | Out-Null

        $warnings | Should -BeNullOrEmpty
    }

    It 'are deleted with the vault by Clear-Vault' {
        & $addBackupSettings $destination
        $own = & $addBackup $destination
        $foreign = & $addBackup $destination '20261008T120000000Z-fedcba9876543210.lvbak'
        $default = [LocalVault.Core.LeftoverBackups]::DefaultFolder($location, [Environment]::MachineName, $env:OneDrive)
        & $addBackup $default | Out-Null

        Clear-Vault -Path $vault -Confirm:$false

        Test-Path -LiteralPath $vault, $own, $foreign | Should -Be $false, $false, $true
        Test-Path -LiteralPath (Join-Path $env:OneDrive 'LocalVault') | Should -BeFalse
    }

    It 'are deleted by Clear-Vault after the vault itself is gone' {
        $default = [LocalVault.Core.LeftoverBackups]::DefaultFolder($location, [Environment]::MachineName, $env:OneDrive)
        $backup = & $addBackup $default
        Remove-Item -LiteralPath $vault

        Clear-Vault -Path $vault -Confirm:$false

        Test-Path -LiteralPath $backup | Should -BeFalse
    }

    It 'still restore with the key kept in the vault' {
        & $addBackupSettings $destination
        $backup = & $addBackup $destination

        (Import-Vault -Path $vault -Source $backup -WarningAction SilentlyContinue).Added | Should -Be 3
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