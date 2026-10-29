namespace GitProfileSwitcher.Core;

public sealed class GitConfigService
{
    const int TimeoutMs = 15_000;
    readonly IProcessRunner _runner;

    public GitConfigService(IProcessRunner runner) => _runner = runner;

    public string? GetGlobal(string key)
    {
        var result = _runner.Run("git", $"config --global --get {key}", TimeoutMs);
        return result.Ok ? result.StandardOutput.Trim() : null;
    }

    public ProcessResult SetGlobal(string key, string value) =>
        _runner.Run("git", $"config --global {key} {Quote(value)}", TimeoutMs);

    internal static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";
}
