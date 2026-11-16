namespace LocalVault.Core;

public enum VaultPathSource
{
    Explicit,
    Environment,
    GitRoot,
}

public sealed record VaultLocation(string Path, VaultPathSource Source, string? GitRoot)
{
    public const string DefaultFileName = "secrets.local.lv";
    public const string LegacyFileName = "secrets.local.clixml";
    public const string EnvironmentVariable = "LOCALVAULT_PATH";
    public const string SideFilePattern = "secrets.local.*";

    public string Directory => System.IO.Path.GetDirectoryName(Path)!;

    public string? LegacyPath =>
        string.Equals(System.IO.Path.GetFileName(Path), DefaultFileName, StringComparison.OrdinalIgnoreCase)
            ? System.IO.Path.Combine(Directory, LegacyFileName)
            : null;

    public string LockPath => Path + ".lock";

    public string PreviousGenerationPath => Path + ".bak";

    public static VaultLocation Resolve(string? explicitPath, string? environmentPath, string workingDirectory)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return FromPath(explicitPath, VaultPathSource.Explicit, workingDirectory);
        }

        if (!string.IsNullOrWhiteSpace(environmentPath))
        {
            return FromPath(environmentPath, VaultPathSource.Environment, workingDirectory);
        }

        var gitRoot = FindGitRoot(workingDirectory)
            ?? throw new VaultException(
                $"No vault path given and '{workingDirectory}' is not inside a git repository. " +
                $"Pass -Path or set ${EnvironmentVariable}.");
        return new VaultLocation(System.IO.Path.Combine(gitRoot, DefaultFileName), VaultPathSource.GitRoot, gitRoot);
    }

    public static string? FindGitRoot(string start)
    {
        for (var directory = new DirectoryInfo(System.IO.Path.GetFullPath(start));
             directory is not null;
             directory = directory.Parent)
        {
            var marker = System.IO.Path.Combine(directory.FullName, ".git");
            if (System.IO.Directory.Exists(marker) || File.Exists(marker))
            {
                return directory.FullName;
            }
        }

        return null;
    }

    public IReadOnlyList<string> FindOtherVaultFiles()
    {
        if (GitRoot is null)
        {
            return [];
        }

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };
        var own = System.IO.Path.GetFullPath(Directory);
        return System.IO.Directory.EnumerateFiles(GitRoot, SideFilePattern, options)
            .Where(file => !IsUnderGitDirectory(file))
            .Where(file => !string.Equals(
                System.IO.Path.GetDirectoryName(file), own, StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void EnsureIgnoredByGit()
    {
        if (GitRoot is not null && !GitIgnore.IsIgnored(Path))
        {
            throw new VaultException(
                $"Not creating '{Path}': git does not ignore it. Add '{SideFilePattern}' to .gitignore first.");
        }
    }

    public void EnsureCanCreate(bool personConfirmed)
    {
        var others = Source == VaultPathSource.GitRoot ? FindOtherVaultFiles() : [];
        if (others.Count > 0)
        {
            throw new VaultException(
                $"Not creating '{Path}': this repository already has vault files elsewhere: " +
                string.Join(", ", others) + ". Pass -Path to use one of them, or to create another vault on purpose.");
        }

        if (Source == VaultPathSource.GitRoot && !personConfirmed)
        {
            throw new VaultException(
                $"There is no vault at '{Path}'. Pass -Path to create one, or confirm when asked.");
        }
    }

    private static VaultLocation FromPath(string path, VaultPathSource source, string workingDirectory)
    {
        var full = System.IO.Path.GetFullPath(path, workingDirectory);
        return new VaultLocation(full, source, FindGitRoot(System.IO.Path.GetDirectoryName(full)!));
    }

    private static bool IsUnderGitDirectory(string file) =>
        file.Contains(
            System.IO.Path.DirectorySeparatorChar + ".git" + System.IO.Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
}
