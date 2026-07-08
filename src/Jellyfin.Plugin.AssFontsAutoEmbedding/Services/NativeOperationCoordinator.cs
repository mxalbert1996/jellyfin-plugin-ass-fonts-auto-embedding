using System;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.AssFontsAutoEmbedding.Services;

public sealed class NativeOperationCoordinator
{
    private readonly SemaphoreSlim _turnstile = new(1, 1);
    private readonly SemaphoreSlim _roomEmpty = new(1, 1);
    private readonly SemaphoreSlim _readersMutex = new(1, 1);
    private int _readerCount;

    public async Task<T> RunExclusiveAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        await _turnstile.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _roomEmpty.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await action(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _roomEmpty.Release();
            }
        }
        finally
        {
            _turnstile.Release();
        }
    }

    public async Task<T> RunSharedAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        await _turnstile.WaitAsync(cancellationToken).ConfigureAwait(false);
        _turnstile.Release();

        var joinedReaders = false;
        await _readersMutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var firstReader = _readerCount == 0;
            _readerCount++;

            try
            {
                if (firstReader)
                {
                    await _roomEmpty.WaitAsync(cancellationToken).ConfigureAwait(false);
                }

                joinedReaders = true;
            }
            catch
            {
                _readerCount--;
                throw;
            }
        }
        finally
        {
            _readersMutex.Release();
        }

        try
        {
            return await action(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (joinedReaders)
            {
                await _readersMutex.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                try
                {
                    _readerCount--;
                    if (_readerCount == 0)
                    {
                        _roomEmpty.Release();
                    }
                }
                finally
                {
                    _readersMutex.Release();
                }
            }
        }
    }
}
