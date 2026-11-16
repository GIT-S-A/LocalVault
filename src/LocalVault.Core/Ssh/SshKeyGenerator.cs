using System.Security.Cryptography;

namespace LocalVault.Core;

public sealed class GeneratedSshKey(byte[] privateKey, string publicKey) : IDisposable
{
    public byte[] PrivateKey => privateKey;

    public string PublicKey => publicKey;

    public void Dispose() => CryptographicOperations.ZeroMemory(privateKey);
}

public static class SshKeyGenerator
{
    public const int RsaBits = 4096;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    public static GeneratedSshKey Generate(SshTools tools, string folder, string type, string comment)
    {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(folder);
        if (!SshKeyInfo.Types.Contains(type))
        {
            throw new VaultException($"'{type}' is not a supported SSH key type: use {string.Join(", ", SshKeyInfo.Types)}.");
        }

        if (!Directory.Exists(folder))
        {
            PrivateFile.CreateFolder(folder);
        }

        var temp = Path.Combine(folder, ".new-" + Guid.NewGuid().ToString("N"));
        var publicPath = temp + SshKeyPath.PublicKeySuffix;
        try
        {
            List<string> arguments = ["-q", "-t", type, "-N", string.Empty, "-C", comment, "-f", temp];
            if (type == SshKeyInfo.Rsa)
            {
                arguments.AddRange(["-b", RsaBits.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
            }

            var result = ToolProcess.Run(tools.KeyGenPath, arguments, Timeout);
            if (result.ExitCode != 0 || !File.Exists(temp) || !File.Exists(publicPath))
            {
                throw new VaultException($"ssh-keygen failed (exit {result.ExitCode}): {result.Error.Trim()}");
            }

            var publicKey = SshKeyInfo.NormalizePublicKey(File.ReadAllText(publicPath));
            return new GeneratedSshKey(File.ReadAllBytes(temp), publicKey);
        }
        finally
        {
            File.Delete(temp);
            File.Delete(publicPath);
        }
    }
}
