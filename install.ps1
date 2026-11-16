#Requires -Version 7.6
[CmdletBinding()]
param(
    [string]$Destination = (Join-Path $env:LOCALAPPDATA 'LocalVault/Modules'),

    [string]$LegacyDestination = (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'PowerShell/Modules'),

    [switch]$SkipModulePath,

    [string]$SkillDestination = (Join-Path $HOME '.claude/skills'),

    [string]$ModulePath,

    [string]$Version,

    [switch]$Latest,

    [string]$ReleaseSource = 'https://github.com/GIT-S-A/LocalVault/releases',

    [switch]$AllowUnsigned,

    [Alias('Force')]
    [switch]$Downgrade
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$defaultDestination = Join-Path $env:LOCALAPPDATA 'LocalVault/Modules'
$publisher = 'GIT Gestion Et Informatique Pour Tous S.A.'
$signedExtensions = '.dll', '.ps1', '.psm1', '.psd1'

function Test-SamePath {
    param([string]$A, [string]$B)

    [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($A)) -eq [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($B))
}

function Add-UserModulePath {
    param([string]$Folder)

    $user = [Environment]::GetEnvironmentVariable('PSModulePath', 'User')
    $entries = @(($(if ($user) { $user } else { [Environment]::GetEnvironmentVariable('PSModulePath', 'Machine') })) -split ';' | Where-Object { $_ })
    if (-not ($entries | Where-Object { Test-SamePath $_ $Folder })) {
        [Environment]::SetEnvironmentVariable('PSModulePath', ((@($Folder) + $entries) -join ';'), 'User')
        "Added '$Folder' to your PSModulePath. Restart Windows Terminal (close every window): its new tabs keep the environment it started with."
        "Until then, in an open window: `$env:PSModulePath = '$Folder;' + `$env:PSModulePath"
    }
    if (-not (($env:PSModulePath -split ';') | Where-Object { $_ -and (Test-SamePath $_ $Folder) })) {
        $env:PSModulePath = "$Folder;$env:PSModulePath"
    }
}

function Move-LegacyInstall {
    $old = Join-Path $LegacyDestination 'LocalVault'
    $parking = Split-Path -Parent $Destination
    foreach ($leftover in Get-ChildItem -LiteralPath $parking -Directory -Filter 'old-module-*' -ErrorAction SilentlyContinue) {
        Remove-Item -LiteralPath $leftover.FullName -Recurse -Force -ErrorAction SilentlyContinue
    }
    if (-not (Test-Path -LiteralPath $old)) {
        return
    }

    $parked = Join-Path $parking "old-module-$([guid]::NewGuid().ToString('N'))"
    try {
        Move-Item -LiteralPath $old -Destination $parked
    }
    catch {
        $parked = $old
    }
    try {
        Remove-Item -LiteralPath $parked -Recurse -Force -ErrorAction Stop
    }
    catch {
        if ($parked -eq $old) {
            Write-Warning "The old copy in '$old' is in use and loads before the new one. Close the PowerShell sessions that imported it and run install.ps1 again."
            return
        }
    }
    "Removed the old copy from '$old'."
}

function Complete-Install {
    param([bool]$RegisterModulePath)

    if (-not (Test-SamePath $Destination $defaultDestination)) {
        return
    }
    if ($RegisterModulePath) {
        Add-UserModulePath $Destination
    }
    Move-LegacyInstall
}

function Get-ReleaseOrder {
    param([hashtable]$Manifest)

    $version = [version]$Manifest.ModuleVersion
    $prerelease = [string]$Manifest.PrivateData.PSData['Prerelease']
    $label = $prerelease.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9')
    $digits = $prerelease.Substring($label.Length)
    $number = if ($digits) { [int]$digits } else { 0 }
    $final = if ($prerelease) { 0 } else { 1 }
    [pscustomobject]@{
        Text   = if ($prerelease) { "$($Manifest.ModuleVersion)-$prerelease" } else { $Manifest.ModuleVersion }
        Sort   = '{0:D6}.{1:D6}.{2:D6}|{3}|{4,-20}|{5:D6}' -f $version.Major, $version.Minor, $version.Build, $final, $label, $number
        Folder = $Manifest.ModuleVersion
    }
}

function Get-InstalledRelease {
    param([hashtable]$Candidate)

    $installedManifest = Join-Path $Destination "LocalVault/$((Get-ReleaseOrder $Candidate).Folder)/LocalVault.psd1"
    if (Test-Path -LiteralPath $installedManifest) {
        Get-ReleaseOrder (Import-PowerShellDataFile -LiteralPath $installedManifest)
    }
}

function Assert-NotOlder {
    param([hashtable]$Candidate, [bool]$AllowOlder)

    $release = Get-ReleaseOrder $Candidate
    $installed = Get-InstalledRelease $Candidate
    if (-not $AllowOlder -and $installed -and [string]::CompareOrdinal($release.Sort, $installed.Sort) -lt 0) {
        throw "LocalVault $($installed.Text) is installed, newer than $($release.Text). Nothing was changed. Add -Downgrade (lv update --downgrade) to install the older one."
    }
}

function Assert-Signed {
    param([string[]]$Paths)

    $refused = foreach ($path in $Paths) {
        $signature = Get-AuthenticodeSignature -LiteralPath $path
        if ($signature.Status -ne 'Valid' -or -not $signature.SignerCertificate -or
            $signature.SignerCertificate.GetNameInfo('SimpleName', $false) -ne $publisher) {
            "$(Split-Path -Leaf $path) ($($signature.Status))"
        }
    }
    if ($refused) {
        throw "Not signed by $publisher`: $($refused -join ', '). Nothing was installed."
    }
}

function Get-SignableFile {
    param([string]$Folder)

    Get-ChildItem -LiteralPath $Folder -Recurse -File | Where-Object { $signedExtensions -contains $_.Extension.ToLowerInvariant() } | ForEach-Object FullName
}

function Save-ReleaseFile {
    param([string]$Name, [string]$OutFile)

    $base = $releaseBase
    if ($base -match '^https?://') {
        try {
            Invoke-WebRequest -Uri "$base/$Name" -OutFile $OutFile -ProgressAction SilentlyContinue
        }
        catch {
            throw "Could not download $base/$Name`: $($_.Exception.Message)"
        }
    }
    elseif (Test-Path -LiteralPath "$base/$Name") {
        Copy-Item -LiteralPath "$base/$Name" -Destination $OutFile
    }
    else {
        throw "No release file '$base/$Name'."
    }
}

function Install-Release {
    $work = Join-Path ([IO.Path]::GetTempPath()) "lv-release-$([guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Path $work | Out-Null
    try {
        $zip = Join-Path $work 'LocalVault.zip'
        Save-ReleaseFile 'LocalVault.zip' $zip
        Save-ReleaseFile 'LocalVault.zip.sha256' "$zip.sha256"
        $expected = ((Get-Content -LiteralPath "$zip.sha256" -TotalCount 1) -split '\s+')[0]
        if ($expected -ne (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash) {
            throw "LocalVault.zip doesn't match its SHA256 file. Nothing was installed."
        }

        $unpacked = Join-Path $work 'release'
        Expand-Archive -LiteralPath $zip -DestinationPath $unpacked
        $manifestFile = Get-ChildItem -LiteralPath (Join-Path $unpacked 'LocalVault') -Filter 'LocalVault.psd1' -Recurse | Select-Object -First 1
        if (-not $manifestFile) { throw 'The release has no module manifest. Nothing was installed.' }
        $candidate = Import-PowerShellDataFile -LiteralPath $manifestFile.FullName
        $release = Get-ReleaseOrder $candidate
        $installed = Get-InstalledRelease $candidate
        if ($installed -and $installed.Text -eq $release.Text) {
            "LocalVault $($release.Text) is up to date."
            Complete-Install -RegisterModulePath (-not $SkipModulePath.IsPresent)
            return
        }

        $inner = Join-Path $unpacked 'install.ps1'
        if (-not $AllowUnsigned) { Assert-Signed @($inner) }
        "Installing LocalVault $($release.Text)."
        $forwarded = @{
            Destination       = $Destination
            LegacyDestination = $LegacyDestination
            SkillDestination  = $SkillDestination
            SkipModulePath    = $SkipModulePath
            AllowUnsigned     = $AllowUnsigned
            Downgrade         = $Downgrade
        }
        & $inner @forwarded
    }
    finally {
        Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if (Test-SamePath $Destination $LegacyDestination) {
    if (Test-Path -LiteralPath (Join-Path $LegacyDestination 'LocalVault')) {
        "Installing into '$defaultDestination' instead of '$Destination': a module folder synced by OneDrive reaches your other PCs."
    }
    $Destination = $defaultDestination
}

if ($Latest -and $Version) { throw 'Give -Version or -Latest, not both.' }
$releaseBase = if ($Version) { "$ReleaseSource/download/$Version" } else { "$ReleaseSource/latest/download" }

$bundled = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'LocalVault') -Directory -ErrorAction SilentlyContinue |
    Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'LocalVault.psd1') } | Select-Object -First 1
$checkout = Test-Path -LiteralPath (Join-Path $PSScriptRoot 'src/LocalVault.PowerShell/LocalVault.psd1')

if ($Latest -or $Version -or (-not $ModulePath -and -not $bundled -and -not $checkout)) {
    Install-Release
    return
}

if (-not $ModulePath -and $bundled) {
    $ModulePath = $bundled.FullName
    if (-not $AllowUnsigned) { Assert-Signed (Get-SignableFile $ModulePath) }
}
elseif (-not $ModulePath) {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw 'The .NET 10 SDK is required. Install it with: winget install --exact --id Microsoft.DotNet.SDK.10'
    }
    if (-not (Get-Module Microsoft.PowerShell.PlatyPS -ListAvailable | Where-Object Version -EQ '1.0.3')) {
        Install-PSResource Microsoft.PowerShell.PlatyPS -Version 1.0.3 -Repository PSGallery -Scope CurrentUser -TrustRepository -Quiet
    }
    $ModulePath = & (Join-Path $PSScriptRoot 'tools/build.ps1') | Select-Object -Last 1
}

