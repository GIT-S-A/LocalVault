#Requires -Version 7.6
Set-StrictMode -Version Latest

BeforeAll {
    . (Join-Path $PSScriptRoot 'TestSupport.ps1')
    $script:secret = ConvertTo-TestSecret 'transfer passphrase'
}

Describe 'Export-Vault and Import-Vault' {
    BeforeEach {
        $script:folder = Initialize-TestFolder
        $script:vault = Join-Path $script:folder 'a/secrets.local.lv'
        $script:other = Join-Path $script:folder 'b/secrets.local.lv'
        $script:export = Join-Path $script:folder 'out/vault.export'
        Set-VaultEntry -Path $vault -Name 'common/x' -Value (ConvertTo-TestSecret 'x1')
        Set-VaultEntry -Path $vault -Name 'y' -Value (ConvertTo-TestSecret 'y1')
    }
    AfterEach { Clear-TestFolder $script:folder }

    It 'round-trips into an empty vault' {
        Export-Vault -Path $vault -Destination $export -Passphrase $secret

        $summary = Import-Vault -Path $other -Source $export -Passphrase $secret
        $summary.Added | Should -Be 2
        Get-VaultEntry -Path $other -Name 'common/x' -AsPlainText | Should -Be 'x1'
    }

    It 'records the export so the list stops warning' {
        Export-Vault -Path $vault -Destination $export -Passphrase $secret

        Get-VaultEntryList -Path $vault -WarningVariable warnings | Out-Null
        $warnings | Should -BeNullOrEmpty
    }

    It 'refuses to overwrite an existing export without -Overwrite' {
        New-Item -ItemType Directory -Path (Split-Path $export) | Out-Null
        Set-Content -LiteralPath $export -Value 'older backup'

        { Export-Vault -Path $vault -Destination $export -Passphrase $secret } | Should -Throw '*already exists*'
        Get-Content -LiteralPath $export | Should -Be 'older backup'

        Export-Vault -Path $vault -Destination $export -Passphrase $secret -Overwrite
        (Get-Content -LiteralPath $export)[0] | Should -Be 'LOCALVAULT-2'
    }

    It 'refuses a short passphrase and writes nothing' {
        { Export-Vault -Path $vault -Destination $export -Passphrase (ConvertTo-TestSecret 'short') } | Should -Throw '*too short*'
        Test-Path -LiteralPath $export | Should -BeFalse
    }

    It 'refuses to export an empty vault' {
        Remove-VaultEntry -Path $vault -Name 'common/x'
        Remove-VaultEntry -Path $vault -Name 'y'

        { Export-Vault -Path $vault -Destination $export -Passphrase $secret } | Should -Throw '*empty*'
    }

    It 'is silent for identical entries' {
        Export-Vault -Path $vault -Destination $export -Passphrase $secret

        $summary = Import-Vault -Path $vault -Source $export -Passphrase $secret
        $summary.Identical | Should -Be 2
        $summary.Added | Should -Be 0
    }

    It 'stops on conflicts when nobody can be asked, writing nothing' {
        Export-Vault -Path $vault -Destination $export -Passphrase $secret
        Set-VaultEntry -Path $other -Name 'y' -Value (ConvertTo-TestSecret 'mine')

        { Import-Vault -Path $other -Source $export -Passphrase $secret } | Should -Throw '*differ from the vault: y*'
        Get-VaultEntry -Path $other -Name 'y' -AsPlainText | Should -Be 'mine'
        { Get-VaultEntry -Path $other -Name 'common/x' } | Should -Throw
    }

    It 'replaces only the named conflicts' {
        Export-Vault -Path $vault -Destination $export -Passphrase $secret
        Set-VaultEntry -Path $other -Name 'y' -Value (ConvertTo-TestSecret 'mine')

        $summary = Import-Vault -Path $other -Source $export -Passphrase $secret -Replace 'y'
        $summary.Replaced | Should -Be 1
        Get-VaultEntry -Path $other -Name 'y' -AsPlainText | Should -Be 'y1'
    }

    It 'replaces all conflicts with -ReplaceAll' {
        Export-Vault -Path $vault -Destination $export -Passphrase $secret
        Set-VaultEntry -Path $other -Name 'common/x' -Value (ConvertTo-TestSecret 'mine')

        Import-Vault -Path $other -Source $export -Passphrase $secret -ReplaceAll | Out-Null
        Get-VaultEntry -Path $other -Name 'common/x' -AsPlainText | Should -Be 'x1'
    }

    It 'refuses a wrong passphrase and leaves the vault unchanged' {
        Export-Vault -Path $vault -Destination $export -Passphrase $secret
        Set-VaultEntry -Path $other -Name 'keep' -Value (ConvertTo-TestSecret 'k')
        $before = Get-Content -LiteralPath $other -Raw

        { Import-Vault -Path $other -Source $export -Passphrase (ConvertTo-TestSecret 'wrong passphrase!') } | Should -Throw '*Wrong passphrase*'
        Get-Content -LiteralPath $other -Raw | Should -Be $before
    }

    It 'refuses to import a local vault file' {
        { Import-Vault -Path $other -Source $vault -Passphrase $secret } | Should -Throw '*Export-Vault*'
    }
}

