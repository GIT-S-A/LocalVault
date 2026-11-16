using LocalVault.Core;

namespace LocalVault.PowerShell;

internal static class RepositoryVaults
{
    public static IReadOnlyList<VaultLocation> Of(VaultLocation current, bool onlyCurrent)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (onlyCurrent)
        {
            return [current];
        }

        var listed = ProjectFile.ReadVaults(current)
            .Select(vault => VaultLocation.Resolve(vault.Path, null, Path.GetDirectoryName(vault.Path)!))
            .ToList();
        if (listed.Count == 0 || (File.Exists(current.Path) && !listed.Any(l => Same(l, current))))
        {
            listed.Insert(0, current);
        }

        return listed.DistinctBy(l => l.Path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static string Label(VaultLocation location) => StoreResolver.VaultLabel(location);

    public static VaultFile StoreFor(VaultLocation location) => new(location, new ClixmlLegacyReader());

    private static bool Same(VaultLocation a, VaultLocation b) => string.Equals(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);
}
