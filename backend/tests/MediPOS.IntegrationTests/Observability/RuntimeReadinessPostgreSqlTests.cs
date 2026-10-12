using MediPOS.Infrastructure;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.IntegrationTests.Observability;

// Real PostgreSQL fixture, external execution only. No substitute persistence provider.
[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class RuntimeReadinessPostgreSqlTests(PostgreSqlFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RuntimeReadinessUsesRestrictedRoleAndDoesNotMutateTenantData()
    {
        await using var services = Services(fixture.ConnectionString);
        var probe = new RuntimeDatabaseProbe(services.GetRequiredService<IServiceScopeFactory>());
        await using var owner = fixture.CreateConstraintContext();
        var tenants = await owner.Tenants.AsNoTracking().CountAsync(Token);
        var audits = await owner.AuditLogs.IgnoreQueryFilters().AsNoTracking().CountAsync(Token);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token); deadline.CancelAfter(TimeSpan.FromSeconds(5));
        await probe.VerifyAsync(deadline.Token);
        Assert.Equal(tenants, await owner.Tenants.AsNoTracking().CountAsync(Token));
        Assert.Equal(audits, await owner.AuditLogs.IgnoreQueryFilters().AsNoTracking().CountAsync(Token));
        using var scope = services.CreateScope();
        Assert.Null(scope.ServiceProvider.GetRequiredService<MediPosDbContext>().SelectedTenantId);
    }

    [Fact]
    public async Task PrivilegedCredentialIsRejectedBeforeReadinessQuery()
    {
        await using var owner = fixture.CreateConstraintContext();
        await using var services = Services(owner.Database.GetConnectionString()!);
        var probe = new RuntimeDatabaseProbe(services.GetRequiredService<IServiceScopeFactory>());
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token); deadline.CancelAfter(TimeSpan.FromSeconds(5));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => probe.VerifyAsync(deadline.Token));
        Assert.DoesNotContain(owner.Database.GetConnectionString()!, error.Message, StringComparison.Ordinal);
    }

    private static ServiceProvider Services(string connection)
    {
        var registrations = new ServiceCollection();
        registrations.AddInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["ConnectionStrings:MediPosDatabase"] = connection, ["Notifications:ExpirationAlerts:Enabled"] = "false" }).Build());
        return registrations.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
}
