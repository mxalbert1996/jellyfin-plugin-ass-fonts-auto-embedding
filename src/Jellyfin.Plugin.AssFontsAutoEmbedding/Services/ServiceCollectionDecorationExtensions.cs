using MediaBrowser.Controller.MediaEncoding;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.AssFontsAutoEmbedding.Services;

internal static class ServiceCollectionDecorationExtensions
{
    // Jellyfin does not expose a first-class subtitle rewrite hook, so keep the integration isolated
    // behind a narrow ISubtitleEncoder decorator that preserves the original implementation for fallback.
    public static void DecorateSubtitleEncoder(this IServiceCollection services, ServiceDescriptor originalDescriptor)
    {
        services.Add(SubtitleEncoderDecoration.CreateOriginalKeyedDescriptor(originalDescriptor));

        services.Add(CreateFactoryDescriptor(typeof(SubtitleEncoderWrapper), originalDescriptor.Lifetime, serviceProvider =>
        {
            var original = serviceProvider.GetRequiredKeyedService<ISubtitleEncoder>(SubtitleEncoderDecoration.OriginalServiceKey);
            var mediaSourceManager = serviceProvider.GetService<MediaBrowser.Controller.Library.IMediaSourceManager>();
            var attachmentFontService = serviceProvider.GetService<AttachmentFontService>();
            var pluginContext = serviceProvider.GetRequiredService<PluginContext>();
            var runtimeState = serviceProvider.GetRequiredService<PluginRuntimeState>();
            var rewriteService = serviceProvider.GetService<RewriteService>();
            var logger = serviceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SubtitleEncoderWrapper>>();
            runtimeState.MarkWrapperRegistered(true);
            return new SubtitleEncoderWrapper(original, mediaSourceManager, attachmentFontService, pluginContext, runtimeState, rewriteService, logger);
        }));

        services.Add(CreateFactoryDescriptor(typeof(ISubtitleEncoder), originalDescriptor.Lifetime, serviceProvider =>
        {
            var wrapper = serviceProvider.GetRequiredService<SubtitleEncoderWrapper>();
            var selfCheck = serviceProvider.GetRequiredService<NativeSelfCheckService>();
            return new SelfCheckedSubtitleEncoder(wrapper, selfCheck);
        }));
    }

    private static ServiceDescriptor CreateFactoryDescriptor(Type serviceType, ServiceLifetime lifetime, Func<IServiceProvider, object> factory)
        => ServiceDescriptor.Describe(serviceType, factory, lifetime);
}
