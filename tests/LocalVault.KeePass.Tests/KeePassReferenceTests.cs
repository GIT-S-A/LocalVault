using System.Buffers.Binary;
using System.Text;
using KeePassLib;
using KeePassLib.Cryptography.KeyDerivation;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;

namespace LocalVault.KeePass.Tests;

public sealed class KeePassReferenceTests : IDisposable
{
    private const string Master = "test master 1";
    private const string KeyPath = "~/.ssh/proj/deploy";
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly IReadOnlyList<string> Group = ["LocalVault", "proj"];
    private static readonly EntryName Os = EntryName.Parse("os");
    private static readonly EntryName Api = EntryName.Parse("api");

    private readonly TempDirectory directory = new();
    private readonly string team;
    private readonly string personal;

    public KeePassReferenceTests()
    {
        team = directory.Combine("team.kdbx");
        personal = directory.Combine("personal.kdbx");
        CreateDatabase(team);
        CreateDatabase(personal);
    }

    public void Dispose() => directory.Dispose();

    [Fact]
    public void PersonalReferenceStoresOnlyWhereTheValueComesFrom()
    {
        var uuid = AddEntry(personal, ["Internet"], "GitHub", password: "token-1");
        var mine = Declared();

        var value = Reference(mine, "Internet/GitHub");
        Assert.Equal("token-1", Encoding.UTF8.GetString(value.GetValue()));
        Assert.Equal(new ItemReference(DatabaseId(personal), uuid, null, "Internet/GitHub"), value.Reference);

        Assert.Equal(1, SavePersonal(mine).Added);
        Assert.Equal(KeePassStore.ReferenceKind, RawField(personal, "os", KeePassStore.KindField));
        Assert.Equal($"{DatabaseId(personal)}/{uuid}", RawField(personal, "os", KeePassStore.RefField));
        Assert.Equal(VaultEntry.SecretKind, RawField(personal, "os", KeePassStore.RefShapeField));
        Assert.Equal(string.Empty, RawField(personal, "os", PwDefs.PasswordField));

        var otherPc = Declared();
        Assert.Equal(1, LoadPersonal(otherPc).Added);
        Assert.Equal("token-1", Encoding.UTF8.GetString(otherPc.Find(Os)!.GetValue()));
        Assert.Equal(uuid, otherPc.Find(Os)!.Reference!.ItemId);
        Assert.Equal(0, SavePersonal(otherPc).Updated);
    }

    [Fact]
    public void SaveReadsTheLinkWithoutOpeningTheTargetStore()
    {
        AddEntry(team, ["Infra"], "AD admin", password: "ad-1");
        var mine = Declared();
        Reference(mine, "Infra/AD admin", team);
        SavePersonal(mine);

        using var keePass = KeePassStore.Open(personal, null, Master);
        using var content = keePass.Read(Group);

        Assert.Empty(content.Items);
        var link = Assert.Single(content.Links);
        Assert.Equal(DatabaseId(team), link.Reference.StoreId);
        Assert.True(link.IsPersonal);
    }

    [Fact]
    public void ReferenceToAnotherStoreIsResolvedFromThatStore()
    {
        AddEntry(team, ["Infra"], "AD admin", password: "ad-1", user: "svc-mgmt", url: "ldaps://dc.example.com");
        var mine = Declared(VaultEntry.CredentialShape);
        Reference(mine, "Infra/AD admin", team);
        SavePersonal(mine);

        var otherPc = Declared(VaultEntry.CredentialShape);
        LoadPersonal(otherPc);

        var loaded = otherPc.Find(Os)!;
        Assert.Equal("svc-mgmt", loaded.UserName);
        Assert.Equal("ad-1", Encoding.UTF8.GetString(loaded.GetValue()));
        Assert.Equal("Infra/AD admin", loaded.Reference!.Title);
    }

