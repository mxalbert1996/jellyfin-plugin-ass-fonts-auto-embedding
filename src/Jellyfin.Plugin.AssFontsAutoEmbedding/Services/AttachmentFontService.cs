using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.IO;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AssFontsAutoEmbedding.Services;

public sealed class AttachmentFontService
{
    private static readonly string[] SupportedFontExtensions = [".ttf", ".otf", ".ttc", ".otc", ".woff", ".woff2"];

    private readonly IAttachmentExtractor? _attachmentExtractor;
    private readonly ConcurrentDictionary<string, ExtractionLockEntry> _extractionLocks = new(StringComparer.Ordinal);
    private readonly IPathManager? _pathManager;
    private readonly ILogger<AttachmentFontService> _logger;

    public AttachmentFontService(IAttachmentExtractor? attachmentExtractor, IPathManager? pathManager, ILogger<AttachmentFontService> logger)
    {
        _attachmentExtractor = attachmentExtractor;
        _pathManager = pathManager;
        _logger = logger;
    }

    public async Task<AttachmentFontPreparationResult> ResolveAsync(MediaSourceInfo mediaSource, CancellationToken cancellationToken)
    {
        var fontAttachments = mediaSource.MediaAttachments?
            .Where(IsFontAttachment)
            .OrderBy(static attachment => attachment.Index)
            .ThenBy(static attachment => attachment.FileName, StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];

        if (fontAttachments.Length == 0)
        {
            return AttachmentFontPreparationResult.Succeeded(ResolvedAttachmentFontSet.Empty);
        }

        if (_attachmentExtractor is null || _pathManager is null)
        {
            return AttachmentFontPreparationResult.Failure("Jellyfin attachment services are unavailable for internal ASS font preparation.");
        }

        var mediaSourceId = mediaSource.Id;
        if (string.IsNullOrWhiteSpace(mediaSourceId))
        {
            return AttachmentFontPreparationResult.Failure("Media source id is unavailable for attachment cache resolution.");
        }

        if (fontAttachments.Any(static attachment => string.IsNullOrWhiteSpace(attachment.FileName)))
        {
            return AttachmentFontPreparationResult.Failure("One or more font attachments do not expose a file name.");
        }

        if (fontAttachments.Any(attachment => !TryResolveExistingCachePath(mediaSourceId, attachment, out _)))
        {
            var extractionResult = await EnsureAttachmentsExtractedAsync(mediaSourceId, mediaSource, fontAttachments, cancellationToken).ConfigureAwait(false);
            if (!extractionResult.Success)
            {
                return extractionResult;
            }
        }

        var resolvedFonts = new List<ResolvedAttachmentFont>(fontAttachments.Length);
        foreach (var attachment in fontAttachments)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!TryResolveExistingCachePath(mediaSourceId, attachment, out var cachePath))
            {
                return AttachmentFontPreparationResult.Failure($"Jellyfin attachment cache path is missing for font attachment '{attachment.FileName}'.");
            }

