namespace HrmsAi.Abstractions;

// Thrown when a job cannot be queued because the queue is full
public class QueueFullException(string message, int statusCode = StatusCodes.Status429TooManyRequests) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
