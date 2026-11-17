using System.Security.Cryptography;

namespace LocalVault.Engine;

public static class KeyFileIdentity
{
    public static string Compute(ReadOnlySpan<byte> keyFile) =>
        Convert.ToHexStringLower(SHA256.HashData(keyFile));
}
