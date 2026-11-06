using HrmsAi.Abstractions;
using HrmsAi.Access.Admin;
using HrmsAi.Access.Data;
using HrmsAi.Access.Services;

namespace HrmsAi.Access.Activity;

public static class ActivityEndpoints
{
    private static readonly Lazy<string> Html = new(() =>
    {
        using var s = typeof(ActivityEndpoints).Assembly.GetManifestResourceStream("activity.html")
                      ?? throw new InvalidOperationException("activity.html is not embedded.");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    });

    public static IEndpointRouteBuilder MapActivityEndpoints(this IEndpointRouteBuilder app)
    {
        // ---- client dashboard: the page is public, the data needs the client's own key ----
        app.MapGet("/dashboard", (HttpContext ctx) => Page(ctx, "client"));

        var portal = app.MapGroup("/portal/api");
        portal.MapGet("/me", PortalMe);
        portal.MapGet("/summary", PortalSummary);
        portal.MapGet("/requests", PortalList);
        portal.MapGet("/requests/{id}", PortalDetail);

        // ---- admin dashboard: only from the server itself, with the admin password ----
        var admin = app.MapGroup("/admin").AddEndpointFilter<RouteGroupBuilder, LocalOnlyFilter>();
        admin.MapGet("/activity", (HttpContext ctx) => Page(ctx, "admin"));

        var api = admin.MapGroup("/api/activity").AddEndpointFilter<RouteGroupBuilder, AdminPasswordFilter>();
        api.MapGet("/summary", AdminSummary);
        api.MapGet("/requests", AdminList);
        api.MapGet("/requests/{id}", AdminDetail);
        return app;
    }

    private static IResult Page(HttpContext ctx, string mode)
    {
        ctx.Response.Headers["Cache-Control"] = "no-store";
        ctx.Response.Headers["Content-Security-Policy"] =
            "default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; connect-src 'self'; img-src data:; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";
        return Results.Content(Html.Value.Replace("__MODE__", mode), "text/html; charset=utf-8");
    }

    private static IResult NoClient() =>
        Results.Json(new { error = "The dashboard needs a client API key. The old shared key has no history." }, statusCode: 403);

    private static string? Norm(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // ---------------- client (own key only) ----------------
    private static async Task<IResult> PortalMe(HttpContext ctx, ApiKeyRepository keys)
    {
        var id = ctx.GetCaller()?.KeyId;
        if (id is null) return NoClient();
        var rec = await keys.GetAsync(id.Value);
        if (rec is null) return Results.NotFound();
        var state = await keys.GetQuotaStateAsync(id.Value);
        return Results.Ok(ApiKeyService.ToView(rec, state?.Used ?? 0));
    }

    private static async Task<IResult> PortalSummary(HttpContext ctx, ActivityRepository repo, int? days)
    {
        var id = ctx.GetCaller()?.KeyId;
        return id is null ? NoClient() : await SummaryCore(repo, id, days);
    }

    private static async Task<IResult> PortalList(
        HttpContext ctx, ActivityRepository repo, int? days, string? module, string? status, int? skip, int? take)
    {
        var id = ctx.GetCaller()?.KeyId;
        return id is null ? NoClient() : await ListCore(repo, id, days, module, status, skip, take);
    }

    private static async Task<IResult> PortalDetail(string id, HttpContext ctx, ActivityRepository repo)
    {
        var key = ctx.GetCaller()?.KeyId;
        return key is null ? NoClient() : await DetailCore(repo, id, key);
    }

    // ---------------- admin (all clients, or one with ?keyId=) ----------------
    private static Task<IResult> AdminSummary(ActivityRepository repo, Guid? keyId, int? days) => SummaryCore(repo, keyId, days);

    private static Task<IResult> AdminList(
        ActivityRepository repo, Guid? keyId, int? days, string? module, string? status, int? skip, int? take)
        => ListCore(repo, keyId, days, module, status, skip, take);

    private static Task<IResult> AdminDetail(string id, ActivityRepository repo) => DetailCore(repo, id, null);

    // ---------------- shared ----------------
    private static async Task<IResult> SummaryCore(ActivityRepository repo, Guid? keyId, int? days)
        => Results.Ok(await repo.SummaryAsync(keyId, Math.Clamp(days ?? 30, 0, 3650)));

    private static async Task<IResult> ListCore(
        ActivityRepository repo, Guid? keyId, int? days, string? module, string? status, int? skip, int? take)
    {
        var (items, hasMore) = await repo.ListAsync(
            keyId, Math.Clamp(days ?? 30, 0, 3650), Norm(module), Norm(status),
            Math.Max(0, skip ?? 0), Math.Clamp(take ?? 25, 1, 100));
        return Results.Ok(new { items, hasMore });
    }

    private static async Task<IResult> DetailCore(ActivityRepository repo, string id, Guid? keyId)
    {
        var d = await repo.DetailAsync(id, keyId);
        return d is null ? Results.NotFound(new { error = "Unknown request." }) : Results.Ok(d);
    }
}
