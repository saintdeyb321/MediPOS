using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.Purchasing.Ubl;
using MediPOS.Application.Tenancy;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.TenancyLicensing;
using MediPOS.Infrastructure.Modules.Purchasing.Ubl;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediPOS.UnitTests.Modules.Purchasing.Ubl;

public sealed class SupplierUblAccessTests
{
    [Theory]
    [InlineData("anonymous")]
    [InlineData("foreign")]
    [InlineData("inactive")]
    [InlineData("license")]
    public async Task EveryUseCaseRejectsUntrustedAccessBeforeParsingOrPersistence(string scenario)
    {
        var tenant = Guid.NewGuid(); var branch = Guid.NewGuid(); var user = new User(scenario == "anonymous" ? null : Guid.NewGuid());
        var member = Membership.Create(scenario == "foreign" ? Guid.NewGuid() : tenant, user.UserId ?? Guid.NewGuid(), TenantRole.Owner, SupplierUblDomainTests.Now.AddDays(-1));
        if (scenario == "inactive") member.Deactivate(SupplierUblDomainTests.Now);
        var license = scenario == "license" ? null : License.Create(tenant, SupplierUblDomainTests.Now.AddDays(-1), SupplierUblDomainTests.Now.AddDays(30), 1, LicenseStatus.Active, Guid.NewGuid(), SupplierUblDomainTests.Now);
        var resolver = new ResolveAccessContextHandler(user, new Reader(member, license), new TenantDataContext()); var clock = new Clock(); var id = Guid.NewGuid();
        // Null persistence/parser ports deliberately prove that denied requests never reach those boundaries.
        var calls = new Func<Task>[] {
            async()=>await new ImportSupplierUblInvoiceHandler(null!,null!,null!,resolver,null!,clock).HandleAsync(new(tenant,branch,Stream.Null,"fixture.xml"),TestContext.Current.CancellationToken),
            async()=>await new ReviewSupplierUblImportHandler(null!,null!,null!,resolver,null!,clock).HandleAsync(new(tenant,branch,id,1,null,"reject","Reject",[]),TestContext.Current.CancellationToken),
            async()=>await new ConvertSupplierUblImportToPurchaseDraftHandler(null!,null!,null!,resolver,null!,clock).HandleAsync(new(tenant,branch,id,1),TestContext.Current.CancellationToken),
            async()=>await new GetSupplierUblImportHandler(null!,resolver,clock).HandleAsync(new(tenant,branch,id),TestContext.Current.CancellationToken),
            async()=>await new GetSupplierUblImportLinesHandler(null!,resolver,clock).HandleAsync(new(tenant,branch,id),TestContext.Current.CancellationToken),
            async()=>await new SearchSupplierUblImportsHandler(null!,resolver,clock).HandleAsync(new(tenant,branch),TestContext.Current.CancellationToken),
        };
        foreach (var call in calls) Assert.Equal(ErrorCategory.Forbidden, (await Assert.ThrowsAsync<ApplicationErrorException>(call)).Error.Category);
    }
    [Fact]
    public async Task CashierWithValidOperationalAccessStillCannotManagePurchases()
    {
        var tenant = Guid.NewGuid(); var user = new User(Guid.NewGuid());
        var member = Membership.Create(tenant, user.UserId!.Value, TenantRole.Cashier, SupplierUblDomainTests.Now.AddDays(-1));
        var license = License.Create(tenant, SupplierUblDomainTests.Now.AddDays(-1), SupplierUblDomainTests.Now.AddDays(30), 1, LicenseStatus.Active, user.UserId.Value, SupplierUblDomainTests.Now);
        var schedule = Enum.GetValues<DayOfWeek>().Select(day => WorkSchedule.Create(tenant, member.Id, tenant, day, new(0, 0), new(23, 59))).ToArray();
        var resolver = new ResolveAccessContextHandler(user, new Reader(member, license, schedule), new TenantDataContext());
        var branch = Guid.NewGuid(); Assert.True((await resolver.HandleAsync(tenant, branch, SupplierUblDomainTests.Now, TestContext.Current.CancellationToken)).IsAllowed);
        Assert.Equal("ubl.purchase_permission_required", (await Assert.ThrowsAsync<ApplicationErrorException>(() => new ImportSupplierUblInvoiceHandler(null!, null!, null!, resolver, null!, new Clock())
            .HandleAsync(new(tenant, branch, Stream.Null, "fixture.xml"), TestContext.Current.CancellationToken))).Error.Code);
    }
    [Theory]
    [InlineData(TenantRole.Owner)]
    [InlineData(TenantRole.Pharmacist)]
    public async Task AuthorizedOwnerAndScheduledPharmacistReachTheParser(TenantRole role)
    {
        var tenant = Guid.NewGuid(); var user = new User(Guid.NewGuid());
        var member = Membership.Create(tenant, user.UserId!.Value, role, SupplierUblDomainTests.Now.AddDays(-1));
        var license = License.Create(tenant, SupplierUblDomainTests.Now.AddDays(-1), SupplierUblDomainTests.Now.AddDays(30), 1, LicenseStatus.Active, user.UserId.Value, SupplierUblDomainTests.Now);
        var schedule = Enum.GetValues<DayOfWeek>().Select(day => WorkSchedule.Create(tenant, member.Id, tenant, day, new(0, 0), new(23, 59))).ToArray();
        var resolver = new ResolveAccessContextHandler(user, new Reader(member, license, schedule), new TenantDataContext());
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new ImportSupplierUblInvoiceHandler(new ParserBoundary(), null!, null!, resolver, null!, new Clock())
            .HandleAsync(new(tenant, Guid.NewGuid(), Stream.Null, "synthetic.xml"), TestContext.Current.CancellationToken));
        Assert.Equal("Synthetic parser boundary reached", error.Message);
    }
    [Fact]
    public async Task PagingAndFiltersAreBoundedBeforeReading()
    {
        var clock = new Clock();
        Assert.Equal("ubl.page_invalid", (await Assert.ThrowsAsync<ApplicationErrorException>(() => new SearchSupplierUblImportsHandler(null!, null!, clock).HandleAsync(new(Guid.NewGuid(), Limit: 101), TestContext.Current.CancellationToken))).Error.Code);
        Assert.Equal("ubl.page_invalid", (await Assert.ThrowsAsync<ApplicationErrorException>(() => new GetSupplierUblImportLinesHandler(null!, null!, clock).HandleAsync(new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Offset: -1), TestContext.Current.CancellationToken))).Error.Code);
    }
    [Fact]
    public void SearchProjectionNeverSelectsRawXmlAndUsesTenantFiltersAndPagination()
    {
        using var context = new MediPosDbContext(new DbContextOptionsBuilder<MediPosDbContext>().UseNpgsql().Options, new TenantDataContext()); var tenant = Guid.NewGuid(); context.SelectTenant(tenant);
        var sql = SupplierUblImportQueries.Search(context.SupplierUblImports.AsNoTracking(), new(tenant, Guid.NewGuid(), Guid.NewGuid(), new(2026, 1, 1), new(2026, 12, 31), "reviewed", "F001-1", 5, 10)).ToQueryString();
        Assert.DoesNotContain("source_xml", sql, StringComparison.Ordinal); Assert.Contains("tenant_id", sql, StringComparison.Ordinal);
        Assert.Contains("LIMIT", sql, StringComparison.Ordinal); Assert.Contains("OFFSET", sql, StringComparison.Ordinal); Assert.Contains("ORDER BY", sql, StringComparison.Ordinal);
        Assert.Contains("supplier_id", sql, StringComparison.Ordinal); Assert.Contains("branch_id", sql, StringComparison.Ordinal);
    }
    private sealed class User(Guid? id) : IAuthenticatedMediPosUser { public Guid? UserId => id; }
    private sealed class Reader(Membership membership, License? license, IReadOnlyList<WorkSchedule>? schedule = null) : IOperationalAccessReader
    {
        public Task<OperationalAccessSnapshot> ReadAsync(Guid user, Guid tenant, Guid? branch, CancellationToken token) => Task.FromResult(new OperationalAccessSnapshot(true, membership, license, true, true, schedule ?? []));
    }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => SupplierUblDomainTests.Now; }
    private sealed class ParserBoundary : ISupplierUblInvoiceParser
    { public Task<ParsedSupplierUblInvoice> ParseAsync(Stream input, string filename, CancellationToken token) => throw new InvalidOperationException("Synthetic parser boundary reached"); }
}
