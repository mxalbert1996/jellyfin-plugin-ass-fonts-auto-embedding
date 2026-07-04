using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AssFontsAutoEmbedding.Services;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AssFontsAutoEmbedding.Tasks;

public sealed class PruneRewriteCacheTask : IScheduledTask
{
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromHours(24);
    private readonly ILogger<PruneRewriteCacheTask> _logger;
    private readonly RewriteCache _rewriteCache;

    public PruneRewriteCacheTask(RewriteCache rewriteCache, ILogger<PruneRewriteCacheTask> logger)
    {
        _rewriteCache = rewriteCache;
        _logger = logger;
    }

    public string Name => "Prune rewritten subtitle cache";

    public string Key => "AssFontsAutoEmbeddingPruneRewriteCache";

    public string Description => "Deletes rewritten subtitle cache directories older than 24 hours.";

    public string Category => "ASS Fonts Auto Embedding";

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        =>
        [
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.IntervalTrigger,
                IntervalTicks = DefaultInterval.Ticks
            }
        ];

    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(0);
        var prunedCount = _rewriteCache.PruneDirectoriesOlderThan(DefaultInterval);
        _logger.LogInformation("Prune rewrite cache task completed. Pruned {PrunedCount} directories.", prunedCount);
        progress.Report(100);
        return Task.CompletedTask;
    }
}
