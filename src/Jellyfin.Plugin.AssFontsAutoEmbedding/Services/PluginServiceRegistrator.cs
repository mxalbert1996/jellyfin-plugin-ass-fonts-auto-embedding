using Jellyfin.Plugin.AssFontsAutoEmbedding.Native;
using Jellyfin.Plugin.AssFontsAutoEmbedding.State;
using MediaBrowser.Controller.IO;
using MediaBrowser.Controller.MediaEncoding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller;

namespace Jellyfin.Plugin.AssFontsAutoEmbedding.Services;

public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        var decorationProbe = new SubtitleEncoderDecoratorProbe(NullLogger<SubtitleEncoderDecoratorProbe>.Instance);
        var registrationSnapshot = SubtitleEncoderDecoratorProbe.Inspect(serviceCollection);
        var originalSubtitleEncoder = decorationProbe.DetachOriginal(serviceCollection);

        serviceCollection.AddSingleton<PluginRuntimeState>();
        serviceCollection.AddSingleton<PluginContext>();
        serviceCollection.AddSingleton<FontDbStateManager>();
        serviceCollection.AddSingleton<PluginPaths>();
        serviceCollection.AddSingleton<FontDbBuildCoordinator>();
        serviceCollection.AddSingleton<NativeOperationCoordinator>();
        serviceCollection.AddSingleton<RewriteCache>();
        serviceCollection.AddSingleton<RewriteCacheKeyFactory>();
        serviceCollection.AddSingleton<FontDbFingerprintService>();
        serviceCollection.AddSingleton<RewriteWorkCoordinator>();
        serviceCollection.AddSingleton<AttachmentFontService>(serviceProvider => new AttachmentFontService(
            serviceProvider.GetService<IAttachmentExtractor>(),
            serviceProvider.GetService<IPathManager>(),
            serviceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AttachmentFontService>>()));
        serviceCollection.AddSingleton<RewriteService>();
        serviceCollection.AddSingleton<NativeLibraryLoader>();
        serviceCollection.AddSingleton<IAssfontsEngine, AssfontsNativeEngine>();
        serviceCollection.AddSingleton<NativeSelfCheckService>();
        serviceCollection.AddSingleton<FontDbBuildService>();
        serviceCollection.AddSingleton<Tasks.RebuildFontDbTask>();
        serviceCollection.AddSingleton<Tasks.PruneRewriteCacheTask>();
        serviceCollection.AddSingleton(new SubtitleEncoderDecorationRegistrationReport
        {
            OriginalRegistrationFound = registrationSnapshot.FoundOriginal,
            DecorationInstalled = originalSubtitleEncoder is not null,
            Summary = registrationSnapshot.Summary
        });

        if (originalSubtitleEncoder is not null)
        {
            serviceCollection.DecorateSubtitleEncoder(originalSubtitleEncoder);
        }

        serviceCollection.AddSingleton<IHostedService, ConfigurationChangeHostedService>();
        serviceCollection.AddSingleton<IHostedService, DecorationLoggingHostedService>();
    }
}
