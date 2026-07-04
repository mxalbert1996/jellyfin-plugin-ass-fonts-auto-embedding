using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AssFontsAutoEmbedding.Configuration;
using Jellyfin.Plugin.AssFontsAutoEmbedding.State;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.AssFontsAutoEmbedding.Services;

public sealed class ConfigurationChangeHostedService : IHostedService
{
    private readonly FontDbBuildService _fontDbBuildService;
    private readonly RewriteCache _rewriteCache;
    private readonly FontDbStateManager _stateManager;
    private readonly ILogger<ConfigurationChangeHostedService> _logger;

    public ConfigurationChangeHostedService(FontDbBuildService fontDbBuildService, RewriteCache rewriteCache, FontDbStateManager stateManager, ILogger<ConfigurationChangeHostedService> logger)
    {
        _fontDbBuildService = fontDbBuildService;
        _rewriteCache = rewriteCache;
        _stateManager = stateManager;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var plugin = Plugin.Instance;
        if (plugin is null)
        {
            _logger.LogWarning("Plugin instance unavailable; configuration change monitoring not attached.");
            return Task.CompletedTask;
        }

        plugin.ConfigurationChanged += OnConfigurationChanged;
        Apply(plugin.Configuration);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (Plugin.Instance is not null)
        {
            Plugin.Instance.ConfigurationChanged -= OnConfigurationChanged;
        }

        return Task.CompletedTask;
    }

    private void OnConfigurationChanged(object? sender, BasePluginConfiguration configuration)
    {
        if (configuration is PluginConfiguration typed)
        {
            Apply(typed);
        }
    }

    private void Apply(PluginConfiguration configuration)
    {
        if (_stateManager.InvalidateForConfigurationChange(configuration))
        {
            _rewriteCache.InvalidateAll();
            _stateManager.RecordRewriteCacheInvalidation();
            _logger.LogInformation("Cleared rewritten subtitle cache and queued font DB rebuild due to configuration change.");
            _ = TriggerRebuildAsync();
        }
    }

    private async Task TriggerRebuildAsync()
    {
        try
        {
            var result = await _fontDbBuildService.RebuildAsync(CancellationToken.None).ConfigureAwait(false);
            if (result.Success)
            {
                _logger.LogInformation("Triggered font DB rebuild after configuration change: {Message}", result.Message);
            }
            else
            {
                _logger.LogWarning("Triggered font DB rebuild after configuration change failed: {Message}", result.Message);
            }
        }
        catch (System.Exception ex)
        {
            _logger.LogWarning(ex, "Triggered font DB rebuild after configuration change threw an exception.");
        }
    }
}
