using System.Runtime.InteropServices;

namespace Tinnitdown.Interop;

internal static partial class Ole32
{
    public const uint COINIT_APARTMENTTHREADED = 0x2;
    public const uint CLSCTX_INPROC_SERVER = 0x1;

    [LibraryImport("ole32.dll")]
    public static partial int CoInitializeEx(nint pvReserved, uint dwCoInit);

    [LibraryImport("ole32.dll")]
    public static partial void CoUninitialize();

    // riid must always match the GUID baked into the [GeneratedComInterface] type passed as ppv;
    // the COM source generator marshals GeneratedComInterface-attributed out params automatically.
    [LibraryImport("ole32.dll")]
    public static partial int CoCreateInstance(
        in Guid rclsid,
        nint pUnkOuter,
        uint dwClsContext,
        in Guid riid,
        out IMMDeviceEnumerator ppv);
}
