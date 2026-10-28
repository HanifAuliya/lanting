using System.Runtime.InteropServices;
using Lanting.App.Shell;

namespace Lanting.App.QuickToggles;

/// <summary>
/// Suara (speaker) atau mikrofon bawaan Windows, lewat Core Audio. "Nyala" berarti tidak di-mute.
/// Perangkat bawaan dibaca ulang setiap kali, jadi ikut berganti saat headset dicolok.
/// </summary>
internal sealed class AudioMuteToggle : IQuickToggle
{
    private const int eRender = 0;
    private const int eCapture = 1;
    private const int eMultimedia = 1;
    private const int CLSCTX_ALL = 23;

    private static readonly Guid IID_IAudioEndpointVolume = new("5CDF2C82-841E-4546-9722-0CF74078229A");

    private readonly int _flow;

    private AudioMuteToggle(int flow, string label, string glyph)
    {
        _flow = flow;
        Label = label;
        Glyph = glyph;
    }

    public string Label { get; }

    public string Glyph { get; }

    public static AudioMuteToggle Speaker() => new(eRender, "Suara", "\uE767");

    public static AudioMuteToggle Microphone() => new(eCapture, "Mikrofon", "\uE720");

    public Task<bool?> ReadAsync() => Task.FromResult(Use(volume =>
    {
        Marshal.ThrowExceptionForHR(volume.GetMute(out var muted));
        return !muted;
    }));

    public Task<bool?> WriteAsync(bool on) => Task.FromResult(Use(volume =>
    {
        Marshal.ThrowExceptionForHR(volume.SetMute(!on, IntPtr.Zero));
        Marshal.ThrowExceptionForHR(volume.GetMute(out var muted));
        return !muted;
    }));

    private bool? Use(Func<IAudioEndpointVolume, bool> action)
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        IAudioEndpointVolume? volume = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();

            // Tidak ada perangkat (misalnya PC tanpa mikrofon): tombol dinonaktifkan.
            if (enumerator.GetDefaultAudioEndpoint(_flow, eMultimedia, out device) != 0 || device is null)
            {
                return null;
            }

            var iid = IID_IAudioEndpointVolume;
            Marshal.ThrowExceptionForHR(device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var activated));
            volume = (IAudioEndpointVolume)activated;
            return action(volume);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            StartupLog.Write($"{Label}: {ex.Message}");
            return null;
        }
        finally
        {
            Release(volume);
            Release(device);
            Release(enumerator);
        }
    }

    private static void Release(object? com)
    {
        if (com is not null)
        {
            Marshal.ReleaseComObject(com);
        }
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator
    {
    }

    // Hanya method sampai yang dipakai yang dideklarasikan; urutannya harus sama dengan vtable aslinya.
    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object endpoint);
    }

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig]
        int RegisterControlChangeNotify(IntPtr notify);

        [PreserveSig]
        int UnregisterControlChangeNotify(IntPtr notify);

        [PreserveSig]
        int GetChannelCount(out uint count);

        [PreserveSig]
        int SetMasterVolumeLevel(float level, IntPtr context);

        [PreserveSig]
        int SetMasterVolumeLevelScalar(float level, IntPtr context);

        [PreserveSig]
        int GetMasterVolumeLevel(out float level);

        [PreserveSig]
        int GetMasterVolumeLevelScalar(out float level);

        [PreserveSig]
        int SetChannelVolumeLevel(uint channel, float level, IntPtr context);

        [PreserveSig]
        int SetChannelVolumeLevelScalar(uint channel, float level, IntPtr context);

        [PreserveSig]
        int GetChannelVolumeLevel(uint channel, out float level);

        [PreserveSig]
        int GetChannelVolumeLevelScalar(uint channel, out float level);

        [PreserveSig]
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, IntPtr context);

        [PreserveSig]
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}
