using Dapper;
using HrmsAi.Abstractions;

namespace HrmsAi.Chat;

// All chat SQL lives here.
public class ChatStore(IDbConnectionFactory db)
{
    public async Task<Guid> CreateConversationAsync(Guid? keyId, string userId, string userName, string language, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        await using var c = db.Create();
        await c.ExecuteAsync(new CommandDefinition(
            @"INSERT INTO dbo.ChatConversations (Id, ApiKeyId, ExternalUserId, UserName, Language)
              VALUES (@id, @keyId, @userId, @userName, @language)",
            new { id, keyId, userId, userName, language }, cancellationToken: ct));
        return id;
    }

    // A conversation is only found for the same key AND the same user.
    public async Task<ConversationRow?> FindConversationAsync(Guid id, Guid? keyId, string userId, CancellationToken ct)
    {
        await using var c = db.Create();
        return await c.QueryFirstOrDefaultAsync<ConversationRow>(new CommandDefinition(
            @"SELECT Id, UserName, Language FROM dbo.ChatConversations
              WHERE Id = @id AND ExternalUserId = @userId
                AND (ApiKeyId = @keyId OR (ApiKeyId IS NULL AND @keyId IS NULL))",
            new { id, keyId, userId }, cancellationToken: ct));
    }

    // Oldest first, so it can go straight into the prompt.
    public async Task<IReadOnlyList<HistoryItem>> RecentMessagesAsync(Guid conversationId, int count, CancellationToken ct)
    {
        await using var c = db.Create();
        var rows = await c.QueryAsync<HistoryItem>(new CommandDefinition(
            @"SELECT TOP (@count) Role, Content FROM dbo.ChatMessages
              WHERE ConversationId = @conversationId ORDER BY Seq DESC",
            new { conversationId, count }, cancellationToken: ct));
        return rows.Reverse().ToList();
    }

    public async Task AddMessageAsync(Guid conversationId, Guid messageId, string role, string content, int? tokens, CancellationToken ct)
    {
        await using var c = db.Create();
        await c.ExecuteAsync(new CommandDefinition(
            @"INSERT INTO dbo.ChatMessages (Id, ConversationId, Role, Content, Tokens)
              VALUES (@messageId, @conversationId, @role, @content, @tokens);
              UPDATE dbo.ChatConversations SET LastMessageAt = SYSUTCDATETIME() WHERE Id = @conversationId;",
            new { messageId, conversationId, role, content, tokens }, cancellationToken: ct));
    }
}