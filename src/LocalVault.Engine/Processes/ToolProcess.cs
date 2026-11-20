using System.ComponentModel;
using System.Diagnostics;

namespace LocalVault.Engine;

public sealed record ToolResult(int ExitCode, string Output, string Error);

public static class ToolProcess
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(120);

    public static ToolResult Run(string executable, IEnumerable<string> arguments, TimeSpan timeout) =>
        Run(executable, arguments, process =>
        {
            if (!process.WaitForExit(timeout))
            {
                process.Kill(entireProcessTree: true);
                throw new VaultException($"'{Path.GetFileName(executable)}' did not finish within {timeout.TotalSeconds} s.");
            }
        });

    public static ToolResult Run(string executable, IEnumerable<string> arguments, Action tick, Func<bool> cancelled)
    {
        ArgumentNullException.ThrowIfNull(tick);
        ArgumentNullException.ThrowIfNull(cancelled);
        return Run(executable, arguments, process =>
        {
            while (!process.WaitForExit(TickInterval))
            {
                if (cancelled())
                {
                    process.Kill(entireProcessTree: true);
                    throw new OperationCanceledException($"'{Path.GetFileName(executable)}' was cancelled.");
                }

                tick();
            }
        });
    }

    public static int RunAttached(string executable, IEnumerable<string> arguments, string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = workingDirectory };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(start) ?? throw new VaultException($"'{executable}' did not start.");
            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Win32Exception ex)
        {
            throw new VaultException($"'{executable}' could not be started: {ex.Message}", ex);
        }
    }

    private static ToolResult Run(string executable, IEnumerable<string> arguments, Action<Process> wait)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var start = new ProcessStartInfo(executable)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        Process process;
        try
        {
            process = Process.Start(start) ?? throw new VaultException($"'{executable}' did not start.");
        }
        catch (Win32Exception ex)
        {
            throw new VaultException($"'{executable}' could not be started: {ex.Message}", ex);
        }

        using (process)
        {
            process.StandardInput.Close();
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            wait(process);
            process.WaitForExit();
            return new ToolResult(process.ExitCode, output.Result, error.Result);
        }
    }
}
