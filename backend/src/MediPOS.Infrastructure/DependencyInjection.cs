using MediPOS.Application.Modules.Branches;
using MediPOS.Application.Modules.Branches.CreateBranch;
using MediPOS.Application.Modules.Branches.CreateLegalEntity;
using MediPOS.Application.Modules.Branches.SetMainHubBranch;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Application.Modules.TenancyLicensing.CreateTenant;
using MediPOS.Application.Modules.TenancyLicensing.ReactivateLicense;
using MediPOS.Application.Modules.TenancyLicensing.RenewLicense;
using MediPOS.Application.Modules.TenancyLicensing.RequestTenantPurge;
using MediPOS.Application.Modules.TenancyLicensing.SuspendLicense;
using MediPOS.Infrastructure.Modules.Branches.Persistence;
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

        return services;
    }
}
