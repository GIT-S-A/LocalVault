using System.Security.Cryptography;
using System.Text;

namespace LocalVault.Engine;

public static class SshKeyReset
{
    public static bool IsResetKey(VaultEntry declaration, string publicKey)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        return declaration.ResetKey is { } reset
            && string.Equals(SshKeyInfo.FingerprintOf(reset.PublicKey), SshKeyInfo.FingerprintOf(publicKey), StringComparison.Ordinal);
    }

    public static byte[]? PreviousOf(VaultEntry declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        if (declaration.ResetKey is not { } reset)
        {
            return null;
        }

        if (reset.Agent)
        {
            return Encoding.UTF8.GetBytes(reset.PublicKey + "\n");
        }

        var target = SshKeyFiles.TargetOf(declaration);
        if (!File.Exists(target))
        {
            return null;
        }

        var key = File.ReadAllBytes(target);
        try
        {
            if (SshKeyFiles.IsPrivateKey(key) && IsResetKey(declaration, OpenSshPrivateKey.PublicKeyOf(key)))
            {
                return key;
            }
        }
        catch (VaultException)
        {
        }

        CryptographicOperations.ZeroMemory(key);
        return null;
    }

    public static VaultEntry AsReplacement(VaultEntry declaration, VaultEntry value, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        ArgumentNullException.ThrowIfNull(value);
        if (value.Ssh is not { } ssh || IsResetKey(declaration, ssh.PublicKey) || PreviousOf(declaration) is not { } previous)
        {
            return value;
        }

        try
        {
            return value.WithPreviousKey(previous, now);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(previous);
        }
    }
}
