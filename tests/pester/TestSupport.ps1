Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $env:LOCALVAULT_MODULE_PATH 'LocalVault.psd1') -Force
Add-Type -LiteralPath (Join-Path $env:LOCALVAULT_MODULE_PATH 'KeePassLib.dll')
$env:LOCALAPPDATA = Join-Path ([IO.Path]::GetTempPath()) ('lv-pester-appdata-' + [guid]::NewGuid().ToString('N'))

function ConvertTo-TestSecret {
    [CmdletBinding()]
    [OutputType([securestring])]
    param([Parameter(Mandatory)][string]$Text)

    $secure = [securestring]::new()
    foreach ($character in $Text.ToCharArray()) {
        $secure.AppendChar($character)
    }
    $secure.MakeReadOnly()
    $secure
}

function Initialize-TestFolder {
    [CmdletBinding()]
    [OutputType([string])]
    param([switch]$Git)

    $folder = Join-Path ([IO.Path]::GetTempPath()) ("lv-pester-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $folder | Out-Null
    if ($Git) {
        git -C $folder init --quiet
        Set-Content -LiteralPath (Join-Path $folder '.gitignore') -Value 'secrets.local.*'
    }
    $folder
}

function Clear-TestFolder {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Folder)

    if (Test-Path -LiteralPath $Folder) {
        Remove-Item -LiteralPath $Folder -Recurse -Force
    }
}

function Initialize-LegacyVault {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Folder,
        [Parameter(Mandatory)][hashtable]$Values
    )

    $table = @{}
    foreach ($name in $Values.Keys) {
        $table[$name] = ConvertTo-TestSecret $Values[$name]
    }
    $path = Join-Path $Folder 'secrets.local.clixml'
    $table | Export-Clixml -LiteralPath $path
    $path
}

function ConvertTo-V1Export {
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)][hashtable]$Values,
        [Parameter(Mandatory)][securestring]$Secret
    )

    $plain = [Net.NetworkCredential]::new('', $Secret).Password
    $body = [Text.Encoding]::UTF8.GetBytes(($Values | ConvertTo-Json -Compress))
    $salt = [Security.Cryptography.RandomNumberGenerator]::GetBytes(16)
    $nonce = [Security.Cryptography.RandomNumberGenerator]::GetBytes(12)
    $kdf = [Security.Cryptography.Rfc2898DeriveBytes]::new($plain, $salt, 210000, [Security.Cryptography.HashAlgorithmName]::SHA256)
    try { $key = $kdf.GetBytes(32) } finally { $kdf.Dispose() }
    $cipher = [byte[]]::new($body.Length)
    $tag = [byte[]]::new(16)
    $aes = [Security.Cryptography.AesGcm]::new($key, 16)
    try { $aes.Encrypt($nonce, $body, $cipher, $tag) } finally { $aes.Dispose() }
    @('SAAS-VAULT-1', [Convert]::ToBase64String($salt), [Convert]::ToBase64String($nonce),
        [Convert]::ToBase64String($tag), [Convert]::ToBase64String($cipher)) -join "`r`n"
}


function Initialize-TestKeePass {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Master
    )

    $key = [KeePassLib.Keys.CompositeKey]::new()
    $key.AddUserKey([KeePassLib.Keys.KcpPassword]::new($Master))
    $database = [KeePassLib.PwDatabase]::new()
    $database.New([KeePassLib.Serialization.IOConnectionInfo]::FromPath($Path), $key)
    $kdf = [KeePassLib.Cryptography.KeyDerivation.AesKdf]::new().GetDefaultParameters()
    $kdf.SetUInt64([KeePassLib.Cryptography.KeyDerivation.AesKdf]::ParamRounds, 1000)
    $database.KdfParameters = $kdf
    $database.Save($null)
    $database.Close()
}