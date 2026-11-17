using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace LocalVault.Engine;

public sealed partial class LeftoverBackups
{
    public const string RootFolder = "LocalVault";
    private const int HashCharacters = 8;

    private LeftoverBackups(IReadOnlyList<string> files, IReadOnlyList<string> folders, bool hasSettings, string? unresolvedDestination)
    {
        Files = files;
        Folders = folders;
        HasSettings = hasSettings;
        UnresolvedDestination = unresolvedDestination;
    }

    public IReadOnlyList<string> Files { get; }

    public IReadOnlyList<string> Folders { get; }

    public bool HasSettings { get; }

    public string? UnresolvedDestination { get; }

    public bool Any => Files.Count > 0 || HasSettings;

    public static LeftoverBackups Find(VaultLocation location, BackupSettings? settings, string machineName, string? oneDrive)
    {
        ArgumentNullException.ThrowIfNull(location);
        var files = new List<string>();
        var folders = new List<string>();
        string? unresolved = null;
        if (settings is not null)
        {
            if (TryResolve(settings.Destination) is { } folder)
            {
                folders.Add(folder);
                files.AddRange(Own(folder, settings.Id, anyId: false));
            }
            else
            {
                unresolved = settings.Destination;
            }
        }

        if (DefaultFolder(location, machineName, oneDrive) is { } defaultFolder
            && !folders.Contains(defaultFolder, StringComparer.OrdinalIgnoreCase))
        {
            folders.Add(defaultFolder);
            files.AddRange(Own(defaultFolder, null, anyId: true));
        }

        return new LeftoverBackups(files, folders.Where(Directory.Exists).ToList(), settings is not null, unresolved);
    }

    public void Delete()
    {
        foreach (var file in Files)
        {
            File.Delete(file);
        }

        foreach (var folder in Folders)
        {
            if (RemoveIfEmpty(folder) && Path.GetDirectoryName(folder) is { } parent
                && string.Equals(Path.GetFileName(parent), RootFolder, StringComparison.OrdinalIgnoreCase))
            {
                RemoveIfEmpty(parent);
            }
        }
    }

    public static string FolderName(VaultLocation location, string machineName)
    {
        ArgumentNullException.ThrowIfNull(location);
        var project = Path.GetFileName(location.GitRoot ?? location.Directory);
        var hash = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(location.Path).ToLowerInvariant())))[..HashCharacters];
        return $"{project}-{hash}-{machineName}";
    }

    public static string? DefaultFolder(VaultLocation location, string machineName, string? oneDrive) =>
        string.IsNullOrEmpty(oneDrive) ? null : Path.Combine(oneDrive, RootFolder, FolderName(location, machineName));

    private static string? TryResolve(string destination)
    {
        var expanded = Environment.ExpandEnvironmentVariables(destination);
        return expanded.Contains('%', StringComparison.Ordinal) || !Path.IsPathFullyQualified(expanded)
            ? null
            : Path.GetFullPath(expanded);
    }

    private static IEnumerable<string> Own(string folder, string? id, bool anyId) =>
        Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder)
                .Select(path => (Path: path, Match: BackupName().Match(Path.GetFileName(path))))
                .Where(file => file.Match.Success
                    && (anyId || (file.Match.Groups["id"].Success ? file.Match.Groups["id"].Value : null) == id))
                .Select(file => file.Path)
            : [];

    private static bool RemoveIfEmpty(string folder)
    {
        if (!Directory.Exists(folder) || Directory.EnumerateFileSystemEntries(folder).Any())
        {
            return false;
        }

        Directory.Delete(folder);
        return true;
    }

    [GeneratedRegex("^[0-9]{8}T[0-9]{9}Z(?:-(?<id>[0-9a-f]{16}))?\\.lvbak$", RegexOptions.CultureInvariant)]
    private static partial Regex BackupName();
}
