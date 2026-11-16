using System.Text;
using KeePassLib;
using KeePassLib.Cryptography.KeyDerivation;
using KeePassLib.Keys;
using KeePassLib.Serialization;

namespace LocalVault.KeePass.Tests;

public sealed class PersonalStoreTests : IDisposable
{
    private const string Master = "test master 1";
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly IReadOnlyList<string> Group = ["LocalVault", "proj"];
    private static readonly EntryName Os = EntryName.Parse("os");

    private readonly TempDirectory directory = new();
    private readonly string team;
    private readonly string personal;

    public PersonalStoreTests()
    {
        team = directory.Combine("team.kdbx");
        personal = directory.Combine("personal.kdbx");
        CreateDatabase(team);
        CreateDatabase(personal);
    }

    public void Dispose() => directory.Dispose();

    [Fact]
    public void PersonalValueGoesToThePersonalStoreAndFillsAnotherPc()
    {
        var mine = Declared("your own account");
        SetValue(mine, "my-password");

        SaveTeam(mine);
        Assert.Equal(1, SavePersonal(personal, mine).Added);

        Assert.Equal(string.Empty, RawField(team, "os", PwDefs.PasswordField));
        Assert.Equal("my-password", RawField(personal, "os", PwDefs.PasswordField));
        Assert.Equal(VaultEntry.SecretKind, RawField(personal, "os", KeePassStore.KindField));

        var other = VaultBody.CreateEmpty();
        LoadTeam(other);
        Assert.True(other.Find(Os)!.IsMissing);
        Assert.Equal(1, LoadPersonal(personal, other).Added);

        var filled = other.Find(Os)!;
        Assert.True(filled.IsRequired);
        Assert.False(filled.IsMissing);
        Assert.Equal("your own account", filled.RequiredNote);
        Assert.Equal("my-password", Encoding.UTF8.GetString(filled.GetValue()));
        Assert.All(ComparePersonal(personal, other), c => Assert.Equal(StoreSyncState.Saved, c.State));
        Assert.All(CompareTeam(other), c => Assert.Equal(StoreSyncState.Saved, c.State));
    }

    [Fact]
    public void CredentialShapeTravelsWithTheDeclarationAndTheValueKeepsItsUserName()
    {
        var mine = VaultBody.CreateEmpty();
        mine.Set(VaultEntry.NewRequired(Os, "domain account", Now, VaultEntry.CredentialShape));
        mine.Set(VaultEntry.NewCredential(Os, "jdoe", "pa55"u8, null, [], Now));
        Assert.True(mine.Find(Os)!.IsCredentialShape);

        SaveTeam(mine);
        SavePersonal(personal, mine);

        Assert.Equal(VaultEntry.CredentialShape, RawField(team, "os", KeePassStore.ShapeField));
        Assert.Equal("jdoe", RawField(personal, "os", PwDefs.UserNameField));

        var other = VaultBody.CreateEmpty();
        LoadTeam(other);
        Assert.True(other.Find(Os)!.IsCredentialShape);
        LoadPersonal(personal, other);
        var filled = other.Find(Os)!;
        Assert.True(filled.IsCredential);
        Assert.True(filled.IsCredentialShape);
        Assert.Equal("jdoe", filled.UserName);
    }

    [Fact]
    public void ShapeIsPartOfTheDeclarationHashAndAPlainDeclarationKeepsItsHash()
    {
        Assert.Equal(
            VaultEntry.ComputeContentHash(VaultEntry.RequiredKind, null, "note"u8),
            VaultEntry.NewRequired(Os, "note", Now).SyncHash());
        Assert.NotEqual(
            VaultEntry.NewRequired(Os, "note", Now).SyncHash(),
            VaultEntry.NewRequired(Os, "note", Now, VaultEntry.CredentialShape).SyncHash());
    }

