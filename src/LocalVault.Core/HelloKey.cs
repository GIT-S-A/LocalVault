using System.Security.Cryptography;

namespace LocalVault.Core;

public enum HelloState
{
    Ready,
    None,
    Unavailable,
}

public interface IHelloKey
{
    HelloState State { get; }

    string? UnavailableReason { get; }

    string ProviderName { get; }

    bool IsHardware { get; }

    string KeyId { get; }

    RSAParameters PublicKey { get; }

    void Create(string message);

    byte[] Decrypt(ReadOnlySpan<byte> wrapped, string message);
}

public static class HelloKeys
{
    private static readonly Lazy<IHelloKey> Windows = new(() => new NgcHelloKey());

    public static IHelloKey Current => Override ?? Windows.Value;

    internal static IHelloKey? Override { get; set; }

    public static string ComputeKeyId(RSAParameters publicKey)
    {
        var modulus = publicKey.Modulus ?? throw new ArgumentException("Public key has no modulus.", nameof(publicKey));
        var exponent = publicKey.Exponent ?? throw new ArgumentException("Public key has no exponent.", nameof(publicKey));
        var blob = new byte[24 + exponent.Length + modulus.Length];
        BitConverter.TryWriteBytes(blob.AsSpan(0), 0x31415352);
        BitConverter.TryWriteBytes(blob.AsSpan(4), modulus.Length * 8);
        BitConverter.TryWriteBytes(blob.AsSpan(8), exponent.Length);
        BitConverter.TryWriteBytes(blob.AsSpan(12), modulus.Length);
        exponent.CopyTo(blob, 24);
        modulus.CopyTo(blob, 24 + exponent.Length);
        return Convert.ToHexStringLower(SHA256.HashData(blob));
    }
}

public class HelloException : VaultException
{
    public HelloException()
    {
    }

    public HelloException(string message)
        : base(message)
    {
    }

    public HelloException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class HelloCancelledException : HelloException
{
    public HelloCancelledException()
        : base("Windows Hello was cancelled.")
    {
    }

    public HelloCancelledException(string message)
        : base(message)
    {
    }

    public HelloCancelledException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
