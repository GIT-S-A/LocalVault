using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LocalVault.Engine;

public static class HelloSeal
{
    public const string Pad = "pkcs1";
    public const string MissingKey = "Windows Hello key missing";
    public const string ChangedKey = "Windows Hello key changed (PIN or Hello reset, other PC)";

    private const byte BlobVersion = 1;
    private const int PointLength = 65;
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private const int ScalarLength = 32;
    private static readonly byte[] Info = Encoding.ASCII.GetBytes("LOCALVAULT hello");

    public static VaultEntry Seal(VaultBody body, VaultEntry plain, IHelloKey key, string createMessage)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(plain);
        ArgumentNullException.ThrowIfNull(key);
        var payload = Encoding.UTF8.GetBytes(plain.SealablePayload().ToJsonString());
        try
        {
            var vaultKey = EnsureVaultKey(body, key, createMessage);
            using var vaultPublic = ECDiffieHellman.Create();
            vaultPublic.ImportSubjectPublicKeyInfo(vaultKey.PublicKey, out _);
            using var ephemeral = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            var point = EncodePoint(ephemeral.ExportParameters(false).Q);
            var blob = new byte[1 + PointLength + NonceLength + TagLength + payload.Length];
            blob[0] = BlobVersion;
            point.CopyTo(blob, 1);
            var nonce = blob.AsSpan(1 + PointLength, NonceLength);
            RandomNumberGenerator.Fill(nonce);
            var aesKey = DeriveKey(ephemeral, vaultPublic.PublicKey, vaultKey.KeyId);
            try
            {
                using var aes = new AesGcm(aesKey, TagLength);
                aes.Encrypt(
                    nonce,
                    payload,
                    blob.AsSpan(1 + PointLength + NonceLength + TagLength),
                    blob.AsSpan(1 + PointLength + NonceLength, TagLength),
                    AssociatedData(plain.Name, plain.Kind, vaultKey.KeyId));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(aesKey);
            }

            return plain.ToSealed(blob, vaultKey.Fingerprint);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    public static string? MissingReason(VaultBody body, IHelloKey key)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(key);
        return key.State switch
        {
            HelloState.Unavailable => $"Windows Hello is not available: {key.UnavailableReason}",
            HelloState.None => MissingKey,
            _ => body.HelloKey is { } vaultKey && string.Equals(vaultKey.KeyId, key.KeyId, StringComparison.OrdinalIgnoreCase)
                ? null
                : body.HelloKey is null ? MissingKey : ChangedKey,
        };
    }

