namespace GitProfileSwitcher.Core;

public static class ProfileImport
{
    // Existing profiles win; discovered ones only fill gaps by email.
    public static List<Profile> Merge(IReadOnlyList<Profile> existing, IEnumerable<DiscoveredProfile> discovered)
    {
        var emails = new HashSet<string>(existing.Select(profile => profile.UserEmail), StringComparer.OrdinalIgnoreCase);
        var merged = existing.ToList();

        foreach (var profile in discovered)
        {
            if (string.IsNullOrWhiteSpace(profile.UserEmail) || !emails.Add(profile.UserEmail))
                continue;

            merged.Add(new Profile
            {
                Name = profile.Name,
                UserName = profile.UserName,
                UserEmail = profile.UserEmail,
                SshKeyPath = profile.SshKeyPath,
                GhAccount = profile.GhAccount
            });
        }

        return merged;
    }
}
