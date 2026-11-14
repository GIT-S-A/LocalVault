#Requires -Version 7.6
Set-StrictMode -Version Latest

BeforeAll {
    . (Join-Path $PSScriptRoot 'TestSupport.ps1')
}

Describe 'Templated files' {
    BeforeEach {
        $script:folder = Initialize-TestFolder
        $script:repository = Join-Path $script:folder 'repo'
        git init --quiet $script:repository
        Set-Content -LiteralPath (Join-Path $script:repository '.gitignore') -Value "secrets.local.*`n.env"
        $script:vault = Join-Path $script:repository 'secrets.local.lv'
        $script:env = Join-Path $script:repository '.env'
        Set-VaultEntry -Path $script:vault -Name 'OS_PASSWORD' -Value (ConvertTo-TestSecret 'os-secret-1') 6>$null
        Set-Content -LiteralPath $script:env -Value "OS_AUTH_URL=https://api`nOS_PASSWORD=os-secret-1`nTOKEN=token-value-22" -NoNewline
        Push-Location $script:repository
    }
    AfterEach {
        Pop-Location
        Clear-TestFolder $script:folder
    }

    It 'turns a file into a template, then writes and clears it' {
        $arguments = 'template', 'add', '.env', '--capture', 'TOKEN'
        lv @arguments 6>$null

        Get-Content -LiteralPath "$env.template" -Raw | Should -Be "OS_AUTH_URL=https://api`nOS_PASSWORD={{OS_PASSWORD}}`nTOKEN={{TOKEN}}"
        Get-VaultEntry -Path $vault -Name 'TOKEN' -AsPlainText | Should -Be 'token-value-22'
        (Get-Content -LiteralPath (Join-Path $repository 'localvault.json') -Raw | ConvertFrom-Json).templates[0].path | Should -Be '.env'

        $arguments = 'template', 'clear'
        lv @arguments | Out-Null
        Test-Path -LiteralPath $env | Should -BeFalse
        (Get-VaultTemplate).Rendered | Should -BeFalse

        $arguments = 'template', 'render'
        lv @arguments | Out-Null
        Get-Content -LiteralPath $env -Raw | Should -Be "OS_AUTH_URL=https://api`nOS_PASSWORD=os-secret-1`nTOKEN=token-value-22"
        (Get-VaultTemplate).UpToDate | Should -BeTrue
        (lv template | Out-String) | Should -Match '\.env\s+True\s+True'
    }

    It 'takes edits of the real file back into the template and asks before storing a new secret' {
        Add-VaultTemplate -File '.env' -Capture 'TOKEN' 6>$null | Out-Null
        Set-Content -LiteralPath $env -Value "OS_AUTH_URL=https://new`nOS_PASSWORD=os-secret-1`nTOKEN=token-new-33" -NoNewline

        { Update-VaultTemplate } | Should -Throw '*TOKEN*-AcceptChanges*'
        Get-Content -LiteralPath "$env.template" -Raw | Should -Match 'https://api'

        Update-VaultTemplate -AcceptChanges 6>$null
        Get-Content -LiteralPath "$env.template" -Raw | Should -Be "OS_AUTH_URL=https://new`nOS_PASSWORD={{OS_PASSWORD}}`nTOKEN={{TOKEN}}"
        Get-VaultEntry -Path $vault -Name 'TOKEN' -AsPlainText | Should -Be 'token-new-33'
    }

    It 'starts from a .example and declares the values it captures' {
        Remove-Item -LiteralPath $env
        Set-Content -LiteralPath "$env.example" -Value "OS_PASSWORD=changeme`nAPI_KEY=" -NoNewline

        Add-VaultTemplate -File '.env' -Capture 'API_KEY' 6>$null | Out-Null

        Get-Content -LiteralPath "$env.template" -Raw | Should -Be "OS_PASSWORD={{OS_PASSWORD}}`nAPI_KEY={{API_KEY}}"
        (Get-VaultEntryList -Path $vault 3>$null | Where-Object Name -EQ 'API_KEY').Sync | Should -Be 'Missing'
        { Write-VaultTemplate } | Should -Throw '*API_KEY*required*'
        Test-Path -LiteralPath $env | Should -BeFalse
    }

    It 'refuses to write a file that git does not ignore' {
        Set-Content -LiteralPath (Join-Path $repository 'config.txt') -Value 'p={{OS_PASSWORD}}'
        Set-Content -LiteralPath (Join-Path $repository 'config.txt.example') -Value 'p=x'
        Remove-Item -LiteralPath (Join-Path $repository 'config.txt')
        Add-VaultTemplate -File 'config.txt' 6>$null | Out-Null

        { Write-VaultTemplate -File 'config.txt' } | Should -Throw '*git does not ignore it*'
        Test-Path -LiteralPath (Join-Path $repository 'config.txt') | Should -BeFalse
    }

    It 'runs a command with the variables and files, then removes them' {
        Add-VaultTemplate -File '.env' -Capture 'TOKEN' 6>$null | Out-Null
        Clear-VaultTemplate -Confirm:$false | Out-Null
        $env:LV_RUN_TEST = 'before'

        $arguments = 'run', 'LV_RUN_TEST=OS_PASSWORD', '--', 'pwsh', '-NoProfile', '-Command', 'Write-Output "$env:LV_RUN_TEST|$((Get-Content .env)[2])"; exit 3'
        $output = lv @arguments

        $output | Should -Be 'os-secret-1|TOKEN=token-value-22'
        $LASTEXITCODE | Should -Be 3
        $env:LV_RUN_TEST | Should -Be 'before'
        Test-Path -LiteralPath $env | Should -BeFalse
        Remove-Item Env:LV_RUN_TEST
    }

    It 'deletes the written files with the vault' {
        Add-VaultTemplate -File '.env' 6>$null | Out-Null

        Clear-Vault -Path $vault -Confirm:$false 6>$null

        Test-Path -LiteralPath $env | Should -BeFalse
        Test-Path -LiteralPath "$env.template" | Should -BeTrue
    }
}

