namespace LocalVault.Core.Tests;

internal sealed class MemoryStore(string databaseId) : IStore
{
    private readonly Dictionary<string, Dictionary<string, Stored>> groups = new(StringComparer.Ordinal);

    public string Path => "memory";

    public string DatabaseId => databaseId;

    public IReadOnlyList<string> Titles(IReadOnlyList<string> groupPath) => [.. Group(groupPath).Values.Select(stored => stored.Name.FullName).Order()];

    public StoreContent Read(IReadOnlyList<string> groupPath, IReferenceStores? stores = null) =>
        new([.. Group(groupPath).Values.Select(stored => new StoreItem(stored.Name, stored.Kind, stored.StoredPath, (byte[])stored.Value.Clone()))], [], DatabaseId);

    public void Put(IReadOnlyList<string> groupPath, VaultEntry entry, bool hello = false)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.IsRequired || entry.IsCredential || entry.Reference is not null || entry.Ssh is not null)
        {
            throw new NotSupportedException($"{nameof(MemoryStore)} holds secrets and files only.");
        }

        Group(groupPath)[entry.Name.FullName] = new Stored(entry.Name, entry.Kind, entry.StoredPath, entry.GetValue());
    }

    public void PutPersonalValue(IReadOnlyList<string> groupPath, VaultEntry entry, bool withDeclaration, bool hello = false) =>
        throw new NotSupportedException();

    public void SetHelloFlags(IReadOnlyList<string> groupPath, EntryName name, bool hello, bool requiredHello) =>
        throw new NotSupportedException();

    public void Delete(IReadOnlyList<string> groupPath, EntryName name) => Group(groupPath).Remove(name.FullName);

    public StoreItem ReadReferenced(ItemLink link, string? storedPath, IReadOnlyList<string> hosts, string storeName) =>
        throw new NotSupportedException();

    public StoreItem ReadReferenceTarget(string search, EntryName name, string? shape, string? storedPath, IReadOnlyList<string> hosts, bool personal, string storeName) =>
        throw new NotSupportedException();

    public bool EnsureInstallInstructions() => false;

    public bool MaintainOrigin(IReadOnlyList<string> projectGroup, RepositoryOrigin origin) => false;

    public void Save()
    {
    }

    public void Dispose()
    {
    }

    private Dictionary<string, Stored> Group(IReadOnlyList<string> groupPath)
    {
        var key = StoreGroupPath.Display(groupPath);
        if (!groups.TryGetValue(key, out var group))
        {
            group = new Dictionary<string, Stored>(EntryName.Comparer);
            groups[key] = group;
        }

        return group;
    }

    private sealed record Stored(EntryName Name, string Kind, string? StoredPath, byte[] Value);
}
