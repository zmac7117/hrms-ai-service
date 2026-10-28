using HrmsAi.Abstractions;

namespace HrmsAi.Chat;

public class ChatModule : IModule
{
    public string Name => "chat";
    public string RoutePrefix => "/api/chat";

    public void ConfigureServices(IServiceCollection services, IConfiguration config)
    {
        // Chat settings, help-article tables and services will be registered here.
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/chat")
           .MapGet("/status", () => Results.Json(new { module = "chat", status = "not implemented yet" }, statusCode: 501));
    }
}
