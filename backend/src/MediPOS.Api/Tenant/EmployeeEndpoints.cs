using System.Text.Json.Serialization;
using MediPOS.Application.Modules.IdentityAccess;
using MediPOS.Application.Modules.IdentityAccess.CreateMembership;
using MediPOS.Application.Modules.IdentityAccess.DeactivateMembership;
using MediPOS.Application.Modules.IdentityAccess.Directory;
using MediPOS.Application.Modules.IdentityAccess.ReplaceWorkSchedule;
using MediPOS.Application.Modules.IdentityAccess.SetMembershipBranches;
using MediPOS.Domain.Modules.IdentityAccess;

namespace MediPOS.Api.Tenant;

public static partial class TenantEndpoints
{
    private static void MapEmployees(RouteGroupBuilder tenants)
    {
        tenants.MapGet("/employees", async (HttpContext c, EmployeeDirectoryQueries queries) => TenantHttp.Response(await queries.HandleAsync(TenantHttp.Tenant(c), TenantHttp.Offset(c), TenantHttp.Limit(c), c.RequestAborted)))
            .Produces<IReadOnlyList<EmployeeSummary>>().WithName("tenant.employees.list").WithQuery<TenantOpenApi.Page>();
        tenants.MapPost("/employees", async (HttpContext c, CreateMembershipHandler handler) =>
        {
            var input = await TenantHttp.Read<EmployeeRequest>(c);
            return TenantHttp.Response(await handler.CreateTenantMembershipAsync(new(TenantHttp.Tenant(c), input.UserId, input.Role, TenantHttp.Actor(c)), c.RequestAborted), 201);
        }).Accepts<EmployeeRequest>("application/json").Produces<MembershipDetails>(201).WithName("tenant.employees.create");
        tenants.MapPost("/employees/{membershipId}/deactivate", async (HttpContext c, DeactivateMembershipHandler handler) =>
        {
            await TenantHttp.Read<EmptyRequest>(c);
            return TenantHttp.Response(await handler.HandleAsync(new(TenantHttp.Tenant(c), TenantHttp.Id(c, "membershipId"), TenantHttp.Actor(c)), c.RequestAborted));
        }).Accepts<EmptyRequest>("application/json").Produces<MembershipDetails>().WithName("tenant.employees.deactivate");
        tenants.MapPut("/employees/{membershipId}/branches", async (HttpContext c, SetMembershipBranchesHandler handler) =>
        {
            var input = await TenantHttp.Read<EmployeeBranchesRequest>(c);
            await handler.HandleAsync(new(TenantHttp.Tenant(c), TenantHttp.Id(c, "membershipId"), input.BranchIds, TenantHttp.Actor(c)), c.RequestAborted);
            return Results.NoContent();
        }).Accepts<EmployeeBranchesRequest>("application/json").Produces(204).WithName("tenant.employees.branches");
        tenants.MapPut("/employees/{membershipId}/schedule", async (HttpContext c, ReplaceWorkScheduleHandler handler) =>
        {
            var input = await TenantHttp.Read<EmployeeScheduleRequest>(c);
            await handler.HandleAsync(new(TenantHttp.Tenant(c), TenantHttp.Id(c, "membershipId"), input.Windows, TenantHttp.Actor(c)), c.RequestAborted);
            return Results.NoContent();
        }).Accepts<EmployeeScheduleRequest>("application/json").Produces(204).WithName("tenant.employees.schedule");
    }
    public sealed record EmployeeRequest([property: JsonRequired] Guid UserId, [property: JsonRequired] TenantRole Role);
    public sealed record EmployeeBranchesRequest([property: JsonRequired] IReadOnlyList<Guid> BranchIds);
    public sealed record EmployeeScheduleRequest([property: JsonRequired] IReadOnlyList<WorkWindow> Windows);
}
