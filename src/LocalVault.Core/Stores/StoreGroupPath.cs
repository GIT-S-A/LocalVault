namespace LocalVault.Core;

public static class StoreGroupPath
{
    public const string Root = "LocalVault";

    public static IReadOnlyList<string> For(VaultLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        var segments = new List<string> { Root };
        if (location.GitRoot is { } gitRoot)
        {
            segments.Add(Path.GetFileName(Path.TrimEndingDirectorySeparator(gitRoot)));
            var relative = Path.GetRelativePath(gitRoot, location.Directory);
            if (relative != ".")
            {
                segments.AddRange(relative.Split(
                    [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries));
            }
        }
        else
        {
            segments.Add(Path.GetFileName(Path.TrimEndingDirectorySeparator(location.Directory)));
        }

        var fileName = Path.GetFileName(location.Path);
        if (!string.Equals(fileName, VaultLocation.DefaultFileName, StringComparison.OrdinalIgnoreCase))
        {
            segments.Add(fileName);
        }

        return segments;
    }

    public static IReadOnlyList<string>? ProjectOf(VaultLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        return location.GitRoot is { } gitRoot
            ? [Root, Path.GetFileName(Path.TrimEndingDirectorySeparator(gitRoot))]
            : null;
    }

    public static string Display(IReadOnlyList<string> segments) => string.Join('/', segments);
}
