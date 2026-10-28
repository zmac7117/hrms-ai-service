namespace HrmsAi.Abstractions;

// A feature area (resume, chat, ...). A host build lists the modules it wants.
public interface IModule
{
    string Name { get; }          // for example "resume"
    string RoutePrefix { get; }   // for example "/api/resume"
    IReadOnlyList<string> OpenPaths => Array.Empty<string>();   // paths that skip the client API key check

    void ConfigureServices(IServiceCollection services, IConfiguration config);
    void MapEndpoints(IEndpointRouteBuilder app);
}
