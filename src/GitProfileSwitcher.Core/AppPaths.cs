namespace GitProfileSwitcher.Core;

public static class AppPaths
{
    public const string FolderName = "GitProfileSwitcher";

    public static string ConfigDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (OperatingSystem.IsWindows())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), FolderName);

        if (OperatingSystem.IsMacOS())
            return Path.Combine(home, "Library", "Application Support", FolderName);

        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var baseDir = string.IsNullOrWhiteSpace(xdg) ? Path.Combine(home, ".config") : xdg;
        return Path.Combine(baseDir, FolderName);
    }

    public static string SshConfigPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "config");
}
