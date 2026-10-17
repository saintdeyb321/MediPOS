using System.Security.Cryptography;
using System.Text;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.CreateMembership;
using MediPOS.Application.Modules.IdentityAccess.ReplaceWorkSchedule;
using MediPOS.Application.Modules.IdentityAccess.SetMembershipBranches;
using MediPOS.Application.Modules.Purchasing.ConfirmPurchase;
using MediPOS.Application.Modules.Purchasing.CreateSupplier;
using MediPOS.Application.Modules.Purchasing.Ubl;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.Purchasing;
using MediPOS.Domain.Modules.Purchasing.Ubl;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.TestFixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MediPOS.IntegrationTests.Modules.Purchasing.Ubl;

// Run only in the external PostgreSQL environment. No persistence substitutes are used.
[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class SupplierUblIntegrationTests(PostgreSqlFixture fixture)
{
    private static CancellationToken Token => SupplierUblTestData.Token;
    private static readonly string[] Tables = ["supplier_ubl_imports", "supplier_ubl_import_lines", "supplier_ubl_purchase_line_links"];

    [Fact]
    public async Task MigrationMatchesModelAndForcesRlsWithCompositeForeignKeys()
    {
        await using var context = fixture.CreateContext();
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Contains(await context.Database.GetAppliedMigrationsAsync(Token), v => v.EndsWith("_AddSupplierUblImports", StringComparison.Ordinal));
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(Token);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT NOT rolsuper AND NOT rolbypassrls FROM pg_roles WHERE rolname=current_user";
        Assert.Equal(true, await command.ExecuteScalarAsync(Token));
        command.CommandText = "SELECT count(*) FROM pg_class WHERE relname=ANY(@tables) AND relrowsecurity AND relforcerowsecurity";
        command.Parameters.AddWithValue("tables", Tables);
        Assert.Equal(3L, await command.ExecuteScalarAsync(Token));
        command.CommandText = "SELECT count(*) FROM pg_policies WHERE tablename=ANY(@tables) AND policyname='tenant_isolation' AND qual IS NOT NULL AND with_check IS NOT NULL";
        Assert.Equal(3L, await command.ExecuteScalarAsync(Token));
        command.CommandText = "SELECT count(*) FROM pg_constraint c JOIN pg_class t ON t.oid=c.conrelid WHERE t.relname=ANY(@tables) AND c.contype='f'";
        Assert.Equal(16L, await command.ExecuteScalarAsync(Token));
        command.CommandText = "SELECT count(*) FROM pg_constraint c JOIN pg_class t ON t.oid=c.conrelid WHERE t.relname=ANY(@tables) AND c.contype='f' AND cardinality(c.conkey)>1";
        Assert.Equal(15L, await command.ExecuteScalarAsync(Token));
    }

    [Fact]
    public async Task ImportPreservesExactBytesUnicodeEvidenceAndUnapprovedSuggestions()
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var rows = await SupplierUblTestData.Setup(source); var xml = SupplierUblTestData.Document();
        var imported = await SupplierUblTestData.Import(source, rows, xml);
        Assert.Equal("requires_review", imported.Status); Assert.False(imported.SupplierApproved);
        Assert.Equal(rows.SupplierId, imported.SupplierId); Assert.Null(imported.RelatedPurchaseId);
        await using var context = fixture.CreateContext(rows.TenantId);
        var document = await context.SupplierUblImports.AsNoTracking().SingleAsync(Token);
        Assert.Equal(Encoding.UTF8.GetBytes(xml), document.SourceXml);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(document.SourceXml)), document.XmlChecksumSha256);
        Assert.Contains("Ñandú", System.Text.Json.JsonSerializer.Deserialize<SupplierUblInvoiceEvidence>(imported.EvidenceJson)!.SupplierName, StringComparison.Ordinal);
        var line = await context.SupplierUblImportLines.AsNoTracking().SingleAsync(Token);
        Assert.Equal("buyer_code", line.SuggestionStatus); Assert.Equal(rows.ProductIds[0], line.SuggestedBusinessProductId);
        Assert.Null(line.ProductUnitId); Assert.Null(line.ApprovedUnitCost); Assert.Equal("requires_review", line.ReviewStatus);
        var audit = await context.AuditLogs.SingleAsync(v => v.EntityId == imported.Id, Token);
        Assert.Equal(AuditAction.SupplierUblImported, audit.Action); Assert.Equal(rows.UserId, audit.ActorId);
        Assert.DoesNotContain("Invoice", audit.AfterJson!, StringComparison.Ordinal);
        await AssertNoStock(context);
    }

    [Fact]
    public async Task HighRiskThreeMedicinesRequireHumanReviewAndProduceOneDraftWithoutStock()
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var rows = await SupplierUblTestData.Setup(source); var xml = SupplierUblTestData.Document(3);
        var imported = await SupplierUblTestData.Import(source, rows, xml);
        var page = await source.GetRequiredService<GetSupplierUblImportLinesHandler>().HandleAsync(new(rows.TenantId, rows.BranchId, imported.Id), Token);
        Assert.Equal(3, page.Lines.Count);
        Assert.Equal(rows.ProductIds[1], page.Lines[1].SuggestedBusinessProductId);
        Assert.Null(page.Lines[1].SuggestedProductUnitId); Assert.Equal("unmatched", page.Lines[2].SuggestionStatus);
        await Assert.ThrowsAsync<ApplicationErrorException>(() => SupplierUblTestData.Convert(source, rows, imported));
        await using var context = fixture.CreateContext(rows.TenantId);
        Assert.Empty(await context.Purchases.ToArrayAsync(Token)); await AssertNoStock(context);
        var third = await SupplierUblTestData.Product(source, rows.TenantId, rows.UserId, rows.CategoryId, "SKU-3");
        var products = rows.ProductIds.Append(third).ToArray();
        var beforeProducts = await context.BusinessProducts.AsNoTracking().OrderBy(v => v.Id).Select(v => new { v.Id, v.RetailPrice, v.WholesalePrice }).ToArrayAsync(Token);
        var beforeUnits = await context.ProductUnits.AsNoTracking().OrderBy(v => v.Id).Select(v => new { v.Id, v.Name, v.ConversionToBase }).ToArrayAsync(Token);
        var reviewed = await SupplierUblTestData.Review(source, rows, imported, products);
        var converted = await SupplierUblTestData.Convert(source, rows, reviewed);
        var retry = await SupplierUblTestData.Convert(source, rows, reviewed);
        var reimport = await SupplierUblTestData.Import(source, rows, xml);
        Assert.Equal(converted.RelatedPurchaseId, retry.RelatedPurchaseId); Assert.Equal(converted.Id, reimport.Id);
        var purchase = await context.Purchases.AsNoTracking().SingleAsync(Token);
        Assert.Equal(PurchaseStatus.Draft, purchase.Status); Assert.Null(purchase.ConfirmedAt);
        var receipts = await context.PurchaseLines.AsNoTracking().ToArrayAsync(Token);
        Assert.Equal(6, receipts.Length); Assert.All(receipts, v => { Assert.Equal(20m, v.BaseQuantity); Assert.Equal(2.125m, v.UnitCost); Assert.NotNull(v.ExpirationDate); });
        Assert.Equal(6, await context.SupplierUblPurchaseLineLinks.CountAsync(Token));
        Assert.Equal(beforeProducts, await context.BusinessProducts.AsNoTracking().OrderBy(v => v.Id).Select(v => new { v.Id, v.RetailPrice, v.WholesalePrice }).ToArrayAsync(Token));
        Assert.Equal(beforeUnits, await context.ProductUnits.AsNoTracking().OrderBy(v => v.Id).Select(v => new { v.Id, v.Name, v.ConversionToBase }).ToArrayAsync(Token));
        await AssertNoStock(context);
        Assert.Equal(3, await context.AuditLogs.CountAsync(v => v.EntityId == imported.Id, Token));
        Assert.Equal(1, await context.AuditLogs.CountAsync(v => v.EntityId == imported.Id && v.Action == AuditAction.SupplierUblConverted, Token));
        Assert.Equal(Encoding.UTF8.GetBytes(xml), (await context.SupplierUblImports.AsNoTracking().SingleAsync(Token)).SourceXml);
        // The existing commercial confirmation remains the only step that receives stock.
        await source.GetRequiredService<ConfirmPurchaseHandler>().HandleAsync(new(rows.TenantId, purchase.Id, rows.UserId), Token);
        Assert.Equal(6, await context.InventoryLots.CountAsync(Token)); Assert.Equal(6, await context.StockMovements.CountAsync(Token));
        Assert.Equal(PurchaseStatus.Confirmed, (await context.Purchases.AsNoTracking().SingleAsync(Token)).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IdenticalConcurrentRequestsReturnOneDocumentOrOneDraft(bool convert)
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var setup = services.CreateAsyncScope(); var source = setup.ServiceProvider;
        var rows = await SupplierUblTestData.Setup(source); var xml = SupplierUblTestData.Document();
        SupplierUblImportDetails? reviewed = null;
        if (convert) reviewed = await SupplierUblTestData.Review(source, rows, await SupplierUblTestData.Import(source, rows, xml), [rows.ProductIds[0]]);
        async Task<SupplierUblImportDetails> Run()
        {
            await using var scope = services.CreateAsyncScope(); SupplierUblTestData.Authenticate(scope.ServiceProvider, rows.UserId);
            return convert ? await SupplierUblTestData.Convert(scope.ServiceProvider, rows, reviewed!) : await SupplierUblTestData.Import(scope.ServiceProvider, rows, xml);
        }
        var results = await Task.WhenAll(Run(), Run()); Assert.Equal(results[0].Id, results[1].Id);
        Assert.Equal(results[0].RelatedPurchaseId, results[1].RelatedPurchaseId);
        await using var context = fixture.CreateContext(rows.TenantId);
        Assert.Equal(1, await context.SupplierUblImports.CountAsync(Token)); Assert.Equal(1, await context.SupplierUblImportLines.CountAsync(Token));
        Assert.Equal(convert ? 1 : 0, await context.Purchases.CountAsync(Token));
        Assert.Equal(convert ? 3 : 1, await context.AuditLogs.CountAsync(v => v.EntityId == results[0].Id, Token)); await AssertNoStock(context);
    }

    [Fact]
    public async Task ChangedContentConflictsAndDoesNotOverwriteOriginal()
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider; var rows = await SupplierUblTestData.Setup(source);
        var xml = SupplierUblTestData.Document(); var original = await SupplierUblTestData.Import(source, rows, xml);
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => SupplierUblTestData.Import(source, rows, xml.Replace("Distribuidora Ñandú", "Otra razón social", StringComparison.Ordinal)));
        Assert.Equal("ubl.document_content_conflict", error.Error.Code);
        await using var context = fixture.CreateContext(rows.TenantId);
        Assert.Equal(original.XmlChecksumSha256, (await context.SupplierUblImports.SingleAsync(Token)).XmlChecksumSha256);
        Assert.Equal(1, await context.AuditLogs.CountAsync(v => v.EntityId == original.Id, Token));
    }

    [Theory]
    [InlineData("USD")]
    [InlineData("total")]
    public async Task DocumentaryIssuesAreStoredButPreventDraftConversion(string issue)
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider; var rows = await SupplierUblTestData.Setup(source);
        var xml = SupplierUblTestData.Document();
        xml = issue == "USD" ? xml.Replace("PEN", "USD", StringComparison.Ordinal) : xml.Replace(">23.60</cbc:PayableAmount>", ">99.00</cbc:PayableAmount>", StringComparison.Ordinal);
        var imported = await SupplierUblTestData.Import(source, rows, xml); Assert.NotEqual("[]", imported.BlockingIssuesJson);
        await Assert.ThrowsAsync<ApplicationErrorException>(() => SupplierUblTestData.Review(source, rows, imported, [rows.ProductIds[0]]));
        await Assert.ThrowsAsync<ApplicationErrorException>(() => SupplierUblTestData.Convert(source, rows, imported));
        await using var context = fixture.CreateContext(rows.TenantId); Assert.Empty(await context.Purchases.ToArrayAsync(Token)); await AssertNoStock(context);
    }

    [Fact]
    public async Task OtherLegalReceiverIsRejectedWithSanitizedAudit()
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider; var rows = await SupplierUblTestData.Setup(source);
        var xml = SupplierUblTestData.Document().Replace(SupplierUblSyntheticXml.BuyerRuc, "20888888888", StringComparison.Ordinal);
        Assert.Equal("ubl.buyer_mismatch", (await Assert.ThrowsAsync<ApplicationErrorException>(() => SupplierUblTestData.Import(source, rows, xml))).Error.Code);
        await using var context = fixture.CreateContext(rows.TenantId);
        Assert.Empty(await context.SupplierUblImports.ToArrayAsync(Token));
        var audit = await context.AuditLogs.SingleAsync(v => v.Action == AuditAction.SupplierUblFileRejected, Token);
        Assert.DoesNotContain("20888888888", audit.AfterJson!, StringComparison.Ordinal); Assert.DoesNotContain("Invoice", audit.AfterJson!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("foreign_product")]
    [InlineData("foreign_supplier")]
    [InlineData("wrong_supplier_ruc")]
    [InlineData("wrong_unit")]
    [InlineData("no_batch")]
    [InlineData("no_expiry")]
    [InlineData("presentation_unconfirmed")]
    public async Task InvalidReviewCannotPersistApprovalOrDraft(string defect)
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var own = await SupplierUblTestData.Setup(source);
        await using var foreignScope = services.CreateAsyncScope();
        var foreign = await SupplierUblTestData.Setup(foreignScope.ServiceProvider);
        SupplierUblTestData.Authenticate(source, own.UserId);
        var imported = await SupplierUblTestData.Import(source, own, SupplierUblTestData.Document());
        await using var context = fixture.CreateContext(own.TenantId);
        var line = await context.SupplierUblImportLines.AsNoTracking().SingleAsync(Token);
        var unit = await context.ProductUnits.AsNoTracking().SingleAsync(v => v.BusinessProductId == own.ProductIds[defect == "wrong_unit" ? 1 : 0] && v.Name == "Caja 20", Token);
        var supplier = own.SupplierId;
        if (defect == "foreign_supplier") supplier = foreign.SupplierId;
        if (defect == "wrong_supplier_ruc") supplier = (await source.GetRequiredService<CreateSupplierHandler>().HandleAsync(new(own.TenantId, "Mismo nombre", "20111111111", null, own.UserId), Token)).Id;
        ReviewSupplierUblLineInput input = new(line.Id, true, defect == "foreign_product" ? foreign.ProductIds[0] : own.ProductIds[0], unit.Id,
            2m, 2.125m, defect != "presentation_unconfirmed", "Explicit physical receipt review",
            [new(2m, defect == "no_batch" ? null : "LOT", defect == "no_expiry" ? null : new(2030, 1, 1))]);
        await Assert.ThrowsAsync<ApplicationErrorException>(() => source.GetRequiredService<ReviewSupplierUblImportHandler>().HandleAsync(new(own.TenantId, own.BranchId, imported.Id, 1, supplier, "complete_review", "Review", [input]), Token));
        var persisted = await context.SupplierUblImports.AsNoTracking().SingleAsync(Token);
        Assert.Equal(1, persisted.Version); Assert.False(persisted.SupplierApproved);
        Assert.Equal("requires_review", (await context.SupplierUblImportLines.AsNoTracking().SingleAsync(Token)).ReviewStatus);
        Assert.Empty(await context.Purchases.ToArrayAsync(Token)); await AssertNoStock(context);
    }

    [Fact]
    public async Task ApprovedPriorMappingIsOnlyASuggestionAndConflictingCodesAreAmbiguous()
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider; var rows = await SupplierUblTestData.Setup(source);
        var imported = await SupplierUblTestData.Import(source, rows, SupplierUblTestData.Document());
        await SupplierUblTestData.Review(source, rows, imported, [rows.ProductIds[0]]);
        var next = await SupplierUblTestData.Import(source, rows, SupplierUblTestData.Document());
        await using var context = fixture.CreateContext(rows.TenantId);
        var suggestion = await context.SupplierUblImportLines.AsNoTracking().SingleAsync(v => v.ImportId == next.Id, Token);
        Assert.Equal("previous_mapping", suggestion.SuggestionStatus); Assert.NotNull(suggestion.SuggestedProductUnitId);
        Assert.Null(suggestion.BusinessProductId); Assert.Equal("requires_review", suggestion.ReviewStatus);
        await SupplierUblTestData.Review(source, rows, next, [rows.ProductIds[1]]);
        var conflict = await SupplierUblTestData.Import(source, rows, SupplierUblTestData.Document());
        Assert.Equal("ambiguous", (await context.SupplierUblImportLines.AsNoTracking().SingleAsync(v => v.ImportId == conflict.Id, Token)).SuggestionStatus);
    }

    [Fact]
    public async Task RejectedImportKeepsEvidenceAndRetryCannotReopenIt()
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider; var rows = await SupplierUblTestData.Setup(source);
        var xml = SupplierUblTestData.Document(); var imported = await SupplierUblTestData.Import(source, rows, xml);
        var rejected = await source.GetRequiredService<ReviewSupplierUblImportHandler>().HandleAsync(new(rows.TenantId, rows.BranchId, imported.Id, 1, null, "reject", "Supplier documentary evidence rejected by reviewer", []), Token);
        Assert.Equal("rejected", rejected.Status); Assert.Equal("rejected", (await SupplierUblTestData.Import(source, rows, xml)).Status);
        await Assert.ThrowsAsync<ApplicationErrorException>(() => SupplierUblTestData.Convert(source, rows, rejected));
        await using var context = fixture.CreateContext(rows.TenantId);
        Assert.Equal(Encoding.UTF8.GetBytes(xml), (await context.SupplierUblImports.AsNoTracking().SingleAsync(Token)).SourceXml);
        Assert.Equal(2, await context.AuditLogs.CountAsync(v => v.EntityId == imported.Id, Token)); await AssertNoStock(context);
    }

    [Theory]
    [InlineData("<!DOCTYPE Invoice [<!ENTITY xxe SYSTEM 'file:///sensitive.txt'>]>")]
    [InlineData("<!DOCTYPE Invoice [<!ENTITY a 'expanded'><!ENTITY b '&a;&a;&a;&a;'>]>")]
    public async Task MaliciousXmlCreatesOnlyFileRejectionAudit(string doctype)
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider; var rows = await SupplierUblTestData.Setup(source);
        var xml = SupplierUblTestData.Document().Replace("<Invoice ", doctype + "<Invoice ", StringComparison.Ordinal);
        Assert.Equal("ubl.xml_invalid_or_unsafe", (await Assert.ThrowsAsync<ApplicationErrorException>(() => SupplierUblTestData.Import(source, rows, xml))).Error.Code);
        await using var context = fixture.CreateContext(rows.TenantId);
        Assert.Empty(await context.SupplierUblImports.ToArrayAsync(Token)); Assert.Empty(await context.SupplierUblImportLines.ToArrayAsync(Token));
        var audit = await context.AuditLogs.SingleAsync(v => v.Action == AuditAction.SupplierUblFileRejected, Token);
        Assert.DoesNotContain("DOCTYPE", audit.AfterJson!, StringComparison.Ordinal); await AssertNoStock(context);
    }

    [Theory]
    [InlineData("foreign_product")]
    [InlineData("wrong_unit")]
    public async Task CompositeForeignKeysRejectDirectSqlMappings(string defect)
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var a = await SupplierUblTestData.Setup(source);
        await using var foreignScope = services.CreateAsyncScope();
        var b = await SupplierUblTestData.Setup(foreignScope.ServiceProvider);
        SupplierUblTestData.Authenticate(source, a.UserId);
        var imported = await SupplierUblTestData.Import(source, a, SupplierUblTestData.Document());
        await using var own = fixture.CreateContext(a.TenantId); await using var foreign = fixture.CreateContext(b.TenantId);
        var unit = defect == "foreign_product" ? await foreign.ProductUnits.SingleAsync(v => v.BusinessProductId == b.ProductIds[0] && v.IsBaseUnit, Token) :
            await own.ProductUnits.SingleAsync(v => v.BusinessProductId == a.ProductIds[1] && v.IsBaseUnit, Token);
        var product = defect == "foreign_product" ? b.ProductIds[0] : a.ProductIds[0];
        await using var connection = new NpgsqlConnection(fixture.ConnectionString); await connection.OpenAsync(Token); await SetTenant(connection, a.TenantId);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE supplier_ubl_import_lines SET business_product_id=@product, product_unit_id=@unit WHERE import_id=@import";
        command.Parameters.AddWithValue("product", product); command.Parameters.AddWithValue("unit", unit.Id); command.Parameters.AddWithValue("import", imported.Id);
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, (await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(Token))).SqlState);
    }

    [Fact]
    public async Task TenantQueriesAndPooledConnectionsHideAllForeignIdentifiers()
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var a = await SupplierUblTestData.Setup(source);
        await using var foreignScope = services.CreateAsyncScope();
        var otherSource = foreignScope.ServiceProvider;
        var b = await SupplierUblTestData.Setup(otherSource);
        SupplierUblTestData.Authenticate(source, a.UserId);
        var imported = await SupplierUblTestData.Import(source, a, SupplierUblTestData.Document());
        var converted = await SupplierUblTestData.Convert(source, a, await SupplierUblTestData.Review(source, a, imported, [a.ProductIds[0]]));
        SupplierUblTestData.Authenticate(otherSource, b.UserId);
        Assert.Equal("ubl.import_not_found", (await Assert.ThrowsAsync<ApplicationErrorException>(() => otherSource.GetRequiredService<GetSupplierUblImportHandler>().HandleAsync(new(b.TenantId, b.BranchId, imported.Id), Token))).Error.Code);
        Assert.Empty((await otherSource.GetRequiredService<SearchSupplierUblImportsHandler>().HandleAsync(new(b.TenantId, b.BranchId, Document: imported.OriginalDocumentId), Token)).Imports);
        var pooled = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { MaxPoolSize = 1, ApplicationName = "ubl-isolation-" + Guid.NewGuid().ToString("N") }.ConnectionString;
        for (var n = 0; n < 3; n++)
        {
            await using (var connection = new NpgsqlConnection(pooled))
            {
                await connection.OpenAsync(Token); await SetTenant(connection, a.TenantId);
                Assert.Equal(1L, await CountForeign(connection, converted));
            }
            await using (var connection = new NpgsqlConnection(pooled))
            {
                await connection.OpenAsync(Token); Assert.Equal(0L, await CountForeign(connection, converted));
                await SetTenant(connection, b.TenantId); Assert.Equal(0L, await CountForeign(connection, converted));
                await using var command = connection.CreateCommand(); command.CommandText = "SELECT count(*) FROM supplier_ubl_import_lines WHERE import_id=@id"; command.Parameters.AddWithValue("id", imported.Id);
                Assert.Equal(0L, await command.ExecuteScalarAsync(Token)); command.CommandText = "SELECT count(*) FROM supplier_ubl_purchase_line_links WHERE import_id=@id";
                Assert.Equal(0L, await command.ExecuteScalarAsync(Token));
            }
        }
        await using var foreign = fixture.CreateContext(b.TenantId);
        Assert.False(await foreign.Purchases.AnyAsync(v => v.Id == converted.RelatedPurchaseId, Token));
    }

    [Theory]
    [InlineData("original_document")]
    [InlineData("original_line")]
    [InlineData("unaudited_review")]
    [InlineData("false_audit")]
    [InlineData("delete")]
    public async Task DatabaseRejectsTamperingAndFalseAuditTransitions(string attempt)
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider; var rows = await SupplierUblTestData.Setup(source);
        var imported = await SupplierUblTestData.Import(source, rows, SupplierUblTestData.Document());
        await using var connection = new NpgsqlConnection(fixture.ConnectionString); await connection.OpenAsync(Token); await SetTenant(connection, rows.TenantId);
        await using var command = connection.CreateCommand(); command.Parameters.AddWithValue("id", imported.Id);
        command.CommandText = attempt switch
        {
            "original_document" => "UPDATE supplier_ubl_imports SET declared_total=declared_total+1, version=version+1 WHERE id=@id",
            "original_line" => "UPDATE supplier_ubl_import_lines SET documentary_quantity=documentary_quantity+1 WHERE import_id=@id",
            "unaudited_review" => "UPDATE supplier_ubl_import_lines SET review_notes='Unaudited administrative change' WHERE import_id=@id",
            "false_audit" => "INSERT INTO audit_logs (id,tenant_id,actor_id,action,entity_type,entity_id,occurred_at,correlation_id,before_json,after_json) SELECT gen_random_uuid(), tenant_id, actor_id, 'supplier_ubl.reviewed', entity_type, entity_id, occurred_at, correlation_id, before_json, '{\"version\":2,\"status\":\"requires_review\"}'::jsonb FROM audit_logs WHERE entity_id=@id",
            _ => "DELETE FROM supplier_ubl_imports WHERE id=@id",
        };
        var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(Token));
        Assert.Contains(error.SqlState, new[] { PostgresErrorCodes.CheckViolation, PostgresErrorCodes.InsufficientPrivilege });
        await using var context = fixture.CreateContext(rows.TenantId);
        Assert.Equal(1, (await context.SupplierUblImports.SingleAsync(Token)).Version);
        Assert.Equal(1, await context.AuditLogs.CountAsync(v => v.EntityId == imported.Id, Token));
    }

    [Theory]
    [InlineData("audit")]
    [InlineData("commit")]
    public async Task DatabaseFailureRollsBackDraftProvenanceAndAuditAndPermitsRetry(string failure)
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider; var rows = await SupplierUblTestData.Setup(source);
        var imported = await SupplierUblTestData.Import(source, rows, SupplierUblTestData.Document());
        var reviewed = await SupplierUblTestData.Review(source, rows, imported, [rows.ProductIds[0]]);
        await using var administrator = fixture.CreateConstraintContext();
        var target = reviewed.Id.ToString("D");
        var table = failure == "audit" ? "audit_logs" : "supplier_ubl_purchase_line_links";
        var condition = failure == "audit" ? $"NEW.entity_id='{target}'::uuid AND NEW.action='supplier_ubl.converted'" : $"NEW.import_id='{target}'::uuid";
        await ExecuteDdl(administrator, $"CREATE FUNCTION ubl_test_failure() RETURNS trigger LANGUAGE plpgsql AS $fail$ BEGIN IF {condition} THEN RAISE EXCEPTION 'Injected database failure' USING ERRCODE='23514'; END IF; RETURN NEW; END $fail$", Token);
        try
        {
            var trigger = failure == "audit" ? $"CREATE TRIGGER ubl_test_failure BEFORE INSERT ON {table} FOR EACH ROW EXECUTE FUNCTION ubl_test_failure()" :
                $"CREATE CONSTRAINT TRIGGER ubl_test_failure AFTER INSERT ON {table} DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION ubl_test_failure()";
            await administrator.Database.ExecuteSqlRawAsync(trigger, Token);
            var error = await Assert.ThrowsAnyAsync<Exception>(() => SupplierUblTestData.Convert(source, rows, reviewed));
            var postgres = error as PostgresException ?? (error as DbUpdateException)?.InnerException as PostgresException;
            Assert.NotNull(postgres); Assert.Equal(PostgresErrorCodes.CheckViolation, postgres.SqlState);
            Assert.Empty(source.GetRequiredService<MediPosDbContext>().ChangeTracker.Entries());
            await using var context = fixture.CreateContext(rows.TenantId);
            Assert.Equal("reviewed", (await context.SupplierUblImports.SingleAsync(Token)).Status);
            Assert.Empty(await context.Purchases.ToArrayAsync(Token)); Assert.Empty(await context.PurchaseLines.ToArrayAsync(Token));
            Assert.Empty(await context.SupplierUblPurchaseLineLinks.ToArrayAsync(Token));
            Assert.Equal(2, await context.AuditLogs.CountAsync(v => v.EntityId == reviewed.Id, Token)); await AssertNoStock(context);
        }
        finally
        {
            await ExecuteDdl(administrator, $"DROP TRIGGER IF EXISTS ubl_test_failure ON {table}; DROP FUNCTION ubl_test_failure()", Token);
        }
        var retry = await SupplierUblTestData.Convert(source, rows, reviewed); Assert.Equal("converted_to_draft", retry.Status);
    }

    [Theory]
    [InlineData(TenantRole.Owner, true, true, true)]
    [InlineData(TenantRole.Pharmacist, true, true, true)]
    [InlineData(TenantRole.Pharmacist, false, true, false)]
    [InlineData(TenantRole.Pharmacist, true, false, false)]
    [InlineData(TenantRole.Cashier, true, true, false)]
    public async Task ServerSessionRoleBranchAndScheduleAreEnforced(TenantRole role, bool assigned, bool scheduled, bool allowed)
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider; var rows = await SupplierUblTestData.Setup(source);
        var user = await IdentityAccessTestSetup.CreateUserAsync(source);
        var membership = await source.GetRequiredService<CreateMembershipHandler>().HandleAsync(new(rows.TenantId, user.Id, role, rows.UserId), Token);
        if (assigned) await source.GetRequiredService<SetMembershipBranchesHandler>().HandleAsync(new(rows.TenantId, membership.Id, [rows.BranchId], rows.UserId), Token);
        await source.GetRequiredService<ReplaceWorkScheduleHandler>().HandleAsync(new(rows.TenantId, membership.Id,
            [new(DayOfWeek.Tuesday, scheduled ? new(8, 0) : new(10, 0), new(18, 0))], rows.UserId), Token);
        SupplierUblTestData.Authenticate(source, user.Id);
        if (allowed)
        {
            var imported = await SupplierUblTestData.Import(source, rows, SupplierUblTestData.Document()); Assert.Equal(membership.Id, imported.ImportedByMembershipId);
        }
        else Assert.Equal(ErrorCategory.Forbidden, (await Assert.ThrowsAsync<ApplicationErrorException>(() => SupplierUblTestData.Import(source, rows, SupplierUblTestData.Document()))).Error.Category);
    }

    [Fact]
    public async Task SearchFiltersAndLinePagesAreBoundedAndExcludeRawXml()
    {
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider; var rows = await SupplierUblTestData.Setup(source);
        var first = await SupplierUblTestData.Import(source, rows, SupplierUblTestData.Document(3));
        await SupplierUblTestData.Import(source, rows, SupplierUblTestData.Document());
        var page = await source.GetRequiredService<SearchSupplierUblImportsHandler>().HandleAsync(new(rows.TenantId, rows.BranchId, rows.SupplierId,
            new(2026, 10, 12), new(2026, 10, 12), "requires_review", first.OriginalDocumentId, Limit: 1), Token);
        Assert.Equal(first.Id, Assert.Single(page.Imports).Id); Assert.False(page.HasMore);
        Assert.DoesNotContain("<Invoice", System.Text.Json.JsonSerializer.Serialize(page), StringComparison.Ordinal);
        var lines = await source.GetRequiredService<GetSupplierUblImportLinesHandler>().HandleAsync(new(rows.TenantId, rows.BranchId, first.Id, 1, 1), Token);
        Assert.Equal(2, Assert.Single(lines.Lines).LineNumber); Assert.True(lines.HasMore);
        await Assert.ThrowsAsync<ApplicationErrorException>(() => source.GetRequiredService<SearchSupplierUblImportsHandler>().HandleAsync(new(rows.TenantId, rows.BranchId, Limit: 101), Token));
    }

    // DDL identifiers come from closed test literals; the target is a generated Guid, never user text.
    private static Task<int> ExecuteDdl(MediPosDbContext context, string sql, CancellationToken token) => context.Database.ExecuteSqlRawAsync(sql, token);
    private static async Task AssertNoStock(MediPosDbContext context)
    {
        Assert.Empty(await context.InventoryLots.AsNoTracking().ToArrayAsync(Token));
        Assert.Empty(await context.StockMovements.AsNoTracking().ToArrayAsync(Token));
        Assert.False(await context.Purchases.AsNoTracking().AnyAsync(v => v.Status == PurchaseStatus.Confirmed, Token));
    }
    private static async Task SetTenant(NpgsqlConnection connection, Guid tenant)
    {
        await using var command = connection.CreateCommand(); command.CommandText = "SELECT set_config('medipos.tenant_id',@tenant,false)";
        command.Parameters.AddWithValue("tenant", tenant.ToString("D")); await command.ExecuteNonQueryAsync(Token);
    }
    private static async Task<long> CountForeign(NpgsqlConnection connection, SupplierUblImportDetails document)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM supplier_ubl_imports WHERE id=@id OR supplier_ruc=@ruc OR xml_checksum_sha256=@checksum OR related_purchase_id=@purchase OR original_document_id=@document";
        command.Parameters.AddWithValue("id", document.Id); command.Parameters.AddWithValue("ruc", document.SupplierRuc);
        command.Parameters.AddWithValue("checksum", document.XmlChecksumSha256); command.Parameters.AddWithValue("purchase", document.RelatedPurchaseId!.Value);
        command.Parameters.AddWithValue("document", document.OriginalDocumentId); return (long)(await command.ExecuteScalarAsync(Token))!;
    }
}
