using Lanting.App.Shell;
using Lanting.Core.Messages;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace Lanting.App.Messages;

/// <summary>
/// Membaca notifikasi aplikasi lain lewat UserNotificationListener (izin "Akses notifikasi" Windows).
/// Event NotificationChanged tidak tersedia untuk aplikasi tanpa paket seperti Lanting, jadi daftar notifikasi
/// dibaca tiap 2 detik dan yang Id-nya baru dikirim lewat <see cref="Arrived"/>. Notifikasi yang sudah ada saat
/// Lanting dinyalakan tidak dikirim ulang.
/// </summary>
internal sealed class ToastSource : IToastSource, IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);
    private static readonly string[] Browsers = ["chrome", "edge", "firefox", "brave", "opera", "vivaldi", "zen", "arc"];

    private readonly HashSet<uint> _seen = [];
    private UserNotificationListener? _listener;
    private Timer? _timer;
    private int _polling;

    public event EventHandler<ToastNotice>? Arrived;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var listener = UserNotificationListener.Current;
            var access = listener.GetAccessStatus();
            if (access != UserNotificationListenerAccessStatus.Allowed)
            {
                access = await listener.RequestAccessAsync();
            }

            if (access != UserNotificationListenerAccessStatus.Allowed)
            {
                StartupLog.Write($"Pesan: akses notifikasi Windows {access}; notifikasi pesan tidak bisa dibaca.");
                return;
            }

            foreach (var existing in await listener.GetNotificationsAsync(NotificationKinds.Toast))
            {
                _seen.Add(existing.Id);
            }

            _listener = listener;
            _timer = new Timer(_ => _ = PollAsync(), null, Interval, Interval);
            StartupLog.Write("Pesan: mendengarkan notifikasi WhatsApp dan Discord.");
        }
        catch (Exception ex)
        {
            StartupLog.Write($"Pesan: notifikasi Windows tidak bisa dibaca: {ex.Message}");
        }
    }

    public Task StopAsync()
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
        _listener = null;
    }

    private async Task PollAsync()
    {
        // Jangan menumpuk bila Windows lambat menjawab.
        if (_listener is not { } listener || Interlocked.Exchange(ref _polling, 1) == 1)
        {
            return;
        }

        try
        {
            var current = await listener.GetNotificationsAsync(NotificationKinds.Toast);
            var ids = new HashSet<uint>();
            foreach (var notification in current)
            {
                ids.Add(notification.Id);
                if (_seen.Contains(notification.Id))
                {
                    continue;
                }

                if (Read(notification) is { } toast)
                {
                    Arrived?.Invoke(this, toast);
                }
            }

            // Hanya ingat yang masih ada di pusat notifikasi, supaya daftarnya tidak terus membesar.
            _seen.Clear();
            _seen.UnionWith(ids);
        }
        catch (Exception ex)
        {
            StartupLog.Write($"Pesan: gagal membaca notifikasi: {ex.Message}");
        }
        finally
        {
            Volatile.Write(ref _polling, 0);
        }
    }

    private static ToastNotice? Read(UserNotification notification)
    {
        try
        {
            var name = notification.AppInfo?.DisplayInfo?.DisplayName ?? string.Empty;
            var binding = notification.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric);
            var texts = binding?.GetTextElements().Select(t => t.Text ?? string.Empty).ToList() ?? [];

            // Untuk mencari tahu bentuk notifikasi browser bila pesan web tidak dikenali: hanya nama aplikasi,
            // jumlah baris, dan baris terakhir bila itu alamat situs (isi pesan tidak dicatat).
            if (Browsers.Any(b => name.Contains(b, StringComparison.OrdinalIgnoreCase)))
            {
                var site = texts.LastOrDefault(t => t.Contains('.') && !t.Contains(' '));
                StartupLog.Write($"Pesan: notifikasi {name}, {texts.Count} baris{(site is null ? string.Empty : $", situs {site}")}");
            }

            return new ToastNotice(notification.Id, name, notification.AppInfo?.AppUserModelId, texts);
        }
        catch (Exception ex)
        {
            StartupLog.Write($"Pesan: satu notifikasi tidak terbaca: {ex.Message}");
            return null;
        }
    }
}
