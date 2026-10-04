using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Shhtartup.Interop;

internal enum EDataFlow
{
    eRender = 0,
    eCapture = 1,
    eAll = 2,
}

internal enum ERole
{
    eConsole = 0,
    eMultimedia = 1,
    eCommunications = 2,
}

internal static class AudioGuids
{
    public static readonly Guid CLSID_MMDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    public static readonly Guid IID_IMMDeviceEnumerator = new("A95664D2-9614-4F35-A746-DE8DB63617E6");
    public static readonly Guid IID_IAudioSessionManager2 = new("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");

    public const uint DEVICE_STATE_ACTIVE = 0x00000001;
}

// Vtable slots below must appear in the EXACT original COM declaration order.
// Slots we never call are still declared (suffixed _Unused) so later real slots
// land at the correct vtable offset -- skipping a slot silently corrupts every
// call after it.
//
// Every method is [PreserveSig]: without it, [GeneratedComInterface] treats the managed return
// value as a hidden trailing [out, retval] parameter (returning uninitialised memory) and turns
// failure HRESULTs into exceptions -- which would crash the app from inside a window procedure.

[GeneratedComInterface]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
internal partial interface IMMDeviceEnumerator
{
    // dwStateMask: DEVICE_STATE_ACTIVE (1) to list only plugged-in, enabled outputs.
    [PreserveSig]
    int EnumAudioEndpoints(EDataFlow dataFlow, uint dwStateMask, out IMMDeviceCollection ppDevices);

    [PreserveSig]
    int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);

    // GetDevice, Register/UnregisterEndpointNotificationCallback -- never called, omitted (trailing slots).
}

[GeneratedComInterface]
[Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
internal partial interface IMMDeviceCollection
{
    [PreserveSig]
    int GetCount(out uint pcDevices);

    [PreserveSig]
    int Item(uint nDevice, out IMMDevice ppDevice);
}

[GeneratedComInterface]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
internal partial interface IMMDevice
{
    [PreserveSig]
    int Activate(in Guid iid, uint dwClsCtx, nint pActivationParams, out IAudioSessionManager2 ppInterface);

    // OpenPropertyStore, GetId, GetState -- never called, omitted (trailing slots).
}

[GeneratedComInterface]
[Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
internal partial interface IAudioSessionManager2
{
    [PreserveSig]
    int GetAudioSessionControl_Unused(in Guid sessionGuid, uint streamFlags, out nint sessionControl);

    [PreserveSig]
    int GetSimpleAudioVolume_Unused(in Guid sessionGuid, uint streamFlags, out nint simpleVolume);

    [PreserveSig]
    int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum);

    // Register/UnregisterSessionNotification, duck notifications -- never called, omitted (trailing slots).
}

[GeneratedComInterface]
[Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
internal partial interface IAudioSessionEnumerator
{
    [PreserveSig]
    int GetCount(out int sessionCount);

    [PreserveSig]
    int GetSession(int sessionIndex, out IAudioSessionControl2 session);
}

[GeneratedComInterface]
[Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D")]
internal partial interface IAudioSessionControl2
{
    // IAudioSessionControl base slots (0-8) -- never called, stubbed to preserve order.
    [PreserveSig] int GetState_Unused(out int state);
    [PreserveSig] int GetDisplayName_Unused(out nint name);
    [PreserveSig] int SetDisplayName_Unused(nint value, in Guid eventContext);
    [PreserveSig] int GetIconPath_Unused(out nint path);
    [PreserveSig] int SetIconPath_Unused(nint value, in Guid eventContext);
    [PreserveSig] int GetGroupingParam_Unused(out Guid groupingParam);
    [PreserveSig] int SetGroupingParam_Unused(in Guid overrideGuid, in Guid eventContext);
    [PreserveSig] int RegisterAudioSessionNotification_Unused(nint newNotifications);
    [PreserveSig] int UnregisterAudioSessionNotification_Unused(nint newNotifications);

    // IAudioSessionControl2 own slots (9-10) -- never called, stubbed.
    [PreserveSig] int GetSessionIdentifier_Unused(out nint retVal);
    [PreserveSig] int GetSessionInstanceIdentifier_Unused(out nint retVal);

    // slot 11: the one we need.
    [PreserveSig]
    int GetProcessId(out uint processId);

    // slot 12: S_OK for the special system-sounds session, S_FALSE otherwise.
    [PreserveSig]
    int IsSystemSoundsSession();

    // SetDuckingPreference -- never called, omitted (trailing slot).
}

[GeneratedComInterface]
[Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8")]
internal partial interface ISimpleAudioVolume
{
    [PreserveSig]
    int SetMasterVolume(float level, in Guid eventContext);

    [PreserveSig]
    int GetMasterVolume(out float level);

    // SetMute/GetMute -- never called, omitted (trailing slots).
}
