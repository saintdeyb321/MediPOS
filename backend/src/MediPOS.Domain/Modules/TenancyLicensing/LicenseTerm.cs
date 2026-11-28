namespace MediPOS.Domain.Modules.TenancyLicensing;

public static class LicenseTerm
{
    public const int TrialDays = 7;
    private static readonly TimeZoneInfo Lima = TimeZoneInfo.FindSystemTimeZoneById("America/Lima");

    public static bool IsPlan(int months) => months is 1 or 3 or 6 or 12;

    public static DateTimeOffset Expiration(DateTimeOffset startsAt, int months)
    {
        if (months == 0) return startsAt.ToUniversalTime().AddDays(TrialDays);
        if (!IsPlan(months)) throw new ArgumentOutOfRangeException(nameof(months));

        var localEnd = TimeZoneInfo.ConvertTime(startsAt, Lima).DateTime.AddMonths(months);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localEnd, Lima));
    }
}
