using HrmsAi.Abstractions;
using HrmsAi.Access.Admin;
using HrmsAi.Access.Data;
using HrmsAi.Access.Services;
using HrmsAi.Access.Settings;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HrmsAi.Access;

// API keys, token limits, usage tracking and the admin UI
public class AccessModule : IModule
{
    public string Name => "access";
    public string RoutePrefix => "/admin";

    // The admin area has its own password, so it skips the client API key check
    public IReadOnlyList<string> OpenPaths => new[] { "/admin" };

    public void ConfigureServices(IServiceCollection services, IConfiguration config)
    {
        services.Configure<AdminSettings>(config.GetSection(AdminSettings.SectionName));
        services.AddMemoryCache();
        services.AddSingleton<ApiKeyRepository>();
        services.AddSingleton<KeyCache>();
        services.AddSingleton<ApiKeyService>();

        // Replace the Phase 1 defaults from the platform
        services.Replace(ServiceDescriptor.Singleton<IApiKeyValidator, DbApiKeyValidator>());
        services.Replace(ServiceDescriptor.Singleton<IQuotaGate, DbQuotaGate>());
        services.Replace(ServiceDescriptor.Singleton<IUsageRecorder, DbUsageRecorder>());
    }

    public void MapEndpoints(IEndpointRouteBuilder app) => app.MapAdminEndpoints();
}
