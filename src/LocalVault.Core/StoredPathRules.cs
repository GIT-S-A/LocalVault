namespace LocalVault.Core;

public static class StoredPathRules
{
    public const string HomePrefix = "~/";

    private const string GitDirectory = ".git";
    private const string VaultFilePrefix = "secrets.local.";

    public static string Normalize(string storedPath)
    {
        ArgumentNullException.ThrowIfNull(storedPath);
        var normalized = storedPath.Replace('\\', '/');
        if (normalized.StartsWith(HomePrefix, StringComparison.Ordinal))
        {
            return HomePrefix + NormalizeRelative(storedPath[HomePrefix.Length..], storedPath);
        }

        return NormalizeRelative(storedPath, storedPath);
    }

    public static bool IsUnderHome(string storedPath)
    {
        ArgumentNullException.ThrowIfNull(storedPath);
        return storedPath.Replace('\\', '/').StartsWith(HomePrefix, StringComparison.Ordinal);
    }

    public static string HomeDirectory =>
        Environment.GetEnvironmentVariable("USERPROFILE") is { Length: > 0 } profile
            ? profile
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string Resolve(string baseDirectory, string storedPath)
    {
        var normalized = Normalize(storedPath);
        return IsUnderHome(normalized)
            ? Within(HomeDirectory, normalized[HomePrefix.Length..], storedPath)
            : Within(baseDirectory, normalized, storedPath);
    }

    public static string FromFullPath(string baseDirectory, string fullPath)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(baseDirectory), Path.GetFullPath(fullPath));
        return Normalize(relative);
    }

    public static string? HomeFormOf(string fullPath)
    {
        var home = Path.GetFullPath(HomeDirectory);
        var full = Path.GetFullPath(fullPath);
        var homeWithSeparator = Path.EndsInDirectorySeparator(home) ? home : home + Path.DirectorySeparatorChar;
        return full.StartsWith(homeWithSeparator, StringComparison.OrdinalIgnoreCase)
            ? Normalize(HomePrefix + Path.GetRelativePath(home, full))
            : null;
    }

    private static string NormalizeRelative(string relative, string storedPath)
    {
        var normalized = relative.Replace('\\', '/');
        var segments = normalized.Split('/');
        if (normalized.Length == 0
            || Path.IsPathRooted(relative)
            || normalized.Contains(':', StringComparison.Ordinal)
            || segments.Any(segment => segment is "" or "." or ".." || segment.EndsWith('.') || segment.EndsWith(' ')))
        {
            throw new VaultException(
                $"'{storedPath}' is not a valid stored path. Use a relative path without '..', or one under your profile starting with ~/.");
        }

        if (segments.Any(segment => segment.Equals(GitDirectory, StringComparison.OrdinalIgnoreCase))
            || segments[^1].StartsWith(VaultFilePrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new VaultException(
                $"'{storedPath}' is not a valid stored path: it points into .git or at a vault file.");
        }

        return normalized;
    }

    private static string Within(string baseDirectory, string relative, string storedPath)
    {
        var root = Path.GetFullPath(baseDirectory);
        var full = Path.GetFullPath(Path.Combine(root, relative));
        var rootWithSeparator = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new VaultException($"'{storedPath}' resolves outside '{root}'.");
        }

        return full;
    }
}
