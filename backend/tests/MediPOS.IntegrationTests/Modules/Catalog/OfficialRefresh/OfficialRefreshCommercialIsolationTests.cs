using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Catalog.OfficialRefresh;
using MediPOS.Application.Modules.SalesPos.CreateSaleDraft;
using MediPOS.Application.Modules.SalesPos.ReplaceSaleLines;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Catalog.OfficialCuration;
using MediPOS.Domain.Modules.SalesPos;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.Cash;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.SalesPos;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static MediPOS.IntegrationTests.Modules.Catalog.OfficialCuration.OfficialCurationTestData;
using static MediPOS.IntegrationTests.Modules.Catalog.OfficialRefresh.OfficialRefreshTestData;

namespace MediPOS.IntegrationTests.Modules.Catalog.OfficialRefresh;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class OfficialRefreshCommercialIsolationTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task LinkedMedicineAlreadySoldKeepsTenantNamePricesStockAndFinancialHistoryAcrossSafeAndCriticalRefreshes()
    {
        await using var catalog = Services(fixture); await using var scope = catalog.CreateAsyncScope(); var source = scope.ServiceProvider;
        var original = await Original(fixture, source); var globalId = original.Publication!.GlobalProductId;
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var commercial = IdentityAccessTestSetup.CreateServices(fixture); await using var commerceScope = commercial.CreateAsyncScope(); var commerce = commerceScope.ServiceProvider;
        var cash = await CashSessionTestData.OpenAsync(commerce, tenant.Identity);
        var context = commerce.GetRequiredService<MediPosDbContext>(); context.SelectTenant(tenant.TenantId);
        var global = await context.GlobalProducts.Include(value => value.MedicineProfile).SingleAsync(value => value.Id == globalId, Token);
        var local = BusinessProduct.FromGlobal(tenant.TenantId, global, "REFRESH-" + Guid.NewGuid().ToString("N"), 2.125m, 1.75m, new DateTimeOffset(2026, 10, 6, 15, 0, 0, TimeSpan.Zero));
        var unit = ProductUnit.Create(tenant.TenantId, local.Id, tenant.TenantId, "Base", 1m, true);
        context.BusinessProducts.Add(local); context.ProductUnits.Add(unit); await context.SaveChangesAsync(Token);
        var draft = await commerce.GetRequiredService<CreateSaleDraftHandler>().HandleAsync(new(tenant.TenantId, tenant.Identity.BranchId), Token);
        var lines = await commerce.GetRequiredService<ReplaceSaleLinesHandler>().HandleAsync(new(tenant.TenantId, tenant.Identity.BranchId, draft.SaleId, draft.Version,
            [new(local.Id, unit.Id, 2m, PriceKind.Retail)]), Token);
        var lots = await SaleCheckoutTestData.ReceiveAsync(commerce, tenant, local.Id, unit.Id, [(10m, SaleCheckoutTestData.Today.AddDays(30))]);
        var checkout = new SaleCheckoutTestData.Rows(tenant, local.Id, unit.Id, cash, lines, lots);
        var confirmed = await SaleCheckoutTestData.ConfirmAsync(commerce, checkout);
        async Task<string> Snapshot()
        {
            await using var database = fixture.CreateContext(tenant.TenantId);
            var snapshot = new
            {
                products = await database.BusinessProducts.OrderBy(value => value.Id).ToArrayAsync(Token),
                units = await database.ProductUnits.OrderBy(value => value.Id).ToArrayAsync(Token),
                suppliers = await database.Suppliers.OrderBy(value => value.Id).ToArrayAsync(Token),
                lots = await database.InventoryLots.OrderBy(value => value.Id).ToArrayAsync(Token),
                movements = await database.StockMovements.OrderBy(value => value.Id).ToArrayAsync(Token),
                sales = await database.Sales.OrderBy(value => value.Id).ToArrayAsync(Token),
                lines = await database.SaleLines.OrderBy(value => value.Id).ToArrayAsync(Token),
                payments = await database.SalePayments.OrderBy(value => value.Id).ToArrayAsync(Token),
                cash = await database.CashSessions.OrderBy(value => value.Id).ToArrayAsync(Token),
                commissions = await database.CommissionEntries.OrderBy(value => value.Id).ToArrayAsync(Token),
                audits = await database.AuditLogs.OrderBy(value => value.Id).ToArrayAsync(Token),
                catalogVersions = await database.Database.SqlQuery<string>($"SELECT coalesce(jsonb_agg(to_jsonb(c) ORDER BY c.branch_id),'[]'::jsonb)::text AS \"Value\" FROM branch_catalog_sync_states c WHERE c.tenant_id={tenant.TenantId}").SingleAsync(Token),
                catalogChanges = await database.Database.SqlQuery<string>($"SELECT coalesce(jsonb_agg(to_jsonb(c) ORDER BY c.id),'[]'::jsonb)::text AS \"Value\" FROM branch_catalog_changes c WHERE c.tenant_id={tenant.TenantId}").SingleAsync(Token),
            };
            Assert.NotEmpty(snapshot.lots); Assert.NotEmpty(snapshot.movements); Assert.NotEmpty(snapshot.sales); Assert.NotEmpty(snapshot.payments);
            return JsonSerializer.Serialize(snapshot);
        }
        var before = await Snapshot(); var proposal = original.Curation.Proposal with { Name = "Nombre oficial corregido " + Guid.NewGuid().ToString("N") };
        var next = await StageVersion(source, original, [proposal]); var approved = await ApproveRefresh(source, await Prepare(source, original, next.Batch, next.Candidates[0]), proposal);
        var applied = await Apply(source, approved); Assert.Equal(globalId, applied.GlobalProductId);
        Assert.Equal(before, await Snapshot());
        var critical = proposal with { Components = [new(proposal.Components[0].Ingredient, "50 \u00b5g/mL")] };
        var third = await StageVersion(source, original, [critical]); var blocked = await Prepare(source, original, third.Batch, third.Candidates[0]);
        var corrected = await source.GetRequiredService<ReviewOfficialCatalogRefreshHandler>().HandleAsync(new(blocked.Id, blocked.Version, "save_correction", "New source strength is a critical difference.", critical), Token);
        Assert.Equal("clinically_significant_change", corrected.ChangeClassification);
        await Assert.ThrowsAsync<ApplicationErrorException>(() => source.GetRequiredService<ReviewOfficialCatalogRefreshHandler>().HandleAsync(new(corrected.Id, corrected.Version, "approve", "Clinical replacement is blocked."), Token));
        Assert.Equal(before, await Snapshot());
        await using var verification = fixture.CreateContext(tenant.TenantId); var unchangedLocal = await verification.BusinessProducts.SingleAsync(value => value.Id == local.Id, Token);
        Assert.Equal(globalId, unchangedLocal.GlobalProductId); Assert.Equal(original.Curation.Proposal.Name, unchangedLocal.Name);
        Assert.Equal(2.125m, unchangedLocal.RetailPrice); Assert.Equal(1.75m, unchangedLocal.WholesalePrice);
        Assert.Equal(SaleStatus.Confirmed, (await verification.Sales.SingleAsync(value => value.Id == confirmed.SaleId, Token)).Status);
        Assert.Equal(proposal.Name, (await verification.GlobalProducts.SingleAsync(value => value.Id == globalId, Token)).Name);
        Assert.Equal(original.Curation.Proposal.Name, Domain.Modules.Catalog.OfficialRefresh.OfficialCatalogProductSnapshot.Read((await History(source, approved.Id)).Entries[^1].PreviousSnapshotJson).Proposal.Name);
    }
}
