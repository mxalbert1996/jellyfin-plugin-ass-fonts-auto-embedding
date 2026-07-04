using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AssFontsAutoEmbedding.Services;

public sealed class DecorationLoggingHostedService : IHostedService
{
    private readonly ILogger<DecorationLoggingHostedService> _logger;
    private readonly SubtitleEncoderDecorationRegistrationReport _report;

    public DecorationLoggingHostedService(SubtitleEncoderDecorationRegistrationReport report, ILogger<DecorationLoggingHostedService> logger)
    {
        _report = report;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_report.DecorationInstalled)
        {
            _logger.LogInformation("Installed subtitle encoder decorator for ASS font embedding. Registrations: {Summary}", _report.Summary);
        }
        else
        {
            _logger.LogWarning("Subtitle encoder decoration could not be installed because no existing ISubtitleEncoder registration was available. Registrations: {Summary}", _report.Summary);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}
