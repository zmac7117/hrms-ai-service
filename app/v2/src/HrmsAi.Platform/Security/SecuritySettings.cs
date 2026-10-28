namespace HrmsAi.Platform.Security;

public class SecuritySettings
{
    public const string SectionName = "Security";

    // Phase 1: one shared key from configuration (environment variable Security__ApiKey).
    // Phase 2 replaces this with database-managed keys.
    public string? ApiKey { get; set; }
}
