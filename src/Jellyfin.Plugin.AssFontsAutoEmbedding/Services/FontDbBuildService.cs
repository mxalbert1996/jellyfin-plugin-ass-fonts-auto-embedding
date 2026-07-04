using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AssFontsAutoEmbedding.Native;
using Jellyfin.Plugin.AssFontsAutoEmbedding.State;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AssFontsAutoEmbedding.Services;

public sealed class FontDbBuildService
{
    private readonly FontDbBuildCoordinator _buildCoordinator;
    private readonly IAssfontsEngine _engine;
    private readonly NativeOperationCoordinator _nativeOperationCoordinator;
    private readonly PluginContext _pluginContext;
    private readonly FontDbFingerprintService _fontDbFingerprintService;
    private readonly PluginPaths _pluginPaths;
    private readonly FontDbStateManager _stateManager;
    private readonly PluginRuntimeState _runtimeState;
    private readonly ILogger<FontDbBuildService> _logger;

    public FontDbBuildService(FontDbBuildCoordinator buildCoordinator, IAssfontsEngine engine, NativeOperationCoordinator nativeOperationCoordinator, PluginContext pluginContext, FontDbFingerprintService fontDbFingerprintService, PluginPaths pluginPaths, FontDbStateManager stateManager, PluginRuntimeState runtimeState, ILogger<FontDbBuildService> logger)
    {
        _buildCoordinator = buildCoordinator;
        _engine = engine;
        _nativeOperationCoordinator = nativeOperationCoordinator;
        _pluginContext = pluginContext;
        _fontDbFingerprintService = fontDbFingerprintService;
        _pluginPaths = pluginPaths;
        _stateManager = stateManager;
        _runtimeState = runtimeState;
        _logger = logger;
    }

    public async Task<AssfontsOperationResult> RebuildAsync(CancellationToken cancellationToken)
        => await _buildCoordinator.RunSingleFlightAsync(ct => RebuildCoreAsync(ct, force: false), cancellationToken).ConfigureAwait(false);

    public async Task<AssfontsOperationResult> ForceRebuildAsync(CancellationToken cancellationToken)
        => await _buildCoordinator.RunSingleFlightAsync(ct => RebuildCoreAsync(ct, force: true), cancellationToken).ConfigureAwait(false);

    private async Task<AssfontsOperationResult> RebuildCoreAsync(CancellationToken cancellationToken, bool force = false)
    {
        var configuration = _pluginContext.GetConfiguration();
        var state = _stateManager.Snapshot();

        if (!configuration.Enabled)
        {
            return AssfontsOperationResult.Fail("Plugin is disabled.");
        }

        if (!_runtimeState.NativeFeaturesEnabled)
        {
            return AssfontsOperationResult.Fail(_runtimeState.DisableReason ?? "Native features are disabled.");
        }

        var configuredFontDirectories = _pluginContext.GetNormalizedFontDirectories();

        if (configuredFontDirectories.Length == 0)
        {
            const string reason = "No font directories are configured.";
            _stateManager.MarkRebuildFailed(reason);
            _logger.LogWarning("Font DB rebuild skipped because no font directories are configured.");
            return AssfontsOperationResult.Fail(reason);
        }

        var missingDirectories = configuredFontDirectories.Where(static path => !Directory.Exists(path)).ToArray();
        if (missingDirectories.Length > 0)
        {
            var reason = $"Configured font directories do not exist: {string.Join(", ", missingDirectories)}";
            _stateManager.MarkRebuildFailed(reason);
            _logger.LogWarning("Font DB rebuild skipped because some configured font directories do not exist: {Directories}", missingDirectories);
            return AssfontsOperationResult.Fail(reason);
        }

        if (!force && !state.IsStale && !state.RebuildQueued)
        {
            _logger.LogDebug("Font DB rebuild skipped because the DB is already current.");
            return AssfontsOperationResult.Ok("Font DB is already current.");
        }

        var dbDirectory = _pluginPaths.GetFontDbDirectory();
        _stateManager.MarkRebuildStarted();

        var result = await _nativeOperationCoordinator.RunExclusiveAsync(
            ct => _engine.BuildFontDatabaseAsync(configuredFontDirectories, dbDirectory, ct),
            cancellationToken).ConfigureAwait(false);
        if (result.Success)
        {
            _stateManager.MarkRebuildSucceeded();
            try
            {
                await _fontDbFingerprintService.RefreshAfterManagedRebuildAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (System.Exception ex)
            {
                _fontDbFingerprintService.InvalidateCachedFingerprint();
                _logger.LogWarning(ex, "Font DB rebuild succeeded, but fingerprint cache refresh failed; falling back to metadata-based refresh on next read.");
            }

            _logger.LogInformation("Font DB rebuild completed successfully in {DbDirectory}. Force={Force}", dbDirectory, force);
        }
        else
        {
            _fontDbFingerprintService.InvalidateCachedFingerprint();
            _stateManager.MarkRebuildFailed(result.Message);
            _logger.LogWarning("Font DB rebuild failed: {Reason}", result.Message);
        }

        return result;
    }
}
