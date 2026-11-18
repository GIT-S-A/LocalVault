using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace LocalVault.Engine;

public sealed record DotNetProject(string Path, string Name, string Id);

public static partial class DotNetProjects
{
    public const string IdProperty = "UserSecretsId";
    public const string PropsFileName = "Directory.Build.props";
    private const string SolutionExtension = ".sln";
    private const string XmlSolutionExtension = ".slnx";
    private const string ProjectExtension = ".csproj";

    public static IReadOnlyList<DotNetProject> Find(string gitRoot)
    {
        ArgumentNullException.ThrowIfNull(gitRoot);
        var root = System.IO.Path.GetFullPath(gitRoot);
        var solutions = Files(root, SolutionExtension).Concat(Files(root, XmlSolutionExtension)).ToList();
        var projects = solutions.Count > 0
            ? solutions.SelectMany(ProjectsOf).Where(path => IsInside(root, path) && File.Exists(path))
            : Files(root, ProjectExtension);
        return projects
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => (Path: path, Id: IdOf(path, root)))
            .Where(project => project.Id is not null)
            .Select(project => new DotNetProject(project.Path, System.IO.Path.GetFileNameWithoutExtension(project.Path), project.Id!))
            .OrderBy(project => project.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string? IdOf(string project, string gitRoot)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(gitRoot);
        if (ReadId(project) is { } own)
        {
            return Checked(own, project);
        }

        var root = System.IO.Path.GetFullPath(gitRoot);
        for (var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(project));
             directory is not null && (IsInside(root, directory) || SameDirectory(root, directory));
             directory = System.IO.Path.GetDirectoryName(directory))
        {
            var props = System.IO.Path.Combine(directory, PropsFileName);
            if (File.Exists(props))
            {
                return ReadId(props) is { } inherited ? Checked(inherited, props) : null;
            }
        }

        return null;
    }

    private static string Checked(string id, string file)
    {
        if (id.Contains("$(", StringComparison.Ordinal))
        {
            throw new VaultException($"'{file}' sets {IdProperty} from an MSBuild property ({id}). Set {IdProperty} literally.");
        }

        if (!IdPattern().IsMatch(id) || id is "." or "..")
        {
            throw new VaultException($"'{file}': {IdProperty} '{id}' may only hold letters, digits, '.', '_' and '-'.");
        }

        return id;
    }

    private static string? ReadId(string file)
    {
        try
        {
            var value = XDocument.Load(file).Descendants().FirstOrDefault(element => element.Name.LocalName == IdProperty)?.Value.Trim();
            return string.IsNullOrEmpty(value) ? null : value;
        }
        catch (XmlException ex)
        {
            throw new VaultException($"'{file}' is not valid XML: {ex.Message}", ex);
        }
    }

    private static IEnumerable<string> ProjectsOf(string solution)
    {
        var directory = System.IO.Path.GetDirectoryName(solution)!;
        IEnumerable<string> relative;
        if (string.Equals(System.IO.Path.GetExtension(solution), XmlSolutionExtension, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                relative = XDocument.Load(solution).Descendants()
                    .Where(element => element.Name.LocalName == "Project")
                    .Select(element => element.Attribute("Path")?.Value)
                    .OfType<string>()
                    .ToList();
            }
            catch (XmlException ex)
            {
                throw new VaultException($"'{solution}' is not valid XML: {ex.Message}", ex);
            }
        }
        else
        {
            relative = File.ReadLines(solution)
                .Select(line => SolutionProjectLine().Match(line))
                .Where(match => match.Success)
                .Select(match => match.Groups[1].Value)
                .ToList();
        }

        return relative
            .Where(path => string.Equals(System.IO.Path.GetExtension(path), ProjectExtension, StringComparison.OrdinalIgnoreCase))
            .Select(path => System.IO.Path.GetFullPath(System.IO.Path.Combine(directory, path.Replace('\\', System.IO.Path.DirectorySeparatorChar))));
    }

    // Windows matches "*.sln" against ".slnx" too, so files are filtered on their exact extension.
    private static IEnumerable<string> Files(string directory, string extension)
    {
        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var file in Directory.EnumerateFiles(directory, "*", options))
        {
            if (string.Equals(System.IO.Path.GetExtension(file), extension, StringComparison.OrdinalIgnoreCase))
            {
                yield return file;
            }
        }

        foreach (var child in Directory.EnumerateDirectories(directory, "*", options))
        {
            var name = System.IO.Path.GetFileName(child);
            if (name.StartsWith('.') || name.Equals("bin", StringComparison.OrdinalIgnoreCase) || name.Equals("obj", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var file in Files(child, extension))
            {
                yield return file;
            }
        }
    }

    private static bool IsInside(string root, string path) =>
        System.IO.Path.GetFullPath(path).StartsWith(System.IO.Path.TrimEndingDirectorySeparator(root) + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static bool SameDirectory(string a, string b) =>
        string.Equals(System.IO.Path.TrimEndingDirectorySeparator(a), System.IO.Path.TrimEndingDirectorySeparator(b), StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex("^[A-Za-z0-9._-]+$")]
    private static partial Regex IdPattern();

    [GeneratedRegex(@"^Project\(""[^""]*""\)\s*=\s*""[^""]*""\s*,\s*""([^""]+)""")]
    private static partial Regex SolutionProjectLine();
}
