using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.AssFontsAutoEmbedding.Services;

public sealed class FontDbFingerprintService
{
    public const string NoDbRequiredFingerprint = "no-db-required";
    private readonly ConcurrentDictionary<string, CachedFingerprint> _cache = new(StringComparer.Ordinal);
    private readonly PluginPaths _pluginPaths;

    public FontDbFingerprintService(PluginPaths pluginPaths)
    {
        _pluginPaths = pluginPaths;
    }

    public async Task<string?> GetFingerprintAsync(bool requiresFontDb, CancellationToken cancellationToken)
    {
        if (!requiresFontDb)
        {
            return NoDbRequiredFingerprint;
        }

        var fontDbFilePath = _pluginPaths.GetFontDbFilePath();
        if (!File.Exists(fontDbFilePath))
        {
            return null;
        }

        var fileInfo = new FileInfo(fontDbFilePath);
        if (_cache.TryGetValue(fontDbFilePath, out var cached)
            && cached.Length == fileInfo.Length
            && cached.LastWriteUtcTicks == fileInfo.LastWriteTimeUtc.Ticks)
        {
            return cached.Fingerprint;
        }

        await using var stream = File.OpenRead(fontDbFilePath);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        var fingerprint = Convert.ToHexString(hash).ToLowerInvariant();
        _cache[fontDbFilePath] = new CachedFingerprint(fileInfo.Length, fileInfo.LastWriteTimeUtc.Ticks, fingerprint);
        return fingerprint;
    }

    public async Task RefreshAfterManagedRebuildAsync(CancellationToken cancellationToken)
    {
        var fontDbFilePath = _pluginPaths.GetFontDbFilePath();
        if (!File.Exists(fontDbFilePath))
        {
            _cache.TryRemove(fontDbFilePath, out _);
            return;
        }

        var fileInfo = new FileInfo(fontDbFilePath);
        await using var stream = File.OpenRead(fontDbFilePath);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        _cache[fontDbFilePath] = new CachedFingerprint(fileInfo.Length, fileInfo.LastWriteTimeUtc.Ticks, Convert.ToHexString(hash).ToLowerInvariant());
    }

    public void InvalidateCachedFingerprint()
    {
        var fontDbFilePath = _pluginPaths.GetFontDbFilePath();
        _cache.TryRemove(fontDbFilePath, out _);
    }

    private sealed record CachedFingerprint(long Length, long LastWriteUtcTicks, string Fingerprint);
}
