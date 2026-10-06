using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Branches.CreateBranch;
using MediPOS.Application.Modules.Catalog.SearchProducts;
using MediPOS.Application.Modules.Catalog.SetBusinessProductStatus;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.IntegrationTests.Modules.Catalog;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class ProductSearchIntegrationTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task BarcodeCodeExactNamePrefixFuzzyIngredientAndBrandHaveStablePriorityWithRepresentativeCatalog()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        await ProductSearchTestData.AddRepresentativeCatalogAsync(source, tenant);
        var barcode = await ProductSearchTestData.CreateAsync(source, tenant, "Zeta", barcode: "MATCH");
        var code = await ProductSearchTestData.CreateAsync(source, tenant, "Omega", code: "MATCH");
        var exact = await ProductSearchTestData.CreateAsync(source, tenant, "match");
        var tiedExact = await ProductSearchTestData.CreateAsync(source, tenant, "MATCH");
        var prefix = await ProductSearchTestData.CreateAsync(source, tenant, "match adicional");
        var fuzzy = await ProductSearchTestData.CreateAsync(source, tenant, "matsh");
        var ingredient = await ProductSearchTestData.CreateAsync(source, tenant, "Comercial",
            medicine: ProductSearchTestData.Medicine(ingredient: "match"));
        var brand = await ProductSearchTestData.CreateAsync(source, tenant, "Nombre ajeno", brand: "match");
        var inactive = await ProductSearchTestData.CreateAsync(source, tenant, "Inactivo", barcode: "match", isActive: false);

        var result = await ProductSearchTestData.SearchAsync(source, tenant, "  MÁtch  ");
        Guid[] expected = [barcode.Id, code.Id, .. new[] { exact.Id, tiedExact.Id }.Order(), prefix.Id, fuzzy.Id, ingredient.Id, brand.Id];
        Assert.Equal(expected, result.DirectMatches.Select(value => value.BusinessProductId));
        Assert.DoesNotContain(result.DirectMatches, value => value.BusinessProductId == inactive.Id);
        Assert.All(result.DirectMatches, value =>
        {
            Assert.Equal(2.5m, value.RetailPrice);
            Assert.Equal(2m, value.WholesalePrice);
            Assert.Equal(0m, value.QuantityAvailableBase);
            Assert.Equal(2, value.Units.Count);
            Assert.All(value.Units, unit => Assert.True(unit.IsActive));
        });
        Assert.Equal(expected, (await ProductSearchTestData.SearchAsync(source, tenant, "match"))
            .DirectMatches.Select(value => value.BusinessProductId));
        Assert.Equal(expected.Take(3), (await ProductSearchTestData.SearchAsync(source, tenant, "match", 3))
            .DirectMatches.Select(value => value.BusinessProductId));
        Assert.Equal(7, (await ProductSearchTestData.SearchAsync(source, tenant, "Articulo de higiene", 7)).DirectMatches.Count);
        Assert.Equal(50, (await ProductSearchTestData.SearchAsync(source, tenant, "Articulo de higiene")).DirectMatches.Count);
        Assert.Empty(result.EquivalentInBranch);
        Assert.Empty(result.OtherBranchAvailability);
    }

    [Theory]
    [InlineData("paracitamol", "Paracetamol")]
    [InlineData("acido folico", "Ácido Fólico")]
    [InlineData("ÁCIDO FÓLICO", "Ácido Fólico")]
    [InlineData("A\u0301CIDO\u00A0\u2003FO\u0301LICO", "Ácido Fólico")]
    [InlineData("paracetamol", "Marca comercial")]
    [InlineData("laboratorio unico", "Marca comercial")]
    [InlineData("laboratorío único", "Marca comercial")]
    public async Task PostgreSqlFindsTypoAccentsIngredientsAndLaboratory(string query, string expectedName)
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        await ProductSearchTestData.CreateAsync(source, tenant, "Paracetamol", medicine: ProductSearchTestData.Medicine());
        await ProductSearchTestData.CreateAsync(source, tenant, "Ácido Fólico", brand: "Marca");
        await ProductSearchTestData.CreateAsync(source, tenant, "Marca comercial",
            medicine: ProductSearchTestData.Medicine(), brand: "Laboratorio Único");
        var result = await ProductSearchTestData.SearchAsync(source, tenant, query);
        Assert.Contains(result.DirectMatches, value => value.Name == expectedName);
    }

    [Fact]
    public async Task ShortQueriesUseLiteralExactOrPrefixAndEscapedWildcardsCannotExpandTheCatalog()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var prefix = await ProductSearchTestData.CreateAsync(source, tenant, "Paracetamol", medicine: ProductSearchTestData.Medicine());
        await ProductSearchTestData.CreateAsync(source, tenant, "Prueba", brand: "Otra");
        var percent = await ProductSearchTestData.CreateAsync(source, tenant, "% oferta", brand: "Otra");
        var underscore = await ProductSearchTestData.CreateAsync(source, tenant, "_ unidad", brand: "Otra");
        Assert.Equal(prefix.Id, Assert.Single((await ProductSearchTestData.SearchAsync(source, tenant, "pa")).DirectMatches).BusinessProductId);
        Assert.Empty((await ProductSearchTestData.SearchAsync(source, tenant, "px")).DirectMatches);
        Assert.Equal(percent.Id, Assert.Single((await ProductSearchTestData.SearchAsync(source, tenant, "%")).DirectMatches).BusinessProductId);
        Assert.Equal(underscore.Id, Assert.Single((await ProductSearchTestData.SearchAsync(source, tenant, "_")).DirectMatches).BusinessProductId);
        Assert.Empty((await ProductSearchTestData.SearchAsync(source, tenant, "'; SELECT * FROM business_products; --")).DirectMatches);
    }

    [Fact]
    public async Task SameKeyAvailableLocallyIsSeparateDeduplicatedAndSuppressesOtherBranchesWhileExpiredLotsDoNotCount()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var barcode = Guid.NewGuid().ToString("N");
        var origin = await ProductSearchTestData.CreateAsync(source, tenant, "Solicitado", barcode: barcode,
            medicine: ProductSearchTestData.Medicine());
        var local = await ProductSearchTestData.CreateAsync(source, tenant, "Otra marca", medicine: ProductSearchTestData.Medicine(), brand: "Otro laboratorio");
        var empty = await ProductSearchTestData.CreateAsync(source, tenant, "Sin saldo", medicine: ProductSearchTestData.Medicine());
        var expired = await ProductSearchTestData.CreateAsync(source, tenant, "Vencido", medicine: ProductSearchTestData.Medicine());
        var strength = await ProductSearchTestData.CreateAsync(source, tenant, "Otra dosis", medicine: ProductSearchTestData.Medicine("250 mg"));
        var form = await ProductSearchTestData.CreateAsync(source, tenant, "Otra forma", medicine: ProductSearchTestData.Medicine(form: "Cápsula"));
        var route = await ProductSearchTestData.CreateAsync(source, tenant, "Otra vía", medicine: ProductSearchTestData.Medicine(route: "Tópica"));
        var inactive = await ProductSearchTestData.CreateAsync(source, tenant, "Inactivo", medicine: ProductSearchTestData.Medicine());
        await ProductSearchTestData.ReceiveAsync(source, tenant, tenant.Identity.BranchId,
            new(origin, 9m, ProductSearchTestData.Today.AddDays(-1)), new(local, 3m, ProductSearchTestData.Today),
            new(local, 8m, ProductSearchTestData.Today.AddDays(-1)), new(expired, 4m, ProductSearchTestData.Today.AddDays(-1)),
            new(strength, 2m, ProductSearchTestData.Today.AddDays(1)), new(form, 2m, ProductSearchTestData.Today.AddDays(1)),
            new(route, 2m, ProductSearchTestData.Today.AddDays(1)), new(inactive, 2m, ProductSearchTestData.Today.AddDays(1)));
        await source.GetRequiredService<SetBusinessProductStatusHandler>().HandleAsync(
            new(tenant.TenantId, inactive.Id, false, tenant.Identity.ActorId), TestContext.Current.CancellationToken);
        await ProductSearchTestData.ReceiveAsync(source, tenant, tenant.SpareBranchId,
            new ProductSearchTestData.Receipt(origin, 20m, ProductSearchTestData.Today.AddDays(1)));
        var context = source.GetRequiredService<MediPosDbContext>();
        var beforeAudit = await context.AuditLogs.CountAsync(TestContext.Current.CancellationToken);
        var beforeMovements = await context.StockMovements.CountAsync(TestContext.Current.CancellationToken);
        var beforeLots = await context.InventoryLots.AsNoTracking().OrderBy(value => value.Id)
            .Select(value => new { value.Id, value.QuantityAvailableBase }).ToListAsync(TestContext.Current.CancellationToken);

        var result = await ProductSearchTestData.SearchAsync(source, tenant, barcode, 1);
        var direct = Assert.Single(result.DirectMatches);
        Assert.Equal(origin.Id, direct.BusinessProductId);
        Assert.Equal(0m, direct.QuantityAvailableBase);
        var equivalent = Assert.Single(result.EquivalentInBranch);
        Assert.Equal(local.Id, equivalent.Product.BusinessProductId);
        Assert.Equal(3m, equivalent.Product.QuantityAvailableBase);
        Assert.Equal(origin.Id, Assert.Single(equivalent.ForDirectProducts));
        Assert.Equal("mismo principio activo/concentración/forma", equivalent.Description);
        Assert.Empty(result.OtherBranchAvailability);
        Assert.DoesNotContain(result.EquivalentInBranch, value =>
            new[] { origin.Id, empty.Id, expired.Id, strength.Id, form.Id, route.Id, inactive.Id }.Contains(value.Product.BusinessProductId));
        Assert.Empty(result.DirectMatches.Select(value => value.BusinessProductId)
            .Intersect(result.EquivalentInBranch.Select(value => value.Product.BusinessProductId)));
        Assert.Equal(beforeAudit, await context.AuditLogs.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(beforeMovements, await context.StockMovements.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(beforeLots, await context.InventoryLots.AsNoTracking().OrderBy(value => value.Id)
            .Select(value => new { value.Id, value.QuantityAvailableBase }).ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AvailableDirectSameKeyIsNotRepeatedAsEquivalentAndSuppressesOtherBranchForEmptyOrigin()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var marker = Guid.NewGuid().ToString("N");
        var empty = await ProductSearchTestData.CreateAsync(source, tenant, "Origen", barcode: marker, medicine: ProductSearchTestData.Medicine());
        var available = await ProductSearchTestData.CreateAsync(source, tenant, "Disponible", barcode: marker, medicine: ProductSearchTestData.Medicine());
        await ProductSearchTestData.ReceiveAsync(source, tenant, tenant.Identity.BranchId,
            new ProductSearchTestData.Receipt(available, 2m, ProductSearchTestData.Today));
        await ProductSearchTestData.ReceiveAsync(source, tenant, tenant.SpareBranchId,
            new ProductSearchTestData.Receipt(empty, 10m, ProductSearchTestData.Today));
        var result = await ProductSearchTestData.SearchAsync(source, tenant, marker);
        Assert.Equal(2, result.DirectMatches.Count);
        Assert.Empty(result.EquivalentInBranch);
        Assert.Empty(result.OtherBranchAvailability);
        Assert.Equal(2, result.DirectMatches.Select(value => value.BusinessProductId).Distinct().Count());
    }

    [Fact]
    public async Task BoundedEquivalentBlockRepresentsEachOriginKeyBeforeConsideringOtherBranches()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var marker = Guid.NewGuid().ToString("N");
        var first = await ProductSearchTestData.CreateAsync(source, tenant, "Origen A", barcode: marker, medicine: ProductSearchTestData.Medicine());
        var second = await ProductSearchTestData.CreateAsync(source, tenant, "Origen B", barcode: marker, medicine: ProductSearchTestData.Medicine("250 mg"));
        var firstOption = await ProductSearchTestData.CreateAsync(source, tenant, "A uno", medicine: ProductSearchTestData.Medicine());
        var extraFirstOption = await ProductSearchTestData.CreateAsync(source, tenant, "A dos", medicine: ProductSearchTestData.Medicine());
        var secondOption = await ProductSearchTestData.CreateAsync(source, tenant, "B uno", medicine: ProductSearchTestData.Medicine("250 mg"));
        await ProductSearchTestData.ReceiveAsync(source, tenant, tenant.Identity.BranchId,
            new(firstOption, 1m, ProductSearchTestData.Today), new(extraFirstOption, 1m, ProductSearchTestData.Today),
            new(secondOption, 1m, ProductSearchTestData.Today));
        await ProductSearchTestData.ReceiveAsync(source, tenant, tenant.SpareBranchId,
            new(first, 5m, ProductSearchTestData.Today), new(second, 5m, ProductSearchTestData.Today));
        var result = await ProductSearchTestData.SearchAsync(source, tenant, marker, 2);
        Assert.Equal(2, result.DirectMatches.Count);
        Assert.Equal(2, result.EquivalentInBranch.Count);
        Assert.Contains(result.EquivalentInBranch, value => value.ForDirectProducts.Contains(first.Id));
        Assert.Contains(result.EquivalentInBranch, value => value.Product.BusinessProductId == secondOption.Id &&
            value.ForDirectProducts.Contains(second.Id));
        Assert.Empty(result.OtherBranchAvailability);
        Assert.Equal(2, result.EquivalentInBranch.Select(value => value.Product.BusinessProductId).Distinct().Count());
    }

    [Fact]
    public async Task InformationalSearchCanReadInventoryLotWhileAnotherTransactionHoldsItsRowLock()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var marker = Guid.NewGuid().ToString("N");
        var product = await ProductSearchTestData.CreateAsync(source, tenant, "Lectura", barcode: marker);
        await ProductSearchTestData.ReceiveAsync(source, tenant, tenant.Identity.BranchId,
            new ProductSearchTestData.Receipt(product, 4m, null));
        var context = source.GetRequiredService<MediPosDbContext>();
        var lotId = await context.InventoryLots.Where(value => value.BusinessProductId == product.Id)
            .Select(value => value.Id).SingleAsync(TestContext.Current.CancellationToken);
        await using var writer = fixture.CreateContext(tenant.TenantId);
        await using var transaction = await writer.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await writer.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT id FROM inventory_lots WHERE id = {lotId} FOR UPDATE", TestContext.Current.CancellationToken);
        // A deadlock guard, not a latency benchmark: search must complete while the lock remains held.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var result = await source.GetRequiredService<SearchProductsHandler>().HandleAsync(
            new(tenant.TenantId, tenant.Identity.BranchId, marker, 1), timeout.Token);
        Assert.Equal(4m, Assert.Single(result.DirectMatches).QuantityAvailableBase);
        await transaction.RollbackAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task OtherBranchesUseExactProductAndOnlySameTenantWithSellableStockAndBoundedRows()
    {
        var (first, second) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        var marker = Guid.NewGuid().ToString("N");
        ProductSearchTestData.Product own;
        ProductSearchTestData.Product foreign;
        await using (var secondScope = services.CreateAsyncScope())
        {
            var source = secondScope.ServiceProvider;
            foreign = await ProductSearchTestData.CreateAsync(source, second, "Foreign", barcode: marker, medicine: ProductSearchTestData.Medicine());
            await ProductSearchTestData.ReceiveAsync(source, second, second.Identity.BranchId,
                new ProductSearchTestData.Receipt(foreign, 90m, ProductSearchTestData.Today));
            await ProductSearchTestData.ReceiveAsync(source, second, second.SpareBranchId,
                new ProductSearchTestData.Receipt(foreign, 99m, ProductSearchTestData.Today));
        }
        await using var scope = services.CreateAsyncScope();
        var firstSource = scope.ServiceProvider;
        own = await ProductSearchTestData.CreateAsync(firstSource, first, "Own", barcode: marker, medicine: ProductSearchTestData.Medicine());
        await ProductSearchTestData.ReceiveAsync(firstSource, first, first.Identity.BranchId,
            new ProductSearchTestData.Receipt(own, 3m, ProductSearchTestData.Today.AddDays(-1)));
        await ProductSearchTestData.ReceiveAsync(firstSource, first, first.SpareBranchId,
            new(own, 4m, ProductSearchTestData.Today), new(own, 6m, ProductSearchTestData.Today.AddDays(-1)));
        var third = await firstSource.GetRequiredService<CreateBranchHandler>().HandleAsync(
            new(first.TenantId, first.LegalEntityId, "Third", first.Identity.ActorId), TestContext.Current.CancellationToken);
        await ProductSearchTestData.ReceiveAsync(firstSource, first, third.Id,
            new ProductSearchTestData.Receipt(own, 5m, ProductSearchTestData.Today));
        var result = await ProductSearchTestData.SearchAsync(firstSource, first, marker, 1);
        Assert.Equal(own.Id, Assert.Single(result.DirectMatches).BusinessProductId);
        Assert.Equal(0m, result.DirectMatches[0].QuantityAvailableBase);
        Assert.Empty(result.EquivalentInBranch);
        var other = Assert.Single(result.OtherBranchAvailability);
        Assert.Equal(first.SpareBranchId, other.BranchId);
        Assert.Equal(own.Id, other.BusinessProductId);
        Assert.Equal(4m, other.QuantityAvailableBase);
        Assert.Equal("Spare", other.BranchName);
        Assert.DoesNotContain(result.DirectMatches, value => value.BusinessProductId == foreign.Id);
        Assert.Equal(result.OtherBranchAvailability.Count,
            result.OtherBranchAvailability.Select(value => (value.BranchId, value.BusinessProductId)).Distinct().Count());
        Assert.Equal(2, (await ProductSearchTestData.SearchAsync(firstSource, first, marker)).OtherBranchAvailability.Count);
        // Direct SQL bypasses EF filters here only to verify forced RLS also protects all search sources.
        var context = firstSource.GetRequiredService<MediPosDbContext>();
        Assert.Equal(0, await context.Database.SqlQuery<int>(
            $"SELECT count(*)::int AS \"Value\" FROM business_products WHERE id = {foreign.Id}").SingleAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, await context.Database.SqlQuery<int>(
            $"SELECT count(*)::int AS \"Value\" FROM inventory_lots WHERE tenant_id = {second.TenantId}").SingleAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, await context.Database.SqlQuery<int>(
            $"SELECT count(*)::int AS \"Value\" FROM product_units WHERE business_product_id = {foreign.Id}").SingleAsync(TestContext.Current.CancellationToken));
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() =>
            ProductSearchTestData.SearchAsync(firstSource, first, marker, branch: second.Identity.BranchId));
        Assert.Equal(ApplicationErrors.BranchNotFound, error.Error);
    }

    [Fact]
    public async Task RetailCountsPositiveStockWithExpiredOrMissingDatesWhileUnavailableMedicineHasNoOtherBranch()
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var services = IdentityAccessTestSetup.CreateServices(fixture);
        await using var scope = services.CreateAsyncScope();
        var source = scope.ServiceProvider;
        var retail = await ProductSearchTestData.CreateAsync(source, tenant, "Retail", code: "RETAIL-EXACT");
        var medicine = await ProductSearchTestData.CreateAsync(source, tenant, "Medicine", code: "MEDICINE-EXACT",
            medicine: ProductSearchTestData.Medicine());
        await ProductSearchTestData.ReceiveAsync(source, tenant, tenant.Identity.BranchId,
            new(retail, 3m, null), new(retail, 2m, ProductSearchTestData.Today.AddDays(-1)));
        await ProductSearchTestData.ReceiveAsync(source, tenant, tenant.SpareBranchId,
            new ProductSearchTestData.Receipt(medicine, 2m, ProductSearchTestData.Today.AddDays(-1)));
        Assert.Equal(5m, Assert.Single((await ProductSearchTestData.SearchAsync(source, tenant, "RETAIL-EXACT"))
            .DirectMatches, value => value.BusinessProductId == retail.Id).QuantityAvailableBase);
        var result = await ProductSearchTestData.SearchAsync(source, tenant, "MEDICINE-EXACT");
        Assert.Equal(0m, Assert.Single(result.DirectMatches, value => value.BusinessProductId == medicine.Id).QuantityAvailableBase);
        Assert.Empty(result.EquivalentInBranch);
        Assert.Empty(result.OtherBranchAvailability);
    }
}
