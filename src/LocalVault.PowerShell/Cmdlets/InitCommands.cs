using System.Management.Automation;
using LocalVault.Engine;

namespace LocalVault.PowerShell;

public sealed record VaultGitIgnoreLine(string Line, string Path);

[Cmdlet(VerbsData.Initialize, "Vault", SupportsShouldProcess = true)]
[OutputType(typeof(VaultGitIgnoreLine))]
public sealed class InitializeVaultCommand : VaultCmdletBase
{
    protected override void Run()
    {
        var missing = GitIgnoreFile.Missing(Location);
        if (missing.Count == 0)
        {
            Say("Nothing to add.");
            return;
        }

        var file = System.IO.Path.Combine(Location.GitRoot!, GitIgnoreFile.FileName);
        if (!ShouldProcess(file, $"Add {string.Join(", ", missing.Select(m => m.Line))}"))
        {
            return;
        }

        GitIgnoreFile.Append(Location.GitRoot!, [.. missing.Select(m => m.Line)]);
        foreach (var line in missing)
        {
            WriteObject(new VaultGitIgnoreLine(line.Line, line.Path));
        }

        Say("Commit .gitignore.");
    }
}
