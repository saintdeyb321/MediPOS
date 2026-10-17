using System.Text;
using MediPOS.Application.Modules.Branches.CreateBranch;
using MediPOS.Application.Modules.Branches.CreateLegalEntity;
using MediPOS.Application.Modules.Catalog;
using MediPOS.Application.Modules.Catalog.CreateCategory;
using MediPOS.Application.Modules.Catalog.CreateLocalBusinessProduct;
using MediPOS.Application.Modules.Catalog.ReplaceProductUnits;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.Purchasing.CreateSupplier;
using MediPOS.Application.Modules.Purchasing.Ubl;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.Purchasing.Ubl;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.TestFixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.IntegrationTests.Modules.Purchasing.Ubl;

internal static class SupplierUblTestData
{
    internal static CancellationToken Token => TestContext.Current.CancellationToken;
    internal sealed record Rows(Guid TenantId, Guid BranchId, Guid LegalId, Guid UserId, Guid MembershipId, Guid SupplierId, Guid CategoryId, IReadOnlyList<Guid> ProductIds);
    internal static void Authenticate(IServiceProvider provider, Guid? user) => ((IdentityAccessTestSetup.TestServerSession)provider.GetRequiredService<IAuthenticatedMediPosUser>()).UserId = user;
    internal static async Task<Rows> Setup(IServiceProvider source, int products = 2)
    {
        var identity = await IdentityAccessTestSetup.CreateAsync(source, TenantRole.Owner);
        var legal = await source.GetRequiredService<CreateLegalEntityHandler>().HandleAsync(new(identity.TenantId, "Botica receptora sintética", SupplierUblSyntheticXml.BuyerRuc, identity.UserId), Token);
        var branch = await source.GetRequiredService<CreateBranchHandler>().HandleAsync(new(identity.TenantId, legal.Id, "Revisión UBL", identity.UserId), Token);
        var supplier = await source.GetRequiredService<CreateSupplierHandler>().HandleAsync(new(identity.TenantId, "Distribuidora Ñandú", SupplierUblSyntheticXml.SupplierRuc, null, identity.UserId), Token);
        var category = await source.GetRequiredService<CreateCategoryHandler>().HandleAsync(new("UBL synthetic " + Guid.NewGuid().ToString("N")), Token);
        var ids = new List<Guid>();
        for (var n = 1; n <= products; n++) ids.Add(await Product(source, identity.TenantId, identity.UserId, category.Id, "SKU-" + n.ToString(System.Globalization.CultureInfo.InvariantCulture), n == 2));
        Authenticate(source, identity.UserId);
        return new(identity.TenantId, branch.Id, legal.Id, identity.UserId, identity.MembershipId, supplier.Id, category.Id, ids);
    }
    internal static async Task<Guid> Product(IServiceProvider source, Guid tenant, Guid user, Guid category, string code, bool ambiguous = false, string? barcode = null)
    {
        var product = await source.GetRequiredService<CreateLocalBusinessProductHandler>().HandleAsync(new(tenant, code, ProductType.Medicine,
            "Medicamento local " + code, category, "Laboratorio", barcode, new MedicineInput([new("Ingrediente sintético", "500 mg")], "Tableta"), 12.50m, 10m, user), Token);
        var input = new List<ProductUnitDefinition> { new("Base", 1, true), new("Caja 20", 20, false) };
        if (ambiguous) input.Add(new("Caja 100", 100, false));
        await source.GetRequiredService<VersionedProductUnitsFixture>().HandleAsync(new(tenant, product.Id, input, user), Token); return product.Id;
    }
    internal static async Task<SupplierUblImportDetails> Import(IServiceProvider source, Rows rows, string xml, string filename = "synthetic.xml")
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        return await source.GetRequiredService<ImportSupplierUblInvoiceHandler>().HandleAsync(new(rows.TenantId, rows.BranchId, input, filename), Token);
    }
    internal static async Task<SupplierUblImportDetails> Review(IServiceProvider source, Rows rows, SupplierUblImportDetails import, IReadOnlyList<Guid> products, bool complete = true)
    {
        var handler = source.GetRequiredService<GetSupplierUblImportLinesHandler>(); var original = await handler.HandleAsync(new(rows.TenantId, rows.BranchId, import.Id), Token);
        var context = source.GetRequiredService<MediPosDbContext>(); context.SelectTenant(rows.TenantId);
        var units = await context.ProductUnits.AsNoTracking().Where(v => products.Contains(v.BusinessProductId) && v.Name == "Caja 20").ToArrayAsync(Token);
        var changes = original.Lines.Select((line, i) => new ReviewSupplierUblLineInput(line.Id, true, products[i], units.Single(u => u.BusinessProductId == products[i]).Id,
            2m, 2.125m, true, "Presentation, physical receipt quantity and inventory cost explicitly reviewed; XML tax is documentary evidence.",
            [new(1m, "LOT-A", new(2030, 1, 1)), new(1m, "LOT-B", new(2031, 1, 1))])).ToArray();
        return await source.GetRequiredService<ReviewSupplierUblImportHandler>().HandleAsync(new(rows.TenantId, rows.BranchId, import.Id, import.Version, rows.SupplierId,
            complete ? "complete_review" : "save_review", "Supplier and document reviewed; receipt is explicit.", changes), Token);
    }
    internal static Task<SupplierUblImportDetails> Convert(IServiceProvider source, Rows rows, SupplierUblImportDetails import) =>
        source.GetRequiredService<ConvertSupplierUblImportToPurchaseDraftHandler>().HandleAsync(new(rows.TenantId, rows.BranchId, import.Id, import.Version), Token);
    internal static string Document(int lines = 1, string? barcode = null) => SupplierUblSyntheticXml.Create("F001-" + Random.Shared.Next(1, 99999999).ToString(System.Globalization.CultureInfo.InvariantCulture), count: lines, buyerCode: lines == 1 ? "SKU-1" : "SKU-", barcode: barcode);
}
