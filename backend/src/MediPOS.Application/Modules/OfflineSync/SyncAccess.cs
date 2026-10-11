using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.OfflineSync;

namespace MediPOS.Application.Modules.OfflineSync;

internal static class SyncAccess
{
    internal static ApplicationErrorException Error(string code, ErrorCategory category = ErrorCategory.Validation) =>
        new(new ApplicationError(code, category, "Offline sync request denied."));

    internal static async Task<AccessContext> RequireAsync(ResolveAccessContextHandler resolver, Guid tenantId,
        Guid? branchId, TimeProvider clock, CancellationToken token, bool allowBranchLookup = false)
    {
        if (tenantId == Guid.Empty || branchId == Guid.Empty) throw Error("sync.identity_invalid");
        var result = await resolver.HandleAsync(tenantId, branchId, clock.GetUtcNow(), token).ConfigureAwait(false);
        if (result.Context is null || (!result.IsAllowed && !(allowBranchLookup && result.Code == "access.branch_required")))
            throw Error(result.Code, ErrorCategory.Forbidden);
        return result.Context;
    }

    internal static void RequireDevice(SyncDevice? device, AccessContext access, bool allowRevoked = false)
    {
        if (device is null || device.TenantId != access.TenantId || device.BranchId != access.BranchId)
            throw Error("sync.device_not_found", ErrorCategory.NotFound);
        if (device.RegisteredByMembershipId != access.MembershipId && access.Role != TenantRole.Owner)
            throw Error("sync.device_denied", ErrorCategory.Forbidden);
        if (!allowRevoked && device.Status != "active") throw Error("sync.device_revoked", ErrorCategory.Forbidden);
    }

    // Data-only JSON in B5.1. No business operation, runtime type loading or command evaluation.
    // Line/payment/cash-session reconciliation and after-shift policy require B5.2/B5.3 functional contracts.
    internal static SyncEvent Receive(Guid tenantId, SyncEventEnvelope envelope, DateTimeOffset now)
    {
        try
        {
            return SyncEvent.Receive(tenantId, envelope.DeviceId, envelope.EventId, envelope.EventType, envelope.SchemaVersion,
                envelope.Payload, envelope.PayloadHash, envelope.ClientOccurredAtUtc, envelope.ClientLocalDate, envelope.ClientTimeZoneId, now);
        }
        catch (ArgumentException error)
        {
            var code = error.Message.StartsWith("sync.", StringComparison.Ordinal) ? error.Message.Split(' ')[0] : "sync.payload_invalid";
            throw Error(code);
        }
        catch (Exception error) when (error is JsonException or FormatException or OverflowException or InvalidOperationException)
        { throw Error("sync.payload_invalid"); }
        catch (Exception error) when (error is TimeZoneNotFoundException or InvalidTimeZoneException)
        { throw Error("sync.time_invalid"); }
    }

    internal static string AuditState(SyncDevice device) => JsonSerializer.Serialize(new
    {
        branchId = device.BranchId,
        registeredByMembershipId = device.RegisteredByMembershipId,
        status = device.Status,
        revokedAtUtc = device.RevokedAtUtc,
        revokedByMembershipId = device.RevokedByMembershipId,
    });
}
