using System.Security.Cryptography;
using System.Text;

namespace LocalVault.Core;

public static class BackupLocator
{
    public const string OneDriveVariable = "OneDrive";
    public const string RootFolder = "LocalVault";
    private const int HashCharacters = 8;

    public static string FolderName(VaultLocation location, string machineName)
    {
        ArgumentNullException.ThrowIfNull(location);
        var project = Path.GetFileName(location.GitRoot ?? location.Directory);
        var hash = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(location.Path).ToLowerInvariant())))[..HashCharacters];
        return $"{project}-{hash}-{machineName}";
    }

    public static string DefaultDestination(VaultLocation location, string machineName)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(OneDriveVariable)))
        {
            throw new VaultException("OneDrive is not set up on this machine. Give -Destination.");
        }

        return Path.Combine($"%{OneDriveVariable}%", RootFolder, FolderName(location, machineName));
    }
}
