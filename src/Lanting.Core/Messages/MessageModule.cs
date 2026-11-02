using Lanting.Core.Island;
using Lanting.Core.Modules;

namespace Lanting.Core.Messages;

/// <summary>Aplikasi chat yang notifikasinya ditampilkan di island.</summary>
public enum ChatApp
{
    WhatsApp,
    Discord,
}

/// <summary>Notifikasi Windows apa adanya, dari aplikasi mana pun.</summary>
/// <param name="AppName">Nama aplikasi pengirim notifikasi, misalnya "WhatsApp" atau "Google Chrome".</param>
/// <param name="AppUserModelId">Id aplikasi di Windows, untuk membukanya lagi.</param>
/// <param name="Texts">Teks notifikasi berurutan (biasanya judul, isi, lalu nama situs untuk notifikasi browser).</param>
public sealed record ToastNotice(uint Id, string AppName, string? AppUserModelId, IReadOnlyList<string> Texts);

/// <summary>Payload konten island: pesan masuk.</summary>
/// <param name="OpenTarget">Yang dibuka saat notifikasi diklik: "shell:AppsFolder\..." untuk aplikasi, atau alamat web.</param>
public sealed record MessageNotice(ChatApp App, string Sender, string Text, string OpenTarget);

/// <summary>Sumber notifikasi Windows (di aplikasi: UserNotificationListener).</summary>
public interface IToastSource
{
    /// <summary>Notifikasi baru masuk. Bisa dari thread mana pun.</summary>
    event EventHandler<ToastNotice>? Arrived;

    Task StartAsync(CancellationToken cancellationToken);

    Task StopAsync();
}

/// <summary>Pilihan notifikasi pesan dari Pengaturan.</summary>
public sealed record MessageOptions
{
    public bool WhatsApp { get; init; } = true;

    public bool Discord { get; init; } = true;

    public bool Enabled(ChatApp app) => app switch
    {
        ChatApp.WhatsApp => WhatsApp,
        _ => Discord,
    };
}

/// <summary>
/// Menampilkan pesan WhatsApp (aplikasi dan web) dan Discord di island. Notifikasi lain diabaikan.
/// Pesan dari pengirim yang sama menggantikan notifikasi sebelumnya, bukan mengantre.
/// </summary>
public sealed class MessageModule : IIslandModule
{
    public const string ModuleId = "messages";

    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(6);
    private static readonly string[] Browsers = ["chrome", "edge", "firefox", "brave", "opera", "vivaldi", "zen", "arc"];

    private readonly IToastSource _source;
    private readonly Lock _gate = new();
    private MessageOptions _options = new();
    private IIslandHost? _host;

    public MessageModule(IToastSource source)
    {
        _source = source;
    }

    public string Id => ModuleId;

    public void UpdateOptions(MessageOptions options)
    {
        lock (_gate)
        {
            _options = options;
        }
    }

    public async Task StartAsync(IIslandHost host, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _host = host;
        }

        _source.Arrived += OnArrived;
        await _source.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync()
    {
        _source.Arrived -= OnArrived;
        await _source.StopAsync().ConfigureAwait(false);
        lock (_gate)
        {
            _host = null;
        }
    }

    /// <summary>
    /// Kenali pesan chat dari notifikasi Windows. Aplikasi WhatsApp/Discord dikenali dari namanya; notifikasi browser
    /// dikenali dari nama situs di teksnya (web.whatsapp.com, discord.com). Baris berisi nama situs tidak ikut ditampilkan.
    /// </summary>
    public static MessageNotice? Recognize(ToastNotice toast)
    {
        var name = toast.AppName;
        var browser = Browsers.Any(b => name.Contains(b, StringComparison.OrdinalIgnoreCase));
        ChatApp? app = null;
        string? open = null;
        if (name.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase))
        {
            app = ChatApp.WhatsApp;
        }
        else if (name.Contains("Discord", StringComparison.OrdinalIgnoreCase))
        {
            app = ChatApp.Discord;
        }
        else if (browser && toast.Texts.Any(t => t.Contains("whatsapp.com", StringComparison.OrdinalIgnoreCase)))
        {
            app = ChatApp.WhatsApp;
            open = "https://web.whatsapp.com";
        }
        else if (browser && toast.Texts.Any(t => t.Contains("discord.com", StringComparison.OrdinalIgnoreCase)))
        {
            app = ChatApp.Discord;
            open = "https://discord.com/app";
        }

        if (app is not { } chat)
        {
            return null;
        }

        var lines = toast.Texts
            .Where(t => !string.IsNullOrWhiteSpace(t)
                && !t.Contains("whatsapp.com", StringComparison.OrdinalIgnoreCase)
                && !t.Contains("discord.com", StringComparison.OrdinalIgnoreCase))
            .Select(t => t.Trim())
            .ToList();
        if (lines.Count == 0)
        {
            return null;
        }

        var sender = lines[0];
        var text = lines.Count > 1 ? string.Join(" ", lines.Skip(1)) : "Pesan baru";
        open ??= toast.AppUserModelId is { Length: > 0 } aumid ? $@"shell:AppsFolder\{aumid}" : chat == ChatApp.WhatsApp ? "whatsapp:" : "discord:";
        return new MessageNotice(chat, sender, text, open);
    }

    private void OnArrived(object? sender, ToastNotice toast)
    {
        if (Recognize(toast) is not { } message)
        {
            return;
        }

        lock (_gate)
        {
            if (_host is null || !_options.Enabled(message.App))
            {
                return;
            }

            _host.Show(new IslandContent(ModuleId, $"{message.App}/{message.Sender}", IslandPriority.Alert, message, Duration));
        }
    }
}
