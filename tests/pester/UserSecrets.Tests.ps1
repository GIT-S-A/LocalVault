#Requires -Version 7.6
Set-StrictMode -Version Latest

BeforeAll {
    . (Join-Path $PSScriptRoot 'TestSupport.ps1')
    $script:apiId = 'lv-pester-api-1'

    function Initialize-TestProject {
        param([string]$Repository, [string]$Relative, [string]$Id)
        $path = Join-Path $Repository $Relative
        New-Item -ItemType Directory -Path (Split-Path $path) -Force | Out-Null
        Set-Content -LiteralPath $path -Value "<Project Sdk=`"Microsoft.NET.Sdk`"><PropertyGroup><UserSecretsId>$Id</UserSecretsId></PropertyGroup></Project>"
        $path
    }

    function Get-SecretsJson {
        param([string]$Id = $script:apiId)
        $path = Join-Path $env:APPDATA "Microsoft/UserSecrets/$Id/secrets.json"
        if (Test-Path -LiteralPath $path) { Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -AsHashtable } else { $null }
    }

    function Write-SecretsJson {
        param([hashtable]$Values, [string]$Id = $script:apiId)
        $path = Join-Path $env:APPDATA "Microsoft/UserSecrets/$Id/secrets.json"
        New-Item -ItemType Directory -Path (Split-Path $path) -Force | Out-Null
        $Values | ConvertTo-Json | Set-Content -LiteralPath $path
    }
}

Describe '.NET user secrets' {
    BeforeEach {
        $script:folder = Initialize-TestFolder
        $script:savedAppData = $env:APPDATA
        $env:APPDATA = Join-Path $script:folder 'roaming'
        $script:repository = Join-Path $script:folder 'repo'
        git init --quiet $script:repository
        Set-Content -LiteralPath (Join-Path $script:repository '.gitignore') -Value 'secrets.local.*'
        $script:vault = Join-Path $script:repository 'secrets.local.lv'
        Initialize-TestProject -Repository $script:repository -Relative 'src/Api/Api.csproj' -Id $script:apiId | Out-Null
        Set-VaultEntry -Path (Join-Path $script:repository 'secrets.local.lv') -Name 'seed' -Value (ConvertTo-TestSecret 'seed-value-1') 6>$null
        Push-Location $script:repository
    }
    AfterEach {
        Pop-Location
        $env:APPDATA = $script:savedAppData
        Clear-TestFolder $script:folder
    }

    It 'tracks a new key, writes it and keeps keys set by hand' {
        Write-SecretsJson @{ 'Mine' = 'hand-set-1' }

        Set-VaultUserSecret -Target 'Api' -Key 'ConnectionStrings:Default' -Value (ConvertTo-TestSecret 'conn-value-1') 6>$null

        (Get-SecretsJson)['ConnectionStrings:Default'] | Should -Be 'conn-value-1'
        (Get-SecretsJson)['Mine'] | Should -Be 'hand-set-1'
        Get-VaultEntry -Name 'ConnectionStrings:Default' -AsPlainText | Should -Be 'conn-value-1'
        $tracking = (Get-Content -Raw (Join-Path $repository 'localvault.json') | ConvertFrom-Json).userSecrets
        $tracking[0].id | Should -Be $apiId
        $tracking[0].keys.'ConnectionStrings:Default' | Should -Be 'ConnectionStrings:Default'
        $tracking[0].PSObject.Properties.Name | Should -Not -Contain 'vault'

        $rows = @(Get-VaultUserSecretList)
        ($rows | Where-Object Key -EQ 'ConnectionStrings:Default').State | Should -Be 'Written'
        ($rows | Where-Object Key -EQ 'Mine').State | Should -Be 'Untracked'
        $rows[0].Projects | Should -Be @('Api')
        (Get-VaultEntryList 3>$null | Where-Object Name -EQ 'ConnectionStrings:Default').UserSecret | Should -BeTrue
        Get-VaultUserSecret -Target $apiId -Key 'connectionstrings:default' -AsPlainText | Should -Be 'conn-value-1'
        (lv user-secrets | Out-String) | Should -Match 'ConnectionStrings:Default\s+ConnectionStrings:Default\s+Written'
    }

    It 'asks before replacing a value changed by hand, and exits 2 without a console' {
        Set-VaultUserSecret -Target 'Api' -Key 'ConnectionStrings:Default' -Value (ConvertTo-TestSecret 'conn-value-1') 6>$null
        Write-SecretsJson @{ 'ConnectionStrings:Default' = 'changed-by-hand' }

        (Get-VaultUserSecretList).State | Should -Be 'Differs'
        { Write-VaultUserSecret 6>$null } | Should -Throw '*ConnectionStrings:Default*-Overwrite*'
        lv user-secrets load 2>$null 6>$null
        $LASTEXITCODE | Should -Be 2
        (Get-SecretsJson)['ConnectionStrings:Default'] | Should -Be 'changed-by-hand'

        Write-VaultUserSecret -Overwrite 6>$null
        (Get-SecretsJson)['ConnectionStrings:Default'] | Should -Be 'conn-value-1'
    }

    It 'adopts a value already in secrets.json and tracks a part of an existing credential' {
        Write-SecretsJson @{ 'Api:Key' = 'from-file-1' }
        Set-VaultEntry -Name 'gh' -Credential -UserName 'jdoe' -Password (ConvertTo-TestSecret 'gh-password-1') 6>$null

        Set-VaultUserSecret -Target 'Api' -Key 'Api:Key' -FromFile 6>$null
        Set-VaultUserSecret -Target 'Api' -Key 'GitHub:User' -Entry 'gh#user' 6>$null

        Get-VaultEntry -Name 'Api:Key' -AsPlainText | Should -Be 'from-file-1'
        (Get-SecretsJson)['GitHub:User'] | Should -Be 'jdoe'
        { Set-VaultUserSecret -Target 'Api' -Key 'GitHub:Totp' -Entry 'gh#totp' } | Should -Throw '*30 seconds*'
        { Set-VaultUserSecret -Target 'Api' -Key 'Absent' -FromFile } | Should -Throw '*Absent is not in*'
    }

    It 'keeps a personal value as a required declaration' {
        Set-VaultUserSecret -Target 'Api' -Key 'Smtp:Password' -Value (ConvertTo-TestSecret 'smtp-mine-1') -Personal 6>$null

        (Get-VaultEntryList 3>$null | Where-Object Name -EQ 'Smtp:Password').Sync | Should -Be 'Personal'
        (Get-SecretsJson)['Smtp:Password'] | Should -Be 'smtp-mine-1'
    }

    It 'refuses a key another vault of the repository tracks' {
        Set-VaultUserSecret -Target 'Api' -Key 'ConnectionStrings:Default' -Value (ConvertTo-TestSecret 'conn-value-1') 6>$null
        $other = Join-Path $repository 'infra/secrets.local.lv'

        { Set-VaultUserSecret -Path $other -Target 'Api' -Key 'ConnectionStrings:Default' -Value (ConvertTo-TestSecret 'other-2') } |
            Should -Throw '*already tracked*'
    }

    It 'clears and untracks only values that still match the vault' {
        Set-VaultUserSecret -Target 'Api' -Key 'A' -Value (ConvertTo-TestSecret 'value-a-1') 6>$null
        Set-VaultUserSecret -Target 'Api' -Key 'B' -Value (ConvertTo-TestSecret 'value-b-1') 6>$null
        $edited = Get-SecretsJson
        $edited['B'] = 'edited-b'
        Write-SecretsJson $edited

        Clear-VaultUserSecret 6>$null 3>$null

        (Get-SecretsJson).Keys | Should -Be @('B')
        Remove-VaultUserSecret -Target 'Api' -Key 'B' 6>$null 3>$null
        (Get-SecretsJson)['B'] | Should -Be 'edited-b'
        Get-VaultEntry -Name 'B' -AsPlainText | Should -Be 'value-b-1'
        (Get-VaultUserSecretList | Where-Object Key -EQ 'B').State | Should -Be 'Untracked'

        Write-VaultUserSecret 6>$null
        Clear-VaultUserSecret 6>$null
        Remove-Item -LiteralPath (Join-Path $env:APPDATA "Microsoft/UserSecrets/$apiId/secrets.json")
        Write-VaultUserSecret 6>$null
        Clear-VaultUserSecret 6>$null
        Test-Path -LiteralPath (Join-Path $env:APPDATA "Microsoft/UserSecrets/$apiId") | Should -BeFalse
    }

    It 'resolves the target by id or project name from the solution' {
        Initialize-TestProject -Repository $repository -Relative 'src/Worker/Worker.csproj' -Id 'lv-pester-worker' | Out-Null
        Set-Content -LiteralPath (Join-Path $repository 'App.slnx') -Value '<Solution><Project Path="src/Worker/Worker.csproj" /></Solution>'

        { Set-VaultUserSecret -Target 'Api' -Key 'A' -Value (ConvertTo-TestSecret 'value-a-1') } | Should -Throw '*lv-pester-worker (Worker)*'
        Set-VaultUserSecret -Target 'lv-pester-worker' -Key 'A' -Value (ConvertTo-TestSecret 'value-a-1') 6>$null
        (Get-SecretsJson -Id 'lv-pester-worker')['A'] | Should -Be 'value-a-1'

        Initialize-TestProject -Repository $repository -Relative 'src/Worker/Worker.csproj' -Id '$(MSBuildProjectName)' | Out-Null
        { Get-VaultUserSecretList } | Should -Throw '*literally*'
    }

    It 'removes the tracked values with the vault' {
        Set-VaultUserSecret -Target 'Api' -Key 'A' -Value (ConvertTo-TestSecret 'value-a-1') 6>$null

        Clear-Vault -Confirm:$false 6>$null

        Get-SecretsJson | Should -BeNullOrEmpty
    }
}

Describe '.NET user secrets on lv load' {
    BeforeEach {
        $script:folder = Initialize-TestFolder
        $script:savedAppData = $env:APPDATA
        $script:savedLocalAppData = $env:LOCALAPPDATA
        $env:APPDATA = Join-Path $script:folder 'roaming'
        $env:LOCALAPPDATA = Join-Path $script:folder 'appdata'
        $script:master = 'pester master 1'
        $script:database = Join-Path $script:folder 'team.kdbx'
        Initialize-TestKeePass -Path $script:database -Master $script:master
        Register-VaultStore -Name 'team' -Target $script:database -Default | Out-Null
        $script:repository = Join-Path $script:folder 'repo'
        git init --quiet $script:repository
        Set-Content -LiteralPath (Join-Path $script:repository '.gitignore') -Value 'secrets.local.*'
        Initialize-TestProject -Repository $script:repository -Relative 'src/Api/Api.csproj' -Id $script:apiId | Out-Null
        Set-VaultEntry -Path (Join-Path $script:repository 'secrets.local.lv') -Name 'seed' -Value (ConvertTo-TestSecret 'seed-value-1') 6>$null
        Push-Location $script:repository
    }
    AfterEach {
        Pop-Location
        $env:APPDATA = $script:savedAppData
        $env:LOCALAPPDATA = $script:savedLocalAppData
        Clear-TestFolder $script:folder
    }

    It 'writes the tracked values after loading the vault, and leaves conflicts as they are' {
        Set-VaultUserSecret -Target 'Api' -Key 'ConnectionStrings:Default' -Value (ConvertTo-TestSecret 'conn-value-1') 6>$null
        $prefix = "Import-Module '$(Join-Path $env:LOCALVAULT_MODULE_PATH 'LocalVault.psd1')'; Set-Location '$repository'; "
        $master | pwsh -NoProfile -NonInteractive -Command ($prefix + 'lv save; exit $LASTEXITCODE') *>$null
        $LASTEXITCODE | Should -Be 0
        Remove-Item -LiteralPath (Join-Path $repository 'secrets.local.lv')
        Remove-Item -LiteralPath (Join-Path $env:APPDATA 'Microsoft') -Recurse

        $output = $master | pwsh -NoProfile -NonInteractive -Command ($prefix + 'lv load; exit $LASTEXITCODE') 2>&1 | Out-String
        $LASTEXITCODE | Should -Be 0 -Because $output
        (Get-SecretsJson)['ConnectionStrings:Default'] | Should -Be 'conn-value-1'

        Write-SecretsJson @{ 'ConnectionStrings:Default' = 'changed-by-hand' }
        $output = $master | pwsh -NoProfile -NonInteractive -Command ($prefix + 'lv load; exit $LASTEXITCODE') 2>&1 | Out-String
        $LASTEXITCODE | Should -Be 0 -Because $output
        $output | Should -Match 'differ from the vault'
        (Get-SecretsJson)['ConnectionStrings:Default'] | Should -Be 'changed-by-hand'
    }
}
