using MediPOS.Application.Modules.Branches;
using MediPOS.Application.Modules.Branches.CreateBranch;
using MediPOS.Application.Modules.Branches.CreateLegalEntity;
using MediPOS.Application.Modules.Branches.SetMainHubBranch;
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
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Application.Modules.TenancyLicensing.CreateTenant;
using MediPOS.Application.Modules.TenancyLicensing.ReactivateLicense;
using MediPOS.Application.Modules.TenancyLicensing.RenewLicense;
using MediPOS.Application.Modules.TenancyLicensing.RequestTenantPurge;
using MediPOS.Application.Modules.TenancyLicensing.SuspendLicense;
using MediPOS.Application.Tenancy;
using MediPOS.Infrastructure.Modules.Branches.Persistence;
using MediPOS.Infrastructure.Modules.Catalog.Persistence;
using MediPOS.Infrastructure.Modules.Catalog.ProductImport;
using MediPOS.Infrastructure.Modules.IdentityAccess.Persistence;
using MediPOS.Infrastructure.Modules.Inventory.Persistence;
using MediPOS.Infrastructure.Modules.Purchasing.Persistence;
using MediPOS.Infrastructure.Modules.TenancyLicensing.Persistence;
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
        return services;
    }
}
