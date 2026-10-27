using Microsoft.UI.Windowing;
using Windows.Graphics;
using static Lanting.App.Shell.NativeMethods;

namespace Lanting.App.Shell;

/// <summary>
/// Mengatur window Win32 di balik island: tanpa border, selalu di atas,
/// tidak muncul di taskbar/Alt+Tab, dan diletakkan di tengah atas layar.
/// </summary>
internal sealed class IslandWindowChrome
{
    private readonly AppWindow _appWindow;
    private readonly IntPtr _hwnd;
    private bool _visible = true;
    private RectInt32 _placed;
    private RECT _hitArea;

    public IslandWindowChrome(AppWindow appWindow, IntPtr hwnd)
    {
        _appWindow = appWindow;
        _hwnd = hwnd;
    }

    /// <summary>Skala DPI monitor tempat island berada (1.0 = 100%).</summary>
    public double Scale => GetDpiForWindow(_hwnd) / 96.0;

    public void Configure()
    {
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
            presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
        }

        // Tanpa ini Windows tetap menyisakan garis putih tipis (sisa bingkai judul) di tepi atas.
        var style = (long)GetWindowLongPtr(_hwnd, GWL_STYLE);
        style = (style & ~(WS_CAPTION | WS_THICKFRAME | WS_SYSMENU)) | WS_POPUP;
        _ = SetWindowLongPtr(_hwnd, GWL_STYLE, (IntPtr)style);
        _ = SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);

        _appWindow.IsShownInSwitchers = false;
        _appWindow.Title = "Lanting";

        // Windows 11 membulatkan sudut dan memberi garis tepi pada setiap window; island menggambar bentuknya sendiri.
        var corner = DWMWCP_DONOTROUND;
        _ = DwmSetWindowAttribute(_hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
        var border = DWMWA_COLOR_NONE;
        _ = DwmSetWindowAttribute(_hwnd, DWMWA_BORDER_COLOR, ref border, sizeof(int));
    }

    /// <summary>
    /// Letakkan window (ukuran dalam DIP) rata tengah di atas layar. Tidak melakukan apa-apa bila posisinya sudah benar,
    /// jadi aman dipanggil setiap kali state berubah (sekalian menyesuaikan bila resolusi atau skala layar berganti).
    /// </summary>
    /// <returns><c>true</c> bila window benar-benar dipindah.</returns>
    public bool Place(double width, double height, double topMargin)
    {
        var scale = Scale;
        var w = (int)Math.Ceiling(width * scale);
        var h = (int)Math.Ceiling(height * scale);

        var display = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary).OuterBounds;
        var x = display.X + ((display.Width - w) / 2);
        var y = display.Y + (int)Math.Round(topMargin * scale);

        var rect = new RectInt32(x, y, w, h);
        if (rect.Equals(_placed))
        {
            return false;
        }

        _placed = rect;
        _appWindow.MoveAndResize(rect);
        StartupLog.Write($"Island {width}x{height} DIP -> {w}x{h} px di ({x},{y}), skala {scale:0.##}, layar {display.Width}x{display.Height}");
        return true;
    }

    /// <summary>
    /// Batasi bagian window yang bisa disentuh mouse ke kotak selebar <paramref name="width"/> DIP di tengah atas.
    /// Sisanya transparan dan klik tembus ke aplikasi di bawahnya. Kotaknya sengaja persegi (bukan bulat)
    /// karena region Windows tidak antialias; sudut pil yang halus digambar oleh clip di dalamnya.
    /// </summary>
    public void SetHitArea(double width, double height)
    {
        var scale = Scale;
        var w = (int)Math.Ceiling(width * scale);
        var h = (int)Math.Ceiling(height * scale);
        var left = (_placed.Width - w) / 2;

        _hitArea = new RECT { Left = left, Top = 0, Right = left + w, Bottom = h };
        // Setelah SetWindowRgn berhasil, region dimiliki Windows; jangan dihapus sendiri.
        var region = CreateRectRgn(_hitArea.Left, _hitArea.Top, _hitArea.Right, _hitArea.Bottom);
        if (SetWindowRgn(_hwnd, region, true) == 0)
        {
            _ = DeleteObject(region);
        }
    }

    /// <summary>Apakah kursor mouse (posisi sebenarnya, bukan dari event XAML) ada di area island.</summary>
    public bool ContainsCursor()
    {
        if (!GetCursorPos(out var cursor) || !GetWindowRect(_hwnd, out var rect))
        {
            return false;
        }

        var x = cursor.X - rect.Left;
        var y = cursor.Y - rect.Top;
        return x >= _hitArea.Left && x < _hitArea.Right && y >= _hitArea.Top && y < _hitArea.Bottom;
    }

    public void SetVisible(bool visible)
    {
        if (visible == _visible)
        {
            return;
        }

        _visible = visible;
        // SW_SHOWNOACTIVATE: island muncul tanpa mencuri fokus dari app yang sedang dipakai.
        _ = ShowWindow(_hwnd, visible ? SW_SHOWNOACTIVATE : SW_HIDE);
    }
}
