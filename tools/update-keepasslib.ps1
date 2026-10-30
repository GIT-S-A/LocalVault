#Requires -Version 7.6
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+(\.\d+)?$')]
    [string]$Version,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9A-Fa-f\s]{64,}$')]
    [string]$Sha256
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$signerFingerprint = 'D95044283EE948D911E8B606A4F762DC58C6F98E'
$root = Split-Path -Parent $PSScriptRoot
$vendor = Join-Path $root 'vendor/KeePassLib'
$expectedHash = ($Sha256 -replace '\s', '').ToUpperInvariant()
$zipName = "KeePass-$Version-Source.zip"
$zipUrl = "https://sourceforge.net/projects/keepass/files/KeePass%202.x/$Version/$zipName/download"
$signatureUrl = "https://keepass.info/integrity/v2/$zipName.asc"
$keyUrl = 'https://keepass.info/integrity/DominikReichl.asc'

$gpgCommand = Get-Command gpg -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
$gpg = if ($gpgCommand) { $gpgCommand.Source } else { Join-Path $env:ProgramFiles 'Git/usr/bin/gpg.exe' }
if (-not (Test-Path -LiteralPath $gpg)) {
    throw 'gpg was not found. Install Git for Windows, which includes it.'
}

$work = Join-Path ([IO.Path]::GetTempPath()) "lv-keepasslib-$([guid]::NewGuid().ToString('N'))"
$download = Join-Path $work 'download'
$extract = Join-Path $work 'extract'
$gnupgHome = Join-Path $work 'gnupg'
New-Item -ItemType Directory -Path $download, $extract, $gnupgHome | Out-Null
try {
    $zip = Join-Path $download $zipName
    $signature = "$zip.asc"
    $key = Join-Path $download 'signer.asc'
    Invoke-WebRequest -Uri $zipUrl -OutFile $zip -UserAgent 'curl'
    Invoke-WebRequest -Uri $signatureUrl -OutFile $signature
    Invoke-WebRequest -Uri $keyUrl -OutFile $key

    $actualHash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
    if ($actualHash -ne $expectedHash) {
        throw "SHA-256 of $zipName is $actualHash, not $expectedHash. Nothing was changed."
    }

    Set-Content -LiteralPath $key -Value ((Get-Content -LiteralPath $key -Raw) -replace "`r`n", "`n") -NoNewline
    $msys = $gpg -match '[\\/]usr[\\/]bin[\\/]gpg(\.exe)?$'
    $gpgPath = { param([string]$Path) if ($msys) { '/' + $Path.Substring(0, 1).ToLowerInvariant() + $Path.Substring(2).Replace('\', '/') } else { $Path } }
    $gpgHome = & $gpgPath $gnupgHome
    & $gpg --homedir $gpgHome --batch --quiet --import (& $gpgPath $key) 2>&1 | Out-Null
    $status = & $gpg --homedir $gpgHome --batch --status-fd 1 --verify (& $gpgPath $signature) (& $gpgPath $zip) 2>$null
    if (-not ($status -match "^\[GNUPG:\] VALIDSIG \S+ .* $signerFingerprint$")) {
        throw "The OpenPGP signature of $zipName is not valid for key $signerFingerprint. Nothing was changed."
    }

    Expand-Archive -LiteralPath $zip -DestinationPath $extract
    $source = Join-Path $extract 'KeePassLib'
    $license = Join-Path $extract 'Docs/License.txt'
    if (-not (Test-Path -LiteralPath $source) -or -not (Test-Path -LiteralPath $license)) {
        throw "$zipName has no KeePassLib folder or Docs/License.txt. Nothing was changed."
    }

    $staging = "$vendor.new-$([guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Path $staging | Out-Null
    Get-ChildItem -LiteralPath $source -Recurse -File -Filter '*.cs' | ForEach-Object {
        $relative = [IO.Path]::GetRelativePath($source, $_.FullName)
        $target = Join-Path $staging $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        Copy-Item -LiteralPath $_.FullName -Destination $target
    }
    Copy-Item -LiteralPath $license -Destination (Join-Path $staging 'License.txt')
    @(
        "KeePassLib from KeePass $Version, unmodified."
        "Source: $zipUrl"
        "SHA-256: $actualHash"
        "OpenPGP signer: $signerFingerprint"
        "Update: tools/update-keepasslib.ps1 -Version <x.y> -Sha256 <hash from https://keepass.info/integrity.html>"
    ) | Set-Content -LiteralPath (Join-Path $staging 'UPSTREAM.txt')

    if (Test-Path -LiteralPath $vendor) {
        Remove-Item -LiteralPath $vendor -Recurse -Force
    }
    New-Item -ItemType Directory -Path (Split-Path -Parent $vendor) -Force | Out-Null
    Move-Item -LiteralPath $staging -Destination $vendor
    "KeePassLib $Version in '$vendor'. Build and run tools/test.ps1, then review 'git diff --stat vendor'."
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
