using System;
using System.Runtime.InteropServices;

namespace Jellyfin.Plugin.AssFontsAutoEmbedding.Interop;

internal sealed class AssfontsNativeBindings
{
    public AssfontsNativeBindings(IntPtr libraryHandle)
    {
        BuildDb = Marshal.GetDelegateForFunctionPointer<AssfontsBuildDbDelegate>(System.Runtime.InteropServices.NativeLibrary.GetExport(libraryHandle, "AssfontsBuildDB"));
        Run = Marshal.GetDelegateForFunctionPointer<AssfontsRunDelegate>(System.Runtime.InteropServices.NativeLibrary.GetExport(libraryHandle, "AssfontsRun"));
    }

    public AssfontsBuildDbDelegate BuildDb { get; }

    public AssfontsRunDelegate Run { get; }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void AssfontsLogCallback(IntPtr message, AssfontsLogLevel level);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void AssfontsBuildDbDelegate(
        IntPtr fontsPaths,
        uint numFonts,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string dbDirectory,
        AssfontsLogCallback callback,
        AssfontsLogLevel logLevel);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void AssfontsRunDelegate(
        IntPtr inputPaths,
        uint numPaths,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string outputPath,
        IntPtr fontsPaths,
        uint numFonts,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string dbDirectory,
        uint brightness,
        uint subsetOnly,
        uint embedOnly,
        uint renameFonts,
        uint combineFonts,
        uint threadCount,
        AssfontsLogCallback callback,
        AssfontsLogLevel logLevel);
}
