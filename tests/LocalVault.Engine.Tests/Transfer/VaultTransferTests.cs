using System.Security.Cryptography;
using System.Text;
using LocalVault.Core;

namespace LocalVault.Engine.Tests;

public sealed class VaultTransferTests : IDisposable
{
    private const string Passphrase = "export passphrase";
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 9, 0, 0, TimeSpan.Zero);
    private readonly TempDirectory directory = new();

    public void Dispose() => directory.Dispose();

    [Fact]
    public void ExportRoundTripsWithoutBackupSettings()
    {
        var body = Body(("a", "1"), ("common/b", "2"));
        body.Backup = BackupSettings.Create("0123456789abcdef", "D:/x", "backup passphrase".AsSpan(), new byte[32]);
        var path = directory.Combine("out", "v.export");

        VaultTransfer.Export(body, path, Passphrase, overwrite: false, Now);

        Assert.Equal(ImportSourceKind.Export, VaultTransfer.DetectSource(path));
        var content = VaultTransfer.OpenExport(path, Passphrase);
        Assert.Equal(["a", "common/b"], content.Entries.Select(e => e.Name.FullName));
        var opened = VaultBody.Parse(Envelope.OpenExport(File.ReadAllText(path), Passphrase));
        Assert.Null(opened.Backup);
        Assert.Equal(Now, opened.Exported);
    }

    [Fact]
    public void ExportRefusesExistingFileWithoutOverwrite()
    {
        var path = directory.Combine("v.export");
        File.WriteAllText(path, "previous backup");

        Assert.Throws<VaultException>(() => VaultTransfer.Export(Body(("a", "1")), path, Passphrase, false, Now));
        Assert.Equal("previous backup", File.ReadAllText(path));

        VaultTransfer.Export(Body(("a", "1")), path, Passphrase, overwrite: true, Now);
        Assert.Equal(ImportSourceKind.Export, VaultTransfer.DetectSource(path));
    }

    [Fact]
    public void ExportRejectsShortPassphraseAndWritesNothing()
    {
        var path = directory.Combine("v.export");

        Assert.Throws<VaultException>(() => VaultTransfer.Export(Body(("a", "1")), path, "short", false, Now));
        Assert.Empty(Directory.EnumerateFileSystemEntries(directory.Path));
    }

    [Fact]
    public void SealedFileVerifyFailureLeavesNoFile()
    {
        var path = directory.Combine("x.export");

        Assert.Throws<VaultException>(() => SealedFile.Write(
            path, "content", _ => throw new VaultException("bad"), overwrite: false));
        Assert.Empty(Directory.EnumerateFileSystemEntries(directory.Path));
    }

    [Fact]
    public void BackupSourceOpensWithKeyFile()
    {
        var keyFile = RandomNumberGenerator.GetBytes(32);
        var path = directory.Combine("b.lvbak");
        File.WriteAllText(path, Envelope.SealBackup(Body(("a", "1")).ToUtf8(), Passphrase, keyFile));

        Assert.Equal(ImportSourceKind.Backup, VaultTransfer.DetectSource(path));
        Assert.Equal("a", VaultTransfer.OpenBackup(path, Passphrase, keyFile).Entries.Single().Name.FullName);
    }

    [Fact]
    public void LegacyExportSourceMapsNames()
    {
        var path = directory.Combine("old.export");
        File.WriteAllText(path, EnvelopeTests.SealLikeReferenceScript(
            Encoding.UTF8.GetBytes("""{"OsPassword":"x","__SshPrivateKey":"k","__File:clouds.yaml":"c"}"""),
            Passphrase));

        Assert.Equal(ImportSourceKind.LegacyExport, VaultTransfer.DetectSource(path));
        var content = VaultTransfer.OpenLegacyExport(path, Passphrase, Now);
        Assert.Equal(["OsPassword", "clouds.yaml"], content.Entries.Select(e => e.Name.FullName));
        Assert.Equal("__SshPrivateKey", Assert.Single(content.Refused).Name);
    }

    [Fact]
    public void LocalVaultIsNotAnImportSource()
    {
        var path = directory.Combine("secrets.local.lv");
        File.WriteAllText(path, Envelope.SealLocal(Body(("a", "1")).ToUtf8()));

        var error = Assert.Throws<VaultException>(() => VaultTransfer.DetectSource(path));
        Assert.Contains("Export-Vault", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NewerEnvelopeIsNotAnImportSource()
    {
        var path = directory.Combine("next.export");
        File.WriteAllText(path, "LOCALVAULT-3\nexport\nkdf=argon2id\na\nb\nc\nd\n");

        var error = Assert.Throws<VaultException>(() => VaultTransfer.DetectSource(path));
        Assert.Contains(Envelope.NewerFormatMessage, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportOfNewerSchemaIsNotImported()
    {
        var path = directory.Combine("schema7.export");
        File.WriteAllText(path, Envelope.SealExport("""{"schema":7,"entries":[]}"""u8, Passphrase));

        var error = Assert.Throws<VaultException>(() => VaultTransfer.OpenExport(path, Passphrase));
        Assert.Contains(Envelope.NewerFormatMessage, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(".git/hooks/pre-commit")]
    [InlineData("secrets.local.lv")]
    [InlineData("../outside.txt")]
    public void ExportWithAnUnsafeStoredPathIsDamaged(string storedPath)
    {
        var path = directory.Combine("bad.export");
        var json = $$$"""{"schema":2,"entries":[{"name":"f","kind":"file","value":"eA==","meta":{"path":"{{{storedPath}}}"}}]}""";
        File.WriteAllText(path, Envelope.SealExport(Encoding.UTF8.GetBytes(json), Passphrase));

        var error = Assert.Throws<VaultException>(() => VaultTransfer.OpenExport(path, Passphrase));
        Assert.Contains("damaged", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("localvault-2-beta1")]
    public void PublishedExportFixtureOpens(string release)
    {
        var content = VaultTransfer.OpenExport(Fixture(release + ".export"), FixturePassphrase);

        AssertFixtureEntries(content);
    }

    [Theory]
    [InlineData("localvault-2-beta1")]
    public void PublishedBackupFixtureOpens(string release)
    {
        var keyFile = File.ReadAllBytes(Fixture(release + ".lvkey"));

        var content = VaultTransfer.OpenBackup(Fixture(release + ".lvbak"), FixturePassphrase, keyFile);

        AssertFixtureEntries(content);
    }

    [Fact]
    public void ClixmlIsDetectedAsLegacyVault()
    {
        var path = directory.Combine("secrets.local.clixml");
        File.WriteAllText(path, "<Objs Version=\"1.1.0.1\"></Objs>");

        Assert.Equal(ImportSourceKind.LegacyVault, VaultTransfer.DetectSource(path));
    }

    [Fact]
    public void PlanClassifiesMissingIdenticalAndDifferent()
    {
        var current = Body(("same", "1"), ("changed", "old"));
        var incoming = new ImportSourceContent(Body(("same", "1"), ("changed", "new"), ("added", "3")).Entries, [], []);

        var plan = ImportPlan.Create(current, incoming);

        Assert.Equal(["added"], plan.Missing.Select(e => e.Name.FullName));
        Assert.Equal(["same"], plan.Identical.Select(e => e.Name.FullName));
        Assert.Equal(["changed"], plan.Different.Select(e => e.Name.FullName));
        Assert.True(plan.HasConflicts);
    }

    [Fact]
    public void PlanSelectsMissingAndChosenOnly()
    {
        var current = Body(("x", "old"), ("y", "old"));
        var incoming = new ImportSourceContent(Body(("x", "new"), ("y", "new"), ("z", "1")).Entries, [], []);
        var plan = ImportPlan.Create(current, incoming);

        Assert.Equal(["z"], plan.Select([], replaceAll: false).Select(e => e.Name.FullName));
        Assert.Equal(["z", "x"], plan.Select(["X"], replaceAll: false).Select(e => e.Name.FullName));
        Assert.Equal(["z", "x", "y"], plan.Select([], replaceAll: true).Select(e => e.Name.FullName));
        Assert.Equal(["y"], plan.Unresolved(["x"], replaceAll: false));
        Assert.Empty(plan.Unresolved([], replaceAll: true));
    }

    [Fact]
    public void PlanCaseClashNeedsExactChoice()
    {
        var mapping = LegacyMapper.Map([new("Foo", "1"), new("foo", "2")], Now);
        var plan = ImportPlan.Create(VaultBody.CreateEmpty(), new ImportSourceContent(mapping.Entries, [], mapping.CaseClashes));

        Assert.True(plan.HasConflicts);
        Assert.Equal(["Foo", "foo"], plan.Unresolved([], replaceAll: true));
        Assert.Equal("2", Encoding.UTF8.GetString(Assert.Single(plan.Select(["foo"], false)).GetValue()));
        Assert.Throws<VaultException>(() => plan.Select(["Foo", "foo"], false));
    }

    private const string FixturePassphrase = "fixture passphrase 2";

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    private static void AssertFixtureEntries(ImportSourceContent content)
    {
        var entries = content.Entries.ToDictionary(e => e.Name.FullName);
        Assert.Equal(["FixtureSecret", "common/db.password", "fixture.txt"], entries.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("fixture value"u8.ToArray(), entries["FixtureSecret"].GetValue());
        Assert.Equal("common value"u8.ToArray(), entries["common/db.password"].GetValue());
        Assert.True(entries["fixture.txt"].IsFile);
        Assert.Equal("config/fixture.txt", entries["fixture.txt"].StoredPath);
        Assert.Equal("fixture file\n"u8.ToArray(), entries["fixture.txt"].GetValue());
    }

    private static VaultBody Body(params (string Name, string Value)[] entries)
    {
        var body = VaultBody.CreateEmpty();
        foreach (var (name, value) in entries)
        {
            body.Set(VaultEntry.NewSecret(EntryName.Parse(name), Encoding.UTF8.GetBytes(value), Now));
        }

        return body;
    }
}
