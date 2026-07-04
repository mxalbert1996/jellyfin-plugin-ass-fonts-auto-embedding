using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AssFontsAutoEmbedding.Native;

namespace Jellyfin.Plugin.AssFontsAutoEmbedding.Services;

public sealed class FontDbBuildCoordinator
{
    private readonly object _syncRoot = new();
    private int _activeCallers;
    private Task<AssfontsOperationResult>? _inFlight;

    public async Task<AssfontsOperationResult> RunSingleFlightAsync(Func<CancellationToken, Task<AssfontsOperationResult>> action, CancellationToken cancellationToken)
    {
        Task<AssfontsOperationResult> taskToAwait;

        lock (_syncRoot)
        {
            _activeCallers++;
            _inFlight ??= action(CancellationToken.None);
            taskToAwait = _inFlight;
        }

        try
        {
            return await taskToAwait.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (_syncRoot)
            {
                _activeCallers--;
                if (_activeCallers == 0 && ReferenceEquals(_inFlight, taskToAwait) && taskToAwait.IsCompleted)
                {
                    _inFlight = null;
                }
            }
        }
    }
}
