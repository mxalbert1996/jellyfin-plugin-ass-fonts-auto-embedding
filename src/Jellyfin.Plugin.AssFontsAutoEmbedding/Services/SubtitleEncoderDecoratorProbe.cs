using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Controller.MediaEncoding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AssFontsAutoEmbedding.Services;

public sealed class SubtitleEncoderDecoratorProbe
{
    private readonly ILogger<SubtitleEncoderDecoratorProbe> _logger;

    public SubtitleEncoderDecoratorProbe(ILogger<SubtitleEncoderDecoratorProbe> logger)
    {
        _logger = logger;
    }

    public void Capture(IServiceCollection services)
    {
        var subtitleDescriptors = services.Where(static d => d.ServiceType == typeof(ISubtitleEncoder)).ToList();
        if (subtitleDescriptors.Count == 0)
        {
            _logger.LogWarning("No existing ISubtitleEncoder registration was present for decoration.");
            return;
        }

        foreach (var descriptor in subtitleDescriptors)
        {
            _logger.LogInformation("Observed ISubtitleEncoder registration candidate: Lifetime={Lifetime}, ImplType={ImplType}, HasFactory={HasFactory}, HasInstance={HasInstance}",
                descriptor.Lifetime,
                descriptor.ImplementationType?.FullName ?? "<factory-or-instance>",
                descriptor.ImplementationFactory is not null,
                descriptor.ImplementationInstance is not null);
        }
    }

    public ServiceDescriptor? DetachOriginal(IServiceCollection services)
    {
        for (var i = services.Count - 1; i >= 0; i--)
        {
            var descriptor = services[i];
            if (descriptor.ServiceType != typeof(ISubtitleEncoder))
            {
                continue;
            }

            services.RemoveAt(i);
            _logger.LogInformation("Detached original ISubtitleEncoder registration for wrapper decoration.");
            return descriptor;
        }

        _logger.LogWarning("Unable to detach original ISubtitleEncoder registration for wrapper decoration. The plugin will remain installed but runtime rewrite interception cannot activate.");
        return null;
    }

    public sealed record Snapshot(int Count, bool FoundOriginal, string Summary);

    public static Snapshot Inspect(IServiceCollection services)
    {
        var descriptors = services.Where(static d => d.ServiceType == typeof(ISubtitleEncoder)).ToList();
        var summary = string.Join(" | ", descriptors.Select(d => $"{d.Lifetime}:{d.ImplementationType?.FullName ?? (d.ImplementationFactory is not null ? "factory" : "instance")}"));
        return new Snapshot(descriptors.Count, descriptors.Count > 0, summary);
    }
}
