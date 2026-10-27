using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Catalog;
using MediPOS.Application.Modules.Catalog.CreateCategory;
using MediPOS.Application.Modules.Catalog.CreateLocalBusinessProduct;
using MediPOS.Application.Modules.Catalog.ProductImport;
using MediPOS.Application.Modules.Catalog.ReplaceProductUnits;
using MediPOS.Application.Modules.Cash.OpenCashSession;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.Inventory.AdjustStock;
using MediPOS.Application.Modules.Inventory.SetBranchProductStockThreshold;
using MediPOS.Application.Modules.Purchasing;
using MediPOS.Application.Modules.Purchasing.ConfirmPurchase;
using MediPOS.Application.Modules.Purchasing.CreatePurchase;
using MediPOS.Application.Modules.Purchasing.CreateSupplier;
using MediPOS.Application.Modules.Purchasing.ReplacePurchaseLines;
using MediPOS.Application.Modules.SalesPos.CreateSaleDraft;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Infrastructure;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.Catalog;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MediPOS.IntegrationTests.Modules.Purchasing;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class RecoverableOperationPostgreSqlTests(PostgreSqlFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private const string Hash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    [Theory]
    [InlineData("supplier")]
    [InlineData("adjustment")]
    [InlineData("minimum")]
    [InlineData("sale-draft")]
    public async Task ResponseLossIsRecoveredAndAnIdenticalRetryNeverInvokesTheBusinessWriteAgain(string kind)
    {
        var data = await Prepare();
        await using var services = Runtime();
        var id = Guid.NewGuid();
        OperationReceipt first;
        await using (var scope = services.CreateAsyncScope())
        {
            var source = Authenticate(scope.ServiceProvider, data.Owner.UserId);
            if (kind == "sale-draft") await source.GetRequiredService<OpenCashSessionHandler>().HandleAsync(new(data.Owner.TenantId, data.Owner.BranchId, 0m), Token);
            first = await source.GetRequiredService<IRecoverableOperations>().ExecuteAsync(data.Owner.TenantId, data.Owner.BranchId, id, kind, Hash, token => Write(source, data, kind, token), Token);
        }
        // The caller lost the response. Recovery uses a new scope and the same intention, not another POST.
        await using var recoveryScope = services.CreateAsyncScope();
        var recovery = Authenticate(recoveryScope.ServiceProvider, data.Owner.UserId).GetRequiredService<IRecoverableOperations>();
        Assert.Equal(first, await recovery.ReadAsync(data.Owner.TenantId, data.Owner.BranchId, id, Token));
        Assert.Equal(first, await recovery.ExecuteAsync(data.Owner.TenantId, data.Owner.BranchId, id, kind, Hash, _ => throw new InvalidOperationException("Duplicate write"), Token));
        var conflict = await Assert.ThrowsAsync<ApplicationErrorException>(() => recovery.ExecuteAsync(data.Owner.TenantId, data.Owner.BranchId, id, kind, new string('B', 64), _ => throw new InvalidOperationException(), Token));
        Assert.Equal("operation.intention_conflict", conflict.Error.Code);
        await using var db = fixture.CreateContext(data.Owner.TenantId);
        Assert.Equal(1, await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM tenant_operation_receipts WHERE request_id = {id}").SingleAsync(Token));
        if (kind == "adjustment") Assert.Equal(4m, (await db.InventoryLots.SingleAsync(x => x.Id == data.LotId, Token)).QuantityAvailableBase);
    }

    [Fact]
    public async Task FailureAfterTheInnerSupplierSaveRollsBackBothBusinessDataAndReceipt()
    {
        var data = await Prepare(); await using var services = Runtime();
        await using var scope = services.CreateAsyncScope(); var source = Authenticate(scope.ServiceProvider, data.Owner.UserId);
        var operations = source.GetRequiredService<IRecoverableOperations>(); var id = Guid.NewGuid(); var name = "Rollback-" + id.ToString("N");
        await Assert.ThrowsAsync<OperationCanceledException>(() => operations.ExecuteAsync(data.Owner.TenantId, data.Owner.BranchId, id, "supplier", Hash, async token =>
        {
            await source.GetRequiredService<CreateSupplierHandler>().HandleAsync(new(data.Owner.TenantId, name, null, null, data.Owner.UserId), token);
            throw new OperationCanceledException("Injected before the receipt commit.");
        }, Token));
        Assert.Empty(source.GetRequiredService<MediPosDbContext>().ChangeTracker.Entries());
        await using var db = fixture.CreateContext(data.Owner.TenantId);
        Assert.False(await db.Suppliers.AnyAsync(x => x.Name == name, Token));
        var missing = await Assert.ThrowsAsync<ApplicationErrorException>(() => operations.ReadAsync(data.Owner.TenantId, data.Owner.BranchId, id, Token));
        Assert.Equal("operation.not_observed", missing.Error.Code);
    }

    [Fact]
    public async Task ConcurrentOriginalIntentIsBusyAndBecomesOneImmutableReceipt()
    {
        var data = await Prepare(); await using var services = Runtime();
        await using var firstScope = services.CreateAsyncScope();
        await using var competingScope = services.CreateAsyncScope();
        var firstSource = Authenticate(firstScope.ServiceProvider, data.Owner.UserId);
        var competing = Authenticate(competingScope.ServiceProvider, data.Owner.UserId).GetRequiredService<IRecoverableOperations>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var id = Guid.NewGuid();
        var first = firstSource.GetRequiredService<IRecoverableOperations>().ExecuteAsync(data.Owner.TenantId, data.Owner.BranchId, id, "supplier", Hash, async token =>
        {
            entered.SetResult();
            await release.Task.WaitAsync(token);
            return await Write(firstSource, data, "supplier", token);
        }, Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
            var busy = await Assert.ThrowsAsync<ApplicationErrorException>(() => competing.ExecuteAsync(data.Owner.TenantId, data.Owner.BranchId, id, "supplier", Hash,
                _ => throw new InvalidOperationException("A busy intention must not execute twice."), Token));
            Assert.Equal("operation.in_progress", busy.Error.Code);
            var missing = await Assert.ThrowsAsync<ApplicationErrorException>(() => competing.ReadAsync(data.Owner.TenantId, data.Owner.BranchId, id, Token));
            Assert.Equal("operation.not_observed", missing.Error.Code);
        }
        finally { release.TrySetResult(); await first; }
        var committed = await first;
        Assert.Equal(committed, await competing.ExecuteAsync(data.Owner.TenantId, data.Owner.BranchId, id, "supplier", Hash,
            _ => throw new InvalidOperationException("A committed intention must replay."), Token));
        await using var db = fixture.CreateContext(data.Owner.TenantId);
        Assert.Equal(1, await db.Suppliers.CountAsync(x => x.Name == "Synthetic supplier", Token));
        var denied = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE tenant_operation_receipts SET result_json = '{{}}' WHERE request_id = {id}", Token));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, denied.SqlState);
        await using var constraintContext = fixture.CreateConstraintContext(data.Owner.TenantId);
        var immutable = await Assert.ThrowsAsync<PostgresException>(() => constraintContext.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM tenant_operation_receipts WHERE request_id = {id}", Token));
        Assert.Equal(PostgresErrorCodes.CheckViolation, immutable.SqlState);
        Assert.Equal(1, await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM tenant_operation_receipts WHERE request_id = {id}").SingleAsync(Token));
    }

    [Fact]
    public async Task ReceiptsAreBoundToMembershipBranchTenantAndCurrentAuthorization()
    {
        var data = await Prepare(); await using var services = Runtime();
        await using var scope = services.CreateAsyncScope(); var source = Authenticate(scope.ServiceProvider, data.Owner.UserId);
        var id = Guid.NewGuid(); var operations = source.GetRequiredService<IRecoverableOperations>();
        await operations.ExecuteAsync(data.Owner.TenantId, data.Owner.BranchId, id, "supplier", Hash, token => Write(source, data, "supplier", token), Token);
        await using var otherScope = services.CreateAsyncScope();
        var other = Authenticate(otherScope.ServiceProvider, data.Owner.ActorId).GetRequiredService<IRecoverableOperations>();
        await Assert.ThrowsAsync<ApplicationErrorException>(() => other.ReadAsync(data.Owner.TenantId, data.Owner.BranchId, id, Token));
        await using var absentScope = fixture.CreateContext(Guid.NewGuid());
        Assert.Equal(0, await absentScope.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM tenant_operation_receipts WHERE request_id = {id}").SingleAsync(Token));
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => operations.ReadAsync(data.Owner.TenantId, Guid.NewGuid(), id, Token));
        Assert.NotNull(error);
    }

    [Fact]
    public async Task ExcelPersistsItsOriginalJobBeforeRowsAndRemainsRecoverableAfterResponseLoss()
    {
        var data = await Prepare(); await using var services = Runtime();
        await using var scope = services.CreateAsyncScope(); var source = Authenticate(scope.ServiceProvider, data.Owner.UserId);
        var bytes = await ProductImportTestData.WorkbookAsync(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["InternalCode"] = "IMPORT-RECOVERY", ["ProductType"] = "retail", ["Name"] = "Synthetic import",
            ["CategoryId"] = data.CategoryId.ToString("D"), ["BrandOrLaboratory"] = "Documented test brand", ["RetailPrice"] = "1.00", ["BaseUnitName"] = "Unidad",
        });
        var id = Guid.NewGuid(); var operations = source.GetRequiredService<IRecoverableOperations>();
        await Assert.ThrowsAsync<IOException>(() => operations.ExecuteAsync(data.Owner.TenantId, data.Owner.BranchId, id, "product-import", Hash, async token =>
        {
            using var file = new MemoryStream(bytes);
            await source.GetRequiredService<ImportProductsFromExcelHandler>().HandleAsync(new(data.Owner.TenantId, "synthetic.xlsx", file, data.Owner.UserId), token);
            throw new IOException("Injected response loss after independently committed rows.");
        }, Token));
        var receipt = await operations.ReadAsync(data.Owner.TenantId, data.Owner.BranchId, id, Token);
        using var result = JsonDocument.Parse(receipt.ResultJson); var jobId = result.RootElement.GetProperty("importJobId").GetGuid();
        var job = await source.GetRequiredService<GetImportJobResultHandler>().HandleAsync(new(data.Owner.TenantId, jobId), Token);
        Assert.Equal("completed", job.Status); Assert.Equal(1, job.ImportedRows);
        await operations.ExecuteAsync(data.Owner.TenantId, data.Owner.BranchId, id, "product-import", Hash, _ => throw new InvalidOperationException("Must not re-import"), Token);
        await using var db = fixture.CreateContext(data.Owner.TenantId);
        Assert.Equal(1, await db.BusinessProducts.CountAsync(x => x.InternalCode == "IMPORT-RECOVERY", Token));
    }

    [Fact]
    public async Task LocalMedicineCorrectionUsesLockedVersionAndPreservesOriginalAuditAndStock()
    {
        var data = await Prepare(); await using var services = Runtime(); await using var scope = services.CreateAsyncScope();
        var source = Authenticate(scope.ServiceProvider, data.Owner.UserId);
        var original = await source.GetRequiredService<CreateLocalBusinessProductHandler>().HandleAsync(new(data.Owner.TenantId, "MED-CORRECTION", ProductType.Medicine, "Synthetic medicine", data.CategoryId, "Documented test brand", null,
            new MedicineInput([new("Paracetamol", "500 mg")], "Tableta", "Oral"), 1m, null, data.Owner.UserId), Token);
        var view = await source.GetRequiredService<CatalogDirectoryQueries>().ProductAsync(data.Owner.TenantId, data.Owner.BranchId, original.Id, Token);
        await using var before = fixture.CreateContext(data.Owner.TenantId);
        var originalAudit = await before.AuditLogs.AsNoTracking().SingleAsync(x => x.EntityId == original.Id, Token);
        var lots = await before.InventoryLots.AsNoTracking().Select(x => new { x.Id, x.QuantityAvailableBase }).ToArrayAsync(Token);
        var movements = await before.StockMovements.AsNoTracking().Select(x => x.Id).ToArrayAsync(Token);
        var command = new UpdateLocalProductCommand(data.Owner.TenantId, original.Id, view.Revision, "Corrected synthetic medicine", data.CategoryId, "Documented test brand", null,
            new MedicineInput([new("Paracetamol", "25 µg")], "Tableta", "Oral"), data.Owner.UserId);
        await source.GetRequiredService<UpdateLocalProductHandler>().HandleAsync(command, Token);
        var conflict = await Assert.ThrowsAsync<ApplicationErrorException>(() => source.GetRequiredService<UpdateLocalProductHandler>().HandleAsync(command, Token));
        Assert.Equal(CatalogErrors.ConcurrentChange, conflict.Error);
        await using var db = fixture.CreateContext(data.Owner.TenantId);
        Assert.Equal(originalAudit.AfterJson, (await db.AuditLogs.AsNoTracking().SingleAsync(x => x.Id == originalAudit.Id, Token)).AfterJson);
        Assert.Equal(lots.OrderBy(x => x.Id), (await db.InventoryLots.AsNoTracking().Select(x => new { x.Id, x.QuantityAvailableBase }).ToArrayAsync(Token)).OrderBy(x => x.Id));
        Assert.Equal(movements.Order(), (await db.StockMovements.AsNoTracking().Select(x => x.Id).ToArrayAsync(Token)).Order());
        var current = await db.BusinessProducts.AsNoTracking().SingleAsync(x => x.Id == original.Id, Token);
        Assert.Null(current.GlobalProductId); Assert.Equal(MedicineComponent.Create("Paracetamol", "25 µg").StrengthNormalized, Assert.Single(current.Medicine!.Components).StrengthNormalized);
        // Returning to identical content must not revive the first review (ABA).
        var correctedView = await source.GetRequiredService<CatalogDirectoryQueries>().ProductAsync(data.Owner.TenantId, data.Owner.BranchId, original.Id, Token);
        await source.GetRequiredService<UpdateLocalProductHandler>().HandleAsync(command with
        {
            ExpectedRevision = correctedView.Revision, Name = original.Name,
            Medicine = new MedicineInput([new("Paracetamol", "500 mg")], "Tableta", "Oral"),
        }, Token);
        var staleOriginal = await Assert.ThrowsAsync<ApplicationErrorException>(() => source.GetRequiredService<UpdateLocalProductHandler>().HandleAsync(command, Token));
        Assert.Equal(CatalogErrors.ConcurrentChange, staleOriginal.Error);
        Assert.Equal(3, await db.AuditLogs.CountAsync(x => x.EntityId == original.Id, Token));
    }

    [Fact]
    public async Task ConcurrentLineEditorsCannotConfirmAReviewWhoseLinesWereReplaced()
    {
        var (tenant, _) = await MediPOS.IntegrationTests.Tenancy.TenantIsolationTestData.CreatePairAsync(fixture);
        var rows = await PurchaseTestData.CreateAsync(fixture, tenant);
        var revision = PurchaseTestData.Revision(fixture, rows.TenantId, rows.PurchaseId);
        await using var db = fixture.CreateContext(rows.TenantId);
        var unit = await db.ProductUnits.AsNoTracking().FirstAsync(x => x.BusinessProductId == tenant.BusinessProductId, Token);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var ready = 0;
        async Task<string> Attempt()
        {
            await using var scope = services.CreateAsyncScope();
            if (Interlocked.Increment(ref ready) == 2) start.SetResult();
            await start.Task.WaitAsync(Token);
            try
            {
                await scope.ServiceProvider.GetRequiredService<ReplacePurchaseLinesHandler>().HandleAsync(new(rows.TenantId, rows.PurchaseId,
                    [new(tenant.BusinessProductId, unit.Id, 1m, 1m, null, null)], rows.ActorId, revision), Token);
                return "saved";
            }
            catch (ApplicationErrorException error) { return error.Error.Code; }
        }
        var outcomes = await Task.WhenAll(Attempt(), Attempt());
        Assert.Single(outcomes, x => x == "saved"); Assert.Single(outcomes, x => x == "purchasing.receipt_changed");
        await using var confirmation = services.CreateAsyncScope();
        var stale = await Assert.ThrowsAsync<ApplicationErrorException>(() => confirmation.ServiceProvider.GetRequiredService<ConfirmPurchaseHandler>().HandleAsync(new(rows.TenantId, rows.PurchaseId, rows.ActorId, revision), Token));
        Assert.Equal("purchasing.receipt_changed", stale.Error.Code);
        Assert.Empty(await db.StockMovements.ToArrayAsync(Token)); Assert.Empty(await db.InventoryLots.ToArrayAsync(Token));
    }

    private static IServiceProvider Authenticate(IServiceProvider source, Guid userId)
    {
        ((IdentityAccessTestSetup.TestServerSession)source.GetRequiredService<IAuthenticatedMediPosUser>()).UserId = userId;
        return source;
    }
    private ServiceProvider Runtime()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new IdentityAccessTestSetup.Clock());
        services.AddInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:MediPosDatabase"] = fixture.ConnectionString }).Build());
        services.AddIdentityAuthentication<IdentityAccessTestSetup.TestGoogleIdentitySource, IdentityAccessTestSetup.TestServerSession>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }
    private async Task<Data> Prepare()
    {
        await using var setup = IdentityAccessTestSetup.CreateServices(fixture); await using var scope = setup.CreateAsyncScope(); var source = scope.ServiceProvider;
        var owner = await IdentityAccessTestSetup.CreateAsync(source, TenantRole.Owner);
        var category = await source.GetRequiredService<CreateCategoryHandler>().HandleAsync(new("Recovery synthetic category"), Token);
        var product = await source.GetRequiredService<CreateLocalBusinessProductHandler>().HandleAsync(new(owner.TenantId, "RECOVERY", ProductType.Retail, "Synthetic item", category.Id, "Documented test brand", null, null, 1m, 0.9m, owner.UserId), Token);
        var units = await source.GetRequiredService<VersionedProductUnitsFixture>().HandleAsync(new(owner.TenantId, product.Id, [new("Unidad", 1m, true)], owner.UserId), Token);
        var purchase = await source.GetRequiredService<CreatePurchaseHandler>().HandleAsync(new(owner.TenantId, owner.BranchId, null, null, owner.UserId), Token);
        var lines = await source.GetRequiredService<ReplacePurchaseLinesHandler>().HandleAsync(new(owner.TenantId, purchase.Id, [new(product.Id, units[0].Id, 5m, 0.5m, null, null)], owner.UserId, PurchaseRevision.For(purchase.Id, [])), Token);
        await source.GetRequiredService<ConfirmPurchaseHandler>().HandleAsync(new(owner.TenantId, purchase.Id, owner.UserId, PurchaseRevision.For(purchase.Id, lines)), Token);
        await using var db = fixture.CreateContext(owner.TenantId);
        var lot = await db.InventoryLots.SingleAsync(x => x.BusinessProductId == product.Id, Token);
        return new(owner, product.Id, category.Id, lot.Id);
    }
    private static async Task<string> Write(IServiceProvider source, Data data, string kind, CancellationToken token)
    {
        object result;
        if (kind == "supplier") result = await source.GetRequiredService<CreateSupplierHandler>().HandleAsync(new(data.Owner.TenantId, "Synthetic supplier", null, null, data.Owner.UserId), token);
        else if (kind == "adjustment") result = await source.GetRequiredService<AdjustStockHandler>().HandleAsync(new(data.Owner.TenantId, data.LotId, -1m, "Synthetic adjustment", data.Owner.UserId), token);
        else if (kind == "minimum") result = await source.GetRequiredService<SetBranchProductStockThresholdHandler>().HandleAsync(new(data.Owner.TenantId, data.Owner.BranchId, data.ProductId, 2m), token);
        else
        {
            result = await source.GetRequiredService<CreateSaleDraftHandler>().HandleAsync(new(data.Owner.TenantId, data.Owner.BranchId), token);
        }
        return JsonSerializer.Serialize(result, Json);
    }
    private sealed record Data(IdentityAccessTestSetup.Setup Owner, Guid ProductId, Guid CategoryId, Guid LotId);
}
