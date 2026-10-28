namespace HrmsAi.Resume.Models;

public record UploadResult(bool Success, Guid? JobId, int StatusCode, string? Error)
{
    public static UploadResult Accepted(Guid jobId) => new(true, jobId, StatusCodes.Status202Accepted, null);
    public static UploadResult Fail(int statusCode, string error) => new(false, null, statusCode, error);
}

