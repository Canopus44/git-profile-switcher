namespace GitProfileSwitcher.Core.Tests;

sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gps-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Best effort cleanup for locked temp files.
        }
    }
}

sealed class FakeProcessRunner : IProcessRunner
{
    public List<(string Exe, string Args)> Calls { get; } = [];

    public Func<string, string, ProcessResult> Handler { get; set; } = (_, _) => new ProcessResult(0, "", "");

    public ProcessResult Run(string exe, string args, int timeoutMs)
    {
        Calls.Add((exe, args));
        return Handler(exe, args);
    }
}
