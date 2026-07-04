using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AssFontsAutoEmbedding.Native;
using Jellyfin.Plugin.AssFontsAutoEmbedding.State;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AssFontsAutoEmbedding.Services;

public sealed class NativeSelfCheckService
{
    private readonly IAssfontsEngine _engine;
    private readonly FontDbStateManager _stateManager;
    private readonly PluginRuntimeState _runtimeState;
    private readonly ILogger<NativeSelfCheckService> _logger;

    public NativeSelfCheckService(IAssfontsEngine engine, FontDbStateManager stateManager, PluginRuntimeState runtimeState, ILogger<NativeSelfCheckService> logger)
    {
        _engine = engine;
        _stateManager = stateManager;
        _runtimeState = runtimeState;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var result = await _engine.SelfCheckAsync(cancellationToken).ConfigureAwait(false);
        if (result.Success)
        {
            _runtimeState.EnableNativeFeatures();
            _logger.LogInformation("assfonts self-check succeeded: {Message}", result.Message);
            return;
        }

        _stateManager.MarkRebuildFailed(result.Message);
        _runtimeState.DisableNativeFeatures(result.Message);
        _logger.LogWarning("assfonts self-check disabled native features: {Message}", result.Message);
    }
}
