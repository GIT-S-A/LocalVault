using System.Text;
using KeePassLib;
using KeePassLib.Security;
using KeePassLib.Serialization;

namespace LocalVault.KeePass.Tests;

public sealed class SshKeyStoreTests : IDisposable
{
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

    public SshKeyStoreTests()
    {
        team = directory.Combine("team.kdbx");
        personal = directory.Combine("personal.kdbx");
        CreateDatabase(team);
        CreateDatabase(personal);
    }

    public void Dispose() => directory.Dispose();

    [Fact]
    public void SharedKeyTravelsThroughTheTeamStoreWithItsPreviousKeyAndHosts()
    {
        var mine = VaultBody.CreateEmpty();
        mine.Set(SharedKey("PRIVATE KEY", previous: "OLD KEY"));

        Assert.Equal(1, Save(team, mine).Added);
        Assert.Equal(SshKeyInfo.Ed25519, RawField(team, "deploy", KeePassStore.SshTypeField));
        Assert.Equal(PublicKey, RawField(team, "deploy", KeePassStore.SshPublicKeyField));
        Assert.Equal("deploy@srv1.example.com", RawField(team, "deploy", KeePassStore.SshHostsField));
        Assert.Equal(string.Empty, RawField(team, "deploy", KeePassStore.PersonalField));
        Assert.Equal("OLD KEY", RawAttachment(team, "deploy", "deploy.old"));

        var teammate = VaultBody.CreateEmpty();
        Assert.Equal(1, Load(team, teammate).Added);
        var loaded = teammate.Find(Deploy)!;
        Assert.Equal(KeyPath, loaded.StoredPath);
        Assert.Equal("PRIVATE KEY", Encoding.UTF8.GetString(loaded.GetValue()));
        Assert.Equal("OLD KEY", Encoding.UTF8.GetString(loaded.GetPrevious()!));
        Assert.Equal(Info.Hosts, loaded.SshHosts);
        Assert.All(Compare(team, teammate), c => Assert.Equal(StoreSyncState.Saved, c.State));
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

        Assert.Equal(VaultEntry.SshKeyShape, RawField(team, "deploy", KeePassStore.ShapeField));
        Assert.Equal("deploy@srv1.example.com", RawField(team, "deploy", KeePassStore.SshHostsField));
        Assert.Equal(string.Empty, RawField(team, "deploy", KeePassStore.SshTypeField));
        Assert.Equal("true", RawField(personal, "deploy", KeePassStore.PersonalField));
        Assert.Equal(PublicKey, RawField(personal, "deploy", KeePassStore.SshPublicKeyField));
        Assert.Equal(string.Empty, RawField(personal, "deploy", KeePassStore.SshHostsField));

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
        entry.Strings.Set(KeePassStore.KindField, new ProtectedString(false, VaultEntry.FileKind));
        entry.Strings.Set(KeePassStore.PathField, new ProtectedString(false, "~/.ssh/authorized_keys"));
        entry.Strings.Set(KeePassStore.SshTypeField, new ProtectedString(false, SshKeyInfo.Ed25519));
        entry.Strings.Set(KeePassStore.SshPublicKeyField, new ProtectedString(false, PublicKey));
        entry.Binaries.Set("authorized_keys", new ProtectedBinary(false, Encoding.UTF8.GetBytes(PublicKey)));
        group.AddEntry(entry, true);
        db.Save(null);
        db.Close();

        using var keePass = KeePassStore.Open(team, null, Master);
        using var content = keePass.Read(Group);

        Assert.Empty(content.Items);
        Assert.Single(content.Skipped);
    }

    [Fact]
    public void OptionGivenAsAHostIsRefusedInTheVaultAndInKeePass()
    {
        Assert.Throws<VaultException>(() => VaultEntry.NewRequired(Deploy, null, Now, VaultEntry.SshKeyShape, KeyPath, ["-oProxyCommand=calc"]));

        var db = new PwDatabase();
        db.Open(IOConnectionInfo.FromPath(team), Key(), null);
        var group = db.RootGroup.FindCreateGroup("LocalVault", true).FindCreateGroup("proj", true);
        var entry = new PwEntry(true, true);
        entry.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "deploy"));
        entry.Strings.Set(KeePassStore.KindField, new ProtectedString(false, VaultEntry.FileKind));
        entry.Strings.Set(KeePassStore.PathField, new ProtectedString(false, KeyPath));
        entry.Strings.Set(KeePassStore.SshTypeField, new ProtectedString(false, SshKeyInfo.Ed25519));
        entry.Strings.Set(KeePassStore.SshPublicKeyField, new ProtectedString(false, PublicKey));
        entry.Strings.Set(KeePassStore.SshHostsField, new ProtectedString(false, "-oProxyCommand=calc"));
        entry.Binaries.Set("deploy", new ProtectedBinary(true, "KEY"u8.ToArray()));
        group.AddEntry(entry, true);
        db.Save(null);
        db.Close();

        using var keePass = KeePassStore.Open(team, null, Master);
        using var content = keePass.Read(Group);
        Assert.Empty(content.Items);
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

    private static StoreSaveResult Save(string path, VaultBody body)
    {
        using var keePass = KeePassStore.Open(path, null, Master);
        using var content = keePass.Read(Group);
        var result = StoreSync.Save(body, content, keePass, Group, _ => false, false, excludeRequired: false);
        keePass.Save();
        StoreSync.RecordSaved(body, result, Now);
        return result;
    }

    private static StoreSaveResult SavePersonal(string path, VaultBody body)
    {
        using var keePass = KeePassStore.Open(path, null, Master);
        using var content = keePass.Read(Group);
        var result = StoreSync.SavePersonal(body, content, keePass, Group, false, _ => false);
        keePass.Save();
        StoreSync.RecordSaved(body, result, Now);
        return result;
    }

    private static StoreLoadResult Load(string path, VaultBody body)
    {
        using var keePass = KeePassStore.Open(path, null, Master);
        using var content = keePass.Read(Group);
        return StoreSync.Load(body, content, _ => false, _ => false, Now);
    }

    private static StoreLoadResult LoadPersonal(string path, VaultBody body)
    {
        using var keePass = KeePassStore.Open(path, null, Master);
        using var content = keePass.Read(Group);
        return StoreSync.LoadPersonal(body, content, _ => false, Now);
    }

    private static IReadOnlyList<StoreComparison> Compare(string path, VaultBody body)
    {
        using var keePass = KeePassStore.Open(path, null, Master);
        using var content = keePass.Read(Group);
        return StoreSync.Compare(body, content);
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

}
