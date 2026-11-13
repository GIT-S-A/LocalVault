using System.Text;
using System.Text.Json.Nodes;

namespace LocalVault.Core.Tests;

public sealed class HelloSealTests : IDisposable
{
    private const string SshPublicKey = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIGmCmhyZfFSbAVfJ/bovNv4H3f/VIW8/KGdICZD31KiE test@example.com";
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly EntryName Cryptomator = EntryName.Parse("cryptomator");
    private static readonly EntryName Other = EntryName.Parse("other");

    private readonly SoftwareHelloKey key = new();

    public void Dispose() => key.Dispose();

    [Fact]
    public void FirstSealCreatesTheKeyWithOnePrompt()
    {
        var body = VaultBody.CreateEmpty();

        Seal(body, Secret(Cryptomator, "s3cret"));

        Assert.Equal(1, key.Prompts);
        Assert.Equal(HelloState.Ready, key.State);
        Assert.Equal(key.KeyId, body.HelloKey!.KeyId);
        Assert.Equal(HelloSeal.Pad, body.HelloKey.Pad);
    }

    [Fact]
    public void SealingWithAnExistingKeyNeverPrompts()
    {
        key.Create("setup");
        var body = VaultBody.CreateEmpty();

        Seal(body, Secret(Cryptomator, "s3cret"));
        Seal(body, Secret(Other, "x"));

        Assert.Equal(1, key.Prompts);
    }

    [Fact]
    public void ASecretRoundTripsThroughTheVaultFile()
    {
        var body = VaultBody.CreateEmpty();
        Seal(body, Secret(Cryptomator, "s3cret"));

        var reread = VaultBody.Parse(body.ToUtf8());
        using var session = HelloSeal.Open(reread, key, "read");
        var plain = session.Unseal(reread.Find(Cryptomator)!);

        Assert.Equal(VaultEntry.SecretKind, plain.Kind);
        Assert.Equal("s3cret", Encoding.UTF8.GetString(plain.GetValue()));
        Assert.False(plain.IsHello);
    }

    [Fact]
    public void OneSessionUnsealsSeveralEntriesWithOnePrompt()
    {
        key.Create("setup");
        var body = VaultBody.CreateEmpty();
        Seal(body, Secret(Cryptomator, "a"));
        Seal(body, Secret(Other, "b"));
        var before = key.Prompts;

        using (var session = HelloSeal.Open(body, key, "read two"))
        {
            Assert.Equal("a", Encoding.UTF8.GetString(session.Unseal(body.Find(Cryptomator)!).GetValue()));
            Assert.Equal("b", Encoding.UTF8.GetString(session.Unseal(body.Find(Other)!).GetValue()));
        }

        Assert.Equal(before + 1, key.Prompts);
        Assert.Equal("read two", key.Messages[^1]);
    }

    [Fact]
    public void ACredentialKeepsEveryPartAndHidesThemAll()
    {
        var totp = TotpSecret.Parse("jbswy3dpehpk3pxp");
        var credential = VaultEntry.NewCredential(Cryptomator, "jdoe", "pw"u8, totp, ["https://a.example.com"], Now);
        var body = VaultBody.CreateEmpty();

        var sealedEntry = Seal(body, credential);

        Assert.Null(sealedEntry.UserName);
        Assert.Empty(sealedEntry.Urls);
        var node = JsonNode.Parse(body.ToUtf8())!["entries"]![0]!;
        Assert.Null(node["user"]);
        Assert.Null(node["totp"]);
        using var session = HelloSeal.Open(body, key, "read");
        var plain = session.Unseal(body.Find(Cryptomator)!);
        Assert.Equal("jdoe", plain.UserName);
        Assert.Equal(["https://a.example.com"], plain.Urls);
        Assert.Equal(totp.Canonical, plain.Totp!.Canonical);
        Assert.Equal(credential.ContentHash(), plain.ContentHash());
    }

