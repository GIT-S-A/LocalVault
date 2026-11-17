using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace LocalVault.Engine;

public enum EnvelopeKind
{
    Unknown,
    Local,
    Export,
    Backup,
    LegacyExport,
    Newer,
}

public static class Envelope
{
    public const string Magic = MagicPrefix + "2";
    public const string LegacyMagic = "SAAS-VAULT-1";
    public const string NewerFormatMessage = "It was written by a newer LocalVault. Update the module.";
    public const int MinimumPassphraseLength = 12;
    public const int KeyFileLength = 32;
    public const int Iterations = 210_000;
    public const int MinimumIterations = 100_000;
    public const int MaximumIterations = 10_000_000;

    private const string MagicPrefix = "LOCALVAULT-";
    private const int MagicVersion = 2;
    private const string LocalMode = "local";
    private const string ExportMode = "export";
    private const string BackupMode = "backup";
    private const string SealParameter = "seal";
    private const string KdfParameter = "kdf";
    private const string IterationsParameter = "iterations";
    private const string CipherParameter = "cipher";
    private const string KeyFileParameter = "keyfile";
    private const string DpapiUser = "dpapi-user";
    private const string PassphraseKdf = "pbkdf2-sha256";
    private const string KeyFileKdf = "pbkdf2-sha256-hkdf-keyfile";
    private const string AesGcm256 = "aes-256-gcm";
    private const int HeaderLineCount = 3;
    private const int LocalLineCount = HeaderLineCount + 1;
    private const int SealedLineCount = HeaderLineCount + 4;
    private const int KeyLength = 32;
    private const int SaltLength = 16;
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private const int KeyFileIdLength = 64;
    private static readonly byte[] BackupInfo = Encoding.UTF8.GetBytes(Magic + " backup");

    public static EnvelopeKind Detect(string text) => ReadHeader(SplitLines(text)).Kind;

    public static string SealLocal(ReadOnlySpan<byte> body)
    {
        var sealedBody = ProtectedData.Protect(body.ToArray(), null, DataProtectionScope.CurrentUser);
        return Join(Magic, LocalMode, Parameters((SealParameter, DpapiUser)), Convert.ToBase64String(sealedBody));
    }

