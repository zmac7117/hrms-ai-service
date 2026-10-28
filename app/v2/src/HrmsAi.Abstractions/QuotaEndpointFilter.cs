namespace HrmsAi.Abstractions;

// Put .RequireQuota() on any endpoint that uses AI tokens
public class QuotaEndpointFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var gate = http.RequestServices.GetRequiredService<IQuotaGate>();
        var result = await gate.CheckAsync(http.GetCaller(), http.RequestAborted);
        if (!result.Allowed)
            return Results.Json(new { error = result.Message ?? "Quota exceeded." },
                                statusCode: StatusCodes.Status429TooManyRequests);
        return await next(context);
    }
}

public static class QuotaExtensions
{
    public static RouteHandlerBuilder RequireQuota(this RouteHandlerBuilder builder)
        => builder.AddEndpointFilter<QuotaEndpointFilter>();
}
