namespace LocalVault.Core;

public static class StoredPathRules
{
    private const string GitDirectory = ".git";
    private const string VaultFilePrefix = "secrets.local.";

    public static string Normalize(string storedPath)
    {
        ArgumentNullException.ThrowIfNull(storedPath);
        var normalized = storedPath.Replace('\\', '/');
        var segments = normalized.Split('/');
        if (normalized.Length == 0
            || Path.IsPathRooted(storedPath)
            || normalized.Contains(':', StringComparison.Ordinal)
            || segments.Any(segment => segment is "" or "." or ".." || segment.EndsWith('.') || segment.EndsWith(' ')))
        {
            throw new VaultException(
                $"'{storedPath}' is not a valid stored path. Use a relative path without '..'.");
        }

        if (segments.Any(segment => segment.Equals(GitDirectory, StringComparison.OrdinalIgnoreCase))
            || segments[^1].StartsWith(VaultFilePrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new VaultException(
                $"'{storedPath}' is not a valid stored path: it points into .git or at a vault file.");
        }

        return normalized;
    }

    public static string Resolve(string baseDirectory, string storedPath)
    {
        var normalized = Normalize(storedPath);
        var root = Path.GetFullPath(baseDirectory);
        var full = Path.GetFullPath(Path.Combine(root, normalized));
        var rootWithSeparator = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new VaultException($"'{storedPath}' resolves outside '{root}'.");
        }

        return full;
    }

    public static string FromFullPath(string baseDirectory, string fullPath)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(baseDirectory), Path.GetFullPath(fullPath));
        return Normalize(relative);
    }
}
