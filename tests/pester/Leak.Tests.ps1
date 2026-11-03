#Requires -Version 7.6
Set-StrictMode -Version Latest

BeforeAll {
    . (Join-Path $PSScriptRoot 'TestSupport.ps1')
    $script:canary = 'CANARY-SECRET-7f3a91'
    $script:passCanary = 'CANARY-PASSPHRASE-55e1'
    $script:capture = {
        param([scriptblock]$Action)
        $VerbosePreference = 'Continue'
        $DebugPreference = 'Continue'
        $InformationPreference = 'Continue'
        $WarningPreference = 'Continue'
        $records = [System.Collections.Generic.List[object]]::new()
        try {
            & $Action *>&1 | ForEach-Object { $records.Add($_) }
        }
        catch {
            $records.Add($_)
        }
        ($records | ForEach-Object {
                $_ | Out-String
                if ($_ -is [Management.Automation.ErrorRecord]) { $_.Exception | Out-String }
            }) -join "`n"
    }
}

Describe 'No secret value in any stream' {
    BeforeAll {
        $script:folder = Initialize-TestFolder
        $script:vault = Join-Path $script:folder 'secrets.local.lv'
        $script:export = Join-Path $script:folder 'v.export'
        $script:pass = ConvertTo-TestSecret $script:passCanary
        Set-VaultEntry -Path $vault -Name 'common/token' -Value (ConvertTo-TestSecret $canary)
    }
    AfterAll {
        Remove-Item Env:\LV_LEAK -ErrorAction SilentlyContinue
        Clear-TestFolder $script:folder
    }

    It '<Name> writes neither the secret nor the passphrase' -ForEach @(
        @{ Name = 'Set-VaultEntry'; Action = { Set-VaultEntry -Path $vault -Name 'other' -Value (ConvertTo-TestSecret $canary) -Verbose -Debug } }
        @{ Name = 'Get-VaultEntryList'; Action = { Get-VaultEntryList -Path $vault -Verbose -Debug } }
        @{ Name = 'Get-VaultEntry'; Action = { Get-VaultEntry -Path $vault -Name 'common/token' -Verbose -Debug } }
        @{ Name = 'Resolve-VaultEntry'; Action = { Resolve-VaultEntry -Path $vault -Name 'token' -Scope 'prod' -Verbose -Debug } }
        @{ Name = 'Use-VaultEnv'; Action = { Use-VaultEnv -Path $vault -Map @{ LV_LEAK = 'common/token' } -Verbose -Debug } }
        @{ Name = 'Export-Vault'; Action = { Export-Vault -Path $vault -Destination $export -Passphrase $pass -Force -Verbose -Debug } }
        @{ Name = 'Import-Vault'; Action = { Import-Vault -Path $vault -Source $export -Passphrase $pass -Verbose -Debug } }
        @{ Name = 'Import-Vault conflict'; Action = {
                Set-VaultEntry -Path $vault -Name 'common/token' -Value (ConvertTo-TestSecret ($canary + '-changed'))
                Import-Vault -Path $vault -Source $export -Passphrase $pass -Verbose -Debug } }
        @{ Name = 'Import-Vault wrong passphrase'; Action = {
                Import-Vault -Path $vault -Source $export -Passphrase (ConvertTo-TestSecret ($passCanary + 'x')) -Verbose -Debug } }
        @{ Name = 'Set-VaultEntry bad name'; Action = { Set-VaultEntry -Path $vault -Name 'bad name' -Value (ConvertTo-TestSecret $canary) -Verbose -Debug } }
    ) {
        $text = & $capture $Action

        $text | Should -Not -Match $canary
        $text | Should -Not -Match $passCanary
    }

    It 'keeps the secret out of the vault file in plain text' {
        $raw = [IO.File]::ReadAllText($vault)

        $raw | Should -Not -Match $canary
        $raw | Should -Not -Match ([Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($canary)))
    }
}