    [Fact]
    public void AFileKeepsItsPathOutsideTheSeal()
    {
        var file = VaultEntry.NewFile(Cryptomator, [1, 2, 3], "conf/key.bin", Now);
        var body = VaultBody.CreateEmpty();

        var sealedEntry = Seal(body, file);

        Assert.Equal("conf/key.bin", sealedEntry.StoredPath);
        using var session = HelloSeal.Open(body, key, "read");
        Assert.Equal([1, 2, 3], session.Unseal(sealedEntry).GetValue());
    }

    [Fact]
    public void TheSealedEntryCarriesThePlainContentHash()
    {
        var plain = Secret(Cryptomator, "s3cret");
        var body = VaultBody.CreateEmpty();

        var sealedEntry = Seal(body, plain);

        Assert.Equal(plain.ContentHash(), sealedEntry.ContentHash());
        Assert.Equal(VaultEntry.SecretKind, sealedEntry.Kind);
        Assert.True(sealedEntry.IsSecret);
        Assert.True(sealedEntry.IsHello);
        Assert.Equal(VaultEntry.HelloKind, JsonNode.Parse(body.ToUtf8())!["entries"]![0]!["kind"]!.GetValue<string>());
    }

    [Fact]
    public void TheSealedValueIsNotThePlainValue()
    {
        var body = VaultBody.CreateEmpty();

        var sealedEntry = Seal(body, Secret(Cryptomator, "s3cret-value"));

        Assert.Throws<HelloException>(() => sealedEntry.GetValue());
        Assert.DoesNotContain(Convert.ToBase64String("s3cret-value"u8), Encoding.UTF8.GetString(body.ToUtf8()), StringComparison.Ordinal);
        Assert.DoesNotContain("s3cret-value", Encoding.UTF8.GetString(body.ToUtf8()), StringComparison.Ordinal);
    }

    [Fact]
    public void ASealedValueMovedToAnotherEntryFailsToUnseal()
    {
        var body = VaultBody.CreateEmpty();
        Seal(body, Secret(Cryptomator, "a"));
        Seal(body, Secret(Other, "b"));
        var json = JsonNode.Parse(body.ToUtf8())!;
        json["entries"]![1]!["value"] = json["entries"]![0]!["value"]!.GetValue<string>();
        var swapped = VaultBody.Parse(Encoding.UTF8.GetBytes(json.ToJsonString()));

        using var session = HelloSeal.Open(swapped, key, "read");

        Assert.Throws<HelloException>(() => session.Unseal(swapped.Find(Other)!));
    }

    [Fact]
    public void AVaultWithHelloEntriesIsSchemaSixAndDropsBackWhenTheyGo()
    {
        var body = VaultBody.CreateEmpty();
        body.Set(Secret(Other, "plain"));
        Assert.Equal(VaultBody.CurrentSchema, Schema(body));

        Seal(body, Secret(Cryptomator, "a"));
        Assert.Equal(VaultBody.HelloSchema, Schema(body));
        Assert.True(VaultBody.Parse(body.ToUtf8()).IsWritable);

        body.Remove(Cryptomator);
        var json = JsonNode.Parse(body.ToUtf8())!;
        Assert.Equal(VaultBody.CurrentSchema, json["schema"]!.GetValue<int>());
        Assert.Null(json["hello"]);
    }

    [Fact]
    public void ReplacingAHelloEntryWithAPlainOneDropsTheSeal()
    {
        var body = VaultBody.CreateEmpty();
        Seal(body, Secret(Cryptomator, "a"));

        body.Set(Secret(Cryptomator, "b"));

        var entry = body.Find(Cryptomator)!;
        Assert.False(entry.IsHello);
        Assert.Null(entry.HelloInnerKind);
        Assert.Equal("b", Encoding.UTF8.GetString(entry.GetValue()));
        Assert.Equal(VaultBody.CurrentSchema, Schema(body));
    }

