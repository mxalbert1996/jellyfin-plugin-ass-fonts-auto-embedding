using System;
using MediaBrowser.Controller.MediaEncoding;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.AssFontsAutoEmbedding.Services;

internal static class SubtitleEncoderDecoration
{
    internal static readonly object OriginalServiceKey = new();

    public static ServiceDescriptor CreateOriginalKeyedDescriptor(ServiceDescriptor originalDescriptor)
    {
        if (originalDescriptor.ImplementationInstance is ISubtitleEncoder instance)
        {
            return new ServiceDescriptor(typeof(ISubtitleEncoder), OriginalServiceKey, instance);
        }

        if (originalDescriptor.ImplementationFactory is not null)
        {
            return new ServiceDescriptor(typeof(ISubtitleEncoder), OriginalServiceKey, (serviceProvider, _) => originalDescriptor.ImplementationFactory(serviceProvider), originalDescriptor.Lifetime);
        }

        if (originalDescriptor.ImplementationType is not null)
        {
            return new ServiceDescriptor(typeof(ISubtitleEncoder), OriginalServiceKey, originalDescriptor.ImplementationType, originalDescriptor.Lifetime);
        }

        throw new InvalidOperationException("Unsupported ISubtitleEncoder service descriptor shape.");
    }
}
