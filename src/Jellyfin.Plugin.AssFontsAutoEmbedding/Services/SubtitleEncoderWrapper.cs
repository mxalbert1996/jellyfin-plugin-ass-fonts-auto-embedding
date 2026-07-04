using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AssFontsAutoEmbedding.Services;

public sealed class SubtitleEncoderWrapper : ISubtitleEncoder
{
    private readonly ISubtitleEncoder _inner;
    private readonly IMediaSourceManager? _mediaSourceManager;
    private readonly AttachmentFontService? _attachmentFontService;
    private readonly PluginContext _pluginContext;
    private readonly PluginRuntimeState _runtimeState;
    private readonly RewriteService? _rewriteService;
    private readonly ILogger<SubtitleEncoderWrapper> _logger;

    public SubtitleEncoderWrapper(ISubtitleEncoder inner, IMediaSourceManager? mediaSourceManager, AttachmentFontService? attachmentFontService, PluginContext pluginContext, PluginRuntimeState runtimeState, RewriteService? rewriteService, ILogger<SubtitleEncoderWrapper> logger)
    {
        _inner = inner;
        _mediaSourceManager = mediaSourceManager;
        _attachmentFontService = attachmentFontService;
        _pluginContext = pluginContext;
        _runtimeState = runtimeState;
        _rewriteService = rewriteService;
        _logger = logger;
    }

    public Task<string> GetSubtitleFileCharacterSet(MediaStream subtitleStream, string language, MediaSourceInfo mediaSource, CancellationToken cancellationToken)
    {
        _runtimeState.MarkWrapperInvocation();
        return _inner.GetSubtitleFileCharacterSet(subtitleStream, language, mediaSource, cancellationToken);
    }

    public Task<string> GetSubtitleFilePath(MediaStream subtitleStream, MediaSourceInfo mediaSource, CancellationToken cancellationToken)
    {
        _runtimeState.MarkWrapperInvocation();
        return _inner.GetSubtitleFilePath(subtitleStream, mediaSource, cancellationToken);
    }

    public Task ExtractAllExtractableSubtitles(MediaSourceInfo mediaSource, CancellationToken cancellationToken)
    {
        _runtimeState.MarkWrapperInvocation();
        return _inner.ExtractAllExtractableSubtitles(mediaSource, cancellationToken);
    }

