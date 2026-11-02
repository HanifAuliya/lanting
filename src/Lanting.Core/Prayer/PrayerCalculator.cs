namespace Lanting.Core.Prayer;

/// <summary>Waktu-waktu dalam jadwal sholat harian, berurutan.</summary>
public enum PrayerName
{
    Imsak,
    Subuh,
    Terbit,
    Dzuhur,
    Ashar,
    Maghrib,
    Isya,
}

/// <summary>Satu waktu di jadwal.</summary>
/// <param name="IsPrayer">Waktu sholat wajib (bukan Imsak atau Terbit); hanya ini yang diberi notifikasi.</param>
public sealed record PrayerTime(PrayerName Name, DateTime Time)
{
    public bool IsPrayer => Name is not (PrayerName.Imsak or PrayerName.Terbit);

    public string Label => Name switch
    {
        PrayerName.Imsak => "Imsak",
        PrayerName.Subuh => "Subuh",
        PrayerName.Terbit => "Terbit",
        PrayerName.Dzuhur => "Dzuhur",
        PrayerName.Ashar => "Ashar",
        PrayerName.Maghrib => "Maghrib",
        _ => "Isya",
    };
}

/// <summary>
/// Menghitung jadwal sholat di laptop, tanpa internet, dengan kriteria Kementerian Agama RI:
/// Subuh saat matahari 20° di bawah ufuk, Isya 18°, Ashar saat bayangan = tinggi benda + bayangan tengah hari (Syafi'i),
/// ditambah ihtiyat (waktu pengaman) lalu dibulatkan ke atas per menit. Imsak 10 menit sebelum Subuh.
/// Rumus posisi matahari dari praytimes.org (cukup akurat sampai kurang dari satu menit).
/// </summary>
public static class PrayerCalculator
{
    private const double FajrAngle = 20;
    private const double IshaAngle = 18;

    // Matahari terbit/terbenam: piringan atas menyentuh ufuk, ditambah pembiasan cahaya.
    private const double RiseSetAngle = 0.833;

    // Ihtiyat dalam menit, mengikuti jadwal Kemenag (dicocokkan di tes dengan jadwal resmi).
    private const double Ihtiyat = 2;
    private const double DzuhurIhtiyat = 3;
    private static readonly TimeSpan ImsakBeforeSubuh = TimeSpan.FromMinutes(10);

    /// <param name="utcOffset">Selisih zona waktu lokasi dari UTC (WIB +7, WITA +8, WIT +9).</param>
    public static IReadOnlyList<PrayerTime> For(DateOnly date, double latitude, double longitude, TimeSpan utcOffset)
    {
        var jd = JulianDay(date) - (longitude / (15 * 24));
        var zone = utcOffset.TotalHours - (longitude / 15);

        var noon = MidDay(jd, 12);
        var dzuhur = noon + zone;
        var subuh = noon - SunAngleTime(jd, latitude, FajrAngle, 5) + zone;
        var terbit = noon - SunAngleTime(jd, latitude, RiseSetAngle, 6) + zone;
        var ashar = noon + AsrTime(jd, latitude, 13) + zone;
        var maghrib = noon + SunAngleTime(jd, latitude, RiseSetAngle, 18) + zone;
        var isya = noon + SunAngleTime(jd, latitude, IshaAngle, 18) + zone;

        var day = date.ToDateTime(TimeOnly.MinValue);
        var subuhTime = RoundUp(day, subuh, Ihtiyat);
        return
        [
            new(PrayerName.Imsak, subuhTime - ImsakBeforeSubuh),
            new(PrayerName.Subuh, subuhTime),
            new(PrayerName.Terbit, RoundDown(day, terbit, -Ihtiyat)),
            new(PrayerName.Dzuhur, RoundUp(day, dzuhur, DzuhurIhtiyat)),
            new(PrayerName.Ashar, RoundUp(day, ashar, Ihtiyat)),
            new(PrayerName.Maghrib, RoundUp(day, maghrib, Ihtiyat)),
            new(PrayerName.Isya, RoundUp(day, isya, Ihtiyat)),
        ];
    }

