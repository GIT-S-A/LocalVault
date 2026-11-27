using System.Text;

namespace LocalVault.Engine.Tests;

public class GitIgnoreFileTests
{
    [Fact]
    public void OnlyACommittedRuleCounts()
    {
        using var repo = TempDirectory.GitRepository(gitignore: null);
        var vault = repo.Combine(VaultLocation.DefaultFileName);
        File.WriteAllText(repo.Combine(".git", "info", "exclude"), "secrets.local.*\n");

        Assert.True(GitIgnore.IsIgnored(vault));
        Assert.False(GitIgnoreFile.IsIgnoredByCommittedRule(repo.Path, vault));

        File.WriteAllText(repo.Combine(".gitignore"), "secrets.*\n!secrets.local.lv\n");
        Assert.False(GitIgnoreFile.IsIgnoredByCommittedRule(repo.Path, vault));

        File.WriteAllText(repo.Combine(".gitignore"), "secrets.local.*\n");
        Assert.True(GitIgnoreFile.IsIgnoredByCommittedRule(repo.Path, vault));
    }

    [Fact]
    public void MissingNamesTheVaultTheDefaultVaultAndTemplateFilesOnce()
    {
        using var repo = TempDirectory.GitRepository(gitignore: null);
        repo.CreateDirectory("infra");
        var location = VaultLocation.Resolve(repo.Combine("infra", "team.lv"), null, repo.Path);
        File.WriteAllText(repo.Combine("infra", "app.json.template"), "{}");
        ProjectFile.AddTemplate(location, repo.Combine("infra", "app[1].json"), repo.Combine("infra", "app.json.template"));

        var missing = GitIgnoreFile.Missing(location);

        Assert.Equal(["/infra/team.lv*", "secrets.local.*", @"/infra/app\[1].json"], missing.Select(m => m.Line));
        Assert.Equal(location.Path, missing[0].Path);

        GitIgnoreFile.Append(repo.Path, [.. missing.Select(m => m.Line)]);
        Assert.Empty(GitIgnoreFile.Missing(location));
    }

    [Fact]
    public void AppendKeepsTheLinesAndLineEndingsAndReusesItsBlock()
    {
        using var repo = new TempDirectory();
        var file = repo.Combine(".gitignore");
        File.WriteAllBytes(file, [.. Encoding.UTF8.Preamble, .. Encoding.UTF8.GetBytes("bin/\r\nobj/")]);

        GitIgnoreFile.Append(repo.Path, ["a"]);
        Assert.Equal("bin/\r\nobj/\r\n\r\n# LocalVault\r\na\r\n", Text(file));

        File.AppendAllText(file, "\r\nother/\r\n");
        GitIgnoreFile.Append(repo.Path, ["b"]);
        Assert.Equal("bin/\r\nobj/\r\n\r\n# LocalVault\r\na\r\nb\r\n\r\nother/\r\n", Text(file));
        Assert.True(File.ReadAllBytes(file).AsSpan().StartsWith(Encoding.UTF8.Preamble));
    }

    [Fact]
    public void AppendCreatesTheFile()
    {
        using var repo = new TempDirectory();

        GitIgnoreFile.Append(repo.Path, ["secrets.local.*"]);

        Assert.Equal($"# LocalVault{Environment.NewLine}secrets.local.*{Environment.NewLine}", File.ReadAllText(repo.Combine(".gitignore")));
    }

    private static string Text(string file)
    {
        var bytes = File.ReadAllBytes(file);
        return Encoding.UTF8.GetString(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble) ? bytes[3..] : bytes);
    }
}
