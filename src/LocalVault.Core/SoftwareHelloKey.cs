using System.Security.Cryptography;

namespace LocalVault.Core;

internal sealed class SoftwareHelloKey : IHelloKey, IDisposable
{
    private RSA? rsa;

    public string ProviderName => "LocalVault test key";

    public HelloState State => UnavailableReason is not null ? HelloState.Unavailable : rsa is null ? HelloState.None : HelloState.Ready;

    public string? UnavailableReason { get; set; }

    public bool IsHardware { get; set; } = true;

    public bool CancelNext { get; set; }

    public int Prompts { get; private set; }

    public List<string> Messages { get; } = [];

    public string KeyId => HelloKeys.ComputeKeyId(PublicKey);

    public RSAParameters PublicKey => (rsa ?? throw new HelloException("Windows Hello key missing.")).ExportParameters(false);

    public void Create(string message)
    {
        Prompt(message);
        rsa?.Dispose();
        rsa = RSA.Create(2048);
    }

    public byte[] Decrypt(ReadOnlySpan<byte> wrapped, string message)
    {
        var key = rsa ?? throw new HelloException("Windows Hello key missing.");
        Prompt(message);
        return key.Decrypt(wrapped.ToArray(), RSAEncryptionPadding.Pkcs1);
    }

    public void Delete()
    {
        rsa?.Dispose();
        rsa = null;
    }

    public void Dispose() => Delete();

    private void Prompt(string message)
    {
        if (UnavailableReason is not null)
        {
            throw new HelloException($"Windows Hello is not available: {UnavailableReason}.");
        }

        Prompts++;
        Messages.Add(message);
        if (CancelNext)
        {
            CancelNext = false;
            throw new HelloCancelledException();
        }
    }
}
