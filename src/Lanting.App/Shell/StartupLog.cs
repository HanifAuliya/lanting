using System.Runtime.InteropServices;

namespace Lanting.App.Shell;

/// <summary>
/// Log sederhana ke %LOCALAPPDATA%\Lanting\lanting.log, supaya error saat startup tidak hilang tanpa jejak.
/// Error fatal juga ditampilkan sebagai kotak pesan.
/// </summary>
internal static class StartupLog
{
    private const long MaxBytes = 256 * 1024;
    private static readonly Lock Gate = new();

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Lanting",
        "lanting.log");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxBytes)
                {
                    File.Delete(FilePath);
                }

                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
        }
        catch (IOException)
        {
            // Log tidak boleh membuat aplikasi ikut gagal.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public static void Fatal(string context, Exception? exception)
    {
        Write($"FATAL {context}: {exception}");
        ShowMessage(
            $"Lanting gagal berjalan.\n\n{exception?.GetType().Name}: {exception?.Message}\n\nDetail lengkap ada di:\n{FilePath}",
            isError: true);
    }

    public static void ShowMessage(string text, bool isError = false)
    {
        // Di CI tidak ada orang yang menutup kotak pesan.
        if (Environment.GetEnvironmentVariable("LANTING_NO_DIALOG") == "1")
        {
            return;
        }

        const uint MB_ICONERROR = 0x10;
        const uint MB_ICONINFORMATION = 0x40;
        const uint MB_TOPMOST = 0x40000;
        _ = MessageBoxW(IntPtr.Zero, text, "Lanting", (isError ? MB_ICONERROR : MB_ICONINFORMATION) | MB_TOPMOST);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hwnd, string text, string caption, uint type);
}
