using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AssFontsAutoEmbedding.Services;

namespace Jellyfin.Plugin.AssFontsAutoEmbedding.Native;

public interface IAssfontsEngine
{
    bool IsAvailable { get; }

    string? LastFailureReason { get; }

    Task<AssfontsOperationResult> SelfCheckAsync(CancellationToken cancellationToken);

    Task<AssfontsOperationResult> BuildFontDatabaseAsync(IReadOnlyList<string> fontDirectories, string dbDirectory, CancellationToken cancellationToken);

    Task<RewriteResult> RewriteSubtitleAsync(string subtitlePath, string outputDirectory, IReadOnlyList<string> fontDirectories, string dbDirectory, CancellationToken cancellationToken);
}
