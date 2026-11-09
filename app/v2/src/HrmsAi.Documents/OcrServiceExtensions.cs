using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HrmsAi.Documents;

public static class OcrServiceExtensions
{
    // Modules that need to read scans call this in ConfigureServices
    public static IServiceCollection AddHrmsOcr(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<OcrSettings>(config.GetSection(OcrSettings.SectionName));
        services.TryAddSingleton<OcrEngine>();
        return services;
    }
}
