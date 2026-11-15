using System.Buffers.Binary;
using System.Text;

namespace LocalVault.Core;

public static class OpenSshPrivateKey
{
    public const string Armor = "-----BEGIN OPENSSH PRIVATE KEY-----";
    private const string End = "-----END OPENSSH PRIVATE KEY-----";
    private static readonly byte[] Magic = "openssh-key-v1\0"u8.ToArray();

    // Layout from OpenSSH PROTOCOL.key: magic, cipher, kdf, kdf options, key count, then the public key blobs.
    // The public blobs are outside the encrypted section, so a passphrase does not hide them.
    public static string PublicKeyOf(ReadOnlySpan<byte> file)
    {
        var text = Encoding.ASCII.GetString(file).ReplaceLineEndings("\n").Trim();
        var begin = text.IndexOf(Armor, StringComparison.Ordinal);
        var end = text.IndexOf(End, StringComparison.Ordinal);
        if (begin < 0 || end < begin)
        {
            throw NotOpenSsh();
        }

        byte[] body;
        try
        {
            body = Convert.FromBase64String(text[(begin + Armor.Length)..end].Replace("\n", string.Empty, StringComparison.Ordinal).Trim());
        }
        catch (FormatException ex)
        {
            throw new VaultException("Not an OpenSSH private key: its body is not base64.", ex);
        }

        if (!body.AsSpan().StartsWith(Magic))
        {
            throw NotOpenSsh();
        }

        ReadOnlySpan<byte> reader = body.AsSpan(Magic.Length);
        _ = ReadString(ref reader);
        _ = ReadString(ref reader);
        _ = ReadString(ref reader);
        if (reader.Length < sizeof(uint) || BinaryPrimitives.ReadUInt32BigEndian(reader) != 1)
        {
            throw new VaultException("Not an OpenSSH private key with exactly one key.");
        }

        reader = reader[sizeof(uint)..];
        var blob = ReadString(ref reader);
        var typeBlob = blob;
        var type = Encoding.ASCII.GetString(ReadString(ref typeBlob));
        return SshKeyInfo.NormalizePublicKey($"{type} {Convert.ToBase64String(blob)}");
    }

    private static ReadOnlySpan<byte> ReadString(ref ReadOnlySpan<byte> reader)
    {
        if (reader.Length < sizeof(uint))
        {
            throw NotOpenSsh();
        }

        var length = BinaryPrimitives.ReadUInt32BigEndian(reader);
        if (length > reader.Length - sizeof(uint))
        {
            throw NotOpenSsh();
        }

        var value = reader.Slice(sizeof(uint), (int)length);
        reader = reader[(sizeof(uint) + (int)length)..];
        return value;
    }

    private static VaultException NotOpenSsh() => new("Not an OpenSSH private key (expected '-----BEGIN OPENSSH PRIVATE KEY-----').");
}