Describe 'Published LOCALVAULT-2 files' {
    BeforeEach {
        $script:folder = Initialize-TestFolder
        $script:vault = Join-Path $script:folder 'secrets.local.lv'
        $script:fixtures = Join-Path $PSScriptRoot '../fixtures'
        $script:passphrase = ConvertTo-TestSecret 'fixture passphrase 2'
    }
    AfterEach { Clear-TestFolder $script:folder }

    It 'imports the 1.0.0-beta1 export' {
        $source = Join-Path $fixtures 'localvault-2-beta1.export'

        Import-Vault -Path $vault -Source $source -Passphrase $passphrase | Out-Null

        Get-VaultEntry -Path $vault -Name FixtureSecret -AsPlainText | Should -Be 'fixture value'
        Restore-VaultFile -Path $vault -Name fixture.txt | Out-Null
        Get-Content -LiteralPath (Join-Path $folder 'config/fixture.txt') -Raw | Should -Be "fixture file`n"
    }

    It 'imports the 1.0.0-beta1 backup with its keyfile' {
        $importVaultSplat = @{
            Path       = $vault
            Source     = Join-Path $fixtures 'localvault-2-beta1.lvbak'
            KeyFile    = Join-Path $fixtures 'localvault-2-beta1.lvkey'
            Passphrase = $passphrase
        }

        (Import-Vault @importVaultSplat).Added | Should -Be 3
    }
}

Describe 'SAAS-VAULT-1 exports' {
    BeforeEach {
        $script:folder = Initialize-TestFolder
        $script:vault = Join-Path $script:folder 'secrets.local.lv'
    }
    AfterEach { Clear-TestFolder $script:folder }

    It 'imports the export written by the reference vault.ps1' {
        $fixture = Join-Path $PSScriptRoot '../fixtures/legacy-vault-export.v1'

        Import-Vault -Path $vault -Source $fixture -Passphrase (ConvertTo-TestSecret 'fixture passphrase 1') | Out-Null

        Get-VaultEntry -Path $vault -Name 'LOKI_PUSH_TOKEN' -AsPlainText | Should -Be 'dummy-loki-token-1'
        Get-VaultEntry -Path $vault -Name 'S3_SECRET_KEY' -AsPlainText | Should -Be "dummy s3 secret with spaces & $([char]0xFC)n$([char]0xEF)code"
    }

    It 'maps files, refuses SSH keys and keeps the rest' {
        $path = Join-Path $folder 'old.export'
        $values = @{
            OsPassword             = 'os'
            '__File:clouds.yaml'   = "clouds:`n  a: 1`n"
            '__SshPrivateKey'      = 'PRIVATE'
            '__SshPublicKey'       = 'PUBLIC'
        }
        Set-Content -LiteralPath $path -Value (ConvertTo-V1Export -Values $values -Secret $secret)

        $summary = Import-Vault -Path $vault -Source $path -Passphrase $secret -WarningVariable warnings -WarningAction SilentlyContinue

        $summary.Added | Should -Be 2
        $summary.Refused | Should -Be 2
        ($warnings -join "`n") | Should -Match '__SshPrivateKey.*KeePass'
        (Get-VaultEntryList -Path $vault -WarningAction SilentlyContinue | Where-Object Name -EQ 'clouds.yaml').Kind | Should -Be 'file'
        Restore-VaultFile -Path $vault -Name 'clouds.yaml' | Out-Null
        Get-Content -LiteralPath (Join-Path $folder 'clouds.yaml') -Raw | Should -Be "clouds:`n  a: 1`n"
    }
}

