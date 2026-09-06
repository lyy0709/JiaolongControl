using System.Runtime.InteropServices;

internal static class RawCurveRead
{
    [DllImport("nvapi64.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr nvapi_QueryInterface(uint id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Getter(IntPtr gpu, IntPtr buffer);

    // Fixed read-only IDs and known v1 sizes; no arbitrary entry point or setter.
    public static byte[] Read(IntPtr gpu, uint id, byte[]? mask = null)
    {
        int size = id switch { 0x507B4B59 => 6188, 0x21537AD4 => 7208, 0x23F1B133 => 9248,
            _ => throw new ArgumentException("Not a permitted curve getter") };
        var bytes = new byte[262144];
        Array.Fill(bytes, (byte)0xA5, size, bytes.Length - size);
        BitConverter.GetBytes(size | (1 << 16)).CopyTo(bytes, 0);
        if (mask != null)
        {
            if (mask.Length != 32) throw new ArgumentException("Expected a 256-bit request mask");
            mask.CopyTo(bytes, 4);
        }
        var pointer = nvapi_QueryInterface(id);
        if (pointer == IntPtr.Zero) throw new NotSupportedException($"Getter {id:X8} unavailable");
        var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            int result = Marshal.GetDelegateForFunctionPointer<Getter>(pointer)(gpu, pinned.AddrOfPinnedObject());
            if (result != 0) throw new InvalidOperationException($"Getter {id:X8}: {result}");
            if (bytes.AsSpan(size).ContainsAnyExcept((byte)0xA5)) throw new InvalidOperationException("Getter exceeded declared buffer");
            return bytes[..size];
        }
        finally { pinned.Free(); }
    }
}
