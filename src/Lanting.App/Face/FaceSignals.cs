using System.Runtime.InteropServices;
using Lanting.App.Shell;
using Lanting.Core.Face;
using Microsoft.UI.Dispatching;
using Windows.System.Power;

namespace Lanting.App.Face;

/// <summary>
/// Memberi <see cref="FaceController"/> keadaan laptop: baterai dan charger (Windows memberi tahu lewat event),
/// dan lama tidak ada sentuhan keyboard/mouse (dibaca tiap 15 detik; satu panggilan Windows yang sangat murah).
/// </summary>
internal sealed class FaceSignals : IDisposable
{
    private static readonly TimeSpan IdleInterval = TimeSpan.FromSeconds(15);

    private readonly FaceController _face;
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _timer;

    public FaceSignals(FaceController face)
    {
        _face = face;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _timer = _dispatcher.CreateTimer();
        _timer.Interval = IdleInterval;
        _timer.Tick += (_, _) => Report();
    }

    public void Start()
    {
        PowerManager.RemainingChargePercentChanged += OnPowerChanged;
        PowerManager.PowerSupplyStatusChanged += OnPowerChanged;
        PowerManager.BatteryStatusChanged += OnPowerChanged;
        _timer.Start();
        Report();
    }

    public void Dispose()
    {
        _timer.Stop();
        PowerManager.RemainingChargePercentChanged -= OnPowerChanged;
        PowerManager.PowerSupplyStatusChanged -= OnPowerChanged;
        PowerManager.BatteryStatusChanged -= OnPowerChanged;
    }

    // Event daya datang dari thread lain.
    private void OnPowerChanged(object? sender, object e) => _dispatcher.TryEnqueue(Report);

    private void Report()
    {
        try
        {
            var hasBattery = PowerManager.BatteryStatus != BatteryStatus.NotPresent;
            var percent = hasBattery ? Math.Clamp(PowerManager.RemainingChargePercent, 0, 100) : (int?)null;
            var plugged = PowerManager.PowerSupplyStatus != PowerSupplyStatus.NotPresent;
            _face.Update(new FaceState(percent, plugged, IdleTime(), DateTime.Now));
        }
        catch (Exception ex)
        {
            StartupLog.Write($"Wajah: keadaan laptop gagal dibaca: {ex.Message}");
        }
    }

    private static TimeSpan IdleTime()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        return GetLastInputInfo(ref info)
            ? TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.dwTime))
            : TimeSpan.Zero;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }
}