$required = 'LocalVault.psd1', 'LocalVault.PowerShell.dll', 'LocalVault.Core.dll', 'LocalVault.KeePass.dll', 'KeePassLib.dll'
$absent = @($required | Where-Object { -not $ModulePath -or -not (Test-Path -LiteralPath (Join-Path $ModulePath $_)) })
if ($absent.Count -gt 0) {
    throw "The build did not produce $($absent -join ', ') in '$ModulePath'. Nothing was installed."
}

$manifest = Import-PowerShellDataFile -LiteralPath (Join-Path $ModulePath 'LocalVault.psd1')
Assert-NotOlder $manifest $Downgrade.IsPresent
$moduleVersion = $manifest.ModuleVersion
$prerelease = $manifest.PrivateData.PSData['Prerelease']
$label = if ($prerelease) { "$moduleVersion-$prerelease" } else { $moduleVersion }
$target = Join-Path $Destination "LocalVault/$moduleVersion"

$parent = Split-Path -Parent $target
New-Item -ItemType Directory -Path $parent -Force | Out-Null
foreach ($leftover in Get-ChildItem -LiteralPath $parent -Directory -Filter "$moduleVersion.old-*") {
    Remove-Item -LiteralPath $leftover.FullName -Recurse -Force -ErrorAction SilentlyContinue
}

