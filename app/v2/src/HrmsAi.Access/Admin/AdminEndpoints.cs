using HrmsAi.Abstractions;
using HrmsAi.Access.Data;
using HrmsAi.Access.Models;
using HrmsAi.Access.Services;

namespace HrmsAi.Access.Admin;

public static class AdminEndpoints
{
    private static readonly Lazy<string> UiHtml = new(() =>
    {
        using var s = typeof(AdminEndpoints).Assembly.GetManifestResourceStream("admin.html")
                      ?? throw new InvalidOperationException("admin.html is not embedded.");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    });

    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/admin").AddEndpointFilter<RouteGroupBuilder, LocalOnlyFilter>();
        admin.MapGet("/", ServeUi);

        var api = admin.MapGroup("/api").AddEndpointFilter<RouteGroupBuilder, AdminPasswordFilter>();
        api.MapGet("/ping", () => Results.Ok(new { ok = true }));
        api.MapGet("/modules", (HttpContext ctx) => Results.Ok(KnownModules(ctx)));
        api.MapGet("/keys", ListKeys);
        api.MapPost("/keys", CreateKey);
        api.MapPut("/keys/{id:guid}", UpdateKey);
        api.MapPost("/keys/{id:guid}/activate", (Guid id, ApiKeyService svc) => SetActive(id, true, svc));
        api.MapPost("/keys/{id:guid}/deactivate", (Guid id, ApiKeyService svc) => SetActive(id, false, svc));
        api.MapPost("/keys/{id:guid}/rotate", Rotate);
        api.MapGet("/keys/{id:guid}/usage", Usage);
        return app;
    }

    private static string[] KnownModules(HttpContext ctx) =>
        ctx.RequestServices.GetServices<IModule>().Where(m => m.Name != "access").Select(m => m.Name).ToArray();

    private static IResult ServeUi(HttpContext ctx)
    {
        ctx.Response.Headers["Cache-Control"] = "no-store";
        ctx.Response.Headers["Content-Security-Policy"] =
            "default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; connect-src 'self'; img-src data:; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";
        return Results.Content(UiHtml.Value, "text/html; charset=utf-8");
    }

    private static async Task<IResult> ListKeys(ApiKeyRepository repo)
    {
        var rows = await repo.ListAsync();
        return Results.Ok(rows.Select(r => ApiKeyService.ToView(r, r.Used)));
    }

    private static async Task<IResult> CreateKey(KeyRequest req, HttpContext ctx, ApiKeyService svc)
    {
        try
        {
            var (record, key) = await svc.CreateAsync(req, KnownModules(ctx));
            return Results.Ok(new CreatedKeyResponse(ApiKeyService.ToView(record, 0), key));
        }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    }

    private static async Task<IResult> UpdateKey(Guid id, KeyRequest req, HttpContext ctx, ApiKeyService svc)
    {
        try
        {
            return await svc.UpdateAsync(id, req, KnownModules(ctx)) ? Results.Ok(new { ok = true }) : Results.NotFound();
        }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    }

    private static async Task<IResult> SetActive(Guid id, bool active, ApiKeyService svc) =>
        await svc.SetActiveAsync(id, active) ? Results.Ok(new { ok = true }) : Results.NotFound();

    private static async Task<IResult> Rotate(Guid id, ApiKeyService svc)
    {
        var result = await svc.RotateAsync(id);
        if (result is null) return Results.NotFound();
        return Results.Ok(new CreatedKeyResponse(ApiKeyService.ToView(result.Value.Record, 0), result.Value.Key));
    }

    private static async Task<IResult> Usage(Guid id, ApiKeyRepository repo, int? days)
    {
        var d = Math.Clamp(days ?? 30, 1, 365);
        return Results.Ok(new { byDay = await repo.UsageByDayAsync(id, d), recent = await repo.RecentUsageAsync(id, 50) });
    }
}
