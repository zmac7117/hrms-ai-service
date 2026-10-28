using HrmsAi.Abstractions;

namespace HrmsAi.Platform.Middleware;

public class ApiKeyMiddleware(RequestDelegate next, IEnumerable<IModule> modules)
{
    private const string HeaderName = "X-API-Key";
    private readonly string[] _open = new[] { "/api/health" }.Concat(modules.SelectMany(m => m.OpenPaths)).ToArray();

    public async Task InvokeAsync(HttpContext ctx, IApiKeyValidator validator)
    {
        if (_open.Any(p => ctx.Request.Path.StartsWithSegments(p)))
        {
            await next(ctx);
            return;
        }

        ctx.Request.Headers.TryGetValue(HeaderName, out var key);
        var result = await validator.ValidateAsync(key.ToString(), ctx.Request.Path.Value ?? "", ctx.RequestAborted);
        if (!result.Success)
        {
            ctx.Response.StatusCode = result.StatusCode;
            await ctx.Response.WriteAsJsonAsync(new { error = result.Error });
            return;
        }

        if (result.Caller is not null) ctx.SetCaller(result.Caller);
        await next(ctx);
    }
}

public static class ApiKeyMiddlewareExtensions
{
    public static IApplicationBuilder UseApiKey(this IApplicationBuilder app) => app.UseMiddleware<ApiKeyMiddleware>();
}
