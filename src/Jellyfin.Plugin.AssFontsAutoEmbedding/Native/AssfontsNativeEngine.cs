using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AssFontsAutoEmbedding.Configuration;
using Jellyfin.Plugin.AssFontsAutoEmbedding.Interop;
using Jellyfin.Plugin.AssFontsAutoEmbedding.Services;
using Jellyfin.Plugin.AssFontsAutoEmbedding.State;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AssFontsAutoEmbedding.Native;

public sealed class AssfontsNativeEngine : IAssfontsEngine
{
    private readonly NativeLibraryLoader _libraryLoader;
    private readonly PluginContext _pluginContext;
    private readonly FontDbStateManager _stateManager;
    private readonly ILogger<AssfontsNativeEngine> _logger;

    public AssfontsNativeEngine(NativeLibraryLoader libraryLoader, PluginContext pluginContext, FontDbStateManager stateManager, ILogger<AssfontsNativeEngine> logger)
    {
        _libraryLoader = libraryLoader;
        _pluginContext = pluginContext;
        _stateManager = stateManager;
        _logger = logger;
    }

    public bool IsAvailable => _libraryLoader.IsLoaded;

    public string? LastFailureReason => _libraryLoader.LastFailureReason;

    public Task<AssfontsOperationResult> SelfCheckAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _stateManager.MarkSelfCheckAttempted();
        var loaded = _libraryLoader.TryLoad();
        if (!loaded)
        {
            return Task.FromResult(AssfontsOperationResult.Fail(_libraryLoader.LastFailureReason ?? "Unknown native load failure."));
        }

        try
        {
            // Invoke the exported function with a real, isolated font directory.
            // We intentionally avoid the guaranteed-invalid zero-font-dir path in assfonts.
            using var dbRoot = new TemporaryDirectory();
            using var fontRoot = new TemporaryDirectory();
            using var marshaller = new NativeStringArrayMarshaller([fontRoot.Path]);
            _libraryLoader.Bindings!.BuildDb(marshaller.Pointer, 1, dbRoot.Path, CreateLogCallback("self-check"), AssfontsLogLevel.Info);

            var fontsJsonPath = Path.Combine(dbRoot.Path, "fonts.json");
            var exists = File.Exists(fontsJsonPath);
            var length = exists ? new FileInfo(fontsJsonPath).Length : 0;
            return Task.FromResult(exists
                ? AssfontsOperationResult.Ok($"Native self-check invoked AssfontsBuildDB with an isolated temp font directory; fonts.json length={length} at '{fontsJsonPath}'.")
                : AssfontsOperationResult.Fail($"Native self-check bound and called AssfontsBuildDB with an isolated temp font directory, but '{fontsJsonPath}' was not produced."));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Native self-check symbol invocation failed.");
            return Task.FromResult(AssfontsOperationResult.Fail(ex.Message));
        }
    }

    public Task<AssfontsOperationResult> BuildFontDatabaseAsync(IReadOnlyList<string> fontDirectories, string dbDirectory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_libraryLoader.TryLoad())
        {
            return Task.FromResult(AssfontsOperationResult.Fail(_libraryLoader.LastFailureReason ?? "Native library is unavailable."));
        }

        Directory.CreateDirectory(dbDirectory);

        try
        {
            using var marshaller = new NativeStringArrayMarshaller(fontDirectories);
            _libraryLoader.Bindings!.BuildDb(
                marshaller.Pointer,
                (uint)fontDirectories.Count,
                dbDirectory,
                CreateLogCallback("build-db"),
                MapLogLevel(_pluginContext.GetNativeLogVerbosity()));

            var fontsJsonPath = Path.Combine(dbDirectory, "fonts.json");
            var success = File.Exists(fontsJsonPath) && new FileInfo(fontsJsonPath).Length > 0;
            return Task.FromResult(success
                ? AssfontsOperationResult.Ok($"DB build invoked and produced non-empty '{fontsJsonPath}'.")
                : AssfontsOperationResult.Fail($"DB build invoked but '{fontsJsonPath}' was missing or empty."));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to build assfonts DB.");
            return Task.FromResult(AssfontsOperationResult.Fail(ex.Message));
        }
    }

    public Task<RewriteResult> RewriteSubtitleAsync(string subtitlePath, string outputDirectory, IReadOnlyList<string> fontDirectories, string dbDirectory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_libraryLoader.TryLoad())
        {
            return Task.FromResult(RewriteResult.Skipped(_libraryLoader.LastFailureReason ?? "Native library is unavailable."));
        }

        Directory.CreateDirectory(outputDirectory);

        try
        {
            using var inputMarshaller = new NativeStringArrayMarshaller([subtitlePath]);
            using var fontMarshaller = new NativeStringArrayMarshaller(fontDirectories);

            _libraryLoader.Bindings!.Run(
                inputMarshaller.Pointer,
                1,
                outputDirectory,
                fontMarshaller.Pointer,
                (uint)fontDirectories.Count,
                dbDirectory,
                0,
                0,
                0,
                0,
                0,
                1,
                CreateLogCallback("rewrite"),
                MapLogLevel(_pluginContext.GetNativeLogVerbosity()));

            var outputPath = Path.Combine(outputDirectory, $"{Path.GetFileNameWithoutExtension(subtitlePath)}.assfonts{Path.GetExtension(subtitlePath)}");
            return Task.FromResult(File.Exists(outputPath) && new FileInfo(outputPath).Length > 0
                ? RewriteResult.Rewritten(outputPath, $"Rewrote subtitle to '{outputPath}'.")
                : RewriteResult.Skipped($"assfonts run completed but expected non-empty output '{outputPath}' was not found."));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to rewrite subtitle {SubtitlePath}.", subtitlePath);
            return Task.FromResult(RewriteResult.Skipped(ex.Message));
        }
    }

    private AssfontsNativeBindings.AssfontsLogCallback CreateLogCallback(string operation)
        => (message, level) =>
        {
            var text = Marshal.PtrToStringUTF8(message) ?? string.Empty;
            switch (level)
            {
                case AssfontsLogLevel.Error:
                    _logger.LogError("assfonts[{Operation}] {Message}", operation, text);
                    break;
                case AssfontsLogLevel.Warn:
                    _logger.LogWarning("assfonts[{Operation}] {Message}", operation, text);
                    break;
                default:
                    _logger.LogInformation("assfonts[{Operation}] {Message}", operation, text);
                    break;
            }
        };

    private static AssfontsLogLevel MapLogLevel(NativeLogVerbosity verbosity)
        => verbosity switch
        {
            NativeLogVerbosity.Info => AssfontsLogLevel.Info,
            NativeLogVerbosity.Warn => AssfontsLogLevel.Warn,
            NativeLogVerbosity.Error => AssfontsLogLevel.Error,
            NativeLogVerbosity.Text => AssfontsLogLevel.Text,
            _ => AssfontsLogLevel.None
        };

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "assfonts-selfcheck-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, true);
                }
            }
            catch
            {
            }
        }
    }

}
