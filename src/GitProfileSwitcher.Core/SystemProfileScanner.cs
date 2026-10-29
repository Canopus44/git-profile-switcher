namespace GitProfileSwitcher.Core;

public sealed record DiscoveredProfile(string Name, string UserName, string UserEmail, string? SshKeyPath, string? GhAccount);

public sealed record ScanResult(IReadOnlyList<DiscoveredProfile> Profiles, IReadOnlyList<string> GhAccounts, IReadOnlyList<string> Notes);

public sealed class SystemProfileScanner
{
    const string GitConfigPrefix = ".gitconfig";

    static readonly string[] KeyPrefixes = ["id_ed25519", "id_ecdsa", "id_rsa", "id_dsa"];

    readonly string? _ghHostsPath;

    public SystemProfileScanner(string? ghHostsPath = null) => _ghHostsPath = ghHostsPath;

    public ScanResult Scan(string homeDirectory)
    {
        var notes = new List<string>();
        var found = new List<(string Name, string UserName, string UserEmail, string? SshKeyPath)>();

        ReadGlobalConfig(homeDirectory, found, notes);
        MatchSshKeys(homeDirectory, found);

        var profiles = new List<DiscoveredProfile>();
        var seenEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in found)
        {
            var email = entry.UserEmail.Trim();
            if (email.Length == 0 || !email.Contains('@') || !seenEmails.Add(email))
                continue;

            profiles.Add(new DiscoveredProfile(entry.Name, entry.UserName.Trim(), email, entry.SshKeyPath, null));
        }

