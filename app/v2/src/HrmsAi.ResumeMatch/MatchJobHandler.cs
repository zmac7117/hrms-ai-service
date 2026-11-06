using System.Text.Json;
using HrmsAi.Abstractions;

namespace HrmsAi.ResumeMatch;

// Runs "match" jobs for the platform's job worker
public class MatchJobHandler(MatchScorer scorer) : IJobHandler
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public string Module => "match";
    public string JobType => "match";

    public async Task<JobOutcome> HandleAsync(AiJob job, CancellationToken ct)
    {
        var input = JsonSerializer.Deserialize<MatchInput>(job.InputText ?? "", Web)
                    ?? throw new InvalidOperationException("The match job has no input.");
        var (json, usage) = await scorer.ScoreAsync(input, ct);
        return new JobOutcome(json, usage);
    }
}
