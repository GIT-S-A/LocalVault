using LocalVault.Core;

namespace LocalVault.PowerShell;

internal sealed class StoreAccess(IVaultHost host, string? storeName) : IDisposable
{
    private const int MaxUnlockChain = 8;
    private readonly StoreRegistry registry = new(StoreRegistry.DefaultPath);
    private readonly Dictionary<string, (IStore Vault, StoreDefinition Store)> opened = new(StringComparer.OrdinalIgnoreCase);

    public StoreRegistry Registry => registry;

    public bool StoreNamed => storeName is not null;

    public StoreResolution Resolve(VaultLocation location) => StoreResolver.For(location, storeName, registry);

    public IReadOnlyList<StoreResolution> MirrorsFor(VaultLocation location, StoreResolution primary) =>
        storeName is null ? StoreResolver.MirrorsFor(location, primary, registry) : [];

    public (IStore Vault, StoreDefinition Store) For(VaultLocation location) => For(Resolve(location));

    public (IStore Vault, StoreDefinition Store) For(StoreResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        var id = resolution.DatabaseId;
        if (id is not null && opened.TryGetValue(id, out var hit))
        {
            return hit;
        }

        var candidate = resolution.Definition ?? throw new StoreNotDefinedException(resolution.Problem!);
        var pair = OpenStore(candidate, id, 0);
        if (id is not null && !string.Equals(pair.Vault.DatabaseId, id, StringComparison.OrdinalIgnoreCase))
        {
            throw new VaultException(
                $"{resolution.Vault} uses store '{resolution.Label}', and '{pair.Store.ExpandedTarget}' is another database. " +
                "Point the store at the right file with lv store add.");
        }

        registry.RecordId(pair.Store, pair.Vault.DatabaseId);
        return pair;
    }

    public IStore Explicit(StoreDefinition store)
    {
        ArgumentNullException.ThrowIfNull(store);
        return OpenStore(store, store.Id, 0).Vault;
    }

    public void Dispose()
    {
        foreach (var vault in opened.Values.Select(pair => pair.Vault).Distinct())
        {
            vault.Dispose();
        }
    }

    private static string TargetKey(StoreDefinition store) => "target:" + Path.GetFullPath(store.ExpandedTarget);

    private (IStore Vault, StoreDefinition Store) Remember(IStore vault, StoreDefinition store)
    {
        if (store.Id is { } recorded && !string.Equals(recorded, vault.DatabaseId, StringComparison.OrdinalIgnoreCase))
        {
            vault.Dispose();
            throw new VaultException(
                $"'{store.ExpandedTarget}' isn't the KeePass database that store '{store.Name}' was added with: another database replaced it. " +
                $"If that is intended, add the store again: lv store add {store.Name} \"{store.Target}\"{(store.Scope == StoreScope.Project ? " --project" : string.Empty)}");
        }

        var pair = (vault, store);
        opened[vault.DatabaseId] = pair;
        opened[TargetKey(store)] = pair;
        return pair;
    }

    private (IStore Vault, StoreDefinition Store) OpenStore(StoreDefinition store, string? id, int depth)
    {
        if (opened.TryGetValue(TargetKey(store), out var hit))
        {
            return hit;
        }

        _ = StoreProviders.For(store);
        if (store.UnlockWith is null)
        {
            host.Tell($"Opening store '{store.Name}' (KeePass {store.ExpandedTarget}).");
            return Remember(Prompted(store), store);
        }

        if (depth >= MaxUnlockChain)
        {
            throw new VaultException($"Store '{store.Name}' is unlocked through more than {MaxUnlockChain} stores. Check --unlock-with in lv store list.");
        }

        var unlockerStore = registry.FindByName(store.UnlockWith)
            ?? throw new VaultException($"Store '{store.Name}' is unlocked with '{store.UnlockWith}', which isn't defined on this PC. Add it with lv store add {store.UnlockWith} <target>.");
        var (unlocking, _) = OpenStore(unlockerStore, unlockerStore.Id, depth + 1);
        var unlocker = unlocking as IUnlockingStore
            ?? throw new VaultException($"Store '{store.Name}' is unlocked with '{unlockerStore.Name}', a {unlockerStore.Type} store, which can't keep other stores' passwords. Remove it with lv store set {store.Name} --no-unlock.");
        registry.RecordId(unlockerStore, unlocker.DatabaseId);
        if (unlocker.FindAutoOpen(id ?? store.Id, [store.ExpandedTarget], Environment.MachineName) is { } target)
        {
            using var keptPrompt = new StorePrompt(host, store.Name, target);
            host.Tell($"Opening store '{store.Name}' (KeePass {target.Path}) with the password kept in '{unlockerStore.Name}'.");
            return Remember(StoreProviders.For(store).Open(target.Path, target.KeyFile ?? store.ExpandedKeyFile, keptPrompt), store);
        }

        host.Tell($"Opening store '{store.Name}' (KeePass {store.ExpandedTarget}). '{unlockerStore.Name}' doesn't hold its password yet.");
        using var prompt = new StorePrompt(host, store.Name);
        var vault = StoreProviders.For(store).Open(store.ExpandedTarget, store.ExpandedKeyFile, prompt);
        if (host.CanConfirm && host.AskYesNo($"Keep the password of '{store.Name}' in '{unlockerStore.Name}' (AutoOpen group), so that next time '{store.Name}' opens with it?"))
        {
            unlocker.RememberAutoOpen(store.Name, store.ExpandedTarget, store.ExpandedKeyFile, prompt.FirstAnswer, vault.DatabaseId);
            unlocker.Save();
            host.Tell($"Kept in '{unlockerStore.Name}'. If KeePass has {Path.GetFileName(unlockerStore.ExpandedTarget)} open, reload it there before saving.");
        }

        return Remember(vault, store);
    }

    private IStore Prompted(StoreDefinition store)
    {
        using var prompt = new StorePrompt(host, store.Name);
        return StoreProviders.For(store).Open(store.ExpandedTarget, store.ExpandedKeyFile, prompt);
    }
}
