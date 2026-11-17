using System.Text;
using LocalVault.Core;

namespace LocalVault.Engine.Tests;

public class LegacyMapperTests
{
    private const string FixturePassphrase = "fixture passphrase 1";
    private static readonly DateTimeOffset Created = new(2026, 10, 8, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ReferenceScriptExportOpensAndMaps()
    {
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "legacy-vault-export.v1"));

        var values = LegacyMapper.ParseExportBody(Envelope.OpenLegacyExport(text, FixturePassphrase));
        var mapping = LegacyMapper.Map(values, Created);

        Assert.Empty(mapping.Refused);
        Assert.Equal("dummy-loki-token-1", Secret(mapping, "LOKI_PUSH_TOKEN"));
        Assert.Equal("dummy s3 secret with spaces & ünïcode", Secret(mapping, "S3_SECRET_KEY"));
    }

    [Fact]
    public void FileNamesThatAreNotStoredPathsAreRefusedAlone()
    {
        var mapping = LegacyMapper.Map(
            [new("__File:a:b", "x"), new("__File:..", "x"), new("__File:secrets.local.lv", "x"), new("OsPassword", "pw")],
            Created);

        Assert.Equal("OsPassword", Assert.Single(mapping.Entries).Name.FullName);
        Assert.Equal(["__File:a:b", "__File:..", "__File:secrets.local.lv"], mapping.Refused.Select(r => r.Name));
    }

    [Fact]
    public void FilesBecomeFileEntriesBesideTheVault()
    {
        var mapping = LegacyMapper.Map(
            [new("__File:clouds.yaml", "clouds:\r\n  x: 1\r\n")],
            Created);

        var entry = Assert.Single(mapping.Entries);
        Assert.True(entry.IsFile);
        Assert.Equal("clouds.yaml", entry.Name.FullName);
        Assert.Equal("clouds.yaml", entry.StoredPath);
        Assert.Equal("clouds:\r\n  x: 1\r\n", Encoding.UTF8.GetString(entry.GetValue()));
    }

    [Theory]
    [InlineData("__SshPrivateKey")]
    [InlineData("__SshPublicKey")]
    [InlineData("__Other")]
    [InlineData("has space")]
    [InlineData("__File:bad name")]
    public void UnsupportedNamesAreRefusedByName(string name)
    {
        var mapping = LegacyMapper.Map([new(name, "value"), new("Kept", "v")], Created);

        Assert.Equal(name, Assert.Single(mapping.Refused).Name);
        Assert.Equal("Kept", Assert.Single(mapping.Entries).Name.FullName);
    }

    [Fact]
    public void EmptyValuesAreRefused()
    {
        var mapping = LegacyMapper.Map([new("Empty", "")], Created);

        Assert.Empty(mapping.Entries);
        Assert.Equal("Empty", Assert.Single(mapping.Refused).Name);
    }

    [Fact]
    public void NamesKeptVerbatim()
    {
        var mapping = LegacyMapper.Map([new("OsPassword", "a"), new("GrafanaAdminPassword", "b")], Created);

        Assert.Equal(["OsPassword", "GrafanaAdminPassword"], mapping.Entries.Select(e => e.Name.FullName));
    }

    [Fact]
    public void CaseOnlyClashIsReportedNotDropped()
    {
        var values = LegacyMapper.ParseExportBody("""{"Foo":"1","foo":"2","Bar":"3"}"""u8);
        var mapping = LegacyMapper.Map(values, Created);

        Assert.Equal("Bar", Assert.Single(mapping.Entries).Name.FullName);
        var clash = Assert.Single(mapping.CaseClashes);
        Assert.Equal(["Foo", "foo"], clash.Select(e => e.Name.FullName));
    }

    [Fact]
    public void ParseExportBodyRejectsNonStrings()
    {
        Assert.Throws<VaultException>(() => LegacyMapper.ParseExportBody("""{"a":1}"""u8));
        Assert.Throws<VaultException>(() => LegacyMapper.ParseExportBody("""[]"""u8));
    }

    private static string Secret(LegacyMapping mapping, string name) =>
        Encoding.UTF8.GetString(mapping.Entries.Single(e => e.Name.FullName == name).GetValue());
}
