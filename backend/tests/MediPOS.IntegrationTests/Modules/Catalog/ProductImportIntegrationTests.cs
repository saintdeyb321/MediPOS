using ClosedXML.Excel;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Catalog.CreateLocalBusinessProduct;
using MediPOS.Application.Modules.Catalog.ProductImport;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Catalog.ProductImport;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WorkbookAdapter = MediPOS.Infrastructure.Modules.Catalog.ProductImport.ProductImportWorkbook;

namespace MediPOS.IntegrationTests.Modules.Catalog;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class ProductImportIntegrationTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task MixedImportCreatesOnlyValidLocalProductsAndBaseUnitsWithPersistedOutcomesAndOneSummaryAudit()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        var retail = ProductImportTestData.Retail(tenant, "IMPORT-R");
        var medicine = ProductImportTestData.Retail(tenant, "IMPORT-M");
        medicine["ProductType"] = "medicine";
        medicine["ActiveIngredients"] = "Paracetamol | Cafeína";
        medicine["Strengths"] = "500 mg | 30 mg";
        medicine["DosageForm"] = "Tableta";
        medicine["Route"] = "Oral";
        var bad = ProductImportTestData.Retail(tenant, "IMPORT-BAD");
        bad["RetailPrice"] = "-1";
        var existing = ProductImportTestData.Retail(tenant, "R1");
        var repeated = ProductImportTestData.Retail(tenant, "REPEATED");
        var bytes = await ProductImportTestData.WorkbookAsync(retail, medicine, bad, existing, repeated, repeated);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var context = source.GetRequiredService<MediPosDbContext>();
        var globals = await context.GlobalProducts.CountAsync(TestContext.Current.CancellationToken);
        var result = await ProductImportTestData.RunAsync(source, tenant, bytes);
        Assert.Equal("completed", result.Status);
        Assert.Equal(6, result.TotalRows);
        Assert.Equal(2, result.ImportedRows);
        Assert.Equal(4, result.InvalidRows);
        Assert.Equal(6, result.Rows.Count);
        Assert.Equal("import.duplicate_internal_code", result.Rows[3].ErrorCode);
        Assert.All(result.Rows.Skip(4), value => Assert.Equal("import.duplicate_internal_code", value.ErrorCode));
        var job = await context.ImportJobs.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(7, job.Id.Version);
        Assert.Equal(tenant.TenantId, job.TenantId);
        Assert.Equal(tenant.Identity.ActorId, job.ActorId);
        Assert.Equal(TimeSpan.Zero, job.CreatedAt.Offset);
        Assert.NotNull(job.CompletedAt);
        Assert.Equal(2, job.ValidRows);
        var products = await context.BusinessProducts.AsNoTracking().Where(value => value.InternalCode.StartsWith("IMPORT-"))
            .OrderBy(value => value.InternalCode).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, products.Count);
        Assert.All(products, value => Assert.Null(value.GlobalProductId));
        var normalized = products.Single(value => value.ProductType == ProductType.Medicine).Medicine!;
        Assert.NotNull(normalized.EquivalenceKey);
        Assert.Contains(normalized.Components, value => value.Ingredient == "CAFEINA" && value.StrengthNormalized == "30 MG");
        foreach (var product in products)
        {
            var unit = await context.ProductUnits.AsNoTracking().SingleAsync(value => value.BusinessProductId == product.Id, TestContext.Current.CancellationToken);
            Assert.True(unit.IsBaseUnit && unit.IsActive);
            Assert.Equal(1m, unit.ConversionToBase);
            Assert.Equal(tenant.TenantId, unit.TenantId);
        }
        var preserved = await context.BusinessProducts.SingleAsync(value => value.Id == tenant.BusinessProductId, TestContext.Current.CancellationToken);
        Assert.Equal("Local retail", preserved.Name);
        Assert.Equal(0m, preserved.RetailPrice);
        Assert.Equal(globals, await context.GlobalProducts.CountAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await context.Purchases.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await context.InventoryLots.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await context.StockMovements.ToListAsync(TestContext.Current.CancellationToken));
        var audit = await context.AuditLogs.SingleAsync(value => value.Action == AuditAction.CatalogProductsImported, TestContext.Current.CancellationToken);
        Assert.Equal(job.Id, audit.EntityId);
        Assert.Equal(tenant.TenantId, audit.TenantId);
        Assert.Equal(6, await context.ImportRowResults.CountAsync(TestContext.Current.CancellationToken));
        var restored = await source.GetRequiredService<GetImportJobResultHandler>().HandleAsync(
            new(tenant.TenantId, job.Id, Limit: 2), TestContext.Current.CancellationToken);
        Assert.Equal(2, restored.Rows.Count);
        Assert.Equal(5, restored.NextAfterRowNumber);
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task TwoConcurrentImportsWithSameCodeCreateOneProductAndReportTheOtherRowAsConflict()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        var bytes = await ProductImportTestData.WorkbookAsync(ProductImportTestData.Retail(tenant, "RACING"));
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readers = 0;
        async Task AfterReadAsync()
        {
            if (Interlocked.Increment(ref readers) == 2) ready.TrySetResult();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        }
        async Task<ImportJobResult> RunAsync()
        {
            await using var scope = services.CreateAsyncScope();
            var source = scope.ServiceProvider;
            var store = new ProductImportTestData.CoordinatedStore(source.GetRequiredService<IProductImportStore>(), AfterReadAsync);
            using var input = new MemoryStream(bytes);
            return await new ImportProductsFromExcelHandler(source.GetRequiredService<IProductImportWorkbookReader>(), store,
                source.GetRequiredService<TimeProvider>()).HandleAsync(new(tenant.TenantId, "race.xlsx", input, tenant.Identity.ActorId),
                TestContext.Current.CancellationToken);
        }
        var results = await Task.WhenAll(RunAsync(), RunAsync());
        Assert.All(results, value => Assert.Equal("completed", value.Status));
        Assert.Equal(1, results.Sum(value => value.ImportedRows));
        Assert.Equal(1, results.Sum(value => value.InvalidRows));
        Assert.Equal("import.duplicate_internal_code", Assert.Single(results.SelectMany(value => value.Rows),
            value => value.Status == "invalid").ErrorCode);
        await using var verification = fixture.CreateContext(tenant.TenantId);
        var product = await verification.BusinessProducts.SingleAsync(value => value.InternalCode == "RACING", TestContext.Current.CancellationToken);
        Assert.Equal(1, await verification.ProductUnits.CountAsync(value => value.BusinessProductId == product.Id, TestContext.Current.CancellationToken));
        Assert.Equal(2, await verification.ImportJobs.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, await verification.AuditLogs.CountAsync(value => value.Action == AuditAction.CatalogProductsImported, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ManualCreationAfterValidationWinsUniqueConstraintWithoutBeingOverwritten()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        var bytes = await ProductImportTestData.WorkbookAsync(ProductImportTestData.Retail(tenant, "MANUAL-RACE"));
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var importScope = services.CreateAsyncScope();
        var source = importScope.ServiceProvider;
        async Task WaitAsync()
        {
            ready.TrySetResult();
            await resume.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        }
        var coordinated = new ProductImportTestData.CoordinatedStore(source.GetRequiredService<IProductImportStore>(), WaitAsync);
        using var input = new MemoryStream(bytes);
        var pending = new ImportProductsFromExcelHandler(source.GetRequiredService<IProductImportWorkbookReader>(), coordinated,
            source.GetRequiredService<TimeProvider>()).HandleAsync(new(tenant.TenantId, "race.xlsx", input, tenant.Identity.ActorId),
            TestContext.Current.CancellationToken);
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        await using (var manual = services.CreateAsyncScope())
        {
            await manual.ServiceProvider.GetRequiredService<CreateLocalBusinessProductHandler>().HandleAsync(
                new(tenant.TenantId, "MANUAL-RACE", ProductType.Retail, "Manual name", tenant.CategoryId, "Manual brand", null, null, 19m, null,
                    tenant.Identity.ActorId), TestContext.Current.CancellationToken);
        }
        resume.TrySetResult();
        var result = await pending;
        Assert.Equal(0, result.ImportedRows);
        Assert.Equal("import.duplicate_internal_code", Assert.Single(result.Rows).ErrorCode);
        await using var verification = fixture.CreateContext(tenant.TenantId);
        var product = await verification.BusinessProducts.SingleAsync(value => value.InternalCode == "MANUAL-RACE", TestContext.Current.CancellationToken);
        Assert.Equal("Manual name", product.Name);
        Assert.Equal(19m, product.RetailPrice);
    }

    [Fact]
    public async Task UnitPersistenceFailureRollsBackOnlyThatProductAndPreservesEarlierCommittedRows()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        var accepted = ProductImportTestData.Retail(tenant, "BEFORE-FAIL");
        var rejected = ProductImportTestData.Retail(tenant, "UNIT-FAIL");
        rejected["BaseUnitName"] = "Fault-" + Guid.NewGuid().ToString("N");
        var bytes = await ProductImportTestData.WorkbookAsync(accepted, rejected, ProductImportTestData.Retail(tenant, "AFTER-FAIL"));
        var constraint = "test_import_unit_" + Guid.NewGuid().ToString("N");
        await using var admin = fixture.CreateConstraintContext();
        // Generated test-only constraint in the isolated integration database; no existing constraints are disabled.
        var addUnitConstraint = await admin.Database.SqlQuery<string>($"""
            SELECT format('ALTER TABLE product_units ADD CONSTRAINT %I CHECK (name <> %L)',
                {constraint}, {rejected["BaseUnitName"]}) AS "Value"
            """).SingleAsync(TestContext.Current.CancellationToken);
        await admin.Database.ExecuteSqlRawAsync(addUnitConstraint, TestContext.Current.CancellationToken);
        try
        {
            await using var services = IdentityAccessTestSetup.CreateServices(fixture);
            await using var scope = services.CreateAsyncScope();
            var result = await ProductImportTestData.RunAsync(scope.ServiceProvider, tenant, bytes);
            Assert.Equal("failed", result.Status);
            Assert.Equal(1, result.ImportedRows);
            Assert.Single(result.Rows);
            var context = scope.ServiceProvider.GetRequiredService<MediPosDbContext>();
            Assert.False(await context.BusinessProducts.AnyAsync(value => value.InternalCode == "UNIT-FAIL" || value.InternalCode == "AFTER-FAIL",
                TestContext.Current.CancellationToken));
            Assert.True(await context.BusinessProducts.AnyAsync(value => value.InternalCode == "BEFORE-FAIL", TestContext.Current.CancellationToken));
            Assert.False(await context.ProductUnits.AnyAsync(value => value.Name == rejected["BaseUnitName"], TestContext.Current.CancellationToken));
            Assert.False(await context.AuditLogs.AnyAsync(value => value.Action == AuditAction.CatalogProductsImported, TestContext.Current.CancellationToken));
        }
        finally
        {
            var dropUnitConstraint = await admin.Database.SqlQuery<string>(
                $"SELECT format('ALTER TABLE product_units DROP CONSTRAINT %I', {constraint}) AS \"Value\"").SingleAsync(TestContext.Current.CancellationToken);
            await admin.Database.ExecuteSqlRawAsync(dropUnitConstraint, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task SummaryAuditFailureCannotLeaveJobCompletedWithoutItsAudit()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        var bytes = await ProductImportTestData.WorkbookAsync(ProductImportTestData.Retail(tenant, "AUDIT-FAIL"));
        var constraint = "test_import_audit_" + Guid.NewGuid().ToString("N");
        await using var admin = fixture.CreateConstraintContext();
        var addAuditConstraint = await admin.Database.SqlQuery<string>($"""
            SELECT format('ALTER TABLE audit_logs ADD CONSTRAINT %I CHECK (action <> ''catalog.products_imported'' OR tenant_id <> %L::uuid)',
                {constraint}, {tenant.TenantId.ToString("D")}) AS "Value"
            """).SingleAsync(TestContext.Current.CancellationToken);
        await admin.Database.ExecuteSqlRawAsync(addAuditConstraint, TestContext.Current.CancellationToken);
        try
        {
            await using var services = IdentityAccessTestSetup.CreateServices(fixture);
            await using var scope = services.CreateAsyncScope();
            var result = await ProductImportTestData.RunAsync(scope.ServiceProvider, tenant, bytes);
            Assert.Equal("failed", result.Status);
            Assert.Equal(1, result.ImportedRows);
            var context = scope.ServiceProvider.GetRequiredService<MediPosDbContext>();
            Assert.False(await context.ImportJobs.AnyAsync(value => value.Status == ImportJobStatus.Completed, TestContext.Current.CancellationToken));
            Assert.False(await context.AuditLogs.AnyAsync(value => value.Action == AuditAction.CatalogProductsImported, TestContext.Current.CancellationToken));
        }
        finally
        {
            var dropAuditConstraint = await admin.Database.SqlQuery<string>(
                $"SELECT format('ALTER TABLE audit_logs DROP CONSTRAINT %I', {constraint}) AS \"Value\"").SingleAsync(TestContext.Current.CancellationToken);
            await admin.Database.ExecuteSqlRawAsync(dropAuditConstraint, TestContext.Current.CancellationToken);
        }
    }

    [Theory]
    [InlineData("TenantId")]
    [InlineData("Stock")]
    [InlineData("Cost")]
    public async Task WorkbookCannotInjectTenantOrOperationalFields(string header)
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        var bytes = await ProductImportTestData.WorkbookAsync(ProductImportTestData.Retail(tenant, "INJECTED"));
        using var source = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(source, WorkbookAdapter.CreateLoadOptions());
        workbook.Worksheet(ProductImportTemplate.SheetName).Cell(ProductImportTemplate.HeaderRow, 1).Value = header;
        using var rewritten = new MemoryStream();
        workbook.SaveAs(rewritten, false, false);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => ProductImportTestData.RunAsync(scope.ServiceProvider, tenant, rewritten.ToArray()));
        Assert.Equal(ProductImportErrors.Headers, error.Error);
        var context = scope.ServiceProvider.GetRequiredService<MediPosDbContext>();
        Assert.Empty(await context.ImportJobs.ToListAsync(TestContext.Current.CancellationToken));
        Assert.False(await context.BusinessProducts.AnyAsync(value => value.InternalCode == "INJECTED", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ImportDoesNotWaitOnTenantLicenseRowLock()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        var bytes = await ProductImportTestData.WorkbookAsync(ProductImportTestData.Retail(tenant, "NO-LICENSE-LOCK"));
        await using var locker = fixture.CreateContext(tenant.TenantId);
        await using var transaction = await locker.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await locker.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM licenses WHERE tenant_id = {tenant.TenantId} FOR UPDATE",
            TestContext.Current.CancellationToken);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var input = new MemoryStream(bytes);
        var result = await scope.ServiceProvider.GetRequiredService<ImportProductsFromExcelHandler>().HandleAsync(
            new(tenant.TenantId, "items.xlsx", input, tenant.Identity.ActorId), timeout.Token);
        Assert.Equal(1, result.ImportedRows);
        await transaction.RollbackAsync(TestContext.Current.CancellationToken);
    }
}
