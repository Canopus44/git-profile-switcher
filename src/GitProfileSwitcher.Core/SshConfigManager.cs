using System.Text;

namespace GitProfileSwitcher.Core;

public sealed record SshApplyResult(bool Ok, bool Changed, string? Error);

public sealed class SshConfigManager
{
    public const string BeginMarker = "# >>> git-profile-switch (managed, do not edit) >>>";
    public const string EndMarker = "# <<< git-profile-switch <<<";

    const string ManualHostError =
        "El archivo SSH ya define 'Host github.com' fuera del bloque administrado. Ajustalo manualmente y volve a intentar.";

    public SshApplyResult Apply(string configPath, string identityFile)
    {
        if (string.IsNullOrWhiteSpace(identityFile))
            return new SshApplyResult(false, false, "La ruta de la llave SSH esta vacia.");

        var text = "";
        if (File.Exists(configPath))
        {
            try
            {
                text = File.ReadAllText(configPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new SshApplyResult(false, false, $"No se pudo leer {configPath}: {ex.Message}");
            }
        }

        var newline = DetectNewline(text);
        var block = string.Join(newline, BlockLines(identityFile.Trim()));

        var blockStart = -1;
        var blockEnd = -1;
        if (text.Contains(BeginMarker) || text.Contains(EndMarker))
        {
            var begin = text.IndexOf(BeginMarker, StringComparison.Ordinal);
            var end = text.IndexOf(EndMarker, StringComparison.Ordinal);
            if (begin < 0 || end < begin)
                return new SshApplyResult(false, false,
                    "El bloque administrado esta incompleto o danado en el archivo SSH.");

            blockStart = LineStart(text, begin);
            blockEnd = LineEnd(text, end + EndMarker.Length);

            var outside = text[..blockStart] + text[blockEnd..];
            if (HasManualGithubHost(outside))
                return new SshApplyResult(false, false, ManualHostError);
        }
        else if (HasManualGithubHost(text))
        {
            return new SshApplyResult(false, false, ManualHostError);
        }

        var updated = blockStart >= 0
            ? text[..blockStart] + block + newline + text[blockEnd..]
            : Append(text, block, newline);

        if (updated == text)
            return new SshApplyResult(true, false, null);

        try
        {
            var directory = Path.GetDirectoryName(configPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(configPath, updated, new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new SshApplyResult(false, false, $"No se pudo escribir {configPath}: {ex.Message}");
        }

        return new SshApplyResult(true, true, null);
    }

    static string Append(string text, string block, string newline)
    {
        if (text.Length == 0)
            return block + newline;

        var separator = text.EndsWith(newline, StringComparison.Ordinal) ? newline : newline + newline;
        return text + separator + block + newline;
    }

    static string[] BlockLines(string identityFile) =>
    [
        BeginMarker,
        "Host github.com",
        "    HostName github.com",
        "    User git",
        "    IdentitiesOnly yes",
        $"    IdentityFile {identityFile}",
        EndMarker
    ];

    static string DetectNewline(string text) =>
        text.Contains("\r\n") ? "\r\n" : text.Contains('\n') ? "\n" : Environment.NewLine;

    static int LineStart(string text, int index)
    {
        var start = index;
        while (start > 0 && text[start - 1] != '\n')
            start--;
        return start;
    }

    static int LineEnd(string text, int index)
    {
        var end = index;
        while (end < text.Length && text[end] != '\n')
            end++;
        return end < text.Length ? end + 1 : end;
    }

    static bool HasManualGithubHost(string text) =>
        text.Split('\n').Any(line => line.Trim().Equals("Host github.com", StringComparison.OrdinalIgnoreCase));
}
