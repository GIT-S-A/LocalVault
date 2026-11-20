using System.Text;

namespace LocalVault.Engine;

public static class SshConfigFile
{
    public const string IncludeLine = "Include ~/.ssh/*/" + SshKeyPath.ConfigFileName;
    private const string Header = "# Written by LocalVault from the SSH key entries of this folder. Changes are overwritten.";

    public static string UserConfigPath => Path.Combine(StoredPathRules.HomeDirectory, SshKeyPath.SshFolder, "config");

    public static string Render(IEnumerable<VaultEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var text = new StringBuilder();
        foreach (var entry in entries.Where(e => e is { IsMissing: false, Ssh: not null }).OrderBy(e => e.Name.FullName, StringComparer.OrdinalIgnoreCase))
        {
            var stored = entry.StoredPath!;
            var key = entry.Ssh!.Agent ? stored + SshKeyPath.PublicKeySuffix : stored;
            var old = SshCommandLine.OldKeySuffixOf(entry) is { } suffix ? stored + suffix : null;
            foreach (var host in entry.SshHosts)
            {
                var destination = SshDestination.Parse(host);
                text.Append("Match host ").Append(destination.Host);
                if (destination.User is not null)
                {
                    text.Append(" user ").Append(destination.User);
                }

                text.Append('\n').Append("    IdentityFile ").Append(key).Append('\n');
                if (old is not null)
                {
                    text.Append("    IdentityFile ").Append(old).Append('\n');
                }

                text.Append("    IdentitiesOnly yes\n");
            }
        }

        return text.Length == 0 ? string.Empty : Header + "\n" + text;
    }

    public static void Write(string folder, IEnumerable<VaultEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(folder);
        var path = Path.Combine(folder, SshKeyPath.ConfigFileName);
        var text = Render(entries);
        if (text.Length == 0)
        {
            File.Delete(path);
        }
        else if (!File.Exists(path) || File.ReadAllText(path) != text)
        {
            File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
    }

    public static bool AddInclude(string configPath)
    {
        ArgumentNullException.ThrowIfNull(configPath);
        var existing = File.Exists(configPath) ? File.ReadAllText(configPath) : null;
        if (existing is not null && Lines(existing).Any(IsInclude))
        {
            return false;
        }

        var newline = existing?.Contains("\r\n", StringComparison.Ordinal) == true ? "\r\n" : "\n";
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        File.WriteAllText(configPath, IncludeLine + newline + (existing ?? string.Empty), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return true;
    }

    public static bool RemoveInclude(string configPath)
    {
        ArgumentNullException.ThrowIfNull(configPath);
        if (!File.Exists(configPath))
        {
            return false;
        }

        var existing = File.ReadAllText(configPath);
        var newline = existing.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = existing.Split(newline);
        var kept = lines.Where(line => !IsInclude(line)).ToArray();
        if (kept.Length == lines.Length)
        {
            return false;
        }

        File.WriteAllText(configPath, string.Join(newline, kept), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return true;
    }

    private static string[] Lines(string text) => text.ReplaceLineEndings("\n").Split('\n');

    private static bool IsInclude(string line) => line.Trim().Equals(IncludeLine, StringComparison.OrdinalIgnoreCase);
}
