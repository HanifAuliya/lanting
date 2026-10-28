namespace Lanting.Core.Clock;

/// <summary>Seberapa jauh hari ini sudah berjalan (dari tengah malam ke tengah malam).</summary>
/// <param name="Fraction">0 tepat tengah malam, mendekati 1 menjelang tengah malam berikutnya.</param>
/// <param name="Remaining">Sisa waktu sampai tengah malam.</param>
public readonly record struct DayProgress(double Fraction, TimeSpan Remaining)
{
    public int Percent => (int)Math.Floor(Fraction * 100);

    public static DayProgress At(DateTime localTime)
    {
        var elapsed = localTime.TimeOfDay;
        var day = TimeSpan.FromDays(1);
        return new DayProgress(elapsed / day, day - elapsed);
    }
}
