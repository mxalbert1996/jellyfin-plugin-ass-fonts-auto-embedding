using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AssFontsAutoEmbedding.Services;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.AssFontsAutoEmbedding.Tasks;

public sealed class RebuildFontDbTask : IScheduledTask
{
    private readonly FontDbBuildService _fontDbBuildService;

    public RebuildFontDbTask(FontDbBuildService fontDbBuildService)
    {
        _fontDbBuildService = fontDbBuildService;
    }

    public string Name => "Rebuild font DB";

    public string Key => "AssFontsAutoEmbeddingRebuildDb";

    public string Description => "Forces a rebuild of the font database using the configured font directories.";

    public string Category => "ASS Fonts Auto Embedding";

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        => [];

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        progress.Report(0);
        var result = await _fontDbBuildService.ForceRebuildAsync(cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            throw new InvalidOperationException($"Font DB rebuild failed: {result.Message}");
        }

        progress.Report(100);
    }
}
