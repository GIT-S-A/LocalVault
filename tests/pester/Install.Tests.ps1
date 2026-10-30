#Requires -Version 7.6
Set-StrictMode -Version Latest

BeforeAll {
    . (Join-Path $PSScriptRoot 'TestSupport.ps1')
    $script:installer = Join-Path $PSScriptRoot '../../install.ps1'
    $script:version = Split-Path -Leaf $env:LOCALVAULT_MODULE_PATH
    function Build-TestRelease {
        param([string]$Path)

        New-Item -ItemType Directory -Path (Join-Path $Path 'LocalVault'), (Join-Path $Path 'skills') -Force | Out-Null
        Copy-Item -LiteralPath $env:LOCALVAULT_MODULE_PATH -Destination (Join-Path $Path "LocalVault/$version") -Recurse
        Copy-Item -LiteralPath $installer -Destination $Path
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../../skills/localvault') -Destination (Join-Path $Path 'skills/localvault') -Recurse
        $Path
    }
}

Describe 'install.ps1' {
    BeforeEach {
        $script:folder = Initialize-TestFolder
        $script:modules = Join-Path $script:folder 'Modules'
        $script:skills = Join-Path $script:folder '.claude/skills'
        $script:target = Join-Path $script:modules "LocalVault/$version"
        New-Item -ItemType Directory -Path (Split-Path -Parent $script:skills) | Out-Null
        $script:installSplat = @{
            ModulePath       = $env:LOCALVAULT_MODULE_PATH
            Destination      = $script:modules
            SkillDestination = $script:skills
        }
    }
    AfterEach { Clear-TestFolder $script:folder }

    It 'installs the module under its version and the Claude skill' {
        & $installer @installSplat | Out-Null

        Test-ModuleManifest -Path (Join-Path $target 'LocalVault.psd1') -ErrorAction Stop | Out-Null
        Test-Path -LiteralPath (Join-Path $target 'en-US/about_LocalVault.help.txt') | Should -BeTrue
        Test-Path -LiteralPath (Join-Path $skills 'localvault/SKILL.md') | Should -BeTrue
    }

    It 'replaces an earlier install of the same version' {
        & $installer @installSplat | Out-Null
        Set-Content -LiteralPath (Join-Path $target 'stale.txt') -Value 'x'
        Set-Content -LiteralPath (Join-Path $skills 'localvault/stale.md') -Value 'x'

        & $installer @installSplat | Out-Null

        Test-Path -LiteralPath (Join-Path $target 'stale.txt') | Should -BeFalse
        Test-Path -LiteralPath (Join-Path $skills 'localvault/stale.md') | Should -BeFalse
        @(Get-ChildItem -LiteralPath (Join-Path $modules 'LocalVault')).Count | Should -Be 1
    }

    It 'leaves an install that is in use unchanged' {
        & $installer @installSplat | Out-Null
        $dll = Join-Path $target 'LocalVault.PowerShell.dll'
        $lock = [IO.File]::Open($dll, 'Open', 'Read', 'None')
        try {
            { & $installer @installSplat } | Should -Throw '*in use*'
        }
        finally {
            $lock.Dispose()
        }

        Test-Path -LiteralPath (Join-Path $target 'LocalVault.psd1') | Should -BeTrue
    }

    It 'installs the new copy when a session still holds the old one' {
        & $installer @installSplat | Out-Null
        Set-Content -LiteralPath (Join-Path $target 'stale.txt') -Value 'x'
        $ready = Join-Path $folder 'ready'
        $holder = Start-Process (Get-Process -Id $PID).Path -PassThru -WindowStyle Hidden -ArgumentList '-NoProfile', '-NonInteractive', '-Command',
            "Import-Module '$(Join-Path $target 'LocalVault.psd1')'; Get-VaultStore | Out-Null; Set-Content '$ready' x; Start-Sleep 60"
        try {
            $deadline = [DateTime]::UtcNow.AddSeconds(30)
            while (-not (Test-Path -LiteralPath $ready) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 200 }

            & $installer @installSplat 3>$null | Out-Null

            Test-ModuleManifest -Path (Join-Path $target 'LocalVault.psd1') -ErrorAction Stop | Out-Null
            Test-Path -LiteralPath (Join-Path $target 'stale.txt') | Should -BeFalse
        }
        finally {
            if (-not $holder.HasExited) { $holder.Kill($true) }
            $holder.WaitForExit()
        }

        $deadline = [DateTime]::UtcNow.AddSeconds(30)
        do {
            & $installer @installSplat 3>$null | Out-Null
            $left = @(Get-ChildItem -LiteralPath (Join-Path $modules 'LocalVault') | ForEach-Object Name)
            if ($left.Count -gt 1) { Start-Sleep -Milliseconds 500 }
        } while ($left.Count -gt 1 -and [DateTime]::UtcNow -lt $deadline)
        $left | Should -Be $version
    }

    It 'installs an unpacked release only when every file is signed by GIT' {
        $package = Build-TestRelease (Join-Path $folder 'package')

        { & (Join-Path $package 'install.ps1') -Destination $modules -SkillDestination $skills -SkipModulePath } | Should -Throw '*Not signed by GIT Gestion Et Informatique Pour Tous S.A.*LocalVault.PowerShell.dll*Nothing was installed*'
        Test-Path -LiteralPath $target | Should -BeFalse

        & (Join-Path $package 'install.ps1') -Destination $modules -SkillDestination $skills -SkipModulePath -AllowUnsigned | Out-Null
        Test-ModuleManifest -Path (Join-Path $target 'LocalVault.psd1') -ErrorAction Stop | Out-Null
        Test-Path -LiteralPath (Join-Path $target 'install.ps1') | Should -BeTrue
        Test-Path -LiteralPath (Join-Path $skills 'localvault/SKILL.md') | Should -BeTrue
    }

    It 'downloads a release, checks its SHA256, and does nothing when it is installed already' {
        $source = Join-Path $folder 'releases'
        $latest = Join-Path $source 'latest/download'
        New-Item -ItemType Directory -Path $latest | Out-Null
        $package = Build-TestRelease (Join-Path $folder 'package')
        $zip = Join-Path $latest 'LocalVault.zip'
        Compress-Archive -Path (Join-Path $package '*') -DestinationPath $zip
        Set-Content -LiteralPath "$zip.sha256" -Value "$((Get-FileHash -LiteralPath $zip).Hash)  LocalVault.zip"
        $download = @{ ReleaseSource = $source; Destination = $modules; SkillDestination = $skills; SkipModulePath = $true }

        { & $installer -Latest @download } | Should -Throw '*Not signed by*install.ps1*'
        Test-Path -LiteralPath $target | Should -BeFalse

        (& $installer -Latest -AllowUnsigned @download | Out-String) | Should -Match 'installed in'
        Test-ModuleManifest -Path (Join-Path $target 'LocalVault.psd1') -ErrorAction Stop | Out-Null
        (& $installer -Latest -AllowUnsigned @download | Out-String) | Should -Match 'is up to date'

        Set-Content -LiteralPath "$zip.sha256" -Value "$('0' * 64)  LocalVault.zip"
        { & $installer -Latest -AllowUnsigned @download } | Should -Throw "*doesn't match its SHA256*"
        { & $installer -Version v0.0.1 -AllowUnsigned @download } | Should -Throw '*No release file*download/v0.0.1/LocalVault.zip*'
    }

    It 'installs nothing from an incomplete build' {
        $broken = Join-Path $folder 'broken'
        New-Item -ItemType Directory -Path $broken | Out-Null
        Copy-Item -LiteralPath (Join-Path $env:LOCALVAULT_MODULE_PATH 'LocalVault.psd1') -Destination $broken
        $splat = $installSplat.Clone()
        $splat.ModulePath = $broken

        { & $installer @splat } | Should -Throw '*LocalVault.PowerShell.dll*Nothing was installed*'
        Test-Path -LiteralPath $target | Should -BeFalse
    }

    It 'moves an install out of the Documents module folder' {
        $legacy = Join-Path $folder 'Documents/PowerShell/Modules'
        New-Item -ItemType Directory -Path (Join-Path $legacy 'LocalVault/1.0.0') -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $legacy 'LocalVault/1.0.0/marker.txt') -Value 'old'
        $moved = Join-Path $env:LOCALAPPDATA "LocalVault/Modules/LocalVault/$version"
        try {
            $output = & $installer -ModulePath $env:LOCALVAULT_MODULE_PATH -Destination $legacy -LegacyDestination $legacy -SkillDestination $skills -SkipModulePath

            Test-ModuleManifest -Path (Join-Path $moved 'LocalVault.psd1') -ErrorAction Stop | Out-Null
            Test-Path -LiteralPath (Join-Path $legacy 'LocalVault') | Should -BeFalse
            "$output" | Should -Match 'instead of'
            "$output" | Should -Match 'Removed the old copy'
            @(Get-ChildItem -LiteralPath (Join-Path $env:LOCALAPPDATA 'LocalVault') -Directory -Filter 'old-module-*').Count | Should -Be 0
        }
        finally {
            Remove-Item -LiteralPath (Join-Path $env:LOCALAPPDATA 'LocalVault/Modules') -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'refuses to replace a newer install unless told to downgrade' {
        & $installer @installSplat | Out-Null
        $older = Join-Path $folder 'older'
        Copy-Item -LiteralPath $env:LOCALVAULT_MODULE_PATH -Destination $older -Recurse
        $text = Get-Content -LiteralPath (Join-Path $older 'LocalVault.psd1') -Raw
        Set-Content -LiteralPath (Join-Path $older 'LocalVault.psd1') -Value ($text -replace "Prerelease = '[^']*'", "Prerelease = 'beta1'")
        Set-Content -LiteralPath (Join-Path $older 'marker.txt') -Value 'older'
        $splat = $installSplat.Clone()
        $splat.ModulePath = $older

        { & $installer @splat } | Should -Throw '*is installed, newer than 1.0.0-beta1*'
        Test-Path -LiteralPath (Join-Path $target 'marker.txt') | Should -BeFalse

        & $installer @splat -Downgrade | Out-Null
        Test-Path -LiteralPath (Join-Path $target 'marker.txt') | Should -BeTrue
    }

    It 'skips the skill when Claude is not set up' {
        Remove-Item -LiteralPath (Split-Path -Parent $skills) -Recurse

        $output = & $installer @installSplat

        $output | Should -HaveCount 2
        $output | Should -Not -Match 'Claude skill'
        Test-Path -LiteralPath (Split-Path -Parent $skills) | Should -BeFalse
    }
}
