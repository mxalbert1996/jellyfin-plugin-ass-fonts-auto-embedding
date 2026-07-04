using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AssFontsAutoEmbedding.Native;
using Jellyfin.Plugin.AssFontsAutoEmbedding.State;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using MediaBrowser.Model.MediaInfo;

namespace Jellyfin.Plugin.AssFontsAutoEmbedding.Services;

public sealed class RewriteService
{
    private readonly IAssfontsEngine _assfontsEngine;
    private readonly AttachmentFontService _attachmentFontService;
    private readonly FontDbFingerprintService _fontDbFingerprintService;
    private readonly FontDbBuildService _fontDbBuildService;
    private readonly FontDbStateManager _fontDbStateManager;
    private readonly NativeOperationCoordinator _nativeOperationCoordinator;
    private readonly PluginPaths _pluginPaths;
    private readonly RewriteCache _rewriteCache;
    private readonly RewriteCacheKeyFactory _rewriteCacheKeyFactory;
    private readonly RewriteWorkCoordinator _rewriteWorkCoordinator;
    private readonly ILogger<RewriteService> _logger;

    public RewriteService(
        IAssfontsEngine assfontsEngine,
        AttachmentFontService attachmentFontService,
        FontDbFingerprintService fontDbFingerprintService,
        FontDbBuildService fontDbBuildService,
        FontDbStateManager fontDbStateManager,
        NativeOperationCoordinator nativeOperationCoordinator,
        PluginPaths pluginPaths,
        RewriteCache rewriteCache,
        RewriteCacheKeyFactory rewriteCacheKeyFactory,
        RewriteWorkCoordinator rewriteWorkCoordinator,
        ILogger<RewriteService> logger)
    {
        _assfontsEngine = assfontsEngine;
        _attachmentFontService = attachmentFontService;
        _fontDbFingerprintService = fontDbFingerprintService;
        _fontDbBuildService = fontDbBuildService;
        _fontDbStateManager = fontDbStateManager;
        _nativeOperationCoordinator = nativeOperationCoordinator;
        _pluginPaths = pluginPaths;
        _rewriteCache = rewriteCache;
        _rewriteCacheKeyFactory = rewriteCacheKeyFactory;
        _rewriteWorkCoordinator = rewriteWorkCoordinator;
        _logger = logger;
    }

    public RewriteEligibilityResult CheckEligibility(RewriteRequest request)
    {
        if (!request.OutputFormat.Equals("ass", StringComparison.OrdinalIgnoreCase))
        {
            return RewriteEligibilityResult.Ineligible("Only ASS subtitle output is handled.");
        }

        if (request.SubtitleStream.IsExternalUrl == true)
        {
            return RewriteEligibilityResult.Ineligible("External URL subtitle streams are not supported.");
        }

        if (request.SubtitleStream.Type != MediaStreamType.Subtitle)
        {
            return RewriteEligibilityResult.Ineligible("Only subtitle streams are supported.");
        }

        if (!IsAssCodec(request.SubtitleStream.Codec, request.ResolvedSubtitlePath))
        {
            return RewriteEligibilityResult.Ineligible("Only ASS/SSA subtitle files are supported.");
        }

        if (request.MediaSource.IsRemote || request.MediaSource.Protocol != MediaProtocol.File)
        {
            return RewriteEligibilityResult.Ineligible("Only local file-based media sources are supported.");
        }

        if (string.IsNullOrWhiteSpace(request.ResolvedSubtitlePath) || !Path.IsPathRooted(request.ResolvedSubtitlePath))
        {
            return RewriteEligibilityResult.Ineligible("Resolved subtitle path is missing or not a rooted local path.");
        }

        if (!File.Exists(request.ResolvedSubtitlePath))
        {
            return RewriteEligibilityResult.Ineligible("Resolved subtitle path does not exist.");
        }

        var hasConfiguredFonts = request.FontDirectories.Count > 0;
        var hasAttachmentFonts = request.AttachmentFonts.Fonts.Count > 0;
        if (!hasConfiguredFonts && !hasAttachmentFonts)
        {
            return RewriteEligibilityResult.Ineligible("No configured font directories or attachment fonts are available for rewrite.");
        }

        var invalidFontDirectories = request.FontDirectories.Where(static path => !Directory.Exists(path)).ToArray();
        if (invalidFontDirectories.Length > 0)
        {
            return RewriteEligibilityResult.Ineligible($"Configured font directories do not exist: {string.Join(", ", invalidFontDirectories)}");
        }

        return RewriteEligibilityResult.Eligible();
    }

