using System.Text;
using System.Text.Json.Nodes;
using LocalVault.Core;

namespace LocalVault.Core.Tests;

public class VaultBodyTests
{
    private static readonly DateTimeOffset Created = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EmptyBodyRoundTrips()
    {
        var body = VaultBody.Parse(VaultBody.CreateEmpty().ToUtf8());

        Assert.Equal(VaultBody.CurrentSchema, body.Schema);
        Assert.Empty(body.Entries);
        Assert.True(body.IsWritable);
    }

    [Fact]
    public void SecretRoundTripsAsBase64Utf8()
    {
        var body = VaultBody.CreateEmpty();
        body.Set(VaultEntry.NewSecret(EntryName.Parse("os.password"), "pässwörd"u8, Created));

        var json = JsonNode.Parse(body.ToUtf8())!;
        var reparsed = VaultBody.Parse(body.ToUtf8());
        var entry = reparsed.Find(EntryName.Parse("OS.PASSWORD"))!;

        Assert.Equal(Convert.ToBase64String("pässwörd"u8), json["entries"]![0]!["value"]!.GetValue<string>());
        Assert.Equal("pässwörd", Encoding.UTF8.GetString(entry.GetValue()));
        Assert.Equal(VaultEntry.SecretKind, entry.Kind);
        Assert.Equal(Created, entry.Created);
        Assert.Null(entry.StoredPath);
    }

    [Fact]
    public void FileEntryKeepsBytesAndPath()
    {
        var bytes = new byte[] { 0, 1, 2, 255, 13, 10 };
        var body = VaultBody.CreateEmpty();
        body.Set(VaultEntry.NewFile(EntryName.Parse("clouds.yaml"), bytes, @"conf\clouds.yaml", Created));

        var entry = VaultBody.Parse(body.ToUtf8()).Entries.Single();

        Assert.True(entry.IsFile);
        Assert.Equal(bytes, entry.GetValue());
        Assert.Equal("conf/clouds.yaml", entry.StoredPath);
    }

    [Fact]
    public void SetReplacesValueOfExistingNameIgnoringCase()
    {
        var body = VaultBody.CreateEmpty();
        body.Set(VaultEntry.NewSecret(EntryName.Parse("Key"), "one"u8, Created));
        body.Set(VaultEntry.NewSecret(EntryName.Parse("key"), "two"u8, Created.AddDays(1)));

        var entry = Assert.Single(body.Entries);
        Assert.Equal("Key", entry.Name.FullName);
        Assert.Equal("two", Encoding.UTF8.GetString(entry.GetValue()));
        Assert.Equal(Created.AddDays(1), entry.Created);
    }

    [Fact]
    public void RemoveLeavesOtherEntries()
    {
        var body = VaultBody.CreateEmpty();
        body.Set(VaultEntry.NewSecret(EntryName.Parse("a"), "1"u8, Created));
        body.Set(VaultEntry.NewSecret(EntryName.Parse("b"), "2"u8, Created));

        Assert.True(body.Remove(EntryName.Parse("A")));
        Assert.False(body.Remove(EntryName.Parse("missing")));

        var reparsed = VaultBody.Parse(body.ToUtf8());
        Assert.Equal("b", Assert.Single(reparsed.Entries).Name.FullName);
    }

    [Fact]
    public void UnknownFieldsAndKindsSurviveRewrite()
    {
        const string json = """
            {
              "schema": 2,
              "future": { "x": 1 },
              "entries": [
                { "name": "a", "kind": "secret", "value": "MQ==", "extra": true,
                  "meta": { "created": "2026-10-08T12:00:00.0000000+00:00", "rotation": 30 } },
                { "name": "b", "kind": "certificate", "value": "Mg==", "meta": {} }
              ]
            }
            """;
        var body = VaultBody.Parse(Encoding.UTF8.GetBytes(json));

        body.Set(VaultEntry.NewSecret(EntryName.Parse("a"), "new"u8, Created));
        body.Set(VaultEntry.NewSecret(EntryName.Parse("c"), "3"u8, Created));

        var rewritten = JsonNode.Parse(body.ToUtf8())!;
        Assert.Equal(1, rewritten["future"]!["x"]!.GetValue<int>());
        var a = rewritten["entries"]![0]!;
        Assert.True(a["extra"]!.GetValue<bool>());
        Assert.Equal(30, a["meta"]!["rotation"]!.GetValue<int>());
        Assert.Equal(Convert.ToBase64String("new"u8), a["value"]!.GetValue<string>());
        Assert.Equal("certificate", rewritten["entries"]![1]!["kind"]!.GetValue<string>());
        Assert.False(VaultBody.Parse(body.ToUtf8()).Find(EntryName.Parse("b"))!.IsKnownKind);
    }

    [Fact]
    public void NewerSchemaIsReadableButNotWritable()
    {
        var body = VaultBody.Parse("""{ "schema": 4, "entries": [] }"""u8);

        Assert.False(body.IsWritable);
    }

    [Fact]
    public void ParseUpgradesOlderSchemaWithEachStep()
    {
        var upgrades = new Dictionary<int, Action<JsonObject>>
        {
            [2] = root => root["two"] = true,
            [3] = root => root["three"] = true,
        };

        var body = VaultBody.Parse("""{ "schema": 2, "entries": [] }"""u8, upgrades, 4);

        Assert.Equal(4, body.Schema);
        Assert.Equal(2, body.UpgradedFrom);
        Assert.True(body.IsWritable);
        var root = JsonNode.Parse(body.ToUtf8())!;
        Assert.True(root["two"]!.GetValue<bool>());
        Assert.True(root["three"]!.GetValue<bool>());
    }

