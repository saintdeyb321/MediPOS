namespace MediPOS.Domain.Modules.IdentityAccess;

public static class WorkDayCodes
{
    public static string ToCode(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "mon",
        DayOfWeek.Tuesday => "tue",
        DayOfWeek.Wednesday => "wed",
        DayOfWeek.Thursday => "thu",
        DayOfWeek.Friday => "fri",
        DayOfWeek.Saturday => "sat",
        DayOfWeek.Sunday => "sun",
        _ => throw new ArgumentOutOfRangeException(nameof(day)),
    };

    public static DayOfWeek FromCode(string code) => code switch
    {
        "mon" => DayOfWeek.Monday,
        "tue" => DayOfWeek.Tuesday,
        "wed" => DayOfWeek.Wednesday,
        "thu" => DayOfWeek.Thursday,
        "fri" => DayOfWeek.Friday,
        "sat" => DayOfWeek.Saturday,
        "sun" => DayOfWeek.Sunday,
        _ => throw new InvalidOperationException("Unknown persisted work schedule day."),
    };
}
