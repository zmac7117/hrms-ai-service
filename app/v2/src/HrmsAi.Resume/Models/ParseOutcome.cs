using HrmsAi.Abstractions;

namespace HrmsAi.Resume.Models;

public record ParseOutcome(ParsedResume Data, AiUsage Usage);