            var fileInfo = new FileInfo(cachePath!);
            resolvedFonts.Add(new ResolvedAttachmentFont(
                attachment.Index,
                attachment.FileName!,
                cachePath!,
                fileInfo.Length,
                fileInfo.LastWriteTimeUtc.Ticks));
        }

        return AttachmentFontPreparationResult.Succeeded(new ResolvedAttachmentFontSet(resolvedFonts, CreateFingerprint(resolvedFonts)));
    }

    private async Task<AttachmentFontPreparationResult> EnsureAttachmentsExtractedAsync(string mediaSourceId, MediaSourceInfo mediaSource, IReadOnlyList<MediaAttachment> fontAttachments, CancellationToken cancellationToken)
    {
        var lockEntry = AcquireExtractionLock(mediaSourceId);
        await lockEntry.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (fontAttachments.All(attachment => TryResolveExistingCachePath(mediaSourceId, attachment, out _)))
            {
                return AttachmentFontPreparationResult.Succeeded(ResolvedAttachmentFontSet.Empty);
            }

            if (string.IsNullOrWhiteSpace(mediaSource.Path) || !Path.IsPathRooted(mediaSource.Path) || !File.Exists(mediaSource.Path))
            {
                return AttachmentFontPreparationResult.Failure("Media source path is unavailable for Jellyfin attachment extraction.");
            }

            _logger.LogDebug("Extracting Jellyfin-managed attachments for media source {MediaSourceId} before ASS rewrite.", mediaSourceId);
            await _attachmentExtractor!.ExtractAllAttachments(mediaSource.Path, mediaSource, cancellationToken).ConfigureAwait(false);

            return fontAttachments.All(attachment => TryResolveExistingCachePath(mediaSourceId, attachment, out _))
                ? AttachmentFontPreparationResult.Succeeded(ResolvedAttachmentFontSet.Empty)
                : AttachmentFontPreparationResult.Failure("Jellyfin attachment extraction completed but one or more font attachments were still missing from cache.");
        }
        finally
        {
            lockEntry.Gate.Release();
            ReleaseExtractionLock(mediaSourceId, lockEntry);
        }
    }

    private ExtractionLockEntry AcquireExtractionLock(string mediaSourceId)
    {
        while (true)
        {
            var entry = _extractionLocks.GetOrAdd(mediaSourceId, static _ => new ExtractionLockEntry());
            entry.AddRef();

            if (ReferenceEquals(entry, _extractionLocks.GetOrAdd(mediaSourceId, entry)))
            {
                return entry;
            }

            entry.ReleaseRef();
        }
    }

    private void ReleaseExtractionLock(string mediaSourceId, ExtractionLockEntry entry)
    {
        if (!entry.ReleaseRef())
        {
            return;
        }

        if (_extractionLocks.TryRemove(new KeyValuePair<string, ExtractionLockEntry>(mediaSourceId, entry)))
        {
            entry.Dispose();
        }
    }

    public string? StageIntoRewriteDirectory(ResolvedAttachmentFontSet fontSet, string rewriteDirectory)
    {
        if (fontSet.Fonts.Count == 0)
        {
            return null;
        }

        var attachmentsDirectory = Path.Combine(rewriteDirectory, "attachments");
        Directory.CreateDirectory(attachmentsDirectory);

        foreach (var font in fontSet.Fonts)
        {
            var safeFileName = Path.GetFileName(font.FileName);
            if (string.IsNullOrWhiteSpace(safeFileName))
            {
                safeFileName = $"attachment-{font.Index.ToString(CultureInfo.InvariantCulture)}{Path.GetExtension(font.SourcePath)}";
            }

            var stagedPath = Path.Combine(attachmentsDirectory, $"{font.Index.ToString("D4", CultureInfo.InvariantCulture)}-{safeFileName}");
            File.Copy(font.SourcePath, stagedPath, true);
        }

        return attachmentsDirectory;
    }

    private bool TryResolveExistingCachePath(string mediaSourceId, MediaAttachment attachment, out string? cachePath)
    {
        cachePath = _pathManager?.GetAttachmentPath(mediaSourceId, attachment.FileName!);
        return !string.IsNullOrWhiteSpace(cachePath) && File.Exists(cachePath);
    }

    private static bool IsFontAttachment(MediaAttachment attachment)
    {
        var extension = Path.GetExtension(attachment.FileName ?? string.Empty);
        if (SupportedFontExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        var mimeType = attachment.MimeType ?? string.Empty;
        return mimeType.StartsWith("font/", StringComparison.OrdinalIgnoreCase)
            || mimeType.Equals("application/font-sfnt", StringComparison.OrdinalIgnoreCase)
            || mimeType.Equals("application/x-font-ttf", StringComparison.OrdinalIgnoreCase)
            || mimeType.Equals("application/x-font-opentype", StringComparison.OrdinalIgnoreCase)
            || mimeType.Equals("application/vnd.ms-opentype", StringComparison.OrdinalIgnoreCase)
            || mimeType.Equals("application/x-truetype-font", StringComparison.OrdinalIgnoreCase)
            || mimeType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase) && SupportedFontExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    private static string CreateFingerprint(IReadOnlyList<ResolvedAttachmentFont> fonts)
    {
        if (fonts.Count == 0)
        {
            return "none";
        }

        var raw = string.Join("|", fonts.Select(font => string.Join(";", new[]
        {
            font.Index.ToString(CultureInfo.InvariantCulture),
            font.FileName,
            font.Length.ToString(CultureInfo.InvariantCulture),
            font.LastWriteUtcTicks.ToString(CultureInfo.InvariantCulture)
        })));

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
    }

    private sealed class ExtractionLockEntry : IDisposable
    {
        private int _referenceCount;

        public SemaphoreSlim Gate { get; } = new(1, 1);

        public void AddRef()
            => Interlocked.Increment(ref _referenceCount);

        public bool ReleaseRef()
            => Interlocked.Decrement(ref _referenceCount) == 0;

        public void Dispose()
            => Gate.Dispose();
    }
}

public sealed record ResolvedAttachmentFont(int Index, string FileName, string SourcePath, long Length, long LastWriteUtcTicks);

public sealed record ResolvedAttachmentFontSet(IReadOnlyList<ResolvedAttachmentFont> Fonts, string Fingerprint)
{
    public static ResolvedAttachmentFontSet Empty { get; } = new(Array.Empty<ResolvedAttachmentFont>(), "none");
}

public sealed record AttachmentFontPreparationResult(bool Success, ResolvedAttachmentFontSet FontSet, string? FailureReason)
{
    public static AttachmentFontPreparationResult Succeeded(ResolvedAttachmentFontSet fontSet)
        => new(true, fontSet, null);

    public static AttachmentFontPreparationResult Failure(string reason)
        => new(false, ResolvedAttachmentFontSet.Empty, reason);
}
