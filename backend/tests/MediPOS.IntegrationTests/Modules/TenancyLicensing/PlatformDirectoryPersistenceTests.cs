using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.IdentityAccess.CreateMembership;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Application.Modules.TenancyLicensing.CreateTenant;
using MediPOS.Domain.Modules.TenancyLicensing;
using MediPOS.Infrastructure;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.IntegrationTests.Modules.TenancyLicensing;

// Prepared external PostgreSQL evidence. Authentication ports are synthetic; Google/Chrome QA remains separate.
[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class PlatformDirectoryPersistenceTests(PostgreSqlFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task DirectoryUsesRestrictedRuntimeAndSelectedTenantWhileReceiptsRemainOperatorOwnedAndReadOnly()
    {
        await using var identityServices = IdentityAccessTestSetup.CreateServices(fixture, TimeProvider.System);
        Guid actor; Guid other; Guid owner;
        await using (var scope = identityServices.CreateAsyncScope())
        {
            actor = (await IdentityAccessTestSetup.CreateUserAsync(scope.ServiceProvider)).Id;
            other = (await IdentityAccessTestSetup.CreateUserAsync(scope.ServiceProvider)).Id;
            owner = (await IdentityAccessTestSetup.CreateUserAsync(scope.ServiceProvider)).Id;
        }
        await using var services = Services(actor); var key = Guid.NewGuid();
        var first = await Create(services, actor, key); var second = await Create(services, actor, Guid.NewGuid());
        await using (var scope = services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<CreateMembershipHandler>().BootstrapOwnerAsync(first.TenantId, owner, actor, Token);
        await using (var scope = services.CreateAsyncScope())
        {
            var queries = scope.ServiceProvider.GetRequiredService<PlatformDirectoryQueries>();
            var lower = first.TenantId.CompareTo(second.TenantId) < 0 ? first.TenantId : second.TenantId;
            var upper = lower == first.TenantId ? second.TenantId : first.TenantId;
            var page = await queries.TenantsAsync(lower, 1, Token);
            Assert.Single(page.Items); Assert.True(page.Items[0].TenantId.CompareTo(lower) > 0); Assert.True(page.Items[0].TenantId.CompareTo(upper) <= 0);
            var receipt = await queries.CreationResultAsync(key, Token);
            Assert.Equal(first.TenantId, receipt.TenantId); Assert.Equal(first.LicenseId, receipt.LicenseId);
            var context = scope.ServiceProvider.GetRequiredService<MediPosDbContext>();
            Assert.True(await context.Database.SqlQueryRaw<bool>("SELECT coalesce(current_setting('medipos.platform_actor_id',true),'')='' AS \"Value\"").SingleAsync(Token));
            // An unscoped raw SELECT cannot reuse the actor from the receipt transaction.
            Assert.Equal(0, await context.Database.SqlQueryRaw<long>("SELECT count(*) AS \"Value\" FROM platform_tenant_creation_requests").SingleAsync(Token));
        }
        await using (var scope = services.CreateAsyncScope())
        {
            var queries = scope.ServiceProvider.GetRequiredService<PlatformDirectoryQueries>();
            var details = await queries.TenantAsync(first.TenantId, Token); Assert.Equal(owner, details.InitialOwnerUserId);
            var context = scope.ServiceProvider.GetRequiredService<MediPosDbContext>();
            Assert.Empty(await context.Licenses.IgnoreQueryFilters().Where(l => l.TenantId == second.TenantId).ToListAsync(Token));
            var registered = await queries.GoogleIdentityAsync(owner, Token); Assert.Equal(owner, registered.UserId);
        }
        await using var otherServices = Services(other);
        await using (var scope = otherServices.CreateAsyncScope())
        {
            var denied = await Assert.ThrowsAsync<ApplicationErrorException>(() => scope.ServiceProvider.GetRequiredService<PlatformDirectoryQueries>().CreationResultAsync(key, Token));
            Assert.Equal("platform.request_not_found", denied.Error.Code);
        }
    }

    private static async Task<LicenseDetails> Create(ServiceProvider services, Guid actor, Guid key)
    {
        await using var scope = services.CreateAsyncScope(); var now = DateTimeOffset.UtcNow;
        return (await scope.ServiceProvider.GetRequiredService<CreateTenantIdempotentlyHandler>().HandleAsync(
            new("Synthetic QA directory tenant", now, now.AddDays(30), 2, LicenseStatus.Trial, actor), key, Token)).License;
    }

    private ServiceProvider Services(Guid actor)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:MediPosDatabase"] = fixture.ConnectionString }).Build();
        var services = new ServiceCollection(); services.AddSingleton(TimeProvider.System); services.AddInfrastructure(configuration);
        services.AddIdentityAuthentication<IdentityAccessTestSetup.TestGoogleIdentitySource, IdentityAccessTestSetup.TestServerSession>();
        services.AddScoped<IPlatformAdministration>(_ => new Approved(actor));
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }
    private sealed class Approved(Guid actor) : IPlatformAdministration
    { public Task<Guid?> GetAuthorizedActorAsync(CancellationToken token) => Task.FromResult<Guid?>(actor); }
}
