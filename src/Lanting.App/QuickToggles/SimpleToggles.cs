using System.Runtime.InteropServices;
using Lanting.App.Shell;
using Microsoft.Win32;

namespace Lanting.App.QuickToggles;

/// <summary>
/// Layar tetap menyala: Windows tidak tidur dan layar tidak mati selama Lanting berjalan dan tombol ini nyala.
/// Sama seperti saat video diputar; otomatis berhenti saat Lanting ditutup.
/// </summary>
internal sealed class KeepAwakeToggle : IQuickToggle
{
    private const uint ES_CONTINUOUS = 0x80000000;
    private const uint ES_SYSTEM_REQUIRED = 0x00000001;
    private const uint ES_DISPLAY_REQUIRED = 0x00000002;

    private bool _on;

    public string Label => "Layar tetap menyala";

    public string Glyph => "\uE706";

    public Task<bool?> ReadAsync() => Task.FromResult<bool?>(_on);

    // Dipanggil di thread UI; status ini melekat pada thread pemanggil, dan thread UI hidup selama aplikasi.
    public Task<bool?> WriteAsync(bool on)
    {
        _ = SetThreadExecutionState(on ? ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED : ES_CONTINUOUS);
        _on = on;
        return Task.FromResult<bool?>(_on);
    }

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint flags);
}

/// <summary>Mode gelap Windows (aplikasi dan taskbar), sama dengan Settings &gt; Personalization &gt; Colors.</summary>
internal sealed class DarkModeToggle : IQuickToggle
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const uint WM_SETTINGCHANGE = 0x001A;
    private const uint SMTO_ABORTIFHUNG = 0x0002;
    private static readonly IntPtr HWND_BROADCAST = new(0xFFFF);

    public string Label => "Mode gelap";

    public string Glyph => "\uE708";

    public Task<bool?> ReadAsync()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return Task.FromResult<bool?>(key?.GetValue("AppsUseLightTheme") is 0);
    }

    public Task<bool?> WriteAsync(bool on)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(PersonalizeKey);
            var light = on ? 0 : 1;
            key.SetValue("AppsUseLightTheme", light, RegistryValueKind.DWord);
            key.SetValue("SystemUsesLightTheme", light, RegistryValueKind.DWord);

            // Beri tahu Windows dan aplikasi lain bahwa tema berubah, supaya langsung berganti tanpa logout.
            _ = SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, IntPtr.Zero, "ImmersiveColorSet", SMTO_ABORTIFHUNG, 200, out _);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            StartupLog.Write($"Mode gelap gagal diubah: {ex.Message}");
        }

        return ReadAsync();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);
}