    public static byte[] OpenLocal(string text)
    {
        var (_, lines) = Expect(text, EnvelopeKind.Local, LocalLineCount);
        try
        {
            return ProtectedData.Unprotect(Decode(lines[HeaderLineCount]), null, DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException ex)
        {
            throw new VaultException(
                "The vault cannot be unsealed by this Windows account on this machine, or it is damaged.", ex);
        }
    }

    public static string SealExport(ReadOnlySpan<byte> body, ReadOnlySpan<char> passphrase)
    {
        RequireStrongPassphrase(passphrase);
        var parameters = Parameters(
            (CipherParameter, AesGcm256),
            (IterationsParameter, Iterations.ToString(CultureInfo.InvariantCulture)),
            (KdfParameter, PassphraseKdf));
        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var key = DerivePassphraseKey(passphrase, salt, Iterations);
        try
        {
            var (nonce, tag, cipher) = Encrypt(key, body, Aad(ExportMode, parameters));
            return Join(Magic, ExportMode, parameters, Encode(salt), Encode(nonce), Encode(tag), Encode(cipher));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public static byte[] OpenExport(string text, ReadOnlySpan<char> passphrase)
    {
        var (header, lines) = Expect(text, EnvelopeKind.Export, SealedLineCount);
        var key = DerivePassphraseKey(passphrase, Decode(lines[3]), header.Iterations);
        try
        {
            return Decrypt(key, lines, Aad(ExportMode, header.Parameters));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public static string SealBackup(ReadOnlySpan<byte> body, ReadOnlySpan<char> passphrase, ReadOnlySpan<byte> keyFile)
    {
        RequireStrongPassphrase(passphrase);
        RequireKeyFile(keyFile);
        var parameters = Parameters(
            (CipherParameter, AesGcm256),
            (IterationsParameter, Iterations.ToString(CultureInfo.InvariantCulture)),
            (KdfParameter, KeyFileKdf),
            (KeyFileParameter, KeyFileIdentity.Compute(keyFile)));
        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var key = DeriveBackupKey(passphrase, keyFile, salt, Iterations);
        try
        {
            var (nonce, tag, cipher) = Encrypt(key, body, Aad(BackupMode, parameters));
            return Join(Magic, BackupMode, parameters, Encode(salt), Encode(nonce), Encode(tag), Encode(cipher));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public static string ReadBackupKeyFileId(string text) =>
        Expect(text, EnvelopeKind.Backup, SealedLineCount).Header.KeyFileId!;

    public static byte[] OpenBackup(string text, ReadOnlySpan<char> passphrase, ReadOnlySpan<byte> keyFile)
    {
        var (header, lines) = Expect(text, EnvelopeKind.Backup, SealedLineCount);
        RequireKeyFile(keyFile);
        if (!string.Equals(KeyFileIdentity.Compute(keyFile), header.KeyFileId, StringComparison.OrdinalIgnoreCase))
        {
            throw new VaultException($"Wrong keyfile. This backup needs the keyfile with id {header.KeyFileId}.");
        }

        var key = DeriveBackupKey(passphrase, keyFile, Decode(lines[3]), header.Iterations);
        try
        {
            return Decrypt(key, lines, Aad(BackupMode, header.Parameters));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public static byte[] OpenLegacyExport(string text, ReadOnlySpan<char> passphrase)
    {
        var lines = SplitLines(text);
        if (ReadHeader(lines).Kind != EnvelopeKind.LegacyExport)
        {
            throw new VaultException($"The file is not a {LegacyMagic} export.");
        }

        if (lines.Length < 5)
        {
            throw new VaultException("The file is damaged.");
        }

        var key = DerivePassphraseKey(passphrase, Decode(lines[1]), Iterations);
        try
        {
            return Decrypt(key, Decode(lines[2]), Decode(lines[3]), Decode(lines[4]), null);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public static void RequireStrongPassphrase(ReadOnlySpan<char> passphrase)
    {
        if (passphrase.Length < MinimumPassphraseLength)
        {
            throw new VaultException(
                $"The passphrase is too short. Use at least {MinimumPassphraseLength} characters.");
        }
    }

    private sealed record Header(EnvelopeKind Kind, string Parameters, int Iterations, string? KeyFileId);

    private static Header ReadHeader(string[] lines)
    {
        var unknown = new Header(EnvelopeKind.Unknown, string.Empty, 0, null);
        if (lines.Length == 0)
        {
            return unknown;
        }

        if (lines[0] == LegacyMagic)
        {
            return unknown with { Kind = EnvelopeKind.LegacyExport };
        }

        if (!lines[0].StartsWith(MagicPrefix, StringComparison.Ordinal)
            || !int.TryParse(lines[0].AsSpan(MagicPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var version)
            || version < MagicVersion)
        {
            return unknown;
        }

        var newer = unknown with { Kind = EnvelopeKind.Newer };
        if (version > MagicVersion)
        {
            return newer;
        }

        if (lines.Length < HeaderLineCount || ParseParameters(lines[2]) is not { } parameters)
        {
            return unknown;
        }

        var kind = lines[1] switch
        {
            LocalMode => EnvelopeKind.Local,
            ExportMode => EnvelopeKind.Export,
            BackupMode => EnvelopeKind.Backup,
            _ => EnvelopeKind.Newer,
        };
        var expected = kind switch
        {
            EnvelopeKind.Local => new Dictionary<string, string?> { [SealParameter] = DpapiUser },
            EnvelopeKind.Export => new Dictionary<string, string?>
            {
                [CipherParameter] = AesGcm256, [IterationsParameter] = null, [KdfParameter] = PassphraseKdf,
            },
            EnvelopeKind.Backup => new Dictionary<string, string?>
            {
                [CipherParameter] = AesGcm256, [IterationsParameter] = null, [KdfParameter] = KeyFileKdf,
                [KeyFileParameter] = null,
            },
            _ => null,
        };
        if (expected is null)
        {
            return newer;
        }

        foreach (var (name, value) in parameters)
        {
            if (!expected.TryGetValue(name, out var supported) || (supported is not null && supported != value))
            {
                return newer;
            }
        }

        if (expected.Keys.Any(name => !parameters.ContainsKey(name)))
        {
            return unknown;
        }

        var iterations = 0;
        if (parameters.TryGetValue(IterationsParameter, out var iterationText))
        {
            if (!int.TryParse(iterationText, NumberStyles.None, CultureInfo.InvariantCulture, out iterations))
            {
                return unknown;
            }

            if (iterations is < MinimumIterations or > MaximumIterations)
            {
                return newer;
            }
        }

        var keyFileId = parameters.GetValueOrDefault(KeyFileParameter);
        if (keyFileId is not null && (keyFileId.Length != KeyFileIdLength || !keyFileId.All(char.IsAsciiHexDigit)))
        {
            return unknown;
        }

        return new Header(kind, lines[2], iterations, keyFileId);
    }

    private static Dictionary<string, string>? ParseParameters(string line)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in line.Split(';'))
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0 || separator == pair.Length - 1 || !parameters.TryAdd(pair[..separator], pair[(separator + 1)..]))
            {
                return null;
            }
        }

        return parameters;
    }

    private static string Parameters(params (string Name, string Value)[] parameters) =>
        string.Join(';', parameters.OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => p.Name + "=" + p.Value));

    private static (Header Header, string[] Lines) Expect(string text, EnvelopeKind kind, int lineCount)
    {
        var lines = SplitLines(text);
        var header = ReadHeader(lines);
        if (header.Kind == EnvelopeKind.Newer)
        {
            throw new VaultException(NewerFormatMessage);
        }

        if (header.Kind != kind)
        {
            throw new VaultException($"The file is not a {Describe(kind)}.");
        }

        if (lines.Length < lineCount)
        {
            throw new VaultException("The file is damaged.");
        }

        return (header, lines);
    }

    private static void RequireKeyFile(ReadOnlySpan<byte> keyFile)
    {
        if (keyFile.Length != KeyFileLength)
        {
            throw new VaultException($"A keyfile holds exactly {KeyFileLength} bytes.");
        }
    }

    private static byte[] Aad(string mode, string parameters) =>
        Encoding.UTF8.GetBytes(Magic + "\n" + mode + "\n" + parameters);

    private static byte[] DerivePassphraseKey(ReadOnlySpan<char> passphrase, ReadOnlySpan<byte> salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, iterations, HashAlgorithmName.SHA256, KeyLength);

    private static byte[] DeriveBackupKey(ReadOnlySpan<char> passphrase, ReadOnlySpan<byte> keyFile, byte[] salt, int iterations)
    {
        var passphraseKey = DerivePassphraseKey(passphrase, salt, iterations);
        var material = new byte[passphraseKey.Length + keyFile.Length];
        try
        {
            passphraseKey.CopyTo(material, 0);
            keyFile.CopyTo(material.AsSpan(passphraseKey.Length));
            return HKDF.DeriveKey(HashAlgorithmName.SHA256, material, KeyLength, salt, BackupInfo);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passphraseKey);
            CryptographicOperations.ZeroMemory(material);
        }
    }

    private static (byte[] Nonce, byte[] Tag, byte[] Cipher) Encrypt(byte[] key, ReadOnlySpan<byte> body, byte[] aad)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        var tag = new byte[TagLength];
        var cipher = new byte[body.Length];
        using var aes = new AesGcm(key, TagLength);
        aes.Encrypt(nonce, body, cipher, tag, aad);
        return (nonce, tag, cipher);
    }

    private static byte[] Decrypt(byte[] key, string[] lines, byte[] aad) =>
        Decrypt(key, Decode(lines[4]), Decode(lines[5]), Decode(lines[6]), aad);

    private static byte[] Decrypt(byte[] key, byte[] nonce, byte[] tag, byte[] cipher, byte[]? aad)
    {
        if (nonce.Length != NonceLength || tag.Length != TagLength)
        {
            throw new VaultException("The file is damaged.");
        }

        var body = new byte[cipher.Length];
        try
        {
            using var aes = new AesGcm(key, TagLength);
            aes.Decrypt(nonce, cipher, tag, body, aad);
            return body;
        }
        catch (CryptographicException ex)
        {
            CryptographicOperations.ZeroMemory(body);
            throw new VaultException("Wrong passphrase or keyfile, or the file has been altered.", ex);
        }
    }

    private static string Describe(EnvelopeKind kind) => kind switch
    {
        EnvelopeKind.Local => "LocalVault vault",
        EnvelopeKind.Export => "LocalVault export",
        EnvelopeKind.Backup => "LocalVault backup",
        EnvelopeKind.LegacyExport => $"{LegacyMagic} export",
        _ => "known vault file",
    };

    private static string[] SplitLines(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray();

    private static string Join(params string[] lines) => string.Join('\n', lines) + "\n";

    private static string Encode(byte[] value) => Convert.ToBase64String(value);

    private static byte[] Decode(string value)
    {
        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException ex)
        {
            throw new VaultException("The file is damaged.", ex);
        }
    }
}
