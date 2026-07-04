using System;
using System.Linq;
using Jellyfin.Plugin.AssFontsAutoEmbedding.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AssFontsAutoEmbedding.State;

public sealed class FontDbStateManager
{
    private readonly ILogger<FontDbStateManager> _logger;
    private readonly FontDbState _state = new();
    private readonly object _syncRoot = new();

    public FontDbStateManager(ILogger<FontDbStateManager> logger)
    {
        _logger = logger;
    }

    public FontDbState Snapshot()
    {
        lock (_syncRoot)
        {
            return new FontDbState
            {
                IsStale = _state.IsStale,
                RebuildQueued = _state.RebuildQueued,
                LastBuildUtc = _state.LastBuildUtc,
                SelfCheckAttempted = _state.SelfCheckAttempted,
                Version = _state.Version,
                LastFailureReason = _state.LastFailureReason,
                LastConfiguredFontDirectories = _state.LastConfiguredFontDirectories.ToList()
            };
        }
    }

    public bool InvalidateForConfigurationChange(PluginConfiguration configuration)
    {
        lock (_syncRoot)
        {
            if (configuration.HasEquivalentFontDirectories(_state.LastConfiguredFontDirectories))
            {
                return false;
            }

            _state.IsStale = true;
            _state.RebuildQueued = true;
            _state.LastFailureReason = null;
            // Bump the DB version when configuration invalidates the current DB so rewritten subtitle
            // cache entries derived from the old DB can no longer be considered valid.
            _state.Version++;
            _state.LastConfiguredFontDirectories = PluginConfiguration.NormalizeFontDirectories(configuration.FontDirectories).ToList();
        }

        _logger.LogInformation("Marked font DB state stale after font directory configuration change.");
        return true;
    }

    public void MarkRebuildStarted()
    {
        lock (_syncRoot)
        {
            _state.RebuildQueued = false;
        }
    }

    public void MarkRebuildSucceeded()
    {
        lock (_syncRoot)
        {
            _state.IsStale = false;
            _state.RebuildQueued = false;
            _state.LastBuildUtc = DateTimeOffset.UtcNow;
            _state.LastFailureReason = null;
            // Bump the DB version again on successful rebuild so newly generated rewrite cache entries
            // are tied to the rebuilt DB contents rather than the invalidated generation.
            _state.Version++;
        }
    }

    public void MarkRebuildFailed(string reason)
    {
        lock (_syncRoot)
        {
            _state.IsStale = true;
            _state.RebuildQueued = false;
            _state.LastFailureReason = reason;
        }
    }

    public void EnqueueManualRebuild()
    {
        lock (_syncRoot)
        {
            _state.RebuildQueued = true;
        }
    }

    public void MarkSelfCheckAttempted()
    {
        lock (_syncRoot)
        {
            _state.SelfCheckAttempted = true;
        }
    }

    public void RecordRewriteCacheInvalidation()
    {
        _logger.LogInformation("Invalidated rewritten subtitle cache entries for stale DB generation.");
    }
}