    [Fact]
    public void CredentialReferenceCarriesUserUrlAndTotpOfTheTarget()
    {
        AddEntry(personal, ["Work"], "Domain", password: "pa55", user: "jdoe", url: "https://login.example.com",
            extra: new() { ["TimeOtp-Secret-Base32"] = "JBSWY3DPEHPK3PXP" });
        var mine = Declared(VaultEntry.CredentialShape);

        var value = Reference(mine, "Domain");

        Assert.True(value.IsCredential);
        Assert.Equal("jdoe", value.UserName);
        Assert.Equal(["https://login.example.com"], value.Urls);
        Assert.NotNull(value.Totp);
        SavePersonal(mine);
        Assert.Equal(VaultEntry.CredentialKind, RawField(personal, "os", KeePassStore.RefShapeField));
        Assert.Equal(string.Empty, RawField(personal, "os", PwDefs.UserNameField));
        Assert.Equal(string.Empty, RawField(personal, "os", "TimeOtp-Secret-Base32"));
    }

    [Fact]
    public void FieldReferencesInsideTheTargetAreFollowedAndALoopStops()
    {
        var end = AddEntry(personal, ["Internet"], "End", password: "deep");
        AddEntry(personal, ["Internet"], "Middle", password: $"{{REF:P@I:{end}}}");
        Assert.Equal("deep", Encoding.UTF8.GetString(Reference(Declared(), "Internet/Middle").GetValue()));

        var a = AddEntry(personal, ["Loop"], "A", password: "placeholder");
        var b = AddEntry(personal, ["Loop"], "B", password: $"{{REF:P@I:{a}}}");
        SetField(personal, a, PwDefs.PasswordField, $"{{REF:P@I:{b}}}");
        var error = Assert.Throws<VaultException>(() => Reference(Declared(), "Loop/A"));
        Assert.Contains("longer than 8", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GoneTargetIsReportedAndTheValueKeptAsItWas()
    {
        var uuid = AddEntry(personal, ["Internet"], "GitHub", password: "token-1");
        var mine = Declared();
        Reference(mine, "GitHub");
        SavePersonal(mine);
        var savedBase = mine.Find(Os)!.KeePassBaseFor(StoreSync.PersonalKey(DatabaseId(personal)));
        DeleteEntry(personal, uuid);

        using var stores = new Stores((personal, "mine"));
        using var keePass = KeePassStore.Open(personal, null, Master);
        using var content = keePass.Read(Group, stores);
        var result = StoreSync.LoadPersonal(mine, content, _ => false, Now);

        var unresolved = Assert.Single(content.Unresolved);
        Assert.Contains("no longer in store 'mine'", unresolved.Reason, StringComparison.Ordinal);
        Assert.Equal(new StoreLoadResult(0, 0, 0, 0), result with { NotSealed = [], StillMissing = [] });
        Assert.Equal("token-1", Encoding.UTF8.GetString(mine.Find(Os)!.GetValue()));
        Assert.Equal(savedBase, mine.Find(Os)!.KeePassBaseFor(StoreSync.PersonalKey(DatabaseId(personal))));
    }

    [Fact]
    public void RefFieldTypedInTheVaultGroupIsLeftOut()
    {
        var uuid = AddEntry(team, ["Internet"], "GitHub", password: "token-1");
        AddEntry(team, ["LocalVault", "proj"], "api", password: $"{{REF:P@I:{uuid}}}", extra: new() { [KeePassStore.KindField] = VaultEntry.SecretKind });

        using var keePass = KeePassStore.Open(team, null, Master);
        using var content = keePass.Read(Group);

        Assert.Empty(content.Items);
        Assert.Contains(content.Skipped, s => s.Contains("uses a {REF:} field. Use lv set api --ref", StringComparison.Ordinal));
    }

    [Fact]
    public void SharedReferenceIsSavedAsALinkAndLoadedByATeammate()
    {
        AddEntry(team, ["Infra"], "AD admin", password: "ad-1");
        var mine = VaultBody.CreateEmpty();
        using (var keePass = KeePassStore.Open(team, null, Master))
        using (var item = keePass.ReadReferenceTarget("AD admin", Api, null, null, [], personal: false, "team"))
        {
            mine.Set(item.ToVaultEntry(Now));
        }

        using (var keePass = KeePassStore.Open(team, null, Master))
        {
            using var content = keePass.Read(Group);
            var result = StoreSync.Save(mine, content, keePass, Group, _ => false);
            keePass.Save();
            StoreSync.RecordSaved(mine, result, Now);
            Assert.Equal(1, result.Added);
        }

        Assert.Equal(KeePassStore.ReferenceKind, RawField(team, "api", KeePassStore.KindField));
        Assert.Equal(string.Empty, RawField(team, "api", KeePassStore.PersonalField));
        Assert.Equal(string.Empty, RawField(team, "api", PwDefs.PasswordField));

        var teammate = VaultBody.CreateEmpty();
        using (var stores = new Stores((team, "team")))
        using (var keePass = KeePassStore.Open(team, null, Master))
        using (var content = keePass.Read(Group, stores))
        {
            Assert.Equal(1, StoreSync.Load(teammate, content, _ => false, _ => false, Now).Added);
        }

        Assert.Equal("ad-1", Encoding.UTF8.GetString(teammate.Find(Api)!.GetValue()));
        Assert.Equal("Infra/AD admin", teammate.Find(Api)!.Reference!.Title);
    }

    [Fact]
    public void ChangeMadeInKeePassIsTakenWithoutAQuestion()
    {
        var uuid = AddEntry(personal, ["Internet"], "GitHub", password: "token-1");
        var mine = Declared();
        var reference = Reference(mine, "GitHub").Reference;
        SavePersonal(mine);

        mine.Set(VaultEntry.NewSecret(Os, "local edit"u8, Now).WithReference(reference));
        SetField(personal, uuid, PwDefs.PasswordField, "token-2");

        var result = LoadPersonal(mine, takeConflict: _ => throw new InvalidOperationException("asked"));

        Assert.Equal(1, result.Taken);
        Assert.Equal("token-2", Encoding.UTF8.GetString(mine.Find(Os)!.GetValue()));
    }

    [Fact]
    public void ChangeMadeOnThisPcIsNotSaved()
    {
        var uuid = AddEntry(personal, ["Internet"], "GitHub", password: "token-1");
        var mine = Declared();
        var reference = Reference(mine, "GitHub").Reference;
        SavePersonal(mine);

        mine.Set(VaultEntry.NewSecret(Os, "local edit"u8, Now).WithReference(reference));
        var result = SavePersonal(mine);

        var refused = Assert.Single(result.ReferenceRefused);
        Assert.Equal(Os, refused.Name);
        Assert.Equal("Internet/GitHub", refused.Reference.Title);
        Assert.Equal($"{DatabaseId(personal)}/{uuid}", RawField(personal, "os", KeePassStore.RefField));
    }

    [Fact]
    public void SshKeyReferencePointsAtTheAttachmentAndReadsItsPublicKey()
    {
        var key = PrivateKey("ssh-ed25519", new byte[32]);
        var uuid = AddEntry(personal, ["SSH"], "Work key", attachments: new() { ["id_ed25519"] = key, ["id_ed25519.pub"] = "ssh-ed25519 AAAA"u8.ToArray() });
        var mine = VaultBody.CreateEmpty();
        mine.Set(VaultEntry.NewRequired(Os, null, Now, VaultEntry.SshKeyShape, KeyPath));

        var value = Reference(mine, "SSH/Work key");

        Assert.Equal("ssh-ed25519", value.Ssh!.PublicKey.Split(' ')[0]);
        Assert.Equal("id_ed25519", value.Reference!.Attachment);
        Assert.Equal(key, value.GetValue());
        SavePersonal(mine);
        Assert.Equal($"{DatabaseId(personal)}/{uuid}/id_ed25519", RawField(personal, "os", KeePassStore.RefField));
        Assert.Equal(VaultEntry.SshKeyShape, RawField(personal, "os", KeePassStore.RefShapeField));
        Assert.Equal(0u, BinaryCount(personal, "os"));

        var otherPc = VaultBody.CreateEmpty();
        otherPc.Set(VaultEntry.NewRequired(Os, null, Now, VaultEntry.SshKeyShape, KeyPath));
        LoadPersonal(otherPc);
        Assert.Equal(key, otherPc.Find(Os)!.GetValue());
        Assert.Equal(value.Ssh.PublicKey, otherPc.Find(Os)!.Ssh!.PublicKey);
    }

    [Fact]
    public void SearchSkipsTheLocalVaultGroupAndRefusesAnAmbiguousTitle()
    {
        AddEntry(personal, ["LocalVault", "proj"], "Shared", password: "x");
        AddEntry(personal, ["A"], "Twice", password: "1");
        AddEntry(personal, ["B"], "Twice", password: "2");

        Assert.Contains("No KeePass entry", Assert.Throws<VaultException>(() => Reference(Declared(), "Shared")).Message, StringComparison.Ordinal);
        Assert.Contains("A/Twice, B/Twice", Assert.Throws<VaultException>(() => Reference(Declared(), "Twice")).Message, StringComparison.Ordinal);
        Assert.Equal("2", Encoding.UTF8.GetString(Reference(Declared(), "B/Twice").GetValue()));
    }

    [Theory]
    [InlineData("ssh-ed25519")]
    [InlineData("ssh-rsa")]
    public void PublicKeyIsReadFromAnOpenSshPrivateKey(string type)
    {
        var publicKey = OpenSshPrivateKey.PublicKeyOf(PrivateKey(type, [1, 2, 3]));

        Assert.StartsWith(type + " ", publicKey, StringComparison.Ordinal);
        Assert.Equal(type == "ssh-rsa" ? SshKeyInfo.Rsa : SshKeyInfo.Ed25519, SshKeyInfo.TypeOf(publicKey));
    }

    [Fact]
    public void PemPrivateKeyIsRefused()
    {
        var pem = "-----BEGIN RSA PRIVATE KEY-----\nMIIBOgIBAAJBAKj34GkxFhD90vcNLYLInFEX6Ppy1tPf9Cnzj4p4WGeKLs1Pt8Qu\n-----END RSA PRIVATE KEY-----\n"u8.ToArray();

        Assert.Contains("Not an OpenSSH private key", Assert.Throws<VaultException>(() => OpenSshPrivateKey.PublicKeyOf(pem)).Message, StringComparison.Ordinal);
    }

    private static VaultBody Declared(string? shape = null)
    {
        var body = VaultBody.CreateEmpty();
        body.Set(VaultEntry.NewRequired(Os, "your own value", Now, shape));
        return body;
    }

    private VaultEntry Reference(VaultBody body, string search, string? store = null)
    {
        var declaration = body.Find(Os)!;
        using var keePass = KeePassStore.Open(store ?? personal, null, Master);
        using var item = keePass.ReadReferenceTarget(
            search, Os, declaration.RequiredShape ?? VaultEntry.SecretKind, declaration.RequiredPath, [], personal: true, store is null ? "mine" : "team");
        body.SetPersonalValue(Os, item.ToVaultEntry(Now));
        return body.Find(Os)!;
    }

    private StoreSaveResult SavePersonal(VaultBody body)
    {
        using var keePass = KeePassStore.Open(personal, null, Master);
        using var content = keePass.Read(Group);
        var result = StoreSync.SavePersonal(body, content, keePass, Group, withDeclaration: false, _ => false);
        keePass.Save();
        StoreSync.RecordSaved(body, result, Now);
        return result;
    }

    private StoreLoadResult LoadPersonal(VaultBody body, Func<EntryName, bool>? takeConflict = null)
    {
        using var stores = new Stores((personal, "mine"), (team, "team"));
        using var keePass = KeePassStore.Open(personal, null, Master);
        using var content = keePass.Read(Group, stores);
        return StoreSync.LoadPersonal(body, content, takeConflict ?? (_ => false), Now);
    }

    private sealed class Stores : IReferenceStores, IDisposable
    {
        private readonly List<ReferenceStore> opened;

        public Stores(params (string Path, string Name)[] stores) =>
            opened = stores.Select(store => new ReferenceStore(KeePassStore.Open(store.Path, null, Master), store.Name)).ToList();

        public ReferenceStore Open(ItemReference reference, bool personal) =>
            opened.FirstOrDefault(store => string.Equals(store.Store.DatabaseId, reference.StoreId, StringComparison.OrdinalIgnoreCase))
                ?? throw new VaultException("its value is in a store this test doesn't know.");

        public void Dispose() => opened.ForEach(store => store.Store.Dispose());
    }

    private static string DatabaseId(string path)
    {
        using var keePass = KeePassStore.Open(path, null, Master);
        return keePass.DatabaseId;
    }

    private static byte[] PrivateKey(string type, byte[] keyBytes)
    {
        var publicBlob = Concat(SshString(Encoding.ASCII.GetBytes(type)), SshString(keyBytes));
        var count = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(count, 1);
        var body = Concat(
            "openssh-key-v1\0"u8.ToArray(),
            SshString("none"u8.ToArray()),
            SshString("none"u8.ToArray()),
            SshString([]),
            count,
            SshString(publicBlob),
            SshString(new byte[16]));
        return Encoding.ASCII.GetBytes($"-----BEGIN OPENSSH PRIVATE KEY-----\n{Convert.ToBase64String(body, Base64FormattingOptions.InsertLineBreaks)}\n-----END OPENSSH PRIVATE KEY-----\n");
    }

    private static byte[] SshString(byte[] value)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)value.Length);
        return Concat(length, value);
    }

    private static byte[] Concat(params byte[][] parts) => parts.SelectMany(part => part).ToArray();

    private static string AddEntry(
        string path,
        string[] groups,
        string title,
        string password = "",
        string user = "",
        string url = "",
        Dictionary<string, string>? extra = null,
        Dictionary<string, byte[]>? attachments = null)
    {
        return WithDatabase(path, db =>
        {
            var group = db.RootGroup;
            foreach (var name in groups)
            {
                group = group.FindCreateGroup(name, true);
            }

            var entry = new PwEntry(true, true);
            entry.Strings.Set(PwDefs.TitleField, new ProtectedString(false, title));
            entry.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, password));
            entry.Strings.Set(PwDefs.UserNameField, new ProtectedString(false, user));
            entry.Strings.Set(PwDefs.UrlField, new ProtectedString(false, url));
            foreach (var (key, value) in extra ?? [])
            {
                entry.Strings.Set(key, new ProtectedString(false, value));
            }

            foreach (var (name, content) in attachments ?? [])
            {
                entry.Binaries.Set(name, new ProtectedBinary(true, content));
            }

            group.AddEntry(entry, true);
            return entry.Uuid.ToHexString();
        });
    }

    private static void SetField(string path, string uuid, string field, string value) =>
        WithDatabase(path, db =>
        {
            var entry = db.RootGroup.FindEntry(new PwUuid(Convert.FromHexString(uuid)), true);
            entry.Strings.Set(field, new ProtectedString(field == PwDefs.PasswordField, value));
            entry.Touch(true, false);
            return 0;
        });

    private static void DeleteEntry(string path, string uuid) =>
        WithDatabase(path, db =>
        {
            var entry = db.RootGroup.FindEntry(new PwUuid(Convert.FromHexString(uuid)), true);
            entry.ParentGroup.Entries.Remove(entry);
            return 0;
        });

    private static T WithDatabase<T>(string path, Func<PwDatabase, T> change)
    {
        var db = new PwDatabase();
        db.Open(IOConnectionInfo.FromPath(path), Key(), null);
        try
        {
            var result = change(db);
            db.Save(null);
            return result;
        }
        finally
        {
            db.Close();
        }
    }

    private static string RawField(string path, string title, string field) =>
        WithDatabase(path, db => VaultEntryOf(db, title).Strings.ReadSafe(field));

    private static uint BinaryCount(string path, string title) => WithDatabase(path, db => VaultEntryOf(db, title).Binaries.UCount);

    private static PwEntry VaultEntryOf(PwDatabase db, string title) =>
        db.RootGroup.FindCreateGroup("LocalVault", false).GetEntries(true).Single(e => e.Strings.ReadSafe(PwDefs.TitleField) == title);

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