    [Fact]
    public void PersonalStoreSharingThePrimaryHoldsDeclarationAndValueInOneEntry()
    {
        var mine = Declared("your own account");
        SetValue(mine, "my-password");

        SaveTeam(mine);
        SavePersonal(team, mine, withDeclaration: true);

        Assert.Equal(VaultEntry.RequiredKind, RawField(team, "os", KeePassStore.KindField));
        Assert.Equal("your own account", RawField(team, "os", PwDefs.NotesField));
        Assert.Equal("my-password", RawField(team, "os", PwDefs.PasswordField));
        Assert.Equal(0, SaveTeam(mine).Updated);
        Assert.Equal(0, SavePersonal(team, mine, withDeclaration: true).Updated);

        var teammate = VaultBody.CreateEmpty();
        LoadTeam(teammate);
        Assert.True(teammate.Find(Os)!.IsMissing);
        teammate.Set(VaultEntry.NewRequired(Os, "your own domain account", Now));
        Assert.Equal(1, SaveTeam(teammate).Updated);
        Assert.Equal("my-password", RawField(team, "os", PwDefs.PasswordField));

        var otherPc = VaultBody.CreateEmpty();
        LoadTeam(otherPc);
        Assert.Equal(1, LoadPersonal(team, otherPc).Added);
        Assert.Equal("my-password", Encoding.UTF8.GetString(otherPc.Find(Os)!.GetValue()));
    }

    [Fact]
    public void ASecretTurnedRequiredLosesItsValueInTheTeamStore()
    {
        var body = VaultBody.CreateEmpty();
        body.Set(VaultEntry.NewSecret(Os, "shared-once"u8, Now));
        SaveTeam(body);

        body.Set(VaultEntry.NewRequired(Os, null, Now));
        SaveTeam(body, replace: true);

        Assert.Equal(string.Empty, RawField(team, "os", PwDefs.PasswordField));
    }

    [Fact]
    public void PersonalMirrorLeavesRequiredEntriesToThePersonalPass()
    {
        var mine = Declared(null);
        SetValue(mine, "my-password");
        mine.Set(VaultEntry.NewSecret(EntryName.Parse("shared"), "s"u8, Now));

        SaveTeam(mine);
        SaveMirror(personal, mine);
        SavePersonal(personal, mine);

        var mirror = SaveMirror(personal, mine);
        Assert.Equal(0, mirror.Updated);
        Assert.Empty(mirror.Overwritten);
        Assert.Equal(0, SavePersonal(personal, mine).Updated);
        Assert.Equal("my-password", RawField(personal, "os", PwDefs.PasswordField));
        Assert.Equal("s", RawField(personal, "shared", PwDefs.PasswordField));
    }

    [Fact]
    public void ChangeMadeOnAnotherPcIsTakenAndBlocksTheOlderSave()
    {
        var mine = Declared(null);
        SetValue(mine, "v1");
        SaveTeam(mine);
        SavePersonal(personal, mine);

        var other = VaultBody.CreateEmpty();
        LoadTeam(other);
        LoadPersonal(personal, other);
        SetValue(other, "v2");
        Assert.Equal(1, SavePersonal(personal, other).Updated);

        SetValue(mine, "v1-local");
        var blocked = SavePersonal(personal, mine);
        Assert.Equal(StoreSyncState.Conflict, Assert.Single(blocked.Skipped).State);
        Assert.Equal("v2", RawField(personal, "os", PwDefs.PasswordField));

        var fresh = Declared(null);
        SetValue(fresh, "v1");
        fresh.RecordKeePassBase(Os, StoreSync.PersonalKey(DatabaseId(personal)), fresh.Find(Os)!.ContentHash(), Now);
        Assert.Equal(1, LoadPersonal(personal, fresh).Taken);
        Assert.Equal("v2", Encoding.UTF8.GetString(fresh.Find(Os)!.GetValue()));
    }

    [Fact]
    public void RemovingTheDeclarationRemovesThePersonalValue()
    {
        var mine = Declared(null);
        SetValue(mine, "my-password");
        SavePersonal(personal, mine);

        mine.Remove(Os);
        var result = SavePersonal(personal, mine);

        Assert.Equal(1, result.Deleted);
        Assert.Empty(mine.KeePassRemovals);
        using var keePass = KeePassStore.Open(personal, null, Master);
        using var content = keePass.Read(Group);
        Assert.Empty(content.Items);
    }

    [Fact]
    public void EntriesTheVaultDoesNotRequireAreIgnored()
    {
        var mine = VaultBody.CreateEmpty();
        mine.Set(VaultEntry.NewSecret(EntryName.Parse("shared"), "s"u8, Now));
        SaveMirror(personal, mine);

        var other = Declared(null);
        Assert.Empty(ComparePersonal(personal, other));
        Assert.Equal(0, LoadPersonal(personal, other).Added);
        Assert.Null(other.Find(EntryName.Parse("shared")));
    }

