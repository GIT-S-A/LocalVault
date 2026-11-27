namespace LocalVault.Engine;

public sealed record StoreResolution(string Vault, StoreReference? Reference, StoreDefinition? Definition, string? Problem)
{
    public string? DatabaseId => Reference?.Id ?? Definition?.Id;

    public string Label => Reference?.Name ?? Definition?.Name ?? Reference?.Id ?? "default";
}

public static class StoreResolver
{
    public static StoreResolution For(VaultLocation location, string? storeName, StoreRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(registry);
        var vault = VaultLabel(location);
        if (storeName is not null)
        {
            var named = Find(storeName, location, registry);
            return new StoreResolution(vault, new StoreReference(storeName, null), named, named is null
                ? $"No store named '{storeName}' in {ProjectFile.FileName} or on this PC. {Known(location, registry)}"
                : null);
        }

        var reference = ProjectFile.StoreFor(location);
        if (reference is null)
        {
            var fallback = registry.Default;
            var stores = registry.Load();
            return new StoreResolution(vault, null, fallback, fallback is not null
                ? null
                : stores.Count == 0
                    ? $"{vault} has no store: none is named in {ProjectFile.FileName} and none is defined on this PC. Define one with lv store add <target> -Name <name> (Register-VaultStore)."
                    : $"{vault} has no store: none is named in {ProjectFile.FileName}, and this PC has several stores and no default. Run lv store use <name>, or give -Store <name>. {Known(location, registry)}");
        }

        var definition = Find(reference, location, registry);
        return new StoreResolution(vault, reference, definition, definition is null
            ? $"{vault} uses store '{reference.Name ?? reference.Id}', which isn't defined on this PC. Define it with lv store add <target> -Name {reference.Name ?? "<name>"} (Register-VaultStore)."
            : null);
    }

    public static IReadOnlyList<StoreResolution> MirrorsFor(VaultLocation location, StoreResolution primary, StoreRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(registry);
        var vault = VaultLabel(location);
        var mirrors = new List<StoreResolution>();
        foreach (var entry in ProjectFile.ReadEntries(location).Where(entry => entry.IsMirror))
        {
            var reference = new StoreReference(entry.Name, entry.Id);
            var definition = Find(reference, location, registry);
            mirrors.Add(new StoreResolution(vault, reference, definition, definition is null
                ? $"{vault}: mirror '{reference.Name ?? reference.Id}' isn't defined on this PC."
                : null));
        }

        foreach (var store in registry.Load().Where(store => store.IsMirror))
        {
            mirrors.Add(new StoreResolution(vault, new StoreReference(store.Name, store.Id), store, null));
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in Keys(primary))
        {
            seen.Add(key);
        }

        return mirrors.Where(mirror => Keys(mirror).All(seen.Add)).ToList();
    }

    public static StoreDefinition? Find(string name, VaultLocation location, StoreRegistry registry) =>
        Find(new StoreReference(name, null), location, registry);

    public static StoreDefinition? Find(StoreReference reference, VaultLocation location, StoreRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(registry);
        if (reference.Name is not null
            && ProjectFile.ReadStores(location).FirstOrDefault(store => string.Equals(store.Name, reference.Name, StringComparison.OrdinalIgnoreCase)) is { } project)
        {
            return WithLocalDetails(project, registry);
        }

        return (reference.Id is null ? null : registry.FindById(reference.Id))
            ?? (reference.Name is null ? null : registry.FindByName(reference.Name));
    }

    public static string VaultLabel(VaultLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        return location.GitRoot is null ? location.Path : Path.GetRelativePath(location.GitRoot, location.Path).Replace('\\', '/');
    }

    private static IEnumerable<string> Keys(StoreResolution resolution)
    {
        if (resolution.DatabaseId is { } id)
        {
            yield return "id:" + id;
        }

        if (resolution.Definition is { } definition)
        {
            yield return "target:" + Path.GetFullPath(definition.ExpandedTarget);
        }
        else if (resolution.Reference?.Name is { } name)
        {
            yield return "name:" + name;
        }
    }

    private static StoreDefinition WithLocalDetails(StoreDefinition project, StoreRegistry registry)
    {
        var local = (project.Id is null ? null : registry.FindById(project.Id)) ?? registry.FindByTarget(project.ExpandedTarget);
        if (local is null)
        {
            return project;
        }

        return project with
        {
            Target = File.Exists(project.ExpandedTarget) ? project.Target : local.Target,
            KeyFile = local.KeyFile,
            UnlockWith = local.UnlockWith,
        };
    }

    private static string Known(VaultLocation location, StoreRegistry registry)
    {
        var names = ProjectFile.ReadStores(location).Select(store => store.Name)
            .Concat(registry.Load().Select(store => store.Name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return names.Count == 0 ? "No store is defined." : "Defined: " + string.Join(", ", names) + ".";
    }
}
