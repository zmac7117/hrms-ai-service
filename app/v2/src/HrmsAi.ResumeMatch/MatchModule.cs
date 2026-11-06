using HrmsAi.Abstractions;

namespace HrmsAi.ResumeMatch;

public class MatchModule : IModule
{
    public string Name => "match";
    public string RoutePrefix => "/api/match";

    public void ConfigureServices(IServiceCollection services, IConfiguration config)
    {
        services.Configure<MatchSettings>(config.GetSection(MatchSettings.SectionName));
        services.AddSingleton<MatchScorer>();
        services.AddSingleton<MatchSubmitService>();
        services.AddSingleton<IJobHandler, MatchJobHandler>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app) => app.MapMatchEndpoints();
}
