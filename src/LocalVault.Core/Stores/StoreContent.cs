namespace LocalVault.Core;

public sealed class StoreContent : IDisposable
{
    public StoreContent(
        IReadOnlyList<StoreItem> items,
        IReadOnlyList<string> skipped,
        string databaseId,
        IReadOnlyList<ItemLink>? links = null,
        IReadOnlyList<UnresolvedReference>? unresolved = null)
    {
        DatabaseId = databaseId;
        Items = items;
        Skipped = skipped;
        Links = links ?? [];
        Unresolved = unresolved ?? [];
    }

    public IReadOnlyList<StoreItem> Items { get; }

    public IReadOnlyList<string> Skipped { get; }

    public IReadOnlyList<ItemLink> Links { get; }

    public IReadOnlyList<UnresolvedReference> Unresolved { get; }

    public string DatabaseId { get; }

    public StoreItem? Find(EntryName name) => Items.FirstOrDefault(item => item.Name.Equals(name));

    public ItemLink? LinkFor(EntryName name, bool personal) => Links.FirstOrDefault(link => link.Name.Equals(name) && link.IsPersonal == personal);

    public bool IsUnresolved(EntryName name, bool personal) => Unresolved.Any(u => u.Name.Equals(name) && u.IsPersonal == personal);

    public void Dispose()
    {
        foreach (var item in Items)
        {
            item.Dispose();
        }
    }
}
