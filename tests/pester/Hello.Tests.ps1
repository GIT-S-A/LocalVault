#Requires -Version 7.6
Set-StrictMode -Version Latest

BeforeAll {
    . (Join-Path $PSScriptRoot 'TestSupport.ps1')
    $script:seed = 'JBSWY3DPEHPK3PXP'
}

Describe 'Windows Hello entries' {
    BeforeEach {
        $script:folder = Initialize-TestFolder
        $script:vault = Join-Path $script:folder 'secrets.local.lv'
        $script:key = Use-TestHelloKey
    }
    AfterEach {
        Clear-TestHelloKey
        $script:key.Dispose()
        Clear-TestFolder $script:folder
    }

    It 'seals a secret, keeps its kind and reads it with one prompt' {
        Set-VaultEntry -Path $vault -Name 'cryptomator' -Value (ConvertTo-TestSecret 'vault-key-1') -Hello 6>$null
        $key.Prompts | Should -Be 1 -Because 'the first seal creates the key'

        $listed = Get-VaultEntryList -Path $vault -WarningAction SilentlyContinue
        $listed.Kind | Should -Be 'secret'
        $listed.Hello | Should -Be 'Ready'
        $key.Prompts | Should -Be 1 -Because 'listing never prompts'

        Get-VaultEntry -Path $vault -Name 'cryptomator' -AsPlainText | Should -Be 'vault-key-1'
        $key.Prompts | Should -Be 2
        $key.Messages[-1] | Should -BeLike "LocalVault: read cryptomator (*secrets.local.lv)"
    }

    It 'seals with an existing key without a prompt' {
        $key.Create('setup')
        Set-VaultEntry -Path $vault -Name 'a' -Value (ConvertTo-TestSecret 'one') -Hello 6>$null
        Set-VaultEntry -Path $vault -Name 'b' -Value (ConvertTo-TestSecret 'two') -Hello 6>$null

        $key.Prompts | Should -Be 1
    }

    It 'reads several entries into the environment with one prompt' {
        Set-VaultEntry -Path $vault -Name 'a' -Value (ConvertTo-TestSecret 'one') -Hello 6>$null
        Set-VaultEntry -Path $vault -Name 'b' -Value (ConvertTo-TestSecret 'two') -Hello 6>$null
        Set-VaultEntry -Path $vault -Name 'c' -Value (ConvertTo-TestSecret 'three') 6>$null
        $before = $key.Prompts

        try {
            Use-VaultEnv -Path $vault -Map @{ LV_TEST_A = 'a'; LV_TEST_B = 'b'; LV_TEST_C = 'c' } 6>$null
            $env:LV_TEST_A | Should -Be 'one'
            $env:LV_TEST_B | Should -Be 'two'
            $env:LV_TEST_C | Should -Be 'three'
        }
        finally {
            Remove-Item Env:\LV_TEST_A, Env:\LV_TEST_B, Env:\LV_TEST_C -ErrorAction SilentlyContinue
        }
        $key.Prompts | Should -Be ($before + 1)
    }

    It 'reads a plain entry without a prompt in a vault that holds Hello entries' {
        Set-VaultEntry -Path $vault -Name 'a' -Value (ConvertTo-TestSecret 'one') -Hello 6>$null
        Set-VaultEntry -Path $vault -Name 'plain' -Value (ConvertTo-TestSecret 'two') 6>$null
        $before = $key.Prompts

        Get-VaultEntry -Path $vault -Name 'plain' -AsPlainText | Should -Be 'two'
        $key.Prompts | Should -Be $before
    }

    It 'keeps Windows Hello when the value changes' {
        Set-VaultEntry -Path $vault -Name 'a' -Value (ConvertTo-TestSecret 'one') -Hello 6>$null

        Set-VaultEntry -Path $vault -Name 'a' -Value (ConvertTo-TestSecret 'two') 6>$null

        (Get-VaultEntryList -Path $vault -WarningAction SilentlyContinue).Hello | Should -Be 'Ready'
        Get-VaultEntry -Path $vault -Name 'a' -AsPlainText | Should -Be 'two'
    }

    It 'moves a <Kind> entry to Windows Hello and back without changing it' -ForEach @(
        @{ Kind = 'secret' }
        @{ Kind = 'credential' }
        @{ Kind = 'file' }
    ) {
        switch ($Kind) {
            'secret' { Set-VaultEntry -Path $vault -Name 'e' -Value (ConvertTo-TestSecret 'pa55') 6>$null }
            'credential' { Set-VaultEntry -Path $vault -Name 'e' -Credential -UserName 'jdoe' -Password (ConvertTo-TestSecret 'pa55') -Totp (ConvertTo-TestSecret $seed) -Url 'https://one' 6>$null }
            'file' {
                Set-Content -LiteralPath (Join-Path $folder 'key.bin') -Value 'pa55' -NoNewline
                Set-VaultEntry -Path $vault -Name 'e' -FilePath (Join-Path $folder 'key.bin') 6>$null
            }
        }
        $created = (Get-VaultEntryList -Path $vault -WarningAction SilentlyContinue).Created

        Set-VaultEntry -Path $vault -Name 'e' -Protection Hello 6>$null
        $sealed = Get-VaultEntryList -Path $vault -WarningAction SilentlyContinue
        $sealed.Kind | Should -Be $Kind
        $sealed.Hello | Should -Be 'Ready'
        $sealed.Created | Should -Be $created

        Set-VaultEntry -Path $vault -Name 'e' -Protection Dpapi 6>$null
        $plain = Get-VaultEntryList -Path $vault -WarningAction SilentlyContinue
        $plain.Hello | Should -Be ''
        $plain.Kind | Should -Be $Kind
        $plain.Created | Should -Be $created
        $prompts = $key.Prompts
        switch ($Kind) {
            'file' { [Text.Encoding]::UTF8.GetString((Get-VaultEntry -Path $vault -Name 'e')) | Should -Be 'pa55' }
            default { Get-VaultEntry -Path $vault -Name 'e' -AsPlainText | Should -Be 'pa55' }
        }
        if ($Kind -eq 'credential') {
            Get-VaultEntry -Path $vault -Name 'e#user' | Should -Be 'jdoe'
            Get-VaultEntry -Path $vault -Name 'e#url' | Should -Be 'https://one'
        }
        $key.Prompts | Should -Be $prompts -Because 'a DPAPI entry reads without Windows Hello'
    }

    It 'says so when the protection is already the one asked for' {
        Set-VaultEntry -Path $vault -Name 'a' -Value (ConvertTo-TestSecret 'one') -Hello 6>$null

        $said = Set-VaultEntry -Path $vault -Name 'a' -Protection Hello 6>&1

        "$said" | Should -Match 'already protected by Windows Hello'
    }

    It 'hides every part of a sealed credential and changes one part with one prompt' {
        Set-VaultEntry -Path $vault -Name 'gh' -Credential -UserName 'jdoe' -Password (ConvertTo-TestSecret 'pa55') -Url 'https://one' -Hello 6>$null
        (Get-VaultEntryList -Path $vault -WarningAction SilentlyContinue).User | Should -BeNullOrEmpty
        $before = $key.Prompts

        Set-VaultEntry -Path $vault -Name 'gh#user' -Value (ConvertTo-TestSecret 'jsmith') 6>$null

        $key.Prompts | Should -Be ($before + 1)
        (Get-VaultEntryList -Path $vault -WarningAction SilentlyContinue).Hello | Should -Be 'Ready'
        Get-VaultEntry -Path $vault -Name 'gh#user' | Should -Be 'jsmith'
        Get-VaultEntry -Path $vault -Name 'gh' -AsPlainText | Should -Be 'pa55'
    }

    It 'seals the personal value of a required entry declared with -Hello' {
        Set-VaultEntry -Path $vault -Name 'pin' -Required -Hello 6>$null
        (Get-VaultEntryList -Path $vault -WarningAction SilentlyContinue).Hello | Should -Be 'Required'

        Set-VaultEntry -Path $vault -Name 'pin' -Value (ConvertTo-TestSecret '1234') 6>$null

        (Get-VaultEntryList -Path $vault -WarningAction SilentlyContinue).Hello | Should -Be 'Ready'
        Get-VaultEntry -Path $vault -Name 'pin' -AsPlainText | Should -Be '1234'
    }

    It 'restores a sealed file' {
        Set-Content -LiteralPath (Join-Path $folder 'key.bin') -Value 'file-content' -NoNewline
        Set-VaultEntry -Path $vault -Name 'f' -FilePath (Join-Path $folder 'key.bin') -Hello 6>$null
        Remove-Item -LiteralPath (Join-Path $folder 'key.bin')

        Restore-VaultFile -Path $vault -Name 'f' | Select-Object -ExpandProperty Action | Should -Be 'Written'

        Get-Content -LiteralPath (Join-Path $folder 'key.bin') -Raw | Should -Be 'file-content'
    }

    It 'reports entries as missing when the key is gone, and refuses to read them' {
        Set-VaultEntry -Path $vault -Name 'a' -Value (ConvertTo-TestSecret 'one') -Hello 6>$null
        $key.Delete()

        $listed = Get-VaultEntryList -Path $vault -WarningVariable warnings -WarningAction SilentlyContinue
        $listed.Hello | Should -Be 'Missing'
        "$warnings" | Should -Match 'Windows Hello key missing.*lv load restores'
        { Get-VaultEntry -Path $vault -Name 'a' -AsPlainText } | Should -Throw '*Windows Hello key missing*lv load restores it from the store*'
    }

    It 'reports entries as missing when the key was replaced' {
        Set-VaultEntry -Path $vault -Name 'a' -Value (ConvertTo-TestSecret 'one') -Hello 6>$null
        $key.Create('PIN reset')

        (Get-VaultEntryList -Path $vault -WarningAction SilentlyContinue).Hello | Should -Be 'Missing'
        { Get-VaultEntry -Path $vault -Name 'a' } | Should -Throw '*Windows Hello key changed*'
        (Get-VaultHelloStatus -Path $vault).State | Should -Be 'KeyChanged'
    }

    It 'refuses to seal when Windows Hello is unavailable and writes nothing' {
        $key.UnavailableReason = 'this is a remote desktop session'

        { Set-VaultEntry -Path $vault -Name 'a' -Value (ConvertTo-TestSecret 'one') -Hello 6>$null } | Should -Throw '*remote desktop*'

        Test-Path -LiteralPath $vault | Should -BeFalse
    }

    It 'never stores a Hello value with DPAPI only when Windows Hello is unavailable' {
        Set-VaultEntry -Path $vault -Name 'a' -Value (ConvertTo-TestSecret 'one') -Hello 6>$null
        $key.UnavailableReason = "Windows Hello isn't set up"

        { Set-VaultEntry -Path $vault -Name 'a' -Value (ConvertTo-TestSecret 'two') 6>$null } | Should -Throw "*isn't set up*"
        { Get-VaultEntry -Path $vault -Name 'a' } | Should -Throw "*isn't set up*"
    }

    It 'needs a console to prompt, but seals with an existing key without one' {
        $key.Create('setup')
        $key.NeedsConsole = $true

        Set-VaultEntry -Path $vault -Name 'a' -Value (ConvertTo-TestSecret 'one') -Hello 6>$null
        { Get-VaultEntry -Path $vault -Name 'a' } | Should -Throw '*interactive console*'
        $key.Prompts | Should -Be 1
    }

    It 'reports a cancelled prompt' {
        Set-VaultEntry -Path $vault -Name 'a' -Value (ConvertTo-TestSecret 'one') -Hello 6>$null
        $key.CancelNext = $true

        { Get-VaultEntry -Path $vault -Name 'a' } | Should -Throw '*cancelled*'
    }

    It 'warns when the key is software-only' {
        $key.IsHardware = $false

        Set-VaultEntry -Path $vault -Name 'a' -Value (ConvertTo-TestSecret 'one') -Hello -WarningVariable warnings -WarningAction SilentlyContinue 6>$null

        "$warnings" | Should -Match 'software-only \(no TPM\)'
        (Get-VaultHelloStatus -Path $vault -WarningAction SilentlyContinue).State | Should -Be 'Software'
    }

    It 'shows the key state and the entries through lv hello' {
        Set-VaultEntry -Path $vault -Name 'a' -Value (ConvertTo-TestSecret 'one') -Hello 6>$null

        $status = Get-VaultHelloStatus -Path $vault
        $status.State | Should -Be 'Ready'
        $status.Hardware | Should -BeTrue
        $status.KeyId | Should -Be $status.VaultKeyId
        $status.Entries | Should -Be 1
        $arguments = @('hello', '--path', $vault)
        $text = lv @arguments | Out-String
        $text | Should -Match 'Windows Hello: Ready, TPM'
        $text | Should -Match '1 Hello entry'
    }

    It 'marks entries through lv mark and seals through lv set --hello' {
        Set-VaultEntry -Path $vault -Name 'a' -Value (ConvertTo-TestSecret 'one') 6>$null

        $arguments = @('mark', 'a', 'hello', '--path', $vault)
        lv @arguments 6>$null
        (Get-VaultEntryList -Path $vault -WarningAction SilentlyContinue).Hello | Should -Be 'Ready'
        $arguments = @('mark', 'a', 'nohello', '--path', $vault)
        lv @arguments 6>$null
        (Get-VaultEntryList -Path $vault -WarningAction SilentlyContinue).Hello | Should -Be ''
        $LASTEXITCODE | Should -Be 0
    }

    It 'refuses to protect an entry that does not exist' {
        Set-VaultEntry -Path $vault -Name 'a' -Value (ConvertTo-TestSecret 'one') 6>$null

        { Set-VaultEntry -Path $vault -Name 'x' -Protection Hello 6>$null } | Should -Throw "*No entry 'x'*"
    }

    It 'never shows a sealed value in any stream' {
        Set-VaultEntry -Path $vault -Name 'a' -Value (ConvertTo-TestSecret 'leak-check-value') -Hello -Verbose -Debug 6>&1 5>&1 4>&1 3>&1 2>&1 | Out-String | Should -Not -Match 'leak-check-value'
        Get-VaultEntryList -Path $vault -Verbose 6>&1 5>&1 4>&1 3>&1 2>&1 | Out-String | Should -Not -Match 'leak-check-value'
        Get-VaultHelloStatus -Path $vault 6>&1 5>&1 4>&1 3>&1 2>&1 | Out-String | Should -Not -Match 'leak-check-value'
    }
}

