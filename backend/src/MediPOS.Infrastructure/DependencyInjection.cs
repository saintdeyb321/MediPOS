using MediPOS.Application.Modules.Branches;
using MediPOS.Application.Modules.Branches.CreateBranch;
using MediPOS.Application.Modules.Branches.CreateLegalEntity;
using MediPOS.Application.Modules.Branches.SetMainHubBranch;
using MediPOS.Application.Modules.IdentityAccess;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.IdentityAccess.CreateMembership;
using MediPOS.Application.Modules.IdentityAccess.DeactivateMembership;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.IdentityAccess.ReplaceWorkSchedule;
using MediPOS.Application.Modules.IdentityAccess.SetMembershipBranches;
using MediPOS.Application.Modules.IdentityAccess.UpsertGoogleUser;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Application.Modules.TenancyLicensing.CreateTenant;
using MediPOS.Application.Modules.TenancyLicensing.ReactivateLicense;
using MediPOS.Application.Modules.TenancyLicensing.RenewLicense;
using MediPOS.Application.Modules.TenancyLicensing.RequestTenantPurge;
using MediPOS.Application.Modules.TenancyLicensing.SuspendLicense;
using MediPOS.Application.Tenancy;
using MediPOS.Infrastructure.Modules.Branches.Persistence;
using MediPOS.Infrastructure.Modules.IdentityAccess.Persistence;
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
