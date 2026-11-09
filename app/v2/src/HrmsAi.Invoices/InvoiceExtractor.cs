using HrmsAi.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace HrmsAi.Invoices;

// One AI call per document. It runs inside a job, so it already holds an AI slot.
public class InvoiceExtractor(IChatClient chat, IOptions<InvoiceSettings> options)
{
    public static string[] Categories(InvoiceSettings s) =>
        s.Categories.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public async Task<(InvoiceExtraction Data, AiUsage Usage)> ExtractAsync(string text, bool includeLines, CancellationToken ct)
    {
        var s = options.Value;
        var categories = Categories(s);
        var document = text.Length <= s.MaxTextChars ? text : text.Substring(0, s.MaxTextChars);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, InvoicePrompt.System(string.Join(", ", categories), includeLines, s.MaxLineItems)),
            new(ChatRole.User, InvoicePrompt.ExampleText),
            new(ChatRole.Assistant, InvoicePrompt.ExampleAnswer(categories, includeLines)),
            new(ChatRole.User, document)
        };
        var chatOptions = new ChatOptions { Temperature = (float)s.Temperature, MaxOutputTokens = s.MaxOutputTokens };
        if (!string.IsNullOrWhiteSpace(s.Model)) chatOptions.ModelId = s.Model;

        var response = await chat.GetResponseAsync<InvoiceExtraction>(messages, chatOptions, cancellationToken: ct);
        if (!response.TryGetResult(out var data))
            throw new InvalidOperationException("The AI returned an answer that could not be read. Please send this document again.");

        var usage = AiUsage.From(response.Usage?.InputTokenCount, response.Usage?.OutputTokenCount, document.Length + 2500);
        return (data, usage);
    }
}
