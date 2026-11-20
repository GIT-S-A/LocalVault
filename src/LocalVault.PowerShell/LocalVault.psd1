@{
    RootModule           = 'LocalVault.PowerShell.dll'
    ModuleVersion        = '1.0.0'
    GUID                 = '2a3606de-c8ef-4b0f-aac5-ce7b3ee85c88'
    Author               = 'Stephane Grobety'
    CompanyName          = 'GIT SA'
    Copyright            = '(c) GIT SA'
    Description          = 'Machine-local, DPAPI-sealed secret store per project, with passphrase-protected export and shared entries in a store (KeePass).'
    PowerShellVersion    = '7.6'
    CompatiblePSEditions = @('Core')
    NestedModules        = @('LocalVault.Cli.psm1')
    FunctionsToExport    = @('Invoke-LocalVault')
    CmdletsToExport      = @(
        'Get-VaultEntryList', 'Get-VaultEntry', 'Get-VaultHelloStatus', 'Copy-VaultEntry', 'Set-VaultEntry', 'Remove-VaultEntry', 'Resolve-VaultEntry',
        'Use-VaultEnv', 'Restore-VaultFile', 'Export-Vault', 'Import-Vault', 'Clear-Vault',
        'Export-VaultToStore', 'Import-VaultFromStore', 'Compare-VaultWithStore', 'Copy-VaultToStore', 'Get-VaultStoreStatus', 'Set-VaultProjectStore',
        'Register-VaultStore', 'Set-VaultStore', 'Unregister-VaultStore', 'Get-VaultStore',
        'Add-VaultTemplate', 'Update-VaultTemplate', 'Write-VaultTemplate', 'Clear-VaultTemplate', 'Remove-VaultTemplate', 'Get-VaultTemplate',
        'New-VaultSshKey', 'Set-VaultSshKeyHost', 'Get-VaultSshKey', 'Get-VaultSshKeyPath', 'Set-VaultSshTools', 'Invoke-VaultSsh', 'Invoke-VaultScp', 'Set-VaultSshInclude',
        'Get-VaultUserSecretList', 'Set-VaultUserSecret', 'Get-VaultUserSecret', 'Remove-VaultUserSecret', 'Write-VaultUserSecret', 'Clear-VaultUserSecret'
    )
    VariablesToExport    = @()
    AliasesToExport      = @('lv')
    PrivateData          = @{
        PSData = @{
            Prerelease = 'beta18'
            Tags       = @('Secrets', 'DPAPI', 'Vault', 'Windows')
            ProjectUri = 'https://github.com/GIT-S-A/LocalVault'
            LicenseUri = 'https://github.com/GIT-S-A/LocalVault/blob/main/LICENSE'
        }
    }
}
