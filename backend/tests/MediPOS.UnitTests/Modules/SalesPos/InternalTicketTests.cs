using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.SalesPos;
using MediPOS.Application.Modules.SalesPos.GetInternalTicket;
using MediPOS.Application.Tenancy;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.SalesPos;
using MediPOS.Domain.Modules.TenancyLicensing;

namespace MediPOS.UnitTests.Modules.SalesPos;

public sealed class InternalTicketTests
{
    internal static readonly DateTimeOffset Now = new(2026, 10, 6, 14, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] PaymentMethods = ["card", "cash", "plin", "transfer", "yape"];
    private static readonly int[] PrintWidths = [58, 80];

    [Fact]
    public async Task ConfirmedTicketKeepsHistoricalLinesAllOriginalPaymentsAndExplicitInternalPrintContract()
    {
        var setup = new Setup();
        var ticket = await setup.ReadAsync();
        var line = Assert.Single(ticket.Lines);
        Assert.Equal((setup.Sale.Id, setup.Sale.CashSessionId, Now.AddSeconds(-1), "confirmed", "CONFIRMADA"),
            (ticket.SaleId, ticket.CashSessionId, ticket.SaleDateTime, ticket.Status, ticket.StatusLabel));
        Assert.Equal(("Producto", "Presentación", 2m, 20m, 10m, 2.125m, "retail", 42.5m),
            (line.ProductName, line.UnitName, line.Quantity, line.BaseQuantity, line.ConversionToBase, line.UnitPrice, line.PriceKind, line.LineTotal));
        Assert.Equal("BASE_UNIT", line.UnitPriceBasis);
        Assert.Equal(ticket.TotalAmount, ticket.Lines.Sum(l => l.LineTotal));
        Assert.Equal(ticket.TotalAmount, ticket.Payments.Sum(p => p.Amount));
        Assert.Equal(PaymentMethods, ticket.Payments.Select(p => p.Method));
        Assert.Equal(setup.Member.UserId, ticket.CurrentDisplayData.SellerUserId);
        Assert.Equal(setup.Member.Id, ticket.CurrentDisplayData.SellerMembershipId);
        Assert.False(ticket.IsVoided);
        Assert.Null(ticket.VoidedAt);
        Assert.Equal("INTERNAL_TICKET", ticket.DocumentType);
        Assert.Equal("DOCUMENTO INTERNO - NO ES COMPROBANTE DE PAGO ELECTRÓNICO", ticket.Footer);
        Assert.Equal(PrintWidths, ticket.SupportedPrintWidthsMm);
        Assert.StartsWith("MP-", ticket.TicketNumber, StringComparison.Ordinal);
        Assert.Equal(ticket.SaleId, Guid.ParseExact(ticket.TicketNumber[3..], "N"));
        Assert.Equal(ticket.TicketNumber, (await setup.ReadAsync()).TicketNumber);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ticket, JsonOptions));
        Assert.Equal(ticket.Footer, json.RootElement.GetProperty("footer").GetString());
        Assert.Equal("BASE_UNIT", json.RootElement.GetProperty("lines")[0].GetProperty("unitPriceBasis").GetString());
        Assert.False(json.RootElement.TryGetProperty("cost", out _));
    }

    [Fact]
    public async Task CatalogEditsUnitReplacementAndLaterVoidCannotRewriteAlreadyReturnedTransactionSnapshots()
    {
        var setup = new Setup();
        var before = await setup.ReadAsync();
        setup.Product.UpdatePrices(999m, 888m);
        setup.Product.SetStatus(false);
        typeof(BusinessProduct).GetProperty(nameof(BusinessProduct.Name))!.SetValue(setup.Product, "Nuevo nombre");
        var replacementUnit = ProductUnit.Create(setup.Sale.TenantId, setup.Product.Id, setup.Sale.TenantId, "Otra unidad", 20m, false);
        Assert.NotEqual(setup.Unit.Id, replacementUnit.Id);
        setup.Reader.Snapshot = setup.Reader.Snapshot! with
        { CurrentDisplayData = setup.Reader.Snapshot.CurrentDisplayData with { BusinessName = "Nombre comercial actual", BranchName = "Sucursal actual" } };
        var after = await setup.ReadAsync();
        Assert.Equal(before.Lines, after.Lines);
        Assert.Equal(before.Payments, after.Payments);
        Assert.Equal(before.TotalAmount, after.TotalAmount);
        Assert.Equal("Nombre comercial actual", after.CurrentDisplayData.BusinessName);
        setup.Sale.Void("  Error de cobro  ", setup.Member.UserId, Now);
        var voided = await setup.ReadAsync();
        Assert.True(voided.IsVoided);
        Assert.Equal(("voided", "ANULADA", Now, "Error de cobro"), (voided.Status, voided.StatusLabel, voided.VoidedAt!.Value, voided.VoidReason));
        Assert.Equal(before.SaleDateTime, voided.SaleDateTime);
        Assert.Equal(before.TicketNumber, voided.TicketNumber);
        Assert.Equal(before.Payments, voided.Payments);
        Assert.Equal(before.Lines, voided.Lines);
        Assert.Equal(42.5m, voided.TotalAmount);
        Assert.False(before.IsVoided);
        Assert.Throws<NotSupportedException>(() => ((IList<InternalTicketPayment>)voided.Payments).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<int>)voided.SupportedPrintWidthsMm).Clear());
    }

    [Theory]
    [InlineData(TenantRole.Cashier, true, true)]
    [InlineData(TenantRole.Pharmacist, true, true)]
    [InlineData(TenantRole.Cashier, false, false)]
    [InlineData(TenantRole.Pharmacist, false, false)]
    [InlineData(TenantRole.Owner, false, true)]
    public async Task TicketRequiresOwnOperationalSaleOrOwnerInActualTenantBranch(TenantRole role, bool own, bool allowed)
    {
        var setup = new Setup(role, own);
        if (allowed) Assert.Equal(setup.Sale.Id, (await setup.ReadAsync()).SaleId);
        else Assert.Equal(InternalTicketErrors.Forbidden, (await Assert.ThrowsAsync<ApplicationErrorException>(setup.ReadAsync)).Error);
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("branch")]
    [InlineData("missing")]
    public async Task ReaderCannotExposeForeignScopeOrUnknownSale(string defect)
    {
        var setup = new Setup();
        if (defect == "missing") setup.Reader.Snapshot = null;
        else typeof(Sale).GetProperty(defect == "tenant" ? nameof(Sale.TenantId) : nameof(Sale.BranchId))!.SetValue(setup.Sale, Guid.NewGuid());
        Assert.Equal(SalesPosErrors.SaleNotFound, (await Assert.ThrowsAsync<ApplicationErrorException>(setup.ReadAsync)).Error);
    }

    [Theory]
    [InlineData("identity", "access.unauthenticated")]
    [InlineData("license", "access.license_denied")]
    [InlineData("membership", "access.membership_inactive")]
    [InlineData("schedule", "access.schedule_denied")]
    [InlineData("branch", "access.branch_denied")]
    [InlineData("tenant", "access.tenant_mismatch")]
    public async Task ServerAccessIsValidatedBeforeAnyTicketHistoryRead(string defect, string code)
    {
        var setup = new Setup();
        switch (defect)
        {
            case "identity": setup.Identity.UserId = null; break;
            case "license": setup.Access.Snapshot = setup.Access.Snapshot with { License = null }; break;
            case "membership": setup.Member.Deactivate(Now); break;
            case "schedule": setup.Access.Snapshot = setup.Access.Snapshot with { Schedule = [] }; break;
            case "branch": setup.Access.Snapshot = setup.Access.Snapshot with { BranchBelongsToTenant = false }; break;
            case "tenant": setup.Query = setup.Query with { TenantId = Guid.NewGuid() }; break;
        }
        Assert.Equal(code, (await Assert.ThrowsAsync<ApplicationErrorException>(setup.ReadAsync)).Error.Code);
        Assert.Equal(0, setup.Reader.Reads);
    }

    [Fact]
    public async Task DraftHasNoFinalTicketAndInvalidIdentifiersNeverReachReader()
    {
        var setup = new Setup(confirmed: false);
        Assert.Equal(InternalTicketErrors.NotFinal, (await Assert.ThrowsAsync<ApplicationErrorException>(setup.ReadAsync)).Error);
        foreach (var query in new[] { setup.Query with { TenantId = Guid.Empty }, setup.Query with { BranchId = Guid.Empty }, setup.Query with { SaleId = Guid.Empty } })
        {
            var reads = setup.Reader.Reads;
            Assert.Equal(ApplicationErrors.InvalidRequest, (await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Handler.HandleAsync(query, TestContext.Current.CancellationToken))).Error);
            Assert.Equal(reads, setup.Reader.Reads);
        }
    }

    [Theory]
    [InlineData("empty-lines")]
    [InlineData("line-total")]
    [InlineData("sale-total")]
    [InlineData("empty-payments")]
    [InlineData("payment-total")]
    [InlineData("payment-tenant")]
    [InlineData("payment-sale")]
    [InlineData("void-metadata")]
    [InlineData("display-seller")]
    public async Task InvalidHistoryFailsClosedInsteadOfRepairingOrReturningAnInventedTicket(string defect)
    {
        var setup = new Setup();
        switch (defect)
        {
            case "empty-lines": ((List<SaleLine>)typeof(Sale).GetField("_lines", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(setup.Sale)!).Clear(); break;
            case "line-total": Set(Assert.Single(setup.Sale.Lines), nameof(SaleLine.LineTotal), 1m); break;
            case "sale-total": Set(setup.Sale, nameof(Sale.TotalAmount), 1m); break;
            case "empty-payments": setup.Reader.Snapshot = setup.Reader.Snapshot! with { Payments = [] }; break;
            case "payment-total": Set(setup.Payments[0], nameof(SalePayment.Amount), .5m); break;
            case "payment-tenant": Set(setup.Payments[0], nameof(SalePayment.TenantId), Guid.NewGuid()); break;
            case "payment-sale": Set(setup.Payments[0], nameof(SalePayment.SaleId), Guid.NewGuid()); break;
            case "void-metadata": Set(setup.Sale, nameof(Sale.Status), SaleStatus.Voided); break;
            case "display-seller":
                setup.Reader.Snapshot = setup.Reader.Snapshot! with
                { CurrentDisplayData = setup.Reader.Snapshot.CurrentDisplayData with { SellerMembershipId = Guid.NewGuid() } }; break;
        }
        Assert.Equal(InternalTicketErrors.CorruptedHistory, (await Assert.ThrowsAsync<ApplicationErrorException>(setup.ReadAsync)).Error);
        Assert.Equal(1, setup.Reader.Reads);
    }

    private static void Set<T>(T instance, string name, object value) => typeof(T).GetProperty(name)!.SetValue(instance, value);

    private sealed class Setup
    {
        public Setup(TenantRole role = TenantRole.Cashier, bool own = true, bool confirmed = true)
        {
            var tenant = Guid.NewGuid();
            Member = Membership.Create(tenant, Guid.NewGuid(), role, Now.AddDays(-1));
            Sale = Sale.CreateDraft(tenant, Guid.NewGuid(), own ? Member.Id : Guid.NewGuid(), Guid.NewGuid(), Now.AddSeconds(-2));
            Product = SaleDomainTests.Product(tenant, 2.125m, 1.75m);
            Unit = SaleDomainTests.Unit(Product, 10m);
            Sale.ReplaceLines([SaleLine.Create(Sale, Product, Unit, 2m, PriceKind.Retail)], Now.AddSeconds(-2));
            Payments = [SalePayment.Create(Sale, PaymentMethod.Cash, 1m), SalePayment.Create(Sale, PaymentMethod.Yape, 2m),
                SalePayment.Create(Sale, PaymentMethod.Plin, 3m), SalePayment.Create(Sale, PaymentMethod.Card, 4m), SalePayment.Create(Sale, PaymentMethod.Transfer, 32.5m)];
            if (confirmed) Sale.Confirm(Payments, Now.AddSeconds(-1));
            Reader = new(new(Sale, Payments, new(tenant, "Botica", Guid.NewGuid(), "Botica SAC", "123", Sale.BranchId, "Centro",
                Sale.SellerMembershipId, own ? Member.UserId : Guid.NewGuid(), "Vendedor")));
            Identity = new() { UserId = Member.UserId };
            Access = new(new(true, Member, License.Create(tenant, Now.AddDays(-1), Now.AddMonths(1), 3, LicenseStatus.Active, Member.UserId, Now.AddDays(-1)),
                true, true, [WorkSchedule.Create(tenant, Member.Id, tenant, DayOfWeek.Tuesday, new(9, 0), new(18, 0))]));
            Handler = new(new ResolveAccessContextHandler(Identity, Access, new TenantDataContext()), Reader, new Clock());
            Query = new(tenant, Sale.BranchId, Sale.Id);
        }
        public Sale Sale { get; }
        public SalePayment[] Payments { get; }
        public Membership Member { get; }
        public BusinessProduct Product { get; }
        public ProductUnit Unit { get; }
        public Reader Reader { get; }
        public Identity Identity { get; }
        public AccessReader Access { get; }
        public GetInternalTicketQuery Query { get; set; }
        public GetInternalTicketHandler Handler { get; }
        public Task<InternalTicket> ReadAsync() => Handler.HandleAsync(Query, TestContext.Current.CancellationToken);
    }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Identity : IAuthenticatedMediPosUser { public Guid? UserId { get; set; } }
    private sealed class AccessReader(OperationalAccessSnapshot snapshot) : IOperationalAccessReader
    {
        public OperationalAccessSnapshot Snapshot { get; set; } = snapshot;
        public Task<OperationalAccessSnapshot> ReadAsync(Guid userId, Guid tenantId, Guid? branchId, CancellationToken cancellationToken) => Task.FromResult(Snapshot);
    }
    private sealed class Reader(InternalTicketSnapshot snapshot) : IInternalTicketReader
    {
        public InternalTicketSnapshot? Snapshot { get; set; } = snapshot;
        public int Reads { get; private set; }
        public Task<InternalTicketSnapshot?> FindAsync(Guid tenantId, Guid branchId, Guid saleId, CancellationToken cancellationToken)
        { Reads++; return Task.FromResult(Snapshot); }
    }
}
