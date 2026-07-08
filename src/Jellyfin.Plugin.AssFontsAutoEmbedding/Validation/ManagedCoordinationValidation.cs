using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
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
        var alternateSubtitlePath = Path.Combine(tempRoot, "example-alt.ass");
        File.WriteAllText(alternateSubtitlePath, "[Script Info]\nTitle: sample-alt\n[V4+ Styles]\n[Events]\n");
        var thirdSubtitlePath = Path.Combine(tempRoot, "example-third.ass");
        File.WriteAllText(thirdSubtitlePath, "[Script Info]\nTitle: sample-third\n[V4+ Styles]\n[Events]\n");

        try
        {
            var pluginData = Path.Combine(tempRoot, "plugin-data");
            Directory.CreateDirectory(pluginData);

            var plugin = new Plugin(new FakeApplicationPaths(pluginData), new FakeXmlSerializer());
            plugin.UpdateConfiguration(new PluginConfiguration
            {
                Enabled = true,
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
            var nativeOperationCoordinator = provider.GetRequiredService<NativeOperationCoordinator>();
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

            var alternateRequestAfterSecondConfiguration = CreateRequest(plugin, alternateSubtitlePath, tempRoot, pluginData, ResolvedAttachmentFontSet.Empty);
            var thirdRequestAfterSecondConfiguration = CreateRequest(plugin, thirdSubtitlePath, tempRoot, pluginData, ResolvedAttachmentFontSet.Empty);
            var distinctConcurrentRewrite1 = rewriteService.TryRewriteAsync(alternateRequestAfterSecondConfiguration, CancellationToken.None);
            var distinctConcurrentRewrite2 = rewriteService.TryRewriteAsync(thirdRequestAfterSecondConfiguration, CancellationToken.None);
            Task.WaitAll(distinctConcurrentRewrite1, distinctConcurrentRewrite2);

            var fourthConfiguration = new PluginConfiguration
            {
                Enabled = true,
                FontDirectories = new List<string> { tempRoot, pluginData, Path.Combine(tempRoot, "fonts-1"), Path.Combine(tempRoot, "fonts-2"), Path.Combine(tempRoot, "fonts-3") },
                NativeLogVerbosity = NativeLogVerbosity.Warn
            };
            Directory.CreateDirectory(Path.Combine(tempRoot, "fonts-2"));
            Directory.CreateDirectory(Path.Combine(tempRoot, "fonts-3"));
            fontDbStateManager.InvalidateForConfigurationChange(fourthConfiguration);
            plugin.UpdateConfiguration(fourthConfiguration);

            var requestAfterFourthConfiguration = CreateRequest(plugin, subtitlePath, tempRoot, pluginData, ResolvedAttachmentFontSet.Empty);

            var sharedRewriteStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseSharedRewrite = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var sharedRewrite = nativeOperationCoordinator.RunSharedAsync(async _ =>
            {
                sharedRewriteStarted.SetResult(true);
                await releaseSharedRewrite.Task.ConfigureAwait(false);
                return true;
            }, CancellationToken.None);
            sharedRewriteStarted.Task.GetAwaiter().GetResult();

            var exclusiveRebuildStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseExclusiveRebuild = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var exclusiveRebuild = nativeOperationCoordinator.RunExclusiveAsync(async _ =>
            {
                exclusiveRebuildStarted.SetResult(true);
                await releaseExclusiveRebuild.Task.ConfigureAwait(false);
                return true;
            }, CancellationToken.None);
            var sharedRewriteBlockedExclusive = !exclusiveRebuildStarted.Task.Wait(TimeSpan.FromMilliseconds(100));
            releaseSharedRewrite.SetResult(true);
            sharedRewrite.GetAwaiter().GetResult();
            var exclusiveRebuildStartedAfterSharedRewrite = exclusiveRebuildStarted.Task.Wait(TimeSpan.FromSeconds(1));
            releaseExclusiveRebuild.SetResult(true);
            exclusiveRebuild.GetAwaiter().GetResult();

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
                FontDirectories = new List<string> { tempRoot, pluginData, Path.Combine(tempRoot, "fonts-1"), Path.Combine(tempRoot, "fonts-2") },
                NativeLogVerbosity = NativeLogVerbosity.Warn
            };
            fontDbStateManager.InvalidateForConfigurationChange(thirdConfiguration);
            plugin.UpdateConfiguration(thirdConfiguration);

            var attachmentOnlyConfiguration = new PluginConfiguration
            {
                Enabled = true,
                FontDirectories = new List<string>(),
                NativeLogVerbosity = NativeLogVerbosity.Warn
            };
            fontDbStateManager.InvalidateForConfigurationChange(attachmentOnlyConfiguration);
            plugin.UpdateConfiguration(attachmentOnlyConfiguration);

            var attachmentOnlyRequest = CreateRequest(plugin, subtitlePath, tempRoot, pluginData, attachmentFingerprint);
            var attachmentOnlyEligibility = rewriteService.CheckEligibility(attachmentOnlyRequest);
            var attachmentOnlyRewrite = rewriteService.TryRewriteAsync(attachmentOnlyRequest, CancellationToken.None).GetAwaiter().GetResult();

            var roomEmpty = GetPrivateField<SemaphoreSlim>(nativeOperationCoordinator, "_roomEmpty");
            roomEmpty.Wait();

            using var canceledSharedReaderTokenSource = new CancellationTokenSource();
            var canceledSharedReader = nativeOperationCoordinator.RunSharedAsync(_ => Task.FromResult(true), canceledSharedReaderTokenSource.Token);
            var canceledSharedReaderEnteredRollbackPath = SpinWait.SpinUntil(
                () => GetPrivateField<int>(nativeOperationCoordinator, "_readerCount") == 1,
                TimeSpan.FromSeconds(1));

            canceledSharedReaderTokenSource.Cancel();
            var canceledSharedReaderObserved = false;
            try
            {
                canceledSharedReader.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                canceledSharedReaderObserved = true;
            }

            var canceledSharedReaderRolledBackReaderCount = SpinWait.SpinUntil(
                () => GetPrivateField<int>(nativeOperationCoordinator, "_readerCount") == 0,
                TimeSpan.FromSeconds(1));
            roomEmpty.Release();

            var secondWriterStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseSecondWriter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondWriter = nativeOperationCoordinator.RunExclusiveAsync(async _ =>
            {
                secondWriterStarted.SetResult(true);
                await releaseSecondWriter.Task.ConfigureAwait(false);
                return true;
            }, CancellationToken.None);
            secondWriterStarted.Task.GetAwaiter().GetResult();

            var secondReaderStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondReader = nativeOperationCoordinator.RunSharedAsync(_ =>
            {
                secondReaderStarted.SetResult(true);
                return Task.FromResult(true);
            }, CancellationToken.None);
            var canceledSharedReaderRollbackWorked = !secondReaderStarted.Task.Wait(TimeSpan.FromMilliseconds(100));
            releaseSecondWriter.SetResult(true);
            secondWriter.GetAwaiter().GetResult();
            secondReader.GetAwaiter().GetResult();

            plugin.UpdateConfiguration(thirdConfiguration);

            using var canceledRewriteWaiterTokenSource = new CancellationTokenSource();
            var rewriteWaiterCoordinator = provider.GetRequiredService<RewriteWorkCoordinator>();
            var cancelledWorkKey = "cancelled-work-key";
            var releaseRewriteGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var holdingRewriteGate = rewriteWaiterCoordinator.RunSingleFlightAsync(cancelledWorkKey, async () =>
            {
                await releaseRewriteGate.Task.ConfigureAwait(false);
                return true;
            }, CancellationToken.None);

            var rewriteGateHeld = SpinWait.SpinUntil(
                () => PrivateConcurrentDictionaryContainsKey(rewriteWaiterCoordinator, "_locks", cancelledWorkKey),
                TimeSpan.FromSeconds(1));
            var canceledRewriteWaiter = rewriteWaiterCoordinator.RunSingleFlightAsync(cancelledWorkKey, () => Task.FromResult(true), canceledRewriteWaiterTokenSource.Token);
            canceledRewriteWaiterTokenSource.Cancel();
            var canceledRewriteWaiterObserved = false;
            try
            {
                canceledRewriteWaiter.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                canceledRewriteWaiterObserved = true;
            }

            releaseRewriteGate.SetResult(true);
            holdingRewriteGate.GetAwaiter().GetResult();

            var canceledRewriteWaiterReleasedReference = !PrivateConcurrentDictionaryContainsKey(rewriteWaiterCoordinator, "_locks", cancelledWorkKey);

            var fakeEngine = (FakeAssfontsEngine)provider.GetRequiredService<IAssfontsEngine>();
            var overlapBlockedBySharedCoordination = !fakeEngine.BuildRewriteOverlapDetected
                && overlapRebuild.Result.Success;
            var dbBuild1 = fontDbBuildService.RebuildAsync(CancellationToken.None);
            var dbBuild2 = fontDbBuildService.RebuildAsync(CancellationToken.None);
            Task.WaitAll(dbBuild1, dbBuild2);
            var forcedBuild = fontDbBuildService.ForceRebuildAsync(CancellationToken.None).GetAwaiter().GetResult();
            const int expectedRewriteInvocations = 8;
            const int expectedDbRebuildInvocations = 6;
            Console.WriteLine($"Concurrent rewrite overlap observed={fakeEngine.ConcurrentRewriteOverlapObserved}");
            Console.WriteLine($"Build/rewrite overlap detected={fakeEngine.BuildRewriteOverlapDetected}");

            Console.WriteLine($"Eligibility: {eligibility.IsEligible} reason={eligibility.Reason}");
            Console.WriteLine($"First rewrite success={first.Success} path={first.OutputFilePath}");
            Console.WriteLine($"Second rewrite success={second.Success} path={second.OutputFilePath}");
            Console.WriteLine($"Attachment fingerprint rewrite success={thirdWithAttachment.Success} path={thirdWithAttachment.OutputFilePath}");
            Console.WriteLine($"Restart-like disk cache reuse success={restartLikeReuse.Success} path={restartLikeReuse.OutputFilePath}");
            Console.WriteLine($"Fake rewrite invocations={fakeEngine.RewriteInvocationCount}");
            Console.WriteLine($"Concurrent rewrite success={concurrentRewrite1.Result.Success && concurrentRewrite2.Result.Success && concurrentRewrite3.Result.Success}");
            Console.WriteLine($"Expected rewrite invocations={expectedRewriteInvocations}");
            Console.WriteLine($"Rewrite single-flight correct={fakeEngine.RewriteInvocationCount == expectedRewriteInvocations}");
            Console.WriteLine($"Shared rewrite blocked exclusive rebuild={sharedRewriteBlockedExclusive}");
            Console.WriteLine($"Exclusive rebuild started after shared rewrite={exclusiveRebuildStartedAfterSharedRewrite}");
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
            Console.WriteLine($"Canceled shared reader entered rollback path={canceledSharedReaderEnteredRollbackPath}");
            Console.WriteLine($"Distinct concurrent rewrites success={distinctConcurrentRewrite1.Result.Success && distinctConcurrentRewrite2.Result.Success}");
            Console.WriteLine($"Canceled shared reader observed={canceledSharedReaderObserved}");
            Console.WriteLine($"Canceled shared reader rollback worked={canceledSharedReaderRolledBackReaderCount && canceledSharedReaderRollbackWorked}");
            Console.WriteLine($"Rewrite gate held before cancellation={rewriteGateHeld}");
            Console.WriteLine($"Canceled rewrite waiter observed={canceledRewriteWaiterObserved}");
            Console.WriteLine($"Canceled rewrite waiter released reference={canceledRewriteWaiterReleasedReference}");
            Console.WriteLine($"Native operation coordination correct={!fakeEngine.BuildRewriteOverlapDetected && fakeEngine.ConcurrentRewriteOverlapObserved}");

            AssertInvariant(eligibility.IsEligible, "Primary request should be eligible.");
            AssertInvariant(first.Success, "Initial rewrite should succeed.");
            AssertInvariant(second.Success, "Cached rewrite should succeed.");
            AssertInvariant(thirdWithAttachment.Success, "Attachment fingerprint rewrite should succeed.");
            AssertInvariant(restartLikeReuse.Success, "Restart-like disk cache reuse should succeed.");
            AssertInvariant(string.Equals(first.OutputFilePath, restartLikeReuse.OutputFilePath, StringComparison.OrdinalIgnoreCase), "Restart-like disk cache reuse should return the same output path.");
            AssertInvariant(concurrentRewrite1.Result.Success && concurrentRewrite2.Result.Success && concurrentRewrite3.Result.Success, "Concurrent rewrites should all succeed.");
            AssertInvariant(fakeEngine.RewriteInvocationCount == expectedRewriteInvocations, $"Rewrite single-flight failed. Expected {expectedRewriteInvocations}, got {fakeEngine.RewriteInvocationCount}.");
            AssertInvariant(sharedRewriteBlockedExclusive, "Shared rewrite should block exclusive rebuild from starting immediately.");
            AssertInvariant(exclusiveRebuildStartedAfterSharedRewrite, "Exclusive rebuild should start after the shared rewrite finishes.");
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
            AssertInvariant(distinctConcurrentRewrite1.Result.Success && distinctConcurrentRewrite2.Result.Success, "Distinct concurrent rewrites should both succeed.");
            AssertInvariant(canceledSharedReaderEnteredRollbackPath, "Canceled shared reader should reach the first-reader rollback path.");
            AssertInvariant(canceledSharedReaderObserved, "Canceled shared reader should observe cancellation.");
            AssertInvariant(canceledSharedReaderRolledBackReaderCount, "Canceled shared reader should restore reader count after rollback.");
            AssertInvariant(canceledSharedReaderRollbackWorked, "Canceled shared reader should roll back cleanly and stay blocked behind the next writer.");
            AssertInvariant(rewriteGateHeld, "Rewrite single-flight gate should be held before cancellation test runs.");
            AssertInvariant(canceledRewriteWaiterObserved, "Canceled rewrite waiter should observe cancellation.");
            AssertInvariant(canceledRewriteWaiterReleasedReference, "Canceled rewrite waiter should release its single-flight reference.");
            AssertInvariant(fakeEngine.ConcurrentRewriteOverlapObserved, "Concurrent distinct rewrites should be allowed to overlap.");
            AssertInvariant(!fakeEngine.BuildRewriteOverlapDetected, "Native operation coordination should still block rebuild/rewrite overlap.");
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
        public bool BuildRewriteOverlapDetected { get; private set; }
        public bool ConcurrentRewriteOverlapObserved { get; private set; }
        public int RewriteInvocationCount { get; private set; }
        private int _activeBuildOperations;
        private int _activeRewriteOperations;

        public Task<AssfontsOperationResult> SelfCheckAsync(CancellationToken cancellationToken)
            => Task.FromResult(AssfontsOperationResult.Ok("ok"));

        public async Task<AssfontsOperationResult> BuildFontDatabaseAsync(IReadOnlyList<string> fontDirectories, string dbDirectory, CancellationToken cancellationToken)
        {
            EnterBuildOperation();
            BuildDbInvocationCount++;
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            Directory.CreateDirectory(dbDirectory);
            File.WriteAllText(Path.Combine(dbDirectory, "fonts.json"), string.Join("|", fontDirectories));
            ExitBuildOperation();
            return AssfontsOperationResult.Ok("db ok");
        }

        public async Task<RewriteResult> RewriteSubtitleAsync(string subtitlePath, string outputDirectory, IReadOnlyList<string> fontDirectories, string dbDirectory, CancellationToken cancellationToken)
        {
            EnterRewriteOperation();
            RewriteInvocationCount++;
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            Directory.CreateDirectory(outputDirectory);
            var outputPath = Path.Combine(outputDirectory, $"{Path.GetFileNameWithoutExtension(subtitlePath)}.assfonts{Path.GetExtension(subtitlePath)}");
            File.WriteAllText(outputPath, "rewritten");
            ExitRewriteOperation();
            return RewriteResult.Rewritten(outputPath, "rewritten");
        }

        private void EnterBuildOperation()
        {
            if (Interlocked.Increment(ref _activeBuildOperations) > 1 || Volatile.Read(ref _activeRewriteOperations) > 0)
            {
                BuildRewriteOverlapDetected = true;
            }
        }

        private void ExitBuildOperation()
        {
            Interlocked.Decrement(ref _activeBuildOperations);
        }

        private void EnterRewriteOperation()
        {
            if (Interlocked.Increment(ref _activeRewriteOperations) > 1)
            {
                ConcurrentRewriteOverlapObserved = true;
            }

            if (Volatile.Read(ref _activeBuildOperations) > 0)
            {
                BuildRewriteOverlapDetected = true;
            }
        }

        private void ExitRewriteOperation()
        {
            Interlocked.Decrement(ref _activeRewriteOperations);
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

    private static T GetPrivateField<T>(object instance, string fieldName, Func<object, T>? valueSelector = null)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Field '{fieldName}' not found on {instance.GetType().Name}.");

        var value = field.GetValue(instance)!;
        return valueSelector is null ? (T)value : valueSelector(value);
    }

    private static bool PrivateConcurrentDictionaryContainsKey(object instance, string fieldName, string key)
    {
        var dictionary = GetPrivateField<object>(instance, fieldName);
        var containsKeyMethod = dictionary.GetType().GetMethod("ContainsKey", new[] { typeof(string) })
            ?? throw new InvalidOperationException($"ContainsKey(string) not found on field '{fieldName}'.");

        return (bool)containsKeyMethod.Invoke(dictionary, new object[] { key })!;
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
