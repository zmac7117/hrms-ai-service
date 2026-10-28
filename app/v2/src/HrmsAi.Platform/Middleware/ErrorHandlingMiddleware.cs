namespace HrmsAi.Platform.Middleware;

public class ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> log)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        try
        {
            await next(ctx);
        }
        catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested)
        {
            // The caller disconnected. Nothing to report.
        }
        catch (HrmsAi.Abstractions.QueueFullException qex)
        {
            if (ctx.Response.HasStarted) throw;
            ctx.Response.StatusCode = qex.StatusCode;
            await ctx.Response.WriteAsJsonAsync(new { error = qex.Message });
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Unhandled error on {Method} {Path}", ctx.Request.Method, ctx.Request.Path);
            if (ctx.Response.HasStarted) throw;
            ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await ctx.Response.WriteAsJsonAsync(new { error = "An internal error occurred." });
        }
    }
}

public static class ErrorHandlingMiddlewareExtensions
{
    public static IApplicationBuilder UseErrorHandling(this IApplicationBuilder app)
        => app.UseMiddleware<ErrorHandlingMiddleware>();
}

