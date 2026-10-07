using MediPOS.Application.Modules.Cash;
using MediPOS.Application.Modules.Cash.DispatchCashTransfer;
using MediPOS.Application.Modules.Cash.ReceiveCashTransfer;
using MediPOS.Application.Modules.IdentityAccess.CreateMembership;
using MediPOS.Application.Modules.IdentityAccess.ReplaceWorkSchedule;
using MediPOS.Application.Modules.IdentityAccess.SetMembershipBranches;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.IntegrationTests.Modules.Cash;

internal static class CashTransferTestData
{
    internal static async Task<Rows> CreateAsync(IServiceProvider source, TenantIsolationTestData.TenantRows tenant, bool sameBranch = false, Guid? existingSource = null)
    {
        var session = existingSource ?? (await CashSessionTestData.OpenAsync(source, tenant.Identity)).CashSessionId;
        var branch = sameBranch ? tenant.Identity.BranchId : tenant.SpareBranchId;
        var destination = await AddCashierAsync(source, tenant.Identity, branch);
        var destinationSession = await CashSessionTestData.OpenAsync(source, destination, 20m);
        var owner = await CashSessionTestData.AddOwnerAsync(source, tenant.Identity);
        return new(tenant.Identity, destination, session, destinationSession.CashSessionId, owner);
    }
    internal static async Task<IdentityAccessTestSetup.Setup> AddCashierAsync(IServiceProvider source, IdentityAccessTestSetup.Setup tenant, Guid branch)
    {
        var user = await IdentityAccessTestSetup.CreateUserAsync(source);
        var member = await source.GetRequiredService<CreateMembershipHandler>().HandleAsync(new(tenant.TenantId, user.Id, TenantRole.Cashier, tenant.ActorId), TestContext.Current.CancellationToken);
        await source.GetRequiredService<SetMembershipBranchesHandler>().HandleAsync(new(tenant.TenantId, member.Id, [branch], tenant.ActorId), TestContext.Current.CancellationToken);
        await source.GetRequiredService<ReplaceWorkScheduleHandler>().HandleAsync(new(tenant.TenantId, member.Id,
            [new(DayOfWeek.Tuesday, new(9, 0), new(18, 0))], tenant.ActorId), TestContext.Current.CancellationToken);
        return tenant with { BranchId = branch, UserId = user.Id, MembershipId = member.Id };
    }
    internal static DispatchCashTransferCommand Command(Rows rows, decimal amount = 50m) => new(rows.Source.TenantId, rows.Source.BranchId, rows.SourceSessionId, rows.Destination.BranchId, amount);
    internal static Task<CashTransferDetails> DispatchAsync(IServiceProvider source, Rows rows, decimal amount = 50m)
    {
        CashSessionTestData.Authenticate(source, rows.Source.UserId);
        return source.GetRequiredService<DispatchCashTransferHandler>().HandleAsync(Command(rows, amount), TestContext.Current.CancellationToken);
    }
    internal static Task<CashTransferDetails> ReceiveAsync(IServiceProvider source, Rows rows, Guid transfer, Guid? destinationSession = null)
    {
        CashSessionTestData.Authenticate(source, rows.Destination.UserId);
        return source.GetRequiredService<ReceiveCashTransferHandler>().HandleAsync(new(rows.Source.TenantId, transfer, destinationSession ?? rows.DestinationSessionId), TestContext.Current.CancellationToken);
    }
    internal sealed record Rows(IdentityAccessTestSetup.Setup Source, IdentityAccessTestSetup.Setup Destination, Guid SourceSessionId, Guid DestinationSessionId, Guid OwnerId);
}
