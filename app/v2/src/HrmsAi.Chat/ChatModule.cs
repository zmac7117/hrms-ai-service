using HrmsAi.Abstractions;

namespace HrmsAi.Chat;

public class ChatModule : IModule
{
    public string Name => "chat";
    public string RoutePrefix => "/api/chat";

    // The page holds no data. Its actions call /api/chat/documents, which still needs the API key.
    public IReadOnlyList<string> OpenPaths => new[] { "/knowledgebase" };

    public void ConfigureServices(IServiceCollection services, IConfiguration config)
    {
        services.AddSignalR();
        services.AddMemoryCache();
        services.Configure<ChatSettings>(config.GetSection("Chat"));
        services.AddSingleton<ChatStore>();
        services.AddSingleton<RunningAnswers>();
        services.AddSingleton<HrmsActionClient>();
        services.AddSingleton<KnowledgeStore>();
        services.AddSingleton<KnowledgeSearch>();
        services.AddSingleton<ChatAnswerService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        // Under the module prefix, so the API key check sees it as a "chat" request.
        app.MapHub<ChatHub>("/api/chat/hub");
        KnowledgeEndpoints.Map(app);

        app.MapGet("/knowledgebase", () => Results.Content(KnowledgePage.Html, "text/html; charset=utf-8"));
        app.MapGet("/api/chat/status", () => Results.Ok(new { module = "chat", status = "running", hub = "/api/chat/hub" }));
    }
}