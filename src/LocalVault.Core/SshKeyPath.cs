namespace LocalVault.Core;

public static class SshKeyPath
{
    public const string SshFolder = ".ssh";
    public const string MarkerFileName = ".localvault";
    public const string MarkerLockFileName = ".localvault.lock";
    public const string ConfigFileName = "lv.config";
    public const string PublicKeySuffix = ".pub";

    private static readonly string[] ReservedNames = [MarkerFileName, MarkerLockFileName, ConfigFileName];

    public static string For(string repository, EntryName name)
    {
        ArgumentNullException.ThrowIfNull(repository);
        return Validate($"{StoredPathRules.HomePrefix}{SshFolder}/{repository}/{FileNameOf(name)}");
    }

    public static string FileNameOf(EntryName name)
    {
        return name.FullName.Replace('/', '.').Replace(':', '_');
    }

    public static string Validate(string storedPath)
    {
        var normalized = StoredPathRules.Normalize(storedPath);
        var segments = normalized.Split('/');
        var file = segments[^1];
        if (segments.Length != 4
            || segments[0] != "~"
            || !segments[1].Equals(SshFolder, StringComparison.OrdinalIgnoreCase)
            || ReservedNames.Contains(file, StringComparer.OrdinalIgnoreCase)
            || file.EndsWith(PublicKeySuffix, StringComparison.OrdinalIgnoreCase)
            || file.EndsWith(VaultEntry.OldKeySuffix, StringComparison.OrdinalIgnoreCase))
        {
            throw new VaultException(
                $"'{storedPath}' is not a valid SSH key path: it must be ~/.ssh/<folder>/<file>, and the file name can't be {string.Join(", ", ReservedNames)} or end in {PublicKeySuffix} or {VaultEntry.OldKeySuffix}.");
        }

        return normalized;
    }

    public static string RepositoryNameOf(string? originUrl, string gitRootFolder)
    {
        ArgumentNullException.ThrowIfNull(gitRootFolder);
        if (string.IsNullOrWhiteSpace(originUrl))
        {
            return gitRootFolder;
        }

        var trimmed = originUrl.Trim().TrimEnd('/');
        var last = trimmed[(trimmed.LastIndexOfAny(['/', ':', '\\']) + 1)..];
        if (last.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            last = last[..^4];
        }

        return last.Length == 0 ? gitRootFolder : last;
    }
}
