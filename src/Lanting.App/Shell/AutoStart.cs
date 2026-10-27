using Microsoft.Win32;

namespace Lanting.App.Shell;

/// <summary>
/// "Mulai bersama Windows" lewat kunci Run milik pengguna (HKCU), sama seperti aplikasi desktop biasa.
/// Tidak butuh hak admin. Installer juga menulis kunci yang sama.
/// </summary>
internal static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Lanting";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
    }

    public static void Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled)
            {
                key.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            StartupLog.Write($"Gagal mengubah mulai bersama Windows: {ex.Message}");
        }
    }
}
