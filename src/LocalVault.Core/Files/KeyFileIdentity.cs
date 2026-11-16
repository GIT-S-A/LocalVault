using System.Security.Cryptography;

namespace LocalVault.Core;

public static class KeyFileIdentity
{
    public static string Compute(ReadOnlySpan<byte> keyFile) =>
        Convert.ToHexStringLower(SHA256.HashData(keyFile));
}
