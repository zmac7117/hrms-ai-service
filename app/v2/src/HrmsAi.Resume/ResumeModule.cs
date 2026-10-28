using HrmsAi.Abstractions;
using HrmsAi.Resume.Endpoints;
using HrmsAi.Resume.Services;
using HrmsAi.Resume.Settings;

namespace HrmsAi.Resume;

public class ResumeModule : IModule
{
    public string Name => "resume";
    public string RoutePrefix => "/api/resume";

    public void ConfigureServices(IServiceCollection services, IConfiguration config)
    {
        services.Configure<StorageSettings>(config.GetSection(StorageSettings.SectionName));
        services.AddSingleton<ResumeParserService>();
        services.AddSingleton<ResumeFileStorage>();
        services.AddSingleton<ResumeUploadService>();
        services.AddSingleton<IJobHandler, ResumeJobHandler>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app) => app.MapResumeEndpoints();
}
