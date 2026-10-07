using MediPOS.Application.Modules.Cash;
using MediPOS.Application.Modules.Cash.OpenCashSession;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.IdentityAccess.CreateMembership;
using MediPOS.Application.Modules.IdentityAccess.ReplaceWorkSchedule;
using MediPOS.Application.Modules.IdentityAccess.SetMembershipBranches;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.IntegrationTests.Modules.Cash;

internal static class CashSessionTestData
{
    internal static async Task<IdentityAccessTestSetup.Setup> CreateAsync(IServiceProvider services, TenantRole role = TenantRole.Cashier)
    {
        var setup = await IdentityAccessTestSetup.CreateAsync(services, role);
        if (role != TenantRole.Owner)
        {
            await services.GetRequiredService<SetMembershipBranchesHandler>().HandleAsync(
                new(setup.TenantId, setup.MembershipId, [setup.BranchId], setup.ActorId), TestContext.Current.CancellationToken);
            await services.GetRequiredService<ReplaceWorkScheduleHandler>().HandleAsync(
                new(setup.TenantId, setup.MembershipId, [new(DayOfWeek.Tuesday, new(9, 0), new(18, 0))], setup.ActorId), TestContext.Current.CancellationToken);
        }
        return setup;
    }

    internal static void Authenticate(IServiceProvider services, Guid userId) =>
        ((IdentityAccessTestSetup.TestServerSession)services.GetRequiredService<IAuthenticatedMediPosUser>()).UserId = userId;

    internal static Task<OpenCashSessionDetails> OpenAsync(IServiceProvider services, IdentityAccessTestSetup.Setup setup, decimal amount = 100m)
    {
        Authenticate(services, setup.UserId);
        return services.GetRequiredService<OpenCashSessionHandler>().HandleAsync(
            new(setup.TenantId, setup.BranchId, amount), TestContext.Current.CancellationToken);
    }

    internal static async Task<Guid> AddOwnerAsync(IServiceProvider services, IdentityAccessTestSetup.Setup setup)
    {
        var user = await IdentityAccessTestSetup.CreateUserAsync(services);
        await services.GetRequiredService<CreateMembershipHandler>().HandleAsync(
            new(setup.TenantId, user.Id, TenantRole.Owner, setup.ActorId), TestContext.Current.CancellationToken);
        return user.Id;
    }
}
