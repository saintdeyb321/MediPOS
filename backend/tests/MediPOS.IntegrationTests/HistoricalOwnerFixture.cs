using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using Microsoft.EntityFrameworkCore;

namespace MediPOS.Tests.Fixtures;

// Legacy snapshot setup only, in the disposable PostgreSQL fixture. Current use cases cannot
// deactivate the last Owner; callers separately assert that denial before constructing this history.
internal static class HistoricalOwnerFixture
{
    internal static async Task RecordInactiveAsync(PostgreSqlFixture fixture, Guid tenantId, Guid membershipId, Guid actorId, CancellationToken token)
    {
        await using var context = fixture.CreateContext(tenantId);
        await using var transaction = await context.Database.BeginTransactionAsync(token);
        var membership = await context.Memberships.SingleAsync(value => value.Id == membershipId, token);
        var before = AuditTrail.MembershipState(membership);
        membership.Deactivate(membership.CreatedAt);
        context.AuditLogs.Add(AuditTrail.Record(tenantId, actorId, AuditAction.MembershipDeactivated, membershipId,
            membership.CreatedAt, before, AuditTrail.MembershipState(membership)));
        await context.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
    }
}
