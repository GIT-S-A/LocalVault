using System.Text;

namespace LocalVault.Core.Tests;

public sealed class SshKeyTests
{
    private const string PublicKey = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIGmCmhyZfFSbAVfJ/bovNv4H3f/VIW8/KGdICZD31KiE test@example.com";
    private const string Fingerprint = "SHA256:ygwjj+x/jcbcUNmwhazPNZzoRvNv7XDnLrscAy0ZQ0M";
    private const string KeyPath = "~/.ssh/proj/deploy";
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly EntryName Deploy = EntryName.Parse("deploy");
    private static readonly SshKeyInfo Info = new(SshKeyInfo.Ed25519, PublicKey, ["deploy@srv1.example.com"]);

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
    public void OldKeyFileDependsOnWhetherThePreviousValueIsAPrivateKey()
    {
        Assert.True(SshKeyFiles.IsPrivateKey("-----BEGIN OPENSSH PRIVATE KEY-----\nabc\n"u8));
        Assert.False(SshKeyFiles.IsPrivateKey(System.Text.Encoding.UTF8.GetBytes(PublicKey)));
        Assert.Equal(Path.Combine("k", "deploy.old"), SshKeyFiles.OldKeyOf(Path.Combine("k", "deploy")));
        Assert.Equal(Path.Combine("k", "deploy.old.pub"), SshKeyFiles.OldPublicKeyOf(Path.Combine("k", "deploy")));
    }

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

}
