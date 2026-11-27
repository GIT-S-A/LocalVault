using System.Text;
using System.Text.RegularExpressions;

namespace LocalVault.Engine;

public sealed record GitIgnoreLine(string Line, string Path);

public static partial class GitIgnoreFile
{
    public const string FileName = ".gitignore";
    public const string Header = "# LocalVault";

    public static string LineFor(VaultLocation vault)
    {
        ArgumentNullException.ThrowIfNull(vault);
        var gitRoot = vault.GitRoot ?? throw new VaultException($"'{vault.Path}' is not in a git repository.");
        return string.Equals(System.IO.Path.GetFileName(vault.Path), VaultLocation.DefaultFileName, StringComparison.OrdinalIgnoreCase)
            ? VaultLocation.SideFilePattern
            : LineForFile(gitRoot, vault.Path) + "*";
    }

    public static string LineForFile(string gitRoot, string path)
    {
        ArgumentNullException.ThrowIfNull(gitRoot);
        ArgumentNullException.ThrowIfNull(path);
        var relative = System.IO.Path.GetRelativePath(gitRoot, path).Replace('\\', '/');
        return "/" + GlobCharacter().Replace(relative, @"\$0");
    }

    public static IReadOnlyList<GitIgnoreLine> Missing(VaultLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        var gitRoot = location.GitRoot ?? throw new VaultException($"'{location.Path}' is not in a git repository; lv init works on a repository's .gitignore.");
        var vaults = new[] { location.Path, System.IO.Path.Combine(gitRoot, VaultLocation.DefaultFileName) }
            .Concat(ProjectFile.ReadVaults(location).Select(vault => vault.Path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => VaultLocation.Resolve(path, null, gitRoot));
        var missing = new List<GitIgnoreLine>();
        foreach (var vault in vaults.Where(vault => !IsIgnoredByCommittedRule(gitRoot, vault.Path)))
        {
            Add(missing, LineFor(vault), vault.Path);
        }

        foreach (var template in ProjectFile.ReadTemplates(location).Where(t => !IsIgnoredByCommittedRule(gitRoot, t.Path)))
        {
            Add(missing, LineForFile(gitRoot, template.Path), template.Path);
        }

        return missing;
    }

    public static bool IsIgnoredByCommittedRule(string gitRoot, string path)
    {
        ArgumentNullException.ThrowIfNull(gitRoot);
        ArgumentNullException.ThrowIfNull(path);
        GitResult? result;
        try
        {
            result = Git.Run(gitRoot, "check-ignore", "-v", "--no-index", "--", path);
        }
        catch (TimeoutException)
        {
            throw new VaultException($"git did not finish within {Git.Timeout.TotalSeconds} s checking '{path}'.");
        }

        if (result is null)
        {
            throw new VaultException($"git could not be started to check that '{path}' is ignored.");
        }

        if (result.ExitCode == 1)
        {
            return false;
        }

        if (result.ExitCode != 0 || RuleLine().Match(result.Output) is not { Success: true } rule)
        {
            throw new VaultException($"git could not check that '{path}' is ignored: {result.Error.Trim()}");
        }

        if (rule.Groups["pattern"].Value.StartsWith('!'))
        {
            return false;
        }

        if (rule.Groups["source"].Value.StartsWith('~'))
        {
            return false;
        }

        var source = System.IO.Path.GetFullPath(rule.Groups["source"].Value, gitRoot);
        var root = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(gitRoot)) + System.IO.Path.DirectorySeparatorChar;
        return string.Equals(System.IO.Path.GetFileName(source), FileName, StringComparison.Ordinal)
            && source.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            && !source.StartsWith(root + ".git" + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    public static void Append(string gitRoot, IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(gitRoot);
        ArgumentNullException.ThrowIfNull(lines);
        var file = System.IO.Path.Combine(gitRoot, FileName);
        var bytes = File.Exists(file) ? File.ReadAllBytes(file) : [];
        var bom = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble);
        var text = Encoding.UTF8.GetString(bom ? bytes[3..] : bytes);
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : text.Contains('\n', StringComparison.Ordinal) ? "\n" : Environment.NewLine;
        var content = text.Length == 0 ? [] : text.Split('\n').Select(line => line.TrimEnd('\r')).ToList();
        if (content.Count > 0 && content[^1].Length == 0)
        {
            content.RemoveAt(content.Count - 1);
        }

        var header = content.FindIndex(line => line.Trim() == Header);
        if (header >= 0)
        {
            var end = header + 1;
            while (end < content.Count && content[end].Trim().Length > 0)
            {
                end++;
            }

            content.InsertRange(end, lines);
        }
        else
        {
            if (content.Count > 0 && content[^1].Trim().Length > 0)
            {
                content.Add(string.Empty);
            }

            content.Add(Header);
            content.AddRange(lines);
        }

        var written = Encoding.UTF8.GetBytes(string.Join(newline, content) + newline);
        File.WriteAllBytes(file, bom ? [.. Encoding.UTF8.Preamble, .. written] : written);
    }

    private static void Add(List<GitIgnoreLine> missing, string line, string path)
    {
        if (!missing.Exists(m => string.Equals(m.Line, line, StringComparison.OrdinalIgnoreCase)))
        {
            missing.Add(new GitIgnoreLine(line, path));
        }
    }

    // git check-ignore -v prints "<source>:<line number>:<pattern>\t<path>"; the source may hold a drive colon.
    [GeneratedRegex(@"^(?<source>.+):(?<line>\d+):(?<pattern>[^\t]*)\t", RegexOptions.Multiline)]
    private static partial Regex RuleLine();

    [GeneratedRegex(@"[*?\[\\]")]
    private static partial Regex GlobCharacter();
}
