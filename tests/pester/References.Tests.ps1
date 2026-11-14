#Requires -Version 7.6
Set-StrictMode -Version Latest

BeforeAll {
    . (Join-Path $PSScriptRoot 'TestSupport.ps1')
    $script:master = 'pester master 1'
    $script:runInChild = {
        param([string]$Command, [string]$Master = $script:master)
        $prefix = "Import-Module '$(Join-Path $env:LOCALVAULT_MODULE_PATH 'LocalVault.psd1')'; "
        $output = $Master | pwsh -NoProfile -NonInteractive -Command ($prefix + "try { $Command } catch { Write-Error `$_; exit 1 }; exit `$LASTEXITCODE") 2>&1
        [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = ($output | Out-String) }
    }

    function Open-TestKeePass {
        param([Parameter(Mandatory)][string]$Path)
        $key = [KeePassLib.Keys.CompositeKey]::new()
        $key.AddUserKey([KeePassLib.Keys.KcpPassword]::new($script:master))
        $database = [KeePassLib.PwDatabase]::new()
        $database.Open([KeePassLib.Serialization.IOConnectionInfo]::FromPath($Path), $key, $null)
        $database
    }

    function Add-TestKeePassEntry {
        param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Group, [Parameter(Mandatory)][string]$Title, [Parameter(Mandatory)][string]$Value)
        $database = Open-TestKeePass $Path
        try {
            $entry = [KeePassLib.PwEntry]::new($true, $true)
            $entry.Strings.Set('Title', [KeePassLib.Security.ProtectedString]::new($false, $Title))
            $entry.Strings.Set('Password', [KeePassLib.Security.ProtectedString]::new($true, $Value))
            $database.RootGroup.FindCreateGroup($Group, $true).AddEntry($entry, $true)
            $database.Save($null)
            $entry.Uuid.ToHexString()
        }
        finally { $database.Close() }
    }

    function Get-TestKeePassField {
        param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Title, [Parameter(Mandatory)][string]$Field)
        $database = Open-TestKeePass $Path
        try {
            $entry = $database.RootGroup.GetEntries($true) | Where-Object { $_.Strings.ReadSafe('Title') -eq $Title -and $_.ParentGroup.Name -ne 'Internet' }
            $entry.Strings.ReadSafe($Field)
        }
        finally { $database.Close() }
    }
}

Describe 'Personal values taken from KeePass references' {
    BeforeEach {
        $script:folder = Initialize-TestFolder
        $script:savedLocalAppData = $env:LOCALAPPDATA
        $env:LOCALAPPDATA = Join-Path $script:folder 'appdata'
        $script:repository = Join-Path $script:folder 'repo'
        git init --quiet $script:repository
        Set-Content -LiteralPath (Join-Path $script:repository '.gitignore') -Value 'secrets.local.*'
        $script:projectDb = Join-Path $script:folder 'project.kdbx'
        $script:personalDb = Join-Path $script:folder 'personal.kdbx'
        Initialize-TestKeePass -Path $script:projectDb -Master $script:master
        Initialize-TestKeePass -Path $script:personalDb -Master $script:master
        $script:vault = Join-Path $script:repository 'secrets.local.lv'
        Set-VaultEntry -Path $script:vault -Name 'shared' -Value (ConvertTo-TestSecret 's-1') 6>$null
        Push-Location $script:repository
        try {
            lv @('store', 'add', 'proj', $script:projectDb, '--project', '--default') 6>$null | Out-Null
            lv @('store', 'add', 'mine', $script:personalDb, '--personal') 6>$null | Out-Null
            lv @('require', 'os', '--note', 'your token') 6>$null
        }
        finally { Pop-Location }
        $script:uuid = Add-TestKeePassEntry -Path $script:personalDb -Group 'Internet' -Title 'GitHub' -Value 'token-1'
    }

    AfterEach {
        $env:LOCALAPPDATA = $script:savedLocalAppData
        Clear-TestFolder $script:folder
    }

    It 'takes the value from the personal store and saves only a reference to it' {
        $set = & $script:runInChild "Set-Location '$($script:repository)'; lv set os --ref Internet/GitHub"
        $set.ExitCode | Should -Be 0 -Because $set.Output
        $set.Output | Should -Match "os comes from KeePass entry 'Internet/GitHub' in personal store 'mine'"
        Get-VaultEntry -Path $script:vault -Name 'os' -AsPlainText | Should -Be 'token-1'

        $saved = & $script:runInChild "Set-Location '$($script:repository)'; lv save" "$($script:master)`n$($script:master)"
        $saved.ExitCode | Should -Be 0 -Because $saved.Output
        $saved.Output | Should -Not -Match 'token-1'
        Get-TestKeePassField -Path $script:personalDb -Title 'os' -Field 'Password' | Should -Be "{REF:P@I:$($script:uuid)}"
    }

    It 'keeps a local change on this PC and refuses to save it' {
        $set = & $script:runInChild "Set-Location '$($script:repository)'; lv set os --ref GitHub"
        $set.ExitCode | Should -Be 0 -Because $set.Output
        & $script:runInChild "Set-Location '$($script:repository)'; lv save" "$($script:master)`n$($script:master)" | Out-Null

        $warning = Set-VaultEntry -Path $script:vault -Name 'os' -Value (ConvertTo-TestSecret 'local') 6>$null 3>&1 | Out-String
        $warning | Should -Match 'os changed on this PC only'
        $warning | Should -Match "Its value comes from KeePass entry 'Internet/GitHub' \(store 'mine'\); lv save won't change that entry"
        $warning | Should -Match 'To use another source here: lv reset os'

        $saved = & $script:runInChild "Set-Location '$($script:repository)'; lv save" "$($script:master)`n$($script:master)"
        $saved.ExitCode | Should -Be 0 -Because $saved.Output
        $saved.Output | Should -Match "your value of 'os' not saved: it changed on this PC only"
        Get-TestKeePassField -Path $script:personalDb -Title 'os' -Field 'Password' | Should -Be "{REF:P@I:$($script:uuid)}"
    }

    It 'refuses a reference for a shared entry' {
        { Set-VaultEntry -Path $script:vault -Name 'shared' -Reference 'GitHub' } | Should -Throw '*not a personal value*'
    }
}
