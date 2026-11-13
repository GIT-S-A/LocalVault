using LocalVault.Core;

namespace LocalVault.PowerShell;

// Tab completion reads the vault file only: no store, no prompt, no key adoption, no write.
public static class LocalVaultCompletion
{
    public static IReadOnlyList<string> EntryNames(string? path, string workingDirectory, bool filesOnly) =>
        Names(path, workingDirectory, entry => !filesOnly || entry.Kind == VaultEntry.FileKind);

    public static IReadOnlyList<string> SshKeyNames(string? path, string workingDirectory) =>
        Names(path, workingDirectory, entry => entry.IsSshKey);

    private static List<string> Names(string? path, string workingDirectory, Func<VaultEntry, bool> wanted)
    {
        try
        {
            var location = VaultLocation.Resolve(path, Environment.GetEnvironmentVariable(VaultLocation.EnvironmentVariable), workingDirectory);
            return RepositoryVaults.StoreFor(location).Read().Body.Entries
                .Where(wanted)
                .Select(entry => entry.Name.FullName)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is VaultException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
