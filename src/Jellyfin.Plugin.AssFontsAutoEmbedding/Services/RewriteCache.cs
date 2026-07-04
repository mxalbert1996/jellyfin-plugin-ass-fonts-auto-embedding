using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AssFontsAutoEmbedding.Services;

public sealed class RewriteCache
{
    private readonly ConcurrentDictionary<string, RewriteCacheEntry> _entries = new();
    private readonly ILogger<RewriteCache> _logger;
    private readonly PluginPaths _pluginPaths;

    public RewriteCache(PluginPaths pluginPaths, ILogger<RewriteCache> logger)
    {
        _pluginPaths = pluginPaths;
        _logger = logger;
    }

    public bool TryGetValid(RewriteCacheKey key, out RewriteCacheEntry? entry)
    {
        if (_entries.TryGetValue(key.Value, out entry) && File.Exists(entry.OutputFilePath) && new FileInfo(entry.OutputFilePath).Length > 0)
        {
            return true;
        }

        entry = null;
        return false;
    }

    public bool TryRehydrateFromDisk(RewriteCacheKey key, string expectedOutputFilePath, out RewriteCacheEntry? entry)
    {
        if (!File.Exists(expectedOutputFilePath) || new FileInfo(expectedOutputFilePath).Length == 0)
        {
            entry = null;
            return false;
        }

        entry = new RewriteCacheEntry(expectedOutputFilePath);
        _entries[key.Value] = entry;
        return true;
    }

    public void Set(RewriteCacheKey key, RewriteCacheEntry entry)
        => _entries[key.Value] = entry;

    public void InvalidateAll()
    {
        foreach (var pair in _entries)
        {
            TryDeleteEntry(pair.Key, pair.Value);
        }

        _entries.Clear();
    }

    public void ClearMemoryOnly()
        => _entries.Clear();

    public int PruneDirectoriesOlderThan(TimeSpan maxAge)
    {
        var rewriteRoot = _pluginPaths.GetRewriteRootDirectory();
        if (!Directory.Exists(rewriteRoot))
        {
            _logger.LogDebug("Rewrite cache prune skipped because root directory {RewriteRoot} does not exist.", rewriteRoot);
            return 0;
        }

        var cutoffUtc = DateTime.UtcNow - maxAge;
        var prunedCount = 0;
        foreach (var directory in Directory.EnumerateDirectories(rewriteRoot))
        {
            try
            {
                var directoryInfo = new DirectoryInfo(directory);
                var timestampUtc = GetDirectoryTimestampUtc(directoryInfo);
                if (timestampUtc >= cutoffUtc)
                {
                    continue;
                }

                if (_entries.TryRemove(directoryInfo.Name, out var entry))
                {
                    TryDeleteEntry(directoryInfo.Name, entry);
                }
                else
                {
                    TryDeleteDirectory(directoryInfo.FullName);
                }

                prunedCount++;
            }
            catch
            {
                _logger.LogWarning("Failed to prune rewrite cache directory {Directory}.", directory);
            }
        }

        _logger.LogInformation("Pruned {PrunedCount} rewrite cache directories older than {MaxAge}.", prunedCount, maxAge);
        return prunedCount;
    }

    private static DateTime GetDirectoryTimestampUtc(DirectoryInfo directoryInfo)
    {
        var timestamps = new List<DateTime> { directoryInfo.LastWriteTimeUtc, directoryInfo.CreationTimeUtc };
        try
        {
            timestamps.AddRange(directoryInfo.EnumerateFileSystemInfos("*", SearchOption.AllDirectories).Select(static info => info.LastWriteTimeUtc));
        }
        catch (Exception ex)
        {
            // Use the directory timestamps if walking children fails.
            _ = ex;
        }

        return timestamps.Where(static time => time > DateTime.UnixEpoch).DefaultIfEmpty(directoryInfo.LastWriteTimeUtc).Max();
    }

    private void TryDeleteEntry(string key, RewriteCacheEntry entry)
    {
        try
        {
            if (File.Exists(entry.OutputFilePath))
            {
                File.Delete(entry.OutputFilePath);
            }

            _pluginPaths.DeleteRewriteOutputDirectory(new RewriteCacheKey(key));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete rewrite cache entry {CacheKey} at {OutputFilePath}.", key, entry.OutputFilePath);
        }
    }

    private void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete rewrite cache directory {Directory}.", directory);
        }
    }
}
