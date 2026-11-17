using System.ComponentModel;
using System.Diagnostics;

namespace LocalVault.Engine;

internal sealed record GitResult(int ExitCode, string Output, string Error);

internal static class Git
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public static GitResult? Run(string workingDirectory, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        Process? process;
        try
        {
            process = Process.Start(start);
        }
        catch (Win32Exception)
        {
            return null;
        }

        using (process)
        {
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(Timeout))
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException($"git did not finish within {Timeout.TotalSeconds} s.");
            }

            process.WaitForExit();
            return new GitResult(process.ExitCode, output.Result, error.Result);
        }
    }
}
