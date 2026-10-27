namespace Lanting.Core.Island;

/// <summary>
/// Satu-satunya pintu modul untuk tampil di island. Modul tidak pernah menyentuh UI langsung.
/// Aman dipanggil dari thread mana pun.
/// </summary>
public interface IIslandHost
{
    void Show(IslandContent content);

    void Withdraw(string moduleId, string key);
}
