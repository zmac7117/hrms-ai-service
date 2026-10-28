namespace HrmsAi.Platform.Jobs;

public class QueueSettings
{
    public const string SectionName = "Queue";

    // How many jobs run at the same time (needs OLLAMA_NUM_PARALLEL to match)
    public int Concurrency { get; set; } = 1;

    // Maximum waiting + running jobs for one API key (0 = no limit)
    public int MaxPendingPerKey { get; set; } = 20;

    // Maximum waiting + running jobs for the whole service (0 = no limit)
    public int MaxPendingTotal { get; set; } = 200;
}
