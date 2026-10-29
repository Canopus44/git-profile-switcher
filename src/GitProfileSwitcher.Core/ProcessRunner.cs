using System.Diagnostics;

namespace GitProfileSwitcher.Core;

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Ok => ExitCode == 0;

    public string Message =>
        string.IsNullOrWhiteSpace(StandardError) ? StandardOutput.Trim() : StandardError.Trim();
}

public interface IProcessRunner
{
    ProcessResult Run(string exe, string args, int timeoutMs);
}

public sealed class ProcessRunner : IProcessRunner
{
    public ProcessResult Run(string exe, string args, int timeoutMs)
    {
        var startInfo = new ProcessStartInfo(exe, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            return new ProcessResult(-1, "", $"No se pudo ejecutar '{exe}': {ex.Message}");
        }

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(timeoutMs))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Process exited between the timeout and the kill.
            }
            process.WaitForExit(2000);
            return new ProcessResult(-1, Completed(stdout), $"Tiempo de espera agotado ejecutando '{exe}'.");
        }

        return new ProcessResult(process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
    }

    static string Completed(Task<string> task) => task.IsCompletedSuccessfully ? task.Result : "";
}
