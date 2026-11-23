using System.Text.Json;
using HrmsAi.Abstractions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace HrmsAi.Chat;

// The HRMS server connects here (X-API-Key header). All HRMS users share that one connection.
public class ChatHub(
    ChatStore store,
    ChatAnswerService answers, HrmsActionClient actions,
    RunningAnswers running,
    IQuotaGate quota,
    IOptions<ChatSettings> options) : Hub
{
    public async Task<StartChatResult> StartChat(StartChatRequest request)
    {
        var ct = Context.ConnectionAborted;
        var caller = Context.GetHttpContext()?.GetCaller();

        var userId = ChatText.Clean(request.ExternalUserId, 100);
        if (userId.Length == 0) throw new HubException("externalUserId is required (max 100 characters).");

        var name = ChatText.Clean(request.UserName, 100);
        var language = ChatText.Clean(request.Language, 10);
        if (language.Length == 0) language = "en";

        if (request.ConversationId is Guid existingId)
        {
            var row = await store.FindConversationAsync(existingId, caller?.KeyId, userId, ct);
            if (row is not null)
                return new StartChatResult(row.Id, ChatText.WelcomeBack(row.UserName), false);
            // Not found for this user: quietly start a new chat instead.
        }

        var id = await store.CreateConversationAsync(caller?.KeyId, userId, name, language, ct);
        return new StartChatResult(id, ChatText.Greeting(name), true);
    }

    public async Task<SendResult> SendMessage(SendMessageRequest request)
    {
        var cfg = options.Value;
        var ct = Context.ConnectionAborted;
        var caller = Context.GetHttpContext()?.GetCaller();

        var text = (request.Text ?? "").Trim();
        if (text.Length == 0) return new SendResult(false, null, "EMPTY");
        if (text.Length > cfg.MaxMessageChars) return new SendResult(false, null, "TOO_LONG");

        var userId = ChatText.Clean(request.ExternalUserId, 100);
        var conversation = await store.FindConversationAsync(request.ConversationId, caller?.KeyId, userId, ct);
        if (conversation is null) return new SendResult(false, null, "NOT_FOUND");

        var q = await quota.CheckAsync(caller, ct);
        if (!q.Allowed) return new SendResult(false, null, "QUOTA_EXCEEDED");

        var cts = running.TryStart(conversation.Id, Context.ConnectionId);
        if (cts is null) return new SendResult(false, null, "BUSY");

        var messageId = Guid.NewGuid();
        var run = new AnswerRequest(conversation.Id, messageId, Context.ConnectionId,
            caller?.KeyId, userId, conversation.UserName, text);

        _ = Task.Run(() => answers.RunAsync(run, cts.Token));
        return new SendResult(true, messageId, null);
    }

    // The HRMS calls this to answer a RunAction request.
    public Task SubmitActionResult(JsonElement response)
    {
        actions.Complete(Context.ConnectionId, response);
        return Task.CompletedTask;
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        running.CancelConnection(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}