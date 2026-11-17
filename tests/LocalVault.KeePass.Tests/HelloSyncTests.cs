using System.Text;
using KeePassLib;
using KeePassLib.Serialization;

namespace LocalVault.KeePass.Tests;

public sealed class HelloSyncTests : IDisposable
{
    private const string Vault = "C:/repo/secrets.local.lv";
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly IReadOnlyList<string> Group = ["LocalVault", "proj"];

    private readonly TempDirectory directory = new();
    private readonly SoftwareHelloKey key = new();
    private readonly HelloEntries hello;
    private readonly string database;

    public HelloSyncTests()
    {
        hello = new HelloEntries(key);
        database = directory.Combine("team.kdbx");
        CreateDatabase(database);
    }

    public void Dispose()
    {
        hello.Dispose();
        key.Dispose();
        directory.Dispose();
    }

    [Fact]
    public void SaveWritesHelloEntriesPlainWithFlagAfterOnePrompt()
    {
        var body = VaultBody.CreateEmpty();
        SetHello(body, "a", "one");
        SetHello(body, "b", "two");
        body.Set(Secret("plain", "three"));
        var before = key.Prompts;

        var result = Save(body);

        Assert.Equal(3, result.Added);
        Assert.Equal(before + 1, key.Prompts);
        Assert.StartsWith("LocalVault: save to the store a, b (", key.Messages[^1], StringComparison.Ordinal);
        Assert.Equal(("one", "true"), Raw("a"));
        Assert.Equal(("two", "true"), Raw("b"));
        Assert.Equal(("three", string.Empty), Raw("plain"));
        Assert.All(Compare(body), c => Assert.Equal(StoreSyncState.Saved, c.State));
    }

    [Fact]
    public void CompareAndUnchangedSaveDoNotPrompt()
    {
        var body = VaultBody.CreateEmpty();
        SetHello(body, "a", "one");
        Save(body);
        var before = key.Prompts;

        Assert.Equal(StoreSyncState.Saved, Assert.Single(Compare(body)).State);
        var result = Save(body);

        Assert.Equal(0, result.Added + result.Updated);
        Assert.Equal(before, key.Prompts);
    }

    [Fact]
    public void LoadSealsFlaggedEntryWithoutPrompt()
    {
        var mine = VaultBody.CreateEmpty();
        SetHello(mine, "a", "one");
        Save(mine);
        var theirs = VaultBody.CreateEmpty();
        var before = key.Prompts;

        var result = Load(theirs);

        Assert.Equal(1, result.Added);
        Assert.Equal(before, key.Prompts);
        var entry = theirs.Find(EntryName.Parse("a"))!;
        Assert.True(entry.IsHello);
        Assert.Equal("secret", entry.Kind);
        Assert.Equal("one", PlainValue(theirs, "a"));
    }

    [Fact]
    public void LoadKeepsHelloWhenStoreLacksTheFlag()
    {
        var body = VaultBody.CreateEmpty();
        SetHello(body, "a", "one");
        Save(body);
        EditRaw("a", "two", hello: false);

        var result = Load(body);

        Assert.Equal(1, result.Taken);
        Assert.True(body.Find(EntryName.Parse("a"))!.IsHello);
        Assert.Equal("two", PlainValue(body, "a"));
    }

    [Fact]
    public void SaveRemovesTheFlagOfAnEntryMovedBackToDpapiWithoutPrompt()
    {
        var body = VaultBody.CreateEmpty();
        SetHello(body, "a", "one");
        Save(body);
        body.Set(hello.Unseal(body, [body.Find(EntryName.Parse("a"))!], "test", Vault).Values.Single());
        var before = key.Prompts;

        var result = Save(body);

        Assert.Equal(1, result.Updated);
        Assert.Equal(before, key.Prompts);
        Assert.Equal(("one", string.Empty), Raw("a"));
    }

