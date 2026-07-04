using System.Collections.Generic;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.AssFontsAutoEmbedding.Services;

public sealed record RewriteRequest(
    BaseItem Item,
    MediaSourceInfo MediaSource,
    MediaStream SubtitleStream,
    string ResolvedSubtitlePath,
    IReadOnlyList<string> FontDirectories,
    ResolvedAttachmentFontSet AttachmentFonts,
    string OutputFormat);
