using Dapper;
using HrmsAi.Abstractions;

namespace HrmsAi.Chat;

public class DocumentRow
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string? FileName { get; set; }
    public int Chunks { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ChunkRow
{
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
}

// All knowledge SQL lives here. Documents belong to one API key.
public class KnowledgeStore(IDbConnectionFactory db)
{
    // A document with the same title is replaced, so uploading again updates it.
    public async Task<Guid> AddDocumentAsync(Guid? keyId, string title, string? fileName, IReadOnlyList<string> chunks, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        await using var c = db.Create();
        await c.OpenAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);

        await c.ExecuteAsync(new CommandDefinition(DeleteSql("d.Title = @title"), new { keyId, title }, tx, cancellationToken: ct));
        await c.ExecuteAsync(new CommandDefinition(
            "INSERT INTO dbo.ChatDocuments (Id, ApiKeyId, Title, FileName) VALUES (@id, @keyId, @title, @fileName)",
            new { id, keyId, title, fileName }, tx, cancellationToken: ct));

        var rows = chunks.Select((content, i) => new { documentId = id, seq = i, content }).ToList();
        await c.ExecuteAsync(new CommandDefinition(
            "INSERT INTO dbo.ChatChunks (DocumentId, Seq, Content) VALUES (@documentId, @seq, @content)",
            rows, tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        return id;
    }

    public async Task<IReadOnlyList<DocumentRow>> ListAsync(Guid? keyId, CancellationToken ct)
    {
        await using var c = db.Create();
        var rows = await c.QueryAsync<DocumentRow>(new CommandDefinition(
            @"SELECT d.Id, d.Title, d.FileName, d.CreatedAt,
                     (SELECT COUNT(*) FROM dbo.ChatChunks k WHERE k.DocumentId = d.Id) AS Chunks
              FROM dbo.ChatDocuments d
              WHERE (d.ApiKeyId = @keyId OR (d.ApiKeyId IS NULL AND @keyId IS NULL))
              ORDER BY d.CreatedAt DESC", new { keyId }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<bool> DeleteAsync(Guid? keyId, Guid id, CancellationToken ct)
    {
        await using var c = db.Create();
        await c.OpenAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var removed = await c.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM dbo.ChatDocuments d WHERE d.Id = @id AND (d.ApiKeyId = @keyId OR (d.ApiKeyId IS NULL AND @keyId IS NULL))",
            new { id, keyId }, tx, cancellationToken: ct));
        if (removed > 0)
            await c.ExecuteAsync(new CommandDefinition(DeleteSql("d.Id = @id"), new { id, keyId }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return removed > 0;
    }

    public async Task<IReadOnlyList<ChunkRow>> LoadChunksAsync(Guid? keyId, CancellationToken ct)
    {
        await using var c = db.Create();
        var rows = await c.QueryAsync<ChunkRow>(new CommandDefinition(
            @"SELECT d.Title, k.Content
              FROM dbo.ChatChunks k JOIN dbo.ChatDocuments d ON d.Id = k.DocumentId
              WHERE (d.ApiKeyId = @keyId OR (d.ApiKeyId IS NULL AND @keyId IS NULL))
              ORDER BY d.Id, k.Seq", new { keyId }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<(string Title, string Text)?> GetTextAsync(Guid? keyId, Guid id, CancellationToken ct)
    {
        await using var c = db.Create();
        var title = await c.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT d.Title FROM dbo.ChatDocuments d WHERE d.Id = @id AND (d.ApiKeyId = @keyId OR (d.ApiKeyId IS NULL AND @keyId IS NULL))",
            new { id, keyId }, cancellationToken: ct));
        if (title is null) return null;
        var parts = await c.QueryAsync<string>(new CommandDefinition(
            "SELECT Content FROM dbo.ChatChunks WHERE DocumentId = @id ORDER BY Seq", new { id }, cancellationToken: ct));
        return (title, string.Join("\n\n", parts));
    }

    // Deletes chunks and documents of this key that match the extra condition.
    private static string DeleteSql(string condition) =>
        $@"DELETE k FROM dbo.ChatChunks k JOIN dbo.ChatDocuments d ON d.Id = k.DocumentId
             WHERE {condition} AND (d.ApiKeyId = @keyId OR (d.ApiKeyId IS NULL AND @keyId IS NULL));
           DELETE d FROM dbo.ChatDocuments d
             WHERE {condition} AND (d.ApiKeyId = @keyId OR (d.ApiKeyId IS NULL AND @keyId IS NULL));";
}