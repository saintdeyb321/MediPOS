using System.Data;
using System.Data.Common;
using System.Globalization;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Commissions.SetCommissionRule;
using MediPOS.Application.Modules.Commissions.SetTenantCommissionsEnabled;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.Reporting.ExportOwnerReport;
using MediPOS.Application.Modules.Reporting.GetOwnerCommissionsReport;
using MediPOS.Application.Modules.Reporting.Operational;
using MediPOS.Application.Modules.SalesPos;
using MediPOS.Application.Modules.SalesPos.CreateSaleDraft;
using MediPOS.Application.Modules.SalesPos.ReplaceSaleLines;
using MediPOS.Application.Modules.SalesPos.VoidSale;
using MediPOS.Domain.Modules.Commissions;
using MediPOS.Domain.Modules.SalesPos;
using MediPOS.Infrastructure;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.Cash;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using UglyToad.PdfPig;

namespace MediPOS.IntegrationTests.Modules.Reporting;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class ReportExportPersistenceTests(PostgreSqlFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(OwnerReportType.Sales, OwnerProductRotationMode.LowActivity)]
    [InlineData(OwnerReportType.Commissions, OwnerProductRotationMode.LowActivity)]
    [InlineData(OwnerReportType.StockRisk, OwnerProductRotationMode.LowActivity)]
    [InlineData(OwnerReportType.Expirations, OwnerProductRotationMode.LowActivity)]
    [InlineData(OwnerReportType.InventoryCapital, OwnerProductRotationMode.LowActivity)]
    [InlineData(OwnerReportType.ProductRotation, OwnerProductRotationMode.LowActivity)]
    [InlineData(OwnerReportType.ProductRotation, OwnerProductRotationMode.Top)]
    public async Task BothDocumentsMatchOriginalQueriesUsingTheSamePostgreSqlSnapshot(OwnerReportType type, OwnerProductRotationMode rotationMode)
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture); var clock = new OwnerOverviewTestData.Clock();
        var scenario = await SeedAsync(tenant, clock);
        await using var baseline = fixture.CreateContext(tenant.TenantId);
        await using var transaction = await baseline.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, Token);
        await baseline.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", Token);
        var query = Query(tenant.TenantId, type);
        if (type == OwnerReportType.ProductRotation) query = query with { RotationMode = rotationMode };
        var request = ReportExportPolicy.CreateRequest(query, clock.Now);
        var original = await OriginalAsync(baseline, request);
        // Importing an exported snapshot proves equality even if later commits occur between these reads.
        var snapshotId = await baseline.Database.SqlQueryRaw<string>("SELECT pg_export_snapshot() AS \"Value\"").SingleAsync(Token);
        var importer = new SnapshotImporter(snapshotId);
        await using var services = ImportedServices(clock, importer); await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        await OwnerOverviewTestData.SelectOwnerAsync(source, tenant.TenantId, scenario.Owner.UserId);
        var exported = await source.GetRequiredService<IReportExportReader>().ReadAsync(request, Token);
        Assert.Equal(1, importer.Imports);
        AssertSame(original, exported.Data);
        var document = new ReportExportDocument(request, exported, ReportExportPolicy.Filters(request, exported.Metadata));
        foreach (var format in Enum.GetValues<ReportExportFormat>())
        {
            var bytes = await source.GetRequiredService<IReportDocumentRenderer>().RenderAsync(document, format, Token);
            AssertValues(exported.Data, bytes, format);
        }
        if (type == OwnerReportType.Commissions)
        {
            var commissions = Assert.IsType<CommissionsExportData>(exported.Data);
            Assert.Equal(2, commissions.Page.Totals.EarnedEntryCount);
            Assert.Equal(.4002m, commissions.Page.Totals.OriginalEarnedAmount);
            Assert.Equal(.2001m, commissions.Page.Totals.CompensatedAmount); Assert.Equal(.2001m, commissions.Page.Totals.CurrentNetAmount);
        }
        if (type == OwnerReportType.InventoryCapital)
        {
            Assert.Equal(1m, Assert.IsType<CapitalExportData>(exported.Data).Snapshot.Branches.Single(row => row.BranchId == tenant.SpareBranchId).TotalInventoryCapital);
            Assert.Equal(scenario.OriginalPurchaseLine, await baseline.InventoryLots.Where(lot => lot.SourceTransferLotAllocationId != null)
                .Select(lot => lot.SourcePurchaseLineId).SingleAsync(Token));
        }
        Assert.Empty(source.GetRequiredService<MediPosDbContext>().ChangeTracker.Entries());
        await transaction.CommitAsync(Token);
    }

    [Theory]
    [InlineData(OwnerSalesDimension.Branch)]
    [InlineData(OwnerSalesDimension.Employee)]
    [InlineData(OwnerSalesDimension.Product)]
    [InlineData(OwnerSalesDimension.Category)]
    public async Task CombinedSalesFiltersExportOnlyMatchingLinesAndDistinctSalesAcrossAllDimensions(OwnerSalesDimension dimension)
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture); var clock = new OwnerOverviewTestData.Clock();
        await using var services = OwnerOverviewTestData.CreateServices(fixture, clock); await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var data = await OperationalReportTestData.SeedAsync(source, tenant, clock);
        var query = Query(tenant.TenantId, OwnerReportType.Sales) with
        {
            Dimension = dimension,
            EmployeeMembershipId = data.Owner.MembershipId,
            BusinessProductId = data.MedicineA,
            CategoryId = tenant.CategoryId,
            BranchId = tenant.Identity.BranchId
        };
        var request = ReportExportPolicy.CreateRequest(query, clock.Now);
        var snapshot = await source.GetRequiredService<IReportExportReader>().ReadAsync(request, Token);
        var sales = Assert.IsType<SalesExportData>(snapshot.Data);
        Assert.Equal(4m, sales.Page.Totals.SalesAmount); Assert.Equal(1, sales.Page.Totals.DistinctSaleCount);
        Assert.Equal(1, Assert.Single(sales.Page.Rows).SaleCount);
        Assert.Equal(dimension == OwnerSalesDimension.Product ? 2m : null, sales.Page.Rows[0].BaseQuantitySold);
        var ordinary = await source.GetRequiredService<GetOwnerSalesReportHandler>().HandleAsync(new(tenant.TenantId, query.BranchId,
            query.Period!, dimension, query.EmployeeMembershipId, query.BusinessProductId, query.CategoryId, Limit: 1), Token);
        Assert.Equal(ordinary.Totals, sales.Page.Totals);
        foreach (var format in Enum.GetValues<ReportExportFormat>())
        {
            var output = await source.GetRequiredService<ExportOwnerReportHandler>().HandleAsync(query with { Format = format }, Token);
            AssertValues(snapshot.Data, output.Content.ToArray(), format);
            Assert.Equal("Solo líneas coincidentes", output.AppliedFilters.Single(filter => filter.Key == "amount_basis").Value);
            Assert.Equal("MED-A", output.AppliedFilters.Single(filter => filter.Key == "product").Value);
        }
        var all = await source.GetRequiredService<IReportExportReader>().ReadAsync(ReportExportPolicy.CreateRequest(Query(tenant.TenantId, OwnerReportType.Sales)
            with
        { Dimension = dimension }, clock.Now), Token);
        Assert.Equal(9m, Assert.IsType<SalesExportData>(all.Data).Page.Totals.SalesAmount); // Mixed cash/Yape counts once.
        if (dimension is OwnerSalesDimension.Product or OwnerSalesDimension.Category)
            Assert.Equal(1, Assert.IsType<SalesExportData>(all.Data).Page.Totals.DistinctSaleCount);
        var mismatch = query with { EmployeeMembershipId = tenant.Identity.MembershipId };
        Assert.Empty(Assert.IsType<SalesExportData>((await source.GetRequiredService<IReportExportReader>().ReadAsync(ReportExportPolicy.CreateRequest(mismatch, clock.Now), Token)).Data).Page.Rows);
    }

    [Theory]
    [InlineData(OwnerReportType.Sales)]
    [InlineData(OwnerReportType.Commissions)]
    [InlineData(OwnerReportType.StockRisk)]
    [InlineData(OwnerReportType.Expirations)]
    [InlineData(OwnerReportType.InventoryCapital)]
    [InlineData(OwnerReportType.ProductRotation)]
    public async Task OwnerBoundaryRejectsOperationalStaffForeignTenantAndForeignBranch(OwnerReportType type)
    {
        var (tenant, other) = await TenantIsolationTestData.CreatePairAsync(fixture); var clock = new OwnerOverviewTestData.Clock();
        var data = await SeedAsync(tenant, clock);
        clock.Now = IdentityAccessTestSetup.Now;
        await SeedAsync(other, clock);
        await using var services = OwnerOverviewTestData.CreateServices(fixture, clock); await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        CashSessionTestData.Authenticate(source, tenant.Identity.UserId);
        var handler = source.GetRequiredService<ExportOwnerReportHandler>(); var query = Query(tenant.TenantId, type);
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => handler.HandleAsync(query, Token));
        Assert.Equal("reports.owner_required", error.Error.Code);
        CashSessionTestData.Authenticate(source, data.Owner.UserId);
        await Assert.ThrowsAsync<ApplicationErrorException>(() => handler.HandleAsync(query with { TenantId = other.TenantId }, Token));
        await Assert.ThrowsAsync<ApplicationErrorException>(() => handler.HandleAsync(query with { BranchId = other.Identity.BranchId }, Token));
        await using var rls = fixture.CreateContext(tenant.TenantId);
        Assert.Empty(await rls.InventoryLots.IgnoreQueryFilters().Where(lot => lot.TenantId == other.TenantId).ToArrayAsync(Token));
        Assert.Empty(await rls.CommissionEntries.IgnoreQueryFilters().Where(entry => entry.TenantId == other.TenantId).ToArrayAsync(Token));
    }

    [Theory]
    [InlineData(OwnerSalesDimension.Branch)]
    [InlineData(OwnerSalesDimension.Employee)]
    public async Task ConcurrentVoidDoesNotMixTotalsRowsOrMetadataAndExportDoesNotWrite(OwnerSalesDimension dimension)
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture); var clock = new OwnerOverviewTestData.Clock();
        await using var writerServices = OwnerOverviewTestData.CreateServices(fixture, clock); OperationalReportTestData.Scenario data;
        await using (var setup = writerServices.CreateAsyncScope()) data = await OperationalReportTestData.SeedAsync(setup.ServiceProvider, tenant, clock);
        var observer = new OwnerOverviewTestData.SqlObserver();
        await using var services = OwnerOverviewTestData.CreateServices(fixture, clock, observer); await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        await OwnerOverviewTestData.SelectOwnerAsync(source, tenant.TenantId, data.Owner.UserId); observer.Reset();
        observer.BeforeSecondSelect = async (command, token) =>
        {
            Assert.Equal(IsolationLevel.RepeatableRead, command.Transaction!.IsolationLevel);
            await using var setting = command.Connection!.CreateCommand(); setting.Transaction = command.Transaction; setting.CommandText = "SHOW transaction_read_only";
            Assert.Equal("on", await setting.ExecuteScalarAsync(token));
            await using var write = writerServices.CreateAsyncScope(); var target = write.ServiceProvider;
            CashSessionTestData.Authenticate(target, data.Owner.UserId);
            await target.GetRequiredService<VoidSaleHandler>().HandleAsync(new(tenant.TenantId, tenant.Identity.BranchId, data.MixedSale, data.MixedVersion, "Concurrent export void"), token);
        };
        var request = ReportExportPolicy.CreateRequest(Query(tenant.TenantId, OwnerReportType.Sales) with { Dimension = dimension, BusinessProductId = data.MedicineA }, clock.Now);
        var snapshot = await source.GetRequiredService<IReportExportReader>().ReadAsync(request, Token);
        var sales = Assert.IsType<SalesExportData>(snapshot.Data); Assert.Equal(4m, sales.Page.Totals.SalesAmount); Assert.Equal(4m, Assert.Single(sales.Page.Rows).SalesAmount);
        foreach (var format in Enum.GetValues<ReportExportFormat>())
            AssertValues(sales, await source.GetRequiredService<IReportDocumentRenderer>().RenderAsync(new(request, snapshot, ReportExportPolicy.Filters(request, snapshot.Metadata)), format, Token), format);
        Assert.Equal("SET TRANSACTION READ ONLY", Assert.Single(observer.Writes));
        Assert.All(observer.Selects, sql => { Assert.DoesNotContain("FOR UPDATE", sql, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("sale_payments", sql, StringComparison.OrdinalIgnoreCase); });
        Assert.Empty(source.GetRequiredService<MediPosDbContext>().ChangeTracker.Entries());
        observer.BeforeSecondSelect = null;
        Assert.Empty(Assert.IsType<SalesExportData>((await source.GetRequiredService<IReportExportReader>().ReadAsync(request, Token)).Data).Page.Rows);
    }

    [Theory]
    [InlineData(9_994, false)]
    [InlineData(9_995, true)]
    public async Task CompleteTenThousandLotExportIsIndependentOfUiPagesAndLargerReportsFail(int additionalLots, bool tooLarge)
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture); var clock = new OwnerOverviewTestData.Clock();
        var data = await SeedAsync(tenant, clock, commissions: false);
        // Synthetic read-limit fixture only: new purchase/lines, one receipt per lot, matching ledger and audit.
        // No guard is disabled and no existing purchase/history is changed to create the cardinality boundary.
        await using (var seed = fixture.CreateContext(tenant.TenantId))
        {
            await using var transaction = await seed.Database.BeginTransactionAsync(Token);
            var purchaseId = Guid.NewGuid();
            var beforeJson = """{"status":"draft"}""";
            await seed.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO purchases (id,tenant_id,branch_id,supplier_id,document_reference,status,created_at,created_by_actor_id)
                SELECT {purchaseId},p.tenant_id,p.branch_id,p.supplier_id,'SYNTHETIC-EXPORT-LIMIT','draft',p.created_at,p.created_by_actor_id
                FROM purchases p JOIN purchase_lines l ON l.purchase_id=p.id AND l.tenant_id=p.tenant_id WHERE l.id={data.OriginalPurchaseLine};
                """, Token);
            Assert.Equal(additionalLots, await seed.Database.ExecuteSqlInterpolatedAsync($"""
                WITH new_lines AS (
                  INSERT INTO purchase_lines (id,tenant_id,purchase_id,business_product_id,quantity,unit_name_snapshot,
                    conversion_to_base_snapshot,base_quantity,unit_cost,batch_number,expiration_date)
                  SELECT gen_random_uuid(),tenant_id,{purchaseId},business_product_id,quantity,unit_name_snapshot,
                    conversion_to_base_snapshot,base_quantity,unit_cost,batch_number,expiration_date
                  FROM purchase_lines CROSS JOIN generate_series(1,{additionalLots}) WHERE id={data.OriginalPurchaseLine}
                  RETURNING *
                ), new_lots AS (
                  INSERT INTO inventory_lots (id,tenant_id,branch_id,business_product_id,source_purchase_line_id,batch_number,expiration_date,quantity_available_base,created_at)
                  SELECT gen_random_uuid(),l.tenant_id,p.branch_id,l.business_product_id,l.id,l.batch_number,l.expiration_date,l.base_quantity,p.created_at
                  FROM new_lines l JOIN purchases p ON p.tenant_id=l.tenant_id AND p.id=l.purchase_id
                  RETURNING *
                )
                INSERT INTO stock_movements (id,tenant_id,branch_id,business_product_id,inventory_lot_id,movement_type,quantity_delta_base,source_purchase_line_id,actor_id,occurred_at)
                SELECT gen_random_uuid(),l.tenant_id,l.branch_id,l.business_product_id,l.id,'purchase_receipt',l.quantity_available_base,l.source_purchase_line_id,
                  p.created_by_actor_id,p.created_at FROM new_lots l JOIN purchases p ON p.tenant_id=l.tenant_id AND p.id={purchaseId};
                """, Token));
            Assert.Equal(1, await seed.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE purchases p SET status='confirmed',confirmed_at=p.created_at,confirmed_by_actor_id=p.created_by_actor_id
                WHERE p.id={purchaseId} AND p.tenant_id={tenant.TenantId};
                """, Token));
            Assert.Equal(1, await seed.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO audit_logs (id,tenant_id,actor_id,action,entity_type,entity_id,occurred_at,correlation_id,before_json,after_json)
                SELECT {Guid.NewGuid()},p.tenant_id,p.confirmed_by_actor_id,'purchase.confirmed','purchase',p.id,p.confirmed_at,{Guid.NewGuid().ToString("N")},
                  {beforeJson}::jsonb,jsonb_build_object('status','confirmed','lineCount',{additionalLots},'confirmedAt',p.confirmed_at)
                FROM purchases p WHERE p.id={purchaseId} AND p.tenant_id={tenant.TenantId};
                """, Token));
            await transaction.CommitAsync(Token);
        }
        await using var services = OwnerOverviewTestData.CreateServices(fixture, clock); await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        await OwnerOverviewTestData.SelectOwnerAsync(source, tenant.TenantId, data.Owner.UserId);
        var query = Query(tenant.TenantId, OwnerReportType.Expirations);
        if (tooLarge)
        {
            var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => source.GetRequiredService<ExportOwnerReportHandler>().HandleAsync(query, Token));
            Assert.Equal("reports.export_too_large", error.Error.Code);
        }
        else
        {
            var ordinary = await source.GetRequiredService<GetOwnerExpirationReportHandler>().HandleAsync(new(tenant.TenantId, null, query.ExpirationRange!.From, query.ExpirationRange.To, Limit: 1), Token);
            Assert.Single(ordinary.Rows); Assert.Equal(10_000, ordinary.Totals.LotCount);
            var result = await source.GetRequiredService<ExportOwnerReportHandler>().HandleAsync(query, Token);
            using var stream = new MemoryStream(result.Content.ToArray()); using var workbook = SpreadsheetDocument.Open(stream, false);
            Assert.Equal(10_000, workbook.WorkbookPart!.WorksheetParts.Single().Worksheet.Descendants<Cell>().Count(cell => cell.StyleIndex?.Value == 3));
        }
    }

    [Fact]
    public async Task PooledConnectionExportsNeverRetainThePreviousTenantScope()
    {
        var (first, second) = await TenantIsolationTestData.CreatePairAsync(fixture); var clock = new OwnerOverviewTestData.Clock();
        var firstData = await SeedAsync(first, clock, commissions: false); clock.Now = IdentityAccessTestSetup.Now;
        var secondData = await SeedAsync(second, clock, commissions: false);
        var connection = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { MaxPoolSize = 1, NoResetOnClose = true, ApplicationName = Guid.NewGuid().ToString("N") }.ConnectionString;
        await using var services = OwnerOverviewTestData.CreateServices(fixture, clock, connectionString: connection); int? pid = null;
        foreach (var scenario in new[] { firstData, secondData, firstData })
        {
            await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider; CashSessionTestData.Authenticate(source, scenario.Owner.UserId);
            foreach (var type in Enum.GetValues<OwnerReportType>())
            {
                var result = await source.GetRequiredService<ExportOwnerReportHandler>().HandleAsync(Query(scenario.Tenant.TenantId, type), Token);
                Assert.Equal(scenario.Tenant.TenantId, source.GetRequiredService<MediPosDbContext>().SelectedTenantId);
                Assert.Equal("medipos-" + ReportExportPolicy.TypeCode(type) + "-2026-10-07.xlsx", result.FileName);
            }
            var current = await source.GetRequiredService<MediPosDbContext>().Database.SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"").SingleAsync(Token);
            if (pid.HasValue) Assert.Equal(pid, current); pid = current;
            var request = ReportExportPolicy.CreateRequest(Query(scenario.Tenant.TenantId, OwnerReportType.Expirations), clock.Now);
            var snapshot = await source.GetRequiredService<IReportExportReader>().ReadAsync(request, Token);
            Assert.All(Assert.IsType<ExpirationsExportData>(snapshot.Data).Page.Rows, row => Assert.Contains(row.BusinessProductId, new[] { scenario.MedicineA, scenario.MedicineB }));
        }
        await using var empty = services.CreateAsyncScope(); var context = empty.ServiceProvider.GetRequiredService<MediPosDbContext>();
        Assert.Equal(0, await context.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM commission_entries").SingleAsync(Token));
        Assert.Equal(string.Empty, await context.Database.SqlQueryRaw<string>("SELECT coalesce(current_setting('medipos.tenant_id', true), '') AS \"Value\"").SingleAsync(Token));
    }

    private async Task<OperationalReportTestData.Scenario> SeedAsync(TenantIsolationTestData.TenantRows tenant, OwnerOverviewTestData.Clock clock, bool commissions = true)
    {
        await using var services = OwnerOverviewTestData.CreateServices(fixture, clock); await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var data = await OperationalReportTestData.SeedAsync(source, tenant, clock);
        if (!commissions) return data;
        clock.Now = IdentityAccessTestSetup.Now;
        await source.GetRequiredService<SetTenantCommissionsEnabledHandler>().HandleAsync(new(tenant.TenantId, true), Token);
        await source.GetRequiredService<SetCommissionRuleHandler>().HandleAsync(new(tenant.TenantId, data.MedicineA, CommissionRuleType.Fixed, .2001m, clock.Now.AddDays(-1), null), Token);
        var unit = await OwnerOverviewTestData.BaseUnitAsync(source, data.MedicineA);
        foreach (var reverse in new[] { false, true })
        {
            var draft = await source.GetRequiredService<CreateSaleDraftHandler>().HandleAsync(new(tenant.TenantId, data.Owner.BranchId), Token);
            var lines = await source.GetRequiredService<ReplaceSaleLinesHandler>().HandleAsync(new(tenant.TenantId, data.Owner.BranchId, draft.SaleId, draft.Version, [new(data.MedicineA, unit, 1m, PriceKind.Retail)]), Token);
            var confirmed = await OwnerOverviewTestData.ConfirmAsync(source, tenant.TenantId, data.Owner.BranchId, lines, mixed: true);
            if (reverse) await source.GetRequiredService<VoidSaleHandler>().HandleAsync(new(tenant.TenantId, data.Owner.BranchId, confirmed.SaleId, confirmed.Version, "Export commission reversal"), Token);
        }
        clock.Now = new(2026, 10, 7, 14, 0, 0, TimeSpan.Zero);
        return data;
    }
    private static ExportOwnerReportQuery Query(Guid tenant, OwnerReportType type) => new(tenant, type, ReportExportFormat.Xlsx,
        Period: type is OwnerReportType.Sales or OwnerReportType.Commissions or OwnerReportType.ProductRotation ? OperationalReportTestData.SalePeriod : null,
        Dimension: type == OwnerReportType.Sales ? OwnerSalesDimension.Product : null,
        CriticalOnly: type == OwnerReportType.StockRisk ? true : null,
        ExpirationRange: type == OwnerReportType.Expirations ? new(OperationalReportTestData.Today.AddDays(-30), OperationalReportTestData.Today.AddDays(31)) : null,
        RotationMode: type == OwnerReportType.ProductRotation ? OwnerProductRotationMode.LowActivity : null);

    private ServiceProvider ImportedServices(OwnerOverviewTestData.Clock clock, SnapshotImporter importer)
    {
        var services = new ServiceCollection(); services.AddSingleton<TimeProvider>(clock);
        services.AddInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:MediPosDatabase"] = fixture.ConnectionString }).Build());
        services.AddIdentityAuthentication<IdentityAccessTestSetup.TestGoogleIdentitySource, IdentityAccessTestSetup.TestServerSession>();
        services.AddDbContext<MediPosDbContext>(options => options.AddInterceptors(importer));
        services.AddPersistenceFixtureAuthorization();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
    private sealed class SnapshotImporter(string snapshot) : DbTransactionInterceptor
    {
        internal int Imports { get; private set; }
        public override async ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection, TransactionEndEventData eventData, DbTransaction result, CancellationToken cancellationToken = default)
        {
            if (result.IsolationLevel == IsolationLevel.RepeatableRead)
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(snapshot, "^[0-9A-Fa-f]+-[0-9A-Fa-f]+-[0-9]+$")) throw new InvalidOperationException("Unexpected snapshot ID.");
                await using var command = connection.CreateCommand(); command.Transaction = result;
                command.CommandText = $"SET TRANSACTION SNAPSHOT '{snapshot}'";
                await command.ExecuteNonQueryAsync(cancellationToken); Imports++;
            }
            return result;
        }
    }
    private static async Task<ReportExportData> OriginalAsync(MediPosDbContext c, ReportExportReadRequest r)
    {
        switch (r.Query.ReportType)
        {
            case OwnerReportType.Sales:
                var request = ReportExportPolicy.SalesRequest(r);
                var rows = await OperationalSalesQueries.Order(OperationalSalesQueries.Groups(c.Sales.AsNoTracking(), c.SaleLines.AsNoTracking(), c.BusinessProducts.AsNoTracking(),
                    c.Branches.AsNoTracking(), c.Memberships.AsNoTracking(), c.Users.AsNoTracking(), c.Categories.AsNoTracking(), request), request.Dimension, request.Sort).ToArrayAsync(Token);
                var amounts = await OperationalSalesQueries.Totals(c.Sales.AsNoTracking(), c.SaleLines.AsNoTracking(), c.BusinessProducts.AsNoTracking(), request).SingleOrDefaultAsync(Token);
                return new SalesExportData(new(rows, new(rows.Length, amounts?.DistinctSaleCount ?? 0, amounts?.SalesAmount ?? 0)));
            case OwnerReportType.Commissions:
                var commissionRows = OwnerCommissionsReportQueries.Rows(c.CommissionEntries.AsNoTracking(), c.Sales.AsNoTracking(), c.Memberships.AsNoTracking(), c.Users.AsNoTracking(), c.BusinessProducts.AsNoTracking(), ReportExportPolicy.CommissionRequest(r));
                var totals = await OwnerCommissionsReportQueries.Totals(commissionRows).SingleOrDefaultAsync(Token) ?? new(0, 0, 0, 0);
                return new CommissionsExportData(new(await commissionRows.OrderByDescending(row => row.ConfirmedAtUtc).ThenBy(row => row.SaleId).ThenBy(row => row.SaleLineId).ThenBy(row => row.CommissionEntryId).ToArrayAsync(Token), totals));
            case OwnerReportType.StockRisk:
                var stock = await OperationalInventoryQueries.Risk(c.Branches.AsNoTracking(), c.BusinessProducts.AsNoTracking(), c.InventoryLots.AsNoTracking(), c.BranchProductStockThresholds.AsNoTracking(), ReportExportPolicy.StockRequest(r))
                    .OrderBy(row => row.ProductName).ThenBy(row => row.BranchId).ThenBy(row => row.BusinessProductId).ToArrayAsync(Token);
                return new StockRiskExportData(new(stock, new(stock.Length, stock.LongCount(row => row.IsCritical))));
            case OwnerReportType.Expirations:
                var expiration = await OperationalInventoryQueries.Expirations(c.InventoryLots.AsNoTracking(), c.BusinessProducts.AsNoTracking(), ReportExportPolicy.ExpirationRequest(r))
                    .OrderBy(row => row.ExpirationDate).ThenBy(row => row.BranchId).ThenBy(row => row.InventoryLotId).ToArrayAsync(Token);
                return new ExpirationsExportData(new(expiration, new(expiration.Length, expiration.LongCount(row => row.State == OwnerExpirationState.Expired), expiration.LongCount(row => row.State == OwnerExpirationState.Expiring), expiration.LongCount(row => row.State == OwnerExpirationState.Later))));
            case OwnerReportType.InventoryCapital:
                var capitalValues = await OperationalInventoryQueries.CapitalBranches(OperationalInventoryQueries.CapitalLots(c.InventoryLots.AsNoTracking(), c.PurchaseLines.AsNoTracking(), c.BusinessProducts.AsNoTracking(), r.Scope)).ToDictionaryAsync(row => row.BranchId, Token);
                var branchIds = await c.Branches.OrderBy(branch => branch.Id).Select(branch => branch.Id).ToArrayAsync(Token);
                var capital = branchIds.Select(id => capitalValues.GetValueOrDefault(id) ?? new OwnerBranchInventoryCapital { BranchId = id }).ToArray();
                return new CapitalExportData(new(capital, new(capital.Sum(row => row.TotalInventoryCapital), capital.Sum(row => row.ExpiredInventoryCapital), capital.Sum(row => row.ExpiringInventoryCapital))));
            default:
                var lines = OperationalSalesQueries.Lines(c.Sales.AsNoTracking(), c.SaleLines.AsNoTracking(), c.BusinessProducts.AsNoTracking(), r.Scope, r.Period!);
                var rotation = await OperationalInventoryQueries.OrderRotation(OperationalInventoryQueries.Rotation(c.BusinessProducts.AsNoTracking(), lines, c.InventoryLots.AsNoTracking(), ReportExportPolicy.RotationRequest(r)), r.Query.RotationMode!.Value).ToArrayAsync(Token);
                return new RotationExportData(new(rotation, new(rotation.Length, rotation.Sum(row => row.SalesAmount))));
        }
    }
    private static void AssertSame(ReportExportData expected, ReportExportData actual)
    {
        switch (expected)
        {
            case SalesExportData v: var a = Assert.IsType<SalesExportData>(actual); Assert.Equal(v.Page.Totals, a.Page.Totals); Assert.Equal(v.Page.Rows, a.Page.Rows); break;
            case CommissionsExportData v: var b = Assert.IsType<CommissionsExportData>(actual); Assert.Equal(v.Page.Totals, b.Page.Totals); Assert.Equal(v.Page.Rows, b.Page.Rows); break;
            case StockRiskExportData v: var c = Assert.IsType<StockRiskExportData>(actual); Assert.Equal(v.Page.Totals, c.Page.Totals); Assert.Equal(v.Page.Rows, c.Page.Rows); break;
            case ExpirationsExportData v: var d = Assert.IsType<ExpirationsExportData>(actual); Assert.Equal(v.Page.Totals, d.Page.Totals); Assert.Equal(v.Page.Rows, d.Page.Rows); break;
            case CapitalExportData v: var e = Assert.IsType<CapitalExportData>(actual); Assert.Equal(v.Snapshot.Totals, e.Snapshot.Totals); Assert.Equal(v.Snapshot.Branches, e.Snapshot.Branches); break;
            case RotationExportData v: var f = Assert.IsType<RotationExportData>(actual); Assert.Equal(v.Page.Totals, f.Page.Totals); Assert.Equal(v.Page.Rows, f.Page.Rows); break;
        }
    }
    private static void AssertValues(ReportExportData data, byte[] bytes, ReportExportFormat format)
    {
        decimal[][] rowValues; decimal[] totals;
        switch (data)
        {
            case SalesExportData v: rowValues = v.Page.Rows.Select(row => row.BaseQuantitySold.HasValue ? new[] { (decimal)row.SaleCount, row.SalesAmount, row.BaseQuantitySold.Value } : [row.SaleCount, row.SalesAmount]).ToArray(); totals = [v.Page.Totals.GroupCount, v.Page.Totals.DistinctSaleCount, v.Page.Totals.SalesAmount]; break;
            case CommissionsExportData v: rowValues = v.Page.Rows.Select(row => new[] { row.OriginalEarnedAmount, row.CompensatedAmount, row.CurrentNetAmount }).ToArray(); totals = [v.Page.Totals.EarnedEntryCount, v.Page.Totals.OriginalEarnedAmount, v.Page.Totals.CompensatedAmount, v.Page.Totals.CurrentNetAmount]; break;
            case StockRiskExportData v: rowValues = v.Page.Rows.Select(row => new[] { row.PhysicalStockBase, row.SellableStockBase, row.MinimumStockBase!.Value }).ToArray(); totals = [v.Page.Totals.BranchProductCount, v.Page.Totals.CriticalStockProductCount]; break;
            case ExpirationsExportData v: rowValues = v.Page.Rows.Select(row => new[] { row.QuantityAvailableBase }).ToArray(); totals = [v.Page.Totals.LotCount, v.Page.Totals.ExpiredLotCount, v.Page.Totals.ExpiringLotCount, v.Page.Totals.LaterLotCount]; break;
            case CapitalExportData v: rowValues = v.Snapshot.Branches.Select(row => new[] { row.TotalInventoryCapital, row.ExpiredInventoryCapital, row.ExpiringInventoryCapital }).ToArray(); totals = [v.Snapshot.Totals.TotalInventoryCapital, v.Snapshot.Totals.ExpiredInventoryCapital, v.Snapshot.Totals.ExpiringInventoryCapital]; break;
            case RotationExportData v: rowValues = v.Page.Rows.Select(row => new[] { (decimal)row.SalesCount, row.BaseQuantitySold, row.SalesAmount, row.CurrentSellableStockBase }).ToArray(); totals = [v.Page.Totals.ProductCount, v.Page.Totals.SalesAmount]; break;
            default: throw new InvalidOperationException();
        }
        if (format == ReportExportFormat.Pdf)
        {
            using var pdf = PdfDocument.Open(bytes); var text = string.Join("\n", pdf.GetPages().Select(page => page.Text));
            Assert.Contains("REPORTE INTERNO MEDIPOS", text); Assert.Contains("Totales del conjunto completo", text);
            foreach (var number in rowValues.SelectMany(value => value).Concat(totals))
                Assert.Contains(number.ToString("0.####", CultureInfo.GetCultureInfo("es-PE")), text);
            return;
        }
        using var stream = new MemoryStream(bytes); using var workbook = SpreadsheetDocument.Open(stream, false);
        var rows = workbook.WorkbookPart!.WorksheetParts.Single().Worksheet.Descendants<Row>().ToArray();
        var dataStart = Array.FindIndex(rows, row => row.Elements<Cell>().Any(cell => cell.InlineString?.InnerText == "Generado (Lima)")) + 4;
        var actualRows = rows.Skip(dataStart).Take(data.RowCount).ToArray();
        Assert.Equal(data.RowCount, actualRows.Length);
        static decimal[] Numbers(Row row) => row.Elements<Cell>().Where(cell => cell.DataType?.Value == CellValues.Number && cell.StyleIndex?.Value is 1 or 2)
            .Select(cell => decimal.Parse(cell.CellValue!.Text, CultureInfo.InvariantCulture)).ToArray();
        for (var i = 0; i < actualRows.Length; i++) Assert.Equal(rowValues[i], Numbers(actualRows[i]));
        var totalStart = Array.FindIndex(rows, row => row.Elements<Cell>().FirstOrDefault()?.InlineString?.InnerText == "Totales del conjunto completo") + 1;
        Assert.Equal(totals, rows.Skip(totalStart).SelectMany(Numbers));
    }
}
