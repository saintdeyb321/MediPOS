using MediPOS.Application.Modules.ElectronicDocuments;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Domain.Modules.Branches;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.ElectronicDocuments;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.SalesPos;
using MediPOS.Domain.Modules.TenancyLicensing;

namespace MediPOS.TestFixtures;

internal sealed class ElectronicDocumentTestData
{
    internal static readonly DateTimeOffset Now = new(2026, 10, 6, 14, 0, 0, TimeSpan.Zero);
    internal Guid Tenant { get; } = Guid.CreateVersion7();
    internal Guid Membership { get; } = Guid.CreateVersion7();
    internal LegalEntity Legal { get; }
    internal Branch Branch { get; }
    internal BusinessProduct Product { get; }
    internal Sale Sale { get; }
    internal SalePayment Payment { get; }
    internal ElectronicDocumentSeries Series { get; }
    internal FiscalInput Input { get; }
    internal ElectronicDocumentTestData(bool confirmed = true, string type = ElectronicDocumentTypes.Receipt)
    {
        Legal = LegalEntity.Create(Tenant, "Botica Ñandú S.A.C.", "20123456789", Now);
        Branch = Branch.Create(Tenant, Legal.Id, Tenant, "Sede", Now);
        Product = BusinessProduct.CreateLocal(Tenant, "P1", ProductType.Retail, "Artículo", Guid.CreateVersion7(), "Marca", null, null, 2.125m, null, Now);
        var unit = ProductUnit.Create(Tenant, Product.Id, Tenant, "Caja", 10m, false);
        Sale = Sale.CreateDraft(Tenant, Branch.Id, Membership, Guid.CreateVersion7(), Now);
        var line = SaleLine.Create(Sale, Product, unit, 2m, PriceKind.Retail); Sale.ReplaceLines([line], Now);
        Payment = SalePayment.Create(Sale, PaymentMethod.Cash, Sale.TotalAmount);
        if (confirmed) Sale.Confirm([Payment], Now);
        Series = ElectronicDocumentSeries.Create(Tenant, Legal.Id, Membership, type, "TEST1", ElectronicDocumentRules.Profile, "Av. Perú 123", 1, Now);
        Input = new(ElectronicDocumentRules.Profile, "PEN", new("ruc", "20987654321", "José Muñoz", "Calle Unión 1"), [new(line.Id, 42.5m, 0m, "explicit_test_classification")]);
    }
    internal RequestElectronicDocumentCommand Command => new(Tenant, Branch.Id, Sale.Id, Series.Id, Series.DocumentType, Input);
    internal ElectronicRequestData Data => new(Sale, Branch, Legal, Series, null);
    internal ElectronicDocument Create() => ElectronicDocument.Create(Series, Branch.Id, Sale.Id, Membership, Membership,
        ElectronicDocumentPreparation.Snapshot(Data, Input), ElectronicDocumentPreparation.Fingerprint(Command), "PEN", Sale.TotalAmount, Now);
    internal AccessContext Access(TenantRole role) => new(Guid.CreateVersion7(), Tenant, Membership, role, Branch.Id, true, true, LicenseStatus.Active, Now.AddDays(-1), Now.AddDays(1), true, Now);
    internal sealed class Clock : TimeProvider
    {
        internal DateTimeOffset NowUtc { get; set; } = Now;
        public override DateTimeOffset GetUtcNow() => NowUtc;
    }
    // This fake exists in test assemblies only; production DI never references it.
    internal sealed class Provider(ElectronicProviderResult result, bool simulation = true) : IElectronicDocumentProvider
    {
        internal int Calls { get; private set; }
        internal Exception? Failure { get; set; }
        public ProviderCapabilities Capabilities { get; } = new(true, simulation, false);
        public Task<ElectronicProviderResult> SubmitAsync(ElectronicDeliveryClaim claim, CancellationToken token)
        { Calls++; return Failure is null ? Task.FromResult(result) : Task.FromException<ElectronicProviderResult>(Failure); }
        public Task<ElectronicProviderResult> QueryAsync(string idempotencyKey, CancellationToken token) => Task.FromResult(ElectronicProviderContract.Unknown("query_not_supported"));
    }
    internal sealed class Identity(Guid tenant) : IElectronicDeliveryIdentity
    {
        internal ElectronicDeliveryIdentity Principal { get; } = new(tenant, Guid.CreateVersion7());
        public Task<ElectronicDeliveryIdentity?> AuthorizeAsync(Guid tenantId, CancellationToken token) => Task.FromResult<ElectronicDeliveryIdentity?>(tenantId == Principal.TenantId ? Principal : null);
    }
    internal sealed class EvidenceVerifier : IElectronicAuthorityEvidenceVerifier
    { public Task<bool> VerifyAsync(AuthorityEvidence evidence, CancellationToken token) => Task.FromResult(false); }
}