    [Fact]
    public void PathsUnderTheProfileStayThere()
    {
        Assert.Equal("~/.ssh/id_ed25519", StoredPathRules.Normalize(@"~\.ssh\id_ed25519"));
        Assert.Equal(Path.Combine(StoredPathRules.HomeDirectory, ".ssh", "id_ed25519"), StoredPathRules.Resolve(directory.Path, "~/.ssh/id_ed25519"));
        Assert.Equal("~/.ssh/k", StoredPathRules.HomeFormOf(Path.Combine(StoredPathRules.HomeDirectory, ".ssh", "k")));
        Assert.Throws<VaultException>(() => StoredPathRules.Normalize("~/../x"));
        Assert.Throws<VaultException>(() => StoredPathRules.Normalize("~/"));
    }

    [Fact]
    public void OnlyARequiredEntryTakesAFileUnderTheProfile()
    {
        var body = VaultBody.CreateEmpty();
        Assert.Throws<VaultException>(() => body.Set(VaultEntry.NewFile(Os, "key"u8, "~/.ssh/id", Now)));

        body.Set(VaultEntry.NewRequired(Os, null, Now, VaultEntry.FileShape, "~/.ssh/id"));
        body.Set(VaultEntry.NewFile(Os, "key"u8, "~/.ssh/id", Now));

        var entry = body.Find(Os)!;
        Assert.True(entry.IsRequired && entry.IsFile && entry.IsUnderHome && entry.IsFileShape);
        Assert.Equal("~/.ssh/id", entry.RequiredPath);
        Assert.True(body.ForExport().Find(Os)!.IsMissing);
    }

    [Fact]
    public void PersonalKeyFileGoesToThePersonalStoreAndComesBackWithItsPath()
    {
        var mine = VaultBody.CreateEmpty();
        mine.Set(VaultEntry.NewRequired(Os, "your SSH key", Now, VaultEntry.FileShape, "~/.ssh/id_ed25519"));
        mine.Set(VaultEntry.NewFile(Os, "PRIVATE KEY"u8, "~/.ssh/id_ed25519", Now));

        SaveTeam(mine);
        SavePersonal(personal, mine);

        Assert.Equal("~/.ssh/id_ed25519", RawField(team, "os", KeePassStore.RequiredPathField));
        Assert.Equal(VaultEntry.FileShape, RawField(team, "os", KeePassStore.ShapeField));
        Assert.Equal(string.Empty, RawField(team, "os", KeePassStore.PathField));
        Assert.Equal("~/.ssh/id_ed25519", RawField(personal, "os", KeePassStore.PathField));

        var other = VaultBody.CreateEmpty();
        LoadTeam(other);
        Assert.Equal("~/.ssh/id_ed25519", other.Find(Os)!.RequiredPath);
        Assert.Equal(1, LoadPersonal(personal, other).Added);
        var filled = other.Find(Os)!;
        Assert.True(filled.IsFile);
        Assert.Equal("~/.ssh/id_ed25519", filled.StoredPath);
        Assert.Equal("PRIVATE KEY", Encoding.UTF8.GetString(filled.GetValue()));
    }

    [Fact]
    public void PersonalKeyFileSharingThePrimaryKeepsItsAttachmentWhenTheDeclarationChanges()
    {
        var mine = VaultBody.CreateEmpty();
        mine.Set(VaultEntry.NewRequired(Os, null, Now, VaultEntry.FileShape, "~/.ssh/id"));
        mine.Set(VaultEntry.NewFile(Os, "PRIVATE KEY"u8, "~/.ssh/id", Now));
        SaveTeam(mine);
        SavePersonal(team, mine, withDeclaration: true);

        var teammate = VaultBody.CreateEmpty();
        LoadTeam(teammate);
        teammate.Set(VaultEntry.NewRequired(Os, "now with a note", Now, VaultEntry.FileShape, "~/.ssh/id"));
        Assert.Equal(1, SaveTeam(teammate).Updated);

        var otherPc = VaultBody.CreateEmpty();
        LoadTeam(otherPc);
        Assert.Equal(1, LoadPersonal(team, otherPc).Added);
        Assert.Equal("PRIVATE KEY", Encoding.UTF8.GetString(otherPc.Find(Os)!.GetValue()));
    }

    [Fact]
    public void AFileUnderTheProfileInTheTeamStoreIsNeverLoaded()
    {
        var planted = VaultBody.CreateEmpty();
        planted.Set(VaultEntry.NewRequired(Os, null, Now));
        planted.Set(VaultEntry.NewFile(Os, "ssh-ed25519 AAAA attacker"u8, "~/.ssh/authorized_keys", Now));
        SavePersonal(team, planted);

        var victim = VaultBody.CreateEmpty();
        LoadTeam(victim);

        Assert.Null(victim.Find(Os));
        Assert.Empty(CompareTeam(victim));
    }

