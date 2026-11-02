using System.Diagnostics;
using Lanting.App.Shell;
using Lanting.Core.Island;
using Lanting.Core.Messages;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using static Lanting.App.Views.Dashboard.DashboardStyle;

namespace Lanting.App.Views;

/// <summary>
/// Pesan masuk dari WhatsApp atau Discord: lingkaran berwarna aplikasinya, nama pengirim, dan isi pesan.
/// Diklik: membuka aplikasinya (atau WhatsApp Web di browser).
/// </summary>
internal sealed class MessageView : IIslandView
{
    private const string ChatGlyph = "\uE8BD";

    private static readonly Color WhatsAppGreen = Color.FromArgb(255, 0x25, 0xD3, 0x66);
    private static readonly Color DiscordBlurple = Color.FromArgb(255, 0x58, 0x65, 0xF2);

    private readonly SolidColorBrush _brand = new(WhatsAppGreen);
    private readonly TextBlock _compactText;
    private readonly TextBlock _sender;
    private readonly TextBlock _text;
    private readonly TextBlock _app;
    private MessageNotice? _message;

    public MessageView(IslandTheme theme)
    {
        // Pil kecil (jarang dipakai; pesan biasanya tampil sebagai notifikasi): [ikon] Pengirim: pesan
        var compactBadge = Badge(18, 9);
        _compactText = Text(theme.Primary, 12);
        _compactText.VerticalAlignment = VerticalAlignment.Center;
        var compact = new Grid { Margin = new Thickness(8, 0, 14, 0), ColumnSpacing = 8 };
        compact.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        compact.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        compact.Children.Add(compactBadge);
        Grid.SetColumn(_compactText, 1);
        compact.Children.Add(_compactText);
        Compact = compact;

        // Pil notifikasi: [ikon] Pengirim / isi pesan ... WhatsApp
        _sender = Text(theme.Primary, 14, bold: true);
        _text = Text(theme.Secondary, 12);
        _app = Caption(theme);
        _app.VerticalAlignment = VerticalAlignment.Top;
        _app.Margin = new Thickness(0, 2, 0, 0);
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
        words.Children.Add(_sender);
        words.Children.Add(_text);

        var notice = new Grid { Margin = new Thickness(14, 0, 20, 0), ColumnSpacing = 12 };
        notice.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        notice.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        notice.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        notice.Children.Add(Badge(38, 17));
        Grid.SetColumn(words, 1);
        notice.Children.Add(words);
        var appLabel = new Grid { VerticalAlignment = VerticalAlignment.Center, Children = { _app } };
        Grid.SetColumn(appLabel, 2);
        notice.Children.Add(appLabel);

        var open = Pressable(notice, "Buka", 26, theme.Transparent, theme.Surface, theme.Control, theme.Primary);
        open.HorizontalAlignment = HorizontalAlignment.Stretch;
        open.VerticalAlignment = VerticalAlignment.Stretch;
        open.Click += (_, _) => Open();
        Notification = open;
    }

    public UIElement Compact { get; }

    public UIElement? Notification { get; }

    public double CompactWidth => 300;

    public bool CanShow(IslandContent content) => content.Payload is MessageNotice;

    public void Show(IslandContent content)
    {
        if (content.Payload is not MessageNotice message)
        {
            return;
        }

        _message = message;
        _brand.Color = message.App == ChatApp.WhatsApp ? WhatsAppGreen : DiscordBlurple;
        _compactText.Text = $"{message.Sender}: {message.Text}";
        _sender.Text = message.Sender;
        _text.Text = message.Text;
        _app.Text = message.App == ChatApp.WhatsApp ? "WHATSAPP" : "DISCORD";
        Motion.Pop(_sender, _text);
    }

    // Lingkaran warna aplikasi dengan ikon gelembung chat putih.
    private Border Badge(double size, double glyph)
    {
        var icon = Icon(ChatGlyph, glyph);
        icon.Foreground = new SolidColorBrush(Microsoft.UI.Colors.White);
        return new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 2),
            Background = _brand,
            VerticalAlignment = VerticalAlignment.Center,
            Child = icon,
        };
    }

    private void Open()
    {
        if (_message is not { } message)
        {
            return;
        }

        try
        {
            // Aplikasi dari Start menu dibuka lewat Explorer; alamat web dan protokol lewat aplikasi bawaannya.
            var start = message.OpenTarget.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)
                ? new ProcessStartInfo("explorer.exe", message.OpenTarget)
                : new ProcessStartInfo(message.OpenTarget) { UseShellExecute = true };
            Process.Start(start);
        }
        catch (Exception ex)
        {
            StartupLog.Write($"Pesan: {message.App} gagal dibuka: {ex.Message}");
        }
    }
}
