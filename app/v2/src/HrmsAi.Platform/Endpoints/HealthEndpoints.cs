using HrmsAi.Abstractions;
using Microsoft.Extensions.Options;

namespace HrmsAi.Platform.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/health", GetHealth);
        return app;
    }

    private static async Task<IResult> GetHealth(HttpContext ctx, IOptions<AiOptions> opt)
    {
        var modules = ctx.RequestServices.GetServices<IModule>().Select(m => m.Name).ToArray();
        using var http = new HttpClient { BaseAddress = new Uri(opt.Value.BaseUrl), Timeout = TimeSpan.FromSeconds(5) };
        try
        {
            var reply = await http.GetStringAsync("/", ctx.RequestAborted);
            return Results.Ok(new { app = "ok", modules, ollama = reply.Trim(), model = opt.Value.ChatModel });
        }
        catch (Exception ex)
        {
            return Results.Json(new { app = "ok", modules, ollama = "unreachable", error = ex.Message }, statusCode: 503);
        }
    }
}