    public async Task<Stream> GetSubtitles(BaseItem item, string mediaSourceId, int subtitleStreamIndex, string outputFormat, long startPositionTicks, long endPositionTicks, bool preserveOriginalSubtitleFormat, CancellationToken cancellationToken)
    {
        _runtimeState.MarkWrapperInvocation();

        Task<Stream> DelegateToCoreAsync()
            => _inner.GetSubtitles(item, mediaSourceId, subtitleStreamIndex, outputFormat, startPositionTicks, endPositionTicks, preserveOriginalSubtitleFormat, cancellationToken);

        async Task<Stream> LogAndDelegateToCoreAsync(string message, params object?[] args)
        {
            _logger.LogDebug(message, args);
            return await DelegateToCoreAsync().ConfigureAwait(false);
        }

        // Jellyfin applies start/end clipping and timestamp shifting only on subtitle conversion
        // paths. For ASS/SSA passthrough requests, the core encoder returns the original stream
        // unchanged, so this wrapper intentionally mirrors that behavior by rewriting the
        // resolved ASS/SSA source file directly and returning the rewritten file.

        if (!ShouldIntercept(outputFormat))
        {
            return await DelegateToCoreAsync().ConfigureAwait(false);
        }

        var configuration = _pluginContext.GetConfiguration();
        if (!configuration.Enabled || !configuration.RewriteEnabled || !_runtimeState.NativeFeaturesEnabled)
        {
            return await LogAndDelegateToCoreAsync("ASS rewrite wrapper inactive; delegating to core subtitle encoder. Reason: {Reason}", _runtimeState.DisableReason ?? "unknown").ConfigureAwait(false);
        }

        if (_mediaSourceManager is null || _rewriteService is null)
        {
            return await LogAndDelegateToCoreAsync("ASS rewrite infrastructure is unavailable in the current DI graph; delegating to core subtitle encoder.").ConfigureAwait(false);
        }

        try
        {
            var mediaSource = await _mediaSourceManager.GetMediaSource(item, mediaSourceId, string.Empty, true, cancellationToken).ConfigureAwait(false);
            var subtitleStream = FindSubtitleStream(mediaSource, subtitleStreamIndex);
            if (subtitleStream is null)
            {
                return await LogAndDelegateToCoreAsync("Subtitle stream {SubtitleStreamIndex} not found on media source {MediaSourceId}; delegating to core encoder.", subtitleStreamIndex, mediaSourceId).ConfigureAwait(false);
            }

            var resolvedSubtitlePath = await ResolveReadableSubtitlePathAsync(subtitleStream, mediaSource, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(resolvedSubtitlePath))
            {
                return await LogAndDelegateToCoreAsync("Resolved subtitle path was empty for item {ItemId}; delegating to core encoder.", item.Id).ConfigureAwait(false);
            }

            var attachmentFonts = ResolvedAttachmentFontSet.Empty;
            if (!subtitleStream.IsExternal)
            {
                if (_attachmentFontService is null)
                {
                    return await LogAndDelegateToCoreAsync("Attachment font helper is unavailable for internal ASS stream on item {ItemId}; delegating to core encoder.", item.Id).ConfigureAwait(false);
                }

                var attachmentPreparation = await _attachmentFontService.ResolveAsync(mediaSource, cancellationToken).ConfigureAwait(false);
                if (!attachmentPreparation.Success)
                {
                    return await LogAndDelegateToCoreAsync("Attachment font preparation failed for item {ItemId}; delegating to core encoder. Reason: {Reason}", item.Id, attachmentPreparation.FailureReason).ConfigureAwait(false);
                }

                attachmentFonts = attachmentPreparation.FontSet;
            }

            var rewriteRequest = new RewriteRequest(item, mediaSource, subtitleStream, resolvedSubtitlePath, _pluginContext.GetNormalizedFontDirectories(), attachmentFonts, outputFormat);
            var rewriteResult = await _rewriteService.TryRewriteAsync(rewriteRequest, cancellationToken).ConfigureAwait(false);
            if (rewriteResult.Success && rewriteResult.OutputFilePath is not null)
            {
                _logger.LogInformation("Returning rewritten ASS subtitle for item {ItemId} from {OutputFilePath}.", item.Id, rewriteResult.OutputFilePath);
                return File.OpenRead(rewriteResult.OutputFilePath);
            }

            _logger.LogDebug("ASS rewrite skipped for item {ItemId}: {Reason}", item.Id, rewriteResult.Message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ASS rewrite failed for item {ItemId}; delegating to core encoder.", item.Id);
        }

        return await DelegateToCoreAsync().ConfigureAwait(false);
    }

    private static bool ShouldIntercept(string outputFormat)
        => outputFormat.Equals("ass", System.StringComparison.OrdinalIgnoreCase);

    private static MediaStream? FindSubtitleStream(MediaSourceInfo mediaSource, int subtitleStreamIndex)
        => mediaSource.MediaStreams?.FirstOrDefault(stream => stream.Type == MediaStreamType.Subtitle && stream.Index == subtitleStreamIndex);

    private Task<string> ResolveReadableSubtitlePathAsync(MediaStream subtitleStream, MediaSourceInfo mediaSource, CancellationToken cancellationToken)
        => subtitleStream.IsExternal
            ? Task.FromResult(subtitleStream.Path ?? string.Empty)
            : _inner.GetSubtitleFilePath(subtitleStream, mediaSource, cancellationToken);
}
