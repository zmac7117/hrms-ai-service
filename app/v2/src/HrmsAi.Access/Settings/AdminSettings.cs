namespace HrmsAi.Access.Settings;

public class AdminSettings
{
    public const string SectionName = "Admin";

    // Set through the app pool environment variable Admin__Password (never in a file you publish)
    public string? Password { get; set; }

    // Keep false: the admin page then works only from the server itself
    public bool AllowRemote { get; set; }
}
