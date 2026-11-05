using System.Text;
using KeePassLib;
using KeePassLib.Cryptography.KeyDerivation;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;

namespace LocalVault.Core.Tests;

public sealed class SshKeyTests : IDisposable
{
    private const string Master = "test master 1";
    private const string PublicKey = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIGmCmhyZfFSbAVfJ/bovNv4H3f/VIW8/KGdICZD31KiE test@example.com";
    private const string Fingerprint = "SHA256:ygwjj+x/jcbcUNmwhazPNZzoRvNv7XDnLrscAy0ZQ0M";
    private const string KeyPath = "~/.ssh/proj/deploy";
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly IReadOnlyList<string> Group = ["LocalVault", "proj"];
    private static readonly EntryName Deploy = EntryName.Parse("deploy");
    private static readonly SshKeyInfo Info = new(SshKeyInfo.Ed25519, PublicKey, ["deploy@srv1.example.com"]);

    private readonly TempDirectory directory = new();
    private readonly string team;
    private readonly string personal;

    public SshKeyTests()
    {
        team = directory.Combine("team.kdbx");
        personal = directory.Combine("personal.kdbx");
        CreateDatabase(team);
        CreateDatabase(personal);
    }

    public void Dispose() => directory.Dispose();

    [Theory]
    [InlineData("deploy", "~/.ssh/proj/deploy")]
    [InlineData("common/deploy", "~/.ssh/proj/common.deploy")]
    [InlineData("prod:web", "~/.ssh/proj/prod_web")]
    public void KeyPathIsTheEntryNameInTheRepositoryFolder(string name, string expected) =>
        Assert.Equal(expected, SshKeyPath.For("proj", EntryName.Parse(name)));

    [Theory]
    [InlineData("~/.ssh/authorized_keys")]
    [InlineData("~/.ssh/proj/sub/key")]
    [InlineData("~/keys/proj/key")]
    [InlineData("~/.ssh/proj/.localvault")]
    [InlineData("~/.ssh/proj/lv.config")]
    [InlineData("~/.ssh/proj/key.pub")]
    [InlineData("~/.ssh/proj/key.old")]
    [InlineData(".ssh/proj/key")]
    public void KeyPathOutsideARepositoryFolderIsRefused(string storedPath) =>
        Assert.Throws<VaultException>(() => SshKeyPath.Validate(storedPath));

    [Theory]
    [InlineData("https://github.com/GIT-S-A/LocalVault.git", "LocalVault")]
    [InlineData("https://dev.azure.com/org/project/_git/infra", "infra")]
    [InlineData("git@github.com:org/repo.git", "repo")]
    [InlineData("C:\\repos\\local-only", "local-only")]
    [InlineData(null, "folder")]
    public void RepositoryNameComesFromTheOrigin(string? origin, string expected) =>
        Assert.Equal(expected, SshKeyPath.RepositoryNameOf(origin, "folder"));

    [Fact]
    public void FingerprintIsTheOpenSshForm()
    {
        Assert.Equal(Fingerprint, SshKeyInfo.FingerprintOf(PublicKey));
        Assert.Equal(SshKeyInfo.Ed25519, SshKeyInfo.TypeOf(PublicKey));
        Assert.Throws<VaultException>(() => SshKeyInfo.NormalizePublicKey("not a key"));
        Assert.Throws<VaultException>(() => SshKeyInfo.NormalizePublicKey("ssh-dss AAAA"));
    }

    [Fact]
    public void SharedKeyIsAllowedUnderTheProfileAndAPlainFileIsNot()
    {
        var body = VaultBody.CreateEmpty();
        Assert.Throws<VaultException>(() => body.Set(VaultEntry.NewFile(Deploy, "key"u8, KeyPath, Now)));

        body.Set(SharedKey("PRIVATE KEY", previous: "OLD KEY"));

        var entry = body.Find(Deploy)!;
        Assert.True(entry.IsSshKey && entry.IsFile && entry.IsUnderHome && !entry.IsRequired);
        Assert.Equal(["deploy@srv1.example.com"], entry.SshHosts);
        Assert.Equal(Fingerprint, entry.Ssh!.Fingerprint);
        Assert.Equal("OLD KEY", Encoding.UTF8.GetString(entry.GetPrevious()!));
        Assert.Equal(VaultBody.CurrentSchema, VaultBody.Parse(body.ToUtf8()).Schema);
    }

