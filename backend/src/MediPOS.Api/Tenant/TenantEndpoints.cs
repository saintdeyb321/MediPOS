using System.Text.Json.Serialization;
using MediPOS.Application.Modules.Branches;
using MediPOS.Application.Modules.Branches.CreateBranch;
using MediPOS.Application.Modules.Branches.CreateLegalEntity;
using MediPOS.Application.Modules.IdentityAccess.Directory;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using Microsoft.AspNetCore.Mvc;

namespace MediPOS.Api.Tenant;

public static partial class TenantEndpoints
{
    public static void MapTenantEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api").RequireAuthorization().AddEndpointFilter<TenantBoundaryFilter>();
        api.MapGet("/me/tenants", async (HttpContext c, ListMyTenantsHandler handler) =>
            TenantHttp.Response(await handler.HandleAsync(TenantHttp.OptionalId(c, "afterTenantId"), TenantHttp.Limit(c), c.RequestAborted)))
            .WithName("tenant.me.tenants").WithQuery<TenantOpenApi.MyTenants>().Produces<MyTenantPage>();
        var tenants = api.MapGroup("/tenants/{tenantId}");
        tenants.MapGet("/access", async (HttpContext c, ResolveAccessContextHandler handler, TimeProvider clock) =>
            TenantHttp.Response(await handler.HandleAsync(TenantHttp.Tenant(c), TenantHttp.OptionalId(c, "branchId"), clock.GetUtcNow(), c.RequestAborted)))
            .WithName("tenant.access").WithQuery<TenantOpenApi.Branch>().Produces<OperationalAccessResult>();
        tenants.MapGet("/branches", async (HttpContext c, BranchDirectoryQueries queries) =>
            TenantHttp.Response(await queries.BranchesAsync(TenantHttp.Tenant(c), TenantHttp.Offset(c), TenantHttp.Limit(c), c.RequestAborted)))
            .WithName("tenant.branches.list").WithQuery<TenantOpenApi.Page>().Produces<IReadOnlyList<BranchDetails>>();
        tenants.MapGet("/legal-entities", async (HttpContext c, BranchDirectoryQueries queries) =>
            TenantHttp.Response(await queries.LegalEntitiesAsync(TenantHttp.Tenant(c), TenantHttp.Offset(c), TenantHttp.Limit(c), c.RequestAborted)))
            .WithName("tenant.legal_entities.list").WithQuery<TenantOpenApi.Page>().Produces<IReadOnlyList<LegalEntityDetails>>();
        tenants.MapGet("/license", async (HttpContext c, BranchDirectoryQueries queries) => TenantHttp.Response(await queries.LicenseAsync(TenantHttp.Tenant(c), c.RequestAborted)))
            .WithName("tenant.license.read").Produces<MediPOS.Application.Modules.TenancyLicensing.LicenseDetails>();
        tenants.MapPost("/legal-entities", async (HttpContext c, CreateLegalEntityHandler handler) =>
        {
            var input = await TenantHttp.Read<LegalEntityRequest>(c);
            return TenantHttp.Response(await handler.HandleAsync(new(TenantHttp.Tenant(c), input.LegalName, input.Ruc, TenantHttp.Actor(c)), c.RequestAborted), 201);
        }).Accepts<LegalEntityRequest>("application/json").Produces<LegalEntityDetails>(201).WithName("tenant.legal_entities.create");
        tenants.MapPost("/branches", async (HttpContext c, CreateBranchHandler handler) =>
        {
            var input = await TenantHttp.Read<BranchRequest>(c);
            return TenantHttp.Response(await handler.HandleAsync(new(TenantHttp.Tenant(c), input.LegalEntityId, input.Name, TenantHttp.Actor(c)), c.RequestAborted), 201);
        }).Accepts<BranchRequest>("application/json").Produces<BranchDetails>(201).WithName("tenant.branches.create");
        tenants.MapGet("/branches/{branchId}/operations/{requestId}", async (HttpContext c, IRecoverableOperations operations) =>
            TenantHttp.Response(await operations.ReadAsync(TenantHttp.Tenant(c), TenantHttp.Branch(c), TenantHttp.Id(c, "requestId"), c.RequestAborted)))
            .Produces<OperationReceipt>().WithName("tenant.operations.read");
        // Every commercial endpoint shares the same real boundary and application authorization.
        MapCatalog(tenants);
        MapPurchasing(tenants);
        MapPos(tenants);
        MapInventory(tenants);
        MapEmployees(tenants);
        MapEmployeeInvitations(api, tenants);
        MapReports(tenants);
        MapImports(tenants);
        MapTransfers(tenants);
        MapCashTransfers(tenants);
        MapCommissions(tenants);
        MapSync(tenants);
        MapNotifications(tenants);
        MapAudit(tenants);
        api.WithMetadata(new ProducesResponseTypeAttribute(typeof(ProblemDetails), 400), new ProducesResponseTypeAttribute(typeof(ProblemDetails), 401), new ProducesResponseTypeAttribute(typeof(ProblemDetails), 403),
            new ProducesResponseTypeAttribute(typeof(ProblemDetails), 404), new ProducesResponseTypeAttribute(typeof(ProblemDetails), 409), new ProducesResponseTypeAttribute(typeof(ProblemDetails), 413),
            new ProducesResponseTypeAttribute(typeof(ProblemDetails), 415), new ProducesResponseTypeAttribute(typeof(ProblemDetails), 429));
    }

    public sealed record LegalEntityRequest([property: JsonRequired] string LegalName, [property: JsonRequired] string Ruc);
    public sealed record BranchRequest([property: JsonRequired] Guid LegalEntityId, [property: JsonRequired] string Name);
}
