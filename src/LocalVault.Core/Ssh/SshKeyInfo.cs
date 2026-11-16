using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace LocalVault.Core;

public sealed partial record SshKeyInfo(string Type, string PublicKey, IReadOnlyList<string> Hosts, bool Agent = false, DateTimeOffset? ReplacedAt = null)
{
    public const string Ed25519 = "ed25519";
    public const string Rsa = "rsa";
    public const string Ecdsa = "ecdsa";

    public static IReadOnlyList<string> Types { get; } = [Ed25519, Rsa, Ecdsa];

    public static bool IsHost(string host) => host is not null && HostPattern().IsMatch(host);

    public static string NormalizeHost(string host)
    {
        ArgumentNullException.ThrowIfNull(host);
        var trimmed = host.Trim();
        return IsHost(trimmed)
            ? trimmed
            : throw new VaultException($"'{host}' is not a host: give [user@]host[:port], e.g. deploy@srv1.example.com.");
    }

    [GeneratedRegex(@"^(?:[A-Za-z0-9_][A-Za-z0-9._-]*@)?(?:[A-Za-z0-9](?:[A-Za-z0-9.-]*[A-Za-z0-9])?|\[[0-9A-Fa-f:.]+\])(?::[0-9]{1,5})?$")]
    private static partial Regex HostPattern();

    public string Fingerprint => FingerprintOf(PublicKey);

    public static string FingerprintOf(string publicKeyLine)
    {
        byte[] blob;
        try
        {
            blob = Convert.FromBase64String(Field(publicKeyLine, 1));
        }
        catch (FormatException ex)
        {
            throw new VaultException("Not an OpenSSH public key line: its key is not base64.", ex);
        }

        return "SHA256:" + Convert.ToBase64String(SHA256.HashData(blob)).TrimEnd('=');
    }

    public static string TypeOf(string publicKeyLine)
    {
        var algorithm = Field(publicKeyLine, 0);
        return algorithm switch
        {
            "ssh-ed25519" => Ed25519,
            "ssh-rsa" => Rsa,
            _ when algorithm.StartsWith("ecdsa-sha2-", StringComparison.Ordinal) => Ecdsa,
            _ => throw new VaultException($"'{algorithm}' is not a supported SSH key type: use {string.Join(", ", Types)}."),
        };
    }

    public static string NormalizePublicKey(string publicKeyLine)
    {
        ArgumentNullException.ThrowIfNull(publicKeyLine);
        var line = publicKeyLine.Trim();
        _ = TypeOf(line);
        _ = FingerprintOf(line);
        return line;
    }

    private static string Field(string publicKeyLine, int index)
    {
        ArgumentNullException.ThrowIfNull(publicKeyLine);
        var fields = publicKeyLine.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 2)
        {
            throw new VaultException("Not an OpenSSH public key line ('<type> <base64> [comment]').");
        }

        return fields[index];
    }
}
