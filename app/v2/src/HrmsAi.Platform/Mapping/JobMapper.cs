using System.Text.Json;
using HrmsAi.Abstractions;
using HrmsAi.Platform.Models;

namespace HrmsAi.Platform.Mapping;

public static class JobMapper
{
    public static JobDto ToDto(this AiJob j) => new(
        j.Id, j.JobType, j.Status, j.InputFileName, j.Error,
        j.CreatedAt, j.StartedAt, j.CompletedAt, j.DurationMs,
        j.ResultJson is null ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(j.ResultJson));
}
