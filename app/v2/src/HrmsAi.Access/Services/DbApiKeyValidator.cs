using System.Security.Cryptography;
using System.Text;
using HrmsAi.Abstractions;
using HrmsAi.Access.Data;

namespace HrmsAi.Access.Services;

// Replaces the single-key check from Phase 1
public class DbApiKeyValidator(
    ApiKeyRepository repo,
    KeyCache cache,
    IEnumerable<IModule> modules,
    IConfiguration config) : IApiKeyValidator
{
    public async Task<AuthResult> ValidateAsync(string? apiKey, string requestPath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return AuthResult.Fail(StatusCodes.Status401Unauthorized, "Unauthorized");

        var hash = KeyGenerator.Hash(apiKey);
        var hex = KeyGenerator.Hex(hash);

        var rec = cache.Get(hex);
        if (rec is null)
        {
            rec = await repo.FindByHashAsync(hash);
            if (rec is not null) cache.Set(hex, rec);
        }
        if (rec is null) return Legacy(apiKey);

        if (!rec.IsActive)
            return AuthResult.Fail(StatusCodes.Status403Forbidden, "This API key is deactivated.");
        if (rec.ExpiresAt is not null && rec.ExpiresAt.Value.Date.AddDays(1) <= DateTime.UtcNow)
            return AuthResult.Fail(StatusCodes.Status403Forbidden, "This API key has expired.");

        // Is the requested path inside a module this key may not use?
        var path = new PathString(requestPath);
        var module = modules.FirstOrDefault(m => m.Name != "access" && path.StartsWithSegments(m.RoutePrefix));
        if (module is not null && !IsAllowed(rec.AllowedModules, module.Name))
            return AuthResult.Fail(StatusCodes.Status403Forbidden, $"This API key is not allowed to use the '{module.Name}' module.");

        return AuthResult.Ok(new CallerInfo(rec.Id, rec.ClientName));
    }

    private static bool IsAllowed(string allowed, string module) =>
        allowed == "*" ||
        allowed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
               .Contains(module, StringComparer.OrdinalIgnoreCase);

    // The old shared key from configuration still works until you remove Security__ApiKey.
    // It is unlimited, not metered, and can see all jobs.
    private AuthResult Legacy(string apiKey)
    {
        var legacy = config["Security:ApiKey"];
        if (!string.IsNullOrEmpty(legacy) &&
            CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(apiKey), Encoding.UTF8.GetBytes(legacy)))
            return AuthResult.Ok(new CallerInfo(null, "legacy"));

        return AuthResult.Fail(StatusCodes.Status401Unauthorized, "Unauthorized");
    }
}
