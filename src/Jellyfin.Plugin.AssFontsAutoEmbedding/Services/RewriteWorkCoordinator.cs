using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.AssFontsAutoEmbedding.Services;

public sealed class RewriteWorkCoordinator
{
    private readonly Dictionary<string, LockEntry> _locks = new(StringComparer.Ordinal);
    private readonly object _syncRoot = new();

    public Task<T> RunSingleFlightAsync<T>(string key, Func<Task<T>> action, CancellationToken cancellationToken)
    {
        LockEntry entry;
        lock (_syncRoot)
        {
            if (!_locks.TryGetValue(key, out entry!))
            {
                entry = new LockEntry();
                _locks.Add(key, entry);
            }
            else
            {
                entry.ReferenceCount++;
            }
        }

        return RunLockedAsync(key, entry, action, cancellationToken);
    }

    public async Task<T> RunSingleFlightAsync<T>(RewriteCacheKey key, Func<Task<T>> action, CancellationToken cancellationToken)
        => await RunSingleFlightAsync(key.Value, action, cancellationToken).ConfigureAwait(false);

    private async Task<T> RunLockedAsync<T>(string key, LockEntry entry, Func<Task<T>> action, CancellationToken cancellationToken)
    {
        var enteredGate = false;
        try
        {
            await entry.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            enteredGate = true;
            return await action().ConfigureAwait(false);
        }
        finally
        {
            if (enteredGate)
            {
                entry.Gate.Release();
            }

            lock (_syncRoot)
            {
                entry.ReferenceCount--;
                if (entry.ReferenceCount == 0
                    && _locks.TryGetValue(key, out var current)
                    && ReferenceEquals(current, entry))
                {
                    _locks.Remove(key);
                }
            }
        }
    }

    private sealed class LockEntry
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);

        public int ReferenceCount { get; set; } = 1;
    }
}
