namespace GitProfileSwitcher.Core;

public enum StepStatus
{
    Ok,
    Warning,
    Error
}

public sealed record StepResult(string Name, StepStatus Status, string Message);

public sealed record ApplyReport(IReadOnlyList<StepResult> Steps, bool Success);

public sealed class ProfileApplier
{
    readonly GitConfigService _git;
    readonly SshConfigManager _ssh;
    readonly IProcessRunner _runner;
    readonly string _sshConfigPath;
    readonly Func<string?> _ghLocator;

    public ProfileApplier(IProcessRunner runner, SshConfigManager ssh, string sshConfigPath, Func<string?>? ghLocator = null)
    {
        _runner = runner;
        _ssh = ssh;
        _sshConfigPath = sshConfigPath;
        _ghLocator = ghLocator ?? GhLocator.Find;
        _git = new GitConfigService(runner);
    }

    public ApplyReport Apply(Profile profile)
    {
        var errors = profile.Validate();
        if (errors.Count > 0)
            return new ApplyReport([new StepResult("Validacion", StepStatus.Error, string.Join(" ", errors))], false);

        var steps = new List<StepResult>();

        if (!TryGit(steps, "git user.name", "user.name", profile.UserName))
            return Report(steps);
        if (!TryGit(steps, "git user.email", "user.email", profile.UserEmail))
            return Report(steps);

        var readBack = _git.GetGlobal("user.email");
        if (!string.Equals(readBack, profile.UserEmail, StringComparison.OrdinalIgnoreCase))
        {
            steps.Add(new StepResult("verificacion user.email", StepStatus.Error,
                $"git global quedo en '{readBack ?? "(vacio)"}' en lugar de '{profile.UserEmail}'."));
            return Report(steps);
        }
        steps.Add(new StepResult("verificacion user.email", StepStatus.Ok, "Coincide con el perfil."));

        if (!string.IsNullOrWhiteSpace(profile.SshKeyPath))
        {
            var ssh = _ssh.Apply(_sshConfigPath, profile.SshKeyPath);
            if (!ssh.Ok)
            {
                steps.Add(new StepResult("ssh github.com", StepStatus.Error, ssh.Error ?? "Error desconocido."));
                return Report(steps);
            }
            steps.Add(new StepResult("ssh github.com", StepStatus.Ok,
                ssh.Changed ? "Bloque actualizado." : "Sin cambios."));
        }

        if (!string.IsNullOrWhiteSpace(profile.GhAccount))
        {
            var gh = _ghLocator();
            if (gh is null)
            {
                steps.Add(new StepResult("gh auth switch", StepStatus.Warning,
                    "No se encontro el ejecutable gh; se omitio el cambio de cuenta."));
            }
            else
            {
                var result = _runner.Run(gh, $"auth switch --user {GitConfigService.Quote(profile.GhAccount)}", 30_000);
                steps.Add(result.Ok
                    ? new StepResult("gh auth switch", StepStatus.Ok, $"Cuenta activa: {profile.GhAccount}.")
                    : new StepResult("gh auth switch", StepStatus.Warning, $"gh fallo: {result.Message}"));
            }
        }

        return Report(steps);
    }

    bool TryGit(List<StepResult> steps, string label, string key, string value)
    {
        var result = _git.SetGlobal(key, value);
        if (result.Ok)
        {
            steps.Add(new StepResult(label, StepStatus.Ok, value));
            return true;
        }

        steps.Add(new StepResult(label, StepStatus.Error, $"git fallo: {result.Message}"));
        return false;
    }

    static ApplyReport Report(List<StepResult> steps) =>
        new(steps, steps.All(step => step.Status != StepStatus.Error));
}
