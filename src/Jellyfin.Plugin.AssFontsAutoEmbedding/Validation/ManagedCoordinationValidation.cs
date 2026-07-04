using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AssFontsAutoEmbedding.Configuration;
using Jellyfin.Plugin.AssFontsAutoEmbedding.Native;
using Jellyfin.Plugin.AssFontsAutoEmbedding.Services;
using Jellyfin.Plugin.AssFontsAutoEmbedding.State;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.AssFontsAutoEmbedding.Validation;

public static class ManagedCoordinationValidation
{
    public static void Main(string[] args)
    {
        _ = args;

        var tempRoot = Path.Combine(Path.GetTempPath(), "assfonts-managed-validation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        var subtitlePath = Path.Combine(tempRoot, "example.ass");
        File.WriteAllText(subtitlePath, "[Script Info]\nTitle: sample\n[V4+ Styles]\n[Events]\n");

        try
        {
            var pluginData = Path.Combine(tempRoot, "plugin-data");
            Directory.CreateDirectory(pluginData);

            var plugin = new Plugin(new FakeApplicationPaths(pluginData), new FakeXmlSerializer());
            plugin.UpdateConfiguration(new PluginConfiguration
            {
                Enabled = true,
                RewriteEnabled = true,
                FontDirectories = new List<string> { tempRoot, pluginData },
                NativeLogVerbosity = NativeLogVerbosity.Warn
            });

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<PluginContext>();
            services.AddSingleton<PluginRuntimeState>();
            services.AddSingleton<FontDbStateManager>();
            services.AddSingleton<PluginPaths>();
            services.AddSingleton<FontDbBuildCoordinator>();
            services.AddSingleton<NativeOperationCoordinator>();
            services.AddSingleton<RewriteCache>();
            services.AddSingleton<RewriteCacheKeyFactory>();
            services.AddSingleton<FontDbFingerprintService>();
            services.AddSingleton<RewriteWorkCoordinator>();
            services.AddSingleton(_ => new AttachmentFontService(null, null, NullLogger<AttachmentFontService>.Instance));
            services.AddSingleton<FontDbBuildService>();
            services.AddSingleton<RewriteService>();
            services.AddSingleton<IAssfontsEngine, FakeAssfontsEngine>();

            using var provider = services.BuildServiceProvider();

            var rewriteService = provider.GetRequiredService<RewriteService>();
            var rewriteCache = provider.GetRequiredService<RewriteCache>();
            var fontDbStateManager = provider.GetRequiredService<FontDbStateManager>();
            var fontDbBuildService = provider.GetRequiredService<FontDbBuildService>();
            var runtimeState = provider.GetRequiredService<PluginRuntimeState>();
            runtimeState.EnableNativeFeatures();

            var attachmentFontPath = Path.Combine(tempRoot, "embedded-font.ttf");
            File.WriteAllText(attachmentFontPath, "font-bytes");
            var attachmentFingerprint = new ResolvedAttachmentFontSet(
                new List<ResolvedAttachmentFont>
                {
                    new(1, Path.GetFileName(attachmentFontPath), attachmentFontPath, new FileInfo(attachmentFontPath).Length, new FileInfo(attachmentFontPath).LastWriteTimeUtc.Ticks)
                },
                "attachment-fingerprint-a");

            var request = CreateRequest(plugin, subtitlePath, tempRoot, pluginData, ResolvedAttachmentFontSet.Empty);
            var requestWithAttachmentFingerprint = CreateRequest(plugin, subtitlePath, tempRoot, pluginData, attachmentFingerprint);

            var eligibility = rewriteService.CheckEligibility(request);
            var first = rewriteService.TryRewriteAsync(request, CancellationToken.None).GetAwaiter().GetResult();
            var second = rewriteService.TryRewriteAsync(request, CancellationToken.None).GetAwaiter().GetResult();
            var thirdWithAttachment = rewriteService.TryRewriteAsync(requestWithAttachmentFingerprint, CancellationToken.None).GetAwaiter().GetResult();

            rewriteCache.ClearMemoryOnly();
            var restartLikeReuse = rewriteService.TryRewriteAsync(request, CancellationToken.None).GetAwaiter().GetResult();

            var secondConfiguration = new PluginConfiguration
            {
                Enabled = true,
                RewriteEnabled = true,
                FontDirectories = new List<string> { tempRoot, pluginData, Path.Combine(tempRoot, "fonts-1") },
                NativeLogVerbosity = NativeLogVerbosity.Warn
            };
            Directory.CreateDirectory(Path.Combine(tempRoot, "fonts-1"));
            fontDbStateManager.InvalidateForConfigurationChange(secondConfiguration);
            plugin.UpdateConfiguration(secondConfiguration);

            var requestAfterSecondConfiguration = CreateRequest(plugin, subtitlePath, tempRoot, pluginData, ResolvedAttachmentFontSet.Empty);

            var concurrentRewrite1 = rewriteService.TryRewriteAsync(requestAfterSecondConfiguration, CancellationToken.None);
            var concurrentRewrite2 = rewriteService.TryRewriteAsync(requestAfterSecondConfiguration, CancellationToken.None);
            var concurrentRewrite3 = rewriteService.TryRewriteAsync(requestAfterSecondConfiguration, CancellationToken.None);
            Task.WaitAll(concurrentRewrite1, concurrentRewrite2, concurrentRewrite3);

            var fourthConfiguration = new PluginConfiguration
            {
                Enabled = true,
                RewriteEnabled = true,
                FontDirectories = new List<string> { tempRoot, pluginData, Path.Combine(tempRoot, "fonts-1"), Path.Combine(tempRoot, "fonts-2"), Path.Combine(tempRoot, "fonts-3") },
                NativeLogVerbosity = NativeLogVerbosity.Warn
            };
            Directory.CreateDirectory(Path.Combine(tempRoot, "fonts-2"));
            Directory.CreateDirectory(Path.Combine(tempRoot, "fonts-3"));
            fontDbStateManager.InvalidateForConfigurationChange(fourthConfiguration);
            plugin.UpdateConfiguration(fourthConfiguration);

            var requestAfterFourthConfiguration = CreateRequest(plugin, subtitlePath, tempRoot, pluginData, ResolvedAttachmentFontSet.Empty);

            var overlapRewrite = rewriteService.TryRewriteAsync(requestAfterFourthConfiguration, CancellationToken.None);
            var overlapRebuild = fontDbBuildService.RebuildAsync(CancellationToken.None);
            Task.WaitAll(overlapRewrite, overlapRebuild);

            var sameConfigRequest1 = CreateRequest(plugin, subtitlePath, tempRoot, pluginData, ResolvedAttachmentFontSet.Empty);
            var sameConfigRequest2 = CreateRequest(plugin, subtitlePath, tempRoot, pluginData, ResolvedAttachmentFontSet.Empty);
            var sameConfigConcurrent1 = rewriteService.TryRewriteAsync(sameConfigRequest1, CancellationToken.None);
            var sameConfigConcurrent2 = rewriteService.TryRewriteAsync(sameConfigRequest2, CancellationToken.None);
            Task.WaitAll(sameConfigConcurrent1, sameConfigConcurrent2);

            var raceConfiguration = new PluginConfiguration
            {
                Enabled = true,
                RewriteEnabled = true,
                FontDirectories = new List<string> { tempRoot, pluginData, Path.Combine(tempRoot, "fonts-1"), Path.Combine(tempRoot, "fonts-2"), Path.Combine(tempRoot, "fonts-4") },
                NativeLogVerbosity = NativeLogVerbosity.Warn
            };
            Directory.CreateDirectory(Path.Combine(tempRoot, "fonts-4"));
            var raceRequestBeforeInvalidate = CreateRequest(plugin, subtitlePath, tempRoot, pluginData, ResolvedAttachmentFontSet.Empty);
            fontDbStateManager.InvalidateForConfigurationChange(raceConfiguration);
            plugin.UpdateConfiguration(raceConfiguration);
            var rewriteAfterInvalidate = rewriteService.TryRewriteAsync(raceRequestBeforeInvalidate, CancellationToken.None).GetAwaiter().GetResult();

            var thirdConfiguration = new PluginConfiguration
            {
                Enabled = true,
                RewriteEnabled = true,
                FontDirectories = new List<string> { tempRoot, pluginData, Path.Combine(tempRoot, "fonts-1"), Path.Combine(tempRoot, "fonts-2") },
                NativeLogVerbosity = NativeLogVerbosity.Warn
            };
            fontDbStateManager.InvalidateForConfigurationChange(thirdConfiguration);
            plugin.UpdateConfiguration(thirdConfiguration);

            var attachmentOnlyConfiguration = new PluginConfiguration
            {
                Enabled = true,
                RewriteEnabled = true,
                FontDirectories = new List<string>(),
                NativeLogVerbosity = NativeLogVerbosity.Warn
            };
            fontDbStateManager.InvalidateForConfigurationChange(attachmentOnlyConfiguration);
            plugin.UpdateConfiguration(attachmentOnlyConfiguration);

            var attachmentOnlyRequest = CreateRequest(plugin, subtitlePath, tempRoot, pluginData, attachmentFingerprint);
            var attachmentOnlyEligibility = rewriteService.CheckEligibility(attachmentOnlyRequest);
            var attachmentOnlyRewrite = rewriteService.TryRewriteAsync(attachmentOnlyRequest, CancellationToken.None).GetAwaiter().GetResult();

            plugin.UpdateConfiguration(thirdConfiguration);

            var fakeEngine = (FakeAssfontsEngine)provider.GetRequiredService<IAssfontsEngine>();
            var overlapBlockedBySharedCoordination = !fakeEngine.OverlapDetected
                && overlapRebuild.Result.Success;
            var dbBuild1 = fontDbBuildService.RebuildAsync(CancellationToken.None);
            var dbBuild2 = fontDbBuildService.RebuildAsync(CancellationToken.None);
            Task.WaitAll(dbBuild1, dbBuild2);
            var forcedBuild = fontDbBuildService.ForceRebuildAsync(CancellationToken.None).GetAwaiter().GetResult();
            const int expectedRewriteInvocations = 6;
            const int expectedDbRebuildInvocations = 6;
            Console.WriteLine($"Native operation overlap observed={fakeEngine.OverlapDetected}");

            Console.WriteLine($"Eligibility: {eligibility.IsEligible} reason={eligibility.Reason}");
            Console.WriteLine($"First rewrite success={first.Success} path={first.OutputFilePath}");
            Console.WriteLine($"Second rewrite success={second.Success} path={second.OutputFilePath}");
            Console.WriteLine($"Attachment fingerprint rewrite success={thirdWithAttachment.Success} path={thirdWithAttachment.OutputFilePath}");
            Console.WriteLine($"Restart-like disk cache reuse success={restartLikeReuse.Success} path={restartLikeReuse.OutputFilePath}");
            Console.WriteLine($"Fake rewrite invocations={fakeEngine.RewriteInvocationCount}");
            Console.WriteLine($"Concurrent rewrite success={concurrentRewrite1.Result.Success && concurrentRewrite2.Result.Success && concurrentRewrite3.Result.Success}");
            Console.WriteLine($"Expected rewrite invocations={expectedRewriteInvocations}");
            Console.WriteLine($"Rewrite single-flight correct={fakeEngine.RewriteInvocationCount == expectedRewriteInvocations}");
            Console.WriteLine($"Rewrite/rebuild overlap scenario blocked cleanly={overlapBlockedBySharedCoordination}");
            Console.WriteLine($"Fake DB rebuild invocations={fakeEngine.BuildDbInvocationCount}");
            Console.WriteLine($"Expected DB rebuild invocations={expectedDbRebuildInvocations}");
            Console.WriteLine($"DB rebuild sharing correct={fakeEngine.BuildDbInvocationCount == expectedDbRebuildInvocations}");
            Console.WriteLine($"Force rebuild success={forcedBuild.Success}");
            Console.WriteLine($"Concurrent rewrite output paths differ after font-dir config change={first.OutputFilePath != concurrentRewrite1.Result.OutputFilePath}");
            Console.WriteLine($"Attachment fingerprint changed cache key={first.OutputFilePath != thirdWithAttachment.OutputFilePath}");
            Console.WriteLine($"Attachment-only rewrite eligible without configured font dirs={attachmentOnlyEligibility.IsEligible}");
            Console.WriteLine($"Attachment-only rewrite success without configured font dirs={attachmentOnlyRewrite.Success}");
            Console.WriteLine($"Rewrite after stale invalidation success={rewriteAfterInvalidate.Success} path={rewriteAfterInvalidate.OutputFilePath}");
            Console.WriteLine($"Same-config concurrent rewrites shared output={string.Equals(sameConfigConcurrent1.Result.OutputFilePath, sameConfigConcurrent2.Result.OutputFilePath, StringComparison.OrdinalIgnoreCase)}");
            Console.WriteLine($"Native operation coordination correct={!fakeEngine.OverlapDetected}");

            AssertInvariant(eligibility.IsEligible, "Primary request should be eligible.");
            AssertInvariant(first.Success, "Initial rewrite should succeed.");
            AssertInvariant(second.Success, "Cached rewrite should succeed.");
            AssertInvariant(thirdWithAttachment.Success, "Attachment fingerprint rewrite should succeed.");
            AssertInvariant(restartLikeReuse.Success, "Restart-like disk cache reuse should succeed.");
            AssertInvariant(string.Equals(first.OutputFilePath, restartLikeReuse.OutputFilePath, StringComparison.OrdinalIgnoreCase), "Restart-like disk cache reuse should return the same output path.");
            AssertInvariant(concurrentRewrite1.Result.Success && concurrentRewrite2.Result.Success && concurrentRewrite3.Result.Success, "Concurrent rewrites should all succeed.");
            AssertInvariant(fakeEngine.RewriteInvocationCount == expectedRewriteInvocations, $"Rewrite single-flight failed. Expected {expectedRewriteInvocations}, got {fakeEngine.RewriteInvocationCount}.");
            AssertInvariant(overlapBlockedBySharedCoordination, "Rewrite/rebuild overlap coordination failed.");
            AssertInvariant(fakeEngine.BuildDbInvocationCount == expectedDbRebuildInvocations, $"DB rebuild sharing failed. Expected {expectedDbRebuildInvocations}, got {fakeEngine.BuildDbInvocationCount}.");
            AssertInvariant(forcedBuild.Success, "Forced DB rebuild should succeed.");
            AssertInvariant(first.OutputFilePath != concurrentRewrite1.Result.OutputFilePath, "Font-directory config change should produce a different cached output path.");
            AssertInvariant(first.OutputFilePath != thirdWithAttachment.OutputFilePath, "Attachment fingerprint should produce a different cache key/output path.");
            AssertInvariant(rewriteAfterInvalidate.Success, "Rewrite after config invalidation should rebuild or wait rather than using stale DB state.");
            AssertInvariant(first.OutputFilePath != rewriteAfterInvalidate.OutputFilePath, "Rewrite after config invalidation should not reuse the old DB-generation cache key/output path.");
            AssertInvariant(string.Equals(sameConfigConcurrent1.Result.OutputFilePath, sameConfigConcurrent2.Result.OutputFilePath, StringComparison.OrdinalIgnoreCase), "Same-config concurrent rewrites should converge on one final cache/output target.");
            AssertInvariant(attachmentOnlyEligibility.IsEligible, "Attachment-only rewrite should be eligible without configured font directories.");
            AssertInvariant(attachmentOnlyRewrite.Success, "Attachment-only rewrite should succeed without configured font directories.");
            AssertInvariant(!fakeEngine.OverlapDetected, "Native operation coordination should prevent overlap.");
        }
        finally
        {
            try
            {
                Directory.Delete(tempRoot, true);
            }
            catch
            {
            }
        }
    }

    private sealed class FakeAssfontsEngine : IAssfontsEngine
    {
        public bool IsAvailable => true;
        public string? LastFailureReason => null;
        public int BuildDbInvocationCount { get; private set; }
        public bool OverlapDetected { get; private set; }
        public int RewriteInvocationCount { get; private set; }
        private int _activeNativeOperations;

        public Task<AssfontsOperationResult> SelfCheckAsync(CancellationToken cancellationToken)
            => Task.FromResult(AssfontsOperationResult.Ok("ok"));

        public Task<AssfontsOperationResult> BuildFontDatabaseAsync(IReadOnlyList<string> fontDirectories, string dbDirectory, CancellationToken cancellationToken)
        {
            EnterNativeOperation();
            BuildDbInvocationCount++;
            Directory.CreateDirectory(dbDirectory);
            File.WriteAllText(Path.Combine(dbDirectory, "fonts.json"), string.Join("|", fontDirectories));
            ExitNativeOperation();
            return Task.FromResult(AssfontsOperationResult.Ok("db ok"));
        }

        public Task<RewriteResult> RewriteSubtitleAsync(string subtitlePath, string outputDirectory, IReadOnlyList<string> fontDirectories, string dbDirectory, CancellationToken cancellationToken)
        {
            EnterNativeOperation();
            RewriteInvocationCount++;
            Directory.CreateDirectory(outputDirectory);
            var outputPath = Path.Combine(outputDirectory, $"{Path.GetFileNameWithoutExtension(subtitlePath)}.assfonts{Path.GetExtension(subtitlePath)}");
            File.WriteAllText(outputPath, "rewritten");
            ExitNativeOperation();
            return Task.FromResult(RewriteResult.Rewritten(outputPath, "rewritten"));
        }

        private void EnterNativeOperation()
        {
            if (Interlocked.Increment(ref _activeNativeOperations) > 1)
            {
                OverlapDetected = true;
            }
        }

        private void ExitNativeOperation()
        {
            Interlocked.Decrement(ref _activeNativeOperations);
        }
    }

    private sealed class FakeApplicationPaths : IApplicationPaths
    {
        private readonly string _pluginData;

        public FakeApplicationPaths(string pluginData)
        {
            _pluginData = pluginData;
        }

        public string ProgramDataPath => _pluginData;
        public string WebPath => _pluginData;
        public string ProgramSystemPath => _pluginData;
        public string DataPath => _pluginData;
        public string ImageCachePath => _pluginData;
        public string PluginsPath => _pluginData;
        public string PluginConfigurationsPath => _pluginData;
        public string LogDirectoryPath => _pluginData;
        public string ConfigurationDirectoryPath => _pluginData;
        public string CachePath => _pluginData;
        public string TempDirectory => _pluginData;
        public string VirtualDataPath => _pluginData;
        public string UserConfigurationDirectoryPath => _pluginData;
        public string InternalMetadataPath => _pluginData;
        public string VirtualInternalMetadataPath => _pluginData;
        public string SystemConfigurationFilePath => Path.Combine(_pluginData, "system.xml");
        public string TrickplayPath => _pluginData;
        public string BackupPath => _pluginData;

        public void MakeSanityCheckOrThrow()
        {
        }

        public void CreateAndCheckMarker(string path, string markerFilename, bool writeAccessRequired)
        {
        }
    }

    private sealed class FakeXmlSerializer : IXmlSerializer
    {
        public void SerializeToFile(object obj, string file) => File.WriteAllText(file, string.Empty);
        public void SerializeToFile(object obj, string file, bool overwrite) => File.WriteAllText(file, string.Empty);
        public void SerializeToStream(object obj, Stream stream)
        {
        }
        public string SerializeToString(object obj) => string.Empty;
        public object DeserializeFromFile(Type type, string file) => Activator.CreateInstance(type)!;
        public object DeserializeFromFile(Type type, string file, bool logError) => Activator.CreateInstance(type)!;
        public object DeserializeFromStream(Type type, Stream stream) => Activator.CreateInstance(type)!;
        public object DeserializeFromString(Type type, string xml) => Activator.CreateInstance(type)!;
        public object DeserializeFromBytes(Type type, byte[] buffer) => Activator.CreateInstance(type)!;
    }

    private sealed class FakeVideo : Video
    {
    }

    private static void AssertInvariant(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static RewriteRequest CreateRequest(Plugin plugin, string subtitlePath, string tempRoot, string pluginData, ResolvedAttachmentFontSet attachmentFonts)
        => new(
            new FakeVideo { Path = Path.Combine(tempRoot, "movie.mkv") },
            new MediaSourceInfo { IsRemote = false, Protocol = MediaBrowser.Model.MediaInfo.MediaProtocol.File },
            new MediaStream
            {
                Index = 0,
                Type = MediaStreamType.Subtitle,
                IsExternal = true,
                Codec = "ass",
                Path = subtitlePath
            },
            subtitlePath,
            plugin.Configuration.FontDirectories,
            attachmentFonts,
            "ass");
}
