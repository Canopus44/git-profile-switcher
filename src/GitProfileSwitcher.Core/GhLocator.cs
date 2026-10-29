namespace GitProfileSwitcher.Core;

public static class GhLocator
{
    public static string? Find()
    {
        var exe = OperatingSystem.IsWindows() ? "gh.exe" : "gh";
        var onPath = FindOnPath(exe);
        if (onPath is not null)
            return onPath;

        if (OperatingSystem.IsWindows())
        {
            var fallback = @"C:\Program Files\GitHub CLI\gh.exe";
            if (File.Exists(fallback))
                return fallback;
        }

        return null;
    }

    static string? FindOnPath(string exe)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
            return null;

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim(), exe);
                if (File.Exists(candidate))
                    return candidate;
            }
            catch (ArgumentException)
            {
                // Malformed PATH entry, skip it.
            }
        }

        return null;
    }
}
