using HrmsAi.Abstractions;
using HrmsAi.Compat;

namespace HrmsAi.JobDescription;

public class JobDescriptionModule : IModule
{
    public string Name => "jd";
    public string RoutePrefix => "/api/jd";

    public void ConfigureServices(IServiceCollection services, IConfiguration config)
    {
        services.Configure<JdSettings>(config.GetSection(JdSettings.SectionName));
        services.AddHrmsLlm();
    }

    public void MapEndpoints(IEndpointRouteBuilder app) => app.MapJdEndpoints();
}