Describe 'Changing one setting of a store' {
    BeforeEach {
        $script:folder = Initialize-TestFolder
        $script:savedLocalAppData = $env:LOCALAPPDATA
        $env:LOCALAPPDATA = Join-Path $script:folder 'appdata'
        $script:database = Join-Path $script:folder 'mine.kdbx'
        $script:key = Join-Path $script:folder 'mine.key'
        Set-Content -LiteralPath $script:database -Value 'x'
        Set-Content -LiteralPath $script:key -Value 'k'
        Set-Content -LiteralPath (Join-Path $script:folder 'team.kdbx') -Value 'x'
        Register-VaultStore -Name 'mine' -Target $script:database -KeyFile $script:key | Out-Null
        Register-VaultStore -Name 'team' -Target (Join-Path $script:folder 'team.kdbx') | Out-Null
        $script:vault = Join-Path $script:folder 'v/secrets.local.lv'
    }
    AfterEach {
        $env:LOCALAPPDATA = $script:savedLocalAppData
        Clear-TestFolder $script:folder
    }

    It 'flips the mirror flag and the unlocker without touching the rest' {
        $arguments = 'store', 'set', 'mine', '--mirror', '--default', '--path', $vault
        lv @arguments | Out-Null
        $arguments = 'store', 'set', 'team', '--unlock-with', 'mine', '--path', $vault
        lv @arguments | Out-Null

        $mine = Get-VaultStore -Name 'mine'
        $mine.Mirror | Should -BeTrue
        $mine.Default | Should -BeTrue
        $mine.KeyFile | Should -Be $key
        (Get-VaultStore -Name 'team').UnlockWith | Should -Be 'mine'

        Set-VaultStore -Name 'mine' -Mirror:$false | Out-Null
        Set-VaultStore -Name 'team' -NoUnlock | Out-Null
        (Get-VaultStore -Name 'mine').Mirror | Should -BeFalse
        (Get-VaultStore -Name 'mine').KeyFile | Should -Be $key
        (Get-VaultStore -Name 'team').UnlockWith | Should -BeNullOrEmpty

        $arguments = 'store', '--path', $vault
        (lv @arguments | Out-String) | Should -Match 'Stores: mine \(Computer, default\), team \(Computer\)'
    }
}

