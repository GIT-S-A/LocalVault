#Requires -Version 7.6
[CmdletBinding()]
param(
    [string]$Commit = 'HEAD',

    [string]$TestScript = 'tools/test.ps1',

    [string]$BuildScript = 'tools/build.ps1',

    [string]$Remote = 'origin',

    [string]$Repository = 'GIT-S-A/LocalVault',

    [string]$KeyVaultUrl = 'https://gitcodesign.vault.azure.net/',

    [string]$CertificateName = 'GITCodeSign-2025',

    [string]$TimestampUrl = 'http://timestamp.digicert.com',

    [switch]$Unsigned
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$publisher = 'GIT Gestion Et Informatique Pour Tous S.A.'
$signedExtensions = '.dll', '.ps1', '.psm1', '.psd1'

$root = Split-Path -Parent $PSScriptRoot
$sha = git -C $root rev-parse --verify --quiet "$Commit^{commit}"
if ($LASTEXITCODE -ne 0) { throw "'$Commit' is not a commit of '$root'." }

if (-not $Unsigned) {
    foreach ($tool in 'azuresigntool', 'az', 'gh') {
        if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
            throw "$tool is required to sign and publish a release. Nothing was tagged."
        }
    }
    az account show --only-show-errors --output none 2>$null
    if ($LASTEXITCODE -ne 0) { throw 'Not logged in to Azure. Run az login first; nothing was tagged.' }
    gh auth status --hostname github.com 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Not logged in to GitHub. Run gh auth login first; nothing was tagged.' }
}

git -C $root fetch --quiet $Remote
if ($LASTEXITCODE -ne 0) { throw "git fetch $Remote failed." }
if (-not (git -C $root branch --remotes --contains $sha)) {
    throw "$($sha.Substring(0, 7)) is not on $Remote yet. Push it first; nothing was tagged."
}

$manifestText = git -C $root show "${sha}:src/LocalVault.PowerShell/LocalVault.psd1"
if ($LASTEXITCODE -ne 0) { throw "$($sha.Substring(0, 7)) has no module manifest." }
$manifestFile = Join-Path ([IO.Path]::GetTempPath()) "lv-release-$([guid]::NewGuid().ToString('N')).psd1"
try {
    Set-Content -LiteralPath $manifestFile -Value $manifestText
    $manifest = Import-PowerShellDataFile -LiteralPath $manifestFile
}
finally {
    [IO.File]::Delete($manifestFile)
}
$prerelease = $manifest.PrivateData.PSData['Prerelease']
$tag = if ($prerelease) { "v$($manifest.ModuleVersion)-$prerelease" } else { "v$($manifest.ModuleVersion)" }

git -C $root fetch --quiet --tags $Remote
if (git -C $root tag --list $tag) {
    throw "$tag exists already. Raise the version in LocalVault.psd1 first; nothing was tagged."
}

$assets = Join-Path $root "out/release/$tag"
if (Test-Path -LiteralPath $assets) { Remove-Item -LiteralPath $assets -Recurse -Force }
$package = Join-Path $assets 'package'
New-Item -ItemType Directory -Path $package | Out-Null

"Testing $($sha.Substring(0, 7)) for $tag in a clean checkout."
$worktree = Join-Path ([IO.Path]::GetTempPath()) "lv-release-$([guid]::NewGuid().ToString('N'))"
git -C $root worktree add --detach --quiet $worktree $sha
if ($LASTEXITCODE -ne 0) { throw "git worktree add failed." }
try {
    pwsh -NoProfile -NonInteractive -File (Join-Path $worktree $TestScript)
    if ($LASTEXITCODE -ne 0) { throw "Tests failed in the clean checkout; $tag was not created." }

    $module = & (Join-Path $worktree $BuildScript) | Select-Object -Last 1
    if (-not $module -or -not (Test-Path -LiteralPath (Join-Path $module 'LocalVault.psd1'))) {
        throw "The build produced no module; $tag was not created."
    }
    Copy-Item -LiteralPath $module -Destination (Join-Path $package "LocalVault/$($manifest.ModuleVersion)") -Recurse
    Copy-Item -LiteralPath (Join-Path $worktree 'install.ps1') -Destination $package
    if (Test-Path -LiteralPath (Join-Path $worktree 'skills/localvault')) {
        New-Item -ItemType Directory -Path (Join-Path $package 'skills') | Out-Null
        Copy-Item -LiteralPath (Join-Path $worktree 'skills/localvault') -Destination (Join-Path $package 'skills/localvault') -Recurse
    }
}
finally {
    git -C $root worktree remove --force $worktree
}

if (-not $Unsigned) {
    $toSign = @(Get-ChildItem -LiteralPath $package -Recurse -File | Where-Object { $signedExtensions -contains $_.Extension.ToLowerInvariant() } | ForEach-Object FullName)
    "Signing $($toSign.Count) files with '$CertificateName' from $KeyVaultUrl."
    $list = Join-Path $assets 'sign-list.txt'
    Set-Content -LiteralPath $list -Value $toSign
    azuresigntool sign -fd sha256 -kvm -tr $TimestampUrl -td sha256 -kvu $KeyVaultUrl -kvc $CertificateName -ifl $list | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Signing failed; $tag was not created." }
    Remove-Item -LiteralPath $list

    $refused = foreach ($path in $toSign) {
        $signature = Get-AuthenticodeSignature -LiteralPath $path
        if ($signature.Status -ne 'Valid' -or -not $signature.TimeStamperCertificate -or
            $signature.SignerCertificate.GetNameInfo('SimpleName', $false) -ne $publisher) {
            "$([IO.Path]::GetRelativePath($package, $path)) ($($signature.Status))"
        }
    }
    if ($refused) { throw "Not signed by $publisher with a timestamp: $($refused -join ', '). $tag was not created." }
}

$zip = Join-Path $assets 'LocalVault.zip'
Compress-Archive -Path (Join-Path $package '*') -DestinationPath $zip
Set-Content -LiteralPath "$zip.sha256" -Value "$((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash)  LocalVault.zip" -NoNewline

git -C $root tag -a $tag -m "LocalVault $($tag.Substring(1))" $sha
git -C $root push --quiet $Remote $tag
if ($LASTEXITCODE -ne 0) { throw "Tagged $tag locally, but pushing it failed. Run: git push $Remote $tag" }

if ($Unsigned) {
    "Tagged $tag ($($sha.Substring(0, 7))). Unsigned: no GitHub release; the files are in '$assets'."
    return
}

$installer = Join-Path $package 'install.ps1'
Set-Content -LiteralPath (Join-Path $assets 'notes.md') -Value @"
Install or update:

``````powershell
`$d=New-Item -ItemType Directory "`$env:TEMP\lv-`$(New-Guid)"; irm https://github.com/$Repository/releases/latest/download/install.ps1 -OutFile "`$d\install.ps1"; & "`$d\install.ps1"
``````

Already installed: ``lv update``.
"@
gh release create $tag $zip "$zip.sha256" $installer --repo $Repository --title "LocalVault $($tag.Substring(1))" --notes-file (Join-Path $assets 'notes.md') | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "Tagged $tag, but the GitHub release failed. Run: gh release create $tag `"$zip`" `"$zip.sha256`" `"$installer`" --repo $Repository --notes-file `"$(Join-Path $assets 'notes.md')`""
}
"Released $tag ($($sha.Substring(0, 7))): https://github.com/$Repository/releases/tag/$tag"
