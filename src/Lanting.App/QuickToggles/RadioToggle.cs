using Lanting.App.Shell;
using Windows.Devices.Radios;

namespace Lanting.App.QuickToggles;

/// <summary>Wi-Fi atau Bluetooth, lewat API radio Windows (sama dengan tombol di Action Center).</summary>
internal sealed class RadioToggle : IQuickToggle
{
    private readonly RadioKind _kind;
    private Radio? _radio;

    public RadioToggle(RadioKind kind, string label, string glyph)
    {
        _kind = kind;
        Label = label;
        Glyph = glyph;
    }

    public string Label { get; }

    public string Glyph { get; }

    public async Task<bool?> ReadAsync()
    {
        var radio = await FindAsync();
        return radio is null ? null : radio.State == RadioState.On;
    }

    public async Task<bool?> WriteAsync(bool on)
    {
        var radio = await FindAsync();
        if (radio is null)
        {
            return null;
        }

        var result = await radio.SetStateAsync(on ? RadioState.On : RadioState.Off);
        if (result != RadioAccessStatus.Allowed)
        {
            // Misalnya "Izinkan aplikasi mengontrol radio perangkat" dimatikan di Pengaturan privasi Windows.
            StartupLog.Write($"{Label}: tidak diizinkan mengubah radio ({result}).");
        }

        return radio.State == RadioState.On;
    }

    private async Task<Radio?> FindAsync()
    {
        if (_radio is not null)
        {
            return _radio;
        }

        try
        {
            if (await Radio.RequestAccessAsync() != RadioAccessStatus.Allowed)
            {
                return null;
            }

            _radio = (await Radio.GetRadiosAsync()).FirstOrDefault(r => r.Kind == _kind);
        }
        catch (Exception ex)
        {
            StartupLog.Write($"{Label}: radio tidak bisa dibaca: {ex.Message}");
        }

        return _radio;
    }
}
