using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HrmsAi.Compat;

public static class CompatServiceExtensions
{
    public static IServiceCollection AddHrmsLlm(this IServiceCollection services)
    {
        services.TryAddSingleton<LlmExecutor>();
        return services;
    }
}
