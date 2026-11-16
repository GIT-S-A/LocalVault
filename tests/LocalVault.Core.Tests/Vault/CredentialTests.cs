using System.Text;

namespace LocalVault.Core.Tests;

public sealed class EntryReferenceTests
{
    [Theory]
    [InlineData("gh", "gh", EntryPart.Password, 1)]
    [InlineData("gh#password", "gh", EntryPart.Password, 1)]
    [InlineData("gh#USER", "gh#user", EntryPart.User, 1)]
    [InlineData("team/gh#totp", "team/gh#totp", EntryPart.Totp, 1)]
    [InlineData("gh#url", "gh#url", EntryPart.Url, 1)]
    [InlineData("gh#url1", "gh#url", EntryPart.Url, 1)]
    [InlineData("gh#url3", "gh#url3", EntryPart.Url, 3)]
    public void PartsParseAndPrint(string text, string printed, EntryPart part, int urlNumber)
    {
        var reference = EntryReference.Parse(text);

        Assert.Equal(part, reference.Part);
        Assert.Equal(urlNumber, reference.UrlNumber);
        Assert.Equal(printed, reference.ToString());
        Assert.Equal(EntryName.Parse(text.Split('#')[0]), reference.Name);
    }

    [Theory]
    [InlineData("gh#")]
    [InlineData("gh#pin")]
    [InlineData("gh#url0")]
    [InlineData("gh#user#x")]
    [InlineData("a b#user")]
    public void UnknownPartsAreRefused(string text)
    {
        Assert.False(EntryReference.TryParse(text, out _));
        Assert.Throws<VaultException>(() => EntryReference.Parse(text));
    }

    [Fact]
    public void TheErrorListsTheParts()
    {
        Assert.Contains("#totp", Assert.Throws<VaultException>(() => EntryReference.Parse("gh#pin")).Message, StringComparison.Ordinal);
        Assert.False(EntryName.TryParse("gh#user", out _));
    }
}

public sealed class TotpSecretTests
{
    private const string RfcSha1 = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";
    private const string RfcSha256 = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZA";
    private const string RfcSha512 = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNA";

    [Theory]
    [InlineData(RfcSha1, "SHA1", 59, "94287082")]
    [InlineData(RfcSha1, "SHA1", 1111111109, "07081804")]
    [InlineData(RfcSha256, "SHA256", 59, "46119246")]
    [InlineData(RfcSha512, "SHA512", 59, "90693936")]
    [InlineData(RfcSha512, "SHA512", 20000000000, "47863826")]
    public void CodesMatchRfc6238(string secret, string algorithm, long seconds, string code)
    {
        var totp = TotpSecret.Create(secret, 8, 30, algorithm);

        Assert.Equal(code, totp.Code(DateTimeOffset.FromUnixTimeSeconds(seconds)));
    }

    [Fact]
    public void UriAndSeedGiveTheSameSecret()
    {
        var fromUri = TotpSecret.Parse("otpauth://totp/GitHub:jdoe?secret=jbswy3dpehpk3pxp&issuer=GitHub");
        var fromSeed = TotpSecret.Parse(" jbsw y3dp ehpk 3pxp ");

        Assert.Equal(fromSeed.Canonical, fromUri.Canonical);
        Assert.Equal("secret=JBSWY3DPEHPK3PXP&algorithm=SHA1&digits=6&period=30", fromSeed.Canonical);
        Assert.Equal(fromSeed.Canonical, TotpSecret.Parse(fromSeed.ToUri("any label")).Canonical);
    }

    [Fact]
    public void UriParametersAreKept()
    {
        var totp = TotpSecret.Parse("otpauth://totp/x?secret=JBSWY3DPEHPK3PXP&digits=8&period=60&algorithm=SHA256");

        Assert.Equal((8, 60, "SHA256"), (totp.Digits, totp.Period, totp.Algorithm));
        Assert.Equal(8, totp.Code(DateTimeOffset.UnixEpoch).Length);
    }

    [Theory]
    [InlineData("otpauth://hotp/x?secret=JBSWY3DPEHPK3PXP&counter=1")]
    [InlineData("otpauth://totp/x?issuer=nobody")]
    [InlineData("otpauth://totp/x?secret=JBSWY3DPEHPK3PXP&digits=4")]
    [InlineData("otpauth://totp/x?secret=JBSWY3DPEHPK3PXP&algorithm=MD5")]
    [InlineData("not base32 1890")]
    [InlineData("")]
    public void BadSecretsAreRefused(string text) => Assert.Throws<VaultException>(() => TotpSecret.Parse(text));

    [Fact]
    public void KeysRoundTripThroughBase32()
    {
        var key = Encoding.ASCII.GetBytes("12345678901234567890");

        Assert.Equal(RfcSha1, TotpSecret.FromKey(key, 6, 30, "SHA1").Secret);
        Assert.Equal(key, TotpSecret.Create(RfcSha1, 6, 30, "HMAC-SHA-1").Key());
    }
}

public sealed class CredentialEntryTests
{
    private const string Seed = "JBSWY3DPEHPK3PXP";
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly EntryName Gh = EntryName.Parse("gh");

    private static VaultEntry Credential() =>
        VaultEntry.NewCredential(Gh, "jdoe", "pa55word"u8, TotpSecret.Parse(Seed), ["https://github.com", " https://api.github.com "], Now);