$staging = "$target.new-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $staging | Out-Null
Copy-Item -Path (Join-Path $ModulePath '*') -Destination $staging -Recurse

if (Test-Path -LiteralPath $target) {
    $previous = "$target.old-$([guid]::NewGuid().ToString('N'))"
    try {
        Move-Item -LiteralPath $target -Destination $previous
    }
    catch {
        Remove-Item -LiteralPath $staging -Recurse -Force
        throw "LocalVault $moduleVersion in '$target' is in use. Close the PowerShell sessions that imported it and run install.ps1 again."
    }
    Move-Item -LiteralPath $staging -Destination $target
    try {
        Remove-Item -LiteralPath $previous -Recurse -Force -ErrorAction Stop
    }
    catch {
        Write-Warning "The previous copy stays in '$previous' while a PowerShell session uses it; the next install.ps1 run deletes it."
    }
}
else {
    Move-Item -LiteralPath $staging -Destination $target
}
"LocalVault $label installed in '$target'."
Complete-Install -RegisterModulePath (-not $SkipModulePath.IsPresent)
"$($PSStyle.Foreground.Red)Only active in new PowerShell windows.$($PSStyle.Reset)"

$skillSource = Join-Path $PSScriptRoot 'skills/localvault'
if ((Test-Path -LiteralPath $skillSource) -and (Test-Path -LiteralPath (Split-Path -Parent $SkillDestination))) {
    $skill = Join-Path $SkillDestination 'localvault'
    if (Test-Path -LiteralPath $skill) {
        Remove-Item -LiteralPath $skill -Recurse -Force
    }
    New-Item -ItemType Directory -Path $SkillDestination -Force | Out-Null
    Copy-Item -LiteralPath $skillSource -Destination $skill -Recurse
    "Claude skill installed in '$skill'."
}
