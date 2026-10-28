namespace HrmsAi.Access.Models;

public record KeyRequest(
    string ClientName,
    string[]? AllowedModules,
    long? TokenLimit,
    string? LimitPeriod,
    DateTime? ExpiresAt,
    string? Notes);

public record KeyView(
    Guid Id,
    string ClientName,
    string KeyPrefix,
    bool IsActive,
    string[] AllowedModules,
    long? TokenLimit,
    string LimitPeriod,
    long Used,
    DateTime? ExpiresAt,
    string? Notes,
    DateTime CreatedAt,
    DateTime? LastUsedAt,
    string Status);

// The full key appears here once and is never stored or shown again
public record CreatedKeyResponse(KeyView Key, string ApiKey);
