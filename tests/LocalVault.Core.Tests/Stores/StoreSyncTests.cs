using System.Text;

namespace LocalVault.Core.Tests;

public sealed class StoreSyncTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly IReadOnlyList<string> Group = ["LocalVault", "proj"];

    private readonly MemoryStore store = new("0123456789abcdef0123456789abcdef");

    public void Dispose() => store.Dispose();

    [Fact]
    public void SaveSendsSharedEntriesOnceAndKeepsLocalOnes()
    {
        var body = Body(("a", "1"), ("mine", "2"));
        body.SetSharing(EntryName.Parse("mine"), local: true);

        var first = Save(body);
        var again = Save(body);

        Assert.Equal(1, first.Added);
        Assert.Equal(0, again.Added + again.Updated + again.Deleted);
        Assert.Equal(["a"], store.Titles(Group));
        Assert.NotNull(body.Find(EntryName.Parse("a"))!.KeePassBaseFor(store.DatabaseId));
    }

    [Fact]
    public void TeammateChangeIsTakenOnLoadAndBlocksSave()
    {
        var mine = Body(("a", "1"));
        Save(mine);
        var theirs = Body();
        Load(theirs);
        Set(theirs, "a", "2");
        Save(theirs);

        Set(mine, "b", "x");
        Assert.Equal(StoreSyncState.ChangedInKeePass, Assert.Single(Save(mine).Skipped).State);

        Assert.Equal(1, Load(mine).Taken);
        Assert.Equal("2", Value(mine, "a"));
    }

    [Fact]
    public void ConflictKeepsBothSidesUnlessReplaced()
    {
        var mine = Body(("a", "1"));
        Save(mine);
        var theirs = Body();
        Load(theirs);
        Set(theirs, "a", "theirs");
        Save(theirs);
        Set(mine, "a", "mine");

        Assert.Equal(StoreSyncState.Conflict, Assert.Single(Save(mine).Skipped).State);
        Assert.Equal(1, Load(mine).Kept);
        Assert.Equal("mine", Value(mine, "a"));

        Save(mine, replace: true);
        var fresh = Body();
        Load(fresh);
        Assert.Equal("mine", Value(fresh, "a"));
    }

    [Fact]
    public void RemovalHereReachesTheStore()
    {
        var body = Body(("a", "1"), ("b", "2"));
        Save(body);
        body.Remove(EntryName.Parse("a"));

        Assert.Equal(0, Load(body).Added);
        Assert.Equal(1, Save(body).Deleted);

        Assert.Equal(["b"], store.Titles(Group));
        Assert.Empty(body.KeePassRemovals);
    }

    [Fact]
    public void RemovalInTheStoreIsAppliedOnlyWhenAccepted()
    {
        var kept = Body(("a", "1"));
        var accepted = Body();
        Save(kept);
        Assert.Equal(1, Load(accepted).Added);
        store.Delete(Group, EntryName.Parse("a"));

        Assert.Equal(1, Load(kept).Kept);
        Assert.Null(kept.Find(EntryName.Parse("a"))!.KeePassBase);
        Assert.Equal(1, Save(kept).Added);

        store.Delete(Group, EntryName.Parse("a"));
        Assert.Equal(1, Load(accepted, remove: true).Removed);
        Assert.Null(accepted.Find(EntryName.Parse("a")));
    }

    private StoreSaveResult Save(VaultBody body, bool replace = false)
    {
        using var content = store.Read(Group);
        var result = StoreSync.Save(body, content, store, Group, _ => replace, mirror: false);
        StoreSync.RecordSaved(body, result, Now);
        return result;
    }

    private StoreLoadResult Load(VaultBody body, bool remove = false)
    {
        using var content = store.Read(Group);
        return StoreSync.Load(body, content, _ => false, _ => remove, Now);
    }

    private static VaultBody Body(params (string Name, string Value)[] entries)
    {
        var body = VaultBody.CreateEmpty();
        foreach (var (name, value) in entries)
        {
            Set(body, name, value);
        }

        return body;
    }

    private static void Set(VaultBody body, string name, string value) =>
        body.Set(VaultEntry.NewSecret(EntryName.Parse(name), Encoding.UTF8.GetBytes(value), Now));

    private static string Value(VaultBody body, string name) =>
        Encoding.UTF8.GetString(body.Find(EntryName.Parse(name))!.GetValue());
}
