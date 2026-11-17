using System.Text.Json.Nodes;

namespace LocalVault.Engine;

public sealed class BackupSettings
{
    private const string IdField = "id";
    private const string DestinationField = "destination";
    private const string PassphraseField = "passphrase";
    private const string KeyFileField = "keyfile";
    private const string KeyFileIdField = "keyfileId";

    internal BackupSettings(JsonObject node)
    {
        Node = node;
    }

    public string? Id => Node[IdField]?.GetValue<string>();

    public string Destination => Node[DestinationField]?.GetValue<string>()
        ?? throw new VaultException("Backup settings have no destination.");

    internal JsonObject Node { get; }

    public static BackupSettings Create(
        string id, string destination, ReadOnlySpan<char> passphrase, ReadOnlySpan<byte> keyFile)
    {
        Envelope.RequireStrongPassphrase(passphrase);
        if (keyFile.Length != Envelope.KeyFileLength)
        {
            throw new VaultException($"A backup keyfile holds exactly {Envelope.KeyFileLength} bytes, not {keyFile.Length}.");
        }

        var passphraseBytes = new byte[System.Text.Encoding.UTF8.GetByteCount(passphrase)];
        System.Text.Encoding.UTF8.GetBytes(passphrase, passphraseBytes);
        try
        {
            var node = new JsonObject
            {
                [IdField] = id,
                [DestinationField] = destination,
                [PassphraseField] = Convert.ToBase64String(passphraseBytes),
                [KeyFileField] = Convert.ToBase64String(keyFile),
                [KeyFileIdField] = KeyFileIdentity.Compute(keyFile),
            };

            return new BackupSettings(node);
        }
        finally
        {
            Array.Clear(passphraseBytes);
        }
    }

    public char[] GetPassphrase()
    {
        var bytes = Convert.FromBase64String(Node[PassphraseField]?.GetValue<string>()
            ?? throw new VaultException("Backup settings have no passphrase."));
        try
        {
            return System.Text.Encoding.UTF8.GetChars(bytes);
        }
        finally
        {
            Array.Clear(bytes);
        }
    }

    public byte[] GetKeyFile() => Convert.FromBase64String(Node[KeyFileField]?.GetValue<string>()
        ?? throw new VaultException("Backup settings have no keyfile."));
}