    [Fact]
    public void ACredentialRoundTripsThroughTheBody()
    {
        var body = VaultBody.CreateEmpty();
        body.Set(Credential());

        var entry = Assert.Single(VaultBody.Parse(body.ToUtf8()).Entries);

        Assert.True(entry.IsCredential);
        Assert.Equal("jdoe", entry.UserName);
        Assert.Equal(["https://github.com", "https://api.github.com"], entry.Urls);
        Assert.Equal(Seed, entry.Totp!.Secret);
        Assert.Equal("pa55word", Encoding.UTF8.GetString(entry.GetValue()));
        Assert.Equal(Credential().ContentHash(), entry.ContentHash());
    }

    [Fact]
    public void PartsAreRead()
    {
        var entry = Credential();

        Assert.Equal("pa55word", Read(entry, "gh"));
        Assert.Equal("jdoe", Read(entry, "gh#user"));
        Assert.Equal("https://api.github.com", Read(entry, "gh#url2"));
        Assert.Equal(TotpSecret.Parse(Seed).Code(Now), Read(entry, "gh#totp"));
        Assert.Throws<VaultException>(() => Read(entry, "gh#url3"));
        var secret = VaultEntry.NewSecret(Gh, "x"u8, Now);
        Assert.Contains("not a credential", Assert.Throws<VaultException>(() => Read(secret, "gh#user")).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryPartChangesTheHash()
    {
        var hashes = new[]
        {
            Credential().ContentHash(),
            Change(Credential(), "gh#user", "other"),
            Change(Credential(), "gh#url2", "https://x"),
            Change(Credential(), "gh#totp", "JBSWY3DPEHPK3PXQ"),
            Credential().WithPart(EntryReference.Parse("gh"), null, "other"u8, Now).ContentHash(),
            VaultEntry.NewSecret(Gh, "pa55word"u8, Now).ContentHash(),
        };

        Assert.Equal(hashes.Length, hashes.Distinct().Count());
    }

    [Fact]
    public void ASecretBecomesACredentialAndKeepsItsValue()
    {
        var secret = VaultEntry.NewSecret(Gh, "kept"u8, Now);

        var credential = secret.WithPart(EntryReference.Parse("gh#user"), "jdoe", [], Now);

        Assert.True(credential.IsCredential);
        Assert.Equal("kept", Read(credential, "gh"));
        Assert.True(secret.WithPart(EntryReference.Parse("gh"), null, "new"u8, Now).IsSecret);
    }

    [Fact]
    public void UrlsAreAddedReplacedAndRemovedInOrder()
    {
        var entry = Credential().WithPart(EntryReference.Parse("gh#url3"), "https://three", [], Now);
        Assert.Equal(3, entry.Urls.Count);

        entry = entry.WithPart(EntryReference.Parse("gh#url"), "https://one", [], Now);
        entry = entry.WithPart(EntryReference.Parse("gh#url2"), string.Empty, [], Now);

        Assert.Equal(["https://one", "https://three"], entry.Urls);
        Assert.Contains("gh#url3", Assert.Throws<VaultException>(() => entry.WithPart(EntryReference.Parse("gh#url5"), "x", [], Now)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplacingACredentialWithASecretDropsItsParts()
    {
        var body = VaultBody.CreateEmpty();
        body.Set(Credential());

        body.Set(VaultEntry.NewSecret(Gh, "plain"u8, Now));

        var entry = Assert.Single(VaultBody.Parse(body.ToUtf8()).Entries);
        Assert.True(entry.IsSecret);
        Assert.Null(entry.UserName);
        Assert.Empty(entry.Urls);
        Assert.Null(entry.Totp);
    }

    [Fact]
    public void ImportSeesSameAndDifferentCredentials()
    {
        Assert.True(Credential().HasSameContent(Credential()));
        Assert.False(Credential().HasSameContent(Credential().WithPart(EntryReference.Parse("gh#user"), "x", [], Now)));
    }

    [Fact]
    public void APersonalCredentialExportsWithoutItsParts()
    {
        var body = VaultBody.CreateEmpty();
        body.Set(VaultEntry.NewRequired(Gh, "ask IT", Now));
        body.Set(Credential());

        var exported = Assert.Single(body.ForExport().Entries);

        Assert.True(exported.IsMissing);
        Assert.Null(exported.UserName);
        Assert.Null(exported.Totp);
    }

    [Theory]
    [InlineData("""{"schema":3,"entries":[{"name":"a","kind":"credential","value":"","user":5}]}""")]
    [InlineData("""{"schema":3,"entries":[{"name":"a","kind":"credential","value":"","urls":"x"}]}""")]
    [InlineData("""{"schema":3,"entries":[{"name":"a","kind":"credential","value":"","totp":"otpauth://hotp/x?secret=AA"}]}""")]
    public void DamagedCredentialsAreRefused(string json) =>
        Assert.Throws<VaultException>(() => VaultBody.Parse(Encoding.UTF8.GetBytes(json)));

    private static string Read(VaultEntry entry, string reference) =>
        Encoding.UTF8.GetString(entry.ReadPart(EntryReference.Parse(reference), Now));

    private static string Change(VaultEntry entry, string reference, string text) =>
        entry.WithPart(EntryReference.Parse(reference), text, [], Now).ContentHash();
}
