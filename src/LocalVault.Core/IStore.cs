namespace LocalVault.Core;

public interface IStore : IDisposable
{
    string Path { get; }

    string DatabaseId { get; }

    StoreContent Read(IReadOnlyList<string> groupPath, IReferenceStores? stores = null);

    void Put(IReadOnlyList<string> groupPath, VaultEntry entry, bool hello = false);

    void PutPersonalValue(IReadOnlyList<string> groupPath, VaultEntry entry, bool withDeclaration, bool hello = false);

    void SetHelloFlags(IReadOnlyList<string> groupPath, EntryName name, bool hello, bool requiredHello);

    void Delete(IReadOnlyList<string> groupPath, EntryName name);

    StoreItem ReadReferenced(ItemLink link, string? storedPath, IReadOnlyList<string> hosts, string storeName);

    StoreItem ReadReferenceTarget(string search, EntryName name, string? shape, string? storedPath, IReadOnlyList<string> hosts, bool personal, string storeName);

    bool EnsureInstallInstructions();

    bool MaintainOrigin(IReadOnlyList<string> projectGroup, RepositoryOrigin origin);

    void Save();
}

public interface IUnlockingStore : IStore
{
    UnlockTarget? FindAutoOpen(string? databaseId, IEnumerable<string> knownPaths, string machineName);

    void RememberAutoOpen(string title, string path, string? keyFile, ReadOnlySpan<char> password, string databaseId);
}

public interface IStoreProvider
{
    string Type { get; }

    IStore Open(string target, string? keyFile, ReadOnlySpan<char> password);

    // Reads the id without the password; null when the target doesn't carry one.
    string? ReadDatabaseId(string target);
}
