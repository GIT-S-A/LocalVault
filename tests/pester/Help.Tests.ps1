#Requires -Version 7.6
Set-StrictMode -Version Latest

BeforeDiscovery {
    $manifest = Import-PowerShellDataFile -LiteralPath (Join-Path $env:LOCALVAULT_MODULE_PATH 'LocalVault.psd1')
    $script:commands = @($manifest.CmdletsToExport) + @($manifest.FunctionsToExport) |
        ForEach-Object { @{ Name = $_ } }
}

BeforeAll {
    . (Join-Path $PSScriptRoot 'TestSupport.ps1')
    $script:docs = Join-Path $PSScriptRoot '../../docs/LocalVault'
    $script:common = [System.Management.Automation.PSCmdlet]::CommonParameters +
        [System.Management.Automation.PSCmdlet]::OptionalCommonParameters
}

Describe 'Help for <Name>' -ForEach $script:commands {
    BeforeAll {
        $script:help = Get-Help -Name $Name -Full
        $script:command = Get-Command -Name $Name
        $parameters = $script:help.parameters
        $script:documented = @(if ($parameters.PSObject.Properties['parameter']) { $parameters.parameter })
    }

    It 'has a synopsis that starts with a verb' {
        $script:help.Synopsis | Should -Match '^[A-Z][a-z]+s '
        $script:help.Synopsis | Should -Not -Match '\{\{'
    }

    It 'has a description' {
        ($script:help.description.Text -join "`n") | Should -Match '\S'
    }

    It 'has at least one example' {
        @($script:help.examples.example).Count | Should -BeGreaterThan 0
    }

    It 'describes every parameter' {
        foreach ($name in $script:command.Parameters.Keys | Where-Object { $_ -notin $script:common }) {
            $parameter = $script:documented | Where-Object Name -EQ $name
            $parameter | Should -Not -BeNullOrEmpty -Because "$name must be documented"
            ($parameter.description.Text -join "`n") | Should -Match '\S' -Because "$name needs a description"
        }
    }

    It 'documents no parameter the command lacks' {
        foreach ($parameter in $script:documented) {
            $script:command.Parameters.Keys | Should -Contain $parameter.Name
        }
    }

    It 'has a page on the module page' {
        Get-Content -LiteralPath (Join-Path $script:docs 'LocalVault.md') -Raw |
            Should -Match ([regex]::Escape("### [$Name]($Name.md)"))
    }
}

Describe 'about_LocalVault' {
    It 'is shipped with the module' {
        (Get-Help -Name about_LocalVault | Out-String) | Should -Match 'Short description'
    }
}