Describe 'A value on the standard input of a program' {
    BeforeEach {
        $script:folder = Initialize-TestFolder
        $script:vault = Join-Path $script:folder 'secrets.local.lv'
        $script:received = Join-Path $script:folder 'received.bin'
        $script:reader = '$in = [Console]::OpenStandardInput(); $out = [IO.File]::Create(''{0}''); $in.CopyTo($out); $out.Close(); exit 4' -f $script:received
        Push-Location $script:folder
    }
    AfterEach {
        Pop-Location
        Clear-TestHelloKey
        Clear-TestFolder $script:folder
    }

    It 'sends a secret as UTF-8 bytes and passes the exit code' {
        $text = 'p' + [char]0xE4 + 'ssword-1'
        Set-VaultEntry -Path $vault -Name 'db' -Value (ConvertTo-TestSecret $text) 6>$null

        $arguments = 'run', '--path', $vault, '--stdin', 'db', '--', 'pwsh', '-NoProfile', '-Command', $reader
        lv @arguments 6>$null

        $LASTEXITCODE | Should -Be 4
        [IO.File]::ReadAllBytes($received) | Should -Be ([Text.Encoding]::UTF8.GetBytes($text))
    }

    It 'sends a file entry as its raw bytes' {
        $bytes = [byte[]](0, 1, 255, 10, 13, 65)
        [IO.File]::WriteAllBytes((Join-Path $folder 'blob.bin'), $bytes)
        Set-VaultEntry -Path $vault -Name 'blob' -FilePath (Join-Path $folder 'blob.bin') 6>$null

        $arguments = 'run', '--path', $vault, '--stdin', 'blob', 'pwsh', '-NoProfile', '-Command', $reader
        lv @arguments 6>$null

        [IO.File]::ReadAllBytes($received) | Should -Be $bytes
    }

    It 'sends the password of a credential' {
        Set-VaultEntry -Path $vault -Name 'svc' -Credential -UserName 'deploy' -Password (ConvertTo-TestSecret 'cred-pw-2') 6>$null

        $arguments = 'run', '--path', $vault, '--stdin', 'svc', 'pwsh', '-NoProfile', '-Command', $reader
        lv @arguments 6>$null

        [Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($received)) | Should -Be 'cred-pw-2'
    }

    It 'reads a Hello entry with one prompt' {
        $key = Use-TestHelloKey
        Set-VaultEntry -Path $vault -Name 'db' -Value (ConvertTo-TestSecret 'hello-pw') -Hello 6>$null
        $before = $key.Prompts

        $arguments = 'run', '--path', $vault, '--stdin', 'db', 'pwsh', '-NoProfile', '-Command', $reader
        lv @arguments 6>$null

        $key.Prompts | Should -Be ($before + 1)
        [Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($received)) | Should -Be 'hello-pw'
    }

    It 'fails before the program starts when the name is not in the vault' {
        Set-VaultEntry -Path $vault -Name 'db' -Value (ConvertTo-TestSecret 'x') 6>$null

        $arguments = 'run', '--path', $vault, '--stdin', 'nope', 'pwsh', '-NoProfile', '-Command', $reader
        lv @arguments 2>$null 6>$null

        $LASTEXITCODE | Should -Be 1
        Test-Path -LiteralPath $received | Should -BeFalse
    }

    It 'refuses a command that is not a program' {
        Set-VaultEntry -Path $vault -Name 'db' -Value (ConvertTo-TestSecret 'x') 6>$null

        $arguments = 'run', '--path', $vault, '--stdin', 'db', 'Get-ChildItem'
        $errors = lv @arguments 2>&1 6>$null | Out-String

        $LASTEXITCODE | Should -Be 1
        $errors | Should -Match 'runs a program'
    }

    It 'never shows the value in any stream' {
        Set-VaultEntry -Path $vault -Name 'db' -Value (ConvertTo-TestSecret 'leak-check-stdin') 6>$null

        $arguments = 'run', '--path', $vault, '--stdin', 'db', 'pwsh', '-NoProfile', '-Command', $reader
        lv @arguments 6>&1 5>&1 4>&1 3>&1 2>&1 | Out-String | Should -Not -Match 'leak-check-stdin'
    }
}
