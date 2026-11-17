using LocalVault.Core;
using LocalVault.Engine;

namespace LocalVault.PowerShell;

internal sealed class ReferenceStores(StoreAccess access, VaultLocation location, string? vaultStoreId) : IReferenceStores
{
    private readonly Dictionary<string, string> failures = new(StringComparer.OrdinalIgnoreCase);

    public ReferenceStore Open(ItemReference reference, bool personal)
    {
        ArgumentNullException.ThrowIfNull(reference);
        var id = reference.StoreId;
        var (definition, name) = Find(id);
        if (definition is null)
        {
            throw new VaultException(name is null
                ? $"its value is in a KeePass database this PC doesn't know (id {id})."
                : $"its value is in store '{name}', which isn't defined on this PC. Add it with lv store add {name} <path to .kdbx>.");
        }

        if (!personal && !MayHoldSharedValues(id))
        {
            throw new VaultException($"shared values can only come from the project's own stores, and store '{definition.Name}' isn't one of them.");
        }

        if (failures.TryGetValue(id, out var failure))
        {
            throw new VaultException(failure);
        }

        try
        {
            var (vault, store) = access.For(new StoreResolution(RepositoryVaults.Label(location), new StoreReference(definition.Name, id), definition, null));
            return new ReferenceStore(vault, store.Name);
        }
        catch (Exception ex) when (ex is VaultException or IOException or UnauthorizedAccessException)
        {
            failures[id] = $"store '{definition.Name}' couldn't be opened: {ex.Message}";
            throw new VaultException(failures[id], ex);
        }
    }

    public bool MayHoldSharedValues(string id)
    {
        var registry = access.Registry;
        var forbidden = registry.Load()
            .Where(store => store.IsPersonal || registry.Load().Any(other => string.Equals(other.UnlockWith, store.Name, StringComparison.OrdinalIgnoreCase)))
            .Any(store => string.Equals(store.Id, id, StringComparison.OrdinalIgnoreCase));
        var allowed = string.Equals(id, vaultStoreId, StringComparison.OrdinalIgnoreCase)
            || ProjectFile.ReadEntries(location).Any(entry => entry.IsSource && string.Equals(entry.Id, id, StringComparison.OrdinalIgnoreCase));
        return allowed && !forbidden;
    }

    public (StoreDefinition? Definition, string? Name) Find(string id)
    {
        var project = ProjectFile.ReadEntries(location).FirstOrDefault(entry => string.Equals(entry.Id, id, StringComparison.OrdinalIgnoreCase));
        var definition = StoreResolver.Find(new StoreReference(project?.Name, id), location, access.Registry);
        return (definition, definition?.Name ?? project?.Name);
    }
}
