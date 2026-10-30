#Requires -Version 7.6
Set-StrictMode -Version Latest

BeforeAll {
    . (Join-Path $PSScriptRoot 'TestSupport.ps1')
    $script:releaser = Join-Path $PSScriptRoot '../../tools/release.ps1'
}

Describe 'tools/release.ps1' {
    BeforeEach {
        $script:folder = Initialize-TestFolder
        $script:origin = Join-Path $script:folder 'origin.git'
        $script:repository = Join-Path $script:folder 'repo'
        git init --quiet --bare $script:origin
        git init --quiet $script:repository
        git -C $script:repository remote add origin $script:origin
        New-Item -ItemType Directory -Path (Join-Path $script:repository 'tools'), (Join-Path $script:repository 'src/LocalVault.PowerShell') | Out-Null
        Copy-Item -LiteralPath $script:releaser -Destination (Join-Path $script:repository 'tools/release.ps1')
        Set-Content -LiteralPath (Join-Path $script:repository 'src/LocalVault.PowerShell/LocalVault.psd1') -Value "@{ ModuleVersion = '9.9.9'; PrivateData = @{ PSData = @{ Prerelease = 'beta1' } } }"
        Set-Content -LiteralPath (Join-Path $script:repository 'tools/fake-test.ps1') -Value 'if (Test-Path (Join-Path $PSScriptRoot "fail.txt")) { exit 1 }; exit 0'
        Set-Content -LiteralPath (Join-Path $script:repository 'tools/fake-build.ps1') -Value '$out = New-Item -ItemType Directory -Path (Join-Path $PSScriptRoot "../out/module") -Force; Set-Content (Join-Path $out "LocalVault.psd1") "@{}"; Set-Content (Join-Path $out "LocalVault.Core.dll") "x"; $out.FullName'
        Set-Content -LiteralPath (Join-Path $script:repository 'install.ps1') -Value '"installer"'
        Set-Content -LiteralPath (Join-Path $script:repository '.gitignore') -Value 'out/'
        git -C $script:repository add -A
        git -C $script:repository -c user.name=t -c user.email=t@t commit --quiet -m first
        $script:release = { & (Join-Path $repository 'tools/release.ps1') -TestScript 'tools/fake-test.ps1' -BuildScript 'tools/fake-build.ps1' -Unsigned }
    }
    AfterEach { Clear-TestFolder $script:folder }

    It 'refuses a commit that is not pushed' {
        { & $release } | Should -Throw '*not on origin yet*'
        git -C $repository tag --list | Should -BeNullOrEmpty
    }

    It 'tests and builds a clean checkout, packages it, then tags and pushes the manifest version' {
        git -C $repository push --quiet origin HEAD:main 2>$null

        (& $release | Out-String) | Should -Match 'Tagged v9.9.9-beta1.*no GitHub release'

        git -C $origin tag --list | Should -Be 'v9.9.9-beta1'
        @(git -C $repository worktree list).Count | Should -Be 1
        $zip = Join-Path $repository 'out/release/v9.9.9-beta1/LocalVault.zip'
        Get-Content -LiteralPath "$zip.sha256" | Should -Be "$((Get-FileHash -LiteralPath $zip).Hash)  LocalVault.zip"
        $unpacked = Join-Path $folder 'unpacked'
        Expand-Archive -LiteralPath $zip -DestinationPath $unpacked
        Test-Path -LiteralPath (Join-Path $unpacked 'install.ps1') | Should -BeTrue
        Test-Path -LiteralPath (Join-Path $unpacked 'LocalVault/9.9.9/LocalVault.Core.dll') | Should -BeTrue
        { & $release } | Should -Throw '*exists already*'
    }

    It 'creates no tag when the tests fail, and ignores uncommitted files' {
        Set-Content -LiteralPath (Join-Path $repository 'tools/fail.txt') -Value 'x'
        git -C $repository add -A
        git -C $repository -c user.name=t -c user.email=t@t commit --quiet -m failing
        git -C $repository push --quiet origin HEAD:main 2>$null
        { & $release } | Should -Throw '*Tests failed*'
        git -C $repository tag --list | Should -BeNullOrEmpty

        git -C $repository rm --quiet tools/fail.txt
        git -C $repository -c user.name=t -c user.email=t@t commit --quiet -m fixed
        git -C $repository push --quiet origin HEAD:main 2>$null
        Set-Content -LiteralPath (Join-Path $repository 'tools/fail.txt') -Value 'uncommitted'
        (& $release | Out-String) | Should -Match 'Tagged'
    }
}
