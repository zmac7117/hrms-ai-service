using HrmsAi.Abstractions;
using HrmsAi.Platform.Data;
using HrmsAi.Platform.Defaults;
using HrmsAi.Platform.Endpoints;
using HrmsAi.Platform.Jobs;
using HrmsAi.Platform.Middleware;
using HrmsAi.Platform.Security;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OllamaSharp;

namespace HrmsAi.Platform;

public static class HrmsAiHost
{
    // Registers the shared runtime, then every module the host asked for.
    public static WebApplicationBuilder AddHrmsAi(this WebApplicationBuilder builder, params IModule[] modules)
    {
        var services = builder.Services;
        var config = builder.Configuration;

        services.Configure<AiOptions>(config.GetSection("Ai"));
        services.Configure<SecuritySettings>(config.GetSection(SecuritySettings.SectionName));
        services.Configure<QueueSettings>(config.GetSection(QueueSettings.SectionName));

        services.AddSingleton<IDbConnectionFactory, SqlConnectionFactory>();
        services.AddSingleton<IJobStore, SqlJobStore>();
        services.AddSingleton<IApiKeyValidator, ConfigApiKeyValidator>();
        services.AddSingleton<IQuotaGate, AllowAllQuotaGate>();
        services.AddSingleton<IUsageRecorder, NoOpUsageRecorder>();

        services.AddSingleton<IChatClient>(sp =>
        {
            var o = sp.GetRequiredService<IOptions<AiOptions>>().Value;
            var http = new HttpClient { BaseAddress = new Uri(o.BaseUrl), Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds) };
            return new OllamaApiClient(http, o.ChatModel);
        });

        services.AddHostedService<JobWorker>();

        foreach (var module in modules)
        {
            services.AddSingleton<IModule>(module);
            module.ConfigureServices(services, config);
        }
        return builder;
    }

    public static WebApplication UseHrmsAi(this WebApplication app)
    {
        app.UseErrorHandling();
        app.UseApiKey();

        app.MapHealthEndpoints();
        app.MapJobEndpoints();
        foreach (var module in app.Services.GetServices<IModule>())
            module.MapEndpoints(app);
        return app;
    }
}