        var ghHostsPath = _ghHostsPath ?? DefaultGhHostsPath(homeDirectory);
        return new ScanResult(profiles, ReadGhAccounts(ghHostsPath, notes), notes);
    }

    public static string DefaultGhHostsPath(string homeDirectory) =>
        OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GitHub CLI", "hosts.yml")
            : Path.Combine(homeDirectory, ".config", "gh", "hosts.yml");

    static void ReadGlobalConfig(string homeDirectory, List<(string Name, string UserName, string UserEmail, string? SshKeyPath)> found, List<string> notes)
    {
        var path = Path.Combine(homeDirectory, GitConfigPrefix);
        if (!File.Exists(path))
        {
            notes.Add("No se encontro ~/.gitconfig en este equipo.");
            return;
        }

        string[] lines;
        try
        {
            lines = File.ReadAllLines(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            notes.Add($"No se pudo leer .gitconfig: {ex.Message}");
            return;
        }

        string? globalName = null;
        string? globalEmail = null;
        var inUser = false;
        var inIncludeIf = false;

        foreach (var line in lines)
        {
            var section = SectionName(line);
            if (section is not null)
            {
                inUser = section == "user";
                inIncludeIf = section == "includeif";
                continue;
            }

            var pair = KeyValue(line);
            if (pair is not { } keyValue)
                continue;

            if (inUser)
            {
                if (keyValue.Key == "name")
                    globalName = keyValue.Value;
                else if (keyValue.Key == "email")
                    globalEmail = keyValue.Value;
            }
            else if (inIncludeIf && keyValue.Key == "path")
            {
                var includePath = ExpandPath(keyValue.Value, homeDirectory);
                if (includePath.Length == 0)
                {
                    notes.Add("includeIf sin un 'path' valido; se omitio.");
                    continue;
                }

                var (name, email) = ReadIncludedUser(includePath);
                if (string.IsNullOrWhiteSpace(email))
                {
                    notes.Add($"No se pudo leer el perfil incluido '{includePath}'.");
                    continue;
                }

                found.Add((DeriveName(includePath, found.Count + 1), name ?? "", email, null));
            }
        }

        if (string.IsNullOrWhiteSpace(globalEmail))
            return;

        var isDuplicate = found.Any(entry => string.Equals(entry.UserEmail, globalEmail, StringComparison.OrdinalIgnoreCase));
        if (!isDuplicate)
            found.Add(("Global", globalName ?? "", globalEmail, null));
    }

    static (string? Name, string? Email) ReadIncludedUser(string path)
    {
        try
        {
            if (!File.Exists(path))
                return (null, null);

            string? name = null;
            string? email = null;
            var inUser = false;
            foreach (var line in File.ReadAllLines(path))
            {
                var section = SectionName(line);
                if (section is not null)
                {
                    inUser = section == "user";
                    continue;
                }

                if (!inUser || KeyValue(line) is not { } keyValue)
                    continue;

                if (keyValue.Key == "name")
                    name = keyValue.Value;
                else if (keyValue.Key == "email")
                    email = keyValue.Value;
            }

            return (name, email);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, null);
        }
    }

    static void MatchSshKeys(string homeDirectory, List<(string Name, string UserName, string UserEmail, string? SshKeyPath)> found)
    {
        if (found.Count == 0)
            return;

        string[] keys;
        try
        {
            var sshDirectory = Path.Combine(homeDirectory, ".ssh");
            if (!Directory.Exists(sshDirectory))
                return;

            keys = Directory.GetFiles(sshDirectory, "id_*")
                .Where(file => !file.EndsWith(".pub", StringComparison.OrdinalIgnoreCase))
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        if (keys.Length == 0)
            return;

        for (var i = 0; i < found.Count; i++)
        {
            var name = found[i].Name;
            var match = keys.FirstOrDefault(key =>
            {
                var suffix = KeySuffix(Path.GetFileName(key));
                return suffix.Length > 0 && string.Equals(suffix, name, StringComparison.OrdinalIgnoreCase);
            });

            if (match is null && found.Count == 1 && keys.Length == 1)
                match = keys[0];

            if (match is not null)
                found[i] = (found[i].Name, found[i].UserName, found[i].UserEmail, match);
        }
    }

    static List<string> ReadGhAccounts(string path, List<string> notes)
    {
        var accounts = new List<string>();
        try
        {
            if (!File.Exists(path))
                return accounts;

            var inUsers = false;
            foreach (var raw in File.ReadAllLines(path))
            {
                if (string.IsNullOrWhiteSpace(raw))
                    continue;

                var indent = raw.Length - raw.TrimStart().Length;
                var line = raw.Trim();

                if (line == "users:")
                {
                    inUsers = true;
                    continue;
                }

                if (!inUsers)
                    continue;

                if (indent < 8)
                {
                    inUsers = false;
                    continue;
                }

                if (indent != 8 || !line.EndsWith(':'))
                    continue;

                var account = line[..^1].Trim();
                if (account.Length > 0 && !account.StartsWith('#'))
                    accounts.Add(account);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            notes.Add($"No se pudo leer hosts.yml de gh: {ex.Message}");
        }

        return accounts;
    }

    static string? SectionName(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length < 2 || trimmed[0] != '[')
            return null;

        var close = trimmed.IndexOf(']');
        var inner = (close > 0 ? trimmed[1..close] : trimmed[1..]).Trim();
        if (inner.Length == 0)
            return null;

        var end = inner.IndexOfAny([' ', '\t', '"']);
        var name = end > 0 ? inner[..end] : inner;
        return name.ToLowerInvariant();
    }

    static (string Key, string Value)? KeyValue(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed[0] is '#' or ';' or '[')
            return null;

        var equals = trimmed.IndexOf('=');
        if (equals <= 0)
            return null;

        var key = trimmed[..equals].Trim().ToLowerInvariant();
        var value = trimmed[(equals + 1)..].Trim();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            value = value[1..^1];

        return (key, value);
    }

    static string ExpandPath(string value, string homeDirectory)
    {
        var path = value.Trim();
        if (path.Length == 0)
            return "";

        if (path == "~")
            return homeDirectory;
        if (path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal))
            path = Path.Combine(homeDirectory, path[2..]);

        return path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
    }

    static string DeriveName(string path, int index)
    {
        var file = Path.GetFileName(path.Replace('\\', '/'));
        if (file.StartsWith(GitConfigPrefix, StringComparison.OrdinalIgnoreCase))
            file = file[GitConfigPrefix.Length..];

        file = file.TrimStart('-', '_', '.', ' ');
        return file.Length == 0 ? $"Perfil {index}" : char.ToUpperInvariant(file[0]) + file[1..];
    }

    static string KeySuffix(string fileName)
    {
        foreach (var prefix in KeyPrefixes)
            if (fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return fileName[prefix.Length..].TrimStart('-', '_', '.');

        return "";
    }
}
