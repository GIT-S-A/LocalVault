namespace LocalVault.Engine;

public static class GitIgnore
{
    public static bool IsIgnored(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        GitResult? result;
        try
        {
            result = Git.Run(NearestExistingDirectory(path), "check-ignore", "--quiet", "--no-index", "--", path);
        }
        catch (TimeoutException)
        {
            throw new VaultException($"git did not finish within {Git.Timeout.TotalSeconds} s checking '{path}'.");
        }

        if (result is null)
        {
            throw new VaultException($"git could not be started to check that '{path}' is ignored.");
        }

        return result.ExitCode switch
        {
            0 => true,
            1 => false,
            _ => throw new VaultException(
                $"git could not check that '{path}' is ignored: {FirstLine(result.Error)}"),
        };
    }

    private static string NearestExistingDirectory(string path)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(path))!);
        while (!directory.Exists && directory.Parent is not null)
        {
            directory = directory.Parent;
        }

        return directory.FullName;
    }

    private static string FirstLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault()
        ?? "no message";
}
