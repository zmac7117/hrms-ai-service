using System.Text.Json;
using HrmsAi.Abstractions;
using HrmsAi.Resume.Services;

namespace HrmsAi.Resume;

// Runs "resume" jobs for the platform's job worker
public class ResumeJobHandler(ResumeParserService parser) : IJobHandler
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public string Module => "resume";
    public string JobType => "resume";

    public async Task<JobOutcome> HandleAsync(AiJob job, CancellationToken ct)
    {
        var outcome = await parser.ParseAsync(job.InputText ?? "", ct);
        return new JobOutcome(JsonSerializer.Serialize(outcome.Data, Web), outcome.Usage);
    }
}
