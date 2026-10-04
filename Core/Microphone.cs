using System.Runtime.InteropServices;

namespace GhostDeck;

/// <summary>
/// Microphone mute (discussion #231): the mute flag of the default Windows recording device,
/// through the documented Core Audio API (MMDevice + IAudioEndpointVolume). It is the same
/// switch as the mute box in the Windows sound settings, so it holds for every application at
/// once and needs no EC - it works on any laptop.
///
/// Windows keeps two "default" recording roles, one for ordinary apps and one for calls, and
/// they can point at different devices (a headset for calls, the built-in array for the rest).
/// Both are switched together: a microphone that is "off" must be off for the call as well.
/// The state shown is the one of the ordinary default device.
/// </summary>
public static class Microphone
{
    private const int ECapture = 1, ERoleConsole = 0, ERoleCommunications = 2, ClsCtxAll = 23;
    private static readonly Guid IidEndpointVolume = new("5CDF2C82-841E-4546-9722-0CF74078229A");

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice? endpoint);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object? iface);
        [PreserveSig] int OpenPropertyStore(int access, out IntPtr properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string? id);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        // the vtable order matters: the eleven methods before SetMute are declared only to hold their slots
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    /// <summary>
    /// Runs <paramref name="use"/> on the volume interface of each default recording device
    /// (ordinary role first, then the call role when it is a different device). Returns how
    /// many devices were visited.
    /// </summary>
    private static int ForEachDefault(Action<IAudioEndpointVolume> use)
    {
        IMMDeviceEnumerator? en = null;
        int visited = 0;
        string? firstId = null;
        try
        {
            en = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            foreach (int role in new[] { ERoleConsole, ERoleCommunications })
            {
                IMMDevice? dev = null;
                object? vol = null;
                try
                {
                    if (en.GetDefaultAudioEndpoint(ECapture, role, out dev) != 0 || dev == null) continue;
                    dev.GetId(out string? id);
                    if (visited > 0 && id != null && id == firstId) continue;   // one device in both roles
                    firstId ??= id;
                    var iid = IidEndpointVolume;
                    if (dev.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out vol) != 0 || vol is not IAudioEndpointVolume v) continue;
                    use(v);
                    visited++;
                }
                finally
                {
                    if (vol != null) Marshal.ReleaseComObject(vol);
                    if (dev != null) Marshal.ReleaseComObject(dev);
                }
            }
        }
        catch { }
        finally { if (en != null) Marshal.ReleaseComObject(en); }
        return visited;
    }

    /// <summary>Whether Windows has a default recording device at all.</summary>
    public static bool Present() => State() >= 0;

    /// <summary>1 = on, 0 = muted, -1 = no recording device.</summary>
    public static int State()
    {
        int state = -1;
        ForEachDefault(v =>
        {
            if (state >= 0) return;   // the ordinary default device decides
            if (v.GetMute(out bool muted) == 0) state = muted ? 0 : 1;
        });
        return state;
    }

    /// <summary>Unmutes (<paramref name="on"/>) or mutes the default recording devices. Throws when there is none or Windows refuses.</summary>
    public static void Set(bool on)
    {
        int failed = 0;
        int n = ForEachDefault(v =>
        {
            var ctx = Guid.Empty;
            if (v.SetMute(!on, ref ctx) < 0) failed++;
        });
        if (n == 0) throw new InvalidOperationException("no recording device found");
        if (failed > 0) throw new InvalidOperationException("Windows refused the microphone mute change");
    }
}
