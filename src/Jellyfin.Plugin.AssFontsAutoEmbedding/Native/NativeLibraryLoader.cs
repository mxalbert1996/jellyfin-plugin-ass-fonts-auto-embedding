using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Jellyfin.Plugin.AssFontsAutoEmbedding.Interop;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AssFontsAutoEmbedding.Native;

public sealed class NativeLibraryLoader
{
    private readonly ILogger<NativeLibraryLoader> _logger;
    private readonly object _syncRoot = new();
    private IntPtr _handle;
    private AssfontsNativeBindings? _bindings;

    public NativeLibraryLoader(ILogger<NativeLibraryLoader> logger)
    {
        _logger = logger;
    }

    public bool IsLoaded => _handle != IntPtr.Zero;

    internal AssfontsNativeBindings? Bindings => _bindings;

    public string? LastFailureReason { get; private set; }

    public string? LoadedLibraryPath { get; private set; }

    public bool TryLoad()
    {
        lock (_syncRoot)
        {
            if (IsLoaded)
            {
                return true;
            }

            var libraryPath = ResolveLibraryPath();
            if (libraryPath is null)
            {
                LastFailureReason = GetLibraryResolutionFailureReason();
                return false;
            }

            if (!File.Exists(libraryPath))
            {
                LastFailureReason = $"Expected native library was not found at '{libraryPath}'.";
                return false;
            }

            try
            {
                _handle = System.Runtime.InteropServices.NativeLibrary.Load(libraryPath);
                _bindings = new AssfontsNativeBindings(_handle);
                LoadedLibraryPath = libraryPath;
                LastFailureReason = null;
                _logger.LogInformation("Loaded libassfonts from {Path}", libraryPath);
                return true;
            }
            catch (Exception ex)
            {
                LastFailureReason = ex.Message;
                _bindings = null;
                _handle = IntPtr.Zero;
                _logger.LogWarning(ex, "Failed to load libassfonts from {Path}", libraryPath);
                return false;
            }
        }
    }

    public string? ResolveLibraryPath()
    {
        if (!TryGetRuntimeLibraryLocation(Assembly.GetExecutingAssembly(), out var runtimeFolder, out var fileName))
        {
            return null;
        }

        if (string.IsNullOrEmpty(runtimeFolder) || string.IsNullOrEmpty(fileName))
        {
            return null;
        }

        var pluginAssemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        if (string.IsNullOrEmpty(pluginAssemblyDirectory))
        {
            return null;
        }

        return Path.Combine(pluginAssemblyDirectory, "native", runtimeFolder, fileName);
    }

    private static bool TryGetRuntimeLibraryLocation(Assembly assembly, out string? runtimeFolder, out string? fileName)
    {
        fileName = GetLibraryFileName();
        if (fileName is null)
        {
            runtimeFolder = null;
            return false;
        }

        var ridPrefix = GetRuntimeIdentifierPrefix();
        if (ridPrefix is null)
        {
            runtimeFolder = null;
            return false;
        }

        var architectureSuffix = GetArchitectureSuffix();
        if (architectureSuffix is null)
        {
            runtimeFolder = null;
            return false;
        }

        var pluginAssemblyDirectory = Path.GetDirectoryName(assembly.Location);
        if (string.IsNullOrEmpty(pluginAssemblyDirectory))
        {
            runtimeFolder = null;
            return false;
        }

        runtimeFolder = $"{ridPrefix}-{architectureSuffix}";
        return true;
    }

    private static string GetLibraryResolutionFailureReason()
    {
        var fileName = GetLibraryFileName();
        if (fileName is null)
        {
            return $"No bundled assfonts native library layout is defined for operating system '{GetPlatformName()}'.";
        }

        var ridPrefix = GetRuntimeIdentifierPrefix();
        if (ridPrefix is null)
        {
            return $"No bundled {fileName} is defined for operating system '{GetPlatformName()}' and architecture '{RuntimeInformation.ProcessArchitecture}'.";
        }

        var architectureSuffix = GetArchitectureSuffix();
        if (architectureSuffix is null)
        {
            return $"No bundled {fileName} is defined for unsupported architecture '{RuntimeInformation.ProcessArchitecture}' on operating system '{GetPlatformName()}'.";
        }

        return $"Could not resolve plugin-local path for bundled {fileName} under native/{ridPrefix}-{architectureSuffix}/.";
    }

    private static string? GetRuntimeIdentifierPrefix()
        => OperatingSystem.IsLinux()
            ? "linux"
            : OperatingSystem.IsMacOS()
                ? "osx"
                : OperatingSystem.IsWindows()
                    ? "win"
                    : null;

    private static string? GetLibraryFileName()
        => OperatingSystem.IsLinux()
            ? "libassfonts.so"
            : OperatingSystem.IsMacOS()
                ? "libassfonts.dylib"
                : OperatingSystem.IsWindows()
                    ? "assfonts.dll"
                    : null;

    private static string? GetArchitectureSuffix()
        => RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => null
        };

    private static string GetPlatformName()
        => OperatingSystem.IsLinux()
            ? "Linux"
            : OperatingSystem.IsMacOS()
                ? "macOS"
                : OperatingSystem.IsWindows()
                    ? "Windows"
                    : "Unknown";
}
