namespace Lanting.Core.Media;

/// <summary>
/// Menebak nama aplikasi dari id-nya. Cadangan bila Windows sendiri tidak tahu namanya
/// (misalnya "Spotify.exe" yang tidak terdaftar di folder Apps).
/// </summary>
public static class MediaApps
{
    private static readonly (string Fragment, string Name)[] Known =
    [
        ("spotify", "Spotify"),
        ("music.youtube", "YouTube Music"),
        ("msedge", "Edge"),
        ("chrome", "Chrome"),
        ("firefox", "Firefox"),
        ("brave", "Brave"),
        ("opera", "Opera"),
        ("zunemusic", "Media Player"),
        ("vlc", "VLC"),
    ];

    public static string DisplayName(string appId)
    {
        if (string.IsNullOrWhiteSpace(appId))
        {
            return string.Empty;
        }

        foreach (var (fragment, known) in Known)
        {
            if (appId.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return known;
            }
        }

        // Aplikasi Store: "Penerbit.Aplikasi_hash!App" -> "App".
        var name = appId;
        var bang = name.LastIndexOf('!');
        if (bang >= 0 && bang < name.Length - 1)
        {
            name = name[(bang + 1)..];
        }

        // Aplikasi biasa: "C:\...\foo.exe" atau "foo.exe" -> "foo".
        // Tidak pakai Path.GetFileName supaya hasilnya sama di luar Windows (tes).
        name = name[(name.LastIndexOfAny(['\\', '/']) + 1)..];
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        return name;
    }
}
