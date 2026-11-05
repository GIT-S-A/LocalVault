#Requires -Version 7.6
Set-StrictMode -Version Latest

BeforeAll {
    . (Join-Path $PSScriptRoot 'TestSupport.ps1')
    $script:seed = 'JBSWY3DPEHPK3PXP'
    $script:runChild = {
        param([string]$Command, [string]$InputText)
        $prefix = "Import-Module '$(Join-Path $env:LOCALVAULT_MODULE_PATH 'LocalVault.psd1')'; "
        $output = $InputText | pwsh -NoProfile -NonInteractive -Command ($prefix + "try { $Command } catch { Write-Error `$_; exit 1 }; exit `$LASTEXITCODE") 2>&1
        [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = ($output | Out-String) }
    }
    $script:currentCode = {
        param([string]$Seed)
        [LocalVault.Core.TotpSecret]::Parse($Seed).Code([DateTimeOffset]::UtcNow)
    }
}

Describe 'Credential entries' {
    BeforeEach {
        $script:folder = Initialize-TestFolder
        $script:vault = Join-Path $script:folder 'secrets.local.lv'
    }
    AfterEach { Clear-TestFolder $script:folder }

    It 'keeps user, password, TOTP and URLs as one entry and reads each part' {
        Set-VaultEntry -Path $vault -Name 'gh' -Credential -UserName 'jdoe' -Password (ConvertTo-TestSecret 'pa55word') -Totp (ConvertTo-TestSecret "otpauth://totp/GitHub?secret=$seed") -Url 'https://github.com', 'https://api.github.com' 6>$null

        Get-VaultEntry -Path $vault -Name 'gh' -AsPlainText | Should -Be 'pa55word'
        Get-VaultEntry -Path $vault -Name 'gh#user' | Should -Be 'jdoe'
        Get-VaultEntry -Path $vault -Name 'gh#url2' | Should -Be 'https://api.github.com'
        Get-VaultEntry -Path $vault -Name 'gh#totp' -AsPlainText | Should -Match '^\d{6}$'
        Get-VaultEntry -Path $vault -Name 'gh#totp' | Should -BeOfType [securestring]
        $credential = Get-VaultEntry -Path $vault -Name 'gh' -AsCredential
        $credential.UserName | Should -Be 'jdoe'
        $credential.GetNetworkCredential().Password | Should -Be 'pa55word'
        $listed = Get-VaultEntryList -Path $vault -WarningAction SilentlyContinue
        $listed.Kind | Should -Be 'credential'
        $listed.User | Should -Be 'jdoe'
        ($listed | ConvertTo-Json) | Should -Not -Match "pa55word|$seed"
    }

    It 'asks for every part on redirected input, one line each' {
        $result = & $runChild "lv set gh --credential --path '$vault'" "jdoe`npa55word`n$seed`nhttps://one`nhttps://two`n"

        $result.ExitCode | Should -Be 0 -Because $result.Output
        Get-VaultEntry -Path $vault -Name 'gh#user' | Should -Be 'jdoe'
        Get-VaultEntry -Path $vault -Name 'gh' -AsPlainText | Should -Be 'pa55word'
        Get-VaultEntry -Path $vault -Name 'gh#url2' | Should -Be 'https://two'
        $result.Output | Should -Not -Match 'pa55word'
    }

    It 'turns a secret into a credential, keeping its value, and changes one part at a time' {
        Set-VaultEntry -Path $vault -Name 'gh' -Value (ConvertTo-TestSecret 'kept-1')

        (& $runChild "lv set gh#user --path '$vault'" 'jdoe').ExitCode | Should -Be 0
        (& $runChild "lv set gh#url --path '$vault'" 'https://one').ExitCode | Should -Be 0
        Set-VaultEntry -Path $vault -Name 'gh' -Value (ConvertTo-TestSecret 'new-2') 6>$null

        $entry = Get-VaultEntryList -Path $vault -WarningAction SilentlyContinue
        $entry.Kind | Should -Be 'credential'
        $entry.User | Should -Be 'jdoe'
        Get-VaultEntry -Path $vault -Name 'gh' -AsPlainText | Should -Be 'new-2'
        Get-VaultEntry -Path $vault -Name 'gh#url' | Should -Be 'https://one'

        (& $runChild "lv set gh#user --path '$vault'" '').ExitCode | Should -Be 0
        { Get-VaultEntry -Path $vault -Name 'gh#user' } | Should -Throw '*no user name*'
    }

    It 'refuses parts on a secret and unknown parts' {
        Set-VaultEntry -Path $vault -Name 'plain' -Value (ConvertTo-TestSecret 'x')

        { Get-VaultEntry -Path $vault -Name 'plain#user' } | Should -Throw '*not a credential*'
        { Get-VaultEntry -Path $vault -Name 'plain#pin' } | Should -Throw '*#totp*'
        { Get-VaultEntry -Path $vault -Name 'plain' -AsCredential } | Should -Throw '*no user name*'
    }

    It 'prints a user name and a URL with lv get' {
        Set-VaultEntry -Path $vault -Name 'gh' -Credential -UserName 'jdoe' -Password (ConvertTo-TestSecret 'pa55word') -Url 'https://one' 6>$null

        $arguments = 'get', 'gh#user', '--path', $vault
        lv @arguments | Should -Be 'jdoe'
        $arguments = 'get', 'gh#url', '--path', $vault
        lv @arguments | Should -Be 'https://one'
    }

    It 'sets environment variables from parts' {
        Set-VaultEntry -Path $vault -Name 'gh' -Credential -UserName 'jdoe' -Password (ConvertTo-TestSecret 'pa55word') -Totp (ConvertTo-TestSecret $seed) 6>$null
        try {
            Use-VaultEnv -Path $vault -Map @{ LV_TEST_USER = 'gh#user'; LV_TEST_PASS = 'gh'; LV_TEST_CODE = 'gh#totp' } 6>$null

            $env:LV_TEST_USER | Should -Be 'jdoe'
            $env:LV_TEST_PASS | Should -Be 'pa55word'
            $env:LV_TEST_CODE | Should -Match '^\d{6}$'
        }
        finally {
            Remove-Item Env:LV_TEST_USER, Env:LV_TEST_PASS, Env:LV_TEST_CODE -ErrorAction SilentlyContinue
        }
    }

    It 'upgrades an older vault on its first write' {
        Set-VaultEntry -Path $vault -Name 'a' -Value (ConvertTo-TestSecret '1')
        $sealed = Get-Content -LiteralPath $vault -Raw
        $body = [LocalVault.Core.Envelope]::OpenLocal($sealed)
        $old = [Text.Encoding]::UTF8.GetString($body).Replace('"schema":5', '"schema":2')
        [IO.File]::WriteAllText($vault, [LocalVault.Core.Envelope]::SealLocal([Text.Encoding]::UTF8.GetBytes($old)))

        $said = Set-VaultEntry -Path $vault -Name 'b' -Value (ConvertTo-TestSecret '2') 6>&1 | Out-String

        $said | Should -Match 'from schema 2 to schema 5'
    }
}

