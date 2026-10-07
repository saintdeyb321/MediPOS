using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Cash;
using MediPOS.Application.Modules.Catalog;
using MediPOS.Application.Modules.Catalog.ReplaceProductUnits;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.SalesPos;
using MediPOS.Application.Modules.SalesPos.CreateSaleDraft;
using MediPOS.Application.Modules.SalesPos.GetSaleDraft;
using MediPOS.Application.Modules.SalesPos.ReplaceSaleLines;
using MediPOS.Application.Tenancy;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.SalesPos;
using MediPOS.Domain.Modules.TenancyLicensing;

namespace MediPOS.UnitTests.Modules.SalesPos;

public sealed class SaleDraftUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreateGetsSellerAndCashFromAuthenticatedAccessWithOnlyTenantBranchInput()
    {
        var setup = new Setup();
        var result = await setup.Create.HandleAsync(new(setup.TenantId, setup.BranchId), TestContext.Current.CancellationToken);
        Assert.Equal(setup.Membership.Id, result.SellerMembershipId);
        Assert.Equal(setup.Cash.Session!.CashSessionId, result.CashSessionId);
        Assert.Equal(SaleStatus.Draft, result.Status);
        Assert.Equal(0m, result.TotalAmount);
        Assert.Empty(result.Lines);
        Assert.Equal((setup.TenantId, setup.BranchId, setup.Membership.Id), setup.Cash.Selection);
        Assert.Equal(1, setup.Store.Creates);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("branch")]
    [InlineData("membership")]
    [InlineData("tenant")]
    public async Task CashMustExactlyMatchTenantBranchAndAuthenticatedMembership(string failure)
    {
        var setup = new Setup();
        setup.Cash.Session = failure switch
        {
            "none" => null,
            "branch" => setup.Cash.Session! with { BranchId = Guid.NewGuid() },
            "membership" => setup.Cash.Session! with { MembershipId = Guid.NewGuid() },
            _ => setup.Cash.Session! with { TenantId = Guid.NewGuid() },
        };
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Create.HandleAsync(
            new(setup.TenantId, setup.BranchId), TestContext.Current.CancellationToken));
        Assert.Equal(failure == "none" ? SalesPosErrors.CashSessionRequired : SalesPosErrors.CashSessionMismatch, error.Error);
        Assert.Equal(0, setup.Store.Creates);
    }

    [Theory]
    [InlineData("license", "access.license_denied")]
    [InlineData("inactive", "access.membership_inactive")]
    [InlineData("schedule", "access.schedule_denied")]
    [InlineData("branch", "access.branch_denied")]
    [InlineData("unassigned", "access.branch_unassigned")]
    [InlineData("tenant", "tenant.scope_conflict")]
    [InlineData("unauthenticated", "access.unauthenticated")]
    public async Task OperationalDenialsBlockCreatingAndReplacingBeforeCashOrDraftReads(string failure, string code)
    {
        var setup = new Setup();
        var draft = await setup.NewDraftAsync();
        setup.Cash.Selection = null;
        setup.Store.Reads = 0;
        switch (failure)
        {
            case "license": setup.Access.Snapshot = setup.Access.Snapshot with { License = null }; break;
            case "inactive": setup.Membership.Deactivate(Now); break;
            case "schedule": setup.Access.Snapshot = setup.Access.Snapshot with { Schedule = [] }; break;
            case "branch": setup.Access.Snapshot = setup.Access.Snapshot with { BranchBelongsToTenant = false }; break;
            case "unassigned": setup.Access.Snapshot = setup.Access.Snapshot with { BranchAssigned = false }; break;
            case "tenant": setup.TenantId = Guid.NewGuid(); break;
            case "unauthenticated": setup.Session.UserId = null; break;
        }
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Replace.HandleAsync(setup.Command(draft), TestContext.Current.CancellationToken));
        Assert.Equal(code, error.Error.Code);
        var create = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Create.HandleAsync(new(setup.TenantId, setup.BranchId), TestContext.Current.CancellationToken));
        Assert.Equal(code, create.Error.Code);
        Assert.Equal(0, setup.Store.Replacements);
        Assert.Equal(0, setup.Store.Reads);
        Assert.Null(setup.Cash.Selection);
    }

    [Fact]
    public async Task OwnerExemptionFromScheduleAndAssignmentStillBindsDraftToOwnMembership()
    {
        var setup = new Setup(TenantRole.Owner);
        setup.Access.Snapshot = setup.Access.Snapshot with { Schedule = [], BranchAssigned = false };
        var draft = await setup.NewDraftAsync();
        var updated = await setup.Replace.HandleAsync(setup.Command(draft), TestContext.Current.CancellationToken);
        Assert.Equal(setup.Membership.Id, updated.SellerMembershipId);
    }

    [Fact]
    public async Task FreeTenantSelectionCannotAuthorizeCreatingADraft()
    {
        var setup = new Setup();
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Create.HandleAsync(
            new(Guid.NewGuid(), setup.BranchId), TestContext.Current.CancellationToken));
        Assert.Equal("access.tenant_mismatch", error.Error.Code);
        Assert.Null(setup.Cash.Selection);
        Assert.Equal(0, setup.Store.Creates);
    }

    [Theory]
    [InlineData(PriceKind.Retail, "42.5")]
    [InlineData(PriceKind.Wholesale, "35")]
    public async Task ReplacementCopiesServerPricesAndConversionsAndReadRetainsSnapshots(PriceKind kind, string expected)
    {
        var setup = new Setup();
        var draft = await setup.NewDraftAsync();
        var replaced = await setup.Replace.HandleAsync(setup.Command(draft, new SaleLineInput(setup.Product.Id, setup.Unit.Id, 2m, kind)), TestContext.Current.CancellationToken);
        var line = Assert.Single(replaced.Lines);
        Assert.Equal(20m, line.BaseQuantity);
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), line.LineTotal);
        Assert.Equal(line.LineTotal, replaced.TotalAmount);
        Assert.Equal(draft.Version + 1, replaced.Version);
        setup.Product.UpdatePrices(999m, 888m);
        var read = await setup.Get.HandleAsync(new(setup.TenantId, setup.BranchId, draft.SaleId), TestContext.Current.CancellationToken);
        Assert.Equal(replaced.TotalAmount, read.TotalAmount);
        Assert.Equal(line, Assert.Single(read.Lines));
        Assert.Equal(1, setup.Store.Replacements);
        Assert.Equal(0, setup.Products.Mutations);
        Assert.Equal(0, setup.Units.Mutations);
    }

    [Theory]
    [InlineData("zero", "sale.invalid_line")]
    [InlineData("negative", "sale.invalid_line")]
    [InlineData("product", "sale.product_unavailable")]
    [InlineData("unit", "sale.unit_unavailable")]
    [InlineData("wrong_product", "sale.unit_unavailable")]
    [InlineData("foreign_unit", "sale.unit_unavailable")]
    [InlineData("missing_unit", "sale.unit_unavailable")]
    [InlineData("wholesale", "sale.wholesale_unavailable")]
    [InlineData("kind", "sale.invalid_line")]
    public async Task InvalidLineNeverReplacesPersistedCart(string failure, string code)
    {
        var setup = new Setup();
        var draft = await setup.NewDraftAsync();
        var prior = await setup.Replace.HandleAsync(setup.Command(draft), TestContext.Current.CancellationToken);
        var input = new SaleLineInput(setup.Product.Id, setup.Unit.Id, 2m, PriceKind.Retail);
        switch (failure)
        {
            case "zero": input = input with { Quantity = 0 }; break;
            case "negative": input = input with { Quantity = -1 }; break;
            case "product": setup.Product.SetStatus(false); break;
            case "unit": setup.Units.Values = [SaleDomainTests.Unit(setup.Product, 1m, false)]; input = input with { ProductUnitId = setup.Units.Values[0].Id }; break;
            case "wrong_product": setup.Units.Values = [SaleDomainTests.Unit(SaleDomainTests.Product(setup.TenantId), 1m)]; input = input with { ProductUnitId = setup.Units.Values[0].Id }; break;
            case "foreign_unit": setup.Units.Values = [SaleDomainTests.Unit(SaleDomainTests.Product(Guid.NewGuid()), 1m)]; input = input with { ProductUnitId = setup.Units.Values[0].Id }; break;
            case "missing_unit": input = input with { ProductUnitId = Guid.NewGuid() }; break;
            case "wholesale": setup.Product.UpdatePrices(1m, null); input = input with { PriceKind = PriceKind.Wholesale }; break;
            case "kind": input = input with { PriceKind = (PriceKind)99 }; break;
        }
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Replace.HandleAsync(setup.Command(prior, input), TestContext.Current.CancellationToken));
        Assert.Equal(code, error.Error.Code);
        Assert.Equal(1, setup.Store.Replacements);
        Assert.Equal(prior.TotalAmount, setup.Store.Current!.Sale.TotalAmount);
        Assert.Equal(prior.Lines[0].SaleLineId, setup.Store.Current.Sale.Lines[0].Id);
    }

    [Fact]
    public async Task LaterInvalidLineAndDuplicateSelectionPreserveWholeExistingCart()
    {
        var setup = new Setup();
        var prior = await setup.Replace.HandleAsync(setup.Command(await setup.NewDraftAsync()), TestContext.Current.CancellationToken);
        var valid = new SaleLineInput(setup.Product.Id, setup.Unit.Id, 3m, PriceKind.Retail);
        var invalid = valid with { ProductUnitId = Guid.NewGuid() };
        await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Replace.HandleAsync(setup.Command(prior, valid, invalid), TestContext.Current.CancellationToken));
        var duplicate = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Replace.HandleAsync(setup.Command(prior, valid, valid), TestContext.Current.CancellationToken));
        Assert.Equal(SalesPosErrors.DuplicateLines, duplicate.Error);
        Assert.Equal(1, setup.Store.Replacements);
        Assert.Equal(prior.TotalAmount, setup.Store.Current!.Sale.TotalAmount);
    }

    [Theory]
    [InlineData("seller", "sale.seller_required")]
    [InlineData("confirmed", "sale.not_draft")]
    [InlineData("cash_closed", "sale.open_cash_session_required")]
    [InlineData("new_cash", "sale.cash_session_mismatch")]
    [InlineData("stale", "sale.concurrent_edit")]
    public async Task EditingRequiresOwnDraftOriginalOpenCashAndCurrentVersion(string failure, string code)
    {
        var setup = new Setup();
        var draft = await setup.NewDraftAsync();
        var command = setup.Command(draft);
        switch (failure)
        {
            case "seller": typeof(Sale).GetProperty(nameof(Sale.SellerMembershipId))!.SetValue(setup.Store.Current!.Sale, Guid.NewGuid()); break;
            case "confirmed": typeof(Sale).GetProperty(nameof(Sale.Status))!.SetValue(setup.Store.Current!.Sale, SaleStatus.Confirmed); break;
            case "cash_closed": setup.Cash.Session = null; break;
            case "new_cash": setup.Cash.Session = setup.Cash.Session! with { CashSessionId = Guid.NewGuid() }; break;
            case "stale": command = command with { ExpectedVersion = draft.Version + 1 }; break;
        }
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Replace.HandleAsync(command, TestContext.Current.CancellationToken));
        Assert.Equal(code, error.Error.Code);
        Assert.Equal(0, setup.Store.Replacements);
    }

    [Fact]
    public async Task StoreConcurrencyConflictPropagatesAndDoesNotReturnSuccessOrRetry()
    {
        var setup = new Setup();
        var draft = await setup.NewDraftAsync();
        setup.Store.Failure = new ApplicationErrorException(SalesPosErrors.ConcurrentEdit);
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Replace.HandleAsync(setup.Command(draft), TestContext.Current.CancellationToken));
        Assert.Equal(SalesPosErrors.ConcurrentEdit, error.Error);
        Assert.Equal(1, setup.Store.Replacements);
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("branch")]
    [InlineData("seller")]
    public async Task DraftReadCannotExposeAnotherTenantBranchOrSeller(string mismatch)
    {
        var setup = new Setup();
        var draft = await setup.NewDraftAsync();
        var property = mismatch switch { "tenant" => nameof(Sale.TenantId), "branch" => nameof(Sale.BranchId), _ => nameof(Sale.SellerMembershipId) };
        typeof(Sale).GetProperty(property)!.SetValue(setup.Store.Current!.Sale, Guid.NewGuid());
        await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Get.HandleAsync(new(setup.TenantId, setup.BranchId, draft.SaleId), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OwnerCanReadAnotherSellerInValidatedBranchButCannotEditTheirDraft()
    {
        var setup = new Setup(TenantRole.Owner);
        var draft = await setup.NewDraftAsync();
        typeof(Sale).GetProperty(nameof(Sale.SellerMembershipId))!.SetValue(setup.Store.Current!.Sale, Guid.NewGuid());
        var read = await setup.Get.HandleAsync(new(setup.TenantId, setup.BranchId, draft.SaleId), TestContext.Current.CancellationToken);
        Assert.Equal(draft.SaleId, read.SaleId);
        var edit = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Replace.HandleAsync(setup.Command(draft), TestContext.Current.CancellationToken));
        Assert.Equal(SalesPosErrors.SellerRequired, edit.Error);
    }

    private sealed class Setup
    {
        public Setup(TenantRole role = TenantRole.Cashier)
        {
            TenantId = Guid.NewGuid(); BranchId = Guid.NewGuid();
            Membership = Membership.Create(TenantId, Guid.NewGuid(), role, Now.AddDays(-1));
            Session = new Session { UserId = Membership.UserId };
            Access = new AccessReader(new(true, Membership,
                License.Create(TenantId, Now.AddDays(-1), Now.AddMonths(1), 3, LicenseStatus.Active, Membership.UserId, Now.AddDays(-1)),
                true, true, [WorkSchedule.Create(TenantId, Membership.Id, TenantId, DayOfWeek.Tuesday, new(9, 0), new(18, 0))]));
            Cash.Session = new(Guid.NewGuid(), TenantId, BranchId, Membership.Id, 0m, Now, Membership.UserId);
            Product = SaleDomainTests.Product(TenantId, 2.125m, 1.75m);
            Unit = SaleDomainTests.Unit(Product, 10m);
            Products = new Products(Product); Units = new Units { Values = [Unit] };
            var resolver = new ResolveAccessContextHandler(Session, Access, new TenantDataContext());
            Create = new(resolver, Cash, Store, new Clock());
            Replace = new(resolver, Cash, Store, Products, Units, new Clock());
            Get = new(resolver, Store, new Clock());
        }
        public Guid TenantId { get; set; }
        public Guid BranchId { get; }
        public Membership Membership { get; }
        public Session Session { get; }
        public AccessReader Access { get; }
        public CashReader Cash { get; } = new();
        public DraftStore Store { get; } = new();
        public BusinessProduct Product { get; }
        public ProductUnit Unit { get; }
        public Products Products { get; }
        public Units Units { get; }
        public CreateSaleDraftHandler Create { get; }
        public ReplaceSaleLinesHandler Replace { get; }
        public GetSaleDraftHandler Get { get; }
        public Task<SaleDraftDetails> NewDraftAsync() => Create.HandleAsync(new(TenantId, BranchId), TestContext.Current.CancellationToken);
        public ReplaceSaleLinesCommand Command(SaleDraftDetails draft, params SaleLineInput[] lines) =>
            new(TenantId, BranchId, draft.SaleId, draft.Version, lines.Length == 0 ? [new(Product.Id, Unit.Id, 1m, PriceKind.Retail)] : lines);
    }
    private sealed class Session : IAuthenticatedMediPosUser { public Guid? UserId { get; set; } }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class AccessReader(OperationalAccessSnapshot snapshot) : IOperationalAccessReader
    {
        public OperationalAccessSnapshot Snapshot { get; set; } = snapshot;
        public Task<OperationalAccessSnapshot> ReadAsync(Guid userId, Guid tenantId, Guid? branchId, CancellationToken cancellationToken) => Task.FromResult(Snapshot);
    }
    private sealed class CashReader : IFindOpenCashSession
    {
        public OpenCashSessionDetails? Session { get; set; }
        public (Guid, Guid, Guid)? Selection { get; set; }
        public Task<OpenCashSessionDetails?> FindAsync(Guid tenantId, Guid branchId, Guid membershipId, CancellationToken cancellationToken)
        { Selection = (tenantId, branchId, membershipId); return Task.FromResult(Session); }
    }
    private sealed class DraftStore : ISaleDraftStore
    {
        public SaleDraftSnapshot? Current { get; set; }
        public int Creates { get; private set; }
        public int Replacements { get; private set; }
        public int Reads { get; set; }
        public Exception? Failure { get; set; }
        public Task<SaleDraftSnapshot?> FindAsync(Guid tenantId, Guid branchId, Guid saleId, CancellationToken cancellationToken)
        { Reads++; return Task.FromResult(Current); }
        public Task<uint> AddAsync(Sale sale, CancellationToken cancellationToken)
        { Creates++; Current = new(sale, 1); return Task.FromResult(1u); }
        public Task<uint> ReplaceLinesAsync(Sale sale, uint expectedVersion, CancellationToken cancellationToken)
        {
            Replacements++;
            if (Failure != null) return Task.FromException<uint>(Failure);
            Current = new(sale, expectedVersion + 1);
            return Task.FromResult(Current.Version);
        }
    }
    private sealed class Products(BusinessProduct product) : IBusinessProductStore
    {
        public int Mutations { get; private set; }
        public Task<BusinessProduct?> FindAsync(Guid tenantId, Guid productId, CancellationToken cancellationToken) => Task.FromResult<BusinessProduct?>(product);
        public Task<bool> InternalCodeExistsAsync(Guid tenantId, string code, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AddAsync(BusinessProduct value, AuditLog audit, CancellationToken cancellationToken) { Mutations++; throw new NotSupportedException(); }
        public Task SaveAsync(BusinessProduct value, AuditLog audit, CancellationToken cancellationToken) { Mutations++; throw new NotSupportedException(); }
    }
    private sealed class Units : IProductUnitStore
    {
        public IReadOnlyList<ProductUnit> Values { get; set; } = [];
        public int Mutations { get; private set; }
        public Task<IReadOnlyList<ProductUnit>> FindAsync(Guid tenantId, Guid productId, CancellationToken cancellationToken) => Task.FromResult(Values);
        public Task ReplaceAsync(Guid tenantId, Guid productId, IReadOnlyList<ProductUnit> units, AuditLog audit, CancellationToken cancellationToken)
        { Mutations++; throw new NotSupportedException(); }
    }
}
