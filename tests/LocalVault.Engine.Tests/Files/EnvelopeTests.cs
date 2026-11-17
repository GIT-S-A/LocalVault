using System.Security.Cryptography;
using System.Text;
using LocalVault.Core;

namespace LocalVault.Engine.Tests;

public class EnvelopeTests
{
    private const string Passphrase = "correct horse battery";
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("""{"schema":2,"entries":[]}""");

    [Fact]
    public void LocalRoundTrips()
    {
        var text = Envelope.SealLocal(Body);

        Assert.StartsWith("LOCALVAULT-2\nlocal\nseal=dpapi-user\n", text, StringComparison.Ordinal);
        Assert.Equal(EnvelopeKind.Local, Envelope.Detect(text));
        Assert.Equal(Body, Envelope.OpenLocal(text));
    }

    [Fact]
    public void LocalRejectsDamagedBlob()
    {
        var text = "LOCALVAULT-2\nlocal\nseal=dpapi-user\n" + Convert.ToBase64String(new byte[64]) + "\n";

        Assert.Throws<VaultException>(() => Envelope.OpenLocal(text));
    }

    [Fact]
    public void LocalAcceptsCrLfLineEndings()
    {
        var text = Envelope.SealLocal(Body).Replace("\n", "\r\n", StringComparison.Ordinal);

        Assert.Equal(Body, Envelope.OpenLocal(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("OTHER-1\nx\n")]
    [InlineData("LOCALVAULT-1\nlocal\nseal=dpapi-user\nabc\n")]
    [InlineData("LOCALVAULT-x\nlocal\nseal=dpapi-user\nabc\n")]
    [InlineData("LOCALVAULT-2\nlocal\nseal\nabc\n")]
    [InlineData("LOCALVAULT-2\nlocal\nseal=dpapi-user;seal=dpapi-user\nabc\n")]
    [InlineData("LOCALVAULT-2\nexport\ncipher=aes-256-gcm;kdf=pbkdf2-sha256\na\nb\nc\nd\n")]
    [InlineData("LOCALVAULT-2\nexport\ncipher=aes-256-gcm;iterations=many;kdf=pbkdf2-sha256\na\nb\nc\nd\n")]
    public void DetectReturnsUnknownForForeignOrDamagedText(string text)
    {
        Assert.Equal(EnvelopeKind.Unknown, Envelope.Detect(text));
        Assert.Throws<VaultException>(() => Envelope.OpenLocal(text));
    }

    [Theory]
    [InlineData("LOCALVAULT-3\nlocal\nseal=dpapi-user\nabc\n")]
    [InlineData("LOCALVAULT-2\nshared\nseal=dpapi-machine\nabc\n")]
    [InlineData("LOCALVAULT-2\nlocal\nseal=dpapi-machine\nabc\n")]
    [InlineData("LOCALVAULT-2\nlocal\ncompress=zstd;seal=dpapi-user\nabc\n")]
    [InlineData("LOCALVAULT-2\nexport\ncipher=chacha20-poly1305;iterations=210000;kdf=pbkdf2-sha256\na\nb\nc\nd\n")]
    [InlineData("LOCALVAULT-2\nexport\ncipher=aes-256-gcm;iterations=210000;kdf=argon2id\na\nb\nc\nd\n")]
    [InlineData("LOCALVAULT-2\nexport\ncipher=aes-256-gcm;iterations=20000000;kdf=pbkdf2-sha256\na\nb\nc\nd\n")]
    public void DetectReportsNewerFormats(string text)
    {
        Assert.Equal(EnvelopeKind.Newer, Envelope.Detect(text));
        var error = Assert.Throws<VaultException>(() => Envelope.OpenExport(text, Passphrase));
        Assert.Equal(Envelope.NewerFormatMessage, error.Message);
    }

    [Fact]
    public void ExportWritesItsParametersInTheHeader()
    {
        var lines = Envelope.SealExport(Body, Passphrase).Split('\n');

        Assert.Equal("LOCALVAULT-2", lines[0]);
        Assert.Equal("export", lines[1]);
        Assert.Equal("cipher=aes-256-gcm;iterations=210000;kdf=pbkdf2-sha256", lines[2]);
    }

    [Fact]
    public void ExportOpensWithTheIterationCountOfItsHeader()
    {
        var text = SealExportWithIterations(Body, Passphrase, 150_000);

        Assert.Equal(Body, Envelope.OpenExport(text, Passphrase));
    }

    [Fact]
    public void ExportRejectsChangedParameters()
    {
        var lines = Envelope.SealExport(Body, Passphrase).Split('\n');
        lines[2] = lines[2].Replace("210000", "210001", StringComparison.Ordinal);

        Assert.Throws<VaultException>(() => Envelope.OpenExport(string.Join('\n', lines), Passphrase));
    }

    [Fact]
    public void ExportRoundTrips()
    {
        var text = Envelope.SealExport(Body, Passphrase);

        Assert.Equal(EnvelopeKind.Export, Envelope.Detect(text));
        Assert.Equal(7, text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Equal(Body, Envelope.OpenExport(text, Passphrase));
    }

    [Fact]
    public void ExportRejectsShortPassphrase()
    {
        Assert.Throws<VaultException>(() => Envelope.SealExport(Body, "elevenchars"));
    }

    [Fact]
    public void ExportRejectsWrongPassphrase()
    {
        var text = Envelope.SealExport(Body, Passphrase);

        Assert.Throws<VaultException>(() => Envelope.OpenExport(text, "wrong passphrase!"));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void ExportRejectsTamperedLine(int line)
    {
        var lines = Envelope.SealExport(Body, Passphrase).Split('\n');
        lines[line] = FlipFirstByte(lines[line]);

        Assert.Throws<VaultException>(() => Envelope.OpenExport(string.Join('\n', lines), Passphrase));
    }

    [Fact]
    public void ExportHeaderIsAuthenticated()
    {
        var lines = Envelope.SealExport(Body, Passphrase).Split('\n');
        var salt = Convert.FromBase64String(lines[3]);
        var key = Rfc2898DeriveBytes.Pbkdf2(Passphrase, salt, 210_000, HashAlgorithmName.SHA256, 32);
        using var aes = new AesGcm(key, 16);
        var plain = new byte[Convert.FromBase64String(lines[6]).Length];

        Assert.ThrowsAny<CryptographicException>(() => aes.Decrypt(
            Convert.FromBase64String(lines[4]),
            Convert.FromBase64String(lines[6]),
            Convert.FromBase64String(lines[5]),
            plain,
            null));
    }

    [Fact]
    public void BackupRoundTrips()
    {
        var keyFile = RandomNumberGenerator.GetBytes(32);
        var text = Envelope.SealBackup(Body, Passphrase, keyFile);

        Assert.Equal(EnvelopeKind.Backup, Envelope.Detect(text));
        Assert.Equal(KeyFileIdentity.Compute(keyFile), Envelope.ReadBackupKeyFileId(text));
        Assert.Equal(Body, Envelope.OpenBackup(text, Passphrase, keyFile));
    }

    [Fact]
    public void BackupRejectsWrongKeyFileByName()
    {
        var keyFile = RandomNumberGenerator.GetBytes(32);
        var text = Envelope.SealBackup(Body, Passphrase, keyFile);

        var error = Assert.Throws<VaultException>(
            () => Envelope.OpenBackup(text, Passphrase, RandomNumberGenerator.GetBytes(32)));
        Assert.Contains(KeyFileIdentity.Compute(keyFile), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BackupRejectsWrongPassphrase()
    {
        var keyFile = RandomNumberGenerator.GetBytes(32);
        var text = Envelope.SealBackup(Body, Passphrase, keyFile);

        Assert.Throws<VaultException>(() => Envelope.OpenBackup(text, "another passphrase", keyFile));
    }

    [Fact]
    public void BackupRejectsTamperedKeyFileId()
    {
        var keyFile = RandomNumberGenerator.GetBytes(32);
        var lines = Envelope.SealBackup(Body, Passphrase, keyFile).Split('\n');
        lines[2] = lines[2].Replace(KeyFileIdentity.Compute(keyFile), new string('0', 64), StringComparison.Ordinal);

        Assert.Throws<VaultException>(() => Envelope.OpenBackup(string.Join('\n', lines), Passphrase, keyFile));
    }

    [Fact]
    public void BackupRejectsKeyFileOfWrongLength()
    {
        Assert.Throws<VaultException>(() => Envelope.SealBackup(Body, Passphrase, new byte[16]));
    }

    [Fact]
    public void LegacyExportOpensFormatOfReferenceScripts()
    {
        var plain = Encoding.UTF8.GetBytes("""{"OsPassword":"s3cret"}""");
        var text = SealLikeReferenceScript(plain, Passphrase);

        Assert.Equal(EnvelopeKind.LegacyExport, Envelope.Detect(text));
        Assert.Equal(plain, Envelope.OpenLegacyExport(text, Passphrase));
        Assert.Throws<VaultException>(() => Envelope.OpenLegacyExport(text, "wrong passphrase!"));
    }

    internal static string SealExportWithIterations(byte[] body, string passphrase, int iterations)
    {
        var header = $"cipher=aes-256-gcm;iterations={iterations};kdf=pbkdf2-sha256";
        var salt = RandomNumberGenerator.GetBytes(16);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var key = Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, iterations, HashAlgorithmName.SHA256, 32);
        var cipher = new byte[body.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(key, 16))
        {
            aes.Encrypt(nonce, body, cipher, tag, Encoding.UTF8.GetBytes("LOCALVAULT-2\nexport\n" + header));
        }

        return string.Join(
            "\n",
            "LOCALVAULT-2",
            "export",
            header,
            Convert.ToBase64String(salt),
            Convert.ToBase64String(nonce),
            Convert.ToBase64String(tag),
            Convert.ToBase64String(cipher)) + "\n";
    }

    internal static string SealLikeReferenceScript(byte[] body, string passphrase)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var key = Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, 210_000, HashAlgorithmName.SHA256, 32);
        var cipher = new byte[body.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(key, 16))
        {
            aes.Encrypt(nonce, body, cipher, tag);
        }

        return string.Join(
            "\r\n",
            "SAAS-VAULT-1",
            Convert.ToBase64String(salt),
            Convert.ToBase64String(nonce),
            Convert.ToBase64String(tag),
            Convert.ToBase64String(cipher)) + "\r\n";
    }

    private static string FlipFirstByte(string base64)
    {
        var bytes = Convert.FromBase64String(base64);
        bytes[0] ^= 0xFF;
        return Convert.ToBase64String(bytes);
    }
}
