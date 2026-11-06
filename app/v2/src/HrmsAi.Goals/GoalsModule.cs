using HrmsAi.Abstractions;
using HrmsAi.Compat;

namespace HrmsAi.Goals;

public class GoalsModule : IModule
{
    public string Name => "goals";
    public string RoutePrefix => "/api/goals";

    public void ConfigureServices(IServiceCollection services, IConfiguration config)
    {
        services.Configure<GoalsSettings>(config.GetSection(GoalsSettings.SectionName));
        services.AddHrmsLlm();
    }

    public void MapEndpoints(IEndpointRouteBuilder app) => app.MapGoalsEndpoints();
}