    public static string? MissingReason(VaultBody body, VaultEntry entry, IHelloKey key)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!entry.IsHello)
        {
            return null;
        }

        return MissingReason(body, key)
            ?? (string.Equals(entry.HelloVaultKey, body.HelloKey!.Fingerprint, StringComparison.OrdinalIgnoreCase) ? null : ChangedKey);
    }

    public static HelloSession Open(VaultBody body, IHelloKey key, string message)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(key);
        if (MissingReason(body, key) is { } reason)
        {
            throw new HelloException(reason + ".");
        }

        var vaultKey = body.HelloKey!;
        byte[] scalar;
        try
        {
            scalar = key.Decrypt(vaultKey.Wrapped, message);
        }
        catch (HelloException ex) when (ex is not HelloCancelledException && key.State == HelloState.Ready)
        {
            throw new HelloException(ChangedKey + ".", ex);
        }
        catch (CryptographicException ex)
        {
            throw new HelloException(ChangedKey + ".", ex);
        }

        try
        {
            if (scalar.Length != ScalarLength)
            {
                throw new HelloException(ChangedKey + ".");
            }

            using var vaultPublic = ECDiffieHellman.Create();
            vaultPublic.ImportSubjectPublicKeyInfo(vaultKey.PublicKey, out _);
            var parameters = vaultPublic.ExportParameters(false);
            parameters.D = scalar;
            var vaultPrivate = ECDiffieHellman.Create();
            try
            {
                vaultPrivate.ImportParameters(parameters);
            }
            catch (CryptographicException ex)
            {
                vaultPrivate.Dispose();
                throw new HelloException(ChangedKey + ".", ex);
            }

            return new HelloSession(vaultPrivate, vaultKey.KeyId);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(scalar);
        }
    }

    private static HelloVaultKey EnsureVaultKey(VaultBody body, IHelloKey key, string createMessage)
    {
        switch (key.State)
        {
            case HelloState.Unavailable:
                throw new HelloException($"Windows Hello is not available: {key.UnavailableReason}.");
            case HelloState.None:
                key.Create(createMessage);
                break;
        }

        var keyId = key.KeyId;
        if (body.HelloKey is { } existing && string.Equals(existing.KeyId, keyId, StringComparison.OrdinalIgnoreCase))
        {
            return existing;
        }

        using var pair = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var parameters = pair.ExportParameters(true);
        try
        {
            using var rsa = RSA.Create(key.PublicKey);
            var created = new HelloVaultKey(keyId, pair.ExportSubjectPublicKeyInfo(), rsa.Encrypt(parameters.D!, RSAEncryptionPadding.Pkcs1), Pad);
            body.HelloKey = created;
            return created;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(parameters.D);
        }
    }

    internal static byte[] DeriveKey(ECDiffieHellman own, ECDiffieHellmanPublicKey other, string keyId)
    {
        var shared = own.DeriveRawSecretAgreement(other);
        try
        {
            return HKDF.DeriveKey(HashAlgorithmName.SHA256, shared, 32, Convert.FromHexString(keyId), Info);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(shared);
        }
    }

    internal static byte[] AssociatedData(EntryName name, string kind, string keyId) =>
        Encoding.UTF8.GetBytes($"{name.FullName}\0{kind}\0{keyId.ToLowerInvariant()}");

    private static byte[] EncodePoint(ECPoint point)
    {
        var encoded = new byte[PointLength];
        encoded[0] = 4;
        point.X!.CopyTo(encoded, 1);
        point.Y!.CopyTo(encoded, 33);
        return encoded;
    }

    public sealed class HelloSession : IDisposable
    {
        private readonly ECDiffieHellman vaultPrivate;
        private readonly string keyId;

        internal HelloSession(ECDiffieHellman vaultPrivate, string keyId)
        {
            this.vaultPrivate = vaultPrivate;
            this.keyId = keyId;
        }

        public VaultEntry Unseal(VaultEntry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);
            if (!entry.IsHello)
            {
                return entry;
            }

            var blob = entry.GetSealedValue();
            if (blob.Length < 1 + PointLength + NonceLength + TagLength || blob[0] != BlobVersion || blob[1] != 4)
            {
                throw new HelloException($"'{entry.Name}' has a Windows Hello value that is damaged.");
            }

            var plain = new byte[blob.Length - 1 - PointLength - NonceLength - TagLength];
            try
            {
                using var ephemeral = ECDiffieHellman.Create(new ECParameters
                {
                    Curve = ECCurve.NamedCurves.nistP256,
                    Q = new ECPoint { X = blob[2..34], Y = blob[34..66] },
                });
                var aesKey = DeriveKey(vaultPrivate, ephemeral.PublicKey, keyId);
                try
                {
                    using var aes = new AesGcm(aesKey, TagLength);
                    aes.Decrypt(
                        blob.AsSpan(1 + PointLength, NonceLength),
                        blob.AsSpan(1 + PointLength + NonceLength + TagLength),
                        blob.AsSpan(1 + PointLength + NonceLength, TagLength),
                        plain,
                        AssociatedData(entry.Name, entry.HelloInnerKind!, keyId));
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(aesKey);
                }

                return entry.ToUnsealed(JsonNode.Parse(plain) as JsonObject
                    ?? throw new HelloException($"'{entry.Name}' has a Windows Hello value that is damaged."));
            }
            catch (CryptographicException ex)
            {
                throw new HelloException($"'{entry.Name}' can't be unsealed: {ChangedKey}.", ex);
            }
            catch (JsonException ex)
            {
                throw new HelloException($"'{entry.Name}' has a Windows Hello value that is damaged.", ex);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
            }
        }

        public void Dispose() => vaultPrivate.Dispose();
    }
}
