namespace GitProfileSwitcher.Core.Tests;

public class SshConfigManagerTests
{
    readonly SshConfigManager _manager = new();

    [Fact]
    public void Apply_MissingFile_CreatesDirectoryAndAppendsBlock()
    {
        using var temp = new TempDir();
        var configPath = temp.File(Path.Combine("nested", "config"));

        var result = _manager.Apply(configPath, "/home/me/.ssh/id_work");

        Assert.True(result.Ok);
        Assert.True(result.Changed);
        Assert.Null(result.Error);
        var content = File.ReadAllText(configPath);
        Assert.Contains(SshConfigManager.BeginMarker, content);
        Assert.Contains("IdentityFile /home/me/.ssh/id_work", content);
        Assert.Contains(SshConfigManager.EndMarker, content);
    }

    [Fact]
    public void Apply_ExistingManagedBlock_ReplacesOnlyThatBlock()
    {
        using var temp = new TempDir();
        var configPath = temp.File("config");
        var original =
            "Host bitbucket.org\n" +
            "    IdentityFile ~/.ssh/bitbucket\n" +
            "\n" +
            SshConfigManager.BeginMarker + "\n" +
            "Host github.com\n" +
            "    IdentityFile ~/.ssh/old\n" +
            SshConfigManager.EndMarker + "\n" +
            "\n" +
            "Host gitlab.com\n" +
            "    User git\n";
        File.WriteAllText(configPath, original);

        var result = _manager.Apply(configPath, "~/.ssh/new");

        Assert.True(result.Ok);
        Assert.True(result.Changed);
        var content = File.ReadAllText(configPath);
        Assert.DoesNotContain("~/.ssh/old", content);
        Assert.Contains("IdentityFile ~/.ssh/new", content);
        Assert.Contains("Host bitbucket.org", content);
        Assert.Contains("Host gitlab.com", content);
        Assert.Equal(2, content.Split(SshConfigManager.BeginMarker).Length);
        Assert.Equal(2, content.Split(SshConfigManager.EndMarker).Length);
    }

    [Fact]
    public void Apply_ExistingCrlfFile_PreservesCrlfAndUnrelatedContent()
    {
        using var temp = new TempDir();
        var configPath = temp.File("config");
        var original = "Host gitlab.com\r\n    User git\r\n";
        File.WriteAllText(configPath, original);

        var result = _manager.Apply(configPath, "C:\\keys\\work");

        Assert.True(result.Ok);
        var content = File.ReadAllText(configPath);
        Assert.StartsWith(original, content);
        Assert.DoesNotContain("\n", content.Replace("\r\n", ""));
    }

    [Fact]
    public void Apply_SameKeyTwice_SecondApplyReportsNoChange()
    {
        using var temp = new TempDir();
        var configPath = temp.File("config");

        var first = _manager.Apply(configPath, "~/.ssh/work");
        var afterFirst = File.ReadAllText(configPath);
        var second = _manager.Apply(configPath, "~/.ssh/work");

        Assert.True(first.Changed);
        Assert.True(second.Ok);
        Assert.False(second.Changed);
        Assert.Equal(afterFirst, File.ReadAllText(configPath));
    }

    [Fact]
    public void Apply_ManualGithubHostOutsideMarkers_AbortsWithoutWriting()
    {
        using var temp = new TempDir();
        var configPath = temp.File("config");
        var original = "Host github.com\n    User git\n";
        File.WriteAllText(configPath, original);

        var result = _manager.Apply(configPath, "~/.ssh/work");

        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
        Assert.Contains("Host github.com", result.Error);
        Assert.Equal(original, File.ReadAllText(configPath));
    }
}
