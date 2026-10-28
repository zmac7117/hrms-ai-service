using System.Text.Json;

namespace HrmsAi.Platform.Models;

public record JobDto(
    Guid Id,
    string JobType,
    string Status,
    string? InputFileName,
    string? Error,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    int? DurationMs,
    JsonElement? Result,
    int? QueuePosition = null,
    int? EstimatedSecondsRemaining = null);

