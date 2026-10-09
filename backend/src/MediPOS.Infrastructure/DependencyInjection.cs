using MediPOS.Application.Modules.Branches;
using MediPOS.Application.Modules.Branches.CreateBranch;
using MediPOS.Application.Modules.Branches.CreateLegalEntity;
using MediPOS.Application.Modules.Branches.SetMainHubBranch;
using MediPOS.Application.Modules.Cash;
using MediPOS.Application.Modules.Cash.CloseCashSession;
using MediPOS.Application.Modules.Cash.DispatchCashTransfer;
using MediPOS.Application.Modules.Cash.GetActiveCashSessions;
using MediPOS.Application.Modules.Cash.GetCashSessionReconciliation;
using MediPOS.Application.Modules.Cash.GetCashTransfer;
using MediPOS.Application.Modules.Cash.GetPendingCashTransfers;
using MediPOS.Application.Modules.Cash.OpenCashSession;
using MediPOS.Application.Modules.Cash.ReceiveCashTransfer;
using MediPOS.Application.Modules.Catalog;
using MediPOS.Application.Modules.Catalog.CreateBusinessProductFromGlobal;
using MediPOS.Application.Modules.Catalog.CreateCategory;
using MediPOS.Application.Modules.Catalog.CreateGlobalProduct;
using MediPOS.Application.Modules.Catalog.CreateLocalBusinessProduct;
using MediPOS.Application.Modules.Catalog.ProductImport;
using MediPOS.Application.Modules.Catalog.ReplaceProductUnits;
using MediPOS.Application.Modules.Catalog.SearchProducts;
using MediPOS.Application.Modules.Catalog.SetBusinessProductStatus;
using MediPOS.Application.Modules.Catalog.UpdateBusinessProductPrices;
using MediPOS.Application.Modules.IdentityAccess;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.IdentityAccess.CreateMembership;
using MediPOS.Application.Modules.IdentityAccess.DeactivateMembership;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.IdentityAccess.ReplaceWorkSchedule;
using MediPOS.Application.Modules.IdentityAccess.SetMembershipBranches;
using MediPOS.Application.Modules.IdentityAccess.UpsertGoogleUser;
using MediPOS.Application.Modules.Inventory;
using MediPOS.Application.Modules.Inventory.AdjustStock;
using MediPOS.Application.Modules.Inventory.GetExpiringLots;
using MediPOS.Application.Modules.Inventory.PlanFefoAllocation;
using MediPOS.Application.Modules.Purchasing;
using MediPOS.Application.Modules.Purchasing.ConfirmPurchase;
using MediPOS.Application.Modules.Purchasing.CreatePurchase;
using MediPOS.Application.Modules.Purchasing.CreateSupplier;
using MediPOS.Application.Modules.Purchasing.FindPurchasesByDocumentReference;
using MediPOS.Application.Modules.Purchasing.ReplacePurchaseLines;
using MediPOS.Application.Modules.Reporting.GetOwnerBranchOverview;
using MediPOS.Application.Modules.SalesPos;
using MediPOS.Application.Modules.SalesPos.ConfirmSale;
using MediPOS.Application.Modules.SalesPos.CreateSaleDraft;
using MediPOS.Application.Modules.SalesPos.GetInternalTicket;
using MediPOS.Application.Modules.SalesPos.GetSaleDraft;
using MediPOS.Application.Modules.SalesPos.ReplaceSaleLines;
using MediPOS.Application.Modules.SalesPos.VoidSale;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Application.Modules.TenancyLicensing.CreateTenant;
using MediPOS.Application.Modules.TenancyLicensing.ReactivateLicense;
using MediPOS.Application.Modules.TenancyLicensing.RenewLicense;
using MediPOS.Application.Modules.TenancyLicensing.RequestTenantPurge;
using MediPOS.Application.Modules.TenancyLicensing.SuspendLicense;
using MediPOS.Application.Modules.Transfers;
using MediPOS.Application.Modules.Transfers.ApproveTransfer;
using MediPOS.Application.Modules.Transfers.CancelTransfer;
using MediPOS.Application.Modules.Transfers.DispatchTransfer;
using MediPOS.Application.Modules.Transfers.GetTransfer;
using MediPOS.Application.Modules.Transfers.ReceiveTransfer;
using MediPOS.Application.Modules.Transfers.RequestTransfer;
using MediPOS.Application.Tenancy;
using MediPOS.Infrastructure.Modules.Branches.Persistence;
using MediPOS.Infrastructure.Modules.Cash.Persistence;
using MediPOS.Infrastructure.Modules.Catalog.Persistence;
using MediPOS.Infrastructure.Modules.Catalog.ProductImport;
using MediPOS.Infrastructure.Modules.IdentityAccess.Persistence;
using MediPOS.Infrastructure.Modules.Inventory.Persistence;
using MediPOS.Infrastructure.Modules.Purchasing.Persistence;
using MediPOS.Infrastructure.Modules.Reporting.Persistence;
using MediPOS.Infrastructure.Modules.SalesPos.Persistence;
using MediPOS.Infrastructure.Modules.TenancyLicensing.Persistence;
using MediPOS.Infrastructure.Modules.Transfers.Persistence;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MediPOS.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("MediPosDatabase");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Configure 'ConnectionStrings:MediPosDatabase' using environment variables or User Secrets.");
        }

        services.AddDbContext<MediPosDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<ITenantDataContext, TenantDataContext>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<ITenancyLicensingStore, TenancyLicensingStore>();
        services.AddScoped<CreateTenantHandler>();
        services.AddScoped<RenewLicenseHandler>();
        services.AddScoped<SuspendLicenseHandler>();
        services.AddScoped<ReactivateLicenseHandler>();
        services.AddScoped<RequestTenantPurgeHandler>();
        services.AddScoped<ITenantLicenseProvisioning, TenantLicenseProvisioning>();
        services.AddScoped<IBranchesStore, BranchesStore>();
        services.AddScoped<CreateLegalEntityHandler>();
        services.AddScoped<CreateBranchHandler>();
        services.AddScoped<SetMainHubBranchHandler>();
        services.AddScoped<IIdentityAccessStore, IdentityAccessStore>();
        services.AddScoped<IOperationalAccessReader, OperationalAccessReader>();
        services.AddScoped<IOpenCashSessionWriter, OpenCashSessionWriter>();
        services.AddScoped<IFindOpenCashSession, CashSessionReader>();
        services.AddScoped<IActiveCashSessionsReader, CashSessionReader>();
        services.AddScoped<ICashCloseTransaction, CashCloseTransaction>();
        services.AddScoped<ICashSessionReconciliationReader, CashSessionReconciliationReader>();
        services.AddScoped<ICashTransferReader, CashTransferReader>();
        services.AddScoped<IOwnerBranchOverviewReader, OwnerBranchOverviewReader>();
        services.AddScoped<ICashTransferTransaction, CashTransferTransaction>();
        services.AddScoped<ISaleDraftStore, SaleDraftStore>();
        services.AddScoped<ISaleCheckoutTransaction, SaleCheckoutTransaction>();
        services.AddScoped<ISaleVoidTransaction, SaleVoidTransaction>();
        services.AddScoped<IInternalTicketReader, InternalTicketReader>();
        services.AddScoped<ITransferRequestStore, TransferRequestStore>();
        services.AddScoped<ITransferReader, TransferReader>();
        services.AddScoped<ITransferTransaction, TransferTransaction>();
        services.AddScoped<CreateMembershipHandler>();
        services.AddScoped<SetMembershipBranchesHandler>();
        services.AddScoped<ReplaceWorkScheduleHandler>();
        services.AddScoped<DeactivateMembershipHandler>();
        services.AddScoped<IGlobalCatalogStore, GlobalCatalogStore>();
        services.AddScoped<IBusinessProductStore, BusinessProductStore>();
        services.AddScoped<IProductSearchStore, ProductSearchStore>();
        services.AddScoped<SearchProductsHandler>();
        services.AddScoped<CreateCategoryHandler>();
        services.AddScoped<CreateGlobalProductHandler>();
        services.AddScoped<CreateBusinessProductFromGlobalHandler>();
        services.AddScoped<CreateLocalBusinessProductHandler>();
        services.AddScoped<UpdateBusinessProductPricesHandler>();
        services.AddScoped<SetBusinessProductStatusHandler>();
        services.AddScoped<IProductUnitStore, ProductUnitStore>();
        services.AddScoped<ReplaceProductUnitsHandler>();
        services.AddScoped<IProductImportStore, ProductImportStore>();
        services.AddSingleton<IProductImportWorkbookReader, MediPOS.Infrastructure.Modules.Catalog.ProductImport.ProductImportWorkbook>();
        services.AddSingleton<IProductImportTemplateWriter, MediPOS.Infrastructure.Modules.Catalog.ProductImport.ProductImportWorkbook>();
        services.AddScoped<ImportProductsFromExcelHandler>();
        services.AddScoped<GetImportJobResultHandler>();
        services.AddSingleton<GenerateProductImportTemplateHandler>();

        services.AddScoped<IPurchasingStore, PurchasingStore>();
        services.AddScoped<IPurchaseReceiptWriter, PurchaseReceiptWriter>();
        services.AddScoped<IStockAdjustmentTransaction, StockAdjustmentTransaction>();
        services.AddScoped<IInventoryReadStore, InventoryReadStore>();
        services.AddScoped<AdjustStockHandler>();
        services.AddScoped<PlanFefoAllocationHandler>();
        services.AddScoped<GetExpiringLotsHandler>();
        services.AddScoped<CreateSupplierHandler>();
        services.AddScoped<CreatePurchaseHandler>();
        services.AddScoped<ReplacePurchaseLinesHandler>();
        services.AddScoped<ConfirmPurchaseHandler>();
        services.AddScoped<FindPurchasesByDocumentReferenceHandler>();

        return services;
    }

    // Called by API composition only once real, server-validated OIDC/session adapters exist.
    public static IServiceCollection AddIdentityAuthentication<TGoogleIdentitySource, TAuthenticatedUser>(this IServiceCollection services)
        where TGoogleIdentitySource : class, IVerifiedGoogleIdentitySource
        where TAuthenticatedUser : class, IAuthenticatedMediPosUser
    {
        services.AddScoped<IVerifiedGoogleIdentitySource, TGoogleIdentitySource>();
        services.AddScoped<IAuthenticatedMediPosUser, TAuthenticatedUser>();
        services.AddScoped<UpsertGoogleUserHandler>();
        services.AddScoped<ResolveAccessContextHandler>();
        services.AddScoped<OpenCashSessionHandler>();
        services.AddScoped<GetActiveCashSessionsHandler>();
        services.AddScoped<CloseCashSessionHandler>();
        services.AddScoped<GetCashSessionReconciliationHandler>();
        services.AddScoped<DispatchCashTransferHandler>();
        services.AddScoped<ReceiveCashTransferHandler>();
        services.AddScoped<GetCashTransferHandler>();
        services.AddScoped<GetPendingCashTransfersHandler>();
        services.AddScoped<GetOwnerBranchOverviewHandler>();
        services.AddScoped<CreateSaleDraftHandler>();
        services.AddScoped<ConfirmSaleHandler>();
        services.AddScoped<VoidSaleHandler>();
        services.AddScoped<ReplaceSaleLinesHandler>();
        services.AddScoped<GetSaleDraftHandler>();
        services.AddScoped<GetInternalTicketHandler>();
        services.AddScoped<RequestTransferHandler>();
        services.AddScoped<ApproveTransferHandler>();
        services.AddScoped<CancelTransferHandler>();
        services.AddScoped<DispatchTransferHandler>();
        services.AddScoped<ReceiveTransferHandler>();
        services.AddScoped<GetTransferHandler>();
        return services;
    }
}
