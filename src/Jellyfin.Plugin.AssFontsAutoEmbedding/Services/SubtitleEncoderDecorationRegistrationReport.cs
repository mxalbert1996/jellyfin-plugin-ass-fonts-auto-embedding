namespace Jellyfin.Plugin.AssFontsAutoEmbedding.Services;

public sealed class SubtitleEncoderDecorationRegistrationReport
{
    public bool OriginalRegistrationFound { get; init; }

    public bool DecorationInstalled { get; init; }

    public string Summary { get; init; } = string.Empty;
}
