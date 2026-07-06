using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.AssFontsAutoEmbedding.Configuration;

public sealed class PluginConfiguration : BasePluginConfiguration
{
    public bool Enabled { get; set; } = true;

    public List<string> FontDirectories { get; set; } = new();

    public NativeLogVerbosity NativeLogVerbosity { get; set; } = NativeLogVerbosity.Warn;

    public bool HasEquivalentFontDirectories(IReadOnlyCollection<string>? other)
    {
        var current = NormalizeFontDirectories(FontDirectories);
        var candidate = NormalizeFontDirectories(other);
        return current.SequenceEqual(candidate, StringComparer.OrdinalIgnoreCase);
    }

    public static string[] NormalizeFontDirectories(IEnumerable<string>? fontDirectories)
        => (fontDirectories ?? Array.Empty<string>())
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => path.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
