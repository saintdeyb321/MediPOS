using MediPOS.Application.Modules.Branches.CreateBranch;
using MediPOS.Application.Modules.IdentityAccess.ReplaceWorkSchedule;
using MediPOS.Application.Modules.IdentityAccess.SetMembershipBranches;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.IntegrationTests.Tenancy;

internal static class TenantIsolationTestData
{
    internal static async Task<(TenantRows A, TenantRows B)> CreatePairAsync(PostgreSqlFixture fixture)
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        return (await CreateRowsAsync(services), await CreateRowsAsync(services));
    }

    private static async Task<TenantRows> CreateRowsAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var setup = await IdentityAccessTestSetup.CreateAsync(provider);
        await provider.GetRequiredService<SetMembershipBranchesHandler>().HandleAsync(
            new(setup.TenantId, setup.MembershipId, [setup.BranchId], Guid.NewGuid()), TestContext.Current.CancellationToken);
        await provider.GetRequiredService<ReplaceWorkScheduleHandler>().HandleAsync(
            new(setup.TenantId, setup.MembershipId, [new(DayOfWeek.Tuesday, new(9, 0), new(18, 0))], Guid.NewGuid()), TestContext.Current.CancellationToken);
        var context = provider.GetRequiredService<MediPOS.Infrastructure.Persistence.MediPosDbContext>();
        var legalId = await context.Branches.Where(value => value.Id == setup.BranchId).Select(value => value.LegalEntityId)
            .SingleAsync(TestContext.Current.CancellationToken);
        var spare = await provider.GetRequiredService<CreateBranchHandler>().HandleAsync(
            new(setup.TenantId, legalId, "Spare", Guid.NewGuid()), TestContext.Current.CancellationToken);
        var windowId = await context.WorkSchedules.Select(value => value.Id).SingleAsync(TestContext.Current.CancellationToken);
        return new TenantRows(setup, legalId, spare.Id, windowId);
    }

    internal sealed record TenantRows(IdentityAccessTestSetup.Setup Identity, Guid LegalEntityId, Guid SpareBranchId, Guid ScheduleId)
    {
        internal Guid TenantId => Identity.TenantId;
    }
}
