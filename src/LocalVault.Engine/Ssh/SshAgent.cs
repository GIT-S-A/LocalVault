namespace LocalVault.Engine;

public sealed record SshAgentKey(string PublicKey, string Type, string Fingerprint, string Comment);

public static class SshAgent
{
    public static IReadOnlyList<SshAgentKey> ListKeys(SshTools tools, Action tick, Func<bool> cancelled)
    {
        ArgumentNullException.ThrowIfNull(tools);
        var result = ToolProcess.Run(tools.AddPath, ["-L"], tick, cancelled);
        return result.ExitCode == 0 ? Parse(result.Output) : [];
    }

    public static IReadOnlyList<SshAgentKey> Parse(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var keys = new List<SshAgentKey>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var fields = line.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 2)
            {
                continue;
            }

            try
            {
                var publicKey = SshKeyInfo.NormalizePublicKey($"{fields[0]} {fields[1]}");
                keys.Add(new SshAgentKey(publicKey, SshKeyInfo.TypeOf(publicKey), SshKeyInfo.FingerprintOf(publicKey), fields.Length > 2 ? fields[2].Trim() : string.Empty));
            }
            catch (VaultException)
            {
                continue;
            }
        }

        return keys;
    }
}