Describe 'Windows Hello entries in exports and stores' {
    BeforeEach {
        $script:folder = Initialize-TestFolder
        $script:vault = Join-Path $script:folder 'secrets.local.lv'
        $script:key = Use-TestHelloKey
        $script:passphrase = ConvertTo-TestSecret 'a long passphrase 1'
    }
    AfterEach {
        Clear-TestHelloKey
        $script:key.Dispose()
        Clear-TestFolder $script:folder
    }

    It 'exports a Hello entry with one prompt and imports it sealed again' {
        Set-VaultEntry -Path $vault -Name 'a' -Value (ConvertTo-TestSecret 'one') -Hello 6>$null
        Set-VaultEntry -Path $vault -Name 'b' -Value (ConvertTo-TestSecret 'two') -Hello 6>$null
        Set-VaultEntry -Path $vault -Name 'plain' -Value (ConvertTo-TestSecret 'three') 6>$null
        $export = Join-Path $folder 'out.export'
        $before = $key.Prompts

        Export-Vault -Path $vault -Destination $export -Passphrase $passphrase 6>$null
        $key.Prompts | Should -Be ($before + 1)
        $key.Messages[-1] | Should -BeLike 'LocalVault: export a, b (*'

        $other = Join-Path $folder 'other/secrets.local.lv'
        $before = $key.Prompts
        Import-Vault -Path $other -Source $export -Passphrase $passphrase 6>$null | Out-Null
        $key.Prompts | Should -Be $before -Because 'sealing never prompts once the key exists'

        $listed = Get-VaultEntryList -Path $other -WarningAction SilentlyContinue
        ($listed | Where-Object Name -EQ 'a').Hello | Should -Be 'Ready'
        ($listed | Where-Object Name -EQ 'plain').Hello | Should -Be ''
        Get-VaultEntry -Path $other -Name 'b' -AsPlainText | Should -Be 'two'
    }

    It 'exports a vault without Hello entries without a prompt' {
        Set-VaultEntry -Path $vault -Name 'plain' -Value (ConvertTo-TestSecret 'three') 6>$null

        Export-Vault -Path $vault -Destination (Join-Path $folder 'out.export') -Passphrase $passphrase 6>$null

        $key.Prompts | Should -Be 0
    }

    It 'refuses an import of Hello entries when Windows Hello is unavailable and writes nothing' {
        Set-VaultEntry -Path $vault -Name 'a' -Value (ConvertTo-TestSecret 'one') -Hello 6>$null
        $export = Join-Path $folder 'out.export'
        Export-Vault -Path $vault -Destination $export -Passphrase $passphrase 6>$null
        $key.UnavailableReason = 'this is a remote desktop session'
        $other = Join-Path $folder 'other/secrets.local.lv'

        { Import-Vault -Path $other -Source $export -Passphrase $passphrase 6>$null } | Should -Throw '*remote desktop*'

        Test-Path -LiteralPath $other | Should -BeFalse
    }

    It 'saves a Hello entry to a store and a teammate loads it sealed' {
        $savedLocalAppData = $env:LOCALAPPDATA
        $env:LOCALAPPDATA = Join-Path $folder 'appdata'
        try {
            $database = Join-Path $folder 'team.kdbx'
            Initialize-TestKeePass -Path $database -Master 'pester master 1'
            Register-VaultStore -Name 'team' -Target $database | Out-Null
            $mine = Join-Path $folder 'me/proj/secrets.local.lv'
            $theirs = Join-Path $folder 'them/proj/secrets.local.lv'
            $script = Join-Path $folder 'run.ps1'
            Set-Content -LiteralPath $script -Value @"
`$ErrorActionPreference = 'Stop'
Import-Module '$(Join-Path $env:LOCALVAULT_MODULE_PATH 'LocalVault.psd1')'
`$key = [LocalVault.Core.SoftwareHelloKey]::new()
[LocalVault.Core.HelloKeys].GetProperty('Override', [Reflection.BindingFlags]'NonPublic, Static').SetValue(`$null, `$key)
`$value = [securestring]::new(); 'kp-hello'.ToCharArray() | ForEach-Object { `$value.AppendChar(`$_) }
Set-VaultEntry -Path '$mine' -Name 'db' -Value `$value -Hello 6>`$null
`$before = `$key.Prompts
Export-VaultToStore -Path '$mine' 6>`$null | Out-Null
"save prompts: `$(`$key.Prompts - `$before)"
Compare-VaultWithStore -Path '$mine' 6>`$null | Out-Null
"compare prompts: `$(`$key.Prompts - `$before)"
Import-VaultFromStore -Path '$theirs' 6>`$null | Out-Null
"theirs hello: `$((Get-VaultEntryList -Path '$theirs' -WarningAction SilentlyContinue).Hello)"
"theirs value: `$(Get-VaultEntry -Path '$theirs' -Name 'db' -AsPlainText)"
"@
            $output = "pester master 1`npester master 1`npester master 1" | pwsh -NoProfile -NonInteractive -File $script 2>&1 | Out-String

            $LASTEXITCODE | Should -Be 0 -Because $output
            $output | Should -Match 'save prompts: 1'
            $output | Should -Match 'compare prompts: 1'
            $output | Should -Match 'theirs hello: Ready'
            $output | Should -Match 'theirs value: kp-hello'
        }
        finally {
            $env:LOCALAPPDATA = $savedLocalAppData
        }
    }
}