    [Fact]
    public void KeyHashCoversValuePreviousAndHosts()
    {
        var plain = SharedKey("K").ContentHash();
        Assert.NotEqual(plain, SharedKey("K", previous: "OLD").ContentHash());
        Assert.NotEqual(plain, VaultEntry.NewSshKey(Deploy, "K"u8, KeyPath, Info with { Hosts = ["other@srv2.example.com"] }, [], Now).ContentHash());
        Assert.NotEqual(plain, SharedKey("K2").ContentHash());
        Assert.Equal(plain, SharedKey("K").ContentHash());
    }

    [Fact]
    public void DeclarationHashCoversHostsAndAPlainDeclarationKeepsItsHash()
    {
        Assert.Equal(
            VaultEntry.NewRequired(Deploy, "note", Now, VaultEntry.FileShape, KeyPath).SyncHash(),
            VaultEntry.ComputeDeclarationHash("note", VaultEntry.FileShape, KeyPath));
        Assert.NotEqual(
            VaultEntry.NewRequired(Deploy, "note", Now, VaultEntry.SshKeyShape, KeyPath).SyncHash(),
            VaultEntry.NewRequired(Deploy, "note", Now, VaultEntry.SshKeyShape, KeyPath, ["deploy@srv1.example.com"]).SyncHash());
    }

    [Fact]
    public void ExportDropsAPersonalKeyAndItsPreviousKey()
    {
        var body = DeclaredPersonal();
        body.Set(PersonalKey("PRIVATE KEY", previous: "OLD KEY"));

        var exported = body.ForExport().Find(Deploy)!;

        Assert.True(exported.IsMissing);
        Assert.False(exported.HasPrevious);
        Assert.Null(exported.Ssh);
        Assert.True(exported.IsSshKeyShape);
        Assert.Equal(["deploy@srv1.example.com"], exported.RequiredHosts);
    }

    [Fact]
    public void SchemaFourBodyIsUpgradedToFive()
    {
        var parsed = VaultBody.Parse("""{"schema":4,"entries":[]}"""u8);
        Assert.Equal(4, parsed.UpgradedFrom);
        Assert.Equal(5, parsed.Schema);
    }

