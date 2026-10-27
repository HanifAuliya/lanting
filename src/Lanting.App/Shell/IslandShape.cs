using Lanting.Core.Island;

namespace Lanting.App.Shell;

/// <summary>Ukuran pil (dalam DIP) untuk setiap state.</summary>
internal readonly record struct IslandShape(double Width, double Height, double CornerRadius)
{
    public static readonly IslandShape Compact = new(200, 34, 17);
    public static readonly IslandShape Notification = new(360, 64, 32);
    public static readonly IslandShape Expanded = new(400, 160, 36);

    /// <summary>Ukuran terbesar; elemen pil selalu sebesar ini lalu dipotong oleh clip yang dianimasikan.</summary>
    public static readonly IslandShape Max = Expanded;

    public static IslandShape For(IslandState state) => state switch
    {
        IslandState.Expanded => Expanded,
        IslandState.Notification => Notification,
        _ => Compact,
    };
}
