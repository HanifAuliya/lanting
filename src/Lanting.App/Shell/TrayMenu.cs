using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;
using Windows.UI.ViewManagement;
using WinUIEx;

namespace Lanting.App.Shell;

/// <summary>
/// Ikon Lanting di system tray (pojok kanan bawah). Klik kiri membuka Pengaturan, klik kanan membuka menu.
/// Warna ikon mengikuti tema taskbar: putih di taskbar gelap, hitam di taskbar terang.
/// </summary>
internal sealed class TrayMenu : IDisposable
{
    private const uint IconId = 1;

    private readonly TrayIcon _icon;
    private readonly UISettings _uiSettings = new();
    private readonly DispatcherQueue _dispatcher;
    private bool _lightTaskbar;

    public TrayMenu()
    {
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _lightTaskbar = IsTaskbarLight();
        _icon = new TrayIcon(IconId, IconPath(_lightTaskbar), "Lanting");
        _icon.Selected += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        _icon.ContextMenu += OnContextMenu;
        _icon.IsVisible = true;

        // Dipicu saat tema Windows berganti (dari thread lain).
        _uiSettings.ColorValuesChanged += OnColorValuesChanged;
    }

    public event EventHandler? SettingsRequested;

    public event EventHandler? ExitRequested;

    public void Dispose()
    {
        _uiSettings.ColorValuesChanged -= OnColorValuesChanged;
        _icon.IsVisible = false;
        _icon.Dispose();
    }

    private void OnContextMenu(TrayIcon sender, TrayIconEventArgs e)
    {
        var settings = new MenuFlyoutItem { Text = "Pengaturan" };
        settings.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);

        var autoStart = new ToggleMenuFlyoutItem { Text = "Mulai bersama Windows", IsChecked = AutoStart.IsEnabled };
        autoStart.Click += (_, _) => AutoStart.Set(autoStart.IsChecked);

        var exit = new MenuFlyoutItem { Text = "Keluar" };
        exit.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        var menu = new MenuFlyout();
        menu.Items.Add(settings);
        menu.Items.Add(autoStart);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(exit);
        e.Flyout = menu;
    }

    private void OnColorValuesChanged(UISettings sender, object args) => _dispatcher.TryEnqueue(() =>
    {
        var light = IsTaskbarLight();
        if (light != _lightTaskbar)
        {
            _lightTaskbar = light;
            _icon.SetIcon(IconPath(light));
        }
    });

    private static string IconPath(bool lightTaskbar) =>
        Path.Combine(AppContext.BaseDirectory, "Assets", lightTaskbar ? "tray-dark.ico" : "tray-light.ico");

    // Tema taskbar bisa berbeda dari tema aplikasi; Windows menyimpannya di sini.
    private static bool IsTaskbarLight()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is 1;
    }
}
