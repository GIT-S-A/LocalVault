namespace LocalVault.Engine;

public enum SshHostCheck
{
    Accepted,
    Failed,
    UnknownHostKey,
}

public static class SshKeyRetirement
{
    private const string HostKeyFailure = "Host key verification failed";

    public static SshHostCheck Check(VaultEntry entry, string host, Func<IReadOnlyList<string>, ToolResult> run)
    {
        ArgumentNullException.ThrowIfNull(run);
        ToolResult result;
        try
        {
            result = run(SshCommandLine.Probe(entry, host));
        }
        catch (VaultException)
        {
            return SshHostCheck.Failed;
        }

        return result.ExitCode == 0 ? SshHostCheck.Accepted
            : result.Error.Contains(HostKeyFailure, StringComparison.OrdinalIgnoreCase) ? SshHostCheck.UnknownHostKey
            : SshHostCheck.Failed;
    }
}