    [Fact]
    public void ParseFailsWhenAnUpgradeStepIsMissing()
    {
        var upgrades = new Dictionary<int, Action<JsonObject>> { [2] = _ => { } };

        Assert.Throws<VaultException>(() => VaultBody.Parse("""{ "schema": 2, "entries": [] }"""u8, upgrades, 4));
    }

    [Fact]
    public void CurrentSchemaIsNotUpgraded()
    {
        Assert.Null(VaultBody.Parse("""{ "schema": 3, "entries": [] }"""u8).UpgradedFrom);
    }

    [Fact]
    public void EveryOlderSchemaHasAnUpgradeStep()
    {
        for (var schema = 2; schema < VaultBody.CurrentSchema; schema++)
        {
            Assert.True(BodyUpgrades.Steps.ContainsKey(schema), $"No upgrade from schema {schema}.");
        }
    }

    [Theory]
    [InlineData("""[]""")]
    [InlineData("""{ "entries": [] }""")]
    [InlineData("""{ "schema": 1, "entries": [] }""")]
    [InlineData("""{ "schema": 2 }""")]
    [InlineData("""{ "schema": 2, "entries": [ 1 ] }""")]
    [InlineData("""{ "schema": 2, "entries": [ { "name": "a b", "kind": "secret", "value": "" } ] }""")]
    [InlineData("""{ "schema": 2, "entries": [ { "name": "a", "value": "" } ] }""")]
    [InlineData("""not json""")]
    public void ParseRejectsMalformedBodies(string json)
    {
        Assert.Throws<VaultException>(() => VaultBody.Parse(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void ParseRejectsNamesDifferingOnlyByCase()
    {
        const string json = """
            { "schema": 2, "entries": [
              { "name": "Foo", "kind": "secret", "value": "" },
              { "name": "foo", "kind": "secret", "value": "" } ] }
            """;

        Assert.Throws<VaultException>(() => VaultBody.Parse(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void WithoutLocalSettingsDropsBackupOnly()
    {
        var body = VaultBody.CreateEmpty();
        body.Set(VaultEntry.NewSecret(EntryName.Parse("a"), "1"u8, Created));
        body.Backup = BackupSettings.Create("0123456789abcdef", "D:/b", "passphrase-123".AsSpan(), new byte[32]);

        var transfer = body.WithoutLocalSettings();

        Assert.Null(transfer.Backup);
        Assert.Single(transfer.Entries);
        Assert.NotNull(body.Backup);
        Assert.DoesNotContain("passphrase", Encoding.UTF8.GetString(transfer.ToUtf8()), StringComparison.Ordinal);
    }

    [Fact]
    public void HasSameContentComparesKindValueAndPath()
    {
        var name = EntryName.Parse("x");
        var secret = VaultEntry.NewSecret(name, "v"u8, Created);

        Assert.True(secret.HasSameContent(VaultEntry.NewSecret(name, "v"u8, Created.AddDays(3))));
        Assert.False(secret.HasSameContent(VaultEntry.NewSecret(name, "w"u8, Created)));
        Assert.False(secret.HasSameContent(VaultEntry.NewFile(name, "v"u8, "x", Created)));
        Assert.False(VaultEntry.NewFile(name, "v"u8, "x", Created)
            .HasSameContent(VaultEntry.NewFile(name, "v"u8, "y", Created)));
    }

    [Theory]
    [InlineData("../x")]
    [InlineData("a/../../x")]
    [InlineData("C:/x")]
    [InlineData("/x")]
    [InlineData("")]
    [InlineData("a//b")]
    [InlineData(".git/hooks/pre-commit")]
    [InlineData("sub/.GIT/config")]
    [InlineData("secrets.local.lv")]
    [InlineData("infra/Secrets.Local.clixml")]
    [InlineData("a./b")]
    [InlineData("a /b")]
    public void StoredPathRejectsEscapes(string path)
    {
        Assert.Throws<VaultException>(() => StoredPathRules.Normalize(path));
    }

    [Theory]
    [InlineData("""{"schema":2,"entries":[{"name":"a","kind":"secret","value":"!!"}]}""")]
    [InlineData("""{"schema":2,"entries":[{"name":"a","kind":"secret","value":"","meta":{"created":5}}]}""")]
    [InlineData("""{"schema":2,"entries":[{"name":"a","kind":"secret","value":"","meta":{"created":"soon"}}]}""")]
    [InlineData("""{"schema":2,"entries":[{"name":"a","kind":"secret","value":"","meta":[]}]}""")]
    [InlineData("""{"schema":2,"entries":[{"name":"a","kind":"file","value":"","meta":{"path":1}}]}""")]
    [InlineData("""{"schema":2,"entries":[{"name":"a","name":"b","kind":"secret","value":""}]}""")]
    [InlineData("""{"schema":2,"schema":2,"entries":[]}""")]
    [InlineData("""{"schema":2,"exported":"never","entries":[]}""")]
    public void ParseRejectsDamagedFields(string json)
    {
        Assert.Throws<VaultException>(() => VaultBody.Parse(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void KindFollowsTheStoredContent()
    {
        var body = VaultBody.CreateEmpty();
        var name = EntryName.Parse("a");
        body.Set(VaultEntry.NewSecret(name, "v"u8, Created));

        body.Set(VaultEntry.NewFile(name, "v"u8, "a.txt", Created));

        Assert.True(body.Find(name)!.IsFile);
    }

    [Fact]
    public void StoredPathResolvesUnderBase()
    {
        var root = Path.GetTempPath();

        Assert.Equal(Path.Combine(root, "a", "b.txt"), StoredPathRules.Resolve(root, "a/b.txt"));
    }
}
