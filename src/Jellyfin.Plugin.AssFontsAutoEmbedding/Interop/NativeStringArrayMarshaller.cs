using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Jellyfin.Plugin.AssFontsAutoEmbedding.Interop;

internal sealed class NativeStringArrayMarshaller : IDisposable
{
    private readonly IntPtr[] _stringPointers;

    public NativeStringArrayMarshaller(IReadOnlyList<string> values)
    {
        _stringPointers = new IntPtr[values.Count];
        for (var i = 0; i < values.Count; i++)
        {
            _stringPointers[i] = Marshal.StringToCoTaskMemUTF8(values[i]);
        }

        Pointer = Marshal.AllocCoTaskMem(IntPtr.Size * values.Count);
        Marshal.Copy(_stringPointers, 0, Pointer, values.Count);
    }

    public IntPtr Pointer { get; }

    public void Dispose()
    {
        foreach (var pointer in _stringPointers)
        {
            if (pointer != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(pointer);
            }
        }

        if (Pointer != IntPtr.Zero)
        {
            Marshal.FreeCoTaskMem(Pointer);
        }
    }
}
