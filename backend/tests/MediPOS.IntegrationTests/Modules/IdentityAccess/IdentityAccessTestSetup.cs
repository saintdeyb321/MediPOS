using MediPOS.Application.Modules.Branches.CreateBranch;
using MediPOS.Application.Modules.Branches.CreateLegalEntity;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.IdentityAccess.CreateMembership;
using MediPOS.Application.Modules.IdentityAccess.UpsertGoogleUser;
using MediPOS.Application.Modules.TenancyLicensing.CreateTenant;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.TenancyLicensing;
using MediPOS.Infrastructure;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.IntegrationTests.Modules.IdentityAccess;

internal static class IdentityAccessTestSetup
{
    internal static readonly DateTimeOffset Now = new(2026, 10, 6, 14, 0, 0, TimeSpan.Zero);

    internal static ServiceProvider CreateServices(PostgreSqlFixture fixture)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:MediPosDatabase"] = fixture.ConnectionString,
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new Clock());
        services.AddInfrastructure(configuration);
        services.AddIdentityAuthentication<TestGoogleIdentitySource, TestServerSession>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    internal static async Task<Setup> CreateAsync(IServiceProvider services, TenantRole role = TenantRole.Cashier)
    {
        var tenant = await services.GetRequiredService<CreateTenantHandler>().HandleAsync(
            new("Botica", Now.AddDays(-1), Now.AddMonths(1), 3, LicenseStatus.Active, Guid.NewGuid()), TestContext.Current.CancellationToken);
        var legal = await services.GetRequiredService<CreateLegalEntityHandler>().HandleAsync(
            new(tenant.TenantId, "Botica SAC", "123"), TestContext.Current.CancellationToken);
        var branch = await services.GetRequiredService<CreateBranchHandler>().HandleAsync(
            new(tenant.TenantId, legal.Id, "Centro"), TestContext.Current.CancellationToken);
        var user = await CreateUserAsync(services);
        var membership = await services.GetRequiredService<CreateMembershipHandler>().HandleAsync(
            new(tenant.TenantId, user.Id, role), TestContext.Current.CancellationToken);
        return new Setup(tenant.TenantId, branch.Id, user.Id, membership.Id, tenant.LicenseId);
    }

    internal static Task<GoogleUserDetails> CreateUserAsync(IServiceProvider services, string? subject = null, string email = "staff@example.test")
    {
        ((TestGoogleIdentitySource)services.GetRequiredService<IVerifiedGoogleIdentitySource>()).Identity =
            new VerifiedGoogleIdentity(subject ?? Guid.NewGuid().ToString("N"), email, "Staff");
        return services.GetRequiredService<UpsertGoogleUserHandler>().HandleAsync(TestContext.Current.CancellationToken);
    }

    internal sealed record Setup(Guid TenantId, Guid BranchId, Guid UserId, Guid MembershipId, Guid LicenseId);

    public sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    // Only the external authentication boundary is faked; all persistence uses real PostgreSQL.
    public sealed class TestGoogleIdentitySource : IVerifiedGoogleIdentitySource
    {
        public VerifiedGoogleIdentity? Identity { get; set; }
        public Task<VerifiedGoogleIdentity?> GetVerifiedIdentityAsync(CancellationToken cancellationToken) => Task.FromResult(Identity);
    }

    public sealed class TestServerSession : IAuthenticatedMediPosUser
    {
        public Guid? UserId { get; set; }
    }
}