    public async Task<RewriteResult> TryRewriteAsync(RewriteRequest request, CancellationToken cancellationToken)
    {
        var eligibility = CheckEligibility(request);
        if (!eligibility.IsEligible)
        {
            return RewriteResult.Skipped(eligibility.Reason);
        }

        var hasConfiguredFontDirectories = request.FontDirectories.Count > 0;
        var initialDbFingerprintResult = await EnsureCurrentDbFingerprintAsync(hasConfiguredFontDirectories, cancellationToken).ConfigureAwait(false);
        if (!initialDbFingerprintResult.Success)
        {
            return RewriteResult.Skipped(initialDbFingerprintResult.FailureReason!);
        }

        var subtitlePath = request.ResolvedSubtitlePath;
        var coordinationKey = _rewriteCacheKeyFactory.Create(request, initialDbFingerprintResult.Fingerprint!);
        var stableCoordinationKey = _rewriteCacheKeyFactory.CreateStableCoordinationKey(request);
        if (TryGetCachedRewriteResult(coordinationKey, subtitlePath, out var cachedResult))
        {
            return cachedResult;
        }

        return await _rewriteWorkCoordinator.RunSingleFlightAsync(stableCoordinationKey, async () =>
        {
            var currentDbFingerprintResult = await EnsureCurrentDbFingerprintAsync(hasConfiguredFontDirectories, cancellationToken).ConfigureAwait(false);
            if (!currentDbFingerprintResult.Success)
            {
                return RewriteResult.Skipped(currentDbFingerprintResult.FailureReason!);
            }

            var activeCacheKey = coordinationKey;
            if (!string.Equals(currentDbFingerprintResult.Fingerprint, initialDbFingerprintResult.Fingerprint, StringComparison.Ordinal))
            {
                activeCacheKey = _rewriteCacheKeyFactory.Create(request, currentDbFingerprintResult.Fingerprint!);
                _logger.LogDebug("Rewrite cache key changed after DB freshness check for subtitle {SubtitlePath}; using refreshed DB fingerprint.", subtitlePath);
            }

            if (TryGetCachedRewriteResult(activeCacheKey, subtitlePath, out var warmCachedResult))
            {
                return warmCachedResult;
            }

            var rewriteDirectory = _pluginPaths.GetRewriteOutputDirectory(activeCacheKey);
            TryDeleteRewriteDirectory(activeCacheKey);
            Directory.CreateDirectory(rewriteDirectory);

            try
            {
                var effectiveFontDirectories = new List<string>();
                var stagedAttachmentDirectory = _attachmentFontService.StageIntoRewriteDirectory(request.AttachmentFonts, rewriteDirectory);
                if (!string.IsNullOrWhiteSpace(stagedAttachmentDirectory))
                {
                    effectiveFontDirectories.Add(stagedAttachmentDirectory);
                }

                var outputPath = await _nativeOperationCoordinator.RunExclusiveAsync(
                    ct => _assfontsEngine.RewriteSubtitleAsync(
                        subtitlePath,
                        rewriteDirectory,
                        effectiveFontDirectories,
                        EnsureRewriteDbDirectory(hasConfiguredFontDirectories),
                        ct),
                    cancellationToken).ConfigureAwait(false);

                if (!outputPath.Success || string.IsNullOrWhiteSpace(outputPath.OutputFilePath) || !File.Exists(outputPath.OutputFilePath) || new FileInfo(outputPath.OutputFilePath).Length == 0)
                {
                    TryDeleteRewriteDirectory(activeCacheKey);
                    return RewriteResult.Skipped(outputPath.Message);
                }

                TryDeleteRewriteArtifacts(rewriteDirectory, subtitlePath, outputPath.OutputFilePath);
                _rewriteCache.Set(activeCacheKey, new RewriteCacheEntry(outputPath.OutputFilePath));
                return RewriteResult.Rewritten(outputPath.OutputFilePath, outputPath.Message);
            }
            catch
            {
                TryDeleteRewriteDirectory(activeCacheKey);
                throw;
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    private bool TryGetCachedRewriteResult(RewriteCacheKey cacheKey, string subtitlePath, out RewriteResult result)
    {
        if (_rewriteCache.TryGetValid(cacheKey, out var cachedEntry))
        {
            result = RewriteResult.Rewritten(cachedEntry!.OutputFilePath, "Using cached rewritten subtitle output.");
            return true;
        }

        var expectedCachedOutputPath = _pluginPaths.GetRewriteOutputFilePath(cacheKey, subtitlePath);
        if (_rewriteCache.TryRehydrateFromDisk(cacheKey, expectedCachedOutputPath, out var diskEntry))
        {
            result = RewriteResult.Rewritten(diskEntry!.OutputFilePath, "Using persisted rewritten subtitle output from disk cache.");
            return true;
        }

        result = RewriteResult.Skipped(string.Empty);
        return false;
    }

    private async Task<DbFingerprintResult> EnsureCurrentDbFingerprintAsync(bool hasConfiguredFontDirectories, CancellationToken cancellationToken)
    {
        if (hasConfiguredFontDirectories)
        {
            var dbState = _fontDbStateManager.Snapshot();
            if (dbState.IsStale || dbState.RebuildQueued)
            {
                var rebuildResult = await _fontDbBuildService.RebuildAsync(cancellationToken).ConfigureAwait(false);
                if (!rebuildResult.Success)
                {
                    return DbFingerprintResult.Failure($"Font DB rebuild failed before rewrite: {rebuildResult.Message}");
                }
            }
        }

        var dbFingerprint = await _fontDbFingerprintService.GetFingerprintAsync(hasConfiguredFontDirectories, cancellationToken).ConfigureAwait(false);
        return dbFingerprint is null
            ? DbFingerprintResult.Failure("Font DB fingerprint is unavailable for cache-safe rewrite reuse.")
            : DbFingerprintResult.Succeeded(dbFingerprint);
    }

    private void TryDeleteRewriteDirectory(RewriteCacheKey cacheKey)
    {
        try
        {
            _pluginPaths.DeleteRewriteOutputDirectory(cacheKey);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to clean partial rewrite directory for cache key {CacheKey}.", cacheKey.Value);
        }
    }

    private void TryDeleteRewriteArtifacts(string rewriteDirectory, string subtitlePath, string finalOutputPath)
    {
        TryDeleteDirectory(Path.Combine(rewriteDirectory, "attachments"));
        TryDeleteDirectory(Path.Combine(rewriteDirectory, Path.GetFileNameWithoutExtension(subtitlePath) + "_subsetted"));

        foreach (var file in Directory.EnumerateFiles(rewriteDirectory))
        {
            if (string.Equals(file, finalOutputPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            TryDeleteFile(file);
        }
    }

    private void TryDeleteDirectory(string directoryPath)
    {
        try
        {
            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to delete rewrite artifact directory {DirectoryPath}.", directoryPath);
        }
    }

    private void TryDeleteFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to delete rewrite artifact file {FilePath}.", filePath);
        }
    }

    private string EnsureRewriteDbDirectory(bool shouldRequireFontDb)
    {
        var dbDirectory = _pluginPaths.GetFontDbDirectory();
        if (!shouldRequireFontDb && !Directory.Exists(dbDirectory))
        {
            Directory.CreateDirectory(dbDirectory);
        }

        return dbDirectory;
    }

    private static bool IsAssCodec(string? codec, string? path)
    {
        if (!string.IsNullOrWhiteSpace(codec) && (codec.Equals("ass", StringComparison.OrdinalIgnoreCase) || codec.Equals("ssa", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var extension = Path.GetExtension(path ?? string.Empty);
        return extension.Equals(".ass", StringComparison.OrdinalIgnoreCase) || extension.Equals(".ssa", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record DbFingerprintResult(bool Success, string? Fingerprint, string? FailureReason)
    {
        public static DbFingerprintResult Succeeded(string fingerprint)
            => new(true, fingerprint, null);

        public static DbFingerprintResult Failure(string reason)
            => new(false, null, reason);
    }
}
