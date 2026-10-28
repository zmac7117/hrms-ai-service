namespace HrmsAi.Abstractions;

public record CallerInfo(Guid? KeyId, string ClientName);

public record AuthResult(bool Success, int StatusCode, string? Error, CallerInfo? Caller)
{
    public static AuthResult Ok(CallerInfo caller) => new(true, 200, null, caller);
    public static AuthResult Fail(int statusCode, string error) => new(false, statusCode, error, null);
}

public interface IApiKeyValidator
{
    Task<AuthResult> ValidateAsync(string? apiKey, string requestPath, CancellationToken ct);
}

public record QuotaResult(bool Allowed, string? Message)
{
    public static QuotaResult Allow { get; } = new(true, null);
    public static QuotaResult Deny(string message) => new(false, message);
}

public interface IQuotaGate
{
    Task<QuotaResult> CheckAsync(CallerInfo? caller, CancellationToken ct);
}
