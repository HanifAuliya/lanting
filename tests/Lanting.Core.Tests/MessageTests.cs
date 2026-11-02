using Lanting.Core.Messages;
using static Lanting.Core.Tests.MediaModuleTests;

namespace Lanting.Core.Tests;

public class MessageTests
{
    private const string WhatsAppId = "5319275A.WhatsAppDesktop_cv1g1gvanyjgm!App";

    private readonly FakeToastSource _source = new();
    private readonly RecordingHost _host = new();

    [Fact]
    public void WhatsApp_desktop_message_is_recognized()
    {
        var message = MessageModule.Recognize(new ToastNotice(1, "WhatsApp", WhatsAppId, ["Budi", "Nanti jadi ke kampus?"]));

        Assert.NotNull(message);
        Assert.Equal(ChatApp.WhatsApp, message.App);
        Assert.Equal("Budi", message.Sender);
        Assert.Equal("Nanti jadi ke kampus?", message.Text);
        Assert.Equal($@"shell:AppsFolder\{WhatsAppId}", message.OpenTarget);
    }

    [Fact]
    public void WhatsApp_web_in_a_browser_is_recognized_and_the_site_line_dropped()
    {
        var message = MessageModule.Recognize(new ToastNotice(2, "Google Chrome", "Chrome", ["Ani", "Oke siap", "web.whatsapp.com"]));

        Assert.NotNull(message);
        Assert.Equal(ChatApp.WhatsApp, message.App);
        Assert.Equal("Oke siap", message.Text);
        Assert.Equal("https://web.whatsapp.com", message.OpenTarget);
    }

    [Fact]
    public void Discord_message_without_body_still_shows()
    {
        var message = MessageModule.Recognize(new ToastNotice(3, "Discord", "com.squirrel.Discord.Discord", ["Server Kampus"]));

        Assert.NotNull(message);
        Assert.Equal(ChatApp.Discord, message.App);
        Assert.Equal("Pesan baru", message.Text);
    }

    [Theory]
    [InlineData("Microsoft Outlook", "Rapat besok")]
    [InlineData("Google Chrome", "youtube.com")]
    public void Other_notifications_are_ignored(string app, string text)
    {
        Assert.Null(MessageModule.Recognize(new ToastNotice(4, app, null, ["Judul", text])));
    }

    [Fact]
    public async Task Messages_show_as_short_notifications_and_respect_the_switches()
    {
        var module = new MessageModule(_source);
        await module.StartAsync(_host, CancellationToken.None);
        module.UpdateOptions(new MessageOptions { Discord = false });

        _source.Raise(new ToastNotice(1, "WhatsApp", WhatsAppId, ["Budi", "Halo"]));
        _source.Raise(new ToastNotice(2, "Discord", "com.squirrel.Discord.Discord", ["Server", "Halo"]));

        var content = Assert.Single(_host.Shown);
        Assert.True(content.IsNotification);
        Assert.Equal(ChatApp.WhatsApp, Assert.IsType<MessageNotice>(content.Payload).App);
    }

    private sealed class FakeToastSource : IToastSource
    {
        public event EventHandler<ToastNotice>? Arrived;

        public void Raise(ToastNotice toast) => Arrived?.Invoke(this, toast);

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync() => Task.CompletedTask;
    }
}