    [Theory]
    [InlineData("""{"type":"dsa","publicKey":"x"}""")]
    [InlineData("""{"type":"ed25519","publicKey":"not a key"}""")]
    [InlineData("""{"type":"ed25519"}""")]
    [InlineData("""{"type":"ed25519","publicKey":"ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIGmCmhyZfFSbAVfJ/bovNv4H3f/VIW8/KGdICZD31KiE","hosts":"one"}""")]
    public void DamagedKeyDataIsRefused(string ssh)
    {
        var json = """{"schema":5,"entries":[{"name":"deploy","kind":"file","value":"","meta":{"path":"~/.ssh/proj/deploy","ssh":""" + ssh + "}}]}";
        Assert.Throws<VaultException>(() => VaultBody.Parse(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void KeyWithAPathOutsideItsFolderIsRefused()
    {
        var json = """{"schema":5,"entries":[{"name":"deploy","kind":"file","value":"","meta":{"path":"~/.ssh/authorized_keys","ssh":{"type":"ed25519","publicKey":""" + "\"" + PublicKey + "\"}}}]}";
        Assert.Throws<VaultException>(() => VaultBody.Parse(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void SharedKeyTravelsThroughTheTeamStoreWithItsPreviousKeyAndHosts()
    {
        var mine = VaultBody.CreateEmpty();
        mine.Set(SharedKey("PRIVATE KEY", previous: "OLD KEY"));

        Assert.Equal(1, Save(team, mine).Added);
        Assert.Equal(SshKeyInfo.Ed25519, RawField(team, "deploy", KeePassVault.SshTypeField));
        Assert.Equal(PublicKey, RawField(team, "deploy", KeePassVault.SshPublicKeyField));
        Assert.Equal("deploy@srv1.example.com", RawField(team, "deploy", KeePassVault.SshHostsField));
        Assert.Equal(string.Empty, RawField(team, "deploy", KeePassVault.PersonalField));
        Assert.Equal("OLD KEY", RawAttachment(team, "deploy", "deploy.old"));

        var teammate = VaultBody.CreateEmpty();
        Assert.Equal(1, Load(team, teammate).Added);
        var loaded = teammate.Find(Deploy)!;
        Assert.Equal(KeyPath, loaded.StoredPath);
        Assert.Equal("PRIVATE KEY", Encoding.UTF8.GetString(loaded.GetValue()));
        Assert.Equal("OLD KEY", Encoding.UTF8.GetString(loaded.GetPrevious()!));
        Assert.Equal(Info.Hosts, loaded.SshHosts);
        Assert.All(Compare(team, teammate), c => Assert.Equal(KeePassSyncState.Saved, c.State));
    }

    [Fact]
    public void RetiringThePreviousKeyReachesTheTeamStore()
    {
        var mine = VaultBody.CreateEmpty();
        mine.Set(SharedKey("PRIVATE KEY", previous: "OLD KEY"));
        Save(team, mine);

        mine.Set(SharedKey("PRIVATE KEY"));
        Assert.Equal(1, Save(team, mine).Updated);

        Assert.Equal(string.Empty, RawAttachment(team, "deploy", "deploy.old"));
        var teammate = VaultBody.CreateEmpty();
        Load(team, teammate);
        Assert.False(teammate.Find(Deploy)!.HasPrevious);
    }

    [Fact]
    public void PersonalKeyGoesToThePersonalStoreAndItsHostsStayWithTheDeclaration()
    {
        var mine = DeclaredPersonal();
        mine.Set(PersonalKey("PRIVATE KEY"));

        Save(team, mine);
        SavePersonal(personal, mine);

        Assert.Equal(VaultEntry.SshKeyShape, RawField(team, "deploy", KeePassVault.ShapeField));
        Assert.Equal("deploy@srv1.example.com", RawField(team, "deploy", KeePassVault.SshHostsField));
        Assert.Equal(string.Empty, RawField(team, "deploy", KeePassVault.SshTypeField));
        Assert.Equal("true", RawField(personal, "deploy", KeePassVault.PersonalField));
        Assert.Equal(PublicKey, RawField(personal, "deploy", KeePassVault.SshPublicKeyField));
        Assert.Equal(string.Empty, RawField(personal, "deploy", KeePassVault.SshHostsField));

        var otherPc = VaultBody.CreateEmpty();
        Load(team, otherPc);
        Assert.True(otherPc.Find(Deploy)!.IsMissing);
        Assert.Equal(1, LoadPersonal(personal, otherPc).Added);
        var filled = otherPc.Find(Deploy)!;
        Assert.Equal(PublicKey, filled.Ssh!.PublicKey);
        Assert.Equal(["deploy@srv1.example.com"], filled.SshHosts);
        Assert.Equal("PRIVATE KEY", Encoding.UTF8.GetString(filled.GetValue()));
    }

    [Fact]
    public void PersonalKeyFoundInATeamStoreIsNeverLoadedAsShared()
    {
        var planted = DeclaredPersonal();
        planted.Set(PersonalKey("PRIVATE KEY"));
        SavePersonal(team, planted);

        var victim = VaultBody.CreateEmpty();
        Load(team, victim);

        Assert.Null(victim.Find(Deploy));
    }

    [Fact]
    public void KeyEntryPointingOutsideItsFolderIsSkipped()
    {
        var db = new PwDatabase();
        db.Open(IOConnectionInfo.FromPath(team), Key(), null);
        var group = db.RootGroup.FindCreateGroup("LocalVault", true).FindCreateGroup("proj", true);
        var entry = new PwEntry(true, true);
        entry.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "deploy"));
        entry.Strings.Set(KeePassVault.KindField, new ProtectedString(false, VaultEntry.FileKind));
        entry.Strings.Set(KeePassVault.PathField, new ProtectedString(false, "~/.ssh/authorized_keys"));
        entry.Strings.Set(KeePassVault.SshTypeField, new ProtectedString(false, SshKeyInfo.Ed25519));
        entry.Strings.Set(KeePassVault.SshPublicKeyField, new ProtectedString(false, PublicKey));
        entry.Binaries.Set("authorized_keys", new ProtectedBinary(false, Encoding.UTF8.GetBytes(PublicKey)));
        group.AddEntry(entry, true);
        db.Save(null);
        db.Close();

        using var keePass = KeePassVault.Open(team, null, Master);
        using var content = keePass.Read(Group);

        Assert.Empty(content.Items);
        Assert.Single(content.Skipped);
    }

    [Theory]
    [InlineData("deploy@srv1.example.com", true)]
    [InlineData("srv1.example.com:2222", true)]
    [InlineData("root@[2001:db8::1]:22", true)]
    [InlineData("-oProxyCommand=calc", false)]
    [InlineData("deploy@-srv", false)]
    [InlineData("-user@srv", false)]
    [InlineData("srv one", false)]
    [InlineData("", false)]
    public void HostIsUserHostAndPortOnly(string host, bool valid) => Assert.Equal(valid, SshKeyInfo.IsHost(host));

    [Fact]
    public void OptionGivenAsAHostIsRefusedInTheVaultAndInKeePass()
    {
        Assert.Throws<VaultException>(() => VaultEntry.NewRequired(Deploy, null, Now, VaultEntry.SshKeyShape, KeyPath, ["-oProxyCommand=calc"]));

        var db = new PwDatabase();
        db.Open(IOConnectionInfo.FromPath(team), Key(), null);
        var group = db.RootGroup.FindCreateGroup("LocalVault", true).FindCreateGroup("proj", true);
        var entry = new PwEntry(true, true);
        entry.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "deploy"));
        entry.Strings.Set(KeePassVault.KindField, new ProtectedString(false, VaultEntry.FileKind));
        entry.Strings.Set(KeePassVault.PathField, new ProtectedString(false, KeyPath));
        entry.Strings.Set(KeePassVault.SshTypeField, new ProtectedString(false, SshKeyInfo.Ed25519));
        entry.Strings.Set(KeePassVault.SshPublicKeyField, new ProtectedString(false, PublicKey));
        entry.Strings.Set(KeePassVault.SshHostsField, new ProtectedString(false, "-oProxyCommand=calc"));
        entry.Binaries.Set("deploy", new ProtectedBinary(true, "KEY"u8.ToArray()));
        group.AddEntry(entry, true);
        db.Save(null);
        db.Close();

        using var keePass = KeePassVault.Open(team, null, Master);
        using var content = keePass.Read(Group);
        Assert.Empty(content.Items);
    }

    [Fact]
    public void LegacySshKeyRefusalNamesTheNewCommand()
    {
        var mapping = LegacyMapper.Map([new("__SshPrivateKey", "KEY")], Now);
        Assert.Contains("lv ssh-key new", mapping.Refused.Single().Reason, StringComparison.Ordinal);
    }

    private static VaultEntry SharedKey(string value, string? previous = null) =>
        VaultEntry.NewSshKey(Deploy, Encoding.UTF8.GetBytes(value), KeyPath, Info, previous is null ? [] : Encoding.UTF8.GetBytes(previous), Now);

    private static VaultEntry PersonalKey(string value, string? previous = null) =>
        VaultEntry.NewSshKey(Deploy, Encoding.UTF8.GetBytes(value), KeyPath, Info with { Hosts = [] }, previous is null ? [] : Encoding.UTF8.GetBytes(previous), Now);

    private static VaultBody DeclaredPersonal()
    {
        var body = VaultBody.CreateEmpty();
        body.Set(VaultEntry.NewRequired(Deploy, "your SSH key", Now, VaultEntry.SshKeyShape, KeyPath, ["deploy@srv1.example.com"]));
        return body;
    }

    private static KeePassSaveResult Save(string path, VaultBody body)
    {
        using var keePass = KeePassVault.Open(path, null, Master);
        using var content = keePass.Read(Group);
        var result = KeePassSync.Save(body, content, keePass, Group, _ => false, false, excludeRequired: false);
        keePass.Save();
        KeePassSync.RecordSaved(body, result, Now);
        return result;
    }

    private static KeePassSaveResult SavePersonal(string path, VaultBody body)
    {
        using var keePass = KeePassVault.Open(path, null, Master);
        using var content = keePass.Read(Group);
        var result = KeePassSync.SavePersonal(body, content, keePass, Group, false, _ => false);
        keePass.Save();
        KeePassSync.RecordSaved(body, result, Now);
        return result;
    }

    private static KeePassLoadResult Load(string path, VaultBody body)
    {
        using var keePass = KeePassVault.Open(path, null, Master);
        using var content = keePass.Read(Group);
        return KeePassSync.Load(body, content, _ => false, _ => false, Now);
    }

    private static KeePassLoadResult LoadPersonal(string path, VaultBody body)
    {
        using var keePass = KeePassVault.Open(path, null, Master);
        using var content = keePass.Read(Group);
        return KeePassSync.LoadPersonal(body, content, _ => false, Now);
    }

    private static IReadOnlyList<KeePassComparison> Compare(string path, VaultBody body)
    {
        using var keePass = KeePassVault.Open(path, null, Master);
        using var content = keePass.Read(Group);
        return KeePassSync.Compare(body, content);
    }

    private static string RawField(string path, string title, string field) =>
        WithEntry(path, title, entry => entry.Strings.ReadSafe(field));

    private static string RawAttachment(string path, string title, string attachment) =>
        WithEntry(path, title, entry => entry.Binaries.Get(attachment) is { } data ? Encoding.UTF8.GetString(data.ReadData()) : string.Empty);

    private static string WithEntry(string path, string title, Func<PwEntry, string> read)
    {
        var db = new PwDatabase();
        db.Open(IOConnectionInfo.FromPath(path), Key(), null);
        try
        {
            return read(db.RootGroup.GetEntries(true).Single(e => e.Strings.ReadSafe(PwDefs.TitleField) == title));
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
