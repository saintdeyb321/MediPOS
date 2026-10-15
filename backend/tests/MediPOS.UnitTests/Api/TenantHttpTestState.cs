using System.Reflection;
using MediPOS.Application.Modules.Branches;
using MediPOS.Application.Modules.Cash;
using MediPOS.Application.Modules.Cash.GetActiveCashSessions;
using MediPOS.Application.Modules.Cash.OpenCashSession;
using MediPOS.Application.Modules.Catalog;
using MediPOS.Application.Modules.Catalog.ProductImport;
using MediPOS.Application.Modules.Catalog.ReplaceProductUnits;
using MediPOS.Application.Modules.Catalog.SearchProducts;
using MediPOS.Application.Modules.Commissions;
using MediPOS.Application.Modules.IdentityAccess;
using MediPOS.Application.Modules.IdentityAccess.Directory;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.IdentityAccess.ReplaceWorkSchedule;
using MediPOS.Application.Modules.Inventory;
using MediPOS.Application.Modules.Purchasing;
using MediPOS.Application.Modules.Purchasing.CreateSupplier;
using MediPOS.Application.Modules.Purchasing.ReplacePurchaseLines;
using MediPOS.Application.Modules.Reporting.ExportOwnerReport;
using MediPOS.Application.Modules.Reporting.GetOwnerBranchOverview;
using MediPOS.Application.Modules.Reporting.Operational;
using MediPOS.Application.Modules.SalesPos;
using MediPOS.Application.Modules.SalesPos.ConfirmSale;
using MediPOS.Application.Modules.SalesPos.VoidSale;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Branches;
using MediPOS.Domain.Modules.Cash;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Catalog.ProductImport;
using MediPOS.Domain.Modules.Commissions;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.Inventory;
using MediPOS.Domain.Modules.Purchasing;
using MediPOS.Domain.Modules.SalesPos;
using MediPOS.Domain.Modules.TenancyLicensing;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.UnitTests.Api;