    /// <summary>Waktu sholat wajib berikutnya setelah <paramref name="now"/> (bisa Subuh besok).</summary>
    public static PrayerTime Next(DateTime now, double latitude, double longitude, TimeSpan utcOffset)
    {
        var today = DateOnly.FromDateTime(now);
        foreach (var day in new[] { today, today.AddDays(1) })
        {
            foreach (var time in For(day, latitude, longitude, utcOffset))
            {
                if (time.IsPrayer && time.Time > now)
                {
                    return time;
                }
            }
        }

        throw new InvalidOperationException("Tidak ada waktu sholat dalam dua hari.");
    }

    private static DateTime RoundUp(DateTime day, double hours, double minutes)
    {
        var total = (hours * 60) + minutes;
        return day.AddMinutes(Math.Ceiling(total - 1e-9));
    }

    private static DateTime RoundDown(DateTime day, double hours, double minutes) =>
        day.AddMinutes(Math.Floor((hours * 60) + minutes));

    private static double JulianDay(DateOnly date)
    {
        var (year, month) = date.Month <= 2 ? (date.Year - 1, date.Month + 12) : (date.Year, date.Month);
        var a = Math.Floor(year / 100.0);
        var b = 2 - a + Math.Floor(a / 4);
        return Math.Floor(365.25 * (year + 4716)) + Math.Floor(30.6001 * (month + 1)) + date.Day + b - 1524.5;
    }

    // Deklinasi matahari dan equation of time (jam) pada hari Julian tertentu.
    private static (double Declination, double EquationOfTime) SunPosition(double jd)
    {
        var d = jd - 2451545.0;
        var g = FixAngle(357.529 + (0.98560028 * d));
        var q = FixAngle(280.459 + (0.98564736 * d));
        var l = FixAngle(q + (1.915 * Sin(g)) + (0.020 * Sin(2 * g)));
        var e = 23.439 - (0.00000036 * d);
        var ra = ArcTan2(Cos(e) * Sin(l), Cos(l)) / 15;
        var eqt = (q / 15) - FixHour(ra);
        var declination = ArcSin(Sin(e) * Sin(l));
        return (declination, eqt);
    }

    // Tengah hari matahari (jam, waktu "lokal matahari" sebelum koreksi zona). hourGuess: perkiraan jam untuk posisi matahari.
    private static double MidDay(double jd, double hourGuess) =>
        FixHour(12 - SunPosition(jd + (hourGuess / 24)).EquationOfTime);

    // Selisih jam dari tengah hari sampai matahari berada di sudut tertentu di bawah ufuk.
    private static double SunAngleTime(double jd, double latitude, double angle, double hourGuess)
    {
        var declination = SunPosition(jd + (hourGuess / 24)).Declination;
        var cos = (-Sin(angle) - (Sin(declination) * Sin(latitude))) / (Cos(declination) * Cos(latitude));
        return ArcCos(Math.Clamp(cos, -1, 1)) / 15;
    }

    // Ashar (Syafi'i): bayangan = 1 x tinggi benda + bayangan saat tengah hari.
    private static double AsrTime(double jd, double latitude, double hourGuess)
    {
        var declination = SunPosition(jd + (hourGuess / 24)).Declination;
        var angle = -ArcCot(1 + Tan(Math.Abs(latitude - declination)));
        return SunAngleTime(jd, latitude, angle, hourGuess);
    }

    private static double Rad(double degrees) => degrees * Math.PI / 180;

    private static double Deg(double radians) => radians * 180 / Math.PI;

    private static double Sin(double d) => Math.Sin(Rad(d));

    private static double Cos(double d) => Math.Cos(Rad(d));

    private static double Tan(double d) => Math.Tan(Rad(d));

    private static double ArcSin(double x) => Deg(Math.Asin(x));

    private static double ArcCos(double x) => Deg(Math.Acos(x));

    private static double ArcTan2(double y, double x) => Deg(Math.Atan2(y, x));

    private static double ArcCot(double x) => Deg(Math.Atan(1 / x));

    private static double FixAngle(double a) => a - (360 * Math.Floor(a / 360));

    private static double FixHour(double h) => h - (24 * Math.Floor(h / 24));
}