    [Fact]
    public void LoadRestoresHelloEntriesAfterKeyLossAndReportsTheOnesNotInTheStore()
    {
        var body = VaultBody.CreateEmpty();
        SetHello(body, "shared", "one");
        var local = Secret("mine", "two");
        body.Set(local);
        body.SetSharing(local.Name, local: true);
        body.Set(hello.Seal(body, body.Find(local.Name)!));
        Save(body);
        var oldKeyId = body.HelloKey!.KeyId;
        key.Delete();
        Assert.Equal(HelloSeal.MissingKey, hello.MissingReason(body, body.Find(EntryName.Parse("shared"))!));

        using (var keePass = KeePassStore.Open(database, null, Master))
        using (var content = keePass.Read(Group))
        {
            Assert.True(StoreSync.HelloLoadChanges(body, content, hello));
        }

        var result = Load(body);

        Assert.Equal(1, result.Taken);
        Assert.NotEqual(oldKeyId, body.HelloKey!.KeyId);
        Assert.Equal("one", PlainValue(body, "shared"));
        var missing = Assert.Single(result.StillMissing);
        Assert.Equal("mine", missing.Name.FullName);
        Assert.Equal(HelloSeal.ChangedKey, missing.Reason);
    }

    [Fact]
    public void LoadSkipsAFlaggedEntryWhenHelloIsUnavailableAndTakesTheRest()
    {
        var mine = VaultBody.CreateEmpty();
        SetHello(mine, "a", "one");
        mine.Set(Secret("plain", "two"));
        Save(mine);
        key.UnavailableReason = "no Hello in tests";
        var theirs = VaultBody.CreateEmpty();

        var result = Load(theirs);

        Assert.Equal(1, result.Added);
        Assert.Equal("a", Assert.Single(result.NotSealed).Name.FullName);
        Assert.Null(theirs.Find(EntryName.Parse("a")));
        Assert.Equal("two", Encoding.UTF8.GetString(theirs.Find(EntryName.Parse("plain"))!.GetValue()));
    }

    [Fact]
    public void SaveSkipsAChangedHelloEntryThatCannotBeRead()
    {
        var body = VaultBody.CreateEmpty();
        SetHello(body, "a", "one");
        key.Delete();

        var result = Save(body);

        Assert.Equal(0, result.Added);
        Assert.Equal(HelloSeal.MissingKey, Assert.Single(result.HelloMissing).Reason);
        Assert.Empty(Titles());
    }

    [Fact]
    public void RequiredHelloDeclarationTravelsWithItsFlag()
    {
        var mine = VaultBody.CreateEmpty();
        mine.Set(VaultEntry.NewRequired(EntryName.Parse("token"), "ask Ops", Now, hello: true));
        Save(mine);
        var theirs = VaultBody.CreateEmpty();

        Load(theirs);

        Assert.True(theirs.Find(EntryName.Parse("token"))!.IsRequiredHello);
        using var raw = new RawDatabase(database);
        Assert.Equal("true", raw.Entry("token").Strings.ReadSafe(KeePassStore.RequiredHelloField));
    }

    [Fact]
    public void PersonalHelloValueIsSavedPlainWithFlagAndLoadedSealed()
    {
        var mine = VaultBody.CreateEmpty();
        mine.Set(VaultEntry.NewRequired(EntryName.Parse("token"), null, Now, hello: true));
        mine.SetPersonalValue(EntryName.Parse("token"), hello.Seal(mine, Secret("token", "personal-1")));
        using (var keePass = KeePassStore.Open(database, null, Master))
        {
            using var content = keePass.Read(Group);
            var saved = StoreSync.SavePersonal(mine, content, keePass, Group, withDeclaration: false, _ => false, new HelloContext(hello, Vault));
            Assert.Equal(1, saved.Added);
            keePass.Save();
        }

        Assert.Equal(("personal-1", "true"), Raw("token"));
        var theirs = VaultBody.CreateEmpty();
        theirs.Set(VaultEntry.NewRequired(EntryName.Parse("token"), null, Now, hello: true));
        using (var keePass = KeePassStore.Open(database, null, Master))
        {
            using var content = keePass.Read(Group);
            Assert.Equal(1, StoreSync.LoadPersonal(theirs, content, _ => false, Now, hello).Added);
        }

        Assert.True(theirs.Find(EntryName.Parse("token"))!.IsHello);
        Assert.Equal("personal-1", PlainValue(theirs, "token"));
    }