// Recording persistence for real HTTP/application composition only. Does not prove PostgreSQL ACID/RLS/locks.
public sealed partial class TenantHttpTestState
{
    internal readonly Guid TenantId = Guid.NewGuid();
    internal User User { get; } = PlatformLicenseHttpTests.Operator();
    internal Membership? Member { get; set; }
    internal License License { get; set; }
    internal bool Assigned { get; set; } = true;
    internal bool Scheduled { get; set; } = true;
    internal int Reads { get; private set; }
    internal Action? AfterDirectoryRead { get; set; }
    internal Action? AfterPrivateRead { get; set; }
    internal Action? AfterCheckoutLock { get; set; }
    internal List<LegalEntity> Entities { get; } = [];
    internal List<Branch> Branches { get; } = [];
    internal List<Category> Categories { get; } = [];
    internal List<GlobalProduct> GlobalProducts { get; } = [];
    internal List<BusinessProduct> Products { get; } = [];
    internal List<ProductUnit> Units { get; } = [];
    internal List<Supplier> Suppliers { get; } = [];
    internal List<Purchase> Purchases { get; } = [];
    internal List<PurchaseLine> PurchaseLines { get; } = [];
    internal List<InventoryLot> Lots { get; } = [];
    internal List<StockMovement> Movements { get; } = [];
    internal List<CashSession> Cash { get; } = [];
    internal List<Sale> Sales { get; } = [];
    internal Dictionary<Guid, uint> Versions { get; } = [];
    internal List<SalePayment> Payments { get; } = [];
    internal List<SalePaymentReversal> Reversals { get; } = [];
    internal List<Membership> Employees { get; } = [];
    internal List<MembershipBranch> Assignments { get; } = [];
    internal List<WorkSchedule> Schedules { get; } = [];
    internal List<ImportJob> Imports { get; } = [];
    internal List<ImportRowResult> ImportRows { get; } = [];
    internal List<AuditLog> Audits { get; } = [];
    internal TenantHttpTestState()
    {
        Member = Membership.Create(TenantId, User.Id, TenantRole.Owner, DateTimeOffset.UtcNow);
        License = License.Create(TenantId, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30), 3, LicenseStatus.Active, User.Id, DateTimeOffset.UtcNow);
    }
    internal void Configure(IServiceCollection services)
    {
        Type[] ports = [typeof(IOperationalAccessReader), typeof(IMyTenantDirectoryReader), typeof(IBranchDirectoryReader), typeof(ICatalogDirectoryReader),
            typeof(IGlobalCatalogStore), typeof(IBranchesStore), typeof(ITenantLicenseProvisioning), typeof(IBusinessProductStore), typeof(IProductUnitStore),
            typeof(IPurchasingStore), typeof(IPurchaseReceiptWriter), typeof(IPurchaseDirectoryReader), typeof(IInventoryDirectoryReader), typeof(IProductSearchStore),
            typeof(IOpenCashSessionWriter), typeof(IFindOpenCashSession), typeof(ISaleDraftStore), typeof(ISaleCheckoutTransaction), typeof(IInternalTicketReader),
            typeof(ICashCloseTransaction), typeof(ICashSessionReconciliationReader), typeof(IOwnerBranchOverviewReader), typeof(ISaleVoidTransaction),
            typeof(IIdentityAccessStore), typeof(IEmployeeDirectoryReader), typeof(IProductImportStore), typeof(IStockAdjustmentTransaction), typeof(IActiveCashSessionsReader),
            typeof(IOwnerOperationalDashboardReader), typeof(IOwnerSalesReportReader), typeof(IOwnerInventoryRiskReader), typeof(IOwnerProductRotationReader), typeof(IReportExportReader)];
        foreach (var port in ports)
            services.AddScoped(port, _ => { var proxy = DispatchProxy.Create(port, typeof(Port)); ((Port)proxy).State = this; return proxy; });
        ConfigureTransfers(services);
        ConfigureCashTransfers(services);
        ConfigureCommissions(services);
        ConfigureSync(services);
        ConfigureThresholds(services);
        ConfigureNotifications(services);
        ConfigureAudit(services);
    }
    private object Invoke(Type type, string method, object?[] a)
    {
        Reads++;
        if (type == typeof(MediPOS.Application.Modules.AuditSupport.IAuditHistoryReader)) return ReadAudit(a);
        if (type == typeof(MediPOS.Application.Modules.Inventory.SetBranchProductStockThreshold.IBranchStockThresholdTransaction)) return Task.FromResult<MediPOS.Application.Modules.Inventory.SetBranchProductStockThreshold.IBranchStockThresholdScope>(new ThresholdScope(this));
        if (InvokeTransfers(type, method, a, out var transferResult)) return transferResult!;
        if (InvokeCashTransfers(type, method, a, out var cashTransferResult)) return cashTransferResult!;
        if (InvokeCommissions(type, method, a, out var commissionResult)) return commissionResult!;
        if (InvokeSync(type, method, a, out var syncResult)) return syncResult!;
        if (InvokeNotifications(type, method, a, out var notificationResult)) return notificationResult!;
        if (type == typeof(IOwnerOperationalDashboardReader)) return Task.FromResult(new DashboardMetrics(0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
        if (type == typeof(IOwnerSalesReportReader)) return Task.FromResult(new SalesReportPage([], new(0, 0, 0)));
        if (type == typeof(IOwnerProductRotationReader)) return Task.FromResult(new ProductRotationPage([], new(0, 0)));
        if (type == typeof(IOwnerInventoryRiskReader)) return method switch
        {
            "ReadStockAsync" => Task.FromResult(new StockRiskPage([], new(0, 0))),
            "ReadExpirationsAsync" => Task.FromResult(new ExpirationPage([], new(0, 0, 0, 0))),
            "ReadCapitalAsync" => Task.FromResult(new InventoryCapitalSnapshot([], new(0, 0, 0))),
            _ => throw new NotSupportedException(method)
        };
        if (type == typeof(IReportExportReader)) return Task.FromResult(new ReportExportSnapshot(new CapitalExportData(new([], new(0, 0, 0))), new("Botica Prueba", Branches.ToDictionary(value => value.Id, value => value.Name), new Dictionary<Guid, string>(), null, null, null)));
        if (type == typeof(IProductImportStore))
            return method switch
            {
                "FindLicenseAsync" => Task.FromResult<License?>(License),
                "FindActiveCategoriesAsync" => Task.FromResult<IReadOnlySet<Guid>>(Categories.Select(value => value.Id).ToHashSet()),
                "FindExistingCodesAsync" => Task.FromResult<IReadOnlySet<string>>(Products.Select(value => value.InternalCode).ToHashSet(StringComparer.Ordinal)),
                "StartAsync" => Save(() => Imports.Add((ImportJob)a[0]!)),
                "SaveInvalidRowsAsync" => Save(() => ImportRows.AddRange((IReadOnlyList<ImportRowResult>)a[1]!)),
                "ImportRowAsync" => ImportRowAsync((ImportJob)a[0]!, (int)a[1]!, (BusinessProduct)a[2]!, (ProductUnit)a[3]!, (Func<CancellationToken, Task>)a[4]!, (CancellationToken)a[5]!),
                "CompleteAsync" => CompleteImportAsync((AuditLog)a[1]!, (Func<CancellationToken, Task>)a[2]!, (CancellationToken)a[3]!),
                "FindAsync" => Task.FromResult(Imports.SingleOrDefault(value => value.Id == (Guid)a[1]!) is { } job ? new ImportJobPage(job, ImportRows.Where(value => value.ImportJobId == job.Id && value.RowNumber > (int)a[2]!).Take((int)a[3]!).ToArray(), false) : null),
                _ => throw new NotSupportedException(method)
            };
        if (type == typeof(IStockAdjustmentTransaction))
        {
            var lot = Lots.SingleOrDefault(value => value.Id == (Guid)a[1]!);
            if (method == "FindLotBranchAsync") return Task.FromResult(lot is null ? (Guid?)null : lot.BranchId);
            return Task.FromResult<IStockAdjustmentScope?>(lot is null ? null : new AdjustmentScope(this, lot));
        }
        if (type == typeof(IIdentityAccessStore))
            return method switch
            {
                "UpsertGoogleUserAsync" => Task.FromResult(User),
                "UserExistsAsync" => Task.FromResult(true),
                "HasActiveMembershipAsync" => Task.FromResult(Employees.Any(value => value.UserId == (Guid)a[1]! && value.IsActive) || Member?.UserId == (Guid)a[1]!),
                "CountActiveOwnersAsync" => Task.FromResult((Member?.Role == TenantRole.Owner && Member.IsActive ? 1 : 0) + Employees.Count(value => value.IsActive && value.Role == TenantRole.Owner)),
                "HasOwnerHistoryAsync" => Task.FromResult(Member?.Role == TenantRole.Owner),
                "FindMembershipAsync" => Task.FromResult(Employees.Append(Member!).SingleOrDefault(value => value is not null && value.Id == (Guid)a[1]!)),
                "FindBranchAssignmentsAsync" => Task.FromResult<IReadOnlyList<Guid>>(Assignments.Where(value => value.MembershipId == (Guid)a[1]!).Select(value => value.BranchId).ToArray()),
                "FindWorkScheduleAsync" => Task.FromResult<IReadOnlyList<WorkSchedule>>(Schedules.Where(value => value.MembershipId == (Guid)a[1]!).ToArray()),
                "AddMembershipAsync" => Save(() => { Employees.Add((Membership)a[0]!); Audits.Add((AuditLog)a[1]!); }),
                "SaveDeactivationAsync" => Save(() => Audits.Add((AuditLog)a[1]!)),
                "ReplaceBranchesAsync" => Save(() => { Assignments.RemoveAll(value => value.MembershipId == (Guid)a[1]!); Assignments.AddRange((IReadOnlyList<MembershipBranch>)a[2]!); Audits.Add((AuditLog)a[3]!); }),
                "ReplaceScheduleAsync" => Save(() => { Schedules.RemoveAll(value => value.MembershipId == (Guid)a[1]!); Schedules.AddRange((IReadOnlyList<WorkSchedule>)a[2]!); Audits.Add((AuditLog)a[3]!); }),
                _ => throw new NotSupportedException(method)
            };
        if (type == typeof(IEmployeeDirectoryReader)) return Task.FromResult<IReadOnlyList<EmployeeSummary>>(Employees.Select(value => new EmployeeSummary(value.Id, value.UserId, "Empleado", value.Role, value.IsActive, value.CreatedAt,
            Assignments.Where(b => b.MembershipId == value.Id).Select(b => b.BranchId).ToArray(), Schedules.Where(w => w.MembershipId == value.Id).Select(w => new WorkWindow(w.DayOfWeek, w.StartTime, w.EndTime)).ToArray())).ToArray());
        if (type == typeof(IOperationalAccessReader))
        {
            var user = (Guid)a[0]!; var tenant = (Guid)a[1]!; var branch = (Guid?)a[2];
            var windows = Member is null || !Scheduled ? [] : Enum.GetValues<DayOfWeek>().Select(day => WorkSchedule.Create(TenantId, Member.Id, TenantId, day, TimeOnly.MinValue, new TimeOnly(23, 59, 59, 999, 999))).ToArray();
            return Task.FromResult(new OperationalAccessSnapshot(user == User.Id, tenant == TenantId && user == User.Id ? Member : null, License,
                branch is null || Branches.Any(value => value.Id == branch), Assigned, windows));
        }
        if (type == typeof(IMyTenantDirectoryReader))
        {
            Assert.Equal(User.Id, (Guid)a[0]!);
            return Task.FromResult(new MyTenantPage(Member?.IsActive == true ? [new(TenantId, "Synthetic Botica", Member.Role)] : [], null));
        }
        if (type == typeof(ITenantLicenseProvisioning)) return Task.FromResult<ITenantLicenseProvisioningScope?>(new LicenseScope(this));
        if (type == typeof(IBranchesStore))
            return method switch
            {
                "TenantExistsAsync" => Task.FromResult((Guid)a[0]! == TenantId),
                "FindLegalEntityAsync" => Task.FromResult(Entities.SingleOrDefault(value => value.Id == (Guid)a[1]!)),
                "FindBranchAsync" => Task.FromResult(Branches.SingleOrDefault(value => value.Id == (Guid)a[1]!)),
                "FindMainHubBranchAsync" => Task.FromResult(Branches.SingleOrDefault(value => value.IsMainHub)),
                "CountBranchesAsync" => Task.FromResult(Branches.Count),
                "AddLegalEntityAsync" => Save(() => { Entities.Add((LegalEntity)a[0]!); Audits.Add((AuditLog)a[1]!); }),
                "AddBranchAsync" => Save(() => { Branches.Add((Branch)a[0]!); Audits.Add((AuditLog)a[1]!); }),
                _ => throw new NotSupportedException(method)
            };
        if (type == typeof(IBranchDirectoryReader))
        {
            if (method == "LicenseAsync") return Task.FromResult(new LicenseDetails(TenantId, License.Id, License.Status, License.StartsAt, License.ExpiresAt, License.MaxBranches));
            if (method == "LegalEntitiesAsync") return Task.FromResult<IReadOnlyList<LegalEntityDetails>>(Entities.Select(value => new LegalEntityDetails(value.Id, value.TenantId, value.LegalName, value.Ruc, value.CreatedAt)).ToArray());
            var result = Branches.Where(_ => a[1] is null || Assigned).Select(value => new BranchDetails(value.Id, value.TenantId, value.LegalEntityId, value.Name, value.IsMainHub, value.CreatedAt)).ToArray();
            AfterDirectoryRead?.Invoke(); return Task.FromResult<IReadOnlyList<BranchDetails>>(result);
        }
        if (type == typeof(IGlobalCatalogStore))
            return method switch
            {
                "FindCategoryAsync" => Task.FromResult(Categories.SingleOrDefault(value => value.Id == (Guid)a[0]!)),
                "FindProductAsync" => Task.FromResult(GlobalProducts.SingleOrDefault(value => value.Id == (Guid)a[0]!)),
                "AddCategoryAsync" => Save(() => Categories.Add((Category)a[0]!)),
                _ => throw new NotSupportedException(method)
            };
        if (type == typeof(IBusinessProductStore))
            return method switch
            {
                "FindAsync" => Task.FromResult(Products.SingleOrDefault(value => value.Id == (Guid)a[1]!)),
                "InternalCodeExistsAsync" => Task.FromResult(Products.Any(value => value.InternalCode == (string)a[1]!)),
                "AddAsync" => Save(() => { Products.Add((BusinessProduct)a[0]!); Audits.Add((AuditLog)a[1]!); }),
                "SaveAsync" => Save(() => Audits.Add((AuditLog)a[1]!)),
                _ => throw new NotSupportedException(method)
            };
        if (type == typeof(IProductUnitStore))
        {
            if (method == "FindAsync") return Task.FromResult<IReadOnlyList<ProductUnit>>(Units.Where(value => value.BusinessProductId == (Guid)a[1]!).ToArray());
            return Save(() => { Units.RemoveAll(value => value.BusinessProductId == (Guid)a[1]!); Units.AddRange((IReadOnlyList<ProductUnit>)a[2]!); Audits.Add((AuditLog)a[3]!); });
        }
        if (type == typeof(ICatalogDirectoryReader))
            return method switch
            {
                "CategoriesAsync" => Task.FromResult<IReadOnlyList<CategoryDetails>>(Categories.Select(CategoryDetails.From).ToArray()),
                "GlobalProductsAsync" => Task.FromResult<IReadOnlyList<GlobalProductDetails>>(GlobalProducts.Select(GlobalProductDetails.From).ToArray()),
                "ProductsAsync" => Task.FromResult<IReadOnlyList<BusinessProductDetails>>(Products.Select(BusinessProductDetails.From).ToArray()),
                "ProductAsync" => Task.FromResult(Products.SingleOrDefault(value => value.Id == (Guid)a[1]!) is { } product ? new CatalogProductView(BusinessProductDetails.From(product), Units.Where(value => value.BusinessProductId == product.Id).Select(ProductUnitDetails.From).ToArray()) : null),
                _ => throw new NotSupportedException(method)
            };
        if (type == typeof(IPurchasingStore))
            return method switch
            {
                "FindSupplierAsync" => Task.FromResult(Suppliers.SingleOrDefault(value => value.Id == (Guid)a[1]!)),
                "FindByDocumentReferenceAsync" => Task.FromResult<IReadOnlyList<Purchase>>(Purchases.Where(value => value.DocumentReference == (string)a[1]!).ToArray()),
                "AddSupplierAsync" => Save(() => Suppliers.Add((Supplier)a[0]!)),
                "AddPurchaseAsync" => Save(() => Purchases.Add((Purchase)a[0]!)),
                "FindPurchaseBranchAsync" => Task.FromResult<Guid?>(Purchases.Single(value => value.Id == (Guid)a[1]!).BranchId),
                "LockPurchaseAsync" => Task.FromResult(Purchases.SingleOrDefault(value => value.Id == (Guid)a[1]!)),
                "FindLinesAsync" => Task.FromResult<IReadOnlyList<PurchaseLine>>(PurchaseLines.Where(value => value.PurchaseId == (Guid)a[1]!).ToArray()),
                "ReplaceLinesAsync" => Save(() => { PurchaseLines.RemoveAll(value => value.PurchaseId == ((Purchase)a[0]!).Id); PurchaseLines.AddRange((IReadOnlyList<PurchaseLine>)a[1]!); }),
                "SaveConfirmationAsync" => Save(() => Audits.Add((AuditLog)a[1]!)),
                _ => throw new NotSupportedException(method)
            };
        if (type == typeof(IPurchaseReceiptWriter)) return Save(() => { Lots.AddRange((IReadOnlyList<InventoryLot>)a[0]!); Movements.AddRange((IReadOnlyList<StockMovement>)a[1]!); });
        if (type == typeof(IPurchaseDirectoryReader))
        {
            if (method == "SuppliersAsync") return Task.FromResult<IReadOnlyList<SupplierDetails>>(Suppliers.Select(value => new SupplierDetails(value.Id, value.Name, value.Ruc, value.Contact, value.IsActive, value.CreatedAt)).ToArray());
            var purchase = Purchases.SingleOrDefault(value => value.Id == (Guid)a[2]!);
            return Task.FromResult(purchase is null ? null : new PurchaseView(PurchaseDetails.From(purchase), PurchaseLines.Where(value => value.PurchaseId == purchase.Id).Select(PurchaseLineDetails.From).ToArray()));
        }
        if (type == typeof(IInventoryDirectoryReader))
        {
            if (method == "ExpiringAsync") return Task.FromResult<IReadOnlyList<ExpiringLotSource>>(ExpiringSources.Where(source => source.TenantId == (Guid)a[0]! && source.BranchId == (Guid)a[1]! && (a[2] is null || source.BusinessProductId == (Guid)a[2]!)).Skip((int)a[4]!).Take((int)a[5]!).ToArray());
            if (method == "StockAsync") return Task.FromResult<IReadOnlyList<ProductStockDetails>>(Lots.GroupBy(value => value.BusinessProductId).Select(group => new ProductStockDetails(group.Key, group.Sum(value => value.QuantityAvailableBase))).ToArray());
            return Task.FromResult<IReadOnlyList<LotStockDetails>>(Lots.Select(value => new LotStockDetails(value.Id, value.BusinessProductId, value.BatchNumber, value.ExpirationDate, value.QuantityAvailableBase)).ToArray());
        }
        if (type == typeof(IProductSearchStore))
        {
            if (method == "FindLicenseAsync") return Task.FromResult<License?>(License);
            return Task.FromResult(new ProductSearchSnapshot(Products.Select(value => new ProductSearchCandidate(TenantId, value.Id, value.Name, value.ProductType, value.BrandOrLaboratory,
                value.InternalCode, value.Barcode, value.RetailPrice, value.WholesalePrice, value.IsActive, null, Lots.Where(lot => lot.BusinessProductId == value.Id).Sum(lot => lot.QuantityAvailableBase),
                Units.Where(unit => unit.BusinessProductId == value.Id).Select(ProductUnitDetails.From).ToArray())).ToArray(), [], []));
        }
        if (type == typeof(IOpenCashSessionWriter)) return Save(() => { Cash.Add((CashSession)a[0]!); Audits.Add((AuditLog)a[1]!); });
        if (type == typeof(IActiveCashSessionsReader)) return Task.FromResult<IReadOnlyList<ActiveCashSessionDetails>>(Cash.Where(session => session.Status == CashSessionStatus.Open).Select(session => new ActiveCashSessionDetails(session.Id, session.BranchId, Branches.Single(branch => branch.Id == session.BranchId).Name, session.MembershipId, User.Id, User.DisplayName, session.OpeningAmount, session.OpenedAt, 0m)).ToArray());
        if (type == typeof(IFindOpenCashSession)) return Task.FromResult(Cash.SingleOrDefault(value => value.BranchId == (Guid)a[1]! && value.MembershipId == (Guid)a[2]! && value.Status == CashSessionStatus.Open) is { } session ? OpenCashSessionDetails.From(session) : null);
        if (type == typeof(ISaleDraftStore))
        {
            if (method == "AddAsync") { var sale = (Sale)a[0]!; Sales.Add(sale); Versions[sale.Id] = 1; return Task.FromResult(1u); }
            if (method == "ReplaceLinesAsync") return Task.FromResult(++Versions[((Sale)a[0]!).Id]);
            var match = Sales.SingleOrDefault(value => value.Id == (Guid)a[2]! && value.BranchId == (Guid)a[1]!);
            return Task.FromResult(match is null ? null : new SaleDraftSnapshot(match, Versions[match.Id]));
        }
        if (type == typeof(ISaleCheckoutTransaction)) return Task.FromResult<ISaleCheckoutScope>(new CheckoutScope(this, Sales.Single(value => value.Id == (Guid)a[4]!)));
        if (type == typeof(ISaleVoidTransaction))
        {
            var sale = Sales.SingleOrDefault(value => value.Id == (Guid)a[method == "FindAsync" ? 2 : 3]!);
            if (method == "FindAsync") return Task.FromResult(sale is null ? null : new SaleVoidSnapshot(sale, Versions[sale.Id]));
            return Task.FromResult<ISaleVoidScope>(new VoidScope(this, sale!));
        }
        if (type == typeof(ICashCloseTransaction)) return Task.FromResult<ICashCloseScope>(new CloseScope(this, Cash.Single(value => value.Id == (Guid)a[2]!)));
        if (type == typeof(ICashSessionReconciliationReader))
        {
            if (method == "ReadLedgerAsync") return Task.FromResult(Ledger((CashSession)a[0]!));
            var session = Cash.SingleOrDefault(value => value.Id == (Guid)a[2]!);
            return Task.FromResult(session is null ? null : new CashReconciliationSnapshot(session, Party(session)));
        }
        if (type == typeof(IInternalTicketReader))
        {
            var sale = Sales.SingleOrDefault(value => value.Id == (Guid)a[2]!);
            if (sale is null) return Task.FromResult<InternalTicketSnapshot?>(null);
            var branch = Branches.Single(value => value.Id == sale.BranchId); var entity = Entities.Single(value => value.Id == branch.LegalEntityId);
            return Task.FromResult<InternalTicketSnapshot?>(new(sale, Payments.Where(value => value.SaleId == sale.Id).ToArray(),
                new(TenantId, "Synthetic Botica", entity.Id, entity.LegalName, entity.Ruc, branch.Id, branch.Name, sale.SellerMembershipId, User.Id, User.DisplayName)));
        }
        if (type == typeof(IOwnerBranchOverviewReader)) return Task.FromResult<IReadOnlyList<BranchOverview>>(Branches.Select(branch => new BranchOverview(branch.Id, branch.Name, branch.IsMainHub,
            Sales.Where(sale => sale.BranchId == branch.Id && sale.Status == SaleStatus.Confirmed).Sum(sale => sale.TotalAmount), Sales.LongCount(sale => sale.BranchId == branch.Id && sale.Status == SaleStatus.Confirmed),
            Sales.LongCount(sale => sale.BranchId == branch.Id && sale.Status == SaleStatus.Voided), Cash.LongCount(cash => cash.BranchId == branch.Id && cash.Status == CashSessionStatus.Open),
            Lots.Where(lot => lot.BranchId == branch.Id && lot.QuantityAvailableBase > 0).Select(lot => lot.BusinessProductId).Distinct().LongCount(), 0, 0, 0, 0, 0, 0)).ToArray());
        throw new NotSupportedException(type.Name + "." + method);
    }
    private static Task Save(Action action) { action(); return Task.CompletedTask; }
    private async Task<ImportRowResult> ImportRowAsync(ImportJob job, int rowNumber, BusinessProduct product, ProductUnit unit, Func<CancellationToken, Task> reauthorize, CancellationToken token)
    { await reauthorize(token); var result = ImportRowResult.Imported(job, rowNumber, product); Products.Add(product); Units.Add(unit); ImportRows.Add(result); return result; }
    private async Task CompleteImportAsync(AuditLog audit, Func<CancellationToken, Task> reauthorize, CancellationToken token) { await reauthorize(token); Audits.Add(audit); }
    private CashSessionParty Party(CashSession cash) => new(cash.BranchId, Branches.Single(value => value.Id == cash.BranchId).Name, cash.MembershipId, User.Id, User.DisplayName);
    private CashPaymentLedger Ledger(CashSession cash) => new(Sales.Where(value => value.CashSessionId == cash.Id).ToArray(), Payments.Where(payment => Sales.Any(sale => sale.Id == payment.SaleId && sale.CashSessionId == cash.Id)).ToArray(), Reversals.Where(reversal => Sales.Any(sale => sale.Id == reversal.SaleId && sale.CashSessionId == cash.Id)).ToArray(), CashTransfers.Where(transfer => transfer.SourceCashSessionId == cash.Id || transfer.DestinationCashSessionId == cash.Id).ToArray());
    public class Port : DispatchProxy
    {
        internal TenantHttpTestState State { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var result = State.Invoke(targetMethod!.DeclaringType!, targetMethod.Name, args!);
            if (targetMethod.DeclaringType == typeof(ICatalogDirectoryReader) || targetMethod.DeclaringType == typeof(IBranchDirectoryReader) ||
                targetMethod.DeclaringType == typeof(IInventoryDirectoryReader) || targetMethod.DeclaringType == typeof(IPurchaseDirectoryReader) || targetMethod.DeclaringType == typeof(IEmployeeDirectoryReader))
                State.AfterPrivateRead?.Invoke();
            return result;
        }
    }
    private sealed class LicenseScope(TenantHttpTestState state) : ITenantLicenseProvisioningScope
    {
        public Guid TenantId => state.TenantId;
        public int MaxBranches => state.License.MaxBranches;
        public bool AllowsOperation(DateTimeOffset at) => state.License.AllowsOperation(at);
        public Task CompleteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class CheckoutScope(TenantHttpTestState state, Sale sale) : ISaleCheckoutScope
    {
        public CashSession CashSession => state.Cash.Single(value => value.Id == sale.CashSessionId);
        public Sale Sale => sale;
        public uint Version { get; } = state.Versions[sale.Id];
        public Task<CommissionConfigurationSnapshot> ReadCommissionConfigurationAsync(CancellationToken cancellationToken) => Task.FromResult(new CommissionConfigurationSnapshot(state.CommissionSettings?.IsEnabled == true, state.CommissionRules.Where(rule => rule.IsActive && sale.Lines.Any(line => line.BusinessProductId == rule.BusinessProductId)).ToArray()));
        public Task<IReadOnlyList<InventoryLot>> LockLotsAsync(Guid productId, ProductType productType, decimal requested, DateOnly today, CancellationToken cancellationToken)
        { state.AfterCheckoutLock?.Invoke(); return Task.FromResult<IReadOnlyList<InventoryLot>>(state.Lots.Where(value => value.BusinessProductId == productId).ToArray()); }
        public Task<uint> CompleteAsync(IReadOnlyList<SalePayment> payments, IReadOnlyList<StockMovement> movements, IReadOnlyList<CommissionEntry> commissions, AuditLog audit, CancellationToken cancellationToken)
        {
            state.Payments.AddRange(payments); state.Movements.AddRange(movements); state.Audits.Add(audit);
            state.CommissionEntries.AddRange(commissions);
            foreach (var movement in movements) state.Lots.Single(value => value.Id == movement.InventoryLotId).ApplySale(movement);
            return Task.FromResult(++state.Versions[sale.Id]);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class CloseScope(TenantHttpTestState state, CashSession session) : ICashCloseScope
    {
        public CashSession Session => session;
        public CashSessionParty Party => state.Party(session);
        public Task<CashPaymentLedger> ReadLedgerAsync(CancellationToken cancellationToken) => Task.FromResult(state.Ledger(session));
        public Task CompleteAsync(CashPaymentTotals totals, AuditLog audit, CancellationToken cancellationToken) => Save(() => state.Audits.Add(audit));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class VoidScope(TenantHttpTestState state, Sale sale) : ISaleVoidScope
    {
        public CashSession CashSession => state.Cash.Single(value => value.Id == sale.CashSessionId);
        public Sale Sale => sale;
        public uint Version => state.Versions[sale.Id];
        public Task<SaleVoidEffects> LoadEffectsAsync(CancellationToken cancellationToken) => Task.FromResult(new SaleVoidEffects(state.Payments.Where(value => value.SaleId == sale.Id).ToArray(), state.Movements.Where(value => sale.Lines.Any(line => line.Id == value.SourceSaleLineId)).ToArray()) { Commissions = state.CommissionEntries.Where(entry => entry.SaleId == sale.Id && entry.EntryType == CommissionEntryType.Earned).ToArray() });
        public Task<CashPaymentLedger> ReadCashLedgerAsync(CancellationToken cancellationToken) => Task.FromResult(state.Ledger(CashSession));
        public Task<IReadOnlyList<InventoryLot>> LockLotsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<InventoryLot>>(state.Lots.ToArray());
        public Task<uint> CompleteAsync(IReadOnlyList<SalePaymentReversal> payments, IReadOnlyList<StockMovement> movements, IReadOnlyList<CommissionEntry> commissions, AuditLog audit, CancellationToken cancellationToken)
        {
            foreach (var movement in movements) state.Lots.Single(value => value.Id == movement.InventoryLotId).ApplySaleReversal(movement, state.Movements.Single(value => value.Id == movement.ReversesStockMovementId));
            state.Reversals.AddRange(payments); state.Movements.AddRange(movements); state.CommissionEntries.AddRange(commissions); state.Audits.Add(audit); return Task.FromResult(++state.Versions[sale.Id]);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class AdjustmentScope(TenantHttpTestState state, InventoryLot lot) : IStockAdjustmentScope
    {
        public InventoryLot Lot => lot;
        public bool AllowsOperation(DateTimeOffset at) => state.License.AllowsOperation(at);
        public Task CompleteAsync(StockMovement adjustment, AuditLog audit, CancellationToken cancellationToken) => Save(() => { lot.ApplyAdjustment(adjustment); state.Movements.Add(adjustment); state.Audits.Add(audit); });
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
