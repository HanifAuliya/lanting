using Lanting.Core.Island;

namespace Lanting.Core.Modules;

/// <summary>
/// Kontrak setiap fitur (media, lirik, baterai, dan modul baru nanti).
/// Fitur baru cukup implement interface ini lalu didaftarkan di DI; tidak ada file lain yang diubah.
/// </summary>
public interface IIslandModule
{
    /// <summary>Id unik dan stabil, dipakai juga sebagai <see cref="IslandContent.ModuleId"/>.</summary>
    string Id { get; }

    /// <summary>Mulai mendengarkan event. Jangan polling; daftar ke event Windows lalu panggil host.</summary>
    Task StartAsync(IIslandHost host, CancellationToken cancellationToken);

    /// <summary>Lepas semua event dan sumber daya. Setelah ini modul tidak boleh memanggil host lagi.</summary>
    Task StopAsync();
}