    [Fact]
    public void ExportCarriesHelloEntriesPlainWithMarker()
    {
        var body = VaultBody.CreateEmpty();
        SetHello(body, "a", "one");
        body.Set(Secret("plain", "two"));
        var names = new[] { EntryName.Parse("a") };
        var plain = body.Clone();
        plain.Set(hello.Unseal(body, [body.Find(names[0])!], "export", Vault).Values.Single());
        var file = directory.Combine("out.export");

        VaultTransfer.Export(plain, file, "a long passphrase 1", overwrite: false, Now, names);

        var imported = VaultTransfer.OpenExport(file, "a long passphrase 1").Entries;
        var a = imported.Single(e => e.Name.FullName == "a");
        Assert.False(a.IsHello);
        Assert.True(a.IsHelloMarked);
        Assert.Equal("one", Encoding.UTF8.GetString(a.GetValue()));
        Assert.False(imported.Single(e => e.Name.FullName == "plain").IsHelloMarked);
        Assert.Throws<HelloException>(() => VaultTransfer.Export(body, directory.Combine("sealed.export"), "a long passphrase 1", overwrite: false, Now));
    }

    private static VaultEntry Secret(string name, string value) =>
        VaultEntry.NewSecret(EntryName.Parse(name), Encoding.UTF8.GetBytes(value), Now);

    private void SetHello(VaultBody body, string name, string value) => body.Set(hello.Seal(body, Secret(name, value)));

    private string PlainValue(VaultBody body, string name)
    {
        using var session = HelloSeal.Open(body, key, "test");
        return Encoding.UTF8.GetString(session.Unseal(body.Find(EntryName.Parse(name))!).GetValue());
    }

    private StoreSaveResult Save(VaultBody body)
    {
        using var keePass = KeePassStore.Open(database, null, Master);
        using var content = keePass.Read(Group);
        var result = StoreSync.Save(body, content, keePass, Group, _ => false, hello: new HelloContext(hello, Vault));
        keePass.Save();
        StoreSync.RecordSaved(body, result, Now);
        return result;
    }

    private StoreLoadResult Load(VaultBody body)
    {
        using var keePass = KeePassStore.Open(database, null, Master);
        using var content = keePass.Read(Group);
        return StoreSync.Load(body, content, _ => false, _ => false, Now, hello);
    }

    private IReadOnlyList<StoreComparison> Compare(VaultBody body)
    {
        using var keePass = KeePassStore.Open(database, null, Master);
        using var content = keePass.Read(Group);
        return StoreSync.Compare(body, content);
    }

    private (string Password, string Hello) Raw(string title)
    {
        using var raw = new RawDatabase(database);
        var entry = raw.Entry(title);
        return (entry.Strings.ReadSafe(PwDefs.PasswordField), entry.Strings.ReadSafe(KeePassStore.HelloField));
    }

    private void EditRaw(string title, string password, bool hello)
    {
        using var raw = new RawDatabase(database);
        var entry = raw.Entry(title);
        entry.Strings.Set(PwDefs.PasswordField, new KeePassLib.Security.ProtectedString(true, password));
        if (!hello)
        {
            entry.Strings.Remove(KeePassStore.HelloField);
        }

        raw.Save();
    }

    private List<string> Titles()
    {
        using var raw = new RawDatabase(database);
        return raw.Group?.Entries.Select(e => e.Strings.ReadSafe(PwDefs.TitleField)).ToList() ?? [];
    }

    private sealed class RawDatabase : IDisposable
    {
        private readonly PwDatabase db = new();

        public RawDatabase(string path) => db.Open(IOConnectionInfo.FromPath(path), Key(), null);

        public PwGroup? Group => db.RootGroup.FindCreateGroup("LocalVault", false)?.FindCreateGroup("proj", false);

        public PwEntry Entry(string title) => Group!.Entries.Single(e => e.Strings.ReadSafe(PwDefs.TitleField) == title);

        public void Save() => db.Save(null);

        public void Dispose() => db.Close();
    }
}
