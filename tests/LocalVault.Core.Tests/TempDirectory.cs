using System.Diagnostics;

namespace LocalVault.Core.Tests;

internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lv-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public static TempDirectory GitRepository(string? gitignore = "secrets.local.*\n")
    {
        var directory = new TempDirectory();
        directory.Git("init", "--quiet");
        if (gitignore is not null)
        {
            File.WriteAllText(directory.Combine(".gitignore"), gitignore);
        }

        return directory;
    }

    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    public string CreateDirectory(params string[] parts) => Directory.CreateDirectory(Combine(parts)).FullName;

    public void Git(params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = Path, UseShellExecute = false };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed.");
        }
    }

    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(Path, recursive: true);
    }
}
