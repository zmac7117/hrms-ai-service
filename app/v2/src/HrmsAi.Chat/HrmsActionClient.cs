using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace HrmsAi.Chat;

public record ActionResult(bool Ok, string? Json, string? Error);

// Sends "RunAction" to the HRMS connection, waits for its "SubmitActionResult" reply,
// and keeps the employee record in memory.
public class HrmsActionClient(
    IHubContext<ChatHub> hub,
    IMemoryCache cache,
    IOptions<ChatSettings> options,
    ILogger<HrmsActionClient> log)
{
    private readonly ConcurrentDictionary<Guid, (string ConnectionId, TaskCompletionSource<JsonElement> Tcs)> _pending = new();

    public async Task<ActionResult> GetProfileAsync(string connectionId, Guid? keyId, string userId, CancellationToken ct)
    {
        var cacheKey = $"profile:{keyId}:{userId}";
        if (cache.TryGetValue(cacheKey, out string? cached) && cached is not null)
            return new ActionResult(true, cached, null);

        var result = await RunAsync(connectionId, "get_employee_profile", userId, ct);
        if (result.Ok && result.Json is not null)
            cache.Set(cacheKey, result.Json, TimeSpan.FromMinutes(options.Value.ProfileCacheMinutes));
        return result;
    }

    public async Task<ActionResult> RunAsync(string connectionId, string action, string userId, CancellationToken ct)
    {
        var requestId = Guid.NewGuid();
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[requestId] = (connectionId, tcs);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.ActionTimeoutSeconds));
        try
        {
            log.LogInformation("Asking HRMS: {Action} for user {User} (request {Request})", action, userId, requestId);
            await hub.Clients.Client(connectionId).SendAsync("RunAction",
                new { requestId, action, externalUserId = userId }, timeout.Token);

            var response = await tcs.Task.WaitAsync(timeout.Token);

            if (!response.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True)
            {
                var error = response.TryGetProperty("error", out var e) ? e.ToString() : "ACTION_FAILED";
                return new ActionResult(false, null, error);
            }
            if (!response.TryGetProperty("data", out var data) || data.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                return new ActionResult(false, null, "NO_DATA");

            return new ActionResult(true, data.GetRawText(), null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            log.LogWarning("HRMS action {Action} timed out (no reply in {Seconds}s).", action, options.Value.ActionTimeoutSeconds);
            return new ActionResult(false, null, "TIMEOUT");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "HRMS action {Action} failed.", action);
            return new ActionResult(false, null, "ACTION_FAILED");
        }
        finally
        {
            _pending.TryRemove(requestId, out _);
        }
    }

    // Called by the hub when the HRMS replies. Only the connection that was asked may answer.
    public void Complete(string connectionId, JsonElement response)
    {
        if (response.ValueKind != JsonValueKind.Object ||
            !response.TryGetProperty("requestId", out var idElement) ||
            !Guid.TryParse(idElement.ToString(), out var id))
        {
            log.LogWarning("HRMS sent an action result without a valid requestId.");
            return;
        }

        if (_pending.TryGetValue(id, out var entry) && entry.ConnectionId == connectionId)
            entry.Tcs.TrySetResult(response.Clone());
        else
            log.LogWarning("Action result {Request} ignored (unknown request or wrong connection).", id);
    }
}