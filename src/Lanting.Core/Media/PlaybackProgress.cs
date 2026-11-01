namespace Lanting.Core.Media;

/// <summary>Titik acuan posisi lagu; tampilan menghitung posisi sekarang sendiri, modul tidak mengirim update tiap detik.</summary>
/// <param name="Position">Posisi pada waktu <paramref name="At"/>.</param>
/// <param name="Duration">Panjang lagu, selalu lebih dari nol.</param>
/// <param name="At">Kapan <paramref name="Position"/> berlaku.</param>
/// <param name="IsPlaying">Bila diputar, posisi maju mengikuti jam; bila dijeda, posisi tetap.</param>
public sealed record PlaybackProgress(TimeSpan Position, TimeSpan Duration, DateTimeOffset At, bool IsPlaying)
{
    /// <summary>Posisi pada waktu <paramref name="now"/>, dibatasi antara 0 dan <see cref="Duration"/>.</summary>
    public TimeSpan PositionAt(DateTimeOffset now)
    {
        var position = IsPlaying && now > At ? Position + (now - At) : Position;
        return position < TimeSpan.Zero ? TimeSpan.Zero : position > Duration ? Duration : position;
    }
}
