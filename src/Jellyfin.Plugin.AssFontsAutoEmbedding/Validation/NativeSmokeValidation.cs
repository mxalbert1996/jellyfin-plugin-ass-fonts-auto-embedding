using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Jellyfin.Plugin.AssFontsAutoEmbedding.Native;
using Jellyfin.Plugin.AssFontsAutoEmbedding.Services;
using Jellyfin.Plugin.AssFontsAutoEmbedding.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.AssFontsAutoEmbedding.Validation;

public static class NativeSmokeValidation
{
    public static int Main(string[] args)
    {
        _ = args;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<PluginRuntimeState>();
        services.AddSingleton<FontDbStateManager>();
        services.AddSingleton<NativeLibraryLoader>();
        services.AddSingleton<IAssfontsEngine, AssfontsNativeEngine>();
        services.AddSingleton<NativeSelfCheckService>();
        services.AddSingleton<PluginContext>();

        services.AddSingleton<MediaBrowser.Controller.MediaEncoding.ISubtitleEncoder, FakeSubtitleEncoder>();

        var probe = new SubtitleEncoderDecoratorProbe(NullLogger<SubtitleEncoderDecoratorProbe>.Instance);
        var before = SubtitleEncoderDecoratorProbe.Inspect(services);
        var original = probe.DetachOriginal(services);
        var decorated = original is not null;
        if (original is not null)
        {
            services.DecorateSubtitleEncoder(original);
        }

        var publicEncoderRegistrations = services.Count(static descriptor => descriptor.ServiceType == typeof(MediaBrowser.Controller.MediaEncoding.ISubtitleEncoder) && !descriptor.IsKeyedService);
        var keyedEncoderRegistrations = services.Count(static descriptor => descriptor.ServiceType == typeof(MediaBrowser.Controller.MediaEncoding.ISubtitleEncoder) && descriptor.IsKeyedService);

        using var provider = services.BuildServiceProvider();
        var encoder = provider.GetRequiredService<MediaBrowser.Controller.MediaEncoding.ISubtitleEncoder>();
        var originalEncoder = provider.GetRequiredKeyedService<MediaBrowser.Controller.MediaEncoding.ISubtitleEncoder>(SubtitleEncoderDecoration.OriginalServiceKey);
        var runtime = provider.GetRequiredService<PluginRuntimeState>();
        var engine = provider.GetRequiredService<IAssfontsEngine>();
        var stateManager = provider.GetRequiredService<FontDbStateManager>();
        var loader = provider.GetRequiredService<NativeLibraryLoader>();

        var fakeMediaSource = new MediaSourceInfo();
        var fakeStream = new MediaStream();
        encoder.GetSubtitleFilePath(fakeStream, fakeMediaSource, CancellationToken.None).GetAwaiter().GetResult();
        var state = stateManager.Snapshot();

        Console.WriteLine($"Decorator probe: before={before.Count}, detached={decorated}, finalType={encoder.GetType().FullName}, wrapperRegistered={runtime.WrapperRegistered}");
        Console.WriteLine($"Registration shape: public={publicEncoderRegistrations}, keyedOriginal={keyedEncoderRegistrations}, keyedType={originalEncoder.GetType().FullName}");
        Console.WriteLine($"Wrapper invocation observed={runtime.WrapperInvocationObserved}");
        Console.WriteLine($"Native path={loader.ResolveLibraryPath()}");
        Console.WriteLine($"Native available={engine.IsAvailable}, selfCheckAttempted={state.SelfCheckAttempted}, lastFailure={engine.LastFailureReason ?? "<none>"}");
        Console.WriteLine("Self-check note: this validation calls AssfontsBuildDB with one isolated temporary font directory instead of the guaranteed-invalid zero-font-dir path. On macOS this still cannot prove Linux .so execution.");

        if (publicEncoderRegistrations != 1)
        {
            throw new InvalidOperationException($"Expected exactly one public ISubtitleEncoder registration after decoration, got {publicEncoderRegistrations}.");
        }

        if (keyedEncoderRegistrations != 1)
        {
            throw new InvalidOperationException($"Expected exactly one keyed original ISubtitleEncoder registration after decoration, got {keyedEncoderRegistrations}.");
        }

        if (!decorated)
        {
            throw new InvalidOperationException("Expected subtitle encoder decoration to occur.");
        }

        if (encoder is not SelfCheckedSubtitleEncoder)
        {
            throw new InvalidOperationException($"Expected resolved public encoder to be {typeof(SelfCheckedSubtitleEncoder).FullName}, got {encoder.GetType().FullName}.");
        }

        if (originalEncoder is not FakeSubtitleEncoder)
        {
            throw new InvalidOperationException($"Expected keyed original encoder to be {typeof(FakeSubtitleEncoder).FullName}, got {originalEncoder.GetType().FullName}.");
        }

        if (!runtime.WrapperRegistered)
        {
            throw new InvalidOperationException("Expected wrapper registration flag to be true.");
        }

        if (!runtime.WrapperInvocationObserved)
        {
            throw new InvalidOperationException("Expected wrapper invocation flag to be true after calling GetSubtitleFilePath.");
        }

        return 0;
    }

    private sealed class FakeSubtitleEncoder : MediaBrowser.Controller.MediaEncoding.ISubtitleEncoder
    {
        public System.Threading.Tasks.Task ExtractAllExtractableSubtitles(MediaBrowser.Model.Dto.MediaSourceInfo mediaSource, CancellationToken cancellationToken)
            => System.Threading.Tasks.Task.CompletedTask;

        public System.Threading.Tasks.Task<string> GetSubtitleFileCharacterSet(MediaBrowser.Model.Entities.MediaStream subtitleStream, string language, MediaBrowser.Model.Dto.MediaSourceInfo mediaSource, CancellationToken cancellationToken)
            => System.Threading.Tasks.Task.FromResult("utf-8");

        public System.Threading.Tasks.Task<string> GetSubtitleFilePath(MediaBrowser.Model.Entities.MediaStream subtitleStream, MediaBrowser.Model.Dto.MediaSourceInfo mediaSource, CancellationToken cancellationToken)
            => System.Threading.Tasks.Task.FromResult(string.Empty);

        public System.Threading.Tasks.Task<Stream> GetSubtitles(MediaBrowser.Controller.Entities.BaseItem item, string mediaSourceId, int subtitleStreamIndex, string outputFormat, long startPositionTicks, long endPositionTicks, bool preserveOriginalSubtitleFormat, CancellationToken cancellationToken)
            => System.Threading.Tasks.Task.FromResult<Stream>(Stream.Null);
    }

}
