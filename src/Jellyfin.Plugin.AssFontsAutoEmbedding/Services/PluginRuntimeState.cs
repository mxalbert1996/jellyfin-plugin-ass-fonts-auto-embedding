using System;
using Jellyfin.Plugin.AssFontsAutoEmbedding.Native;

namespace Jellyfin.Plugin.AssFontsAutoEmbedding.Services;

public sealed class PluginRuntimeState
{
    public bool NativeFeaturesEnabled { get; private set; } = true;

    public bool WrapperRegistered { get; private set; }

    public bool WrapperInvocationObserved { get; private set; }

    public bool RewritePathEnabled => NativeFeaturesEnabled && WrapperInvocationObserved;

    public string? DisableReason { get; private set; }

    public void DisableNativeFeatures(string reason)
    {
        NativeFeaturesEnabled = false;
        DisableReason = reason;
    }

    public void DisableNativeFeatures(AssfontsOperationResult result)
    {
        if (!result.Success)
        {
            DisableNativeFeatures(result.Message);
        }
    }

    public void EnableNativeFeatures()
    {
        NativeFeaturesEnabled = true;
        DisableReason = null;
    }

    public void MarkWrapperRegistered(bool registered)
    {
        WrapperRegistered = registered;
    }

    public void MarkWrapperInvocation()
    {
        WrapperInvocationObserved = true;
    }
}
