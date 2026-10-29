namespace GitProfileSwitcher.Core.Tests;

public class SystemProfileScannerTests
{
    static readonly SystemProfileScanner Scanner = new(ghHostsPath: "");

    [Fact]
    public void Scan_GlobalIdentityOnly_ReturnsSingleProfile()
    {
        using var home = new TempDir();
        File.WriteAllText(home.File(".gitconfig"), """
            [user]
                name = Octo Cat
                email = octocat@ejemplo.com
            [core]
                editor = vim
            """);

        var result = Scanner.Scan(home.Path);

        var profile = Assert.Single(result.Profiles);
        Assert.Equal("octocat@ejemplo.com", profile.UserEmail);
        Assert.Equal("Octo Cat", profile.UserName);
        Assert.Null(profile.SshKeyPath);
        Assert.Empty(result.GhAccounts);
    }

    [Fact]
    public void Scan_IncludeIfWorkAndPersonal_ReturnsTwoProfilesWithKeys()
    {
        using var home = new TempDir();
        Directory.CreateDirectory(home.File(".ssh"));
        File.WriteAllText(home.File(".gitconfig-work"), """
            [user]
                name = Octo Work
                email = octocat@empresa.com
            """);
        File.WriteAllText(home.File(".gitconfig-personal"), """
            [user]
                name = Octo Personal
                email = octocat@ejemplo.com
            """);
        File.WriteAllText(Path.Combine(home.Path, ".ssh", "id_ed25519_work"), "");
        File.WriteAllText(Path.Combine(home.Path, ".ssh", "id_ed25519_personal"), "");
        File.WriteAllText(home.File(".gitconfig"), $"""
            [includeIf "gitdir:C:/trabajo/"]
                path = {home.Path.Replace('\\', '/')}/.gitconfig-work
            [includeIf "gitdir:C:/personal/"]
                path = {home.Path.Replace('\\', '/')}/.gitconfig-personal
            """);

        var result = Scanner.Scan(home.Path);

        Assert.Equal(2, result.Profiles.Count);
        var work = Assert.Single(result.Profiles, profile => profile.Name == "Work");
        Assert.Equal("octocat@empresa.com", work.UserEmail);
        Assert.Equal("Octo Work", work.UserName);
        Assert.Equal(Path.Combine(home.Path, ".ssh", "id_ed25519_work"), work.SshKeyPath);

        var personal = Assert.Single(result.Profiles, profile => profile.Name == "Personal");
        Assert.Equal("octocat@ejemplo.com", personal.UserEmail);
        Assert.Equal(Path.Combine(home.Path, ".ssh", "id_ed25519_personal"), personal.SshKeyPath);
        Assert.Empty(result.Notes);
    }

    [Fact]
    public void Scan_DuplicateEmailsDeduplicatedAndMalformedIncludeWarns()
    {
        using var home = new TempDir();
        File.WriteAllText(home.File(".gitconfig-a"), """
            [user]
                name = Octo A
                email = octocat@ejemplo.com
            """);
        File.WriteAllText(home.File(".gitconfig-b"), """
            [user]
                name = Octo B
                email = OCTOCAT@ejemplo.com
            """);
        File.WriteAllText(home.File(".gitconfig"), $"""
            [includeIf "gitdir:C:/a/"]
                path = {home.Path.Replace('\\', '/')}/.gitconfig-a
            [includeIf "gitdir:C:/b/"]
                path = {home.Path.Replace('\\', '/')}/.gitconfig-b
            [includeIf "gitdir:C:/roto/"]
                path = {home.Path.Replace('\\', '/')}/.gitconfig-no-existe
            """);

        var result = Scanner.Scan(home.Path);

        Assert.Single(result.Profiles);
        Assert.Equal("octocat@ejemplo.com", result.Profiles[0].UserEmail);
        Assert.NotEmpty(result.Notes);
    }

    [Fact]
    public void Scan_GhHosts_ReturnsAccountsWithoutTokens()
    {
        using var home = new TempDir();
        var hosts = home.File("hosts.yml");
        File.WriteAllText(hosts, """
            github.com:
                git_protocol: https
                users:
                    octocat:
                    octo-work:
                user: octocat
                oauth_token: ghp_SuperSecretTokenValue
            """);
        var scanner = new SystemProfileScanner(hosts);

        var result = scanner.Scan(home.Path);

        Assert.Equal(["octocat", "octo-work"], result.GhAccounts);
        var text = string.Join("\n", result.GhAccounts.Concat(result.Notes).Concat(result.Profiles.Select(p => p.UserEmail)));
        Assert.DoesNotContain("ghp_", text);
        Assert.DoesNotContain("SuperSecret", text);
    }

    [Fact]
    public void Merge_ExistingProfilesWinOverDiscovered()
    {
        using var home = new TempDir();
        File.WriteAllText(home.File(".gitconfig"), """
            [user]
                name = Octo Cat
                email = octocat@ejemplo.com
            """);
        var existing = new Profile
        {
            Name = "Mio",
            UserName = "Octo",
            UserEmail = "octocat@ejemplo.com"
        };
        var store = new ProfileStore(home.File("config"));
        store.Save([existing], existing.Id);

        var loaded = store.Load();
        var discovered = Scanner.Scan(home.Path).Profiles;
        var merged = ProfileImport.Merge(loaded.Data.Profiles, discovered);

        var kept = Assert.Single(merged);
        Assert.Equal(existing.Id, kept.Id);
        Assert.Equal("Mio", kept.Name);
        Assert.Equal(existing.Id, store.Load().Data.ActiveProfileId);
    }
}
