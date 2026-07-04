namespace Jellyfin.Plugin.AssFontsAutoEmbedding.Services;

public sealed record RewriteResult(bool Success, string? OutputFilePath, string Message)
{
    public static RewriteResult Skipped(string message) => new(false, null, message);

    public static RewriteResult Rewritten(string outputFilePath, string message) => new(true, outputFilePath, message);
}
