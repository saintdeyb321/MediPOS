using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Domain.Modules.Cash;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.Inventory;

namespace MediPOS.Application.Modules.Reporting.Operational;

public enum OperationalPeriodType { Day, Week, Month, Year, Custom }
public sealed record OperationalPeriodInput(OperationalPeriodType Type, DateOnly? FromLocalDate = null, DateOnly? ToLocalDate = null);
public sealed record OperationalReportPeriod(OperationalPeriodType Type, DateOnly FromLocalDate, DateOnly ToLocalDate,
    DateTimeOffset StartUtc, DateTimeOffset EndExclusiveUtc)
{
    public string TimeZoneId { get; init; } = "America/Lima";
    public string TypeCode => Type.ToString().ToLowerInvariant();
}

public static class OperationalReportPolicy
{
    public const int MaximumOffset = 10_000;
    public const int MaximumLimit = 100;
    private static readonly TimeZoneInfo Lima = TimeZoneInfo.FindSystemTimeZoneById("America/Lima");
    public static DateOnly Today(DateTimeOffset now) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, Lima).DateTime);

    // Named periods use FromLocalDate as an anchor, defaulting to today's business date; To is exclusive to Custom.
    public static OperationalReportPeriod Resolve(OperationalPeriodInput input, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!Enum.IsDefined(input.Type) || (input.Type != OperationalPeriodType.Custom && input.ToLocalDate.HasValue))
            throw new ArgumentException("Named periods accept an optional local anchor; Custom requires both inclusive dates.");
        var anchor = input.FromLocalDate ?? Today(now);
        var first = anchor; var last = anchor;
        switch (input.Type)
        {
            case OperationalPeriodType.Week:
                first = anchor.AddDays(-(((int)anchor.DayOfWeek + 6) % 7)); last = first.AddDays(6); break;
            case OperationalPeriodType.Month:
                first = new(anchor.Year, anchor.Month, 1); last = new(anchor.Year, anchor.Month, DateTime.DaysInMonth(anchor.Year, anchor.Month)); break;
            case OperationalPeriodType.Year:
                first = new(anchor.Year, 1, 1); last = new(anchor.Year, 12, 31); break;
            case OperationalPeriodType.Custom:
                if (!input.FromLocalDate.HasValue || !input.ToLocalDate.HasValue) throw new ArgumentException("Custom requires both dates.");
                first = input.FromLocalDate.Value; last = input.ToLocalDate.Value; break;
        }
        ValidateDates(first, last);
        static DateTime Midnight(DateOnly value) => value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new(input.Type, first, last, new(TimeZoneInfo.ConvertTimeToUtc(Midnight(first), Lima)),
            new(TimeZoneInfo.ConvertTimeToUtc(Midnight(last.AddDays(1)), Lima)));
    }

    public static void ValidateDates(DateOnly first, DateOnly last)
    {
        if (last < first || last.DayNumber - first.DayNumber >= 366 || last == DateOnly.MaxValue)
            throw new ArgumentException("An inclusive range of at most 366 days with a representable exclusive end is required.");
    }

    public static void ValidateScope(Guid tenant, Guid? branch, int offset = 0, int limit = 50)
    {
        if (tenant == Guid.Empty || branch == Guid.Empty || offset is < 0 or > MaximumOffset || limit is < 1 or > MaximumLimit)
            throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
    }

    public static async Task<AccessContext> RequireOwnerAsync(ResolveAccessContextHandler resolver, Guid tenant, Guid? branch,
        DateTimeOffset now, CancellationToken token)
    {
        var result = await resolver.HandleAsync(tenant, branch, now, token).ConfigureAwait(false);
        if (result.Context?.Role is TenantRole.Cashier or TenantRole.Pharmacist) throw new ApplicationErrorException(OperationalReportErrors.Forbidden);
        if (!result.IsAllowed || result.Context is null) throw new ApplicationErrorException(new(result.Code, ErrorCategory.Forbidden, "Operational access was denied."));
        if (result.Context.Role != TenantRole.Owner) throw new ApplicationErrorException(OperationalReportErrors.Forbidden);
        return result.Context;
    }

    internal static async Task<T> ReadOwnerAsync<T>(ResolveAccessContextHandler resolver, Guid tenant, Guid? branch,
        DateTimeOffset generatedAt, TimeProvider clock, Func<Task<T>> read, Action<T> validate, CancellationToken token)
    {
        await RequireOwnerAsync(resolver, tenant, branch, generatedAt, token).ConfigureAwait(false);
        try
        {
            var result = await read().ConfigureAwait(false);
            await RequireOwnerAsync(resolver, tenant, branch, clock.GetUtcNow(), token).ConfigureAwait(false);
            validate(result);
            return result;
        }
        catch (OverflowException) { throw new ApplicationErrorException(OperationalReportErrors.Overflow); }
        catch (ArgumentException) { throw new ApplicationErrorException(OperationalReportErrors.Corrupted); }
    }

    internal static OperationalReportPeriod RequirePeriod(OperationalPeriodInput input, DateTimeOffset now)
    {
        try { return Resolve(input, now); }
        catch (ArgumentException) { throw new ApplicationErrorException(OperationalReportErrors.InvalidPeriod); }
    }
}

public static class OperationalReportErrors
{
    public static readonly ApplicationError Forbidden = new("reports.owner_required", ErrorCategory.Forbidden, "Owner permission is required.");
    public static readonly ApplicationError InvalidPeriod = new("reports.invalid_period", ErrorCategory.Validation, "A valid Lima period of at most 366 days is required.");
    public static readonly ApplicationError InvalidSort = new("reports.invalid_sort", ErrorCategory.Validation, "The sort must be valid for the selected dimension.");
    public static readonly ApplicationError Overflow = new("reports.metric_overflow", ErrorCategory.Conflict, "Report metrics exceed their exact supported range.");
    public static readonly ApplicationError Corrupted = new("reports.corrupted_data", ErrorCategory.Conflict, "Report metrics or references are inconsistent.");
}

public static class OperationalReportMoney
{
    public static decimal Validate(decimal amount)
    {
        if (amount > CashSession.MaximumReconciliationAmount) throw new OverflowException("Report amount exceeds numeric(28,4).");
        if (!CashSession.IsValidReconciliationAmount(amount)) throw new ArgumentException("Report money must be nonnegative and exact to four decimal places.");
        return amount;
    }

    public static decimal AverageTicket(decimal amount, long count)
    {
        Validate(amount);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        return count == 0 ? 0m : Validate(InventoryValuation.CostValue(amount, 1m, count)!.Value);
    }

    // Final monetary unit is a lot: exact rational valuation, one ToEven rounding to four places, then sum lot values.
    // Infrastructure translates this pure method to the equivalent PostgreSQL numeric function.
    public static decimal LotCapital(decimal quantity, decimal unitCost, decimal conversion) =>
        Validate(InventoryValuation.CostValue(quantity, unitCost, conversion)!.Value);
}
