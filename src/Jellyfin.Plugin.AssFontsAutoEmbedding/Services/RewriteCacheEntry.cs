namespace Jellyfin.Plugin.AssFontsAutoEmbedding.Services;

public sealed class RewriteCacheEntry
{
    public RewriteCacheEntry(string outputFilePath)
    {
        OutputFilePath = outputFilePath;
    }

    public string OutputFilePath { get; }
}