Describe 'Legacy secrets.local.clixml' {
    BeforeEach {
        $script:folder = Initialize-TestFolder
        $script:vault = Join-Path $script:folder 'secrets.local.lv'
        $script:legacy = Initialize-LegacyVault -Folder $script:folder -Values @{
            OsPassword           = 'os-pw'
            GrafanaAdminPassword = 'grafana'
            __SshPrivateKey      = 'PRIVATE'
        }
    }
    AfterEach { Clear-TestFolder $script:folder }

    It 'reads without migrating' {
        Get-VaultEntry -Path $vault -Name 'GrafanaAdminPassword' -AsPlainText | Should -Be 'grafana'

        Test-Path -LiteralPath $legacy | Should -BeTrue
        Test-Path -LiteralPath $vault | Should -BeFalse
    }

    It 'migrates on the first write and keeps the old file' {
        Set-VaultEntry -Path $vault -Name 'new' -Value (ConvertTo-TestSecret 'n') -WarningVariable warnings -WarningAction SilentlyContinue

        Test-Path -LiteralPath $legacy | Should -BeFalse
        @(Get-ChildItem -LiteralPath $folder -Filter 'secrets.local.clixml.migrated-*').Count | Should -Be 1
        Get-VaultEntry -Path $vault -Name 'OsPassword' -AsPlainText | Should -Be 'os-pw'
        Get-VaultEntry -Path $vault -Name 'GrafanaAdminPassword' -AsPlainText | Should -Be 'grafana'
        ($warnings -join "`n") | Should -Match '__SshPrivateKey.*still in'
    }

    It 'blocks when both files exist and differ, then merges through Import-Vault' {
        Set-VaultEntry -Path $vault -Name 'new' -Value (ConvertTo-TestSecret 'n') -WarningAction SilentlyContinue
        Initialize-LegacyVault -Folder $folder -Values @{ GrafanaAdminPassword = 'rotated'; Extra = 'e' } | Out-Null

        Get-VaultEntry -Path $vault -Name 'new' -AsPlainText -WarningVariable warnings -WarningAction SilentlyContinue | Should -Be 'n'
        ($warnings -join "`n") | Should -Match 'Import-Vault'
        { Set-VaultEntry -Path $vault -Name 'other' -Value (ConvertTo-TestSecret 'o') } | Should -Throw '*Import-Vault*'

        Import-Vault -Path $vault -Source $legacy -Replace 'GrafanaAdminPassword' | Out-Null

        Test-Path -LiteralPath $legacy | Should -BeFalse
        Get-VaultEntry -Path $vault -Name 'GrafanaAdminPassword' -AsPlainText | Should -Be 'rotated'
        Get-VaultEntry -Path $vault -Name 'Extra' -AsPlainText | Should -Be 'e'
        @(Get-ChildItem -LiteralPath $folder -Filter 'secrets.local.clixml.migrated-*').Count | Should -Be 2
    }
}

Describe 'Importing a secrets.local.clixml from elsewhere' {
    BeforeEach {
        $script:folder = Initialize-TestFolder
        $script:vault = Join-Path $script:folder 'a/secrets.local.lv'
        $other = New-Item -ItemType Directory -Path (Join-Path $script:folder 'b')
        $script:foreign = Initialize-LegacyVault -Folder $other.FullName -Values @{ Shared = 'theirs'; Extra = 'e' }
        Set-VaultEntry -Path $script:vault -Name 'Shared' -Value (ConvertTo-TestSecret 'mine')
    }
    AfterEach { Clear-TestFolder $script:folder }

    It 'leaves the source file in place' {
        Import-Vault -Path $vault -Source $foreign -ReplaceAll | Out-Null

        Test-Path -LiteralPath $foreign | Should -BeTrue
        Get-VaultEntry -Path $vault -Name 'Extra' -AsPlainText | Should -Be 'e'
    }

    It 'leaves the source file in place when nothing differs' {
        Import-Vault -Path $vault -Source $foreign -ReplaceAll | Out-Null
        Import-Vault -Path $vault -Source $foreign | Out-Null

        Test-Path -LiteralPath $foreign | Should -BeTrue
    }
}

Describe 'Concurrent writers' {
    It 'loses no entry across two processes' {
        $folder = Initialize-TestFolder
        try {
            $vault = Join-Path $folder 'secrets.local.lv'
            Set-VaultEntry -Path $vault -Name 'seed' -Value (ConvertTo-TestSecret 's')
            $writer = {
                param($Module, $Vault, $Prefix)
                Import-Module $Module
                for ($i = 0; $i -lt 50; $i++) {
                    $value = [securestring]::new()
                    $value.AppendChar('x')
                    Set-VaultEntry -Path $Vault -Name "$Prefix$i" -Value $value -InformationAction SilentlyContinue
                }
            }
            $module = Join-Path $env:LOCALVAULT_MODULE_PATH 'LocalVault.psd1'
            $jobs = 'a', 'b' | ForEach-Object { Start-Job -ScriptBlock $writer -ArgumentList $module, $vault, $_ }
            $jobs | Wait-Job | Receive-Job -ErrorAction Stop
            $jobs | Remove-Job

            @(Get-VaultEntryList -Path $vault -WarningAction SilentlyContinue).Count | Should -Be 101
        }
        finally { Clear-TestFolder $folder }
    }
}

Describe 'Mailbox names' {
    BeforeEach { $script:folder = Initialize-TestFolder }
    AfterEach { Clear-TestFolder $script:folder }

    It 'migrates a clixml vault keyed by email addresses' {
        Initialize-LegacyVault -Folder $folder -Values @{ 'john.doe@example.com' = 'm1'; 'jane+test@example.org' = 'm2' } | Out-Null
        $vault = Join-Path $folder 'secrets.local.lv'

        Set-VaultEntry -Path $vault -Name 'other' -Value (ConvertTo-TestSecret 'x') 6>$null

        Get-VaultEntry -Path $vault -Name 'john.doe@example.com' -AsPlainText | Should -Be 'm1'
        Get-VaultEntry -Path $vault -Name 'jane+test@example.org' -AsPlainText | Should -Be 'm2'
    }
}