    [Theory]
    [InlineData(VaultEntry.SecretKind)]
    [InlineData(VaultEntry.FileKind)]
    [InlineData(VaultEntry.CredentialKind)]
    public void AnEntryMovesToHelloAndBackWithItsKindValueAndMeta(string kind)
    {
        var plain = kind switch
        {
            VaultEntry.FileKind => VaultEntry.NewFile(Cryptomator, [7, 8], "conf/key.bin", Now),
            VaultEntry.CredentialKind => VaultEntry.NewCredential(Cryptomator, "jdoe", "pw"u8, TotpSecret.Parse("jbswy3dpehpk3pxp"), ["https://a.example.com"], Now),
            _ => Secret(Cryptomator, "s3cret"),
        };
        var body = VaultBody.CreateEmpty();
        body.Set(plain);
        body.SetSharing(Cryptomator, local: true);
        body.RecordKeePassBase(Cryptomator, "db1", plain.ContentHash(), Now);
        var hash = plain.ContentHash();

        body.Set(HelloSeal.Seal(body, body.Find(Cryptomator)!, key, "create"));

        var sealedEntry = body.Find(Cryptomator)!;
        Assert.True(sealedEntry.IsHello);
        Assert.Equal(kind, sealedEntry.Kind);
        Assert.Equal(hash, sealedEntry.ContentHash());
        Assert.True(sealedEntry.IsLocal);
        Assert.Equal(hash, sealedEntry.KeePassBaseFor("db1"));
        Assert.Equal(Now, sealedEntry.Created);

        using (var session = HelloSeal.Open(body, key, "read"))
        {
            body.Set(session.Unseal(sealedEntry));
        }

        var back = VaultBody.Parse(body.ToUtf8()).Find(Cryptomator)!;
        Assert.False(back.IsHello);
        Assert.Equal(kind, back.Kind);
        Assert.Equal(hash, back.ContentHash());
        Assert.True(back.IsLocal);
        Assert.Equal(hash, back.KeePassBaseFor("db1"));
        Assert.Equal(Now, back.Created);
        Assert.Equal(VaultBody.CurrentSchema, Schema(body));
        Assert.True(back.HasSameContent(plain));
    }

    [Fact]
    public void ASealedEntryIsNotSealedTwice()
    {
        var body = VaultBody.CreateEmpty();
        var sealedEntry = Seal(body, Secret(Cryptomator, "a"));

        Assert.Throws<HelloException>(() => HelloSeal.Seal(body, sealedEntry, key, "create"));
    }

