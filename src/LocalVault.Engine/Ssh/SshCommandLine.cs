namespace LocalVault.Engine;

public sealed record SshDestination(string? User, string Host, string? Port)
{
    public static SshDestination Parse(string host)
    {
        var normalized = SshKeyInfo.NormalizeHost(host);
        var at = normalized.IndexOf('@', StringComparison.Ordinal);
        var user = at >= 0 ? normalized[..at] : null;
        var rest = normalized[(at + 1)..];
        if (rest.StartsWith('['))
        {
            var close = rest.IndexOf(']', StringComparison.Ordinal);
            return new SshDestination(user, rest[1..close], close + 1 < rest.Length ? rest[(close + 2)..] : null);
        }

        var colon = rest.LastIndexOf(':');
        return colon < 0 ? new SshDestination(user, rest, null) : new SshDestination(user, rest[..colon], rest[(colon + 1)..]);
    }

    public IReadOnlyList<string> Arguments =>
        Port is null ? [Target] : ["-p", Port, Target];

    private string Target => User is null ? Host : $"{User}@{Host}";
}

public static class SshCommandLine
{
    public static IReadOnlyList<string> Identity(VaultEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        List<string> arguments = ["-i", SshKeyFiles.KeyFileOf(entry)];
        if (OldKeyFileOf(entry) is { } old)
        {
            arguments.AddRange(["-i", old]);
        }

        arguments.AddRange(["-o", "IdentitiesOnly=yes"]);
        return arguments;
    }

    public static IReadOnlyList<string> Ssh(VaultEntry entry, string host, IEnumerable<string> extra)
    {
        ArgumentNullException.ThrowIfNull(extra);
        return [.. Identity(entry), .. SshDestination.Parse(host).Arguments, .. extra];
    }

    public static IReadOnlyList<string> Scp(VaultEntry entry, IEnumerable<string> extra)
    {
        ArgumentNullException.ThrowIfNull(extra);
        return [.. Identity(entry), .. extra];
    }

    // -F none: an Include of lv.config would add the old key as an identity and hide a host that refuses the new one.
    public static IReadOnlyList<string> Probe(VaultEntry entry, string host)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return
        [
            "-F", "none", "-i", SshKeyFiles.KeyFileOf(entry), "-o", "IdentitiesOnly=yes", "-o", "BatchMode=yes", "-o", "ConnectTimeout=5",
            .. SshDestination.Parse(host).Arguments, "exit",
        ];
    }

    public static string? OldKeyFileOf(VaultEntry entry) =>
        OldKeySuffixOf(entry) is { } suffix ? SshKeyFiles.TargetOf(entry) + suffix : null;

    public static string? OldKeySuffixOf(VaultEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var previous = entry.GetPrevious();
        if (previous is null)
        {
            return null;
        }

        try
        {
            return SshKeyFiles.IsPrivateKey(previous) ? VaultEntry.OldKeySuffix : VaultEntry.OldKeySuffix + SshKeyPath.PublicKeySuffix;
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(previous);
        }
    }
}