    [Fact]
    public void RegistryKeepsOnePersonalStore()
    {
        var registry = new StoreRegistry(directory.Combine("appdata", StoreRegistry.FileName));
        registry.Register("team", team, null, makeDefault: true);
        registry.Register("mine", personal, null, makeDefault: false, personal: true);
        Assert.Equal("mine", registry.Personal!.Name);

        registry.Change("team", null, null, clearUnlock: false, makeDefault: false, personal: true);
        Assert.Equal("team", registry.Personal!.Name);
        Assert.False(registry.FindByName("mine")!.IsPersonal);

        registry.Change("mine", null, null, clearUnlock: false, makeDefault: false, personal: false);
        Assert.Equal("team", registry.Personal!.Name);

        registry.Unregister("team");
        Assert.Null(registry.Personal);
    }

    private static VaultBody Declared(string? note)
    {
        var body = VaultBody.CreateEmpty();
        body.Set(VaultEntry.NewRequired(Os, note, Now));
        return body;
    }

    private static void SetValue(VaultBody body, string value) =>
        body.Set(VaultEntry.NewSecret(Os, Encoding.UTF8.GetBytes(value), Now));

    private StoreSaveResult SaveTeam(VaultBody body, bool replace = false) => SaveShared(team, body, replace, mirror: false);

    private static StoreSaveResult SaveMirror(string path, VaultBody body) => SaveShared(path, body, replace: false, mirror: true);

    private static StoreSaveResult SaveShared(string path, VaultBody body, bool replace, bool mirror)
    {
        using var keePass = KeePassStore.Open(path, null, Master);
        using var content = keePass.Read(Group);
        var result = StoreSync.Save(body, content, keePass, Group, _ => replace, mirror, excludeRequired: mirror);
        keePass.Save();
        StoreSync.RecordSaved(body, result, Now);
        return result;
    }

    private static StoreSaveResult SavePersonal(string path, VaultBody body, bool withDeclaration = false)
    {
        using var keePass = KeePassStore.Open(path, null, Master);
        using var content = keePass.Read(Group);
        var result = StoreSync.SavePersonal(body, content, keePass, Group, withDeclaration, _ => false);
        keePass.Save();
        StoreSync.RecordSaved(body, result, Now);
        return result;
    }

    private StoreLoadResult LoadTeam(VaultBody body)
    {
        using var keePass = KeePassStore.Open(team, null, Master);
        using var content = keePass.Read(Group);
        return StoreSync.Load(body, content, _ => false, _ => false, Now);
    }

    private static StoreLoadResult LoadPersonal(string path, VaultBody body)
    {
        using var keePass = KeePassStore.Open(path, null, Master);
        using var content = keePass.Read(Group);
        return StoreSync.LoadPersonal(body, content, _ => false, Now);
    }

    private static IReadOnlyList<StoreComparison> ComparePersonal(string path, VaultBody body)
    {
        using var keePass = KeePassStore.Open(path, null, Master);
        using var content = keePass.Read(Group);
        return StoreSync.ComparePersonal(body, content);
    }

    private IReadOnlyList<StoreComparison> CompareTeam(VaultBody body)
    {
        using var keePass = KeePassStore.Open(team, null, Master);
        using var content = keePass.Read(Group);
        return StoreSync.Compare(body, content);
    }

    private static string DatabaseId(string path)
    {
        using var keePass = KeePassStore.Open(path, null, Master);
        return keePass.DatabaseId;
    }

    private static string RawField(string path, string title, string field)
    {
        var db = new PwDatabase();
        db.Open(IOConnectionInfo.FromPath(path), Key(), null);
        try
        {
            return db.RootGroup.GetEntries(true).Single(e => e.Strings.ReadSafe(PwDefs.TitleField) == title).Strings.ReadSafe(field);
        }
        finally
        {
            db.Close();
        }
    }

    private static CompositeKey Key()
    {
        var key = new CompositeKey();
        key.AddUserKey(new KcpPassword(Master));
        return key;
    }

    private static void CreateDatabase(string path)
    {
        var db = new PwDatabase();
        db.New(IOConnectionInfo.FromPath(path), Key());
        var kdf = new AesKdf().GetDefaultParameters();
        kdf.SetUInt64(AesKdf.ParamRounds, 1000);
        db.KdfParameters = kdf;
        db.RecycleBinEnabled = true;
        db.Save(null);
        db.Close();
    }
}
