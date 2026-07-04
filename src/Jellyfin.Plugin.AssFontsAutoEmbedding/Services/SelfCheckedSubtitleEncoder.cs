using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.AssFontsAutoEmbedding.Services;

public sealed class SelfCheckedSubtitleEncoder : ISubtitleEncoder
{
    private readonly ISubtitleEncoder _inner;
    private readonly Lazy<Task> _selfCheckTask;

    public SelfCheckedSubtitleEncoder(ISubtitleEncoder inner, NativeSelfCheckService nativeSelfCheckService)
    {
        _inner = inner;
        _selfCheckTask = new Lazy<Task>(() => nativeSelfCheckService.RunAsync(CancellationToken.None), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public async Task<string> GetSubtitleFileCharacterSet(MediaStream subtitleStream, string language, MediaSourceInfo mediaSource, CancellationToken cancellationToken)
    {
        await EnsureSelfCheckAsync().ConfigureAwait(false);
        return await _inner.GetSubtitleFileCharacterSet(subtitleStream, language, mediaSource, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> GetSubtitleFilePath(MediaStream subtitleStream, MediaSourceInfo mediaSource, CancellationToken cancellationToken)
    {
        await EnsureSelfCheckAsync().ConfigureAwait(false);
        return await _inner.GetSubtitleFilePath(subtitleStream, mediaSource, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Stream> GetSubtitles(BaseItem item, string mediaSourceId, int subtitleStreamIndex, string outputFormat, long startPositionTicks, long endPositionTicks, bool preserveOriginalSubtitleFormat, CancellationToken cancellationToken)
    {
        await EnsureSelfCheckAsync().ConfigureAwait(false);
        return await _inner.GetSubtitles(item, mediaSourceId, subtitleStreamIndex, outputFormat, startPositionTicks, endPositionTicks, preserveOriginalSubtitleFormat, cancellationToken).ConfigureAwait(false);
    }

    public async Task ExtractAllExtractableSubtitles(MediaSourceInfo mediaSource, CancellationToken cancellationToken)
    {
        await EnsureSelfCheckAsync().ConfigureAwait(false);
        await _inner.ExtractAllExtractableSubtitles(mediaSource, cancellationToken).ConfigureAwait(false);
    }

    private Task EnsureSelfCheckAsync()
        => _selfCheckTask.Value;
}
