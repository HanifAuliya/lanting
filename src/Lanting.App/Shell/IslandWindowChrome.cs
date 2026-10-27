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

        _appWindow.IsShownInSwitchers = false;
        _appWindow.Title = "Lanting";

        // Windows 11 membulatkan sudut dan memberi garis tepi pada setiap window; island menggambar bentuknya sendiri.
        var corner = DWMWCP_DONOTROUND;
        _ = DwmSetWindowAttribute(_hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
        var border = DWMWA_COLOR_NONE;
        _ = DwmSetWindowAttribute(_hwnd, DWMWA_BORDER_COLOR, ref border, sizeof(int));
    }

    /// <summary>Ubah ukuran window (dalam DIP) dan tetap rata tengah di atas layar.</summary>
    public void Resize(double width, double height, double topMargin)
    {
        var scale = Scale;
        var w = (int)Math.Ceiling(width * scale);
        var h = (int)Math.Ceiling(height * scale);

        var display = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary).OuterBounds;
        var x = display.X + ((display.Width - w) / 2);
        var y = display.Y + (int)Math.Round(topMargin * scale);

        _appWindow.MoveAndResize(new RectInt32(x, y, w, h));
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
