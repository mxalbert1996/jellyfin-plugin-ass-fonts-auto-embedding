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
    private readonly PluginContext _pluginContext;
    private readonly RewriteCache _rewriteCache;

    public PruneRewriteCacheTask(RewriteCache rewriteCache, PluginContext pluginContext, ILogger<PruneRewriteCacheTask> logger)
    {
        _rewriteCache = rewriteCache;
        _pluginContext = pluginContext;
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

        var cleanedCount = !_pluginContext.GetConfiguration().Enabled
            ? _rewriteCache.DeleteAllDirectories()
            : _rewriteCache.PruneDirectoriesOlderThan(DefaultInterval);

        _logger.LogInformation("Rewrite cache cleanup task completed. Cleaned up {CleanedCount} directories.", cleanedCount);

        progress.Report(100);
        return Task.CompletedTask;
    }
}
