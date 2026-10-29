namespace GitProfileSwitcher.Core.Tests;

public class ProfileApplierTests
{
    static Profile BuildProfile(string sshKeyPath = "") => new()
    {
        Name = "Trabajo",
        UserName = "octocat",
        UserEmail = "octocat@example.com",
        SshKeyPath = string.IsNullOrEmpty(sshKeyPath) ? null : sshKeyPath,
        GhAccount = "octocat"
    };

    static FakeProcessRunner GitRunner(string email) => new()
    {
        Handler = (_, args) => args.Contains("--get user.email")
            ? new ProcessResult(0, email + "\n", "")
            : new ProcessResult(0, "", "")
    };

    [Fact]
    public void Apply_Success_RunsStepsInOrder()
    {
        using var temp = new TempDir();
        var runner = GitRunner("octocat@example.com");
        var applier = new ProfileApplier(runner, new SshConfigManager(), temp.File("ssh-config"), () => "gh");

        var report = applier.Apply(BuildProfile("~/.ssh/work"));

        Assert.True(report.Success);
        var names = report.Steps.Select(step => step.Name).ToList();
        Assert.Equal(
            ["git user.name", "git user.email", "verificacion user.email", "ssh github.com", "gh auth switch"],
            names);
        Assert.All(report.Steps, step => Assert.Equal(StepStatus.Ok, step.Status));
        Assert.Contains(runner.Calls, call => call.Exe == "gh" && call.Args.Contains("auth switch --user"));
        Assert.Contains("IdentityFile ~/.ssh/work", File.ReadAllText(temp.File("ssh-config")));
    }

    [Fact]
    public void Apply_GitFailure_StopsRemainingSteps()
    {
        using var temp = new TempDir();
        var runner = new FakeProcessRunner
        {
            Handler = (_, args) => args.Contains("user.name")
                ? new ProcessResult(1, "", "fatal: no se pudo escribir")
                : new ProcessResult(0, "", "")
        };
        var applier = new ProfileApplier(runner, new SshConfigManager(), temp.File("ssh-config"), () => "gh");

        var report = applier.Apply(BuildProfile("~/.ssh/work"));

        Assert.False(report.Success);
        var step = Assert.Single(report.Steps);
        Assert.Equal("git user.name", step.Name);
        Assert.Equal(StepStatus.Error, step.Status);
        Assert.False(File.Exists(temp.File("ssh-config")));
        Assert.DoesNotContain(runner.Calls, call => call.Exe == "gh");
    }

    [Fact]
    public void Apply_MissingGh_IsWarningButStillSuccessful()
    {
        using var temp = new TempDir();
        var applier = new ProfileApplier(
            GitRunner("octocat@example.com"), new SshConfigManager(), temp.File("ssh-config"), () => null);

        var report = applier.Apply(BuildProfile());

        Assert.True(report.Success);
        var ghStep = Assert.Single(report.Steps, step => step.Name == "gh auth switch");
        Assert.Equal(StepStatus.Warning, ghStep.Status);
    }
}
