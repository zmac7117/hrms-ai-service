using HrmsAi.Abstractions;
using HrmsAi.Documents;

namespace HrmsAi.Invoices;

public class InvoiceModule : IModule
{
    public string Name => "invoice";
    public string RoutePrefix => "/api/invoice";

    public void ConfigureServices(IServiceCollection services, IConfiguration config)
    {
        services.Configure<InvoiceSettings>(config.GetSection(InvoiceSettings.SectionName));
        services.AddHrmsOcr(config);
        services.AddSingleton<InvoiceSubmitService>();
        services.AddSingleton<InvoiceReader>();
        services.AddSingleton<InvoiceExtractor>();
        services.AddSingleton<DuplicateFinder>();
        services.AddSingleton<IJobHandler, InvoiceJobHandler>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app) => app.MapInvoiceEndpoints();
}
