using MediPOS.Application.Modules.IdentityAccess;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.IdentityAccess.CreateMembership;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.TenancyLicensing;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.Tests.Fixtures;

// The older persistence suites isolate commercial/database invariants from authorization policy.
// All database work remains real PostgreSQL. SecurityHardeningTests deliberately does not use this setup.
internal static class PersistenceAuthorizationFixtures
{
    internal static IServiceCollection AddPersistenceFixtureAuthorization(this IServiceCollection services)
    {
        services.AddScoped<ITenantOperationAuthorizer, BusinessRuleAuthorizationStub>();
        services.AddScoped<VersionedProductUnitsFixture>();
        services.AddScoped<IPlatformAdministration>(_ => new PlatformAdministrationStub(Guid.NewGuid()));
        services.AddScoped(provider => new CreateMembershipHandler(provider.GetRequiredService<IIdentityAccessStore>(),
            provider.GetRequiredService<ITenantLicenseProvisioning>(), provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ITenantOperationAuthorizer>(), new PlatformAdministrationStub(null)));
        return services;
    }

    internal static IServiceProvider WithPlatformActor(this IServiceProvider provider, Guid actor)
    {
        ((PlatformAdministrationStub)provider.GetRequiredService<IPlatformAdministration>()).Actor = actor;
        return provider;
    }
}
