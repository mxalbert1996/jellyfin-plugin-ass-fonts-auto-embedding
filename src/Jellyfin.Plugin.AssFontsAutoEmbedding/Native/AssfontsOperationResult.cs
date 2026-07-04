namespace Jellyfin.Plugin.AssFontsAutoEmbedding.Native;

public sealed record AssfontsOperationResult(bool Success, string Message)
{
    public static AssfontsOperationResult Ok(string message) => new(true, message);

    public static AssfontsOperationResult Fail(string message) => new(false, message);
}
