using System.Net;
using System.Security.Cryptography;
using System.Text;
using HrmsAi.Access.Settings;
using Microsoft.Extensions.Options;

namespace HrmsAi.Access.Admin;

// The admin page and API answer 404 unless the request comes from this server itself.
// Requests that came through a tunnel or proxy carry forwarding headers and are refused.
public class LocalOnlyFilter(IOptions<AdminSettings> options) : IEndpointFilter
{
    private static readonly string[] ProxyHeaders =
        { "X-Forwarded-For", "X-Forwarded-Host", "Forwarded", "X-Real-IP", "CF-Connecting-IP", "True-Client-IP" };

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        if (!options.Value.AllowRemote)
        {
            var ip = http.Connection.RemoteIpAddress;
            var local = ip is not null && IPAddress.IsLoopback(ip);
            var proxied = ProxyHeaders.Any(h => http.Request.Headers.ContainsKey(h));
            if (!local || proxied) return Results.NotFound();
        }
        return await next(context);
    }
}

public class AdminPasswordFilter(IOptions<AdminSettings> options) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var password = options.Value.Password;
        if (string.IsNullOrEmpty(password))
            return Results.Json(new { error = "The admin password is not configured (setting Admin:Password)." }, statusCode: 503);

        http.Request.Headers.TryGetValue("X-Admin-Key", out var supplied);
        var ok = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(supplied.ToString()), Encoding.UTF8.GetBytes(password));
        if (!ok)
        {
            await Task.Delay(500, http.RequestAborted);   // slows down guessing
            return Results.Json(new { error = "Wrong admin password." }, statusCode: 401);
        }
        return await next(context);
    }
}
