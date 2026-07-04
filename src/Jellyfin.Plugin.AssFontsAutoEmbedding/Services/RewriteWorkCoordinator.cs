using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.AssFontsAutoEmbedding.Services;

public sealed class RewriteWorkCoordinator
{
    private readonly ConcurrentDictionary<string, LockEntry> _locks = new();

    public Task<T> RunSingleFlightAsync<T>(string key, Func<Task<T>> action, CancellationToken cancellationToken)
    {
        var entry = _locks.AddOrUpdate(
            key,
            static _ => new LockEntry(),
            static (_, existing) =>
            {
                existing.AddReference();
                return existing;
            });

        return RunLockedAsync(key, entry, action, cancellationToken);
    }

    public async Task<T> RunSingleFlightAsync<T>(RewriteCacheKey key, Func<Task<T>> action, CancellationToken cancellationToken)
        => await RunSingleFlightAsync(key.Value, action, cancellationToken).ConfigureAwait(false);

    private async Task<T> RunLockedAsync<T>(string key, LockEntry entry, Func<Task<T>> action, CancellationToken cancellationToken)
    {
        await entry.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await action().ConfigureAwait(false);
        }
        finally
        {
            entry.Gate.Release();
            if (entry.ReleaseReference() == 0)
            {
                _locks.TryRemove(new KeyValuePair<string, LockEntry>(key, entry));
            }
        }
    }

    private sealed class LockEntry
    {
        private int _referenceCount = 1;

        public SemaphoreSlim Gate { get; } = new(1, 1);

        public void AddReference()
            => Interlocked.Increment(ref _referenceCount);

        public int ReleaseReference()
            => Interlocked.Decrement(ref _referenceCount);
    }
}
