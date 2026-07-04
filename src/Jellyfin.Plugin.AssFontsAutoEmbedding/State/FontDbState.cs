using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.AssFontsAutoEmbedding.State;

public sealed class FontDbState
{
    public bool IsStale { get; set; } = true;

    public bool RebuildQueued { get; set; }

    public DateTimeOffset? LastBuildUtc { get; set; }

    public bool SelfCheckAttempted { get; set; }

    public long Version { get; set; }

    public List<string> LastConfiguredFontDirectories { get; set; } = new();

    public string? LastFailureReason { get; set; }
}
