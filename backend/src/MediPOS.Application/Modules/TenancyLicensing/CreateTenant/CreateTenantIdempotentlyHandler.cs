using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Domain.Modules.TenancyLicensing;

namespace MediPOS.Application.Modules.TenancyLicensing.CreateTenant;

public sealed class CreateTenantIdempotentlyHandler(
    CreateTenantHandler create, ITenantCreationRequests requests, IPlatformAdministration administration)
{
    public const string Operation = "platform.tenant.create.v1";
    public static readonly ApplicationError PayloadConflict = new("platform.idempotency_conflict", ErrorCategory.Conflict,
        "The request key is already associated with another payload.");

    public async Task<TenantCreationResult> HandleAsync(CreateTenantCommand command, Guid key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        await PlatformAdministrationAccess.RequireAsync(administration, command.ActorId, cancellationToken).ConfigureAwait(false);
        TenantCreationStatusPolicy.RequireAllowed(command.Status);
        if (key == Guid.Empty || string.IsNullOrWhiteSpace(command.TradingName))
            throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        // PostgreSQL timestamptz stores microseconds. Use precisely the same values for the
        // hash, domain creation and first response, including equivalent explicit UTC offsets.
        static DateTimeOffset UtcMicroseconds(DateTimeOffset value) => new(value.UtcTicks - value.UtcTicks % 10, TimeSpan.Zero);
        var normalized = command with
        {
            TradingName = command.TradingName.Trim(),
            StartsAt = UtcMicroseconds(command.StartsAt),
            ExpiresAt = UtcMicroseconds(command.ExpiresAt)
        };
        var canonical = JsonSerializer.Serialize(new[]
        {
            Operation, normalized.TradingName,
            normalized.StartsAt.ToString("O", CultureInfo.InvariantCulture), normalized.ExpiresAt.ToString("O", CultureInfo.InvariantCulture),
            normalized.MaxBranches.ToString(CultureInfo.InvariantCulture), StatusCode(normalized.Status)
        });
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        await using var reservation = await requests.BeginAsync(command.ActorId, key, hash, cancellationToken).ConfigureAwait(false);
        // A waiter may have lost authorization while another request held the unique key.
        await PlatformAdministrationAccess.RequireAsync(administration, command.ActorId, cancellationToken).ConfigureAwait(false);
        if (reservation.OriginalResult is { } original) return new(original, true);
        var result = await create.HandleAsync(normalized, cancellationToken).ConfigureAwait(false);
        await PlatformAdministrationAccess.RequireAsync(administration, command.ActorId, cancellationToken).ConfigureAwait(false);
        await reservation.CompleteAsync(result, cancellationToken).ConfigureAwait(false);
        return new(result, false);
    }

    // Explicit v1 codes must survive enum renames; never hash ordinals or Enum.ToString().
    private static string StatusCode(LicenseStatus status) => status switch
    {
        LicenseStatus.Trial => "trial",
        LicenseStatus.Active => "active",
        LicenseStatus.Grace => "grace",
        LicenseStatus.Suspended => "suspended",
        LicenseStatus.Cancelled => "cancelled",
        LicenseStatus.PurgePending => "purge_pending",
        LicenseStatus.Purged => "purged",
        _ => throw new ApplicationErrorException(ApplicationErrors.InvalidRequest)
    };
}
