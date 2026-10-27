namespace Lanting.Core.Island;

/// <summary>Bentuk island di layar. Lihat diagram state di dokumen rencana.</summary>
public enum IslandState
{
    /// <summary>Tidak terlihat sama sekali.</summary>
    Hidden,

    /// <summary>Pil kecil di tengah atas.</summary>
    Compact,

    /// <summary>Pil besar saat di-hover atau diklik.</summary>
    Expanded,

    /// <summary>Pil sedang untuk notifikasi sementara.</summary>
    Notification,
}
