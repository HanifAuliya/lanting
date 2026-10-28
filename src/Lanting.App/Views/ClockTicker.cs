using System.Globalization;
using Microsoft.UI.Dispatching;

namespace Lanting.App.Views;

/// <summary>
/// Berdetak sekali tiap pergantian menit (bukan tiap detik), untuk jam di pil dan dashboard.
/// Dipicu di thread UI.
/// </summary>
internal sealed class ClockTicker
{
    /// <summary>Format tanggal dan jam bahasa Indonesia ("Selasa, 28 Oktober").</summary>
    public static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("id-ID");

    private readonly DispatcherQueueTimer _timer;

    public ClockTicker()
    {
        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.IsRepeating = false;
        _timer.Tick += (_, _) =>
        {
            Tick?.Invoke(this, DateTime.Now);
            Schedule();
        };
        Schedule();
    }

    public event EventHandler<DateTime>? Tick;

    // Titik dua ditulis langsung: pemisah jam bawaan id-ID adalah titik ("13.48"), jam digital biasanya "13:48".
    public static string Time(DateTime time) => time.ToString("HH':'mm", Culture);

    public static string Date(DateTime time) => time.ToString("dddd, d MMMM", Culture);

    private void Schedule()
    {
        // Tunggu sampai detik ke-0 menit berikutnya (plus sedikit supaya pasti sudah lewat).
        var now = DateTime.Now;
        _timer.Interval = TimeSpan.FromSeconds(60 - now.Second) - TimeSpan.FromMilliseconds(now.Millisecond) + TimeSpan.FromMilliseconds(50);
        _timer.Start();
    }
}