Describe 'Credentials in templates' {
    BeforeEach {
        $script:folder = Initialize-TestFolder
        $script:repository = Join-Path $script:folder 'repo'
        git init --quiet $script:repository
        Set-Content -LiteralPath (Join-Path $script:repository '.gitignore') -Value "secrets.local.*`n.env"
        $script:vault = Join-Path $script:repository 'secrets.local.lv'
        $script:env = Join-Path $script:repository '.env'
        Set-VaultEntry -Path $vault -Name 'gh' -Credential -UserName 'jdoe-user' -Password (ConvertTo-TestSecret 'gh-password-5') -Totp (ConvertTo-TestSecret $seed) 6>$null
        Set-Content -LiteralPath $env -Value "USER=jdoe-user`nPASS=gh-password-5" -NoNewline
        Push-Location $script:repository
    }
    AfterEach {
        Pop-Location
        Clear-TestFolder $script:folder
    }

    It 'renders parts, the TOTP code included, and follows a new user name' {
        Add-VaultTemplate -File '.env' 6>$null | Out-Null
        Set-Content -LiteralPath "$env.template" -Value "USER={{gh#user}}`nPASS={{gh}}`nCODE={{gh#totp}}" -NoNewline

        Write-VaultTemplate | Out-Null
        $written = Get-Content -LiteralPath $env -Raw
        $written | Should -Match "^USER=jdoe-user`nPASS=gh-password-5`nCODE=\d{6}$"

        Set-Content -LiteralPath $env -Value ($written -replace 'jdoe-user', 'other-user') -NoNewline
        Update-VaultTemplate -AcceptChanges 6>$null

        Get-VaultEntry -Path $vault -Name 'gh#user' | Should -Be 'other-user'
        Get-VaultEntry -Path $vault -Name 'gh' -AsPlainText | Should -Be 'gh-password-5'
        Get-Content -LiteralPath "$env.template" -Raw | Should -Be "USER={{gh#user}}`nPASS={{gh}}`nCODE={{gh#totp}}"
    }
}

Describe 'Credentials in a store' {
    BeforeEach {
        $script:folder = Initialize-TestFolder
        $script:savedLocalAppData = $env:LOCALAPPDATA
        $env:LOCALAPPDATA = Join-Path $script:folder 'appdata'
        $script:master = 'pester master 1'
        $script:database = Join-Path $script:folder 'team.kdbx'
        Initialize-TestKeePass -Path $script:database -Master $script:master
        Register-VaultStore -Name 'team' -Target $script:database | Out-Null
        $script:mine = Join-Path $script:folder 'me/proj/secrets.local.lv'
        $script:theirs = Join-Path $script:folder 'them/proj/secrets.local.lv'
    }
    AfterEach {
        $env:LOCALAPPDATA = $script:savedLocalAppData
        Clear-TestFolder $script:folder
    }

    It 'saves a credential and a teammate loads every part' {
        Set-VaultEntry -Path $mine -Name 'gh' -Credential -UserName 'jdoe' -Password (ConvertTo-TestSecret 'pa55word') -Totp (ConvertTo-TestSecret $seed) -Url 'https://github.com' 6>$null

        (& $runChild "lv save --path '$mine'" $master).ExitCode | Should -Be 0
        $loaded = & $runChild "lv load --path '$theirs'" $master
        $loaded.ExitCode | Should -Be 0 -Because $loaded.Output

        Get-VaultEntry -Path $theirs -Name 'gh#user' | Should -Be 'jdoe'
        Get-VaultEntry -Path $theirs -Name 'gh' -AsPlainText | Should -Be 'pa55word'
        Get-VaultEntry -Path $theirs -Name 'gh#url' | Should -Be 'https://github.com'
        $before = & $currentCode $seed
        $code = Get-VaultEntry -Path $theirs -Name 'gh#totp' -AsPlainText
        $code | Should -BeIn @($before, (& $currentCode $seed))
        (Get-VaultEntryList -Path $mine -WarningAction SilentlyContinue).Sync | Should -Be 'Saved'
    }
}
