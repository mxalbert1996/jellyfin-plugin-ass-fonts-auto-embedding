using System;
using Jellyfin.Plugin.AssFontsAutoEmbedding.Configuration;

namespace Jellyfin.Plugin.AssFontsAutoEmbedding.Services;

public sealed class PluginContext
{
    public PluginConfiguration GetConfiguration()
        => Plugin.Instance?.Configuration ?? throw new InvalidOperationException("Plugin configuration is unavailable.");

    public string[] GetNormalizedFontDirectories()
        => PluginConfiguration.NormalizeFontDirectories(GetConfiguration().FontDirectories);

    public NativeLogVerbosity GetNativeLogVerbosity()
        => GetConfiguration().NativeLogVerbosity;
}
