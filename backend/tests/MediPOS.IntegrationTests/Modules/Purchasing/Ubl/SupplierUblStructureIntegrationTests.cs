using System.Text;
using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Purchasing;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.TestFixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MediPOS.IntegrationTests.Modules.Purchasing.Ubl;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class SupplierUblStructureIntegrationTests(PostgreSqlFixture fixture)
{
    private static CancellationToken Token => SupplierUblTestData.Token;
    [Fact]
    public async Task MissingInvoiceTaxTotalIsAuditedWithoutPartialImportsOrCommercialEffectsAndIsTenantPrivate()
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var a = await SupplierUblTestData.Setup(source);
        await using var foreignScope = services.CreateAsyncScope();
        var b = await SupplierUblTestData.Setup(foreignScope.ServiceProvider);
        SupplierUblTestData.Authenticate(source, a.UserId);
        var xml = SupplierUblSyntheticXml.ChangeElement(SupplierUblTestData.Document(), "cac:TaxTotal", element => element.Remove());
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => SupplierUblTestData.Import(source, a, xml));
        Assert.Equal("ubl.required_element_missing", error.Error.Code); Assert.Null(error.InnerException);
        await using var context = fixture.CreateContext(a.TenantId);
        var audit = await context.AuditLogs.AsNoTracking().SingleAsync(v => v.Action == AuditAction.SupplierUblFileRejected, Token);
        Assert.Equal(a.TenantId, audit.TenantId); Assert.Equal(a.UserId, audit.ActorId);
        Assert.Equal(IdentityAccessTestSetup.Now, audit.OccurredAt); Assert.Equal(TimeSpan.Zero, audit.OccurredAt.Offset);
        Assert.Equal(32, audit.CorrelationId.Length); Assert.Null(audit.BeforeJson);
        using var after = JsonDocument.Parse(audit.AfterJson!);
        Assert.Equal(2, after.RootElement.EnumerateObject().Count());
        Assert.Equal(a.BranchId, after.RootElement.GetProperty("branchId").GetGuid());
        Assert.Equal("ubl.required_element_missing", after.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("Invoice", audit.AfterJson!, StringComparison.Ordinal);
        Assert.DoesNotContain(SupplierUblSyntheticXml.SupplierRuc, audit.AfterJson!, StringComparison.Ordinal);
        Assert.DoesNotContain(SupplierUblSyntheticXml.BuyerRuc, audit.AfterJson!, StringComparison.Ordinal);
        Assert.Empty(await context.SupplierUblImports.ToArrayAsync(Token));
        Assert.Empty(await context.SupplierUblImportLines.ToArrayAsync(Token));
        Assert.Empty(await context.SupplierUblPurchaseLineLinks.ToArrayAsync(Token));
        Assert.Empty(await context.Purchases.ToArrayAsync(Token)); Assert.Empty(await context.PurchaseLines.ToArrayAsync(Token));
        Assert.Empty(await context.InventoryLots.ToArrayAsync(Token)); Assert.Empty(await context.StockMovements.ToArrayAsync(Token));
        Assert.False(await context.AuditLogs.AnyAsync(v => v.EntityType == AuditEntityType.SupplierUblImport && v.Action != AuditAction.SupplierUblFileRejected, Token));
        await using var otherTenant = fixture.CreateContext(b.TenantId);
        Assert.False(await otherTenant.AuditLogs.AnyAsync(v => v.Id == audit.Id, Token));
        // Verify PostgreSQL RLS independently of EF query filters with the restricted runtime credential.
        await using var connection = new NpgsqlConnection(fixture.ConnectionString); await connection.OpenAsync(Token);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT set_config('medipos.tenant_id',@tenant,false)";
        command.Parameters.AddWithValue("tenant", b.TenantId.ToString("D")); await command.ExecuteNonQueryAsync(Token);
        command.CommandText = "SELECT count(*) FROM audit_logs WHERE id=@audit";
        command.Parameters.Clear(); command.Parameters.AddWithValue("audit", audit.Id);
        Assert.Equal(0L, await command.ExecuteScalarAsync(Token));
    }
    [Fact]
    public async Task ValidFileAfterStructuralRejectionStillReviewsAndConvertsOnceToDraft()
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var rows = await SupplierUblTestData.Setup(source);
        var valid = SupplierUblTestData.Document();
        var invalid = SupplierUblSyntheticXml.ChangeElement(valid, "cac:TaxTotal", element => element.Remove());
        await Assert.ThrowsAsync<ApplicationErrorException>(() => SupplierUblTestData.Import(source, rows, invalid));
        Assert.DoesNotContain(source.GetRequiredService<MediPOS.Infrastructure.Persistence.MediPosDbContext>().ChangeTracker.Entries(),
            v => v.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);
        var imported = await SupplierUblTestData.Import(source, rows, valid);
        var reviewed = await SupplierUblTestData.Review(source, rows, imported, [rows.ProductIds[0]]);
        var converted = await SupplierUblTestData.Convert(source, rows, reviewed);
        var retry = await SupplierUblTestData.Convert(source, rows, reviewed);
        Assert.Equal(converted.RelatedPurchaseId, retry.RelatedPurchaseId);
        await using var context = fixture.CreateContext(rows.TenantId);
        Assert.Equal(PurchaseStatus.Draft, (await context.Purchases.SingleAsync(Token)).Status);
        Assert.Equal(2, await context.PurchaseLines.CountAsync(Token)); Assert.Equal(2, await context.SupplierUblPurchaseLineLinks.CountAsync(Token));
        var document = await context.SupplierUblImports.SingleAsync(Token);
        Assert.Equal("converted_to_draft", document.Status); Assert.Equal(Encoding.UTF8.GetBytes(valid), document.SourceXml);
        Assert.Equal(1, await context.SupplierUblImportLines.CountAsync(Token));
        Assert.Equal(1, await context.AuditLogs.CountAsync(v => v.Action == AuditAction.SupplierUblFileRejected, Token));
        Assert.Equal(3, await context.AuditLogs.CountAsync(v => v.EntityId == imported.Id, Token));
        Assert.Empty(await context.InventoryLots.ToArrayAsync(Token)); Assert.Empty(await context.StockMovements.ToArrayAsync(Token));
    }
}
