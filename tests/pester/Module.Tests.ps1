#Requires -Version 7.6
Set-StrictMode -Version Latest

BeforeAll {
    $script:modulePath = $env:LOCALVAULT_MODULE_PATH
    $script:manifestPath = Join-Path $script:modulePath 'LocalVault.psd1'
}

Describe 'LocalVault module' {
    It 'has a valid manifest' {
        { Test-ModuleManifest -Path $script:manifestPath -ErrorAction Stop } | Should -Not -Throw
    }

    It 'requires PowerShell 7.6 Core' {
        $manifest = Import-PowerShellDataFile -LiteralPath $script:manifestPath
        $manifest.PowerShellVersion | Should -Be '7.6'
        $manifest.CompatiblePSEditions | Should -Be @('Core')
    }

    It 'has a prerelease label of letters and digits' {
        $prerelease = (Import-PowerShellDataFile -LiteralPath $script:manifestPath).PrivateData.PSData['Prerelease']
        if ($prerelease) {
            $prerelease | Should -MatchExactly '^[A-Za-z0-9]+$'
        }
    }

    It 'imports from the build output with the manifest version' {
        $module = Import-Module $script:manifestPath -Force -PassThru
        try {
            $module.Version | Should -Be ([version](Split-Path -Leaf $script:modulePath))
        }
        finally {
            Remove-Module $module
        }
    }
}
