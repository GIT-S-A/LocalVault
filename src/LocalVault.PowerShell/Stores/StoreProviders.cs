using LocalVault.Core;
using LocalVault.Engine;
using LocalVault.KeePass;

namespace LocalVault.PowerShell;

internal static class StoreProviders
{
    private static readonly IStoreProvider[] All = [new KeePassStoreProvider()];

    public static IStoreProvider For(StoreDefinition store)
    {
        ArgumentNullException.ThrowIfNull(store);
        return All.FirstOrDefault(provider => provider.Type == store.Type)
            ?? throw new VaultException($"Store '{store.Name}' is of type '{store.Type}', which this LocalVault doesn't support. Update it with lv update.");
    }

    public static string? ReadDatabaseId(string type, string target) =>
        All.FirstOrDefault(provider => provider.Type == type)?.ReadDatabaseId(target);
}
