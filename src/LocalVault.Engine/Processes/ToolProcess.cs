using System.ComponentModel;
using System.Diagnostics;

namespace LocalVault.Engine;

public sealed record ToolResult(int ExitCode, string Output, string Error);

public static class ToolProcess
{
    public static ToolResult Run(string executable, IEnumerable<string> arguments, TimeSpan timeout)
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
            if (!process.WaitForExit(timeout))
            {
                process.Kill(entireProcessTree: true);
                throw new VaultException($"'{Path.GetFileName(executable)}' did not finish within {timeout.TotalSeconds} s.");
            }

            process.WaitForExit();
            return new ToolResult(process.ExitCode, output.Result, error.Result);
        }
    }
}
