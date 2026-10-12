using System.Reflection;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.ElectronicDocuments;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Infrastructure;
using MediPOS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.UnitTests.Api;

public sealed class SecurityCompositionTests
{
    private const string SyntheticConnection = "Host=localhost;Database=synthetic_security_test;Username=synthetic_runtime";

    [Theory]
    [InlineData("Production", false)]
    [InlineData("Staging", false)]
    [InlineData("Development", true)]
    public async Task ActualApiPublishesOnlyHealthAndDevelopmentOpenApi(string environment, bool openApi)
    {
        await using var application = global::Program.CreateApplication(
            ["--environment=" + environment, "--ConnectionStrings:MediPosDatabase=" + SyntheticConnection]);
        var routes = ((IEndpointRouteBuilder)application).DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>().Select(endpoint => endpoint.RoutePattern.RawText!).Order().ToArray();
        Assert.Contains("/health", routes);
        Assert.Equal(openApi ? 2 : 1, routes.Length);
        if (openApi) Assert.Contains(routes, route => route.StartsWith("/openapi/", StringComparison.Ordinal));
        using var scope = application.Services.CreateScope();
        Assert.Null(scope.ServiceProvider.GetService<IAuthenticatedMediPosUser>());
        Assert.Null(scope.ServiceProvider.GetService<IVerifiedGoogleIdentitySource>());
        var provider = scope.ServiceProvider.GetRequiredService<IElectronicDocumentProvider>();
        Assert.False(provider.Capabilities.Operational);
        Assert.False(provider.Capabilities.Simulation);
        Assert.Null(await scope.ServiceProvider.GetRequiredService<IElectronicDeliveryIdentity>().AuthorizeAsync(Guid.NewGuid(), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("CreateTenantHandler", "access.platform_denied")]
    [InlineData("RenewLicenseHandler", "access.platform_denied")]
    [InlineData("SuspendLicenseHandler", "access.platform_denied")]
    [InlineData("ReactivateLicenseHandler", "access.platform_denied")]
    [InlineData("RequestTenantPurgeHandler", "access.platform_denied")]
    [InlineData("CreateCategoryHandler", "access.platform_denied")]
    [InlineData("CreateGlobalProductHandler", "access.platform_denied")]
    [InlineData("CreateLegalEntityHandler", "access.unauthenticated")]
    [InlineData("CreateBranchHandler", "access.unauthenticated")]
    [InlineData("SetMainHubBranchHandler", "access.unauthenticated")]
    [InlineData("CreateMembershipHandler", "access.unauthenticated")]
    [InlineData("SetMembershipBranchesHandler", "access.unauthenticated")]
    [InlineData("ReplaceWorkScheduleHandler", "access.unauthenticated")]
    [InlineData("DeactivateMembershipHandler", "access.unauthenticated")]
    [InlineData("CreateBusinessProductFromGlobalHandler", "access.unauthenticated")]
    [InlineData("CreateLocalBusinessProductHandler", "access.unauthenticated")]
    [InlineData("UpdateBusinessProductPricesHandler", "access.unauthenticated")]
    [InlineData("SetBusinessProductStatusHandler", "access.unauthenticated")]
    [InlineData("ReplaceProductUnitsHandler", "access.unauthenticated")]
    [InlineData("ImportProductsFromExcelHandler", "access.unauthenticated")]
    [InlineData("GetImportJobResultHandler", "access.unauthenticated")]
    [InlineData("CreateSupplierHandler", "access.unauthenticated")]
    [InlineData("CreatePurchaseHandler", "access.unauthenticated")]
    [InlineData("ReplacePurchaseLinesHandler", "access.unauthenticated")]
    [InlineData("ConfirmPurchaseHandler", "access.unauthenticated")]
    [InlineData("FindPurchasesByDocumentReferenceHandler", "access.unauthenticated")]
    [InlineData("AdjustStockHandler", "access.unauthenticated")]
    [InlineData("GetExpiringLotsHandler", "access.unauthenticated")]
    [InlineData("PlanFefoAllocationHandler", "access.unauthenticated")]
    [InlineData("SearchProductsHandler", "access.unauthenticated")]
    public async Task FoundationalUseCasesFailClosedBeforeAccessingPersistence(string handlerName, string code)
    {
        var registration = new ServiceCollection();
        registration.AddInfrastructure(Configuration(SyntheticConnection));
        var assembly = typeof(ITenantOperationAuthorizer).Assembly;
        // Tripwire persistence ports prevent even an accidental network attempt when a guard regresses.
        foreach (var descriptor in registration.ToArray().Where(descriptor => descriptor.ServiceType.IsInterface && descriptor.ServiceType.Assembly == assembly
            && descriptor.ServiceType.Name is not (nameof(ITenantOperationAuthorizer) or nameof(IPlatformAdministration))
            && !descriptor.ServiceType.Name.StartsWith("IElectronic", StringComparison.Ordinal)))
        {
            var port = descriptor.ServiceType;
            registration.AddScoped(port, _ => DispatchProxy.Create(port, typeof(PersistenceTripwire)));
        }
        using var services = registration.BuildServiceProvider();
        using var scope = services.CreateScope();
        var type = assembly.GetTypes().Single(type => type.Name == handlerName);
        var handler = scope.ServiceProvider.GetRequiredService(type);
        var method = type.GetMethod("HandleAsync")!;
        var commandType = method.GetParameters()[0].ParameterType;
        var constructor = commandType.GetConstructors().First();
        var command = constructor.Invoke(constructor.GetParameters().Select(SyntheticArgument).ToArray());
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => (Task)method.Invoke(handler, [command, TestContext.Current.CancellationToken])!);
        Assert.Equal(code, error.Error.Code);
        Assert.Equal(ErrorCategory.Forbidden, error.Error.Category);
    }

    [Fact]
    public void MultiplexingIsRejectedWithoutOpeningAConnection()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddInfrastructure(Configuration(SyntheticConnection + ";Multiplexing=true")));
        Assert.Contains("Multiplexing", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryTenantPrivateModelHasATenantColumnFilterAndTenantSafeForeignKeys()
    {
        using var context = new MediPosDbContext(new DbContextOptionsBuilder<MediPosDbContext>().UseNpgsql().Options, new MediPOS.Application.Tenancy.TenantDataContext());
        var entities = context.Model.GetEntityTypes().Where(entity => !entity.IsOwned() && entity.FindProperty("TenantId") is not null).ToArray();
        Assert.NotEmpty(entities);
        foreach (var entity in entities)
        {
            Assert.NotNull(entity.GetDeclaredQueryFilters().SingleOrDefault());
            Assert.Equal("tenant_id", entity.FindProperty("TenantId")!.GetColumnName());
            foreach (var foreignKey in entity.GetForeignKeys().Where(key => key.PrincipalEntityType.FindProperty("TenantId") is not null))
            {
                Assert.Contains(foreignKey.Properties, property => property.Name == "TenantId");
                Assert.Contains(foreignKey.PrincipalKey.Properties, property => property.Name == "TenantId");
            }
        }
    }

    private static IConfiguration Configuration(string connection) => new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["ConnectionStrings:MediPosDatabase"] = connection }).Build();

    private static object? SyntheticArgument(ParameterInfo parameter)
    {
        var type = parameter.ParameterType;
        if (parameter.HasDefaultValue) return parameter.DefaultValue;
        if (type == typeof(Guid)) return Guid.NewGuid();
        if (type == typeof(string)) return "synthetic";
        if (type == typeof(DateTimeOffset)) return DateTimeOffset.UtcNow;
        if (type == typeof(Stream)) return Stream.Null;
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>)) return Array.CreateInstance(type.GenericTypeArguments[0], 0);
        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }

    public class PersistenceTripwire : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => throw new InvalidOperationException("Persistence was touched before authorization.");
    }
}
