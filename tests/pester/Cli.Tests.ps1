#Requires -Version 7.6
Set-StrictMode -Version Latest

BeforeAll {
    . (Join-Path $PSScriptRoot 'TestSupport.ps1')
    $script:runChild = {
        param([string]$Command, [string]$InputText)
        $prefix = "Import-Module '$(Join-Path $env:LOCALVAULT_MODULE_PATH 'LocalVault.psd1')'; "
        $output = $InputText | pwsh -NoProfile -NonInteractive -Command ($prefix + $Command + '; exit $LASTEXITCODE') 2>&1
        [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = ($output | Out-String) }
    }
}

Describe 'lv' {
    BeforeEach {
        $script:folder = Initialize-TestFolder
        $script:vault = Join-Path $script:folder 'secrets.local.lv'
        Set-VaultEntry -Path $vault -Name 'a' -Value (ConvertTo-TestSecret 'cli-1')
    }
    AfterEach { Clear-TestFolder $script:folder }

    It 'is exported as an alias of Invoke-LocalVault' {
        (Get-Alias lv).Definition | Should -Be 'Invoke-LocalVault'
    }

    It 'lists names without values' {
        $arguments = 'list', '--path', $vault
        $text = lv @arguments 3>$null | Out-String

        $text | Should -Match '\ba\b'
        $text | Should -Not -Match 'cli-1'
        $LASTEXITCODE | Should -Be 0
    }

    It 'reads a value from redirected input' {
        $result = & $runChild "lv set b --path '$vault'" 'from-stdin'

        $result.ExitCode | Should -Be 0 -Because $result.Output
        Get-VaultEntry -Path $vault -Name 'b' -AsPlainText | Should -Be 'from-stdin'
    }

    It 'prints a value only into a pipe' {
        $arguments = 'get', 'a', '--path', $vault
        lv @arguments | ForEach-Object { $_ } | Should -Be 'cli-1'

        $result = & $runChild "lv get a --path '$vault'" ''
        $result.Output | Should -Match 'cli-1'
    }

    It 'sets environment variables for the window' {
        try {
            $arguments = 'env', 'LV_CLI_A=a', '--path', $vault
            lv @arguments
            $env:LV_CLI_A | Should -Be 'cli-1'
        }
        finally { Remove-Item Env:\LV_CLI_A -ErrorAction SilentlyContinue }
    }

    It 'removes an entry' {
        $arguments = 'rm', 'a', '--path', $vault
        lv @arguments

        { Get-VaultEntry -Path $vault -Name 'a' } | Should -Throw
    }

    It 'exits 2 on unresolved import conflicts and 1 on errors' {
        $export = Join-Path $folder 'v.export'
        Export-Vault -Path $vault -Destination $export -Passphrase (ConvertTo-TestSecret 'cli passphrase')
        Set-VaultEntry -Path $vault -Name 'a' -Value (ConvertTo-TestSecret 'changed')

        $conflict = & $runChild "lv import '$export' --path '$vault'" 'cli passphrase'
        $conflict.ExitCode | Should -Be 2 -Because $conflict.Output

        $replaced = & $runChild "lv import '$export' --path '$vault' --replace a" 'cli passphrase'
        $replaced.ExitCode | Should -Be 0 -Because $replaced.Output
        Get-VaultEntry -Path $vault -Name 'a' -AsPlainText | Should -Be 'cli-1'

        (& $runChild "lv nonsense" '').ExitCode | Should -Be 1
        (& $runChild "lv rm 'bad name!' --path '$vault'" '').ExitCode | Should -Be 1
    }

    It 'refuses extra words, such as a value typed after the name' {
        $result = & $runChild "lv set b typed-value --path '$vault'" 'from-stdin'

        $result.ExitCode | Should -Be 1
        $result.Output | Should -Match 'Too many words'
        { Get-VaultEntry -Path $vault -Name 'b' } | Should -Throw
    }

    It 'prints the commands and the vault when run alone' {
        $text = lv | Out-String
        $LASTEXITCODE | Should -Be 0
        $text | Should -Match 'set <name> \[--file <path>\] \[--local\]'
        $text | Should -Not -Match 'kp-catalog|recover|unshare'

        $arguments = '--path', $vault
        lv @arguments | Out-String | Should -Match ([regex]::Escape("Vault: $vault (exists)"))

        $missing = Join-Path $folder 'none.lv'
        $arguments = '--help', '--path', $missing
        lv @arguments | Out-String | Should -Match ([regex]::Escape("Vault: $missing (not created yet)"))
        Test-Path -LiteralPath $missing | Should -BeFalse
    }

    It 'says when there is nothing to list' {
        $missing = Join-Path $folder 'none.lv'
        $arguments = 'list', '--path', $missing
        $output = lv @arguments 6>&1 3>$null
        "$output" | Should -Be "No vault at $missing."

        $arguments = 'list', 'prod', '--path', $vault
        "$(lv @arguments 6>&1 3>$null)" | Should -Be "No entries in scope 'prod' of $vault."

        Remove-VaultEntry -Path $vault -Name 'a'
        $arguments = 'list', '--path', $vault
        "$(lv @arguments 6>&1 3>$null)" | Should -Be "No entries in $vault."
    }

    It 'prints the usage of a command used with a wrong syntax' -ForEach @(
        @{ Line = @('store', 'add'); Usage = 'lv store \[list \| add \[<name>\] <target>' }
        @{ Line = @('set'); Usage = 'lv set <name> \[--file <path> \| --credential\] \[--local\]' }
        @{ Line = @('list', '--overwrite'); Usage = 'lv list \[<scope>\]' }
        @{ Line = @('store', 'copy'); Usage = 'lv store .*copy <target \| name>' }
        @{ Line = @('store', 'use', 'a', '--project'); Usage = 'lv store ' }
        @{ Line = @('store', 'bogus'); Usage = 'lv store ' }
        @{ Line = @('mark', 'a', 'public'); Usage = 'lv mark <name> shared\|local' }
        @{ Line = @('export'); Usage = 'lv export <file> \[--overwrite\]' }
        @{ Line = @('restore', '--force'); Usage = 'lv restore ' }
        @{ Line = @('import', 'a', '--store'); Usage = 'lv import <file>' }
    ) {
        $text = "$(lv @Line 2>&1)"
        $LASTEXITCODE | Should -Be 1
        $text | Should -Match "Usage: $Usage"
    }

    It 'defines a store from its target alone' {
        $database = Join-Path $folder 'it.kdbx'
        Set-Content -LiteralPath $database -Value 'x'
        $arguments = 'store', 'add', $database, '--default', '--path', $vault
        lv @arguments | Out-Null

        $registered = Get-VaultStore -Path $vault
        $registered.Name | Should -Be 'it'
        $registered.Scope | Should -Be 'Computer'
        $registered.Default | Should -BeTrue
        $arguments = 'store', 'rm', 'it', '--path', $vault
        lv @arguments
        Get-VaultStore -Path $vault | Should -BeNullOrEmpty

        $arguments = 'store', 'add', 'az', 'azkv:git-saas-prod', '--path', $vault
        "$(lv @arguments 2>&1)" | Should -Match "type 'azkv' are not supported yet"
    }
    It 'completes commands, subcommands, options and entry names' {
        $complete = { param([string]$Line) @((TabExpansion2 -inputScript $Line -cursorColumn $Line.Length).CompletionMatches.CompletionText) }

        & $complete 'lv s' | Should -Be @('save', 'set', 'ssh-key', 'start', 'store')
        & $complete 'lv store ' | Should -Be @('list', 'add', 'set', 'rm', 'use', 'diff', 'copy')
        & $complete 'lv export --' | Should -Be @('--overwrite', '--path')
        & $complete "lv mark --path '$vault' a " | Should -Be @('shared', 'local', 'hello', 'nohello')
        & $complete "lv get --path '$vault' " | Should -Be @('a')
    }
    It 'says where the project stands and what to run next' {
        $arguments = 'start', '--path', $vault
        $text = lv @arguments | Out-String
        $text | Should -Match 'Stores:  none'
        $text | Should -Match ([regex]::Escape("lv store add <name> <path to the team .kdbx> --default"))
        $text | Should -Match ([regex]::Escape("lv list --path $vault"))

        $database = Join-Path $folder 'team.kdbx'
        Set-Content -LiteralPath $database -Value 'x'
        Register-VaultStore -Target $database -Default | Out-Null
        $arguments = 'start', '--path', $vault
        $text = lv @arguments | Out-String
        $text | Should -Match 'lv save +send 1 change'
        $text | Should -Match "store 'team' \(Computer\)"
        $text | Should -Not -Match '--unlock-with'
        Unregister-VaultStore -Name 'team'
    }

    It 'updates with the installer the module carries' {
        $copy = Join-Path $folder 'module'
        Copy-Item -LiteralPath $env:LOCALVAULT_MODULE_PATH -Destination $copy -Recurse
        Set-Content -LiteralPath (Join-Path $copy 'install.ps1') -Value 'param([switch]$Latest, [string]$Version, [switch]$Downgrade) "called latest=$Latest version=$Version downgrade=$Downgrade"'
        $run = { param($Arguments) pwsh -NoProfile -NonInteractive -Command "Import-Module '$(Join-Path $copy 'LocalVault.psd1')'; lv $Arguments" 2>&1 | Out-String }

        & $run 'update' | Should -Match 'called latest=True version= downgrade=False'
        & $run 'update v1.0.0-beta5 --downgrade' | Should -Match 'called latest=False version=v1.0.0-beta5 downgrade=True'

        Remove-Item -LiteralPath (Join-Path $copy 'install.ps1')
        & $run 'update' | Should -Match 'has no installer'
    }

    It 'prints its version' {
        $manifest = Import-PowerShellDataFile -LiteralPath (Join-Path $env:LOCALVAULT_MODULE_PATH 'LocalVault.psd1')
        $release = "$($manifest.ModuleVersion)-$($manifest.PrivateData.PSData['Prerelease'])"
        lv --version | Should -Be "LocalVault $release"
        (lv | Out-String) | Should -Match ([regex]::Escape("LocalVault $release"))
        (lv --help | Out-String) | Should -Match ([regex]::Escape("LocalVault $release"))
    }

    It 'rejects unknown options' {
        $arguments = 'list', '--bogus', '--path', $vault
        "$(lv @arguments 2>&1)" | Should -BeLike '*Unknown option --bogus*'
        $LASTEXITCODE | Should -Be 1
    }
}
