namespace HrmsAi.Abstractions;

public static class CallerExtensions
{
    private const string Key = "hrmsai.caller";

    public static void SetCaller(this HttpContext ctx, CallerInfo caller) => ctx.Items[Key] = caller;

    public static CallerInfo? GetCaller(this HttpContext ctx) =>
        ctx.Items.TryGetValue(Key, out var v) ? v as CallerInfo : null;
}
