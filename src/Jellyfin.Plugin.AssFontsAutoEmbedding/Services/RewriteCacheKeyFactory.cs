using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Jellyfin.Plugin.AssFontsAutoEmbedding.Services;

public sealed class RewriteCacheKeyFactory
{
    public RewriteCacheKey Create(RewriteRequest request, string dbFingerprint)
    {
        var raw = BuildRawKeyMaterial(CreateInput(request), dbFingerprint);
        return new RewriteCacheKey(Hash(raw));
    }

    public string CreateStableCoordinationKey(RewriteRequest request)
        => Hash(BuildRawKeyMaterial(CreateInput(request), dbFingerprint: null));

    private static RewriteKeyInput CreateInput(RewriteRequest request)
    {
        var fileInfo = new FileInfo(request.ResolvedSubtitlePath);
        return new RewriteKeyInput(
            request.ResolvedSubtitlePath,
            fileInfo.Exists ? fileInfo.Length.ToString(CultureInfo.InvariantCulture) : "missing",
            fileInfo.Exists ? fileInfo.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture) : "missing",
            request.AttachmentFonts.Fingerprint,
            request.OutputFormat);
    }

    private static string BuildRawKeyMaterial(RewriteKeyInput input, string? dbFingerprint)
    {
        var segments = dbFingerprint is null
            ? new[] { input.SubtitlePath, input.SubtitleLength, input.SubtitleLastWriteUtcTicks, input.AttachmentFingerprint, input.OutputFormat }
            : new[] { input.SubtitlePath, input.SubtitleLength, input.SubtitleLastWriteUtcTicks, dbFingerprint, input.AttachmentFingerprint, input.OutputFormat };

        return string.Join("|", segments);
    }

    private static string Hash(string raw)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();

    private sealed record RewriteKeyInput(
        string SubtitlePath,
        string SubtitleLength,
        string SubtitleLastWriteUtcTicks,
        string AttachmentFingerprint,
        string OutputFormat);
}
