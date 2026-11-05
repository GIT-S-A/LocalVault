#Requires -Version 7.6
Set-StrictMode -Version Latest

BeforeAll {
    . (Join-Path $PSScriptRoot 'TestSupport.ps1')
    $script:savedProfile = $env:USERPROFILE
    $script:savedLocalAppData = $env:LOCALAPPDATA

    function Initialize-TestRepository {
        param([string]$Origin = 'https://example.com/team/proj.git', [string]$Leaf = 'proj')
        $root = Join-Path (Initialize-TestFolder) $Leaf
        New-Item -ItemType Directory -Path $root | Out-Null
        git -C $root init --quiet
        Set-Content -LiteralPath (Join-Path $root '.gitignore') -Value 'secrets.local.*'
        if ($Origin) { git -C $root remote add origin $Origin }
        $root
    }
}

AfterAll {
    $env:USERPROFILE = $script:savedProfile
    $env:LOCALAPPDATA = $script:savedLocalAppData
}

Describe 'SSH keys' {
    BeforeEach {
        $env:USERPROFILE = Initialize-TestFolder
        $env:LOCALAPPDATA = Join-Path $env:USERPROFILE 'appdata'
        $script:repo = Initialize-TestRepository
        $script:vault = Join-Path $script:repo 'secrets.local.lv'
        $script:keyFolder = Join-Path $env:USERPROFILE '.ssh\proj'
    }

    AfterEach {
        Clear-TestFolder -Folder (Split-Path $script:repo)
        Clear-TestFolder -Folder $env:USERPROFILE
    }

    It 'creates a shared key in the repository folder with its public key and marker' {
        $info = New-VaultSshKey -Path $script:vault -Name deploy -HostName deploy@srv1.example.com -Shared -WarningVariable warned 6>$null 3>$null

        $key = Join-Path $script:keyFolder 'deploy'
        $info.Sharing | Should -Be 'Shared'
        $info.Path | Should -Be $key
        $info.Hosts | Should -Be @('deploy@srv1.example.com')
        $info.Fingerprint | Should -Match '^SHA256:'
        Test-Path -LiteralPath $key | Should -BeTrue
        (Get-Content -LiteralPath "$key.pub" -Raw).Trim() | Should -Be $info.PublicKey
        (Get-Acl -LiteralPath $key).AreAccessRulesProtected | Should -BeTrue
        $marker = Get-Content -LiteralPath (Join-Path $script:keyFolder '.localvault') -Raw | ConvertFrom-Json
        $marker.origin | Should -Be 'https://example.com/team/proj.git'
        $marker.keys.deploy.users | Should -Be @((Resolve-Path $script:vault).Path)
        ($warned -join "`n") | Should -Match 'readable by everyone'
        Get-VaultSshKeyPath -Path $script:vault -Name deploy | Should -Be $key
        @(Get-ChildItem -LiteralPath $script:keyFolder -Force -Filter '.new-*').Count | Should -Be 0
    }

    It 'creates a personal key as a declaration and its value' {
        $info = New-VaultSshKey -Path $script:vault -Name me -HostName me@srv1.example.com -WarningVariable warned 6>$null 3>$null

        $info.Sharing | Should -Be 'Personal'
        ($warned -join "`n") | Should -Match 'only on this PC'
        $entry = Get-VaultEntryList -Path $script:vault 3>$null | Where-Object Name -EQ me
        $entry.Sync | Should -Be 'Personal'
        (Get-VaultSshKey -Path $script:vault -Name me).Hosts | Should -Be @('me@srv1.example.com')
    }

    It 'refuses to replace a key without a store to carry the new one' {
        New-VaultSshKey -Path $script:vault -Name deploy -Shared 6>$null 3>$null | Out-Null
        $before = Get-Content -LiteralPath (Join-Path $script:keyFolder 'deploy') -Raw

        { New-VaultSshKey -Path $script:vault -Name deploy -Replace 6>$null 3>$null } | Should -Throw '*replacing it needs the store*'
        Get-Content -LiteralPath (Join-Path $script:keyFolder 'deploy') -Raw | Should -Be $before
        (Get-VaultSshKey -Path $script:vault -Name deploy).Previous | Should -BeFalse
    }

    It 'refuses to replace a key outside a console without -Replace' {
        Initialize-TestKeePass -Path (Join-Path $env:USERPROFILE 'team.kdbx') -Master 'pester master 1'
        Register-VaultStore -Name team -Target (Join-Path $env:USERPROFILE 'team.kdbx') -Default | Out-Null
        New-VaultSshKey -Path $script:vault -Name deploy -Shared 6>$null 3>$null | Out-Null

        { New-VaultSshKey -Path $script:vault -Name deploy 6>$null 3>$null } | Should -Throw '*already holds a key. Add -Replace*'
    }

    It 'replaces a shared key and keeps the old one as fallback' {
        Initialize-TestKeePass -Path (Join-Path $env:USERPROFILE 'team.kdbx') -Master 'pester master 1'
        Register-VaultStore -Name team -Target (Join-Path $env:USERPROFILE 'team.kdbx') -Default | Out-Null
        $key = Join-Path $script:keyFolder 'deploy'
        $first = New-VaultSshKey -Path $script:vault -Name deploy -HostName deploy@srv1.example.com -Shared 6>$null 3>$null
        $firstKey = Get-Content -LiteralPath $key -Raw

        $second = New-VaultSshKey -Path $script:vault -Name deploy -Replace 6>$null 3>$null
        $secondKey = Get-Content -LiteralPath $key -Raw

        $second.Sharing | Should -Be 'Shared'
        $second.Hosts | Should -Be @('deploy@srv1.example.com')
        $second.Previous | Should -BeTrue
        $second.Fingerprint | Should -Not -Be $first.Fingerprint
        $secondKey | Should -Not -Be $firstKey
        Get-Content -LiteralPath "$key.old" -Raw | Should -Be $firstKey
        (Get-Acl -LiteralPath "$key.old").AreAccessRulesProtected | Should -BeTrue
        Test-Path -LiteralPath "$key.old.pub" | Should -BeFalse
        (Get-Content -LiteralPath "$key.pub" -Raw).Trim() | Should -Be $second.PublicKey

        New-VaultSshKey -Path $script:vault -Name deploy -Replace 6>$null 3>$null | Out-Null
        Get-Content -LiteralPath "$key.old" -Raw | Should -Be $secondKey
    }

    It 'replaces a personal key when the personal store is defined' {
        Initialize-TestKeePass -Path (Join-Path $env:USERPROFILE 'mine.kdbx') -Master 'pester master 1'
        Register-VaultStore -Name mine -Target (Join-Path $env:USERPROFILE 'mine.kdbx') -Personal 6>$null | Out-Null
        New-VaultSshKey -Path $script:vault -Name me -HostName me@srv1.example.com 6>$null 3>$null | Out-Null

        $replaced = New-VaultSshKey -Path $script:vault -Name me -Replace 6>$null 3>$null

        $replaced.Sharing | Should -Be 'Personal'
        $replaced.Hosts | Should -Be @('me@srv1.example.com')
        $replaced.Previous | Should -BeTrue
        Test-Path -LiteralPath (Join-Path $script:keyFolder 'me.old') | Should -BeTrue
        { New-VaultSshKey -Path $script:vault -Name me -Shared -Replace 6>$null 3>$null } | Should -Throw '*declared as a personal key*'
    }

    It 'refuses a key another clone already wrote' {
        New-VaultSshKey -Path $script:vault -Name deploy -Shared 6>$null 3>$null | Out-Null
        $other = Initialize-TestRepository
        try {
            { New-VaultSshKey -Path (Join-Path $other 'secrets.local.lv') -Name deploy -Shared 6>$null 3>$null } |
                Should -Throw '*Run lv load to use it*'
        }
        finally { Clear-TestFolder -Folder (Split-Path $other) }
    }

    It 'refuses the folder of another repository with the same name' {
        New-VaultSshKey -Path $script:vault -Name deploy -Shared 6>$null 3>$null | Out-Null
        $other = Initialize-TestRepository -Origin 'https://example.com/other/proj.git'
        try {
            { New-VaultSshKey -Path (Join-Path $other 'secrets.local.lv') -Name second -Shared 6>$null 3>$null } |
                Should -Throw '*another repository*'
        }
        finally { Clear-TestFolder -Folder (Split-Path $other) }
    }

    It 'keeps a file LocalVault did not write unless told to overwrite it' {
        New-Item -ItemType Directory -Path $script:keyFolder -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $script:keyFolder 'deploy') -Value 'HAND MADE'

        { New-VaultSshKey -Path $script:vault -Name deploy -Shared 6>$null 3>$null } | Should -Throw "*wasn't written by LocalVault*"
        New-VaultSshKey -Path $script:vault -Name deploy -Shared -Overwrite 6>$null 3>$null | Out-Null
        Get-Content -LiteralPath (Join-Path $script:keyFolder 'deploy') -Raw | Should -Match 'OPENSSH PRIVATE KEY'
    }

    It 'restores a deleted key file' {
        New-VaultSshKey -Path $script:vault -Name deploy -Shared 6>$null 3>$null | Out-Null
        Remove-Item -LiteralPath (Join-Path $script:keyFolder 'deploy')

        $restored = Restore-VaultFile -Path $script:vault -Name deploy 6>$null 3>$null

        $restored.Action | Should -Be 'Written'
        Test-Path -LiteralPath (Join-Path $script:keyFolder 'deploy') | Should -BeTrue
    }

    It 'changes the hosts of a key and refuses an option given as a host' {
        New-VaultSshKey -Path $script:vault -Name deploy -Shared 6>$null 3>$null | Out-Null

        Set-VaultSshKeyHost -Path $script:vault -Name deploy -HostName deploy@srv2.example.com, 'srv3.example.com:2222' 6>$null
        (Get-VaultSshKey -Path $script:vault -Name deploy).Hosts | Should -Be @('deploy@srv2.example.com', 'srv3.example.com:2222')

        { Set-VaultSshKeyHost -Path $script:vault -Name deploy -HostName '-oProxyCommand=calc' } | Should -Throw '*is not a host*'
    }

    It 'names the key file after the entry name' {
        $info = New-VaultSshKey -Path $script:vault -Name common/deploy -Shared 6>$null 3>$null
        Split-Path -Leaf $info.Path | Should -Be 'common.deploy'
    }

    It 'takes the OpenSSH folder from this PC only and checks it' {
        $tools = Set-VaultSshTools
        $tools.IsDefault | Should -BeTrue

        $empty = Initialize-TestFolder
        try {
            { Set-VaultSshTools -Directory $empty } | Should -Throw '*has no ssh.exe*'
            (Set-VaultSshTools).IsDefault | Should -BeTrue

            foreach ($tool in 'ssh.exe', 'ssh-keygen.exe', 'ssh-add.exe') { Copy-Item (Join-Path $tools.Directory $tool) $empty }
            (Set-VaultSshTools -Directory $empty).Directory | Should -Be $empty
            (Set-VaultSshTools -Default).IsDefault | Should -BeTrue
        }
        finally { Clear-TestFolder -Folder $empty }
    }

    It 'creates and lists keys through lv' {
        Push-Location $script:repo
        try {
            $create = @('ssh-key', 'new', 'deploy', '--host', 'deploy@srv1.example.com', '--host', 'deploy@srv2.example.com', '--shared', '--path', $script:vault)
            lv @create 6>$null 3>$null
            $LASTEXITCODE | Should -Be 0
            (Get-VaultSshKey -Name deploy).Hosts | Should -Be @('deploy@srv1.example.com', 'deploy@srv2.example.com')
            (lv @('ssh-key') | Out-String) | Should -Match 'deploy\s+Shared\s+file\s+ed25519'
            lv @('ssh-key', 'path', 'deploy') | Should -Be (Join-Path $script:keyFolder 'deploy')
            lv @('ssh-key', 'hosts', 'deploy') 6>$null
            (Get-VaultSshKey -Name deploy).Hosts.Count | Should -Be 0
            lv @('ssh-key', 'new', 'other', '--type', 'dsa') 2>$null
            $LASTEXITCODE | Should -Be 1
            lv @('ssh-key', 'new', 'deploy', '--replace') 2>$null
            $LASTEXITCODE | Should -Be 1
            Initialize-TestKeePass -Path (Join-Path $env:USERPROFILE 'team.kdbx') -Master 'pester master 1'
            Register-VaultStore -Name team -Target (Join-Path $env:USERPROFILE 'team.kdbx') -Default | Out-Null
            lv @('ssh-key', 'new', 'deploy', '--replace') 6>$null 3>$null
            $LASTEXITCODE | Should -Be 0
            (Get-VaultSshKey -Name deploy).Previous | Should -BeTrue
        }
        finally { Pop-Location }
    }
}
