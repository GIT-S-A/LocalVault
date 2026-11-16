namespace LocalVault.Core;

public sealed record RepositoryOrigin(string Url, string Folder)
{
    public static RepositoryOrigin? Of(VaultLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        if (location.GitRoot is not { } gitRoot || ReadOriginUrl(gitRoot) is not { Length: > 0 } url)
        {
            return null;
        }

        return new RepositoryOrigin(WithoutCredentials(url), Path.GetFileName(Path.TrimEndingDirectorySeparator(gitRoot)));
    }

    public static string WithoutCredentials(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        var schemeEnd = url.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd < 0)
        {
            return url;
        }

        var authorityStart = schemeEnd + 3;
        var authorityEnd = url.IndexOf('/', authorityStart);
        var authority = url[authorityStart..(authorityEnd < 0 ? url.Length : authorityEnd)];
        var at = authority.LastIndexOf('@');
        if (at < 0)
        {
            return url;
        }

        var isHttp = url.StartsWith("http", StringComparison.OrdinalIgnoreCase);
        var userName = authority[..at].Split(':')[0];
        var keptUser = isHttp || userName.Length == 0 ? string.Empty : userName + "@";
        return url[..authorityStart] + keptUser + url[(authorityStart + at + 1)..];
    }

    private static string? ReadOriginUrl(string gitRoot)
    {
        try
        {
            return Git.Run(gitRoot, "remote", "get-url", "origin") is { ExitCode: 0 } result ? result.Output.Trim() : null;
        }
        catch (TimeoutException)
        {
            return null;
        }
    }
}
