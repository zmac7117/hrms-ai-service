using HrmsAi.Abstractions;
using Microsoft.Extensions.Options;

namespace HrmsAi.Chat;

// Manage the knowledge of the calling API key:
//   GET    /api/chat/documents            list
//   POST   /api/chat/documents            upload (form: file + title, or form/JSON: text + title)
//   DELETE /api/chat/documents/{id}       remove
public static class KnowledgeEndpoints
{
    private record UploadJson(string? Title, string? Text);

    public static void Map(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/chat/documents");

        g.MapGet("", async (HttpContext http, KnowledgeStore store, CancellationToken ct) =>
            Results.Ok(await store.ListAsync(http.GetCaller()?.KeyId, ct)));

        g.MapDelete("/{id:guid}", async (Guid id, HttpContext http, KnowledgeStore store, KnowledgeSearch search, CancellationToken ct) =>
        {
            var keyId = http.GetCaller()?.KeyId;
            var removed = await store.DeleteAsync(keyId, id, ct);
            if (removed) search.Invalidate(keyId);
            return removed ? Results.Ok(new { deleted = true }) : Results.NotFound(new { error = "Document not found." });
        });

        g.MapGet("/{id:guid}", async (Guid id, HttpContext http, KnowledgeStore store, CancellationToken ct) =>
        {
            var doc = await store.GetTextAsync(http.GetCaller()?.KeyId, id, ct);
            return doc is null
                ? Results.NotFound(new { error = "Document not found." })
                : Results.Ok(new { id, title = doc.Value.Title, text = doc.Value.Text });
        });

        g.MapPost("", Upload);
    }

    private static async Task<IResult> Upload(
        HttpContext http, KnowledgeStore store, KnowledgeSearch search, IOptions<ChatSettings> options, CancellationToken ct)
    {
        var cfg = options.Value;
        var keyId = http.GetCaller()?.KeyId;
        string? title = null, text = null, fileName = null;

        if (http.Request.HasFormContentType)
        {
            var form = await http.Request.ReadFormAsync(ct);
            title = form["title"].ToString();
            var file = form.Files.GetFile("file");
            if (file is not null)
            {
                fileName = Path.GetFileName(file.FileName);
                var ext = Path.GetExtension(fileName).ToLowerInvariant();
                if (ext is not ".txt" and not ".md")
                    return Results.BadRequest(new { error = "Only .txt and .md files for now. PDF and Word come in a later step." });
                if (file.Length > cfg.MaxDocumentChars * 4L)
                    return Results.BadRequest(new { error = "File is too large." });
                using var reader = new StreamReader(file.OpenReadStream());
                text = await reader.ReadToEndAsync(ct);
            }
            else text = form["text"].ToString();
        }
        else
        {
            var body = await http.Request.ReadFromJsonAsync<UploadJson>(ct);
            title = body?.Title;
            text = body?.Text;
        }

        text = (text ?? "").Trim();
        if (text.Length == 0) return Results.BadRequest(new { error = "No text found. Send a file or a text field." });
        if (text.Length > cfg.MaxDocumentChars)
            return Results.BadRequest(new { error = $"Text is too long (limit {cfg.MaxDocumentChars} characters)." });

        title = ChatText.Clean(string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(fileName ?? "") : title, 200);
        if (title.Length == 0) return Results.BadRequest(new { error = "A title is required." });

        var chunks = KnowledgeChunker.Split(text, cfg.MaxChunkChars);
        if (chunks.Count == 0) return Results.BadRequest(new { error = "No text found." });
        if (chunks.Count > cfg.MaxChunksPerDocument)
            return Results.BadRequest(new { error = $"Document is too big ({chunks.Count} pieces, limit {cfg.MaxChunksPerDocument})." });

        var id = await store.AddDocumentAsync(keyId, title, fileName, chunks, ct);
        search.Invalidate(keyId);
        return Results.Ok(new { id, title, chunks = chunks.Count });
    }
}