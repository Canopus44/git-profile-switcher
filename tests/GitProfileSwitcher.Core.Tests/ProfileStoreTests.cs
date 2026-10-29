namespace GitProfileSwitcher.Core.Tests;

public class ProfileStoreTests
{
    [Fact]
    public void SaveThenLoad_RoundTripsProfilesAndActiveId()
    {
        using var temp = new TempDir();
        var store = new ProfileStore(temp.Path);
        var profile = new Profile
        {
            Name = "Trabajo",
            UserName = "octocat",
            UserEmail = "octocat@example.com",
            SshKeyPath = @"C:\keys\work",
            GhAccount = "octocat"
        };

        store.Save([profile], profile.Id);
        var result = store.Load();

        Assert.Null(result.Error);
        Assert.Equal(profile.Id, result.Data.ActiveProfileId);
        var loaded = Assert.Single(result.Data.Profiles);
        Assert.Equal("Trabajo", loaded.Name);
        Assert.Equal("octocat", loaded.UserName);
        Assert.Equal("octocat@example.com", loaded.UserEmail);
        Assert.Equal(@"C:\keys\work", loaded.SshKeyPath);
        Assert.Equal("octocat", loaded.GhAccount);

        var json = File.ReadAllText(store.FilePath);
        Assert.Contains("\"activeProfileId\"", json);
        Assert.Contains("\"userEmail\"", json);
        Assert.Empty(Directory.GetFiles(temp.Path, "*.tmp"));
    }

    [Fact]
    public void Load_MissingFile_ReturnsEmptyStore()
    {
        using var temp = new TempDir();
        var store = new ProfileStore(temp.Path);

        var result = store.Load();

        Assert.Null(result.Error);
        Assert.Empty(result.Data.Profiles);
        Assert.Null(result.Data.ActiveProfileId);
        Assert.False(File.Exists(store.FilePath));
    }

    [Fact]
    public void Load_CorruptJson_SurfacesErrorAndLeavesFileUntouched()
    {
        using var temp = new TempDir();
        var store = new ProfileStore(temp.Path);
        const string corrupt = "{ esto no es json";
        File.WriteAllText(store.FilePath, corrupt);

        var result = store.Load();

        Assert.NotNull(result.Error);
        Assert.Contains("profiles.json", result.Error);
        Assert.Empty(result.Data.Profiles);
        Assert.Equal(corrupt, File.ReadAllText(store.FilePath));
    }
}