    [Fact]
    public void AMissingKeyMakesEveryHelloEntryMissing()
    {
        var body = VaultBody.CreateEmpty();
        Seal(body, Secret(Cryptomator, "a"));
        key.Delete();

        Assert.Equal(HelloSeal.MissingKey, HelloSeal.MissingReason(body, body.Find(Cryptomator)!, key));
        Assert.Contains(HelloSeal.MissingKey, Assert.Throws<HelloException>(() => HelloSeal.Open(body, key, "read")).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AReplacedKeyMakesOldEntriesMissingAndNewOnesReadable()
    {
        var body = VaultBody.CreateEmpty();
        Seal(body, Secret(Cryptomator, "old"));
        key.Create("hello reset");

        Assert.Equal(HelloSeal.ChangedKey, HelloSeal.MissingReason(body, body.Find(Cryptomator)!, key));

        Seal(body, Secret(Other, "new"));

        Assert.Equal(HelloSeal.ChangedKey, HelloSeal.MissingReason(body, body.Find(Cryptomator)!, key));
        Assert.Null(HelloSeal.MissingReason(body, body.Find(Other)!, key));
        using var session = HelloSeal.Open(body, key, "read");
        Assert.Equal("new", Encoding.UTF8.GetString(session.Unseal(body.Find(Other)!).GetValue()));
        Assert.Throws<HelloException>(() => session.Unseal(body.Find(Cryptomator)!));
    }

    [Fact]
    public void ResealingAMissingEntryRestoresIt()
    {
        var body = VaultBody.CreateEmpty();
        Seal(body, Secret(Cryptomator, "old"));
        key.Create("new pc");

        Seal(body, Secret(Cryptomator, "from store"));

        Assert.Null(HelloSeal.MissingReason(body, body.Find(Cryptomator)!, key));
        using var session = HelloSeal.Open(body, key, "read");
        Assert.Equal("from store", Encoding.UTF8.GetString(session.Unseal(body.Find(Cryptomator)!).GetValue()));
    }

    [Fact]
    public void HelloUnavailableRefusesToSealAndLeavesTheBodyAlone()
    {
        key.UnavailableReason = "this is a remote desktop session";
        var body = VaultBody.CreateEmpty();
        var before = body.ToUtf8();

        var error = Assert.Throws<HelloException>(() => HelloSeal.Seal(body, Secret(Cryptomator, "a"), key, "create"));

        Assert.Contains("remote desktop", error.Message, StringComparison.Ordinal);
        Assert.Equal(before, body.ToUtf8());
        Assert.Equal(0, key.Prompts);
    }

    [Fact]
    public void HelloUnavailableMakesEntriesMissingWithTheReason()
    {
        var body = VaultBody.CreateEmpty();
        Seal(body, Secret(Cryptomator, "a"));
        key.UnavailableReason = "Windows Hello isn't set up";

        Assert.Contains("isn't set up", HelloSeal.MissingReason(body, body.Find(Cryptomator)!, key), StringComparison.Ordinal);
        Assert.Throws<HelloException>(() => HelloSeal.Open(body, key, "read"));
    }

    [Fact]
    public void ACancelledPromptIsReportedAsCancelled()
    {
        var body = VaultBody.CreateEmpty();
        Seal(body, Secret(Cryptomator, "a"));
        key.CancelNext = true;

        Assert.Throws<HelloCancelledException>(() => HelloSeal.Open(body, key, "read"));
    }

    [Fact]
    public void SshKeysAndDeclarationsAreRefused()
    {
        var body = VaultBody.CreateEmpty();
        var ssh = VaultEntry.NewSshKey(Cryptomator, "K"u8, "~/.ssh/proj/deploy", new SshKeyInfo(SshKeyInfo.Ed25519, SshPublicKey, []), [], Now);

        Assert.Throws<VaultException>(() => HelloSeal.Seal(body, ssh, key, "create"));
        Assert.Throws<VaultException>(() => HelloSeal.Seal(body, VaultEntry.NewRequired(Other, null, Now), key, "create"));
    }

    [Fact]
    public void AHelloEntryWithoutHelloDataIsRefused()
    {
        var json = """{ "schema": 6, "entries": [ { "name": "a", "kind": "hello", "value": "" } ] }""";

        Assert.Throws<VaultException>(() => VaultBody.Parse(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void DamagedHelloKeyDataIsRefused()
    {
        var json = """{ "schema": 6, "entries": [], "hello": { "keyId": "ab" } }""";

        Assert.Throws<VaultException>(() => VaultBody.Parse(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void TheKeyIdIsTheHashOfTheWindowsPublicKeyBlob()
    {
        key.Create("setup");
        var publicKey = key.PublicKey;
        var blob = new byte[24 + publicKey.Exponent!.Length + publicKey.Modulus!.Length];
        Encoding.ASCII.GetBytes("RSA1").CopyTo(blob, 0);
        BitConverter.GetBytes(2048).CopyTo(blob, 4);
        BitConverter.GetBytes(publicKey.Exponent.Length).CopyTo(blob, 8);
        BitConverter.GetBytes(publicKey.Modulus.Length).CopyTo(blob, 12);
        publicKey.Exponent.CopyTo(blob, 24);
        publicKey.Modulus.CopyTo(blob, 24 + publicKey.Exponent.Length);

        Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(blob)), key.KeyId);
        Assert.Equal(publicKey.Modulus, NgcHelloKey.ParsePublicBlob(blob).Modulus);
    }

    private VaultEntry Seal(VaultBody body, VaultEntry plain)
    {
        var sealedEntry = HelloSeal.Seal(body, plain, key, "create");
        body.Set(sealedEntry);
        return sealedEntry;
    }

    private static VaultEntry Secret(EntryName name, string value) => VaultEntry.NewSecret(name, Encoding.UTF8.GetBytes(value), Now);

    private static int Schema(VaultBody body) => JsonNode.Parse(body.ToUtf8())!["schema"]!.GetValue<int>();
}
