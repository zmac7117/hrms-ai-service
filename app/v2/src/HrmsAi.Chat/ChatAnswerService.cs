using System.Diagnostics;
using System.Text;
using HrmsAi.Abstractions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace HrmsAi.Chat;

// Runs one answer in the background:
// fixed replies -> HRMS data -> knowledge search -> AI slot -> scope check -> stream the answer -> save -> record usage.
public class ChatAnswerService(
    IServiceScopeFactory scopes,
    IHubContext<ChatHub> hub,
    ChatStore store,
    RunningAnswers running,
    HrmsActionClient actions,
    KnowledgeSearch knowledge,
    IOptions<ChatSettings> options,
    ILogger<ChatAnswerService> log)
{
    public async Task RunAsync(AnswerRequest r, CancellationToken ct)
    {
        var cfg = options.Value;
        var client = hub.Clients.Client(r.ConnectionId);
        var sw = Stopwatch.StartNew();
        try
        {
            using var scope = scopes.CreateScope();
            var usageRecorder = scope.ServiceProvider.GetRequiredService<IUsageRecorder>();

            var history = await store.RecentMessagesAsync(r.ConversationId, cfg.HistoryMessages, ct);
            await store.AddMessageAsync(r.ConversationId, Guid.NewGuid(), "user", r.Text, null, ct);

            // ---- 1. Greetings, thanks, bye: fixed reply, no AI ----
            var fixedReply = ChatShortcuts.Reply(r.Text, r.UserName);
            if (fixedReply is not null)
            {
                await ReplyFixedAsync(client, usageRecorder, r, fixedReply, "shortcut", sw.ElapsedMilliseconds, ct);
                return;
            }

            // ---- 1b. Leave balance: ask the HRMS, reply built by code (never by the AI) ----
            if (ChatShortcuts.WantsLeaveBalance(r.Text))
            {
                var balance = await actions.RunAsync(r.ConnectionId, "get_leave_balance", r.ExternalUserId, ct);
                var reply = balance.Ok && balance.Json is not null
                    ? LeaveBalanceReply.Format(balance.Json, r.UserName, r.Text)
                    : null;
                if (reply is null)
                {
                    log.LogWarning("Leave balance not available for {Message}: {Error}", r.MessageId, balance.Error);
                    reply = cfg.HrSystemDownReply;
                    await ReplyFixedAsync(client, usageRecorder, r, reply, "hrms_unavailable", sw.ElapsedMilliseconds, ct);
                }
                else
                {
                    var built = LeaveBalanceReply.Build(balance.Json!, r.UserName, r.Text);
                    if (built is null)
                        await ReplyFixedAsync(client, usageRecorder, r, reply, "leave_balance", sw.ElapsedMilliseconds, ct);
                    else
                        await ReplyCardAsync(client, usageRecorder, r, built, reply, "leave_balance", sw.ElapsedMilliseconds, ct);
                }
                return;
            }

            // ---- 1c. Attendance: ask the HRMS, reply built by code (never by the AI) ----
            if (ChatShortcuts.WantsAttendance(r.Text))
            {
                var attendance = await actions.RunAsync(r.ConnectionId, "get_attendance_summary", r.ExternalUserId, ct);
                var reply = attendance.Ok && attendance.Json is not null
                    ? AttendanceReply.Format(attendance.Json, r.UserName, r.Text)
                    : null;
                if (reply is null)
                {
                    log.LogWarning("Attendance not available for {Message}: {Error}", r.MessageId, attendance.Error);
                    reply = cfg.HrSystemDownReply;
                    await ReplyFixedAsync(client, usageRecorder, r, reply, "hrms_unavailable", sw.ElapsedMilliseconds, ct);
                }
                else
                {
                    var built = AttendanceReply.Build(attendance.Json!, r.UserName, r.Text);
                    if (built is null)
                        await ReplyFixedAsync(client, usageRecorder, r, reply, "attendance", sw.ElapsedMilliseconds, ct);
                    else
                        await ReplyCardAsync(client, usageRecorder, r, built, reply, "attendance", sw.ElapsedMilliseconds, ct);
                }
                return;
            }

            // ---- 2. Questions about the user's own details: ask the HRMS (kept in memory afterwards) ----
            string? profileJson = null;
            var wantsProfile = ChatShortcuts.WantsProfile(r.Text);
            if (wantsProfile)
            {
                var profile = await actions.GetProfileAsync(r.ConnectionId, r.KeyId, r.ExternalUserId, ct);
                if (!profile.Ok || profile.Json is null)
                {
                    log.LogWarning("Profile not available for {Message}: {Error}", r.MessageId, profile.Error);
                    await ReplyFixedAsync(client, usageRecorder, r, cfg.HrSystemDownReply, "hrms_unavailable", sw.ElapsedMilliseconds, ct);
                    return;
                }
                profileJson = ChatText.Truncate(profile.Json, cfg.MaxProfileChars);
            }

            // ---- 3. Knowledge search (a short follow-up is searched together with the question before it) ----
            KnowledgeResult? found = null;
            string? knowledgeText = null;
            if (!wantsProfile)
            {
                var query = r.Text;
                if (r.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < 4)
                {
                    var previous = history.LastOrDefault(h => h.Role == "user");
                    if (previous is not null) query = previous.Content + " " + r.Text;
                }
                found = await knowledge.SearchAsync(r.KeyId, query, ct);
                if (found.Hits.Count > 0)
                    knowledgeText = string.Join("\n\n", found.Hits.Select((h, i) => $"[{i + 1}] {h.Title}\n{h.Content}"));
            }

            var chat = scope.ServiceProvider.GetRequiredService<IChatClient>();
            var gate = scope.ServiceProvider.GetRequiredService<IAiGate>();

            using var slot = await gate.AcquireAsync(TimeSpan.FromSeconds(cfg.MaxWaitSeconds), ct);
            if (slot is null)
            {
                await SendErrorAsync(client, r, "AI_BUSY", "I'm busy right now, please try again.");
                return;
            }

            // ---- 4. Scope check (skipped when we have the user's record or matching knowledge) ----
            var checkUsage = AiUsage.None;
            if (cfg.ScopeCheck && !wantsProfile && knowledgeText is null)
            {
                var verdict = await ScopeGuard.CheckAsync(chat, history, r.Text, cfg.OutOfScopeReply, ct, cfg.ScopeModel ?? cfg.Model);
                log.LogInformation("Scope check for {Message}: inScope={InScope} reason={Reason}", r.MessageId, verdict.InScope, verdict.Reason);
                checkUsage = verdict.Usage;
                if (!verdict.InScope)
                {
                    await ReplyFixedAsync(client, usageRecorder, r, cfg.OutOfScopeReply, "out_of_scope", sw.ElapsedMilliseconds, ct, checkUsage);
                    return;
                }
            }

            // ---- 5. HR question, we have documents, nothing matched: do not guess ----
            if (!wantsProfile && knowledgeText is null && found is { HasDocuments: true }
                && cfg.StrictKnowledge && ChatShortcuts.IsQuestion(r.Text))
            {
                await ReplyFixedAsync(client, usageRecorder, r, cfg.NoAnswerReply, "no_answer", sw.ElapsedMilliseconds, ct, checkUsage);
                return;
            }

            // ---- 6. Real answer, streamed ----
            var messages = ChatPrompt.Build(r.UserName, history, r.Text, profileJson, knowledgeText);
            var chatOptions = new ChatOptions
            {
                ModelId = string.IsNullOrWhiteSpace(cfg.Model) ? null : cfg.Model,
                Temperature = (float)cfg.Temperature,
                MaxOutputTokens = cfg.MaxOutputTokens
            };

            var answer = new StringBuilder();
            long? inTokens = null, outTokens = null;

            await foreach (var update in chat.GetStreamingResponseAsync(messages, chatOptions, ct))
            {
                foreach (var content in update.Contents)
                {
                    if (content is UsageContent uc)
                    {
                        inTokens = uc.Details.InputTokenCount ?? inTokens;
                        outTokens = uc.Details.OutputTokenCount ?? outTokens;
                    }
                }
                if (string.IsNullOrEmpty(update.Text)) continue;
                answer.Append(update.Text);
                await client.SendAsync("ReceiveToken",
                    new { conversationId = r.ConversationId, messageId = r.MessageId, text = update.Text }, ct);
            }

            var text = answer.ToString().Trim();
            if (text.Length == 0)
            {
                await SendErrorAsync(client, r, "AI_FAILED", "Something went wrong, please try again.");
                return;
            }

            var promptChars = messages.Sum(m => m.Text?.Length ?? 0);
            var answerUsage = AiUsage.From(inTokens, outTokens, promptChars);
            var usage = new AiUsage(answerUsage.PromptTokens + checkUsage.PromptTokens,
                                    answerUsage.CompletionTokens + checkUsage.CompletionTokens);

            var operation = wantsProfile ? "profile_answer" : knowledgeText is not null ? "knowledge_answer" : "message";
            await store.AddMessageAsync(r.ConversationId, r.MessageId, "assistant", text, usage.TotalTokens, CancellationToken.None);
            await usageRecorder.RecordAsync(new UsageRecord(r.KeyId, "chat", operation, usage, null,
                DurationMs: (int)sw.ElapsedMilliseconds,
                InputSummary: ChatText.Truncate(r.Text, 300),
                ResultText: text, Status: "Done"), CancellationToken.None);

            await client.SendAsync("ReceiveDone",
                new { conversationId = r.ConversationId, messageId = r.MessageId, totalTokens = usage.TotalTokens }, ct);
        }
        catch (OperationCanceledException)
        {
            log.LogInformation("Chat answer {Message} stopped (connection closed or app stopping).", r.MessageId);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Chat answer {Message} failed.", r.MessageId);
            try
            {
                using var scope = scopes.CreateScope();
                var rec = scope.ServiceProvider.GetRequiredService<IUsageRecorder>();
                await rec.RecordAsync(new UsageRecord(r.KeyId, "chat", "message", AiUsage.None, null,
                    DurationMs: (int)sw.ElapsedMilliseconds,
                    InputSummary: ChatText.Truncate(r.Text, 300),
                    Error: ChatText.Truncate(ex.Message, 900), Status: "Failed"), CancellationToken.None);
            }
            catch { /* history must never hide the real error */ }
            await SendErrorAsync(client, r, "AI_FAILED", "Something went wrong, please try again.");
        }
        finally
        {
            running.Finish(r.ConversationId);
        }
    }

    // A reply written by code, not by the AI.
    private async Task ReplyFixedAsync(ISingleClientProxy client, IUsageRecorder recorder, AnswerRequest r,
        string reply, string operation, long elapsedMs, CancellationToken ct, AiUsage? usage = null)
    {
        var used = usage ?? AiUsage.None;
        await store.AddMessageAsync(r.ConversationId, r.MessageId, "assistant", reply, used.TotalTokens, CancellationToken.None);
        await recorder.RecordAsync(new UsageRecord(r.KeyId, "chat", operation, used, null,
            DurationMs: (int)elapsedMs,
            InputSummary: ChatText.Truncate(r.Text, 300),
            ResultText: reply, Status: "Done"), CancellationToken.None);

        await client.SendAsync("ReceiveToken",
            new { conversationId = r.ConversationId, messageId = r.MessageId, text = reply }, ct);
        await client.SendAsync("ReceiveDone",
            new { conversationId = r.ConversationId, messageId = r.MessageId, totalTokens = used.TotalTokens }, ct);
    }

    // A reply made of a short sentence plus a card (table). The full text is stored for the history.
    private async Task ReplyCardAsync(ISingleClientProxy client, IUsageRecorder recorder, AnswerRequest r,
        CardReply built, string fullText, string operation, long elapsedMs, CancellationToken ct)
    {
        await store.AddMessageAsync(r.ConversationId, r.MessageId, "assistant", fullText, 0, CancellationToken.None);
        await recorder.RecordAsync(new UsageRecord(r.KeyId, "chat", operation, AiUsage.None, null,
            DurationMs: (int)elapsedMs,
            InputSummary: ChatText.Truncate(r.Text, 300),
            ResultText: fullText, Status: "Done"), CancellationToken.None);

        await client.SendAsync("ReceiveToken",
            new { conversationId = r.ConversationId, messageId = r.MessageId, text = built.Intro }, ct);
        await client.SendAsync("ReceiveCard",
            new { conversationId = r.ConversationId, messageId = r.MessageId, card = built.Card }, ct);
        await client.SendAsync("ReceiveDone",
            new { conversationId = r.ConversationId, messageId = r.MessageId, totalTokens = 0 }, ct);
    }

    private static async Task SendErrorAsync(ISingleClientProxy client, AnswerRequest r, string code, string message)
    {
        try
        {
            await client.SendAsync("ReceiveError",
                new { conversationId = r.ConversationId, messageId = r.MessageId, errorCode = code, message },
                CancellationToken.None);
        }
        catch { /* the connection may be gone */ }
    }
}