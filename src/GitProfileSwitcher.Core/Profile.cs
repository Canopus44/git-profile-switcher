namespace GitProfileSwitcher.Core;

public sealed class Profile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("D");
    public string Name { get; set; } = "";
    public string UserName { get; set; } = "";
    public string UserEmail { get; set; } = "";
    public string? SshKeyPath { get; set; }
    public string? GhAccount { get; set; }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Name))
            errors.Add("El nombre es obligatorio.");
        if (string.IsNullOrWhiteSpace(UserName))
            errors.Add("git user.name es obligatorio.");
        if (string.IsNullOrWhiteSpace(UserEmail))
            errors.Add("git user.email es obligatorio.");
        else if (!UserEmail.Contains('@'))
            errors.Add("git user.email debe contener '@'.");
        return errors;
    }
}
