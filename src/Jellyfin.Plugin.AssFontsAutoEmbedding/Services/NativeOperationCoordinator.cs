using System;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.AssFontsAutoEmbedding.Services;

public sealed class NativeOperationCoordinator
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<T> RunExclusiveAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await action(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }
}
