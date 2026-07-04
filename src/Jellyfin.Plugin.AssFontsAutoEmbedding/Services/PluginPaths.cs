using System.IO;

namespace Jellyfin.Plugin.AssFontsAutoEmbedding.Services;

public sealed class PluginPaths
{
    public string GetPluginDataDirectory()
        => Plugin.Instance?.DataFolderPath ?? throw new System.InvalidOperationException("Plugin instance is unavailable; plugin data path cannot be resolved yet.");

    public string GetFontDbDirectory()
        => Path.Combine(GetPluginDataDirectory(), "db");

    public string GetFontDbFilePath()
        => Path.Combine(GetFontDbDirectory(), "fonts.json");

    public string GetRewriteRootDirectory()
        => Path.Combine(GetPluginDataDirectory(), "rewrite-cache");

    public string GetRewriteOutputDirectory(RewriteCacheKey key)
        => Path.Combine(GetRewriteRootDirectory(), key.Value);

    public string GetRewriteOutputFilePath(RewriteCacheKey key, string subtitlePath)
        => Path.Combine(GetRewriteOutputDirectory(key), $"{Path.GetFileNameWithoutExtension(subtitlePath)}.assfonts{Path.GetExtension(subtitlePath)}");

    public void DeleteRewriteOutputDirectory(RewriteCacheKey key)
    {
        var directory = GetRewriteOutputDirectory(key);
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }
}
