using System.Security.Cryptography;
using System.Text;
using HrmsAi.Abstractions;
using Microsoft.Extensions.Options;

namespace HrmsAi.Platform.Security;

public class ConfigApiKeyValidator(IOptions<SecuritySettings> options) : IApiKeyValidator
{
    private readonly byte[] _key = Encoding.UTF8.GetBytes(options.Value.ApiKey ?? "");

    public Task<AuthResult> ValidateAsync(string? apiKey, string requestPath, CancellationToken ct)
    {
        var ok = _key.Length > 0
            && !string.IsNullOrEmpty(apiKey)
            && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(apiKey), _key);

        return Task.FromResult(ok
            ? AuthResult.Ok(new CallerInfo(null, "default"))
            : AuthResult.Fail(StatusCodes.Status401Unauthorized, "Unauthorized"));
    }
}
