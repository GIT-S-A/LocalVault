using LocalVault.Core;

namespace LocalVault.Engine.Tests;

public class EntryNameTests
{
    [Theory]
    [InlineData("OsPassword", null, "OsPassword")]
    [InlineData("LOKI_PUSH_TOKEN", null, "LOKI_PUSH_TOKEN")]
    [InlineData("os.password", null, "os.password")]
    [InlineData("file.clouds.yaml", null, "file.clouds.yaml")]
    [InlineData("common/licence-key", "common", "licence-key")]
    [InlineData("prod-ch:1/db.password", "prod-ch:1", "db.password")]
    [InlineData("john.doe@example.com", null, "john.doe@example.com")]
    [InlineData("migration/jane+test@example.org", "migration", "jane+test@example.org")]
    public void ParseAcceptsValidNames(string value, string? scope, string key)
    {
        var name = EntryName.Parse(value);

        Assert.Equal(scope, name.Scope);
        Assert.Equal(key, name.Key);
        Assert.Equal(value, name.FullName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a/b/c")]
    [InlineData("/key")]
    [InlineData("scope/")]
    [InlineData("has space")]
    [InlineData("tab\tname")]
    [InlineData("dollar$")]
    [InlineData(@"back\slash")]
    [InlineData("été")]
    [InlineData("__File:clouds.yaml/x/y")]
    public void ParseRejectsInvalidNames(string value)
    {
        Assert.False(EntryName.TryParse(value, out _));
        Assert.Throws<VaultException>(() => EntryName.Parse(value));
    }

    [Fact]
    public void ParseRejectsNull()
    {
        Assert.False(EntryName.TryParse(null, out _));
    }

    [Fact]
    public void EqualityIgnoresCaseAndKeepsOriginalCase()
    {
        var upper = EntryName.Parse("Common/Key");
        var lower = EntryName.Parse("common/key");

        Assert.Equal(upper, lower);
        Assert.Equal(upper.GetHashCode(), lower.GetHashCode());
        Assert.Equal("Common/Key", upper.FullName);
    }

    [Fact]
    public void ScopedAndUnscopedNamesDiffer()
    {
        Assert.NotEqual(EntryName.Parse("key"), EntryName.Parse("common/key"));
    }

    [Fact]
    public void CombineBuildsScopedName()
    {
        var name = EntryName.Combine("prod", "db.password");

        Assert.Equal("prod/db.password", name.FullName);
        Assert.True(name.IsScoped);
    }

    [Fact]
    public void CombineRejectsInvalidSegments()
    {
        Assert.Throws<VaultException>(() => EntryName.Combine("a/b", "key"));
    }
}
