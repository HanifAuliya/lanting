using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using WinUIEx.Messaging;

namespace Lanting.App.Shell;

/// <summary>
/// Memberi tahu saat ada aplikasi layar penuh (game, video, presentasi) di layar island.
/// Caranya sama dengan taskbar Windows: window island didaftarkan sebagai "appbar" (tanpa memakan ruang layar),
/// lalu Windows sendiri mengirim pesan ABN_FULLSCREENAPP. Tidak ada polling.
/// </summary>
internal sealed class FullscreenWatcher : IDisposable
{
    private const uint ABM_NEW = 0;
    private const uint ABM_REMOVE = 1;
    private const int ABN_FULLSCREENAPP = 2;
    private const uint CallbackMessage = 0x8000 + 0x4C; // WM_APP + 'L'

    // SHQueryUserNotificationState: game Direct3D layar penuh, mode presentasi, atau aplikasi layar penuh lain.
    private const int QUNS_BUSY = 2;
    private const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;
    private const int QUNS_PRESENTATION_MODE = 4;

    private readonly IntPtr _hwnd;
    private readonly WindowMessageMonitor _monitor;
    private bool _registered;

    public FullscreenWatcher(Window window, IntPtr hwnd)
    {
        _hwnd = hwnd;
        _monitor = new WindowMessageMonitor(window);
        _monitor.WindowMessageReceived += OnMessage;

        var data = AppBarData();
        _registered = SHAppBarMessage(ABM_NEW, ref data) != 0;
        if (!_registered)
        {
            StartupLog.Write("Deteksi layar penuh tidak tersedia (gagal mendaftar appbar).");
        }
    }

    /// <summary><c>true</c> saat aplikasi layar penuh muncul, <c>false</c> saat hilang. Dipicu di thread UI.</summary>
    public event EventHandler<bool>? Changed;

    /// <summary>Keadaan saat ini, untuk awal aplikasi (pesan hanya datang saat ada perubahan).</summary>
    public static bool IsFullscreenNow() =>
        SHQueryUserNotificationState(out var state) == 0
        && state is QUNS_BUSY or QUNS_RUNNING_D3D_FULL_SCREEN or QUNS_PRESENTATION_MODE;

    public void Dispose()
    {
        _monitor.WindowMessageReceived -= OnMessage;
        _monitor.Dispose();
        if (_registered)
        {
            var data = AppBarData();
            _ = SHAppBarMessage(ABM_REMOVE, ref data);
            _registered = false;
        }
    }

    private void OnMessage(object? sender, WindowMessageEventArgs e)
    {
        if (e.Message.MessageId == CallbackMessage && (int)e.Message.WParam == ABN_FULLSCREENAPP)
        {
            Changed?.Invoke(this, e.Message.LParam != 0);
        }
    }

    private APPBARDATA AppBarData() => new()
    {
        cbSize = (uint)Marshal.SizeOf<APPBARDATA>(),
        hWnd = _hwnd,
        uCallbackMessage = CallbackMessage,
    };

    [DllImport("shell32.dll")]
    private static extern UIntPtr SHAppBarMessage(uint message, ref APPBARDATA data);

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public NativeMethods.RECT rc;
        public IntPtr lParam;
    }
}
